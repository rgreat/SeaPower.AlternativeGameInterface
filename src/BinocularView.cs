using System;
using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Binoculars at the maximum magnification (the last optical profile, e.g. 20x120 STAB) start with the game's post-process filter switched on
    /// (night / thermal look). With [Camera] BinocularsNormalView the filter starts off, i.e. the normal image; the game's own filter key (the toggle key)
    /// switches it on again. [Camera] BinocularsNormalViewDayOnly: only while the sun is up, at night the game's default stays.
    /// A postfix of CameraManager.ApplyOpticalPostProcessProfile (moved here from Alternative Special Effects, where it was [Optics] BinocularsNormalView / NormalViewDayOnly).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _binoNormal, _binoNormalDay;

        void BindBinocularViewConfig()
        {
            _binoNormal = Config.Bind("Camera", "BinocularsNormalView", true,
                "Binoculars at the maximum magnification start with the normal view (the game starts them with its night / thermal filter on). The game's filter key switches the filter on.");
            _binoNormalDay = Config.Bind("Camera", "BinocularsNormalViewDayOnly", true,
                "The normal view is the default only by day; at night the game's own default filter stays.");
        }

        void HookBinocularView(Harmony harmony, Assembly asm)
        {
            var cm = asm.GetType("SeaPower.CameraManager");
            var m = cm == null ? null : cm.GetMethod("ApplyOpticalPostProcessProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Trace("CameraManager.ApplyOpticalPostProcessProfile not found: no normal binocular view option"); return; }
            TryPatch(harmony, m, null, "BinoViewPost", null);
            _bvAsm = asm;
            Trace("patched CameraManager.ApplyOpticalPostProcessProfile (BinocularsNormalView)");
        }

        static Assembly _bvAsm;
        const BindingFlags BvAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static object BvF(object o, string n) { var f = o == null ? null : AccessTools.Field(o.GetType(), n); return f == null ? null : f.GetValue(o); }
        static object BvP(object o, string n) { var p = o == null ? null : AccessTools.Property(o.GetType(), n); return p == null ? null : p.GetValue(o, null); }
        static void BvSetF(object o, string n, object v) { var f = o == null ? null : AccessTools.Field(o.GetType(), n); if (f != null) f.SetValue(o, v); }

        static void BinoViewPost(object __instance, object profile)
        {
            try
            {
                var a = Active;
                if ((object)a == null || a._binoNormal == null || !a._binoNormal.Value || profile == null) return;
                var mode = BvP(__instance, "CurrentOpticalViewMode");
                if (mode == null || mode.ToString() != "Binoculars") return;
                var list = BvF(__instance, "_availableOpticalViewProfiles") as IList;
                if (list == null || list.Count == 0 || !ReferenceEquals(list[list.Count - 1], profile)) return;   // the maximum magnification only
                if (a._binoNormalDay != null && a._binoNormalDay.Value && !SunIsUp()) { Trace("binoculars: night, the game's default filter stays"); return; }
                var pp = BvF(__instance, "_opticalPostProcess");
                var mats = BvF(pp, "fullscreenMaterials") as Array;
                if (pp == null || mats == null || mats.Length == 0) return;
                BvSetF(pp, "effectEnabled", false);
                BvSetF(pp, "toggleKey", BvF(pp, "originalToggleKey"));   // the game's key switches the filter on again
                Trace("binoculars " + BvP(profile, "Id") + ": the filter is off, normal view");
            }
            catch (Exception e) { Trace("binoculars: " + e.Message); }
        }

        static bool SunIsUp()
        {
            try
            {
                var ds = _bvAsm == null ? null : _bvAsm.GetType("SeaPower.DynamicSky");
                var sg = ds == null ? null : _bvAsm.GetType("SeaPower.Singleton`1").MakeGenericType(ds);
                var inst = sg == null ? null : sg.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
                if (inst == null) return true;
                var m = inst.GetType().GetMethod("getSunlightIntensity", BvAll, null, Type.EmptyTypes, null);
                return m == null || Convert.ToSingle(m.Invoke(inst, null)) >= 0.5f;
            }
            catch { return true; }
        }
    }
}
