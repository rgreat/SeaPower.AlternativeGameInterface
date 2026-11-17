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
    /// BepInEx/AlternativeGameInterface_screens (01_Windows.png, 02_Desktop_mode.png ...), then the window is put back as it was. Nothing is applied or changed.
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
            string dir = Path.Combine(Paths.BepInExRootPath, "AlternativeGameInterface_screens");
            int saved = 0;
            Type capT = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
            Type pngT = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            MethodInfo cap = capT == null ? null : capT.GetMethod("CaptureScreenshotAsTexture", Type.EmptyTypes);
            MethodInfo png = pngT == null ? null : pngT.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
            if (cap == null || png == null) { Logger.LogWarning("Screenshots of the tabs: ScreenCapture / ImageConversion not available"); _shotBusy = false; yield break; }
            bool began = false;
            try { Directory.CreateDirectory(dir); foreach (var old in Directory.GetFiles(dir, "*.png")) File.Delete(old); } catch (Exception e) { Logger.LogWarning("Screenshots of the tabs: " + e.Message); }
            try { began = SettingsWindow.ShotBegin(); } catch (Exception e) { Trace("ShotBegin: " + e.Message); }
            if (!began) { Logger.LogWarning("Screenshots of the tabs: the settings window is not open"); _shotBusy = false; yield break; }
            int tabs = SettingsWindow.ShotTabCount(), index = 0;
            for (int t = 0; t < tabs; t++)
            {
                int pages = SettingsWindow.ShotPages(t);
                for (int p = 0; p < pages; p++)
                {
                    try { SettingsWindow.ShotShow(t, p); } catch (Exception e) { Trace("ShotShow tab " + t + " page " + p + ": " + e.Message); continue; }
                    yield return null; yield return null; yield return null;   // the new rows are laid out
                    yield return new WaitForEndOfFrame();
                    bool done = false;
                    for (int attempt = 0; attempt < 8 && !done; attempt++)
                    {
                    if (attempt > 0) { yield return null; yield return new WaitForEndOfFrame(); }   // the capture sometimes returns nothing: again on a later frame
                    Texture2D full = null, crop = null;
                    try
                    {
                        float x, y, w, h;
                        if (!SettingsWindow.ShotRect(out x, out y, out w, out h)) { Trace("settings screenshot: tab " + t + " page " + p + ": no window rect"); break; }   // the whole options window
                        full = cap.Invoke(null, null) as Texture2D;
                        if (full == null) { if (attempt == 7) Trace("settings screenshot: tab " + t + " page " + p + ": no capture"); continue; }
                        int ix = Mathf.Clamp((int)x, 0, full.width - 1), iw = Mathf.Clamp((int)Math.Ceiling(w), 1, full.width - ix);
                        int top = Mathf.Clamp((int)y, 0, full.height - 1), ih = Mathf.Clamp((int)Math.Ceiling(h), 1, full.height - top);
                        int iy = full.height - top - ih;            // the texture's rows start at the bottom
                        crop = new Texture2D(iw, ih, TextureFormat.RGBA32, false);
                        crop.SetPixels(full.GetPixels(ix, iy, iw, ih)); crop.Apply();
                        var bytes = png.Invoke(null, new object[] { crop }) as byte[];
                        index++;
                        string name = index.ToString("00") + "_" + SettingsWindow.ShotName(t, p) + ".png";
                        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
                        File.WriteAllBytes(Path.Combine(dir, name.Replace(' ', '_')), bytes);
                        saved++; done = true;
                        Trace("settings screenshot: " + name + " " + iw + "x" + ih);
                    }
                    catch (Exception e) { Trace("settings screenshot: " + e.Message); }
                    finally { if (full != null) Destroy(full); if (crop != null) Destroy(crop); }
                    }
                }
            }
            try { SettingsWindow.ShotEnd(); } catch (Exception e) { Trace("ShotEnd: " + e.Message); }
            Logger.LogInfo("Screenshots of the settings tabs: " + saved + " files in " + dir);
            _shotBusy = false;
        }
    }
}
