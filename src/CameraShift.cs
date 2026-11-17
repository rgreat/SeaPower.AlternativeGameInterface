using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;
using Time = UnityEngine.Time;
using Mathf = UnityEngine.Mathf;
using Camera = UnityEngine.Camera;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Camera offset (2.3.3), as the Camera Offset mod (bananaduck.cameraoffset) does it, for players without it: the 3D view's picture is shifted
    /// sideways / up and down (a lens shift: the projection matrix' [0,2] / [1,2], so the camera itself does not move and the object stays
    /// selected and followed), in the follow cameras (Follow, Follow behind) while an object is followed, so the object sits off the centre;
    /// glides to a new value; none in binoculars / periscope. One X / Y pair, set on a 2D pad (SettingsWindow.PadRow), ±25 %. Settings: the World View tab (shown when the settings are opened from
    /// the World View window), live while they are changed there, before Apply. Greyed out with a note (and nothing
    /// patched) when the Camera Offset mod is loaded. Offsets in percent of the view (50 % = half the view; the matrix value is twice the share).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _camOn;
        ConfigEntry<float> _camX, _camY;
        const float CamGlide = 0.1f;   // seconds to glide to a new offset (as the Camera Offset mod)
        bool _camAvail = true;
        static bool _camModLoaded;   // the Camera Offset mod is loaded AND ticked in the game's Mods menu: it does the work, ours is greyed out
        const string CamModHarmonyId = "bananaduck.cameraoffset.patch";
        public bool CamModLoaded { get { return _camModLoaded; } }
        public bool CamShiftAvailable { get { return _camAvail; } }

        static float _sx, _sy, _camVx, _camVy; static bool _camApplied;
        static FieldInfo _camF, _camPF; static PropertyInfo _camModeP, _camOpticalP;
        static MethodInfo _rpExists; static PropertyInfo _rpInst, _rpSel;

        void BindCameraShiftConfig()
        {
            RefreshCamMod();
            _camOn = Config.Bind("WorldView", "CameraOffset", false, "World View: the 3D picture is shifted by the offsets below (as the Camera Offset mod; not used when that mod is loaded).");
            _camX = Config.Bind("WorldView", "CameraOffsetFollowX", 0f, new ConfigDescription("Follow cameras: where the followed object is on the screen, sideways from the centre, % of the view.", new AcceptableValueRange<float>(-25f, 25f)));
            _camY = Config.Bind("WorldView", "CameraOffsetFollowY", 0f, new ConfigDescription("Follow cameras: where the followed object is on the screen, up / down from the centre, % of the view.", new AcceptableValueRange<float>(-25f, 25f)));
        }

        /// <summary>The camera offset in the World View window's look menu: the switch and the pad, as on the settings' World View tab; set at once
        /// (saved, live). Greyed out with the note when the Camera Offset mod is loaded.</summary>
        FrameworkElement CameraOffsetMenuRow()
        {
            var dim = new SolidColorBrush(Color.FromRgb(150, 156, 162));
            var box = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0f, 6f, 0f, 0f) };
            box.Children.Add(new Border { Height = 1f, Margin = new Thickness(0f, 0f, 0f, 6f), Background = new SolidColorBrush(Color.FromRgb(70, 76, 86)) });
            box.Children.Add(new TextBlock { Text = Loc.Or("grp.Camera offset", "Camera offset"), FontSize = 11f, Foreground = dim, Margin = new Thickness(4f, 0f, 0f, 2f) });
            var body = new StackPanel { Orientation = Orientation.Vertical };
            var tb = new TextBlock { FontSize = 12f, Foreground = new SolidColorBrush(Color.FromRgb(230, 232, 235)) };
            Action showOn = () => tb.Text = (_camOn.Value ? "☑ " : "☐ ") + Loc.Or("lbl.WorldView/CameraOffset", "Shift the view");
            showOn();
            var on = new Border { Padding = new Thickness(4f, 3f, 4f, 3f), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Child = tb };
            on.MouseEnter += (s, e) => on.Background = new SolidColorBrush(Color.FromRgb(62, 69, 79));
            on.MouseLeave += (s, e) => on.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            on.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; _camOn.Value = !_camOn.Value; SettingsWindow.Forget(_camOn); showOn(); };
            body.Children.Add(on);
            Action refresh;
            var pad = SettingsWindow.Pad(() => _camX.Value, () => _camY.Value, (x, y) =>
            {
                _camX.Value = x; _camY.Value = y;
                SettingsWindow.Forget(_camX); SettingsWindow.Forget(_camY);
                if (!_camOn.Value) { _camOn.Value = true; SettingsWindow.Forget(_camOn); showOn(); }   // moving the dot means: shift
            }, 0.5f, 25f, out refresh);
            pad.Margin = new Thickness(0f, 2f, 0f, 0f);
            body.Children.Add(pad);
            if (CamModWorking())
            {
                body.IsEnabled = false; body.Opacity = 0.45f;
                box.Children.Add(new TextBlock { Text = Loc.Or("cam.conflict", "The Camera Offset mod is loaded."), FontSize = 11f, MaxWidth = 280f, TextWrapping = TextWrapping.Wrap,
                                                 Foreground = new SolidColorBrush(Color.FromRgb(236, 212, 110)), Margin = new Thickness(4f, 0f, 0f, 4f) });
            }
            box.Children.Add(body);
            return box;
        }

        static Assembly CameraOffsetModAssembly()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (asm.GetType("CameraOffset.cameraoffset", false) != null) return asm;
            }
            catch { }
            return null;
        }

        /// <summary>Is the Camera Offset mod working? Told by its own Harmony patch on CameraManager.OnLateUpdate being there: the game / Anchor Chain
        /// decide whether a mod runs (unticked in the Mods menu it stops, also without a restart), so the patch itself is the truth, whatever
        /// took it off. Read at start, after the Mods menu is saved / a load screen, when a settings page or the look menu is built, and once
        /// a second by the camera postfix.</summary>
        static void RefreshCamMod()
        {
            _camModChecked = UnityEngine.Time.unscaledTime;
            bool on = false;
            try
            {
                if (_camLate != null)
                {
                    var info = Harmony.GetPatchInfo(_camLate);
                    if (info != null) foreach (var pt in info.Postfixes) if (pt.owner == CamModHarmonyId) { on = true; break; }
                }
                else on = CameraOffsetModAssembly() != null;   // before our own patch: loaded = working
            }
            catch (Exception e) { Trace("camera offset: Camera Offset mod state: " + e.Message); }
            if (on == _camModLoaded) return;
            _camModLoaded = on;
            if (!on) _camApplied = true;   // its lens shift is reset by our next frame
            Trace(on ? "camera offset: the Camera Offset mod works - its own settings are used, ours greyed out" : "camera offset: the Camera Offset mod does not work (its patch is gone) - ours is used");
        }
        static float _camModChecked = -10f;
        static MethodInfo _camLate;
        public bool CamModWorking() { RefreshCamMod(); return _camModLoaded; }

        void PatchCameraShift(Harmony harmony, Assembly asm)
        {
            try
            {
                var cm = asm.GetType("SeaPower.CameraManager");
                var late = cm == null ? null : cm.GetMethod("OnLateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (late == null) { Trace("camera offset: CameraManager.OnLateUpdate not found"); _camAvail = false; return; }
                _camLate = late;
                _camF = cm.GetField("_camera", BindingFlags.Instance | BindingFlags.NonPublic);
                _camPF = cm.GetField("_particleCamera", BindingFlags.Instance | BindingFlags.NonPublic);
                _camModeP = cm.GetProperty("CurrentMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _camOpticalP = cm.GetProperty("IsOpticalViewActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var rp = asm.GetType("SeaPower.RenderPosition");
                var srp = rp == null ? null : rp.BaseType;   // Singleton<RenderPosition>
                _rpExists = srp == null ? null : srp.GetMethod("InstanceExists", BindingFlags.Static | BindingFlags.Public);
                _rpInst = srp == null ? null : srp.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                _rpSel = rp == null ? null : rp.GetProperty("SelectedObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                harmony.Patch(late, postfix: Hm("CameraShiftPostfix"));
                Trace("patched CameraManager.OnLateUpdate (camera offset)");
                _camModLoaded = false; RefreshCamMod();
            }
            catch (Exception e) { Trace("patch camera offset: " + e.Message); _camAvail = false; }
        }

        static object Live(ConfigEntryBase e) { return SettingsWindow.Shown(e); }   // the settings window's value while it is open: the offset follows the sliders before Apply

        static void CameraShiftPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null || a._camOn == null) return;
            if (UnityEngine.Time.unscaledTime - _camModChecked > 1f) RefreshCamMod();
            if (_camModLoaded) return;   // the Camera Offset mod works: it sets the matrix   // the Camera Offset mod works: it sets the matrix
            try
            {
                float tx = 0f, ty = 0f;
                if ((bool)Live(a._camOn))
                {
                    // the follow cameras only, while an object is followed; none in binoculars / periscope
                    string mode = _camModeP == null ? "" : Convert.ToString(_camModeP.GetValue(__instance, null));
                    bool optical = _camOpticalP != null && (bool)_camOpticalP.GetValue(__instance, null);
                    if ((mode == "Follow" || mode == "FollowBehind") && !optical && HasSelected()) { tx = (float)Live(a._camX) / 50f; ty = (float)Live(a._camY) / 50f; }   // % of the view -> the lens shift (1 = half the view)
                }
                float dt = Time.unscaledDeltaTime;
                _sx = Mathf.SmoothDamp(_sx, tx, ref _camVx, CamGlide, float.PositiveInfinity, dt); _sy = Mathf.SmoothDamp(_sy, ty, ref _camVy, CamGlide, float.PositiveInfinity, dt);
                if (Math.Abs(_sx - tx) < 0.0001f) _sx = tx;
                if (Math.Abs(_sy - ty) < 0.0001f) _sy = ty;
                bool active = _sx != 0f || _sy != 0f;
                if (!active && !_camApplied) return;
                Shift(_camF == null ? null : _camF.GetValue(__instance) as Camera, active);
                Shift(_camPF == null ? null : _camPF.GetValue(__instance) as Camera, active);
                _camApplied = active;
            }
            catch { }
        }

        static bool HasSelected()
        {
            try
            {
                if (_rpExists == null || !(bool)_rpExists.Invoke(null, new object[] { false })) return false;
                var inst = _rpInst.GetValue(null, null);
                var sel = inst == null ? null : _rpSel.GetValue(inst, null) as UnityEngine.Object;
                return sel != null;
            }
            catch { return false; }
        }

        static void Shift(Camera cam, bool active)
        {
            if (cam == null) return;
            cam.ResetProjectionMatrix();
            if (!active) return;
            var p = cam.projectionMatrix;
            p[0, 2] = -p[0, 0] * _sx;
            p[1, 2] = -p[1, 1] * _sy;
            cam.projectionMatrix = p;
        }
    }
}
