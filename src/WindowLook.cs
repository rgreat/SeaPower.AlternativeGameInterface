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
            public List<TextBlock> AutoTexts = new List<TextBlock>();   // the texts the last window step coloured by the backdrop (recoloured at once by AutoLookRefresh)
            public int AutoDisp = -1;                                  // the display of the window while the texts follow the backdrop
            public float AutoT;   // text by background: 0 = the texts as they are (dark backdrop), 1 = the texts darkened as far as they go (light backdrop)
        }
        static readonly ConditionalWeakTable<object, LookState> Looks = new ConditionalWeakTable<object, LookState>();

        ConfigEntry<string> _slOrient;

        void BindLookConfig()
        {
            try
            {
                float hd = Config.Bind("General", "TextHaloDepth", 1f, "Text contrast: offset in pixels of the pure white / black shadow behind the texts (no blur; 0.5 - 3). Restart the game after changing.").Value;
                OutlineDepth = Math.Max(0.5f, Math.Min(3f, hd));
            }
            catch { }
            _slOrient = Config.Bind("General", "BackdropRows", "auto",
                "Text by background: which row of the screen copy comes first: auto (by the graphics API), topFirst or bottomFirst. Change it only if the texts of a window follow the brightness of the wrong place.");
            _looks = Config.Bind("General", "WindowLooks", "",
                "Opacity, scale and text contrast (+ / -) of the remembered windows, written by the plugin: pattern|opacity|scale|contrast;...");
        }

        Dictionary<string, float[]> LookMap()
        {
            if (_lookMap != null) return _lookMap;
            _lookMap = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in (_looks.Value ?? "").Split(';'))
            {
                var p = rec.Split('|');
                float o, s, ol = 0f;
                if ((p.Length == 3 || p.Length == 4) && !string.IsNullOrWhiteSpace(p[0]) &&
                    float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out o) &&
                    float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out s))
                {
                    // the text contrast: '+' / '-' = the choice made (by itself at 50 % or by the user); '1' / '0' / none = a record of an older version, where the contrast
                    // was on below 50 % opacity whatever the flag said
                    string f = p.Length == 4 ? p[3].Trim() : "0";
                    if (f == "+") ol = 1f;
                    else if (f == "-") ol = 0f;
                    else if (f == "*") ol = 1f;   // an earlier build of the merged option
                    else { float legacy; float.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out legacy); ol = legacy > 0.5f || o < 0.5f ? 1f : 0f; }
                    _lookMap[p[0].Trim()] = new[] { Clamp(o, 0f, 1f), Clamp(s, 0.5f, 2f), ol > 0.5f ? 1f : 0f };
                }
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
                  .Append('|').Append(kv.Value[2] > 0.5f ? '+' : '-');
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
            float auto = -1f;   // text by background (see ShiftText)
            if (look[2] > 0.5f)
            {
                int disp = DisplayOf(w);   // the display the window is on (0 = the main one)
                SceneLumAsk(disp);
                float sl;
                if (SceneLumUnder(panel, disp, out sl))
                {
                    AutoStep(st, sl);
                }
                auto = st.AutoT;
            }
            else st.AutoT = 0f;
            st.AutoTexts.Clear();
            st.AutoDisp = auto >= 0f ? DisplayOf(w) : -1;
            _owDisp = st.AutoDisp;
            OutlineWalk(panel, look[2] > 0.5f, menu, ref tb, auto, auto >= 0f ? st.AutoTexts : null);
            FrameOpacity(w, look[0]);
            var cp = FindContentPresenter(w, panel);
            if (cp != null)
            {
                var cur = cp.LayoutTransform as ScaleTransform;
                if (Math.Abs(look[1] - 1f) < 0.001f) { if (cur != null) cp.LayoutTransform = null; }
                else if (cur == null || Math.Abs(cur.ScaleX - look[1]) > 0.001f) cp.LayoutTransform = new ScaleTransform(look[1], look[1]);
            }
        }

        // black halo of the texts (our own effect: recognised by its blur radius, the game's own effects stay)
        static float OutlineDepth = 1f;   // [General] TextHaloDepth; the shadow has no blur: pure white / black

        static bool IsOurHalo(Effect ef)
        {
            var d = ef as DropShadowEffect;
            return d != null && d.BlurRadius < 0.001f && Math.Abs(d.ShadowDepth - OutlineDepth) < 0.001f;
        }

        static bool IsDarkText(Brush b)
        {
            var sc = b as SolidColorBrush;
            if (sc == null) return false;
            float r = Convert.ToSingle(sc.Color.R), g = Convert.ToSingle(sc.Color.G), bl = Convert.ToSingle(sc.Color.B);
            if (Math.Max(r, Math.Max(g, bl)) > 1.0001f) { r /= 255f; g /= 255f; bl /= 255f; }
            return 0.299f * r + 0.587f * g + 0.114f * bl < 0.5f;
        }

        // text contrast: grey texts go to pure white / black, every text gets a halo of the opposite colour (coloured texts keep their colour)
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, Brush> _origFg = new System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, Brush>();

        static bool IsGrey(Brush b, out bool dark)
        {
            dark = false;
            var sc = b as SolidColorBrush;
            if (sc == null) return false;
            float r = Convert.ToSingle(sc.Color.R), g = Convert.ToSingle(sc.Color.G), bl = Convert.ToSingle(sc.Color.B);
            if (Math.Max(r, Math.Max(g, bl)) > 1.0001f) { r /= 255f; g /= 255f; bl /= 255f; }
            dark = 0.299f * r + 0.587f * g + 0.114f * bl < 0.5f;
            return Math.Max(r, Math.Max(g, bl)) - Math.Min(r, Math.Min(g, bl)) < 0.12f;
        }

        /// <summary>The frame of a transparent window is half as strong as the window's opacity (100 % = the frame as set, never below 20 %).</summary>
        void FrameOpacity(object w, float opacity)
        {
            object b;
            if (!Borders.TryGetValue(w, out b)) return;
            var fe = b as FrameworkElement;
            if (fe == null) return;
            float f = opacity >= 0.999f ? 1f : Math.Max(0.2f, opacity * 0.5f);
            if (Math.Abs(fe.Opacity - f) > 0.001f) fe.Opacity = f;
        }

        // ---- text by background: the lightness of a text colour moves with the backdrop (HSL), the hue stays
        static void Rgb01(SolidColorBrush b, out float r, out float g, out float bl, out float a)
        {
            var c = b.Color;
            r = Convert.ToSingle(c.R); g = Convert.ToSingle(c.G); bl = Convert.ToSingle(c.B); a = Convert.ToSingle(c.A);
            if (Math.Max(r, Math.Max(g, bl)) > 1.0001f || a > 1.0001f) { r /= 255f; g /= 255f; bl /= 255f; a /= 255f; }
        }

        static Color ShiftText(SolidColorBrush src, float t)
        {
            float r, g, b, a;
            Rgb01(src, out r, out g, out b, out a);
            bool grey = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 0.12f;
            if (grey) { float y = 0.299f * r + 0.587f * g + 0.114f * b; r = g = b = y < 0.5f ? 0f : 1f; }
            float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2f, h = 0f, s = 0f;
            if (mx - mn > 1e-5f)
            {
                float d = mx - mn;
                s = l > 0.5f ? d / (2f - mx - mn) : d / (mx + mn);
                if (mx == r) h = (g - b) / d + (g < b ? 6f : 0f); else if (mx == g) h = (b - r) / d + 2f; else h = (r - g) / d + 4f;
                h /= 6f;
            }
            if (l > 0.3f) l += (l * 0.15f - l) * t;   // down to 15 % of the lightness (nearly black) on a fully light backdrop, coloured texts too; dark texts stay as they are
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s, p = 2f * l - q;
            float rr = s <= 0f ? l : Hue(p, q, h + 1f / 3f), gg = s <= 0f ? l : Hue(p, q, h), bb = s <= 0f ? l : Hue(p, q, h - 1f / 3f);
            return Color.FromArgb(ToByte(a), ToByte(rr), ToByte(gg), ToByte(bb));
        }

        static float Hue(float p, float q, float t)
        {
            if (t < 0f) t += 1f; else if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        static byte ToByte(float v) { return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f))); }

        static bool SameColour(SolidColorBrush cur, Color want)
        {
            float r, g, b, a;
            Rgb01(cur, out r, out g, out b, out a);
            return Math.Abs(ToByte(r) - want.R) <= 3 && Math.Abs(ToByte(g) - want.G) <= 3 && Math.Abs(ToByte(b) - want.B) <= 3;   // a step this small is not worth a new brush
        }

        /// <summary>The backdrop brightness 'sl' switches the window's texts between two states, no gradations: t = 0 (the texts as they are, black halo on light
        /// ones) and t = 1 (darkened, white halo). Up at 0.66 and down at 0.58 (a hysteresis; white text with a black halo stays until the backdrop is really
        /// light). True when t changed.</summary>
        static int _owDisp;
        sealed class TBox { public float T; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, TBox> _txtT = new System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, TBox>();
        static float TextTOf(TextBlock t) { TBox b; return _txtT.TryGetValue(t, out b) ? b.T : 0f; }
        /// <summary>The state (0 / 1) of one text by the brightness right under it (same hysteresis); without a picture: the window's state.</summary>
        static float TextT(TextBlock t, int disp, float fallback)
        {
            TBox b;
            if (!_txtT.TryGetValue(t, out b)) { b = new TBox { T = fallback }; _txtT.Add(t, b); }
            float sl;
            if (disp >= 0 && SceneLumUnder(t, disp, out sl)) b.T = sl >= 0.66f ? 1f : sl <= 0.58f ? 0f : b.T;
            return b.T;
        }

        static bool AutoStep(LookState st, float sl)
        {
            float nt = sl >= 0.66f ? 1f : sl <= 0.58f ? 0f : st.AutoT;
            if (nt == st.AutoT) return false;
            st.AutoT = nt;
            return true;
        }

        // dark text: a white halo, light text: a black one
        static void ApplyHalo(TextBlock t, bool darkText)
        {
            var cur = t.Effect;
            if (cur == null || (IsOurHalo(cur) && (Convert.ToSingle(((DropShadowEffect)cur).Color.R) > 127f) != darkText))
                t.Effect = new DropShadowEffect { Color = darkText ? Color.FromRgb(255, 255, 255) : Color.FromRgb(0, 0, 0), BlurRadius = 0f, ShadowDepth = OutlineDepth, Direction = 315f, Opacity = 1f };
        }

        /// <summary>A new picture of a display arrived: the windows on it that follow the backdrop get their texts recoloured at once (no tree walk: the texts
        /// the last window step found), when the backdrop moved their colour by a visible step.</summary>
        static void AutoLookRefresh(int display)
        {
            var a = Active;
            if ((object)a == null) return;
            try
            {
                foreach (var tr in a._tracked)
                {
                    var w = tr.Ref == null ? null : tr.Ref.Target;
                    LookState st;
                    if (w == null || !Looks.TryGetValue(w, out st) || !st.Applied || st.AutoDisp != display || st.AutoTexts.Count == 0) continue;
                    var panel = st.Panel == null ? null : st.Panel.Target as Panel;
                    if (panel == null || !panel.IsLoaded) continue;
                    foreach (var tb in st.AutoTexts)
                    {
                        float before = TextTOf(tb), nt = TextT(tb, display, before);
                        if (nt == before) continue;
                        Brush o;
                        var src = _origFg.TryGetValue(tb, out o) ? o as SolidColorBrush : null;
                        if (src == null || FgForeign(tb)) continue;
                        var want = ShiftText(src, nt);
                        var cur = tb.Foreground as SolidColorBrush;
                        if (cur == null || !SameColour(cur, want)) SetFg(tb, new SolidColorBrush(want));
                        ApplyHalo(tb, IsDarkText(tb.Foreground));
                    }
                }
            }
            catch (Exception e) { Trace("AutoLookRefresh: " + e.Message); }
        }

        // Elements another code rewrites all the time (a mod that sets its colours on every refresh) must not be fought over: a colour / background we
        // set is remembered; when it is found replaced twice, the element is left alone for good (it flickered between its colour and ours).
        sealed class Hits { public int N; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Brush> _ourFg = new System.Runtime.CompilerServices.ConditionalWeakTable<object, Brush>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Hits> _fgHits = new System.Runtime.CompilerServices.ConditionalWeakTable<object, Hits>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Hits> _bgHits = new System.Runtime.CompilerServices.ConditionalWeakTable<object, Hits>();

        static void SetFg(TextBlock t, Brush b) { t.Foreground = b; _ourFg.Remove(t); _ourFg.Add(t, b); }

        /// <summary>True when the text's colour is managed by other code (replaced after us twice): it is not touched any more.</summary>
        static bool FgForeign(TextBlock t)
        {
            Hits h;
            if (_fgHits.TryGetValue(t, out h) && h.N >= 2) return true;
            Brush ours;
            if (_ourFg.TryGetValue(t, out ours) && !ReferenceEquals(t.Foreground, ours))
            {
                _ourFg.Remove(t); _origFg.Remove(t);   // what we knew of the text is stale
                if (h == null) h = _fgHits.GetOrCreateValue(t);
                h.N++;
                return h.N >= 2;
            }
            return false;
        }

        void OutlineWalk(DependencyObject e, bool on, object skip, ref int budget, float auto = -1f, List<TextBlock> sink = null)
        {
            if (ReferenceEquals(e, skip) || --budget < 0) return;
            var t = e as TextBlock;
            if (t != null && !FgForeign(t))
            {
                try
                {
                    if (on)
                    {
                        if (auto >= 0f)
                        {
                            // text by background: the colour keeps its hue, only its lightness moves (light texts get darker on a light backdrop);
                            // grey texts start from the pure white / black of the contrast. The two halo colours (white / black) stay.
                            Brush o0; bool known = _origFg.TryGetValue(t, out o0);
                            var src = (known ? o0 : t.Foreground) as SolidColorBrush;
                            if (src != null)
                            {
                                if (!known) _origFg.Add(t, t.Foreground);
                                var want = ShiftText(src, TextT(t, _owDisp, auto));
                                var cur0 = t.Foreground as SolidColorBrush;
                                if (cur0 == null || !SameColour(cur0, want)) SetFg(t, new SolidColorBrush(want));
                                if (sink != null) sink.Add(t);
                            }
                        }
                        bool darkText = IsDarkText(t.Foreground);
                        ApplyHalo(t, darkText);
                        bool dk; Brush o;
                        if (auto < 0f && IsGrey(t.Foreground, out dk) && !_origFg.TryGetValue(t, out o))
                        {
                            var sc = (SolidColorBrush)t.Foreground;
                            bool pure = (dk && sc.Color.R == 0 && sc.Color.G == 0 && sc.Color.B == 0) || (!dk && sc.Color.R == 255 && sc.Color.G == 255 && sc.Color.B == 255);
                            if (!pure)
                            {
                                _origFg.Add(t, t.Foreground);
                                SetFg(t, new SolidColorBrush(dk ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255)));
                            }
                        }
                        else if (auto < 0f && _origFg.TryGetValue(t, out o))
                        {
                            // text by background was switched off: back to the pure white / black of the text's own kind (grey) or to the colour it had
                            if (IsGrey(o, out dk)) SetFg(t, new SolidColorBrush(dk ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255)));
                            else { t.Foreground = o; _ourFg.Remove(t); _origFg.Remove(t); }
                        }
                    }
                    else
                    {
                        if (IsOurHalo(t.Effect)) t.Effect = null;
                        Brush o;
                        if (_origFg.TryGetValue(t, out o)) { t.Foreground = o; _ourFg.Remove(t); _origFg.Remove(t); }
                    }
                }
                catch { }
            }
            for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
            {
                var c = VisualTreeHelper.GetChild(e, k);
                if (c != null) OutlineWalk(c, on, skip, ref budget, auto, sink);
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

        /// <summary>A background the game's style changes with the state (selected / hover / pressed / checked / disabled): the Border of a list, tree,
        /// tab, menu or button item's template. A brush set the ordinary way is a local value, which wins over the style's triggers (the unit type tab that was
        /// red when the look was applied stayed red). Such a background gets the dimmed copy through SetCurrentValue: the style stays the value's source,
        /// the next trigger puts the game's brush back, and the item's state events dim that one again (HookState).</summary>
        static bool StateDriven(DependencyObject e)
        {
            if (!(e is Border) && !(e is Panel)) return false;   // only backgrounds: a text, an icon or any other element of the item keeps its own Opacity
            var fe = e as FrameworkElement;
            var tp = fe == null ? null : fe.TemplatedParent;
            return tp is ListBoxItem || tp is TreeViewItem || tp is TabItem || tp is ComboBoxItem || tp is MenuItem || tp is ButtonBase;
        }

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
            else if (layer || factor >= 0.999f || StateDriven(e)) SetBackground(e, layer && _titleFactor > factor && IsTitleStrip(e) ? _titleFactor : factor);   // state-driven: hooked even while its brush is empty (an item selected later)
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

        sealed class StateHook { public float Factor = 1f; }
        static readonly ConditionalWeakTable<object, StateHook> _stateHooks = new ConditionalWeakTable<object, StateHook>();
        static readonly List<WeakReference> _redimQueue = new List<WeakReference>();

        /// <summary>The state events of an item whose template background is dimmed: each one dims the brush the style has just put (at once and, for the
        /// triggers that follow the event, once more at the next window step). The factor is the window's last one; 1 = the game's brush is left alone.</summary>
        static void HookState(FrameworkElement tp, float factor)
        {
            if (tp == null) return;
            StateHook h;
            if (_stateHooks.TryGetValue(tp, out h)) { h.Factor = factor; return; }
            if (factor >= 0.999f) return;                      // never dimmed: nothing to keep up
            h = new StateHook { Factor = factor };
            _stateHooks.Add(tp, h);
            try
            {
                RoutedEventHandler re = (s, a) => StateChanged(tp);
                tp.MouseEnter += (s, a) => StateChanged(tp);
                tp.MouseLeave += (s, a) => StateChanged(tp);
                tp.IsEnabledChanged += (s, a) => StateChanged(tp);
                tp.AddHandler(UIElement.GotKeyboardFocusEvent, (KeyboardFocusChangedEventHandler)((s, a) => StateChanged(tp)));
                tp.AddHandler(UIElement.LostKeyboardFocusEvent, (KeyboardFocusChangedEventHandler)((s, a) => StateChanged(tp)));
                var lbi = tp as ListBoxItem; if (lbi != null) { lbi.Selected += re; lbi.Unselected += re; }
                var tvi = tp as TreeViewItem; if (tvi != null) { tvi.Selected += re; tvi.Unselected += re; }
                if (tp is TabItem) { tp.AddHandler(Selector.SelectedEvent, re); tp.AddHandler(Selector.UnselectedEvent, re); }
                var tb = tp as ToggleButton; if (tb != null) { tb.Checked += re; tb.Unchecked += re; tb.Indeterminate += re; }
                if (tp is ButtonBase || tp is MenuItem)
                {
                    tp.PreviewMouseLeftButtonDown += (s, a) => StateChanged(tp);
                    tp.PreviewMouseLeftButtonUp += (s, a) => StateChanged(tp);
                    tp.LostMouseCapture += (s, a) => StateChanged(tp);
                }
            }
            catch (Exception ex) { Trace("look: state events of " + tp.GetType().Name + ": " + ex.Message); }
        }

        static void StateChanged(FrameworkElement tp)
        {
            Redim(tp);
            _redimQueue.Add(new WeakReference(tp));
        }

        /// <summary>The template backgrounds of one item get the item's factor again (the style may just have put its own brush).</summary>
        static void Redim(FrameworkElement tp)
        {
            StateHook h;
            if (tp == null || !_stateHooks.TryGetValue(tp, out h)) return;
            int budget = 200;
            RedimWalk(tp, tp, h.Factor, ref budget);
        }

        static void RedimWalk(DependencyObject e, FrameworkElement tp, float factor, ref int budget)
        {
            if (--budget < 0) return;
            var fe = e as FrameworkElement;
            if (fe != null && ReferenceEquals(fe.TemplatedParent, tp) && (e is Border || e is Panel))
            {
                Brush br;
                if (Layered(e, out br) || (fe.Tag as string ?? "").StartsWith(DimTag)) SetBackground(e, factor);
            }
            for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
            {
                var c = VisualTreeHelper.GetChild(e, k);
                if (c != null && !(c is ListBoxItem) && !(c is TreeViewItem) && !(c is TabItem)) RedimWalk(c, tp, factor, ref budget);   // not into nested items: they have their own
            }
        }

        /// <summary>The items whose state changed since the last step: once more, after the triggers that follow the events.</summary>
        static void FlushRedim()
        {
            if (_redimQueue.Count == 0) return;
            var list = _redimQueue.ToArray();
            _redimQueue.Clear();
            foreach (var w in list) { try { Redim(w.Target as FrameworkElement); } catch { } }
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
            bool state = StateDriven(e);
            if (state) HookState(fe.TemplatedParent, factor);
            Brush cur = b != null ? b.Background : p != null ? p.Background : null;
            if (cur == null) { if (fe.Tag is string && ((string)fe.Tag).StartsWith(DimTag)) fe.Tag = null; return; }
            Hits bh;
            if (!state && _bgHits.TryGetValue(fe, out bh) && bh.N >= 2) return;   // managed by other code: not touched
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
            if (!state && tag != null && tag.StartsWith(DimTag) && !ours)
            {
                // our brush was replaced by other code: twice = it sets its backgrounds all the time, the element is left alone (it flickered)
                var h = _bgHits.GetOrCreateValue(fe);
                if (++h.N >= 2) { fe.Tag = null; return; }
            }
            if (!ours) { if (factor >= 0.999f) return; origOp = cur.Opacity; cur = (Brush)cur.Clone(); }
            cur.Opacity = origOp * factor;
            if (state)
            {
                var dp = b != null ? Border.BackgroundProperty : Panel.BackgroundProperty;
                if (factor >= 0.999f)
                {
                    fe.InvalidateProperty(dp);                       // back to what the style gives
                    var now = b != null ? b.Background : p.Background;
                    if (!ReferenceEquals(now, cur)) { fe.Tag = null; return; }
                }
                fe.SetCurrentValue(dp, cur);
            }
            else if (b != null) b.Background = cur; else p.Background = cur;
            fe.Tag = factor >= 0.999f ? null : DimTag + origOp.ToString("0.####", CultureInfo.InvariantCulture) + "|" + factor.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Every tracked window with a remembered look: once a second, content built later (tabs, lists) is dimmed / scaled too.</summary>
        void LookStep(object w)
        {
            FlushRedim();
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
        void AddLookButton(object w, Panel panel, float top, float right, float size, Panel host = null, Brush bg = null)
        {
            var st = Looks.GetOrCreateValue(w);
            RemoveLookButton(w);
            ApplyLook(w, panel);
            float bw = 10f;
            var arrow = new Border
            {
                Width = bw, Height = host != null ? float.NaN : size,
                HorizontalAlignment = host != null ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = host != null ? VerticalAlignment.Stretch : VerticalAlignment.Top,
                Margin = host != null ? new Thickness(0f) : new Thickness(0f, top, right + size + 2f, 0f),
                Background = bg ?? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Child = new Path
                {
                    Data = new StreamGeometry("F0 M0,0 L8,0 L4,5 Z"), Fill = new SolidColorBrush(GearGrey),
                    Width = 8f, Height = 5f, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.9f
                }
            };
            arrow.MouseEnter += (s, e) => arrow.Child.Opacity = 1f;
            arrow.MouseLeave += (s, e) => arrow.Child.Opacity = 0.9f;
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
            if (host != null) host.Children.Insert(0, arrow);   // before the gear
            else
            {
                Panel.SetZIndex(arrow, 100001);
                var grid = panel as Grid;
                if (grid != null)
                {
                    Grid.SetRowSpan(arrow, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(arrow, Math.Max(1, grid.ColumnDefinitions.Count));
                }
                panel.Children.Add(arrow);
            }
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
                if (isOpacity)
                {
                    // crossing 50 % switches the text contrast on / off by itself; after that it is the user's choice (remembered like the rest)
                    bool wasLow = v[0] < 0.5f, nowLow = value < 0.5f;
                    v[0] = value;
                    if (wasLow != nowLow) v[2] = nowLow ? 1f : 0f;
                }
                else if (value < 0f) v[2] = v[2] > 0.5f ? 0f : 1f; else v[1] = value;
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
            // text contrast (the texts also follow the backdrop): set by itself when the opacity crosses 50 %, always free to change
            var outl = new Border
            {
                Margin = new Thickness(0f, 6f, 0f, 0f), Padding = new Thickness(4f, 3f, 4f, 3f),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Child = new TextBlock
                {
                    Text = (look[2] > 0.5f ? "☑ " : "☐ ") + Loc.T("look.outline"), FontSize = 12f,
                    Foreground = new SolidColorBrush(Color.FromRgb(230, 232, 235))
                }
            };
            try { ToolTipService.SetToolTip(outl, Loc.T("look.outline.tip")); } catch { }
            outl.MouseEnter += (s, e) => outl.Background = new SolidColorBrush(Color.FromRgb(62, 69, 79));
            outl.MouseLeave += (s, e) => outl.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            outl.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; pick(false, -1f); };
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
            if (MonitorChoosable(w)) wrap.Children.Add(MonitorRow(w, () => { CloseLookMenu(popup); ls.Menu = null; }));
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
