using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [General] Backgrounds (Mods / Mixed / Game) and RealPhotos: which background pictures the menu and the loading screens use; Mods: the mods' pictures replace the game's own.
    /// The game takes its main-menu and loading-screen pictures from every ticked folder at once:
    /// FileManager.GetAllFiles("ui/backgrounds", "*.png") (MenuSystemViewModel.Awake, LoadScreen.Awake) joins the files of
    /// StreamingAssets/original and of every ticked mod (and user), and a random one is shown. With the option, a postfix drops
    /// the files of the game's own folder (FileManager.specialDirectories[1] = original) when any other folder has some.
    /// The loading screen (Singleton LoadScreen, made once at the start, before the mod's hooks) keeps the list it read in its
    /// Awake: a prefix of LoadScreen.ChangeBackground (every loading screen) filters its imagePaths the same way.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<string> _modBackgrounds;   // Mods / Mixed / Game
        ConfigEntry<string> _menuBg;           // Video / Slideshow: the main menu's background
        ConfigEntry<float> _slideSec;          // the slideshow's picture time
        ConfigEntry<bool> _realPhotos;         // this mod's own pictures (real photos) among the backgrounds
        static string _originalDir, _ownDir;

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
            if (m == null) { Trace("FileManager.GetAllFiles not found: Backgrounds does nothing"); return; }
            harmony.Patch(m, postfix: Hm("AllFilesPostfix"));
            Trace("patched FileManager.GetAllFiles (Backgrounds)");
            var ls = asm.GetType("SeaPower.LoadScreen");
            var cb = ls == null ? null : ls.GetMethod("ChangeBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _lsImages = ls == null ? null : ls.GetField("imagePaths", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (cb != null && _lsImages != null) { harmony.Patch(cb, prefix: Hm("LoadBackgroundPrefix")); Trace("patched LoadScreen.ChangeBackground (Backgrounds)"); }
            else Trace("LoadScreen.ChangeBackground / imagePaths not found: loading screens keep the game's pictures too");
            _fileManagerType = fm;
            _lsType = ls; _lsChange = cb;
            HookMenuBackground(harmony, asm);
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
                if (imgs == null || _originalDir == null || FilterBackgrounds(imgs).Count == imgs.Count) { Trace("backgrounds: nothing to replace in the first loading screen"); return; }
                _lsChange.Invoke(ls, null);
                Trace("backgrounds: the first loading screen's picture replaced");
            }
            catch (Exception e) { _rerolled = true; Trace("backgrounds (first load): " + e.Message); }
        }
        static Type _fileManagerType;

        /// <summary>0: mixed (as the game), 1: the mods' pictures only, 2: the game's own only.</summary>
        static int BackgroundMode
        {
            get
            {
                var a = Active; if ((object)a == null || a._modBackgrounds == null) return 0;
                var v = a._modBackgrounds.Value;
                return v == "Mods" ? 1 : v == "Game" ? 2 : 0;
            }
        }
        static bool RealPhotos { get { var a = Active; return (object)a == null || a._realPhotos == null || a._realPhotos.Value; } }
        static bool BackgroundsOn { get { return BackgroundMode != 0 || !RealPhotos; } }

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

        /// <summary>
        /// The pictures to keep. Groups: the game's own (StreamingAssets/original), this mod's own (the real photos), other mods'.
        /// Mods: other mods' (+ the real photos when RealPhotos); none left - the game's own. Mixed: all, without the real photos when they are off.
        /// Game: the game's own. An empty result means "leave the list as it is".
        /// </summary>
        static List<string> FilterBackgrounds(List<string> list)
        {
            if (_ownDir == null) _ownDir = OwnDir();
            var game = new List<string>(); var own = new List<string>(); var other = new List<string>();
            foreach (var f in list)
            {
                var n = Norm(f);
                if (n.StartsWith(_originalDir, StringComparison.OrdinalIgnoreCase)) game.Add(f);
                else if (_ownDir != null && n.StartsWith(_ownDir, StringComparison.OrdinalIgnoreCase)) own.Add(f);
                else other.Add(f);
            }
            bool photos = RealPhotos;
            var keep = new List<string>();
            switch (BackgroundMode)
            {
                case 2: keep.AddRange(game); break;
                case 1: keep.AddRange(other); if (photos) keep.AddRange(own); if (keep.Count == 0) keep.AddRange(game); break;
                default: keep.AddRange(game); keep.AddRange(other); if (photos) keep.AddRange(own); break;
            }
            return keep.Count == 0 ? list : keep;
        }

        static void DropGamePictures(List<string> list, string where)
        {
            if (list == null || list.Count == 0 || _originalDir == null) return;
            var keep = FilterBackgrounds(list);
            if (keep.Count == list.Count) return;
            int was = list.Count;
            var set = new HashSet<string>(keep);
            list.RemoveAll(f => !set.Contains(f));
            Trace("backgrounds (" + where + "): " + list.Count + " of " + was + " kept (mode " + BackgroundMode + ", real photos " + (RealPhotos ? "on" : "off") + ")");
        }

        /// <summary>The folder of this mod (the real photos live in its ui/backgrounds): the first folder under StreamingAssets or the Workshop item folder; else the dll's own.</summary>
        static string OwnDir()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(AlternativeGameInterfacePlugin).Assembly.Location) ?? "";
                string path = dir.Replace('\\', '/').TrimEnd('/') + "/";
                foreach (var marker in new[] { "/StreamingAssets/", "/1286220/" })
                {
                    int i = path.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    string rest = path.Substring(i + marker.Length);
                    int j = rest.IndexOf('/');
                    if (j > 0) return path.Substring(0, i + marker.Length + j + 1);
                }
                return Norm(dir);
            }
            catch { return null; }
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

        // ---------------------------------------------------------------- main menu: the game's video or a slideshow of the pictures
        // MainMenuView.xaml has a MediaElement (main_menu.mp4, looped) over the picture MenuSystemView shows (MenuSystemViewModel.BackgroundImage, picked in
        // ChangeBackground when CurrentWindow changes). Slideshow: the video is not loaded (its Source is cleared before the page is shown) and the picture
        // changes every few seconds while the main menu page is up (ChangeBackground again).

        static Type _menuVmType, _mainMenuType;
        static MethodInfo _menuChange;
        static FieldInfo _menuKey, _menuRandom, _mainMenuInst;
        static WeakReference _menuVm;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> VideoOff = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();
        float _slideNext;

        static bool Slideshow { get { var a = Active; return (object)a != null && a._menuBg != null && a._menuBg.Value == "Slideshow"; } }

        void HookMenuBackground(Harmony harmony, Assembly asm)
        {
            _menuVmType = asm.GetType("SeapowerUI.ViewModels.MenuSystemViewModel");
            _mainMenuType = asm.GetType("SeapowerUI.MainMenuView");
            if (_menuVmType == null || _mainMenuType == null) { Trace("MenuSystemViewModel / MainMenuView not found: the menu background option does nothing"); return; }
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _menuChange = _menuVmType.GetMethod("ChangeBackground", all);
            _menuKey = _menuVmType.GetField("backgroundImageResourceKey", all);
            _menuRandom = _menuVmType.GetField("randomizeImage", all);
            _mainMenuInst = _mainMenuType.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
            var setter = _menuVmType.GetProperty("CurrentWindow", all);
            var set = setter == null ? null : setter.GetSetMethod(true);
            if (set != null) { harmony.Patch(set, prefix: Hm("MenuWindowPrefix")); Trace("patched MenuSystemViewModel.CurrentWindow (menu background)"); }
            else Trace("MenuSystemViewModel.CurrentWindow not found: the video is switched off a frame late");
        }

        static void MenuWindowPrefix(object __instance, object value)
        {
            try
            {
                _menuVm = new WeakReference(__instance);
                if (Slideshow && value != null && value.GetType() == _mainMenuType) DisableMenuVideo(value);
            }
            catch (Exception e) { Trace("menu background: " + e.Message); }
        }

        sealed class VideoState { public object Source; }

        /// <summary>The page's video is not loaded: Source cleared (nothing to open), element collapsed and stopped. The original Source is kept for RestoreMenuVideo.</summary>
        static bool _videoOffAny;
        static void DisableMenuVideo(object page)
        {
            object o;
            if (VideoOff.TryGetValue(page, out o)) return;
            var me = MenuVideoOf(page);
            if (me == null) return;
            var st = new VideoState();
            try { st.Source = Active.Get(me, "Source"); } catch { }
            VideoOff.Add(page, st);
            _videoOffAny = true;
            try { Active.SetV(me, "Source", null); } catch (Exception e) { Trace("menu video: Source not cleared: " + e.Message); }
            CallMedia(me, "Stop");
            me.Visibility = Noesis.Visibility.Collapsed;
            Trace("menu background: the video is off (slideshow)");
        }

        /// <summary>Slideshow switched off while the page is up: the video comes back and plays from the start.</summary>
        static void RestoreMenuVideo(object page)
        {
            object o;
            if (!VideoOff.TryGetValue(page, out o)) return;
            VideoOff.Remove(page);
            _videoOffAny = false;
            var me = MenuVideoOf(page); var st = o as VideoState;
            if (me == null) return;
            try { if (st != null && st.Source != null) Active.SetV(me, "Source", st.Source); } catch (Exception e) { Trace("menu video: Source not restored: " + e.Message); }
            me.Visibility = Noesis.Visibility.Visible;
            CallMedia(me, "Play");
            Trace("menu background: the video is back");
        }

        static Noesis.UIElement MenuVideoOf(object page)
        {
            var fe = page as Noesis.FrameworkElement;
            return fe == null ? null : fe.FindName("MainMenuBackgroundVideo_MediaElement") as Noesis.UIElement;
        }

        static void CallMedia(object me, string name)
        {
            try { var m = me.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null); if (m != null) m.Invoke(me, null); } catch { }
        }

        // the cross-fade: a second Image over the game's one (same Grid, below the page); the next picture fades in on it, then it becomes the game's BackgroundImage
        const float FadeSeconds = 1.5f;
        Noesis.Image _fadeImg;
        object _fadeTex, _fadeVm; string _fadePath; float _fadeStart;

        void MenuBackgroundStep()
        {
            if (_mainMenuInst == null || _menuVm == null) return;
            var vm = _menuVm.Target;
            // the menu scene is gone (a mission is loaded): nothing to do any more
            if (vm == null || (vm is UnityEngine.Object && !(UnityEngine.Object)vm)) { _menuVm = null; _fadeTex = null; return; }
            if (!Slideshow && !_videoOffAny && _fadeTex == null) return;   // the game's video, as the game has it
            var page = _mainMenuInst.GetValue(null);
            bool onMain = page != null && ReferenceEquals(Get(vm, "CurrentWindow"), page);
            if (!Slideshow)
            {
                if (_fadeTex != null) FinishFade(vm);
                _slideNext = 0f;
                if (page != null) RestoreMenuVideo(page);
                _videoOffAny = false;
                return;
            }
            if (_fadeTex != null)
            {
                float t = (Time.unscaledTime - _fadeStart) / FadeSeconds;
                if (!onMain || t >= 1f) FinishFade(vm);
                else if (_fadeImg != null) _fadeImg.Opacity = t * t * (3f - 2f * t);
                return;
            }
            if (!onMain) { _slideNext = 0f; return; }
            DisableMenuVideo(page);
            float now = Time.unscaledTime;
            if (_slideNext == 0f) _slideNext = now + SlideSeconds();
            if (now < _slideNext) return;
            _slideNext = now + SlideSeconds();
            if (!GetB(vm, "BackgroundImageVisible")) return;
            try { StartFade(vm); } catch (Exception e) { Trace("menu slideshow: " + (e.InnerException ?? e).Message); _fadeTex = null; }
        }

        void StartFade(object vm)
        {
            var paths = Get(vm, "imagePaths") as List<string>;
            if (paths == null) { var f = _menuVmType.GetField("imagePaths", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); paths = f == null ? null : f.GetValue(vm) as List<string>; }
            if (paths == null || paths.Count == 0) return;
            var cur = _menuKey == null ? null : _menuKey.GetValue(vm) as string;
            string next = null;
            for (int i = 0; i < 6 && (next == null || (paths.Count > 1 && string.Equals(next, cur, StringComparison.OrdinalIgnoreCase))); i++) next = paths[UnityEngine.Random.Range(0, paths.Count)];
            var rl = _menuVmType.Assembly.GetType("SeaPower.Singleton`1").MakeGenericType(_menuVmType.Assembly.GetType("SeaPower.ResourceLoader")).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
            var tex = rl.GetType().GetMethod("getNoesisTextureSourceResource").Invoke(rl, new object[] { next, false, false, true });
            if (tex == null) return;
            if (_fadeImg == null)
            {
                var root = Get(vm, "RootControl");
                var grid = root == null ? null : Get(root, "Content") as Noesis.Grid;
                if (grid == null) return;
                _fadeImg = new Noesis.Image { Stretch = Noesis.Stretch.UniformToFill, IsHitTestVisible = false, Opacity = 0f };
                grid.Children.Insert(1, _fadeImg);   // over the game's picture, under the page
            }
            _fadeImg.Source = tex as Noesis.ImageSource;
            _fadeImg.Opacity = 0f; _fadeImg.Visibility = Noesis.Visibility.Visible;
            _fadeTex = tex; _fadePath = next; _fadeVm = vm; _fadeStart = Time.unscaledTime;
        }

        void FinishFade(object vm)
        {
            var tex = _fadeTex; var path = _fadePath; _fadeTex = null;
            try
            {
                var prev = _menuKey == null ? null : _menuKey.GetValue(vm) as string;
                SetV(vm, "BackgroundImage", tex);
                if (_menuKey != null) _menuKey.SetValue(vm, path);
                if (_fadeImg != null) { _fadeImg.Visibility = Noesis.Visibility.Collapsed; _fadeImg.Source = null; _fadeImg.Opacity = 0f; }
                if (!string.IsNullOrWhiteSpace(prev) && !string.Equals(prev, path, StringComparison.OrdinalIgnoreCase))
                {
                    var rl = _menuVmType.Assembly.GetType("SeaPower.Singleton`1").MakeGenericType(_menuVmType.Assembly.GetType("SeaPower.ResourceLoader")).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                    rl.GetType().GetMethod("PurgeCachedTextures").Invoke(rl, new object[] { (Predicate<string>)(k => string.Equals(k, prev, StringComparison.OrdinalIgnoreCase)) });
                }
            }
            catch (Exception e) { Trace("menu slideshow (end of fade): " + (e.InnerException ?? e).Message); }
        }

        float SlideSeconds() { return _slideSec == null ? 30f : Math.Max(5f, _slideSec.Value); }

        static string Norm(string p) { return p.Replace('\\', '/').TrimEnd('/') + "/"; }
    }
}
