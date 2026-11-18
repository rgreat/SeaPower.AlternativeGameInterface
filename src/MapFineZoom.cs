using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [General] MapFineZoomShift: with Shift held the mouse wheel zooms the map in steps 5 times smaller.
    /// The game zooms in MapDragger.MapZoom (the wheel on the Thumb over the map, CommonMap.xaml): ZoomMap(mouse, ZoomLevel + delta / 255), and with Shift it takes delta * -3
    /// (its own handling of the Shift wheel). Here, with Shift: the direction is the game's (the sign of delta * -3), the size is a fifth of the last wheel step without Shift
    /// (|delta| of a notch, 120 until one is seen), so the result does not depend on what the system does to the wheel with Shift.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _fineZoom;
        static PropertyInfo _fzMap, _fzZoom; static MethodInfo _fzZoomTo; static float _fzNotch = 120f; static float _fzDbgAt;

        void BindFineZoomConfig()
        {
            _fineZoom = Config.Bind("General", "MapFineZoomShift", true, "Map: with Shift held the mouse wheel zooms the map in steps 5 times smaller.");
        }

        void HookFineZoom(Harmony harmony, Assembly asm)
        {
            Type t = null; foreach (var x in SafeTypes(asm)) if (x.Name == "MapDragger") { t = x; break; }
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var m = t == null ? null : t.GetMethod("MapZoom", f);
            _fzMap = t == null ? null : t.GetProperty("DraggedMap", f);
            var mt = _fzMap == null ? null : _fzMap.PropertyType;
            _fzZoom = mt == null ? null : mt.GetProperty("ZoomLevel", f);
            _fzZoomTo = mt == null ? null : mt.GetMethod("ZoomMap", f, null, new[] { typeof(Noesis.Point), typeof(float) }, null);
            if (m == null || _fzMap == null || _fzZoom == null || _fzZoomTo == null) { Trace("fine zoom: MapDragger.MapZoom / MapBase.ZoomMap not found (not available)"); return; }
            TryPatch(harmony, m, "FineZoomPrefix", null, null);
        }

        static bool FineZoomPrefix(object __instance, Noesis.MouseWheelEventArgs args)
        {
            try
            {
                var a = Active; if ((object)a == null || a._fineZoom == null || !a._fineZoom.Value || args == null) return true;
                bool shift = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
                if (!shift) { if (args.Delta != 0) _fzNotch = Math.Abs(args.Delta); return true; }        // the game's own step; its size is remembered
                if (args.Delta == 0) return true;
                var map = _fzMap.GetValue(__instance, null) as Noesis.UIElement; if (map == null) return true;
                float dir = Math.Sign(args.Delta * -3f);                                     // the game's direction with Shift
                float step = dir * _fzNotch / 255f / 5f;
                float z = (float)_fzZoom.GetValue(map, null);
                _fzZoomTo.Invoke(map, new object[] { args.GetPosition(map), z + step });
                if (_debug && UnityEngine.Time.unscaledTime - _fzDbgAt > 1f) { _fzDbgAt = UnityEngine.Time.unscaledTime; Trace("fine zoom: delta " + args.Delta + " with Shift, notch " + _fzNotch + ", zoom " + z.ToString("0.000") + " + " + step.ToString("0.0000") + " (the game: " + (args.Delta * -3f / 255f).ToString("0.000") + ")"); }
                args.Handled = true;
                return false;
            }
            catch (Exception e) { Trace("fine zoom: " + e.Message); return true; }
        }
    }
}
