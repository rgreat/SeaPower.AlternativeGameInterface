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
                "Diagnostics: switching it on takes a screenshot of every tab of the settings window (and every page of the Desktop tab) to BepInEx/AlternativeGameInterface_screens, then it switches itself off.");
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
            try { yield return StartCoroutine(Rgreat.Kit.SettingsShots.Run(new SettingsWindow.ShotAdapter(), Path.Combine(Paths.BepInExRootPath, "AlternativeGameInterface_screens"), s => Trace(s), s => Logger.LogWarning(s))); }
            finally { _shotBusy = false; }
        }
    }
}
