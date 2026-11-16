using System;
using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Encyclopedia (the "museum" mission) has a short pause menu: Resume, Options, Send debug, Exit. Here Save / Load are added to it.
    /// The buttons are the game's own: a throw-away PauseMenuViewModel runs InitialiseMissionPause and the two buttons (their file pickers,
    /// saving and loading) are taken from its list. A save goes to saves/missions, the folder of the main menu "Scenarios" load dialog.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        static Type _pmvmType; static PropertyInfo _pmvmButtons, _inMuseumProp; static FieldInfo _btnKey;
        static bool _museumBusy;
        ConfigEntry<bool> _soundReload;

        void BindSoundReloadConfig()
        {
            _soundReload = Config.Bind("General", "FixSoundOnReload", true,
                "Loading a saved game from a mission: the game's distance-based sounds keep updating while the camera is gone and every frame throws an error box. Those errors are swallowed.");
        }

        /// <summary>PlaySoundDistanceBased.Update throws NullReferenceException in CameraManager.getCameraUnityPosition while a mission is unloaded for a load (game 0.9.1).</summary>
        void HookSoundReload(Harmony harmony, Assembly asm)
        {
            var t = asm.GetType("SeaPower.PlaySoundDistanceBased");
            var m = t == null ? null : t.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (m == null) { Trace("PlaySoundDistanceBased.Update not found: FixSoundOnReload not available"); return; }
            harmony.Patch(m, finalizer: Hm("SoundReloadFinalizer"));
            Trace("patched PlaySoundDistanceBased.Update (FixSoundOnReload)");
        }

        static Exception SoundReloadFinalizer(Exception __exception)
        {
            var a = Active;
            if (__exception is NullReferenceException && (object)a != null && a._soundReload != null && a._soundReload.Value
                && __exception.StackTrace != null && __exception.StackTrace.Contains("getCameraUnityPosition"))
            { if (_soundSwallowed++ == 0) Trace("sound update without a camera (mission reload): errors swallowed"); return null; }
            return __exception;
        }
        static int _soundSwallowed;

        void HookMuseumSave(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _pmvmType = asm.GetType("SeapowerUI.ViewModels.PauseMenuViewModel") ?? asm.GetType("SeapowerUI.PauseMenuViewModel");
            if (_pmvmType == null) foreach (var t in asm.GetTypes()) if (t.Name == "PauseMenuViewModel") { _pmvmType = t; break; }
            var globals = asm.GetType("SeaPower.Globals") ?? asm.GetType("Globals");
            _inMuseumProp = globals == null ? null : globals.GetProperty("_inMuseum", all);
            var btn = _pmvmType == null ? null : _pmvmType.Assembly.GetType(string.Join(".", "SeapowerUI", "ViewModels", "PauseMenuButton"));
            if (btn == null) foreach (var t in asm.GetTypes()) if (t.Name == "PauseMenuButton") { btn = t; break; }
            _btnKey = btn == null ? null : btn.GetField("contentKey", all);
            _pmvmButtons = _pmvmType == null ? null : _pmvmType.GetProperty("PauseMenuButtons", all);
            MethodInfo init = null;
            if (_pmvmType != null) foreach (var m in _pmvmType.GetMethods(all | BindingFlags.DeclaredOnly))
                if (m.Name == "Initialise" && m.GetParameters().Length == 1) init = m;
            if (init == null || _inMuseumProp == null || _btnKey == null || _pmvmButtons == null
                || _pmvmType.GetMethod("InitialiseMissionPause", all) == null)
            { Trace("Encyclopedia Save/Load not available (pause menu types not found)"); return; }
            harmony.Patch(init, prefix: Hm("MuseumPausePrefix"));
            Trace("patched PauseMenuViewModel.Initialise (Encyclopedia Save/Load)");
        }

        static string BtnKey(object button) { try { return _btnKey.GetValue(button) as string; } catch { return null; } }

        static void MuseumPausePrefix(object __instance, object __0)
        {
            if (_museumBusy || (object)Active == null || !Enabled) return;
            var list = __0 as IList;
            if (list == null) return;
            try
            {
                if (!(_inMuseumProp.GetValue(null, null) is bool) || !(bool)_inMuseumProp.GetValue(null, null)) return;
                foreach (var b in list) { var k = BtnKey(b); if (k == "SaveAs" || k == "Load") return; }
                object save = null, load = null;
                // the hub marks the list it has put its button into (AppDomain data); the throw-away list must not overwrite that mark
                var mark = AppDomain.CurrentDomain.GetData("rgreat.settings.v1.pause");
                _museumBusy = true;
                try
                {
                    var tmp = Activator.CreateInstance(_pmvmType);
                    _pmvmType.GetMethod("InitialiseMissionPause", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(tmp, null);
                    var src = _pmvmButtons.GetValue(tmp, null) as IList;
                    if (src != null) foreach (var b in src) { var k = BtnKey(b); if (k == "SaveAs") save = b; else if (k == "Load") load = b; }
                }
                finally { _museumBusy = false; AppDomain.CurrentDomain.SetData("rgreat.settings.v1.pause", mark); }
                if (save == null || load == null) { Trace("Encyclopedia Save/Load: game buttons not found"); return; }
                int at = list.Count;
                for (int i = 0; i < list.Count; i++) if (BtnKey(list[i]) == "Options") { at = i; break; }
                list.Insert(at, load);
                list.Insert(at, save);
                Trace("Encyclopedia pause menu: Save / Load added");
            }
            catch (Exception e) { Trace("Encyclopedia Save/Load: " + e); }
        }
    }
}
