using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [Map] ClipContactEllipse: the estimate of the position of a contact made of bearings (TMA) is a point and an error ellipse. With a low accuracy a part of the
    /// ellipse lies behind the sensor (on the other side of the line through the sensor perpendicular to the bearing): it looks as if the target may be in the
    /// opposite direction. The ESTIMATE itself is changed (the map takes the icon, the range in the label and the ellipse from it): the ellipse is squeezed along
    /// the bearing (an affine map: again an ellipse) so that its near edge is not nearer than the sensor; its centre, the standard deviations and the axes follow.
    /// Only the copy in the Vehicle (DetectedPosition / ExtrapolatedPosition, written by TransferFromSnapshot / TransferFromComponents from the ECS data each update)
    /// is changed, the ECS data of the tracker are not: nothing accumulates. The sensor and the bearing are those of the latest bearing track of the contact
    /// (Vehicle.TryGetLatestBearingTrack, the one the game uses for its bearing fan); not for an estimate with a fresh precise position track; only the player's
    /// plotting table (IsUiTable).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _clipEllipse;

        sealed class EstSensor
        {
            public bool Ok;
            public object OriginBox;   // boxed GeoPosition of the sensor
            public double Yaw;            // the bearing, the game's (grid) yaw
            public double TrueHead;       // the same, true heading
            public int Tick;
        }

        static readonly ConditionalWeakTable<object, EstSensor> EstSensors = new ConditionalWeakTable<object, EstSensor>();
        static Type _tGeo, _tFan, _tWorld, _tUtils, _tTma;
        static MethodInfo _mBearingTrack, _mPrecise, _mYaw, _mGrid, _mClone, _mIsUi;
        static PropertyInfo _pWorld, _pEm, _pIsUi;
        static FieldInfo _fGeoLat, _fGeoLon, _fDetected, _fExtrap, _fKField;
        static ConstructorInfo _cDouble2;
        static Type _tDouble2;
        static bool _ellipseWarned;
        static int _ellipseLog;
        static double _kCached = double.NaN;

        void BindContactEllipseConfig()
        {
            _clipEllipse = Config.Bind("Map", "ClipContactEllipse", true,
                "Map: the estimate of the position of a contact made of bearings is changed so that its error ellipse does not reach behind the sensor (the opposite side of the bearing): the ellipse is squeezed along the bearing, the icon and the range follow its centre.");
        }

        void HookContactEllipse(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type veh = null;
            foreach (var t in SafeTypes(asm))
            {
                if (t.Name == "Vehicle" && t.GetMethod("TryGetLatestBearingTrack", all) != null) veh = t;
                else if (t.Name == "Utils" && t.GetMethod("UnityYawToTrueHeading", all) != null) _tUtils = t;
                else if (t.Name == "GeoPosition" && t.IsValueType) _tGeo = t;
                else if (t.Name == "TMAContext") _tTma = t;
            }
            if (veh == null || _tUtils == null || _tGeo == null || _tTma == null) { Trace("contact estimate: types not found (ClipContactEllipse not available)"); return; }
            _mBearingTrack = veh.GetMethod("TryGetLatestBearingTrack", all);
            _mPrecise = veh.GetMethod("HasFreshPrecisePositionTrack", all);
            _pIsUi = veh.GetProperty("IsUiTable", all);
            _tFan = veh.GetNestedType("FanInfo", all);
            _mYaw = _tUtils.GetMethod("UnityYawToTrueHeading", all);
            _mGrid = _tUtils.GetMethod("GeoToGridMeters", all);
            _fGeoLat = _tGeo.GetField("_latitude", all); _fGeoLon = _tGeo.GetField("_longitude", all);
            _fDetected = veh.GetField("DetectedPosition", all); _fExtrap = veh.GetField("ExtrapolatedPosition", all);
            _gDetected = FastGet.Field(_fDetected); _gExtrap = FastGet.Field(_fExtrap); _gIsUi = FastGet.Property(_pIsUi);
            _fKField = _tTma.GetField("KField", all);
            _tWorld = Type.GetType("Unity.Entities.World, Unity.Entities");
            _pWorld = _tWorld == null ? null : _tWorld.GetProperty("DefaultGameObjectInjectionWorld", all);
            _pEm = _tWorld == null ? null : _tWorld.GetProperty("EntityManager", all);
            _mClone = typeof(object).GetMethod("MemberwiseClone", all);
            var snap = veh.GetMethod("TransferFromSnapshot", all);
            var comp = veh.GetMethod("TransferFromComponents", all);
            if (_mBearingTrack == null || _tFan == null || _mYaw == null || _mGrid == null || _fGeoLat == null || _pWorld == null || _pEm == null || _fDetected == null || _fExtrap == null
                || _fKField == null || _pIsUi == null || (snap == null && comp == null))
            { Trace("contact estimate: members not found (ClipContactEllipse not available)"); return; }
            if (snap != null) TryPatch(harmony, snap, null, "VehicleTransferPostfix", null);
            if (comp != null) TryPatch(harmony, comp, null, "VehicleTransferPostfix", null);
            Trace("patched Vehicle.TransferFromSnapshot / TransferFromComponents (ClipContactEllipse)");
        }

        static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { var l = new List<Type>(); foreach (var t in e.Types) if (t != null) l.Add(t); return l; }
        }

        static double D(object o, string name) { try { return Convert.ToDouble(FieldV(o, name)); } catch { return double.NaN; } }

        static void SetNum(object boxed, string name, double v)
        {
            var f = boxed.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) f.SetValue(boxed, Convert.ChangeType(v, f.FieldType));
        }

        static bool Why(string why) { if (_ellipseLog < 12) { _ellipseLog++; Trace("contact estimate: not changed - " + why); } return false; }

        /// <summary>TMAContext.KField.Data (the ellipse is KField.Data^0.5 standard deviations); 5.99 (95%, two dimensions) if it cannot be read.</summary>
        static unsafe double KValue()
        {
            if (!double.IsNaN(_kCached)) return _kCached;
            double k = 5.991;
            try
            {
                var sh = _fKField.GetValue(null);
                var ptr = sh.GetType().GetProperty("UnsafeDataPointer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (ptr != null)
                {
                    var o = ptr.GetValue(sh, null);
                    float v = *(float*)System.Reflection.Pointer.Unbox(o);
                    if (v > 0.01f && v < 1000f) k = v;
                    Trace("contact estimate: K = " + k);
                }
                else Trace("contact estimate: KField.UnsafeDataPointer not found, K = " + k);
            }
            catch (Exception e) { Trace("contact estimate: K not read (" + e.Message + "), K = " + k); }
            _kCached = k;
            return k;
        }

        /// <summary>The sensor of the latest bearing track of the contact (looked up 5 times a second at most).</summary>
        static EstSensor Sensor(object veh)
        {
            EstSensor st;
            if (!EstSensors.TryGetValue(veh, out st)) { st = new EstSensor(); EstSensors.Add(veh, st); }
            int now = Environment.TickCount;
            if (st.Tick != 0 && unchecked(now - st.Tick) < 200) return st;
            st.Tick = now == 0 ? 1 : now;
            st.Ok = false;
            var em = _pEm.GetValue(_pWorld.GetValue(null, null), null);
            if (_mPrecise != null && (bool)_mPrecise.Invoke(veh, new object[] { em })) { Why("a fresh precise position track"); return st; }
            var args = new object[] { em, Activator.CreateInstance(_tFan) };
            if (!(bool)_mBearingTrack.Invoke(veh, args)) { Why("no bearing track"); return st; }
            var fan = args[1];
            st.OriginBox = FieldV(fan, "Origin");
            st.Yaw = D(fan, "BearingDeg");
            st.TrueHead = Convert.ToDouble(_mYaw.Invoke(null, new object[] { (float)st.Yaw, st.OriginBox }));
            st.Ok = !double.IsNaN(st.Yaw) && !double.IsNaN(st.TrueHead);
            return st;
        }

        static object Double2(double x, double y)
        {
            if (_cDouble2 == null) return null;
            return _cDouble2.Invoke(new object[] { x, y });
        }

        /// <summary>The estimate (GeoPosition, PositionEigpairs, Entity) squeezed along the bearing so that its error ellipse is not nearer than the sensor; null: nothing to change.</summary>
        static object Squeeze(object est, EstSensor se)
        {
            var center = FieldV(est, "Item1"); var eig = FieldV(est, "Item2");
            if (center == null || eig == null) return null;
            var maj = FieldV(eig, "MajorAxis"); var min = FieldV(eig, "MinorAxis");
            double s1 = D(eig, "MajorStdDev"), s2 = D(eig, "MinorStdDev");
            if (maj == null || min == null || double.IsNaN(s1) || double.IsNaN(s2) || s1 <= 0.0 || s2 < 0.0) return null;
            if (_cDouble2 == null) { _tDouble2 = maj.GetType(); _cDouble2 = _tDouble2.GetConstructor(new[] { typeof(double), typeof(double) }); if (_cDouble2 == null) return null; }
            double mx = D(maj, "x"), my = D(maj, "y"), nx = D(min, "x"), ny = D(min, "y");
            if (double.IsNaN(mx) || double.IsNaN(my) || double.IsNaN(nx) || double.IsNaN(ny)) return null;
            // the sensor from the centre, in the grid metres (as the game does it)
            var gc = _mGrid.Invoke(null, new object[] { center }); var go = _mGrid.Invoke(null, new object[] { se.OriginBox });
            double dx = D(go, "x") - D(gc, "x"), dy = D(go, "y") - D(gc, "y");
            double yr = se.Yaw * Math.PI / 180.0, ux = Math.Sin(yr), uy = Math.Cos(yr);   // the bearing direction (x east, y north of the game's grid)
            double K = KValue(), rk = Math.Sqrt(K);
            // covariance: s1^2 m m' + s2^2 n n'
            double sxx = s1 * s1 * mx * mx + s2 * s2 * nx * nx, syy = s1 * s1 * my * my + s2 * s2 * ny * ny, sxy = s1 * s1 * mx * my + s2 * s2 * nx * ny;
            double h = rk * Math.Sqrt(Math.Max(0.0, sxx * ux * ux + 2.0 * sxy * ux * uy + syy * uy * uy));   // half of the extent of the ellipse along the bearing
            double d0 = -(dx * ux + dy * uy);                                           // the centre ahead of the sensor along the bearing
            if (h < 1.0 || d0 - h >= 0.0) return null;                                  // nothing behind the sensor
            if (d0 + h <= 0.0) { Why("the whole ellipse is behind the sensor"); return null; }   // a stale bearing: left as the game has it
            double k = (d0 + h) / (2.0 * h);                                            // 0 < k < 1
            double shift = (h - d0) / 2.0;                                              // the centre moves forward along the bearing
            double s11 = 1.0 + (k - 1.0) * ux * ux, s12 = (k - 1.0) * ux * uy, s22 = 1.0 + (k - 1.0) * uy * uy;
            double txx = s11 * sxx + s12 * sxy, txy = s11 * sxy + s12 * syy, tyx = s12 * sxx + s22 * sxy, tyy = s12 * sxy + s22 * syy;   // S Sigma
            double qxx = txx * s11 + txy * s12, qxy = txx * s12 + txy * s22, qyy = tyx * s12 + tyy * s22;                                   // (S Sigma) S'
            double mean = (qxx + qyy) / 2.0, diff = (qxx - qyy) / 2.0, rad = Math.Sqrt(diff * diff + qxy * qxy);
            double l1 = mean + rad, l2 = Math.Max(1e-6, mean - rad);
            double th = 0.5 * Math.Atan2(2.0 * qxy, qxx - qyy);
            var newEig = _mClone.Invoke(eig, null);
            SetField(newEig, "MajorAxis", Double2(Math.Cos(th), Math.Sin(th)));
            SetField(newEig, "MinorAxis", Double2(-Math.Sin(th), Math.Cos(th)));
            SetNum(newEig, "MajorStdDev", Math.Sqrt(l1));
            SetNum(newEig, "MinorStdDev", Math.Sqrt(l2));
            // the centre in latitude / longitude: the shift along the true heading of the bearing
            const double M = 111120.0;
            var newCenter = _mClone.Invoke(center, null);
            double cLat = Convert.ToDouble(_fGeoLat.GetValue(center)), cLon = Convert.ToDouble(_fGeoLon.GetValue(center));
            double tr = se.TrueHead * Math.PI / 180.0, cosLat = Math.Max(1e-6, Math.Cos(cLat * Math.PI / 180.0));
            _fGeoLat.SetValue(newCenter, cLat + shift * Math.Cos(tr) / M);
            _fGeoLon.SetValue(newCenter, cLon + shift * Math.Sin(tr) / (M * cosLat));
            var res = _mClone.Invoke(est, null);
            SetField(res, "Item1", newCenter); SetField(res, "Item2", newEig);
            if (_ellipseLog < 12) { _ellipseLog++; Trace("contact estimate: changed, bearing " + se.Yaw.ToString("0.0") + ", std " + s1.ToString("0") + " x " + s2.ToString("0") + " -> " + Math.Sqrt(l1).ToString("0") + " x " + Math.Sqrt(l2).ToString("0") + " m, extent along the bearing " + (2 * h).ToString("0") + " -> " + (d0 + h).ToString("0") + " m, centre moved " + shift.ToString("0") + " m"); }
            return res;
        }

        static void SetField(object o, string name, object v)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) f.SetValue(o, v);
        }

        /// <summary>After the game copied the estimate of the tracker to the contact: the copy is squeezed.</summary>
        static Func<object, object> _gDetected, _gExtrap, _gIsUi;      // the three reads of every transfer as delegates

        static void VehicleTransferPostfix(object __instance)
        {
            long p = Prof.T();
            try { VehicleTransferCore(__instance); } finally { Prof.Lap(Prof.PVeh, p); }
        }

        static void VehicleTransferCore(object __instance)
        {
            var a = Active;
            if ((object)a == null || a._clipEllipse == null || !a._clipEllipse.Value || !MapImp) return;
            try
            {
                var d = _gDetected != null ? _gDetected(__instance) : _fDetected.GetValue(__instance);
                var x = _gExtrap != null ? _gExtrap(__instance) : _fExtrap.GetValue(__instance);
                if (d == null && x == null) return;
                if (!(bool)(_gIsUi != null ? _gIsUi(__instance) : _pIsUi.GetValue(__instance, null))) return;           // the player's table only: the other sides' estimates are theirs
                var se = Sensor(__instance);
                if (!se.Ok) return;
                if (d != null) { var n = Squeeze(d, se); if (n != null) _fDetected.SetValue(__instance, n); }
                if (x != null) { var n = Squeeze(x, se); if (n != null) _fExtrap.SetValue(__instance, n); }
            }
            catch (Exception e) { if (!_ellipseWarned) { _ellipseWarned = true; Trace("contact estimate: " + e); } }
        }
    }
}
