using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [Map] CompactSonobuoys / SonobuoysThreshold: with many sonobuoys on the map at once the icons get half the size and their text is removed; with fewer
    /// (zoomed in) they are shown as usual. The count is that of the sonobuoy icons that lie inside the map of a map window and are not hidden by the game.
    /// Mechanics: the icon of a unit is the Canvas "UnitIcon" of the game's UnitOverlay.xaml template (DataContext = MapUnitViewModel, VisualType Passive/ActiveSonobouy);
    /// its RenderTransform (a translate by -11.25) gets a scale of 0.5 about the icon centre in front of it. The text lines of the label are the ContactInfoLine1..4 properties
    /// of the same view model: their getters return "" while the unit is flagged (and PropertyChanged is raised for them when the flag changes).
    /// The step is run twice a second per map window; with a hysteresis of 10% so that the state does not flicker on the border.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _sonoOn;
        ConfigEntry<float> _sonoMax;

        sealed class SonoWinState
        {
            public float Next, WalkNext; public bool Crowded, LayerOff; public int Count, Found;
            public readonly List<WeakReference> Icons = new List<WeakReference>();   // the sonobuoy icons of the last walk of the tree (the tree is walked every two seconds, the icons counted every half)
            public Noesis.FrameworkElement Map;
        }
        sealed class SonoIconState { public bool Small; public Noesis.Transform Orig; }
        sealed class SonoFlag { public bool Hide; }

        static readonly ConditionalWeakTable<object, SonoWinState> SonoWins = new ConditionalWeakTable<object, SonoWinState>();
        static readonly ConditionalWeakTable<object, SonoIconState> SonoIcons = new ConditionalWeakTable<object, SonoIconState>();
        static readonly ConditionalWeakTable<object, SonoFlag> SonoFlags = new ConditionalWeakTable<object, SonoFlag>();
        static MethodInfo _sonoRaise;
        static FieldInfo _sonoLabelF;
        static MethodInfo _sonoBump;
        static bool _sonoHooked;
        static int _sonoCrowdWins;                                // map windows in the compact state now
        static int _sonoLayerWins;                                // map windows whose layer 'Weapon Sonobuoys' is off now (the game leaves the text of the hidden sonobuoys)
        static PropertyInfo _sonoDisplayStateP; static MethodInfo _sonoLayerVisibleM; static bool _sonoLayerLooked;
        static int _sonoHidden;                                   // how many view models are flagged now: 0 = the getters are not touched at all
        static readonly List<Noesis.Canvas> _sonoIcons = new List<Noesis.Canvas>();
        static readonly Stack<Noesis.DependencyObject> _sonoStack = new Stack<Noesis.DependencyObject>();

        void BindSonobuoyConfig()
        {
            _sonoOn = Config.Bind("Map", "CompactSonobuoys", true,
                "Map: with many sonobuoys visible on the map at once their icons are half the size and their text is removed (until you zoom in and fewer are left).");
            _sonoMax = Config.Bind("Map", "SonobuoysThreshold", 20f,
                new ConfigDescription("Compact sonobuoys: how many sonobuoys visible on the map at once switch the compact look on (it is switched off again below 90% of it).",
                    new AcceptableValueRange<float>(5f, 300f)));
        }

        void HookSonobuoys(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type vm = null;
            foreach (var t in SafeTypes(asm)) if (t.Name == "MapUnitViewModel") { vm = t; break; }
            if (vm == null) { Trace("sonobuoys: MapUnitViewModel not found (CompactSonobuoys not available)"); return; }
            _sonoRaise = vm.GetMethod("OnPropertyChanged", all, null, new[] { typeof(string) }, null);
            if (_sonoRaise == null) { Trace("sonobuoys: OnPropertyChanged not found (CompactSonobuoys not available)"); return; }
            _sonoLabelF = vm.GetField("Label", all);
            if (_sonoLabelF != null) _sonoBump = _sonoLabelF.FieldType.GetMethod("BumpContentVersion", all);
            if (_sonoBump == null) Trace("sonobuoys: Label.BumpContentVersion not found (text may come back late)");
            int n = 0;
            for (int i = 1; i <= 4; i++)
            {
                var p = vm.GetProperty("ContactInfoLine" + i, all);
                var g = p == null ? null : p.GetGetMethod(true);
                if (g != null) { TryPatch(harmony, g, null, "SonoLinePostfix", null); n++; }
            }
            _sonoHooked = n == 4;
            Trace("patched MapUnitViewModel.ContactInfoLine1-4 getters (CompactSonobuoys): " + n);
        }

        static void SonoLinePostfix(object __instance, MethodBase __originalMethod, ref string __result)
        {
            if (__result != null && _glHooked && (__result.IndexOf('\n') >= 0 || __result.IndexOf('\r') >= 0) && MapImp) __result = GlNoEmptyLines(__result);      // an empty line inside the text of a label line
            if (_sonoHidden <= 0) return;                    // nothing is compact: no lookup per call (the getters are read for every unit all the time)
            SonoFlag f;
            if (SonoFlags.TryGetValue(__instance, out f) && f.Hide) __result = "";
        }

        static bool IsSonobuoyVm(object dc)
        {
            if (dc == null || dc.GetType().Name != "MapUnitViewModel") return false;
            var vt = FieldV(dc, "VisualType");
            if (vt == null) return false;
            var s = vt.ToString();
            return s == "PassiveSonobouy" || s == "ActiveSonobouy";
        }

        static bool IsTypeNamed(object o, string name)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType) if (t.Name == name) return true;
            return false;
        }

        /// <summary>Twice a second per map window: counts the sonobuoy icons on the map, switches the compact look on / off.</summary>
        void SonobuoyStep(object w, float now)
        {
            if (!_sonoHooked || _sonoOn == null) return;
            var root = w as Noesis.DependencyObject;
            if (root == null) return;
            SonoWinState st;
            if (!SonoWins.TryGetValue(w, out st)) { st = new SonoWinState(); SonoWins.Add(w, st); }
            if (now < st.Next) return;
            if (!IsMapWindow(w)) { st.Next = now + 5f; return; }       // not a map window (the header may not be set yet: asked again)
            st.Next = now + 0.5f;
            if (!(_sonoOn.Value && MapImp) && !st.Crowded && !st.LayerOff) return;
            try
            {
                // the tree is walked every two seconds (it is the expensive part); between the walks the icons of the last walk are counted
                if (now >= st.WalkNext)
                {
                    st.WalkNext = now + 2f;
                    SonoWalk(root, st);
                }
                if (st.Icons.Count != st.Found) { st.Found = st.Icons.Count; Trace("sonobuoys: " + st.Found + " icons in the map window (compact above " + Math.Max(1f, _sonoMax.Value) + " visible on the map)"); }
                if (st.Icons.Count == 0 && !st.Crowded && !st.LayerOff) { st.Count = 0; return; }

                var icons = _sonoIcons;
                icons.Clear();
                for (int i = st.Icons.Count - 1; i >= 0; i--)
                {
                    var c = st.Icons[i].Target as Noesis.Canvas;
                    if (c == null || Noesis.VisualTreeHelper.GetParent(c) == null) { st.Icons.RemoveAt(i); continue; }     // gone from the tree
                    icons.Add(c);
                }
                var map = st.Map;

                // the visible ones: inside the map, not collapsed by the game
                float x0 = float.MinValue, y0 = float.MinValue, x1 = float.MaxValue, y1 = float.MaxValue;
                if (map != null && map.ActualWidth > 1f && map.ActualHeight > 1f)
                {
                    var a = map.PointToScreen(new Noesis.Point(0f, 0f)); var b = map.PointToScreen(new Noesis.Point(map.ActualWidth, map.ActualHeight));
                    x0 = Math.Min(a.X, b.X); x1 = Math.Max(a.X, b.X); y0 = Math.Min(a.Y, b.Y); y1 = Math.Max(a.Y, b.Y);
                }
                int count = 0;
                foreach (var c in icons)
                {
                    if (!c.IsVisible) continue;
                    var p = c.PointToScreen(new Noesis.Point(11.25f, 11.25f));
                    if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1) count++;
                }
                st.Count = count;
                float thr = Math.Max(1f, _sonoMax.Value);
                bool crowded = _sonoOn.Value && MapImp && (st.Crowded ? count > thr * 0.9f : count > thr);
                if (crowded != st.Crowded) { st.Crowded = crowded; _sonoCrowdWins += crowded ? 1 : -1; Trace("sonobuoys: " + count + " on the map, " + (crowded ? "compact" : "usual")); }
                // the layer 'Weapon Sonobuoys' (Map Layers) is off: the game hides the icons, the text of their labels stays - it goes too
                bool layerOff = _sonoOn.Value && MapImp && SonoLayerOff(map);
                if (layerOff != st.LayerOff) { st.LayerOff = layerOff; _sonoLayerWins += layerOff ? 1 : -1; Trace("sonobuoys: the layer is " + (layerOff ? "off, their text is removed" : "on again")); }
                foreach (var c in icons) SonoApply(c, crowded, crowded || layerOff);
                icons.Clear();
                // usual everywhere: every live unit has its text back; the flags of units that left the map meanwhile were never taken off - the count starts again
                if (!crowded && !layerOff && _sonoCrowdWins <= 0 && _sonoLayerWins <= 0) { _sonoCrowdWins = 0; _sonoLayerWins = 0; _sonoHidden = 0; }
            }
            catch (Exception e) { st.Next = now + 5f; Trace("sonobuoys: " + e.Message); }
        }

        /// <summary>The layer 'Weapon Sonobuoys' (number 23) of the map's display state is switched off (MapBase.DisplayState.IsLayerVisible).</summary>
        static bool SonoLayerOff(Noesis.FrameworkElement map)
        {
            if (map == null) return false;
            try
            {
                if (!_sonoLayerLooked)
                {
                    _sonoLayerLooked = true;
                    const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    _sonoDisplayStateP = map.GetType().GetProperty("DisplayState", all);
                    var ds = _sonoDisplayStateP == null ? null : _sonoDisplayStateP.PropertyType;
                    _sonoLayerVisibleM = ds == null ? null : ds.GetMethod("IsLayerVisible", all, null, new[] { typeof(int) }, null);
                    if (_sonoLayerVisibleM == null) Trace("sonobuoys: MapBase.DisplayState.IsLayerVisible not found (the text of hidden sonobuoys stays)");
                }
                if (_sonoDisplayStateP == null || _sonoLayerVisibleM == null) return false;
                var state = _sonoDisplayStateP.GetValue(map, null);
                return state != null && !(bool)_sonoLayerVisibleM.Invoke(state, new object[] { 23 });
            }
            catch { return false; }
        }

        /// <summary>Walks the visual tree of the window once: the sonobuoy icons (weakly kept) and the map control.</summary>
        static void SonoWalk(Noesis.DependencyObject root, SonoWinState st)
        {
            st.Icons.Clear();
            st.Map = null;
            var stack = _sonoStack;
            stack.Clear();
            stack.Push(root);
            int budget = 80000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                var c = e as Noesis.Canvas;
                if (c != null && c.Name == "UnitIcon" && IsSonobuoyVm(c.DataContext)) { st.Icons.Add(new WeakReference(c)); continue; }
                if (st.Map == null && e is Noesis.FrameworkElement && IsTypeNamed(e, "MapBase")) st.Map = (Noesis.FrameworkElement)e;
                int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                for (int k = 0; k < n; k++) { var ch = Noesis.VisualTreeHelper.GetChild(e, k); if (ch != null) stack.Push(ch); }
            }
            stack.Clear();
        }

        /// <summary>The icon (small) and the text (hide) of one unit for the state (only what differs from the state of this icon is touched).</summary>
        static void SonoApply(Noesis.Canvas c, bool small, bool hide)
        {
            SonoIconState s;
            if (!SonoIcons.TryGetValue(c, out s)) { s = new SonoIconState(); SonoIcons.Add(c, s); }
            if (s.Small != small)
            {
                s.Small = small;
                if (small)
                {
                    s.Orig = c.RenderTransform;
                    var g = new Noesis.TransformGroup();
                    g.Children.Add(new Noesis.ScaleTransform { ScaleX = 0.5f, ScaleY = 0.5f, CenterX = 11.25f, CenterY = 11.25f });
                    if (s.Orig != null) g.Children.Add(s.Orig); else g.Children.Add(new Noesis.TranslateTransform { X = -11.25f, Y = -11.25f });
                    c.RenderTransform = g;
                }
                else
                {
                    c.RenderTransform = s.Orig;
                    s.Orig = null;
                }
            }
            var vm = c.DataContext;
            if (vm == null) return;
            SonoFlag f;
            if (!SonoFlags.TryGetValue(vm, out f)) { f = new SonoFlag(); SonoFlags.Add(vm, f); }
            if (f.Hide == hide) return;
            f.Hide = hide;
            _sonoHidden += hide ? 1 : -1;
            for (int i = 1; i <= 4; i++) _sonoRaise.Invoke(vm, new object[] { "ContactInfoLine" + i });   // the bindings read the lines again
            try
            {
                var lab = _sonoLabelF == null ? null : _sonoLabelF.GetValue(vm);
                if (lab != null && _sonoBump != null) _sonoBump.Invoke(lab, null);              // the label solver measures the label again
            }
            catch { }
        }
    }
}
