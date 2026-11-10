using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// WindowedMapAndWorldView. What the game's XAML does (GameMapView.xaml, WorldViewWindowView.xaml, MainGameView.xaml):
    ///  - the Tactical Display window: Maximized="{Binding IsFullscreen, Mode=TwoWay}" - the window and SceneMapViewModel.IsFullscreen
    ///    are one state (maximize / restore buttons write into it, a change of it maximizes / restores the window);
    ///  - the World View window (WorldViewWindowViewModel) is in WindowHost.Windows while ShowPip3D = MapVisible &amp;&amp; IsFullscreen &amp;&amp; PipOpen
    ///    (SyncWorldViewWindow), its Visibility is bound to ShowPip3D; its menu item in the map's window menu (WorldViewOpen -> PipOpen)
    ///    is enabled only while IsFullscreen;
    ///  - Tab: InputHandler -> MainGameViewModel.ToggleMapFullscreen flips IsFullscreen (or PipOpen with forced picture-in-picture);
    ///    IsFullscreen turned on sets PipOpen = AutoOpenPictureInPicture (MainGameViewModel, Map.PropertyChanged).
    /// With the option on: the game's "World View shown" is made permanent (ShowPip3D and PipOpen answer true whatever IsFullscreen is), so the
    /// Tactical Display window is an ordinary window (its Maximized and IsFullscreen may differ freely, no revert, no blocking), Tab does not
    /// flip the fullscreen. Both windows are below every other window and change places only between themselves (BringToFront).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        sealed class MapModeState { public float Next; public bool Set, MenuDone; public int MenuTries; }
        static readonly ConditionalWeakTable<object, MapModeState> MapMode = new ConditionalWeakTable<object, MapModeState>();
        static WeakReference _mapWin;        // the Tactical Display window (its DataContext is the SceneMapViewModel)

        static UnityEngine.GameObject _backdrop;
        static WeakReference _wvWin, _wvView;                  // the World View window, its Pip3DView element
        static FieldInfo _cmCamera; static MethodInfo _cmSetRects, _cmRequest;
        static bool _mapRectSelf;
        static int _wvDisp; static float[] _wvPx;                                   // the World View picture on the screen, pixels, top left origin
        static UnityEngine.Rect _wvRectNow; static int _wvAlive; static bool _mapDownInside;
        static FieldInfo _mcsState;
        // WindowedMapRenderToTexture: the cameras render into _rtCam (the window's pixel size, or bigger with supersampling), _rtShown is what the element draws
        static UnityEngine.RenderTexture _rtCam, _rtShown; static int _rtPendW, _rtPendH; static float _rtPendSince;
        static Noesis.TextureSource _rtSrc; static Noesis.ImageBrush _rtBrush; static UnityEngine.RenderTexture _rtBrushOf;
        static FieldInfo _cmParticle; static float _rtBgA = -1f;

        static FieldInfo _gMain;
        /// <summary>The game's main view model of a running mission (null in the menus) and its map.</summary>
        static object MainVm()
        {
            try
            {
                if (_gMain == null && _cmCamera != null)
                {
                    var gt = _cmCamera.DeclaringType.Assembly.GetType("SeaPower.Globals");
                    _gMain = gt == null ? null : gt.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                return _gMain == null ? null : _gMain.GetValue(null);
            }
            catch { return null; }
        }

        static PropertyInfo _hudProp; static bool _photoApplied;
        /// <summary>The HUD is hidden (Backspace: the game's photo mode; also the console, the briefing): the 3D view is shown on the whole screen.</summary>
        static bool PhotoMode() { return MapOn() && HudHidden() && _wvDisp == 0; }

        static bool HudHidden()
        {
            try
            {
                var vm = MainVm();
                if (vm == null) return false;
                if (_hudProp == null) _hudProp = vm.GetType().GetProperty("IsHudVisible");
                return _hudProp != null && !(bool)_hudProp.GetValue(vm, null);
            }
            catch { return false; }
        }

        static PropertyInfo _mapProp;
        static bool MapMissionOn()
        {
            var vm = MainVm();
            if (vm == null) return false;
            try
            {
                if (_mapProp == null) _mapProp = vm.GetType().GetProperty("Map");
                return _mapProp != null && _mapProp.GetValue(vm, null) != null;
            }
            catch { return false; }
        }

        // CameraManager.Instance and its members are looked up once (these are used every frame)
        static PropertyInfo _cmInstProp, _cmActiveProp, _cmSuperProp;
        static readonly object[] _args16 = new object[] { 16, 16 };
        static readonly object[] _argsRect = new object[1];
        static readonly UnityEngine.Camera[] _cams1 = new UnityEngine.Camera[1], _cams2 = new UnityEngine.Camera[2];
        /// <summary>The game's CameraManager (null when there is none).</summary>
        static object CmInst()
        {
            try
            {
                if (_cmCamera == null) return null;
                if (_cmInstProp == null)
                {
                    var cmT = _cmCamera.DeclaringType;
                    var gt = cmT.Assembly.GetType("SeaPower.Singleton`1").MakeGenericType(cmT);
                    _cmInstProp = gt.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                return _cmInstProp == null ? null : _cmInstProp.GetValue(null, null);
            }
            catch { return null; }
        }

        /// <summary>The cameras the picture is drawn by (a shared array: use it at once, do not keep it).</summary>
        static UnityEngine.Camera[] RtCameras(object cm)
        {
            var c1 = _cmCamera.GetValue(cm) as UnityEngine.Camera;
            var c2 = _cmParticle == null ? null : _cmParticle.GetValue(cm) as UnityEngine.Camera;
            if (c2 == null) { _cams1[0] = c1; return _cams1; }
            _cams2[0] = c1; _cams2[1] = c2; return _cams2;
        }

        /// <summary>The cameras go back to the screen, the textures are freed.</summary>
        static void MapRtRelease(object cm)
        {
            try
            {
                if (cm != null)
                    foreach (var c in RtCameras(cm))
                        if (c != null && c.targetTexture == _rtCam) { c.targetTexture = null; if (_rtBgA >= 0f) { var bg = c.backgroundColor; bg.a = _rtBgA; c.backgroundColor = bg; } }
            }
            catch { }
            _rtBgA = -1f;
            _rtBrush = null; _rtSrc = null; _rtBrushOf = null;
            FreeRt(_rtCam, _rtShown);
            _rtCam = null; _rtShown = null;
        }

        static void FreeRt(UnityEngine.RenderTexture cam, UnityEngine.RenderTexture shown)
        {
            if (cam != null && cam != shown) { cam.Release(); UnityEngine.Object.Destroy(cam); }
            if (shown != null) { shown.Release(); UnityEngine.Object.Destroy(shown); }
        }

        /// <summary>After the last camera has drawn into the (supersampled) texture: it is shrunk by halves into the texture the interface shows.</summary>
        sealed class MapRtResolve : UnityEngine.MonoBehaviour
        {
            void OnPostRender()
            {
                var big = _rtCam; var small = _rtShown;
                if (big == null || small == null || big == small || !big.IsCreated() || !small.IsCreated()) return;
                var own = GetComponent<UnityEngine.Camera>();
                if (own == null || own.targetTexture != big) return;
                var cm = CmInst();
                if (cm == null) return;
                var cams = RtCameras(cm);
                var last = cams.Length > 1 && cams[1] != null && cams[1].enabled ? cams[1] : cams[0];
                if (last == null || last.gameObject != gameObject) return;
                var cur = big;
                var fm = big.filterMode; big.filterMode = UnityEngine.FilterMode.Bilinear;
                while (cur.width > small.width * 2 || cur.height > small.height * 2)
                {
                    var tmp = UnityEngine.RenderTexture.GetTemporary(Math.Max(small.width, cur.width / 2), Math.Max(small.height, cur.height / 2), 0, big.format);
                    tmp.filterMode = UnityEngine.FilterMode.Bilinear;
                    UnityEngine.Graphics.Blit(cur, tmp);
                    if (cur != big) UnityEngine.RenderTexture.ReleaseTemporary(cur);
                    cur = tmp;
                }
                UnityEngine.Graphics.Blit(cur, small);
                if (cur != big) UnityEngine.RenderTexture.ReleaseTemporary(cur);
                big.filterMode = fm;
            }
        }

        /// <summary>Texture of the window's pixel size: made when the size settles (0.15 s), the cameras are switched to it. With supersampling
        /// the cameras draw into a bigger one that is shrunk into the shown one.</summary>
        void MapRtStep(object cm, float pw, float ph)
        {
            int factor = 1;
            try { if (_cmSuperProp == null) _cmSuperProp = cm.GetType().GetProperty("PipSupersampleFactor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); if (_cmSuperProp != null) factor = Math.Max(1, (int)_cmSuperProp.GetValue(null, null)); } catch { }
            int w = Math.Max(16, Math.Min(8192, (int)Math.Round(pw))), h = Math.Max(16, Math.Min(8192, (int)Math.Round(ph)));
            float k = factor, cap = 16000000f;
            if (w * (float)h * k * k > cap) k = Math.Max(1f, (float)Math.Sqrt(cap / (w * (float)h)));
            int bw = k <= 1f ? w : Math.Min(8192, (int)Math.Round(w * k)), bh = k <= 1f ? h : Math.Min(8192, (int)Math.Round(h * k));
            if (w != _rtPendW || h != _rtPendH) { _rtPendW = w; _rtPendH = h; _rtPendSince = UnityEngine.Time.unscaledTime; }
            if (_rtShown != null && _rtShown.width == w && _rtShown.height == h && _rtCam != null && _rtCam.width == bw && _rtCam.height == bh) return;
            if (_rtShown != null && UnityEngine.Time.unscaledTime - _rtPendSince < 0.15f) return;
            var oldCam = _rtCam; var oldShown = _rtShown;
            int aa = Math.Max(1, UnityEngine.QualitySettings.antiAliasing);
            UnityEngine.RenderTexture shown, camRt;
            if (bw == w && bh == h)
                shown = camRt = new UnityEngine.RenderTexture(w, h, 24, UnityEngine.RenderTextureFormat.Default) { antiAliasing = aa };
            else
            {
                camRt = new UnityEngine.RenderTexture(bw, bh, 24, UnityEngine.RenderTextureFormat.Default) { antiAliasing = aa };
                shown = new UnityEngine.RenderTexture(w, h, 0, UnityEngine.RenderTextureFormat.Default) { antiAliasing = 1 };
            }
            foreach (var t in new[] { camRt, shown }) { t.useMipMap = false; t.filterMode = UnityEngine.FilterMode.Bilinear; t.wrapMode = UnityEngine.TextureWrapMode.Clamp; }
            camRt.name = "AlternativeGameInterface_WorldView_Camera"; shown.name = "AlternativeGameInterface_WorldView";
            camRt.Create(); if (shown != camRt) shown.Create();
            _rtCam = camRt; _rtShown = shown;
            foreach (var c in RtCameras(cm))
            {
                if (c == null) continue;
                if (_rtBgA < 0f) _rtBgA = c.backgroundColor.a;
                var bg = c.backgroundColor; bg.a = 1f; c.backgroundColor = bg;     // nothing of the scene is see-through in the window
                c.targetTexture = camRt;
                if (c.GetComponent<MapRtResolve>() == null) c.gameObject.AddComponent<MapRtResolve>();
            }
            FreeRt(oldCam, oldShown);
            Trace("World View texture " + w + "x" + h + (shown != camRt ? " (cameras draw " + bw + "x" + bh + ", shrunk)" : "") + ", supersample x" + factor + ", MSAA " + aa);
        }
        static UnityEngine.Transform _backdropSprite;
        static float _backdropAspect;
        const string BackdropFile = "AlternativeGameInterface_Backdrop";   // .jpg / .png next to AlternativeGameInterface.dll: replace it with any picture (cover-fitted)

        /// <summary>The picture over the grey: AlternativeGameInterface_Backdrop.jpg / .png next to the dll (a world map). No file - plain grey.</summary>
        void AddBackdropMap()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(AlternativeGameInterfacePlugin).Assembly.Location) ?? "";
                string file = null;
                foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir, BackdropFile + ext))) { file = System.IO.Path.Combine(dir, BackdropFile + ext); break; }
                if (file == null)
                {
                    Trace("backdrop picture: no " + BackdropFile + ".jpg/.png next to the dll - plain grey");
                    return;
                }
                var ic = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var load = ic == null ? null : ic.GetMethod("LoadImage", new[] { typeof(UnityEngine.Texture2D), typeof(byte[]) });
                if (load == null) { Trace("backdrop picture: ImageConversion not found"); return; }
                var tex = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, true);
                if (!(bool)load.Invoke(null, new object[] { tex, System.IO.File.ReadAllBytes(file) })) { Trace("backdrop picture: cannot read " + file); return; }
                tex.wrapMode = UnityEngine.TextureWrapMode.Clamp; tex.filterMode = UnityEngine.FilterMode.Trilinear; tex.anisoLevel = 4;
                var go = new UnityEngine.GameObject("AlternativeGameInterface_BackdropMap");
                go.layer = 31;
                go.transform.SetParent(_backdrop.transform, false);
                go.transform.localPosition = new UnityEngine.Vector3(0f, 0f, 10f);
                var sr = go.AddComponent<UnityEngine.SpriteRenderer>();
                sr.sprite = UnityEngine.Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new UnityEngine.Vector2(0.5f, 0.5f), 100f);
                _backdropSprite = go.transform; _backdropAspect = (float)tex.width / tex.height; _backdropFitW = 0;
                _backdrop.transform.position = new UnityEngine.Vector3(50000f, 0f, 0f);
                _backdrop.GetComponent<UnityEngine.Camera>().cullingMask = 1 << 31;
                FitBackdropMap();
                Trace("backdrop picture: " + System.IO.Path.GetFileName(file) + " " + tex.width + "x" + tex.height);
            }
            catch (Exception e) { Trace("backdrop picture: " + e.Message); }
        }

        static int _backdropFitW, _backdropFitH;
        void FitBackdropMap()
        {
            // camera: orthographic, size 5 (10 units high); the picture is 100 px per unit, scaled to cover the screen
            float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
            if (sh <= 0) return;
            if (_backdropFitW == (int)sw && _backdropFitH == (int)sh) return;      // the picture is refitted only when the screen size changes
            _backdropFitW = (int)sw; _backdropFitH = (int)sh;
            float viewW = 10f * sw / sh, viewH = 10f;
            float picH = 10f, picW = picH * _backdropAspect;              // unit scale: the sprite is (w/100) x (h/100); relative to it
            var sp = _backdropSprite.GetComponent<UnityEngine.SpriteRenderer>().sprite;
            float w0 = sp.rect.width / 100f, h0 = sp.rect.height / 100f;
            float k = Math.Max(viewW / w0, viewH / h0);
            _backdropSprite.localScale = new UnityEngine.Vector3(k, k, 1f);
        }

        /// <summary>The screen behind the windows: in the game's "map fullscreen" state the main camera is shrunk to the World View's rectangle
        /// and nothing clears the rest of the screen (it was hidden by the opaque map). A camera of our own that only clears to grey, drawn
        /// before every other camera, does it while the map windows are shown.</summary>
        void BackdropStep()
        {
            bool want = false;
            try
            {
                want = _windowedMap != null && _windowedMap.Value && MapMissionOn();
            }
            catch { }
            if (want && _backdrop == null)
            {
                _backdrop = new UnityEngine.GameObject("AlternativeGameInterface_Backdrop");
                UnityEngine.Object.DontDestroyOnLoad(_backdrop);
                var cam = _backdrop.AddComponent<UnityEngine.Camera>();
                cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
                cam.backgroundColor = new UnityEngine.Color(0.55f, 0.57f, 0.60f, 1f);
                cam.cullingMask = 0;
                cam.depth = -1000f;
                cam.orthographic = true; cam.orthographicSize = 5f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.useOcclusionCulling = false;
                AddBackdropMap();
                Trace("backdrop camera created (WindowedMapAndWorldView)");
            }
            else if (want && _backdropSprite != null) FitBackdropMap();
            else if (!want && _backdrop != null)
            {
                UnityEngine.Object.Destroy(_backdrop);
                _backdrop = null; _backdropSprite = null;
                Trace("backdrop camera removed");
            }
            // the mission is over / the option is off: the cameras go back to the screen and the textures are freed (also with no backdrop)
            if (!want && (_rtCam != null || _rtShown != null || _photoApplied) || want && _rtCam != null && WorldDetachedNow())
            {
                try { MapRtRelease(CmInst()); } catch { }
                _photoApplied = false; _wvPx = null; _wvView = null; _wvDisp = 0;
            }
        }

        void HookMapFullscreen(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = asm.GetType("SeapowerUI.ViewModels.SceneMapViewModel");
            var main = asm.GetType("SeapowerUI.ViewModels.MainGameViewModel");
            if (t == null || main == null) { Trace("SceneMapViewModel / MainGameViewModel not found: WindowedMapAndWorldView not available"); return; }
            var tab = main.GetMethod("ToggleMapFullscreen", all);
            var show = main.GetProperty("ShowPip3D", all);
            var showGet = show == null ? null : show.GetGetMethod(true);
            if (tab != null) TryPatch(harmony, tab, "MapTabPrefix", null, null);
            if (showGet != null) TryPatch(harmony, showGet, null, "MapShowPipPostfix", null);
            var pipSet = main.GetProperty("PipOpen", all);
            var pipSetter = pipSet == null ? null : pipSet.GetSetMethod(true);
            if (pipSetter != null) TryPatch(harmony, pipSetter, null, "MapPipOpenPostfix", null);      // the World View open / closed is remembered per desktop
            // the 3D picture: the main camera renders into a texture of the World View window's size, the window's Pip3DView element draws it
            var cmT = asm.GetType("SeaPower.CameraManager");
            var setRects = cmT == null ? null : cmT.GetMethod("SetCameraRects", all);
            var pipView = asm.GetType("SeapowerUI.Pip3DView");
            var onRender = pipView == null ? null : pipView.GetMethod("OnRender", all);
            var remap = pipView == null ? null : pipView.GetMethod("RemapMouse", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var pause = cmT == null ? null : cmT.GetMethod("SetRenderingPaused", all);
            if (pause != null) TryPatch(harmony, pause, "MapPausePrefix", null, null);
            if (cmT != null) { _cmCamera = cmT.GetField("_camera", all); _cmParticle = cmT.GetField("_particleCamera", all); _cmSetRects = setRects; _cmRequest = cmT.GetMethod("RequestPipFrame", all); }
            if (setRects != null) TryPatch(harmony, setRects, "MapSetRectsPrefix", null, null);
            if (onRender != null) TryPatch(harmony, onRender, "MapPipRenderPrefix", null, null);
            if (remap != null) TryPatch(harmony, remap, "MapRemapPrefix", null, null);
            Trace("World View drawn from the camera texture: SetRenderingPaused " + (pause != null) + ", SetCameraRects " + (setRects != null) + ", Pip3DView.OnRender " + (onRender != null) + ", RemapMouse " + (remap != null));
            // clicks into the 3D world only inside the World View picture (the rest of the desktop is the backdrop)
            var mcs = asm.GetType("SeaPower.MouseControlState");
            var mcsUpdate = mcs == null ? null : mcs.GetMethod("OnUpdate", all);
            if (mcs != null) _mcsState = mcs.GetField("_currentState", all);
            if (mcsUpdate != null && _mcsState != null) TryPatch(harmony, mcsUpdate, null, "MapMouseStatePostfix", null);
            Trace("patched MouseControlState.OnUpdate (WindowedMapAndWorldView): " + (mcsUpdate != null && _mcsState != null));
            Trace("patched MainGameViewModel.ToggleMapFullscreen, ShowPip3D (WindowedMapAndWorldView)" +
                  (tab == null || showGet == null ? " - not all found" : ""));
        }

        static bool MapOn() { var a = Active; return (object)a != null && a._windowedMap != null && a._windowedMap.Value; }

        static bool WorldDetachedNow() { try { var vm = MainVm(); return vm != null && SGetB(vm, "WorldViewDetached"); } catch { return false; } }

        /// <summary>The one who called the patched method (frame 3: 0 = Caller itself, 1 = the prefix, 2 = the patched method): the game's binding machinery
        /// (a compiler-made closure / PropertyAccessor) or the game's own code.</summary>
        static string Caller(int skip)
        {
            try
            {
                var m = new System.Diagnostics.StackFrame(skip, false).GetMethod();
                return m == null || m.DeclaringType == null ? "" : (m.DeclaringType.FullName ?? m.DeclaringType.Name) + "." + m.Name;
            }
            catch { return ""; }
        }

        // The World View is shown when it is open (PipOpen - its X button and the "World View" item of the Tactical Display's menu), whatever the
        // Tactical Display and its maximized state are: both windows are opened and closed independently of each other.
        static void MapShowPipPostfix(object __instance, ref bool __result)
        {
            if (__result || !MapOn() || PhotoMode()) return;      // photo mode (HUD hidden): the game's own value - no window, the optical view fills the screen like in the game
            try
            {
                var t = __instance.GetType();
                bool det = (bool)t.GetProperty("WorldViewDetached").GetValue(__instance, null);
                if (!det && (bool)t.GetProperty("PipOpen").GetValue(__instance, null)) __result = true;
            }
            catch { }
        }

        static PropertyInfo _showPipProp;
        // the cameras render only while the World View is shown (closed: nothing to see, no load)
        static void MapPausePrefix(ref bool paused)
        {
            if (!MapOn()) return;
            try
            {
                var vm = MainVm();
                if (vm == null) return;
                if (SGetB(vm, "WorldViewDetached")) return;      // the game's World View monitor: the cameras render there, whatever the window says
                if (_showPipProp == null) _showPipProp = vm.GetType().GetProperty("ShowPip3D");
                paused = !((bool)_showPipProp.GetValue(vm, null) || PhotoMode());
            }
            catch { }
        }

        // the game's own rectangle of the picture (bottom left) is not applied: the step below puts it over the World View window
        static bool MapSetRectsPrefix(UnityEngine.Rect rect)
        {
            if (_mapRectSelf || !MapOn()) return true;
            return rect.width > 0.999f && rect.height > 0.999f;     // back to the whole screen is let through
        }

        // the element draws nothing (the camera shows through); the picture is still "requested" so that the game keeps it active
        static bool MapPipRenderPrefix(object __instance, Noesis.DrawingContext context)
        {
            if (!MapOn()) return true;
            try
            {
                var cm = CmInst();
                if (cm != null && _cmRequest != null) _cmRequest.Invoke(cm, _args16);
            }
            catch (Exception e) { TraceOnce("RequestPipFrame", e); }
            {
                try
                {
                    var fe0 = (Noesis.FrameworkElement)__instance;
                    if (_rtShown != null && _rtShown.IsCreated() && fe0.ActualWidth > 0f && fe0.ActualHeight > 0f)
                    {
                        if (_rtBrush == null || _rtBrushOf != _rtShown)
                        {
                            _rtSrc = new Noesis.TextureSource(_rtShown);
                            _rtBrush = new Noesis.ImageBrush(_rtSrc) { Stretch = Noesis.Stretch.Fill };
                            _rtBrushOf = _rtShown;
                        }
                        // texel for pixel: the texture's corner is put on a whole screen pixel (the element itself stands at a fractional one),
                        // so the interface samples every texel at its centre - no resampling blur
                        var q0 = fe0.PointToScreen(new Noesis.Point(0f, 0f));
                        var q1 = fe0.PointToScreen(new Noesis.Point(fe0.ActualWidth, fe0.ActualHeight));
                        float sx = (q1.X - q0.X) / fe0.ActualWidth, sy = (q1.Y - q0.Y) / fe0.ActualHeight;
                        if (sx > 0.01f && sy > 0.01f)
                        {
                            float ox = ((float)Math.Round(q0.X) - q0.X) / sx, oy = ((float)Math.Round(q0.Y) - q0.Y) / sy;
                            context.DrawRectangle(_rtBrush, null, new Noesis.Rect(ox, oy, _rtShown.width / sx, _rtShown.height / sy));
                        }
                        else context.DrawRectangle(_rtBrush, null, new Noesis.Rect(0f, 0f, fe0.ActualWidth, fe0.ActualHeight));
                        return false;
                    }
                }
                catch (Exception e) { Trace("World View texture draw: " + e.Message); }
            }
            try
            {
                // a transparent fill: nothing is seen, but the element is still hit-tested (the game takes the mouse over it as "over the World View")
                var fe = (Noesis.FrameworkElement)__instance;
                if (fe.ActualWidth > 0f && fe.ActualHeight > 0f)
                {
                    if (_clearBrush == null) _clearBrush = new Noesis.SolidColorBrush(Noesis.Color.FromArgb(0, 0, 0, 0));
                    context.DrawRectangle(_clearBrush, null, new Noesis.Rect(0f, 0f, fe.ActualWidth, fe.ActualHeight));
                }
            }
            catch (Exception e) { TraceOnce("World View hit area", e); }
            return false;
        }

        static Noesis.SolidColorBrush _clearBrush;
        static readonly System.Collections.Generic.HashSet<string> _traceOnce = new System.Collections.Generic.HashSet<string>();
        /// <summary>A failure that may repeat every frame is written to the trace once.</summary>
        static void TraceOnce(string what, Exception e)
        {
            try { if (_traceOnce.Add(what + e.GetType().Name + e.Message)) Trace(what + ": " + e.GetType().Name + " " + e.Message); } catch { }
        }

        // the mouse is over the real screen area of the picture: no remapping
        static bool MapRemapPrefix(UnityEngine.Vector3 mousePos, ref UnityEngine.Vector3 __result)
        {
            if (!MapOn()) return true;
            if (WorldDisplayMouse()) return true;      // the mouse is on the monitor the game shows the World View on: its own mapping
            if (PhotoMode()) { __result = mousePos; return false; }
            if (MapOn() && _rtCam != null && _wvPx != null && _wvPx[2] > _wvPx[0] && _wvPx[3] > _wvPx[1])
            {
                // the camera renders into a texture: the mouse in pixels of that texture (bottom left origin)
                float mx = mousePos.x, my = UnityEngine.Screen.height - mousePos.y;
                if (_wvDisp > 0)
                {
                    try { var rel = UnityEngine.Display.RelativeMouseAt(mousePos); mx = rel.x; my = UnityEngine.Display.displays[_wvDisp].renderingHeight - rel.y; } catch { }
                }
                float u = (mx - _wvPx[0]) / (_wvPx[2] - _wvPx[0]);
                float v = (my - _wvPx[1]) / (_wvPx[3] - _wvPx[1]);
                __result = new UnityEngine.Vector3(u * _rtCam.width, (1f - v) * _rtCam.height, mousePos.z);
                return false;
            }
            __result = mousePos;
            return false;
        }

        /// <summary>Every frame after the interface is updated: the texture follows the World View window's picture element (size in screen pixels).</summary>
        void MapPipStep()
        {
            if (_windowedMap == null || !_windowedMap.Value || _cmCamera == null || _cmSetRects == null) return;
            try
            {
                // photo mode: the cameras draw on the screen itself, back into the texture when the HUD is back
                bool photo = PhotoMode();
                if (photo != _photoApplied)
                {
                    var cm0 = CmInst();
                    if (cm0 != null)
                    {
                        foreach (var cam in RtCameras(cm0)) if (cam != null) cam.targetTexture = photo ? null : _rtCam;
                        _photoApplied = photo;
                        Trace(photo ? "photo mode: the 3D view on the whole screen" : "photo mode over: the 3D view back in the World View window");
                    }
                }
                if (photo) return;
                var ww = _wvWin == null ? null : _wvWin.Target;
                if (ww == null || !GetB(ww, "IsLoaded")) return;
                var view = _wvView == null ? null : _wvView.Target;
                if (view == null || !GetB(view, "IsLoaded"))
                {
                    float tn = UnityEngine.Time.unscaledTime;
                    if (tn < _wvFindNext) return;                    // the tree is searched a few times a second, not every frame
                    _wvFindNext = tn + 0.25f;
                    view = FindPipView(ww, 0);
                    _wvView = view == null ? null : new WeakReference(view);
                    if (view == null) return;
                }
                var cm = CmInst();
                if (cm == null) return;
                if (_cmActiveProp == null) _cmActiveProp = _cmCamera.DeclaringType.GetProperty("IsPipActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_cmActiveProp == null || !(bool)_cmActiveProp.GetValue(cm, null)) return;
                var fe = (Noesis.FrameworkElement)view;
                float aw = fe.ActualWidth, ah = fe.ActualHeight;
                if (aw < 4f || ah < 4f) return;
                var p0 = fe.PointToScreen(new Noesis.Point(0f, 0f));
                var p1 = fe.PointToScreen(new Noesis.Point(aw, ah));
                float sw, sh; DisplaySize(fe, out sw, out sh);
                _wvDisp = DisplayOf(fe);
                float l = Math.Min(p0.X, p1.X), r = Math.Max(p0.X, p1.X), t = Math.Min(p0.Y, p1.Y), b = Math.Max(p0.Y, p1.Y);
                // the whole window area (also the part off the screen): the texture is made for it, the mouse is mapped by it
                if (_wvPx == null) _wvPx = new float[4];
                _wvPx[0] = l; _wvPx[1] = t; _wvPx[2] = r; _wvPx[3] = b;
                _wvRectNow = new UnityEngine.Rect(l / sw, (sh - b) / sh, (r - l) / sw, (b - t) / sh); _wvAlive = UnityEngine.Time.frameCount;
                MapRtStep(cm, r - l, b - t);
                var c0 = _cmCamera.GetValue(cm) as UnityEngine.Camera;
                if (c0 != null && (c0.rect.width < 0.999f || c0.rect.height < 0.999f || c0.rect.x > 0.001f || c0.rect.y > 0.001f))
                {
                    _mapRectSelf = true;
                    _argsRect[0] = new UnityEngine.Rect(0f, 0f, 1f, 1f);
                    try { _cmSetRects.Invoke(cm, _argsRect); } finally { _mapRectSelf = false; }
                }
            }
            catch (Exception e) { TraceOnce("MapPipStep", e); }
        }
        static float _wvFindNext;

        object FindPipView(object e, int depth)
        {
            if (e == null || depth > 40) return null;
            if (e.GetType().Name == "Pip3DView") return e;
            for (int k = 0, n = VtChildrenCount(e); k < n; k++)
            {
                var f = FindPipView(VtChild(e, k), depth + 1);
                if (f != null) return f;
            }
            return null;
        }

        static bool _inOver;
        /// <summary>Mouse inside the World View picture but the game says "UI" (the optical view overlay lies over the picture and is not the Pip3DView):
        /// when the hit element is inside the World View window, it is the 3D view: the game's update is repeated without the "over Noesis" flag.</summary>
        /// <summary>What is under the mouse, and whether it is the 3D picture: inside the World View window and not under a control of a window (a resize thumb, the title bar thumb, a button:
        /// those take the mouse themselves). The point is in screen pixels with the top left origin, like Visual.PointToScreen gives them (the game's own UpdateCursor multiplies the mouse
        /// by UIScale * height / 1080, which is right only at 1080 lines: at 1440 lines its hit lies a third too far from the corner and the interface's "mouse over" flag is wrong).</summary>
        static bool HitIsWorld(Noesis.Visual win, out Noesis.DependencyObject hit)
        {
            hit = null;
            float px, py;
            if (!PointerOn(DisplayOf(win), out px, out py)) return false;
            var root = Noesis.VisualTreeHelper.GetRoot(win) as Noesis.Visual;
            if (root == null) return false;
            var pt = root.PointFromScreen(new Noesis.Point(px, py));
            hit = Noesis.VisualTreeHelper.HitTest(root, pt).VisualHit;
            for (Noesis.DependencyObject d = hit; d != null; d = Noesis.VisualTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, win)) return true;
                for (var t = d.GetType(); t != null; t = t.BaseType) if (t.Name == "Thumb" || t.Name == "ButtonBase") return false;
            }
            return false;
        }

        static Noesis.Visual WorldWindow() { return _wvWin == null ? null : _wvWin.Target as Noesis.Visual; }

        static UnityEngine.Vector3 _hitMp; static bool _hitKnown, _hitWorld;
        /// <summary>The mouse is inside the picture's rectangle: is the 3D picture really what is under it (not a window over it)? Tested when the mouse moved or a button went down, not every frame.</summary>
        static bool OverWorld(bool inside, bool force)
        {
            if (!inside) { _hitKnown = false; return false; }
            var mp = UnityEngine.Input.mousePosition;
            if (!force && _hitKnown && mp == _hitMp) return _hitWorld;
            var ww = WorldWindow(); Noesis.DependencyObject hit = null;
            _hitWorld = ww != null && (HitIsWorld(ww, out hit) || hit == null);      // no hit at all: the picture, as in the game's own test
            _hitMp = mp; _hitKnown = true;
            return _hitWorld;
        }

        /// <summary>The mouse is over the 3D picture but the game says "UI" (its own test is off at other resolutions than 1080, or the optical view overlay lies over the picture): the game's update is repeated without the "over Noesis" flag.</summary>
        static void MapOverlayIsWorld(object mcs)
        {
            if (_inOver) return;
            if (!"UI".Equals(_mcsState.GetValue(mcs).ToString())) return;
            RepeatWorld(mcs);
        }

        static void RepeatWorld(object mcs)
        {
            var t = mcs.GetType();
            // the first pass left "UI" as the current state; a button pressed now would take it as the state the press began in and the whole drag would go to the UI
            _mcsState.SetValue(mcs, Enum.Parse(_mcsState.FieldType, "All"));
            t.GetMethod("setMouseOverNoesis").Invoke(mcs, new object[] { false });
            _inOver = true;
            try { t.GetMethod("OnUpdate").Invoke(mcs, null); } finally { _inOver = false; }
        }

        /// <summary>Ordinary mode (the 3D view fills the screen): the game's own "mouse over the interface" test is off at any height but 1080 lines (see HitIsWorld),
        /// so a window of ours (or the game's) dragged over the picture rotated the camera too. The test is repeated in screen pixels: over an interface element = UI state,
        /// over the picture = the game's update without the "over Noesis" flag. A press keeps the side it began on until the button is released.</summary>
        static bool _fixDownNoes;
        static void GameMouseFix(object mcs)
        {
            var a = Active; if ((object)a == null || HudHidden() || MainVm() == null) return;
            if (MouseDisplay() > 0 || WorldDisplayMouse()) return;      // the mouse is on another monitor (the game's second map / World View): the game decides
            var view = a.MainGameViewInstance() as Noesis.Visual;
            var root = view == null ? null : Noesis.VisualTreeHelper.GetRoot(view) as Noesis.Visual;
            if (root == null) return;
            var mp = UnityEngine.Input.mousePosition;
            bool down = UnityEngine.Input.GetMouseButtonDown(0) || UnityEngine.Input.GetMouseButtonDown(1) || UnityEngine.Input.GetMouseButtonDown(2);
            bool held = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetMouseButton(2);
            bool noes = _fixDownNoes;
            if (!held || down)
            {
                var hit = Noesis.VisualTreeHelper.HitTest(root, root.PointFromScreen(new Noesis.Point(mp.x, UnityEngine.Screen.height - mp.y))).VisualHit;
                noes = hit != null;
                for (Noesis.DependencyObject d = hit; d != null && noes; d = Noesis.VisualTreeHelper.GetParent(d)) if (d.GetType().Name == "Pip3DView") noes = false;
                _fixDownNoes = noes;
            }
            if (!noes) { MapOverlayIsWorld(mcs); return; }
            _mcsState.SetValue(mcs, Enum.Parse(_mcsState.FieldType, "UI"));
            var cmT = _cmCamera.DeclaringType;
            var gt = cmT.Assembly.GetType("SeaPower.Singleton`1").MakeGenericType(cmT);
            var cm = gt.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
            if (cm != null) cmT.GetMethod("setMouseInteractionFlag").Invoke(cm, new object[] { false });
        }

        static void MapMouseStatePostfix(object __instance)
        {
            if (!MapOn()) { try { GameMouseFix(__instance); } catch { } return; }
            if (WorldDisplayMouse()) return;      // the mouse is on the monitor with the game's World View: the game decides
            if (Monitors.Count > 0 && MouseDisplay() > 0 && MouseDisplay() != _wvDisp) { ForceUi(__instance); return; }      // another monitor of ours: only windows are there
            if (PhotoMode()) return;     // photo mode: the whole screen is the 3D view
            try
            {
                var mp = UnityEngine.Input.mousePosition;
                // no World View picture (closed): no mouse in the 3D world at all
                bool alive = UnityEngine.Time.frameCount - _wvAlive <= 2 && _wvRectNow.width > 0f && _wvPx != null;
                float px, py;
                bool inside = alive && PointerOn(_wvDisp, out px, out py) && px >= _wvPx[0] && px <= _wvPx[2] && py >= _wvPx[1] && py <= _wvPx[3];
                bool down = UnityEngine.Input.GetMouseButtonDown(0) || UnityEngine.Input.GetMouseButtonDown(1) || UnityEngine.Input.GetMouseButtonDown(2);
                bool over = OverWorld(inside, down);       // the picture, not a window over it or a thumb: resizing a window must not rotate the camera too
                if (down) _mapDownInside = over;
                bool held = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetMouseButton(2);
                if (held ? _mapDownInside : over) { MapOverlayIsWorld(__instance); return; }
                ForceUi(__instance);
            }
            catch { }
        }

        /// <summary>The mouse state is UI and the camera ignores the mouse.</summary>
        static void ForceUi(object mcs)
        {
            _mcsState.SetValue(mcs, Enum.Parse(_mcsState.FieldType, "UI"));
            var cmT = _cmCamera.DeclaringType;
            var gt = cmT.Assembly.GetType("SeaPower.Singleton`1").MakeGenericType(cmT);
            var cm = gt.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
            if (cm != null) cmT.GetMethod("setMouseInteractionFlag").Invoke(cm, new object[] { false });
        }

        /// <summary>CameraManager.MouseOnWorldDisplay: the mouse is on the display the game shows the World View on (the game's own monitor setting).</summary>
        static bool WorldDisplayMouse()
        {
            try { var cm = CmInst(); return cm != null && SGetB(cm, "MouseOnWorldDisplay"); }
            catch { return false; }
        }

        // Tab: no fullscreen flip (the order of the two windows is changed by a click)
        static bool MapTabPrefix()
        {
            if (!MapOn()) return true;
            if (!Caller(3).StartsWith("SeaPower.InputHandler.") && !Caller(3).Contains("InputHandler.")) return true;
            if (WindowSetsOn()) SwitchWindowSetNow();      // Tab: the other set of window settings
            else Trace("minimap fullscreen key: the toggle skipped (WindowedMapAndWorldView)");
            return false;
        }

        /// <summary>Per map window: the Tactical Display window is remembered, starts as an ordinary window, its "World View" menu item is made usable,
        /// and the game is told that the World View state changed (so that its window is created).</summary>
        void MapModeStep(object w, float now)
        {
            if (_windowedMap == null || !_windowedMap.Value || _winType == null) return;
            var st = MapMode.GetOrCreateValue(w);
            if ((st.Set && st.MenuDone) || now < st.Next) return;
            st.Next = now + 0.5f;
            try
            {
                if (!IsMapWindow(w)) { st.Set = st.MenuDone = true; return; }
                var dc = Get(w, "DataContext");
                if (dc == null || dc.GetType().Name != "SceneMapViewModel") {
                    st.Set = st.MenuDone = true; _wvWin = new WeakReference(w);
                    return;
                }   // the World View window
                if (!st.Set)
                {
                    _mapWin = new WeakReference(w);
                    st.Set = true;
                    if (GetB(dc, "IsFullscreen")) SetV(dc, "IsFullscreen", false);   // the window starts as a window
                    Trace("map: Tactical Display is a window (WindowedMapAndWorldView)");
                    // the game re-reads ShowPip3D and creates the World View window
                    var main = MainVm();
                    if (main != null)
                    {
                        var m = main.GetType().GetMethod("NotifyPipChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (m != null) m.Invoke(main, null);
                    }
                }
                if (!st.MenuDone)
                {
                    // the menu item "World View: open / close" of the window's menu is enabled by the game only while the map is maximized (IsEnabled = IsFullscreen)
                    if (EnableWorldViewItem(w)) { st.MenuDone = true; Trace("map: the World View item of the Tactical Display's menu is enabled"); }
                    else if (++st.MenuTries >= 20) { st.MenuDone = true; Trace("map: the World View item of the Tactical Display's menu not found"); }
                }
            }
            catch (Exception e) { st.Set = st.MenuDone = true; Trace("MapModeStep: " + e.Message); }
        }

        /// <summary>Finds the menu item whose IsEnabled is bound to the window's IsFullscreen and frees it from that binding.</summary>
        bool EnableWorldViewItem(object w)
        {
            if (!InitNoesis()) return false;
            var enabledProp = _uiElementType.GetProperty("IsEnabledProperty", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
            return FindMenuItem(w, enabledProp, 0);
        }

        bool FindMenuItem(object e, object enabledProp, int depth)
        {
            if (e == null || depth > 60) return false;
            if (e.GetType().Name == "MenuItem" && GetB(e, "IsCheckable"))
            {
                var bnd = Invoke(_getBinding, null, e, enabledProp);
                if (bnd != null && bnd != Failed)
                {
                    string path = "";
                    try { var pp = Get(bnd, "Path"); path = pp == null ? "" : (Get(pp, "Path") as string ?? pp.ToString()); } catch { }
                    if (path.IndexOf("IsFullscreen", StringComparison.Ordinal) >= 0)
                    {
                        Invoke(_clearBinding, null, e, enabledProp);
                        SetV(e, "IsEnabled", true);
                        return true;
                    }
                }
            }
            var items = e.GetType().GetProperty("Items") == null ? null : Get(e, "Items") as System.Collections.IEnumerable;
            if (items != null && (e.GetType().Name == "MenuItem" || e.GetType().Name == "Menu"))
                foreach (var it in items) if (FindMenuItem(it, enabledProp, depth + 1)) return true;
            for (int k = 0, n = VtChildrenCount(e); k < n; k++)
                if (FindMenuItem(VtChild(e, k), enabledProp, depth + 1)) return true;
            return false;
        }
    }
}
