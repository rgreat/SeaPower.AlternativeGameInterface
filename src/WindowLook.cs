using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// WindowLookMenu (1.7.0): next to the gear in a remembered window's title bar a small arrow button opens a drop-down list with the
    /// window's opacity and scale. The values are kept per window pattern (WindowPatterns entry) in [General] WindowLooks.
    /// Opacity = the backgrounds of the window (Border / panel brushes) only; scale = the content only (the window keeps its size).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<string> _looks;
        Dictionary<string, float[]> _lookMap;   // pattern -> { opacity, scale, text outline 0/1 }

        static readonly float[] OpacitySteps = Steps(10, 0), ScaleLow = Steps(5, 12), ScaleHigh = Steps(13, 20);   // 100..0 %, 50..120 %, 130..200 %, step 10 %

        static float[] Steps(int from, int to)
        {
            var l = new List<float>();
            if (from > to) for (int i = from; i >= to; i--) l.Add(i / 10f); else for (int i = from; i <= to; i++) l.Add(i / 10f);
            return l.ToArray();
        }

        sealed class LookState
        {
            public WeakReference Panel, Button, Menu;
            public bool Applied;
            public float Opacity = 1f, Scale = 1f, Next;
            public bool Outline, Bottom;
        }
        static readonly ConditionalWeakTable<object, LookState> Looks = new ConditionalWeakTable<object, LookState>();

        void BindLookConfig()
        {
            _looks = Config.Bind("General", "WindowLooks", "",
                "Opacity and scale of the remembered windows, written by the plugin: pattern|opacity|scale|outline;...");
        }

        Dictionary<string, float[]> LookMap()
        {
            if (_lookMap != null) return _lookMap;
            _lookMap = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in (_looks.Value ?? "").Split(';'))
            {
                var p = rec.Split('|');
                float o, s, ol = 0f;
                if ((p.Length == 3 || (p.Length == 4 && float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out ol))) && !string.IsNullOrWhiteSpace(p[0]) &&
                    float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out o) &&
                    float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out s))
                    _lookMap[p[0].Trim()] = new[] { Clamp(o, 0f, 1f), Clamp(s, 0.5f, 2f), ol > 0.5f ? 1f : 0f };
            }
            return _lookMap;
        }

        // ---- window level: -1 below the others, 0 as the game has it (default), 1 above; per window pattern, [General] WindowLevels
        ConfigEntry<string> _levels;
        Dictionary<string, int> _levelMap;

        Dictionary<string, int> LevelMap()
        {
            if (_levelMap != null) return _levelMap;
            _levelMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (_levels == null) _levels = Config.Bind("General", "WindowLevels", "Tactical Display|-1;World View|-1", "Level of the remembered windows (-1 below the others, 1 above), written by the plugin: pattern|level;... The Tactical Display and the World View are below the others by default.");
            foreach (var rec in (_levels.Value ?? "").Split(';'))
            {
                var p = rec.Split('|'); int l;
                if (p.Length == 2 && !string.IsNullOrWhiteSpace(p[0]) && int.TryParse(p[1], out l) && l != 0) _levelMap[p[0].Trim()] = l < 0 ? -1 : 1;
            }
            return _levelMap;
        }

        int LevelOf(object w)
        {
            try
            {
                string label, key = MatchPattern(w, out label); int l;
                return key != null && LevelMap().TryGetValue(key, out l) ? l : 0;
            }
            catch { return 0; }
        }

        void SetLevel(object w, int level)
        {
            string label, key = MatchPattern(w, out label);
            if (key == null) return;
            var m = LevelMap();
            if (level == 0) m.Remove(key); else m[key] = level;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in m) { if (sb.Length > 0) sb.Append(';'); sb.Append(kv.Key).Append('|').Append(kv.Value); }
            _levels.Value = sb.ToString();
            BringToFront(w, "level", false);
        }

        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

        void SaveLooks()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in LookMap())
            {
                if (Math.Abs(kv.Value[0] - 1f) < 0.001f && Math.Abs(kv.Value[1] - 1f) < 0.001f && kv.Value[2] < 0.5f) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('|').Append(kv.Value[0].ToString("0.##", CultureInfo.InvariantCulture))
                  .Append('|').Append(kv.Value[1].ToString("0.##", CultureInfo.InvariantCulture))
                  .Append('|').Append(kv.Value[2] > 0.5f ? '1' : '0');
            }
            _looks.Value = sb.ToString();
        }

        float[] LookOf(object w, bool stored = false)
        {
            // the fullscreen Tactical Display / World View: no opacity, scale or outline (the stored values stay for the window mode)
            if (!stored && IsBottomWindow(w)) return new[] { 1f, 1f, 0f };
            string label, key = MatchPattern(w, out label);
            float[] v;
            if (key == null || !LookMap().TryGetValue(key, out v)) return new[] { 1f, 1f, 0f };
            if (!stored && NoScale(w))
            {
                // WindowedMapAndWorldView: the window shows a texture drawn texel for pixel: no opacity (an off-screen group would resample it), scale or outline
                if (MapOn()) return new[] { 1f, 1f, 0f };
                if (Math.Abs(v[1] - 1f) > 0.001f) return new[] { v[0], 1f, v[2] };
            }
            return v;
        }

        /// <summary>The World View window shows a render: the scale has no meaning for it (the option is off for it).</summary>
        bool NoScale(object w)
        {
            try
            {
                if (!IsMapWindow(w)) return false;
                var dc = Get(w, "DataContext");
                return dc != null && dc.GetType().Name != "SceneMapViewModel";
            }
            catch { return false; }
        }

        /// <summary>Applies the window's remembered opacity / scale: opacity = the backgrounds (Border / panel brushes) of the window get
        /// more transparent, text and controls stay; scale = a LayoutTransform on the presenter of the window's content, so the window
        /// keeps its size and only what is in it grows or shrinks (the layout is redone for the scaled space).</summary>
        void ApplyLook(object w, Panel panel)
        {
            if (w == null || panel == null) return;
            var st = Looks.GetOrCreateValue(w);
            var look = LookOf(w);
            st.Panel = new WeakReference(panel);
            st.Opacity = look[0]; st.Scale = look[1]; st.Outline = look[2] > 0.5f; st.Bottom = IsBottomWindow(w); st.Applied = true;
            st.Next = UnityEngine.Time.unscaledTime + 1f;
            var menu = st.Menu == null ? null : st.Menu.Target;
            _bigArea = Math.Max(1f, panel.ActualWidth * panel.ActualHeight * 0.15f);
            int budget = 6000;
            // windows with a render of their own (the map, the 3D view): only the render takes the opacity, in full; the backgrounds under it become fully transparent (under the 3D view they stay: it would show the main scene), the rest (title bar, frame) is dimmed as usual
            bool render = look[0] < 0.999f && HasRender(panel, ref budget);
            float lf = render ? look[0] : SolveFactor(panel, look[0], menu);   // render window: the rest (title bar, frame) as single layers
            budget = 6000;
            // the title bar never gets below 30 % (without it the window cannot be seen / grabbed any more)
            _titleFactor = look[0] >= 0.3f ? lf : (render ? 0.3f : SolveFactor(panel, 0.3f, menu));
            try
            {
                var a0 = panel.PointToScreen(new Point(0f, 0f)); var b0 = panel.PointToScreen(new Point(100f, 0f));
                _lkK = (float)Math.Sqrt((b0.X - a0.X) * (b0.X - a0.X) + (b0.Y - a0.Y) * (b0.Y - a0.Y)) / 100f; _lkTop = a0.Y; _lkPanel = panel;
            }
            catch { _lkK = 0f; _lkPanel = null; }
            DimWalk(panel, render ? look[0] : lf, lf, render, false, menu, ref budget);   // under the 3D view too: what really lies under the window shows through
            int tb = 6000;
            OutlineWalk(panel, look[2] > 0.5f && look[0] < 0.999f, menu, ref tb);
            var cp = FindContentPresenter(w, panel);
            if (cp != null)
            {
                var cur = cp.LayoutTransform as ScaleTransform;
                if (Math.Abs(look[1] - 1f) < 0.001f) { if (cur != null) cp.LayoutTransform = null; }
                else if (cur == null || Math.Abs(cur.ScaleX - look[1]) > 0.001f) cp.LayoutTransform = new ScaleTransform(look[1], look[1]);
            }
        }

        // black halo of the texts (our own effect: recognised by its blur radius, the game's own effects stay)
        const float OutlineBlur = 1.2f;

        static bool IsOurHalo(Effect ef)
        {
            var d = ef as DropShadowEffect;
            return d != null && Math.Abs(d.BlurRadius - OutlineBlur) < 0.001f;
        }

        static bool IsDarkText(Brush b)
        {
            var sc = b as SolidColorBrush;
            if (sc == null) return false;
            float r = Convert.ToSingle(sc.Color.R), g = Convert.ToSingle(sc.Color.G), bl = Convert.ToSingle(sc.Color.B);
            if (Math.Max(r, Math.Max(g, bl)) > 1.0001f) { r /= 255f; g /= 255f; bl /= 255f; }
            return 0.299f * r + 0.587f * g + 0.114f * bl < 0.5f;
        }

        void OutlineWalk(DependencyObject e, bool on, object skip, ref int budget)
        {
            if (ReferenceEquals(e, skip) || --budget < 0) return;
            var t = e as TextBlock;
            if (t != null)
            {
                try
                {
                    if (on)
                    {
                        bool darkText = IsDarkText(t.Foreground);
                        var cur = t.Effect;
                        // dark text: a white halo, light text: a black one
                        if (cur == null || (IsOurHalo(cur) && (Convert.ToSingle(((DropShadowEffect)cur).Color.R) > 127f) != darkText))
                            t.Effect = new DropShadowEffect { Color = darkText ? Color.FromRgb(255, 255, 255) : Color.FromRgb(0, 0, 0), BlurRadius = OutlineBlur, ShadowDepth = 1f, Direction = 315f, Opacity = 1f };
                    }
                    else if (IsOurHalo(t.Effect)) t.Effect = null;
                }
                catch { }
            }
            for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
            {
                var c = VisualTreeHelper.GetChild(e, k);
                if (c != null) OutlineWalk(c, on, skip, ref budget);
            }
        }

        /// <summary>The ContentPresenter that shows the window's content (the largest one with it: the title bar's buttons use the same Content).</summary>
        FrameworkElement FindContentPresenter(object w, Panel panel)
        {
            var content = Get(w, "Content");
            if (content == null) return null;
            FrameworkElement best = null; float bestH = 0f;
            var stack = new Stack<DependencyObject>();
            stack.Push(panel);
            int budget = 3000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                var cp = e as ContentPresenter;
                if (cp != null && ReferenceEquals(cp.Content, content) && cp.ActualHeight > bestH) { best = cp; bestH = cp.ActualHeight; }
                for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
                {
                    var c = VisualTreeHelper.GetChild(e, k);
                    if (c != null) stack.Push(c);
                }
            }
            return best;
        }

        // elements the game draws itself (the map's raster layer, the 3D view): their own Opacity is what can be changed
        static readonly HashSet<string> PictureTypes = new HashSet<string> { "RenderedLayer", "Pip3DView" };

        static bool IsPicture(DependencyObject e)
        {
            var fe = e as FrameworkElement;
            if (fe == null) return false;
            if ((e is Image || e is Shape) && fe.ActualWidth * fe.ActualHeight >= _bigArea) return true;
            return PictureTypes.Contains(e.GetType().Name);
        }

        static float _titleFactor = 1f, _lkK, _lkTop; static Panel _lkPanel;

        /// <summary>A background of the title bar: a strip (up to 48 units high) at the top edge of the window.</summary>
        static bool IsTitleStrip(DependencyObject e)
        {
            var fe = e as FrameworkElement;
            if (fe == null || _lkPanel == null || _lkK <= 0.01f || fe.ActualHeight > 48f || fe.ActualHeight < 8f) return false;
            try { return fe.PointToScreen(new Point(0f, 0f)).Y - _lkTop < 3f * _lkK; } catch { return false; }
        }

        static bool _lookTrace;   // set while the menu applies a choice: the factor is traced (the once-a-second refresh is silent)
        const string DimTag = "SWdim|";
        static float _bigArea = float.MaxValue;   // area (px^2) from which an Image / Shape counts as a background layer: 15 % of the window

        static bool Layered(DependencyObject e, out Brush brush)
        {
            brush = null;
            if (IsPicture(e)) return true;   // a picture / map / 3D view under the content
            var b = e as Border;
            if (b != null) brush = b.Background;
            else
            {
                var p = e as Panel;
                if (p != null) brush = p.Background;
            }
            if (brush == null) return false;
            var sc = brush as SolidColorBrush;
            if (sc != null)
            {
                float a = Convert.ToSingle(sc.Color.A);
                if (a < 0.02f) return false;   // transparent hit-test backgrounds are no layer
            }
            return true;
        }

        /// <summary>The opacity the game's own brush of a layer has: its colour's alpha times the brush's Opacity (the original one when we
        /// dimmed it before: the Tag has it).</summary>
        static float LayerAlpha(DependencyObject e, Brush brush)
        {
            float a = 1f;
            var sc = brush as SolidColorBrush;
            if (sc != null) { a = Convert.ToSingle(sc.Color.A); if (a > 1.0001f) a /= 255f; }
            float op = brush.Opacity;
            var fe = e as FrameworkElement;
            string tag = fe == null ? null : fe.Tag as string;
            if (tag != null && tag.StartsWith(DimTag))
            {
                var parts = tag.Split('|');
                float o, l;
                if (parts.Length == 3 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out o) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out l)) op = o;
            }
            return a * op;
        }

        /// <summary>The most opaque stack of backgrounds in the window (layers lie over each other: a panel in a panel in the window's
        /// background): the alphas of the layers on the path to the deepest one, and their combined opacity.</summary>
        void CollectLayers(DependencyObject e, List<float> path, List<float> best, ref float bestT, object skip, ref int budget)
        {
            if (ReferenceEquals(e, skip) || --budget < 0) return;
            Brush brush;
            bool layer = Layered(e, out brush);
            if (layer)
            {
                var fe = e as FrameworkElement;
                bool picture = brush == null || IsPicture(e) || (!(e is Border) && !(e is Panel));
                path.Add(picture ? 1f : LayerAlpha(e, brush));
                float rest = 1f;
                foreach (var a in path) rest *= 1f - a;
                if (1f - rest > bestT) { bestT = 1f - rest; best.Clear(); best.AddRange(path); }
            }
            for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
            {
                var c = VisualTreeHelper.GetChild(e, k);
                if (c != null) CollectLayers(c, path, best, ref bestT, skip, ref budget);
            }
            if (layer) path.RemoveAt(path.Count - 1);
        }

        /// <summary>The factor for every layer so that the most opaque stack shows op times its own opacity (linear in what is seen):
        /// 1 - prod(1 - a_i * f) = op * T, found by bisection.</summary>
        float SolveFactor(DependencyObject root, float op, object skip)
        {
            if (op >= 0.999f) return 1f;
            var best = new List<float>();
            float t = 0f; int budget = 6000;
            CollectLayers(root, new List<float>(), best, ref t, skip, ref budget);
            if (best.Count == 0 || t < 0.01f) return op;
            float target = op * t, lo = 0f, hi = 1f;
            for (int it = 0; it < 24; it++)
            {
                float f = (lo + hi) / 2f, rest = 1f;
                foreach (var a in best) rest *= 1f - a * f;
                if (1f - rest < target) lo = f; else hi = f;
            }
            float res = (lo + hi) / 2f;
            if (_lookTrace) Trace("look: " + best.Count + " layer(s), original opacity " + Fmt(t) + ", opacity " + Fmt(op) + " -> factor " + Fmt(res) + " per layer");
            return res;
        }

        /// <summary>A 3D view (the World View window): what lies under it stays opaque (the main scene behind the window would show through otherwise).</summary>
        static bool HasType(DependencyObject e, string name)
        {
            if (e.GetType().Name == name) return true;
            for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
            {
                var c = VisualTreeHelper.GetChild(e, k);
                if (c != null && HasType(c, name)) return true;
            }
            return false;
        }

        static bool HasRender(DependencyObject e, ref int budget)
        {
            if (--budget < 0) return false;
            if (PictureTypes.Contains(e.GetType().Name)) return true;
            for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
            {
                var c = VisualTreeHelper.GetChild(e, k);
                if (c != null && HasRender(c, ref budget)) return true;
            }
            return false;
        }

        /// <summary>Sets the factor on the layers below 'e'. In a window with a render ('render'): the render takes the opacity, the backgrounds
        /// that lie under it (the elements it is inside of) become fully transparent, the rest (frame, title bar) stays. True when 'e' holds a render.</summary>
        bool DimWalk(DependencyObject e, float pictureFactor, float factor, bool render, bool keepBacking, object skip, ref int budget)
        {
            if (ReferenceEquals(e, skip) || --budget < 0) return false;
            bool holds = PictureTypes.Contains(e.GetType().Name);
            for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
            {
                var c = VisualTreeHelper.GetChild(e, k);
                if (c != null && DimWalk(c, pictureFactor, factor, render, keepBacking, skip, ref budget)) holds = true;
            }
            Brush brush;
            bool layer = Layered(e, out brush);
            if (PictureTypes.Contains(e.GetType().Name)) SetBackground(e, pictureFactor);
            else if (render && holds) { if (layer) SetBackground(e, keepBacking ? 1f : 0f); }
            else if (layer || factor >= 0.999f) SetBackground(e, layer && _titleFactor > factor && IsTitleStrip(e) ? _titleFactor : factor);
            return holds;
        }

        /// <summary>A big Image / Shape (a map, a picture): the element's own Opacity; original and factor kept in the Tag like the brushes.</summary>
        static void SetElementOpacity(FrameworkElement fe, float factor)
        {
            string tag = fe.Tag as string;
            float origOp = 1f, last = 1f;
            bool ours = false;
            if (tag != null && tag.StartsWith(DimTag))
            {
                var parts = tag.Split('|');
                if (parts.Length == 3 &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out origOp) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out last))
                    ours = Math.Abs(fe.Opacity - origOp * last) < 0.002f;
            }
            else if (fe.Tag != null) return;
            if (!ours) { if (factor >= 0.999f) return; origOp = fe.Opacity; }
            fe.Opacity = origOp * factor;
            fe.Tag = factor >= 0.999f ? null : DimTag + origOp.ToString("0.####", CultureInfo.InvariantCulture) + "|" + factor.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Debug trace: the big elements of the window (what is under its content), to see what a picture / map / 3D view is made of.</summary>
        void DumpBig(object w, Panel panel)
        {
            float area = panel.ActualWidth * panel.ActualHeight;
            var sb = new System.Text.StringBuilder("look: big elements of " + Describe(w) + " (" + Fmt(panel.ActualWidth) + "x" + Fmt(panel.ActualHeight) + "):");
            int budget = 3000;
            Dump(panel, 0, area, sb, ref budget);
            Trace(sb.ToString());
        }

        static void Dump(DependencyObject e, int depth, float area, System.Text.StringBuilder sb, ref int budget)
        {
            if (--budget < 0 || depth > 40) return;
            var fe = e as FrameworkElement;
            if (fe != null && fe.ActualWidth * fe.ActualHeight >= area * 0.1f)
            {
                Brush br = null;
                var b = e as Border; var p = e as Panel; var sh = e as Shape;
                if (b != null) br = b.Background; else if (p != null) br = p.Background; else if (sh != null) br = sh.Fill;
                sb.Append("\n  ").Append(new string(' ', depth)).Append(e.GetType().Name).Append(' ').Append(Fmt(fe.ActualWidth)).Append('x').Append(Fmt(fe.ActualHeight))
                  .Append(" op=").Append(Fmt(fe.Opacity)).Append(br == null ? "" : " brush=" + br.GetType().Name + "/" + Fmt(br.Opacity));
            }
            for (int k = 0; k < VisualTreeHelper.GetChildrenCount(e); k++)
            {
                var c = VisualTreeHelper.GetChild(e, k);
                if (c != null) Dump(c, depth + 1, area, sb, ref budget);
            }
        }

        /// <summary>Puts the factor on the element's background: a copy of the game's brush (the game's own one is shared by every window)
        /// whose Opacity is the original's times the factor. The original Opacity and the factor are kept in the element's Tag (the elements
        /// are re-found by the tree, so nothing else can be kept for them); an element whose Tag is the game's own is left alone.</summary>
        static void SetBackground(DependencyObject e, float factor)
        {
            var fe = e as FrameworkElement;
            if (fe == null) return;
            var b = e as Border;
            var p = e as Panel;
            if (IsPicture(e) || (b == null && p == null)) { SetElementOpacity(fe, factor); return; }
            Brush cur = b != null ? b.Background : p != null ? p.Background : null;
            if (cur == null) { if (fe.Tag is string && ((string)fe.Tag).StartsWith(DimTag)) fe.Tag = null; return; }
            string tag = fe.Tag as string;
            float origOp = 1f, last = 1f;
            bool ours = false;
            if (tag != null && tag.StartsWith(DimTag))
            {
                var parts = tag.Split('|');
                if (parts.Length == 3 &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out origOp) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out last))
                    ours = Math.Abs(cur.Opacity - origOp * last) < 0.002f;   // not ours any more: the game put its brush back
            }
            else if (fe.Tag != null) return;   // the game's own Tag
            if (!ours) { if (factor >= 0.999f) return; origOp = cur.Opacity; cur = (Brush)cur.Clone(); }
            cur.Opacity = origOp * factor;
            if (b != null) b.Background = cur; else p.Background = cur;
            fe.Tag = factor >= 0.999f ? null : DimTag + origOp.ToString("0.####", CultureInfo.InvariantCulture) + "|" + factor.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Every tracked window with a remembered look: once a second, content built later (tabs, lists) is dimmed / scaled too.</summary>
        void LookStep(object w)
        {
            LookState st;
            if (!Looks.TryGetValue(w, out st) || !st.Applied) return;
            bool flip = IsBottomWindow(w) != st.Bottom;   // maximized / restored: the look goes off / comes back
            if (!flip && st.Opacity >= 0.999f && Math.Abs(st.Scale - 1f) < 0.001f && !st.Outline) return;
            float now = UnityEngine.Time.unscaledTime;
            if (!flip && now < st.Next) return;
            var panel = st.Panel == null ? null : st.Panel.Target as Panel;
            if (panel != null) ApplyLook(w, panel);
        }

        void RemoveLookButton(object w)
        {
            LookState st;
            if (!Looks.TryGetValue(w, out st)) return;
            if (st.Button != null) RemoveFromParent(st.Button.Target);
            if (st.Menu != null) CloseLookMenu(st.Menu.Target);
            st.Button = null; st.Menu = null;
        }

        /// <summary>Called after the gear was put in the panel: the arrow button just left of it ('right' = the gear's right margin).</summary>
        void AddLookButton(object w, Panel panel, float top, float right, float size)
        {
            var st = Looks.GetOrCreateValue(w);
            RemoveLookButton(w);
            ApplyLook(w, panel);
            float bw = 10f;
            var arrow = new Border
            {
                Width = bw, Height = size, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0f, top, right + size + 2f, 0f),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Opacity = 0.9f,
                Child = new Path
                {
                    Data = new StreamGeometry("F0 M0,0 L8,0 L4,5 Z"), Fill = new SolidColorBrush(GearGrey),
                    Width = 8f, Height = 5f, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            arrow.MouseEnter += (s, e) => arrow.Opacity = 1f;
            arrow.MouseLeave += (s, e) => arrow.Opacity = 0.9f;
            try { arrow.Cursor = Cursors.Hand; ToolTipService.SetToolTip(arrow, Loc.T("tip.look")); } catch { }
            var owner = new WeakReference(w);
            float menuTop = top + size + 3f, menuRight = right + size + 2f;
            arrow.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (!Enabled) return;
                var ow = owner.Target;
                var pn = panel;
                if (ow == null) return;
                LookState ls;
                if (!Looks.TryGetValue(ow, out ls)) return;
                if (ls.Menu != null && ls.Menu.Target != null) { CloseLookMenu(ls.Menu.Target); ls.Menu = null; return; }
                var menu = BuildLookMenu(ow, pn, ls, arrow);
                ls.Menu = new WeakReference(menu);
            };
            Panel.SetZIndex(arrow, 100001);
            var grid = panel as Grid;
            if (grid != null)
            {
                Grid.SetRowSpan(arrow, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(arrow, Math.Max(1, grid.ColumnDefinitions.Count));
            }
            panel.Children.Add(arrow);
            st.Button = new WeakReference(arrow);
        }

        UIElement BuildLookMenu(object w, Panel panel, LookState ls, UIElement target)
        {
            var look = LookOf(w, true);
            var back = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(6f),
                Background = new SolidColorBrush(Color.FromRgb(30, 33, 38)), BorderBrush = new SolidColorBrush(Color.FromRgb(70, 76, 86)),
                BorderThickness = new Thickness(1f)
            };
            var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = true, Child = back };
            var cols = new StackPanel { Orientation = Orientation.Horizontal };
            var wref = new WeakReference(w);
            Action<bool, float> pick = (isOpacity, value) =>
            {
                var ow = wref.Target;
                if (ow == null) return;
                string label, key = MatchPattern(ow, out label);
                if (key == null) return;
                var map = LookMap();
                float[] v;
                if (!map.TryGetValue(key, out v)) { v = new[] { 1f, 1f, 0f }; map[key] = v; }
                if (isOpacity) v[0] = value; else if (value < 0f) v[2] = v[2] > 0.5f ? 0f : 1f; else v[1] = value;
                SaveLooks();
                _lookTrace = _debug;
                ApplyLook(ow, panel);
                _lookTrace = false;
                if (_debug) DumpBig(ow, panel);
                CloseLookMenu(popup);
                ls.Menu = null;
            };
            bool wvMap = NoScale(w) && MapOn();   // World View shows the texture of the 3D view: no opacity / outline here
            if (!wvMap) cols.Children.Add(LookColumn(Loc.T("look.opacity"), OpacitySteps, look[0], true, pick));
            if (!NoScale(w))
            {
                cols.Children.Add(new Border { Width = 1f, Margin = new Thickness(8f, 2f, 8f, 2f), Background = new SolidColorBrush(Color.FromRgb(70, 76, 86)) });
                cols.Children.Add(LookColumn(Loc.T("look.scale"), ScaleLow, look[1], false, pick));
                cols.Children.Add(new Border { Width = 6f });
                cols.Children.Add(LookColumn("", ScaleHigh, look[1], false, pick));
            }
            // text outline: greyed out (and not clickable) at 100 % opacity
            bool dis = look[0] >= 0.999f;
            var outl = new Border
            {
                Margin = new Thickness(0f, 6f, 0f, 0f), Padding = new Thickness(4f, 3f, 4f, 3f),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Opacity = dis ? 0.4f : 1f,
                Child = new TextBlock
                {
                    Text = (look[2] > 0.5f ? "☑ " : "☐ ") + Loc.T("look.outline"), FontSize = 12f,
                    Foreground = new SolidColorBrush(dis ? Color.FromRgb(150, 156, 162) : Color.FromRgb(230, 232, 235))
                }
            };
            try { ToolTipService.SetToolTip(outl, Loc.T("look.outline.tip")); } catch { }
            if (!dis)
            {
                outl.MouseEnter += (s, e) => outl.Background = new SolidColorBrush(Color.FromRgb(62, 69, 79));
                outl.MouseLeave += (s, e) => outl.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                outl.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; pick(false, -1f); };
            }
            else outl.PreviewMouseLeftButtonDown += (s, e) => e.Handled = true;
            // level: below the others / as the game has it / above the others
            var lvRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0f, 6f, 0f, 0f) };
            lvRow.Children.Add(new TextBlock { Text = Loc.T("look.level"), FontSize = 11f, Foreground = new SolidColorBrush(Color.FromRgb(150, 156, 162)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4f, 0f, 6f, 0f) });
            int curLevel = LevelOf(w);
            foreach (var lv in new[] { -1, 0, 1 })
            {
                int level = lv; bool on = level == curLevel;
                var b = new Border
                {
                    Padding = new Thickness(6f, 2f, 6f, 2f), Margin = new Thickness(0f, 0f, 2f, 0f),
                    Background = new SolidColorBrush(on ? Color.FromRgb(52, 58, 66) : Color.FromArgb(0, 0, 0, 0)),
                    Child = new TextBlock { Text = Loc.T(level < 0 ? "look.level.bottom" : level > 0 ? "look.level.top" : "look.level.default"), FontSize = 12f, Foreground = new SolidColorBrush(on ? Color.FromRgb(255, 255, 255) : Color.FromRgb(230, 232, 235)) }
                };
                b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(Color.FromRgb(62, 69, 79));
                b.MouseLeave += (s, e) => b.Background = new SolidColorBrush(on ? Color.FromRgb(52, 58, 66) : Color.FromArgb(0, 0, 0, 0));
                b.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    var ow = wref.Target; if (ow == null) return;
                    SetLevel(ow, level);
                    CloseLookMenu(popup); ls.Menu = null;
                };
                lvRow.Children.Add(b);
            }
            var wrap = new StackPanel { Orientation = Orientation.Vertical };
            if (!wvMap) { wrap.Children.Add(cols); wrap.Children.Add(outl); }
            wrap.Children.Add(lvRow);
            // the menu is a popup: it is not limited by the window; its right edge is under the arrow's right edge
            back.Child = wrap;
            back.SizeChanged += (s, e) => { try { popup.HorizontalOffset = (float)((FrameworkElement)target).ActualWidth - (float)back.ActualWidth; } catch { } };
            try { popup.IsOpen = true; } catch { }
            bool entered = false;
            back.MouseEnter += (s, e) => entered = true;
            back.MouseLeave += (s, e) => { if (entered) { CloseLookMenu(popup); ls.Menu = null; } };
            panel.Children.Add(popup);
            return popup;
        }

        static void CloseLookMenu(object m)
        {
            var p = m as Popup;
            try { if (p != null) p.IsOpen = false; } catch { }
            RemoveFromParent(m);
        }

        static UIElement LookColumn(string title, float[] steps, float current, bool isOpacity, Action<bool, float> pick)
        {
            var col = new StackPanel { Orientation = Orientation.Vertical, MinWidth = 58f };
            col.Children.Add(new TextBlock
            {
                Text = title, Foreground = new SolidColorBrush(Color.FromRgb(150, 156, 162)), FontSize = 11f, Margin = new Thickness(4f, 0f, 4f, 3f)
            });
            foreach (var step in steps)
            {
                float v = step;
                bool on = Math.Abs(v - current) < 0.005f;
                var row = new Border
                {
                    Padding = new Thickness(4f, 2f, 4f, 2f), Background = new SolidColorBrush(on ? Color.FromRgb(52, 58, 66) : Color.FromArgb(0, 0, 0, 0)),
                    Child = new TextBlock
                    {
                        Text = (on ? "✓ " : "   ") + Math.Round(v * 100f).ToString(CultureInfo.InvariantCulture) + "%",
                        Foreground = new SolidColorBrush(Color.FromRgb(230, 232, 235)), FontSize = 12f
                    }
                };
                row.MouseEnter += (s, e) => row.Background = new SolidColorBrush(Color.FromRgb(62, 69, 79));
                row.MouseLeave += (s, e) => row.Background = new SolidColorBrush(on ? Color.FromRgb(52, 58, 66) : Color.FromArgb(0, 0, 0, 0));
                row.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; pick(isOpacity, v); };
                col.Children.Add(row);
            }
            return col;
        }
    }
}
