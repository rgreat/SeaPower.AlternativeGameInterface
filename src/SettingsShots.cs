using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Debug (2.3.1): "Screenshot all tabs now" in the Debug tab of the settings window. The window goes through every tab (all of them, as with "Show all settings",
    /// and every page of the Desktop tab), a screenshot of the options window alone is taken at the end of a frame and written as a PNG to
    /// BepInEx/AlternativeGameInterface_screens (01_Windows.png, 02_Desktop_mode.png ...), the window is fitted to every tab first (the smallest size in which all its options are seen), then put back as it was. Nothing is applied or changed.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _shotTabs;
        static bool _shotBusy;

        void BindSettingsShotsConfig()
        {
            _shotTabs = Config.Bind("Debug", "ScreenshotTabs", false,
                "Diagnostics: switching it on takes a screenshot of every tab (and every page) of this settings window, then the same for the windows of the other installed rgreat mods, to BepInEx/<Mod>_screens, then it switches itself off.");
        }

        void SettingsShotsStep()
        {
            if (_shotTabs == null || !_shotTabs.Value) return;
            _shotTabs.Value = false;
            if (!_shotBusy) StartCoroutine(SettingsShotsRoutine());
        }

        IEnumerator SettingsShotsRoutine()
        {
            _shotBusy = true;
            string cur = "alternative-game-interface";
            try
            {
                // the window of this mod first, then the window of every other installed mod in turn (each one takes the place of the last), then back
                yield return StartCoroutine(SettingsWindow.ShotsRun());
                var others = new System.Collections.Generic.List<object[]>();
                foreach (var m in Rgreat.Hub.SettingsHub.ActiveMods()) if (Rgreat.Hub.SettingsHub.IdOf(m) != cur && Rgreat.Hub.SettingsHub.ShotsOf(m) != null) others.Add(m);
                foreach (var m in others)
                {
                    string id = Rgreat.Hub.SettingsHub.IdOf(m);
                    Rgreat.Hub.SettingsHub.SwitchTo(cur, id, SettingsWindow.ShotAnchor, null);
                    cur = id;
                    for (int i = 0; i < 90; i++) yield return null;   // the window of the other mod is built and laid out
                    IEnumerator run = null;
                    try { run = Rgreat.Hub.SettingsHub.ShotsOf(m)(); } catch (Exception e) { Trace("screens of " + id + ": " + e.Message); }
                    if (run != null) yield return StartCoroutine(run);
                }
                if (cur != "alternative-game-interface")
                {
                    Rgreat.Hub.SettingsHub.SwitchTo(cur, "alternative-game-interface", SettingsWindow.ShotAnchor, null);
                    Logger.LogInfo("Screenshots of the tabs of " + (others.Count + 1) + " mods are in BepInEx/<mod>_screens.");
                }
            }
            finally { _shotBusy = false; }
        }
    }
}
