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
    /// [Map] FormationUpdateByLeader (on by default): the map position of the aircraft and helicopters of one formation is updated together, when the leader's is.
    /// The game takes a position of a unit for the map only with the chance 1/15 at every change (MapUnitViewModel constructor: random.NextDouble() &gt; 1/15 returns),
    /// so the units of a formation move at random moments. Here the setter of MapUnitViewModel.Location is patched: a member of the formation (aircraft / helicopters only) does not take its own position while the leader has taken one in the last 3 s; when the leader takes a position
    /// (the postfix), the same moment all the members take their current positions. A first position of a unit is always taken. No leader update for
    /// 3 s (a leader that is dormant, destroyed, not in the registry): the members update themselves as the game does.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _lead;
        sealed class LeadState { public bool Seen; public object Bo; }
        sealed class LeadTime { public float At; }
        static readonly ConditionalWeakTable<object, LeadState> LeadVms = new ConditionalWeakTable<object, LeadState>();      // map view model -> state
        static readonly ConditionalWeakTable<object, List<WeakReference>> LeadByBo = new ConditionalWeakTable<object, List<WeakReference>>();          // game object -> its map view models (a unit can have more than one: every map has its own)
        static readonly ConditionalWeakTable<object, LeadTime> LeadTimes = new ConditionalWeakTable<object, LeadTime>();      // formation -> time of the leader's last position
        static MethodInfo _leadSet, _leadBearSet, _leadBearCalc; static bool _leadBusy, _leadFailed; static int _leadBlocked, _leadSent, _leadNoVm; static float _leadDbgAt;

        void BindLeaderUpdateConfig()
        {
            _lead = Config.Bind("Map", "FormationUpdateByLeader", true, "Map: the aircraft and helicopters of one formation take their new map positions together, at the moments the leader takes its own (the game takes them at random moments, a unit at a time).");
        }

        void HookLeaderUpdate(Harmony harmony, Assembly asm)
        {
            var t = asm.GetType("SeapowerUI.MapUnitViewModel");
            var p = t == null ? null : t.GetProperty("Location", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _leadSet = p == null ? null : p.GetSetMethod(true);
            if (_leadSet == null) { Trace("formation update by leader: MapUnitViewModel.Location setter not found (not available)"); return; }
            TryPatch(harmony, _leadSet, "LeadPrefix", "LeadPostfix", null);
            var bp = t.GetProperty("VelocityBearing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _leadBearSet = bp == null ? null : bp.GetSetMethod(true);
            _leadBearCalc = t.GetMethod("SpeedVectorBearing", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (_leadBearSet != null) TryPatch(harmony, _leadBearSet, "LeadBearingPrefix", null, null);           // the speed vector of a member moves with its position
            if (_leadBearSet == null || _leadBearCalc == null) Trace("formation update by leader: VelocityBearing / SpeedVectorBearing not found (the speed vector of a member follows the game)");
        }

        static bool LeadOn() { var a = Active; return (object)a != null && a._lead != null && a._lead.Value && MapImp && !_leadFailed; }

        /// <summary>The state of the unit: registered at its first position (always taken). Then: air unit in a formation → formation, game object of the leader; leader = this unit?</summary>
        static bool LeadInfo(object vm, out LeadState st, out object f, out object bo, out object lbo)
        {
            f = bo = lbo = null;
            if (!LeadVms.TryGetValue(vm, out st))
            {
                st = new LeadState(); LeadVms.Add(vm, st);
                var u = FieldV(vm, "Unit"); st.Bo = FieldV(u, "BaseObject");
                if (st.Bo != null)
                {
                    List<WeakReference> l; if (!LeadByBo.TryGetValue(st.Bo, out l)) { l = new List<WeakReference>(); LeadByBo.Add(st.Bo, l); }
                    l.RemoveAll(w => !w.IsAlive); l.Add(new WeakReference(vm));
                }
                return false;                                                           // the first position of a unit is always taken
            }
            st.Seen = true; bo = st.Bo; if (bo == null) return false;
            var vt = FieldV(vm, "VisualType"); int vi = vt == null ? 0 : Convert.ToInt32(vt);
            if (vi != 1 && vi != 2) return false;                                      // MapVisualType: 1 Air, 2 Helicopter
            f = FieldV(bo, "Formation"); if (f == null) return false;
            lbo = FieldV(FieldV(f, "LeaderStation"), "UnitObject");
            return lbo != null;
        }

        /// <summary>The first live map view model of a game object, or null.</summary>
        static object LeadAny(object bo)
        {
            List<WeakReference> l; if (!LeadByBo.TryGetValue(bo, out l)) return null;
            for (int i = 0; i < l.Count; i++) { var o = l[i].Target; if (o != null) return o; }
            return null;
        }
        static object LeadPos(object vm) { return FieldV(FieldV(FieldV(vm, "Unit"), "Position"), "Value"); }

        /// <summary>The leader moves this member now: an aircraft / helicopter of a formation, not the leader, and the leader took a position in the last 3 s (else the unit updates itself: dormant leader, leader change).</summary>
        static bool LeadHeld(object vm)
        {
            LeadState st; object f, bo, lbo;
            if (!LeadInfo(vm, out st, out f, out bo, out lbo) || ReferenceEquals(lbo, bo)) return false;
            if (LeadAny(lbo) == null) return false;
            LeadTime lt; return LeadTimes.TryGetValue(f, out lt) && UnityEngine.Time.unscaledTime - lt.At <= 3f;
        }

        static bool LeadPrefix(object __instance)
        {
            if (_leadBusy || !LeadOn()) return true;
            try { if (!LeadHeld(__instance)) return true; _leadBlocked++; return false; }
            catch (Exception e) { _leadFailed = true; Trace("formation update by leader: " + e.Message + " (switched off until the game is restarted)"); return true; }
        }

        static bool LeadBearingPrefix(object __instance)
        {
            if (_leadBusy || !LeadOn() || _leadBearCalc == null) return true;
            try { return !LeadHeld(__instance); }
            catch { return true; }
        }

        static void LeadPostfix(object __instance)
        {
            if (_leadBusy || !LeadOn()) return;
            try
            {
                LeadState st; object f, bo, lbo;
                if (!LeadInfo(__instance, out st, out f, out bo, out lbo) || !ReferenceEquals(lbo, bo)) return;        // only the leader
                LeadTime lt; if (!LeadTimes.TryGetValue(f, out lt)) { lt = new LeadTime(); LeadTimes.Add(f, lt); }
                lt.At = UnityEngine.Time.unscaledTime;
                var sts = FieldV(f, "Stations") as IList; if (sts == null) return;
                _leadBusy = true;
                try
                {
                    for (int i = 0; i < sts.Count; i++)
                    {
                        var ubo = FieldV(sts[i], "UnitObject"); if (ubo == null || ReferenceEquals(ubo, bo)) continue;
                        List<WeakReference> ml; if (!LeadByBo.TryGetValue(ubo, out ml)) { _leadNoVm++; continue; }
                        for (int k = 0; k < ml.Count; k++)                              // every map view model of the unit
                        {
                            var mvm = ml[k].Target; if (mvm == null) continue;
                            LeadState ms; if (!LeadVms.TryGetValue(mvm, out ms) || !ms.Seen) continue;
                            var vt = FieldV(mvm, "VisualType"); int vi = vt == null ? 0 : Convert.ToInt32(vt); if (vi != 1 && vi != 2) continue;
                            var mp = LeadPos(mvm); if (mp == null) continue;
                            _leadSet.Invoke(mvm, new object[] { mp }); _leadSent++;
                            if (_leadBearSet != null && _leadBearCalc != null)
                            {
                                var vel = FieldV(FieldV(mvm, "Unit"), "UnityVelocityVector");
                                if (vel != null) _leadBearSet.Invoke(mvm, new[] { _leadBearCalc.Invoke(null, new[] { vel, mp }) });
                            }
                        }
                    }
                }
                finally { _leadBusy = false; }
                if (_debug && UnityEngine.Time.unscaledTime - _leadDbgAt > 2f) { _leadDbgAt = UnityEngine.Time.unscaledTime; Trace("formation update by leader: members blocked " + _leadBlocked + ", positions given " + _leadSent + ", members without a view model " + _leadNoVm + ", stations " + sts.Count); }
            }
            catch (Exception e) { _leadFailed = true; Trace("formation update by leader: " + e.Message + " (switched off until the game is restarted)"); }
        }
    }
}
