using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// WindowedMapAndWorldView: the optical view's frame (SeapowerUI.OpticalOverlayView) in the World View window. The game's window template
    /// (WorldViewWindowView.xaml) puts the overlay, laid out for the whole screen (UiRootWidth x UiRootHeight), into a Viewbox with Stretch=UniformToFill, so in a window of another
    /// shape the frame is cropped (its centre, the crosshair and the eyepieces are cut off). With the option on the Viewbox is switched to Uniform: the whole frame is fitted into the
    /// window's picture, centred, and black bars (rectangles in the window's content grid) cover the rest of the picture. Nothing is polled: the overlay is caught by its constructor,
    /// and size/visibility changes (events) refit it. With the option off the game's layout is restored.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        sealed class BinoItem { public WeakReference View; public bool InBox; public Rectangle[] Bars; public bool Hooked, Traced, Fitted; public int Logged; public float Bx, By, K = 1f, Cx, Cy; public object Compass; }
        static readonly List<BinoItem> _bino = new List<BinoItem>();
        static WeakReference _binoVm;

        ConfigEntry<float> _binoDown;

        void BindBinocularConfig()
        {
            _binoDown = Config.Bind("Camera", "BinocularsBelowHorizon", 10f,
                "Ship binoculars (the optical view of a vessel): how far below the horizon they can be lowered, in degrees. The game stops them at the horizon. 0 = as in the game.");
        }

        static FieldInfo _bpObject; static PropertyInfo _bpMode; static Type _bpVessel;

        // the game's constraint of the ship binoculars (pitch -90..0, i.e. up to the horizon) is widened to the horizon + the option's degrees (a positive pitch looks down)
        static void BinoPitchPre(ref float pitchAngle, out float __state) { __state = pitchAngle; }

        static void BinoPitchPost(object __instance, ref float pitchAngle, float __state)
        {
            try
            {
                var a = Active; if ((object)a == null || a._binoDown == null) return;
                float down = a._binoDown.Value;
                if (down <= 0f) return;
                var t = __instance.GetType();
                if (_bpObject == null) { _bpObject = t.GetField("_opticalViewObject", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); _bpMode = t.GetProperty("CurrentOpticalViewMode"); _bpVessel = t.Assembly.GetType("SeaPower.Vessel"); }
                if (_bpObject == null || _bpMode == null || _bpVessel == null) return;
                var obj = _bpObject.GetValue(__instance);
                if (obj != null && _bpVessel.IsInstanceOfType(obj) && "Binoculars".Equals(_bpMode.GetValue(__instance, null).ToString()))
                    pitchAngle = Math.Max(-90f, Math.Min(down, __state));
            }
            catch { }
        }

        void HookBinocularFrame(Harmony harmony, Assembly asm)
        {
            var cmType = asm.GetType("SeaPower.CameraManager");
            var constraint = cmType == null ? null : cmType.GetMethod("ApplyOpticalRotationConstraint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (constraint != null) { TryPatch(harmony, constraint, "BinoPitchPre", "BinoPitchPost", null); Trace("patched CameraManager.ApplyOpticalRotationConstraint (BinocularsBelowHorizon)"); }
            else Trace("ApplyOpticalRotationConstraint not found: BinocularsBelowHorizon not available");
            var t = asm.GetType("SeapowerUI.OpticalOverlayView");
            var c = t == null ? null : t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (c == null) { Trace("OpticalOverlayView not found: the binocular frame stays as the game draws it"); return; }
            TryPatch(harmony, c, null, "BinoCtorPostfix", null);
            if (_windowedMap != null) _windowedMap.SettingChanged += (s, e) => { BinoFitAll("option"); PhotoLayers(); };
            Trace("patched OpticalOverlayView constructor (binocular frame in the World View window)");
        }

        static void BinoCtorPostfix(object __instance)
        {
            try
            {
                var fe = __instance as FrameworkElement;
                if (fe == null) return;
                var it = new BinoItem { View = new WeakReference(fe) };
                lock (_bino) { _bino.RemoveAll(b => b.View.Target == null); _bino.Add(it); }
                fe.Loaded += (s, e) => { var a = Active; if ((object)a != null) a.BinoLoaded(it); };
            }
            catch { }
        }

        // the overlay is in the tree: the one in the World View window has a Viewbox above it
        void BinoLoaded(BinoItem it)
        {
            try
            {
                var v = it.View.Target as FrameworkElement;
                if (v == null || it.Hooked) { BinoFit(it, "loaded again"); return; }
                Viewbox box = null;
                int n = 0;
                for (var p = VisualTreeHelper.GetParent(v); p != null && n < 6 && box == null; p = VisualTreeHelper.GetParent(p), n++) box = p as Viewbox;
                if (box == null) { it.Hooked = true; return; }           // the full-screen one: as the game lays it out
                it.Hooked = true; it.InBox = true;
                var grid = VisualTreeHelper.GetParent(box) as Grid;
                if (grid != null) grid.SizeChanged += (s, e) => BinoFit(it, "window picture size");
                box.SizeChanged += (s, e) => BinoFit(it, "viewbox size");
                box.IsVisibleChanged += (s, e) => BinoFit(it, "viewbox visible");
                v.SizeChanged += (s, e) => BinoFit(it, "overlay size");
                v.IsVisibleChanged += (s, e) => BinoFit(it, "overlay visible");
                var vm = MainVm(); var inpc = vm as System.ComponentModel.INotifyPropertyChanged;
                if (inpc != null && (_binoVm == null || !ReferenceEquals(_binoVm.Target, vm)))
                {
                    _binoVm = new WeakReference(vm);      // the optics switched on/off: the layout follows in the same frame, this fits what is known by then
                    inpc.PropertyChanged += (s, e) => { if (e.PropertyName == "IsOpticalViewActive" || e.PropertyName == "ShowPipOpticalOverlay" || e.PropertyName == "ShowFullscreenOpticalOverlay") { var a = Active; if ((object)a != null) a.BinoState(e.PropertyName); } else if (e.PropertyName == "IsHudVisible") { var a = Active; if ((object)a != null) a.PhotoLayers(); } };
                }
                if (!it.Traced) { it.Traced = true; Trace("binocular frame: " + BinoDescribe(v)); }
                BinoFit(it, "loaded");
            }
            catch (Exception e) { Trace("BinoLoaded: " + e.Message); }
        }

        /// <summary>
        /// Backspace (the game's photo mode, HUD hidden): the game's flags that depend on the World View window follow the HUD, so that the optical view fills the screen like in the game
        /// (ShowPip3D is not forced then, see MapShowPipPostfix: the World View window is hidden and the full-screen overlay is shown) and the windows layer of the main view is hidden
        /// (with the optics on the game switches its UI camera on, and the windows would show over the picture). The HUD back: everything as it was. Event driven (IsHudVisible).
        /// </summary>
        void PhotoLayers()
        {
            try
            {
                var vm = MainVm();
                if (vm == null) return;
                bool photo = PhotoMode();
                var view = MainGameViewInstance() as FrameworkElement;
                var layer = view == null ? null : view.FindName("OverlayCanvasNew") as UIElement;
                if (layer != null)
                {
                    var want = photo ? Visibility.Collapsed : Visibility.Visible;
                    if (layer.Visibility != want) { layer.Visibility = want; Trace(photo ? "photo mode: the windows layer hidden" : "photo mode over: the windows layer shown"); }
                }
                var notify = vm.GetType().GetMethod("NotifyPropertyChanged", new[] { typeof(string) });
                if (notify != null)
                    foreach (var n in new[] { "ShowPip3D", "HideHudForOpticalView", "ShowFullscreenOpticalOverlay", "ShowPipOpticalOverlay", "ShowDetachedOpticalOverlay" })
                        notify.Invoke(vm, new object[] { n });
            }
            catch (Exception e) { Trace("PhotoLayers: " + e.Message); }
        }

        /// <summary>The sensor bearing block (the 140x140 compass in the bottom right corner of the overlay) is moved towards the centre by what the window cuts off, so that it keeps its distance from the visible edge.</summary>
        void BinoCompass(BinoItem it, FrameworkElement v, float cutR, float cutB)
        {
            it.Cx = cutR; it.Cy = cutB;
            if (it.Compass == null && (cutR > 0f || cutB > 0f))
            {
                it.Compass = BinoFindCompass(v, 0);
                if (it.Compass == null) Trace("binocular frame: the sensor bearing block was not found");
            }
            var c = it.Compass as FrameworkElement;
            if (c == null) return;
            if (cutR <= 0f && cutB <= 0f) c.ClearValue(UIElement.RenderTransformProperty);
            else c.RenderTransform = new TranslateTransform(-cutR, -cutB);
        }

        object BinoFindCompass(object e, int depth)
        {
            if (e == null || depth > 6) return null;
            var fe = e as FrameworkElement;
            if (fe is Grid && fe.Width == 140f && fe.Height == 140f && fe.HorizontalAlignment == HorizontalAlignment.Right && fe.VerticalAlignment == VerticalAlignment.Bottom) return fe;
            for (int k = 0, n = VtChildrenCount(e); k < n; k++)
            {
                var f = BinoFindCompass(VtChild(e, k), depth + 1);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>The most the frame and the interface are enlarged (over the whole-frame fit) to make the black bars of a disproportionate window smaller.</summary>
        const float BinoMaxEnlargeWide = 1.30f, BinoMaxEnlargeTall = 1.30f;      // black bars at the sides (a wide window) / above and below (a tall window)

        static Viewbox BinoViewbox(FrameworkElement v)
        {
            int n = 0;
            for (var p = VisualTreeHelper.GetParent(v); p != null && n < 6; p = VisualTreeHelper.GetParent(p), n++)
                if (p is Viewbox) return (Viewbox)p;
            return null;
        }

        void BinoFit(BinoItem it, string why)
        {
            try
            {
                var v = it.View.Target as FrameworkElement;
                if (v == null) return;
                // the Viewbox and the grid are native Noesis types: a managed wrapper held only by a WeakReference is gone after a while, so they are looked up from the overlay each time
                Viewbox box = BinoViewbox(v);
                var g = box == null ? null : VisualTreeHelper.GetParent(box) as Grid;
                if (box == null) return;
                float w = g == null ? 0f : g.ActualWidth, h = g == null ? 0f : g.ActualHeight, cw = v.ActualWidth, ch = v.ActualHeight;
                bool fit = MapOn() && g != null && w > 0f && h > 0f && cw > 0f && ch > 0f;
                if (!fit)
                {
                    if (it.Logged < 6 && GetB(MainVm(), "IsOpticalViewActive")) { it.Logged++; Trace("binocular frame (" + why + "): not fitted - option " + MapOn() + ", window grid " + (g == null ? "none" : w.ToString("0.#") + "x" + h.ToString("0.#")) + ", overlay " + cw.ToString("0.#") + "x" + ch.ToString("0.#")); }
                    if (it.Fitted) { it.Fitted = false; it.K = 1f; BinoCompass(it, v, 0f, 0f); box.Stretch = Stretch.UniformToFill; v.ClearValue(UIElement.RenderTransformProperty); BinoBars(it, g, 0f, 0f, 0f, 0f, false); Trace("binocular frame: as the game lays it out"); }
                    return;
                }
                float sFit = Math.Min(w / cw, h / ch), sFill = Math.Max(w / cw, h / ch);
                float k = Math.Min(w / cw > h / ch ? BinoMaxEnlargeWide : BinoMaxEnlargeTall, sFill / sFit);       // the frame and the interface are enlarged to cover the disproportion, up to BinoMaxEnlarge; what does not fit is cut off by the window
                float s = sFit * k;
                float bx = Math.Max(0f, w - cw * s) / 2f, by = Math.Max(0f, h - ch * s) / 2f;
                // what the window cuts off at the right and at the bottom (in the overlay's own units): the sensor bearing block, which sits in the bottom right corner, is moved in by that much
                float cutR = Math.Max(0f, cw / 2f - w / (2f * s)), cutB = Math.Max(0f, ch / 2f - h / (2f * s));
                if (it.Fitted && Math.Abs(bx - it.Bx) < 0.01f && Math.Abs(by - it.By) < 0.01f && Math.Abs(k - it.K) < 0.0005f && Math.Abs(cutR - it.Cx) < 0.01f && Math.Abs(cutB - it.Cy) < 0.01f) { BinoBars(it, g, bx, by, 0f, 0f, v.IsVisible); return; }
                it.Fitted = true; it.Bx = bx; it.By = by; it.K = k;
                BinoCompass(it, v, cutR, cutB);
                box.Stretch = Stretch.Uniform;
                if (k > 1.0005f) { v.RenderTransformOrigin = new Noesis.Point(0.5f, 0.5f); v.RenderTransform = new ScaleTransform(k, k); }
                else v.ClearValue(UIElement.RenderTransformProperty);
                BinoBars(it, g, bx, by, 0f, 0f, v.IsVisible);
                Trace("binocular frame: fitted into the window picture " + w.ToString("0.#") + "x" + h.ToString("0.#") + " (overlay " + cw.ToString("0.#") + "x" + ch.ToString("0.#") + ", scale " + s.ToString("0.###") + ", enlarged x" + k.ToString("0.###") + ", black bars " + bx.ToString("0.#") + " x " + by.ToString("0.#") + ", cut off " + cutR.ToString("0.#") + " x " + cutB.ToString("0.#") + ")");
            }
            catch (Exception e) { Trace("BinoFit: " + e.Message); }
        }

        /// <summary>The black bars beside/above the fitted frame (the picture shows the world there otherwise): four rectangles in the window's content grid above the Viewbox.</summary>
        void BinoBars(BinoItem it, Grid g, float bx, float by, float unused1, float unused2, bool show)
        {
            if (g == null) return;
            if (it.Bars == null)
            {
                it.Bars = new Rectangle[4];
                for (int i = 0; i < 4; i++)
                {
                    var r = new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 0, 0)), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                    r.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == 1 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
                    r.VerticalAlignment = i == 2 ? VerticalAlignment.Top : i == 3 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
                    g.Children.Add(r); it.Bars[i] = r;
                }
            }
            it.Bars[0].Width = it.Bars[1].Width = bx; it.Bars[2].Height = it.Bars[3].Height = by;
            it.Bars[0].Visibility = it.Bars[1].Visibility = show && bx > 0.5f ? Visibility.Visible : Visibility.Collapsed;
            it.Bars[2].Visibility = it.Bars[3].Visibility = show && by > 0.5f ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The optics flags changed: the layout follows in the same frame, so the frame is fitted with what is known by then.</summary>
        void BinoState(string name)
        {
            try
            {
                BinoFitAll("optics " + name);
            }
            catch (Exception e) { Trace("BinoState: " + e.Message); }
        }

        void BinoFitAll(string why)
        {
            List<BinoItem> items; lock (_bino) items = new List<BinoItem>(_bino);
            foreach (var it in items) BinoFit(it, why);
        }

        /// <summary>For the log: the overlay's place in the tree and its size.</summary>
        static string BinoDescribe(FrameworkElement v)
        {
            var sb = new StringBuilder();
            for (DependencyObject p = v; p != null; p = VisualTreeHelper.GetParent(p))
            {
                var fe = p as FrameworkElement;
                sb.Append(p.GetType().Name);
                if (fe != null) sb.Append('(').Append(fe.ActualWidth.ToString("0.#")).Append('x').Append(fe.ActualHeight.ToString("0.#")).Append(')');
                sb.Append(" < ");
            }
            return sb.ToString();
        }
    }
}
