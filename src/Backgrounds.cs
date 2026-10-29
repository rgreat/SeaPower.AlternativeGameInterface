using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [General] ModBackgroundsOnly: background pictures of mods replace the game's own.
    /// The game takes its main-menu and loading-screen pictures from every ticked folder at once:
    /// FileManager.GetAllFiles("ui/backgrounds", "*.png") (MenuSystemViewModel.Awake, LoadScreen.Awake) joins the files of
    /// StreamingAssets/original and of every ticked mod (and user), and a random one is shown. With the option, a postfix drops
    /// the files of the game's own folder (FileManager.specialDirectories[1] = original) when any other folder has some.
    /// The loading screen (Singleton LoadScreen, made once at the start, before the mod's hooks) keeps the list it read in its
    /// Awake: a prefix of LoadScreen.ChangeBackground (every loading screen) filters its imagePaths the same way.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _modBackgrounds;
        static string _originalDir;

        void HookBackgrounds(Harmony harmony, Assembly asm)
        {
            var fm = asm.GetType("SeaPower.FileManager");
            // 0.8.6: GetAllFiles(string path, string pattern); 0.8.7 adds a third parameter (LayerKind kinds = All): any overload that takes the path and
            // the pattern first and returns the file list is the one (the callers pass two arguments, the default fills the rest)
            MethodInfo m = null;
            if (fm != null)
                foreach (var c in fm.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (c.Name != "GetAllFiles" || c.ReturnType != typeof(List<string>)) continue;
                    var ps = c.GetParameters();
                    if (ps.Length < 2 || ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string)) continue;
                    if (m == null || ps.Length < m.GetParameters().Length) m = c;
                }
            if (m == null) { Trace("FileManager.GetAllFiles not found: ModBackgroundsOnly does nothing"); return; }
            harmony.Patch(m, postfix: Hm("AllFilesPostfix"));
            Trace("patched FileManager.GetAllFiles (ModBackgroundsOnly)");
            var ls = asm.GetType("SeaPower.LoadScreen");
            var cb = ls == null ? null : ls.GetMethod("ChangeBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _lsImages = ls == null ? null : ls.GetField("imagePaths", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (cb != null && _lsImages != null) { harmony.Patch(cb, prefix: Hm("LoadBackgroundPrefix")); Trace("patched LoadScreen.ChangeBackground (ModBackgroundsOnly)"); }
            else Trace("LoadScreen.ChangeBackground / imagePaths not found: loading screens keep the game's pictures too");
            _fileManagerType = fm;
            _lsType = ls; _lsChange = cb;
        }

        static FieldInfo _lsImages;
        static Type _lsType;
        static MethodInfo _lsChange;
        static bool _rerolled;
        static int _rerollTries;

        /// <summary>
        /// The very first loading screen (game start) is already running when the mod's hooks go in: it has picked one of the game's pictures.
        /// While no load screen has finished yet, as soon as the LoadScreen shows a picture ChangeBackground is run once more (its prefix now
        /// drops the game's pictures), so the first screen shows a mod's picture too.
        /// </summary>
        void RerollFirstBackground()
        {
            if (_rerolled || !BackgroundsOn || _lsType == null || _lsChange == null) return;
            if (LoadScreenDone || LoadScreenCount > 0 || ++_rerollTries > 600) { _rerolled = true; return; }
            try
            {
                var sg = _lsType.Assembly.GetType("SeaPower.Singleton`1");
                var inst = sg == null ? null : sg.MakeGenericType(_lsType).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                var ls = inst == null ? null : inst.GetValue(null, null);
                if (ls == null || Get(ls, "BackgroundImage") == null) return;   // not up yet / not showing a picture
                _rerolled = true;
                if (_originalDir == null) _originalDir = OriginalDir(null);
                var imgs = _lsImages == null ? null : _lsImages.GetValue(ls) as List<string>;
                bool modOnes = false;
                if (imgs != null && _originalDir != null)
                    foreach (var f in imgs) if (!Norm(f).StartsWith(_originalDir, StringComparison.OrdinalIgnoreCase)) { modOnes = true; break; }
                if (!modOnes) { Trace("backgrounds: no pictures in mods, the game's stay"); return; }
                _lsChange.Invoke(ls, null);
                Trace("backgrounds: the first loading screen's picture replaced");
            }
            catch (Exception e) { _rerolled = true; Trace("backgrounds (first load): " + e.Message); }
        }
        static Type _fileManagerType;

        static bool BackgroundsOn { get { var a = Active; return (object)a != null && a._modBackgrounds != null && a._modBackgrounds.Value; } }

        static void AllFilesPostfix(object __instance, string path, List<string> __result)
        {
            if (!BackgroundsOn || path == null) return;
            try
            {
                if (!path.Replace('\\', '/').Trim('/').Equals("ui/backgrounds", StringComparison.OrdinalIgnoreCase)) return;
                if (_originalDir == null) _originalDir = OriginalDir(__instance);
                DropGamePictures(__result, "list");
            }
            catch (Exception e) { Trace("backgrounds: " + e.Message); }
        }

        static void LoadBackgroundPrefix(object __instance)
        {
            if (!BackgroundsOn) return;
            try
            {
                if (_originalDir == null) _originalDir = OriginalDir(null);
                DropGamePictures(_lsImages.GetValue(__instance) as List<string>, "loading screen");
            }
            catch (Exception e) { Trace("backgrounds (loading screen): " + e.Message); }
        }

        /// <summary>The game's own pictures out of the list, when mods have some (the list is changed in place).</summary>
        static void DropGamePictures(List<string> list, string where)
        {
            if (list == null || list.Count == 0 || _originalDir == null) return;
            var own = new List<string>();
            foreach (var f in list) if (Norm(f).StartsWith(_originalDir, StringComparison.OrdinalIgnoreCase)) own.Add(f);
            if (own.Count == 0 || own.Count == list.Count) return;   // no mod pictures (or only mod ones): as the game
            foreach (var f in own) list.Remove(f);
            Trace("backgrounds (" + where + "): " + list.Count + " from mods, " + own.Count + " of the game left out");
        }

        /// <summary>StreamingAssets/original: FileManager.specialDirectories[1] (the FileManager given, or its singleton).</summary>
        static string OriginalDir(object fileManager)
        {
            if (fileManager == null && _fileManagerType != null)
            {
                var sg = _fileManagerType.Assembly.GetType("SeaPower.Singleton`1");
                var inst = sg == null ? null : sg.MakeGenericType(_fileManagerType).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                fileManager = inst == null ? null : inst.GetValue(null, null);
            }
            if (fileManager == null) return null;
            var dirs = fileManager.GetType().GetField("specialDirectories", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(fileManager) as DirectoryInfo[];
            return dirs == null || dirs.Length < 2 ? null : Norm(dirs[1].FullName);
        }

        static string Norm(string p) { return p.Replace('\\', '/').TrimEnd('/') + "/"; }
    }
}
