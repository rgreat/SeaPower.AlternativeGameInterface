using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// SnapWindows: while a window is dragged by its title bar or resized by its edges, its visible edges snap to the visible edges of
    /// the other windows on screen and to the edges of the screen when closer than SnapDistance.
    ///  - Visible edges: a window's template keeps an invisible resize zone around the body (see the frame, BodyInsets); its insets
    ///    are measured once per window and taken off its rectangle, so snapped windows touch without a gap.
    ///  - Other windows are measured on the screen (PointToScreen) and taken into the moved window's own X / Y units: windows may sit
    ///    in different cells / layers of the UI.
    ///  - An edge snaps to another window only where the two overlap along the edge (with SnapDistance to spare).
    ///  - The game's drag and resize work in steps relative to the Thumb, which moves with the window: a snapped edge stays on its
    ///    target until the mouse has moved more than SnapDistance away, then follows the mouse again - no drift.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        FieldInfo _resizeParent;   // SeapowerUI.ResizeCommand._parent (the window)
        static readonly ConditionalWeakTable<object, float[]> SnapInsets = new ConditionalWeakTable<object, float[]>();

        static void ResizePrefix(object __instance, out float[] __state)
        {
            __state = null;
            var a = Active;
            if ((object)a == null || !(a._snapOn.Value || a._removeLimitsAll.Value) || a._resizeParent == null) return;
            var w = a._resizeParent.GetValue(__instance);
            if (w == null) return;
            __state = new[] { a.GetF(w, "X", 0f), a.GetF(w, "Y", 0f), a.GetF(w, "ActualWidth", 0f), a.GetF(w, "ActualHeight", 0f) };
        }

        static void ResizePostfix(object __instance, object parameter, float[] __state)
        {
            var a = Active;
            try { if ((object)a != null && a._resizeParent != null) a.NewsUserChanged(a._resizeParent.GetValue(__instance), "resized"); } catch { }
            if ((object)a == null || __state == null || !(parameter is Thickness)) return;
            var w = a._resizeParent.GetValue(__instance);
            try { if (a._removeLimitsAll.Value) a.ResizeBeyondScreen(w, (Thickness)parameter, __state); }
            catch (Exception e) { Trace("ResizeBeyondScreen: " + e.Message); }
            try { if (a._snapOn.Value) a.SnapResize(w, (Thickness)parameter, __state); }
            catch (Exception e) { Trace("SnapResize: " + e.Message); }
        }

        /// <summary>
        /// The window's visible part inside its element: insets left, top, right, bottom. The template keeps an invisible resize zone
        /// around the window. Taken from the frame's body (the largest opaque element, BodyInsets - the frame sits right on it), with two
        /// corrections:
        ///  - left / right: the resize zone is the same on both sides, so neither is taken wider than the other (in Unit Reference
        ///    the body is the information panel only: its left inset was the whole unit tree);
        ///  - top: the body starts below the title bar; the window's top is the title bar's top (HeaderThumb), if it is higher.
        /// (1.3.0 build before: the title bar's left offset as the zone - too wide when the title has its own padding / icon: every
        /// edge was taken far inside, a window snapped to the screen's left edge went off it, the other edges missed.)
        /// Measured once per window when it is laid out; zeros until then.
        /// </summary>
        float[] VisibleInsets(object w)
        {
            float[] ins;
            if (SnapInsets.TryGetValue(w, out ins)) return ins;
            ins = null;
            try
            {
                // relative to the template's root panel: that panel is what X / Y move (the window element itself stays at the origin
                // of its cell - PointToScreen of the element is the same wherever the window is)
                var panel = TemplateRootPanel(w);
                float[] b = null;
                if (panel != null && panel.ActualWidth > 1f) b = BodyInsets(panel);
                if (b != null)
                {
                    float side = Math.Min(b[0], b[2]);
                    float top = b[1];
                    var thumb = Get(w, "HeaderThumb") as FrameworkElement;
                    if (thumb != null && thumb.ActualHeight > 1f)
                    {
                        var tp = thumb.TranslatePoint(new Point(0f, 0f), panel);
                        if (tp.Y >= 0f && tp.Y < top) top = tp.Y;
                    }
                    ins = new[] { side, top, side, b[3] };
                    Trace("snap: body insets " + Fmt(b[0]) + "," + Fmt(b[1]) + "," + Fmt(b[2]) + "," + Fmt(b[3]));
                }
            }
            catch (Exception e) { Trace("VisibleInsets: " + e.Message); }
            if (ins == null) return new float[4]; // not laid out yet: try again next time
            SnapInsets.Add(w, ins);
            Trace("snap: visible insets of '" + TextOf(Get(w, "Header")) + "' " + Fmt(ins[0]) + "," + Fmt(ins[1]) + "," + Fmt(ins[2]) + "," + Fmt(ins[3]));
            return ins;
        }

        /// <summary>What a window snaps to, in its own X / Y units: the other windows' visible rectangles {L, T, R, B} and the screen.</summary>
        sealed class SnapSpace
        {
            public readonly List<float[]> Rects = new List<float[]>();
            public float L, T, R, B;     // the screen (B: above the mission HUD's bottom bar)
        }

        /// <summary>
        /// Measures the screen and the other windows for window 'w' as it is now. The layout is brought up to date first: several drag
        /// steps can come in one frame, and a layout one step behind took every target off by that step (windows jumped back and forth).
        /// Other windows (on screen, not maximized, visible, of the same scene, not ancestors / descendants) are measured on the screen
        /// (PointToScreen) and taken into w's X / Y units - windows may sit in different cells / layers of the UI. Null when 'w' cannot
        /// be measured.
        /// </summary>
        SnapSpace Measure(object w)
        {
            var me = w as FrameworkElement;
            var mp = me == null ? null : TemplateRootPanel(w);
            if (mp == null) return null;
            Point s0, s1;
            float x0, y0;
            try
            {
                me.UpdateLayout();
                x0 = GetF(w, "X", 0f); y0 = GetF(w, "Y", 0f);
                // the template's root panel moves with X / Y (not the window element): its origin on the screen is where X, Y are now
                s0 = mp.PointToScreen(new Point(0f, 0f)); s1 = mp.PointToScreen(new Point(100f, 100f));
            }
            catch { return null; }
            float kx = (s1.X - s0.X) / 100f, ky = (s1.Y - s0.Y) / 100f;   // screen pixels per unit
            if (kx < 0.01f || ky < 0.01f) return null;
            var sp = new SnapSpace();
            sp.L = x0 - s0.X / kx;
            sp.T = y0 - s0.Y / ky;
            float dsw, dsh; DisplaySize(mp, out dsw, out dsh);
            sp.R = x0 + (dsw - s0.X) / kx;
            sp.B = y0 + (BottomLimit(mp) - s0.Y) / ky;
            if (Periodic && UnityEngine.Time.unscaledTime > _snapTraceAt)
            {
                _snapTraceAt = UnityEngine.Time.unscaledTime + 1f;
                Trace("snap: '" + TextOf(Get(w, "Header")) + "' X,Y " + Fmt(x0) + "," + Fmt(y0) + " at screen " + Fmt(s0.X) + "," + Fmt(s0.Y) + ", " +
                      kx.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " px per unit; screen " + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height +
                      " = X " + Fmt(sp.L) + ".." + Fmt(sp.R) + ", Y " + Fmt(sp.T) + ".." + Fmt(sp.B) + "; insets " + string.Join(",", Array.ConvertAll(VisibleInsets(w), Fmt)));
            }
            var seen = new List<object>();
            int gen = GenOf(w);
            foreach (var t in _tracked)
            {
                var o = t.Ref.Target as FrameworkElement;
                if (o == null || ReferenceEquals(o, w) || o.Equals(w) || !IsOnScreen(o) || GetB(o, "Maximized")) continue;
                if (GenOf(o) != gen || o.Opacity < 0.05f) continue;   // a window of the previous scene / one fading away
                if (IsAncestor(o, w) || IsAncestor(w, o)) continue;
                bool dup = false;
                foreach (var x in seen) if (ReferenceEquals(x, o) || x.Equals(o)) { dup = true; break; }
                if (dup) continue;
                seen.Add(o);
                var op = TemplateRootPanel(o);
                if (op == null || op.ActualWidth < 20f || op.ActualHeight < 20f) continue;
                var ins = VisibleInsets(o);
                Point a, b;
                try { a = op.PointToScreen(new Point(ins[0], ins[1])); b = op.PointToScreen(new Point(op.ActualWidth - ins[2], op.ActualHeight - ins[3])); }
                catch { continue; }
                if (b.X <= 0f || b.Y <= 0f || a.X >= dsw || a.Y >= dsh) continue; // off the screen
                sp.Rects.Add(new[] { x0 + (a.X - s0.X) / kx, y0 + (a.Y - s0.Y) / ky, x0 + (b.X - s0.X) / kx, y0 + (b.Y - s0.Y) / ky });
            }
            return sp;
        }

        // the mission HUD's bottom bar, in screen pixels (Screen.height when there is none), kept for a few seconds
        float _bottomLimit = -1f, _bottomUntil;
        float _snapTraceAt;
        object _bottomView;

        /// <summary>
        /// The bottom edge windows snap to: the top of the mission HUD's bottom bar - the bar that holds the HUD's window buttons
        /// (MainGameView: Unit Status, Tactical Display, Event Log... toggle buttons): going up from such a button, the first element
        /// at least half the screen wide that reaches the bottom of the screen. If none is found but the button sits in the bottom
        /// fifth of the screen, the button's top. Otherwise (no mission HUD) the screen's bottom.
        /// </summary>
        float BottomLimit(object on = null)
        {
            float sh, sw; DisplaySize(on, out sw, out sh);
            if (DisplayOf(on) > 0) return sh;      // the HUD's bottom bar is on the main display only
            var view = MainGameViewInstance();
            float now = UnityEngine.Time.unscaledTime;
            if (_bottomLimit >= 0f && now < _bottomUntil && ReferenceEquals(view, _bottomView)) return _bottomLimit;
            _bottomView = view; _bottomUntil = now + 3f; _bottomLimit = sh;
            try
            {
                var v = view as FrameworkElement;
                if (v == null || !v.IsVisible) return _bottomLimit;
                foreach (var name in new[] { "tb_UnitStatusWindow", "tb_MapWindow", "tb_EventLogWindow", "tb_FormationManagerWindow", "tb_CameraControlsWindow" })
                {
                    var f = view.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var btn = f == null ? null : f.GetValue(view) as FrameworkElement;
                    if (btn == null || !btn.IsVisible || btn.ActualHeight < 1f) continue;
                    var bt = btn.PointToScreen(new Point(0f, 0f));
                    for (DependencyObject d = btn; d != null; d = VisualTreeHelper.GetParent(d))
                    {
                        var fe = d as FrameworkElement;
                        if (fe == null || fe.ActualWidth < 1f) continue;
                        var a = fe.PointToScreen(new Point(0f, 0f)); var b = fe.PointToScreen(new Point(fe.ActualWidth, fe.ActualHeight));
                        if (b.X - a.X < sw * 0.5f) continue;
                        if (b.Y >= sh * 0.97f && a.Y > sh * 0.6f && a.Y < sh) { _bottomLimit = a.Y; break; }
                        break; // wide, but not a bar at the bottom
                    }
                    if (_bottomLimit >= sh && bt.Y > sh * 0.8f && bt.Y < sh) _bottomLimit = bt.Y;
                    Trace("snap: bottom edge " + Fmt(_bottomLimit) + " of " + Fmt(sh) + " px (HUD bar found from " + name + ")");
                    break;
                }
            }
            catch (Exception e) { Trace("BottomLimit: " + e.Message); }
            return _bottomLimit;
        }

        /// <summary>
        /// Screen pixels per UI unit (vertical) of an element: the game scales its UI (UIScale x screen height / 1080). Measured on the
        /// element when it is on screen, otherwise that formula; 1 when nothing works.
        /// </summary>
        float ScreenScale(object e)
        {
            try
            {
                var v = e as Visual;
                if (v != null && VisualTreeHelper.GetParent(v) != null)
                {
                    var a = v.PointToScreen(new Point(0f, 0f)); var b = v.PointToScreen(new Point(0f, 100f));
                    float k = (b.Y - a.Y) / 100f;
                    if (k > 0.05f && k < 20f) return k;
                }
            }
            catch { }
            try
            {
                float ui = 1f;
                var asm = GameAssembly();
                var om = asm == null ? null : asm.GetType("SeaPower.OptionsManager");
                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
                var exists = om == null ? null : om.GetMethod("InstanceExists", sf, null, new[] { typeof(bool) }, null);
                if (exists != null && (bool)exists.Invoke(null, new object[] { false }))
                {
                    var inst = om.GetProperty("Instance", sf).GetValue(null, null);
                    var p = Get(inst, "UIScale");
                    if (p is float && (float)p > 0f) ui = (float)p;
                }
                return Math.Max(0.05f, ui * UnityEngine.Screen.height / 1080f);
            }
            catch { return 1f; }
        }

        /// <summary>The nearest snap within the distance for an edge at 'pos': the shift to apply, or NaN.</summary>
        static float Nearest(float pos, IEnumerable<float> lines, float dist)
        {
            float best = float.NaN;
            foreach (var l in lines)
            {
                float d = l - pos;
                if (Math.Abs(d) <= dist && (float.IsNaN(best) || Math.Abs(d) < Math.Abs(best))) best = d;
            }
            return best;
        }

        const float Touch = 1.5f;   // visible edges this close count as touching (after snapping they coincide)

        /// <summary>
        /// The lines one side of a window at l, t, r, b can snap to (side: 0 left, 1 top, 2 right, 3 bottom):
        ///  - the screen's edges;
        ///  - contact: the opposite side of a window beside it (my left to its right... ), if they overlap along the side (with dist);
        ///  - level: the same side of a window the window touches on the other axis (my left to the left of a window right above or
        ///    below it, my top to the top of a window right beside it) - only windows it touches, not ones merely near.
        /// </summary>
        static List<float> EdgeLines(SnapSpace sp, int side, float l, float t, float r, float b, float dist)
        {
            var lines = new List<float>();
            if (side == 0 || side == 2) { lines.Add(sp.L); lines.Add(sp.R); }
            else { lines.Add(sp.T); lines.Add(sp.B); }
            foreach (var q in sp.Rects)   // q: L, T, R, B
            {
                if (side == 0 || side == 2)
                {
                    if (q[1] <= b + dist && q[3] >= t - dist) lines.Add(side == 0 ? q[2] : q[0]);
                    bool stacked = (Math.Abs(q[3] - t) <= Touch || Math.Abs(q[1] - b) <= Touch) && q[0] < r && q[2] > l;
                    if (stacked) lines.Add(side == 0 ? q[0] : q[2]);
                }
                else
                {
                    if (q[0] <= r + dist && q[2] >= l - dist) lines.Add(side == 1 ? q[3] : q[1]);
                    bool besides = (Math.Abs(q[2] - l) <= Touch || Math.Abs(q[0] - r) <= Touch) && q[1] < b && q[3] > t;
                    if (besides) lines.Add(side == 1 ? q[1] : q[3]);
                }
            }
            return lines;
        }

        /// <summary>The smaller of two snap shifts (NaN: none).</summary>
        static float Pick(float a, float b)
        {
            return float.IsNaN(a) ? b : float.IsNaN(b) ? a : Math.Abs(a) <= Math.Abs(b) ? a : b;
        }

        /// <summary>
        /// Dragging by the title bar: the window at x, y snaps by its nearest visible edge, separately in x and y. Two passes: what it
        /// touches after the first decides the level lines of the second (both from the position the mouse asks for).
        /// </summary>
        void SnapMove(object w, ref float x, ref float y, float[] start)
        {
            float dist = Math.Max(1f, Math.Min(50f, _snapDist.Value));
            var sp = Measure(w);
            if (sp == null) return;
            var ins = VisibleInsets(w);
            var panel = TemplateRootPanel(w);   // the insets are measured on it; it fills the window
            float width = panel != null ? panel.ActualWidth : GetF(w, "ActualWidth", 0f), height = panel != null ? panel.ActualHeight : GetF(w, "ActualHeight", 0f);
            float rx = x, ry = y;
            Func<float, float, float> snapX = (xx, yy) =>
            {
                float l = xx + ins[0], r = xx + width - ins[2], t = yy + ins[1], b = yy + height - ins[3];
                float d = Pick(Nearest(l, EdgeLines(sp, 0, l, t, r, b, dist), dist), Nearest(r, EdgeLines(sp, 2, l, t, r, b, dist), dist));
                return float.IsNaN(d) ? xx : xx + d;
            };
            Func<float, float, float> snapY = (xx, yy) =>
            {
                float l = xx + ins[0], r = xx + width - ins[2], t = yy + ins[1], b = yy + height - ins[3];
                float d = Pick(Nearest(t, EdgeLines(sp, 1, l, t, r, b, dist), dist), Nearest(b, EdgeLines(sp, 3, l, t, r, b, dist), dist));
                return float.IsNaN(d) ? yy : yy + d;
            };
            float x1 = snapX(rx, ry), y1 = snapY(x1, ry);
            x = snapX(rx, y1);
            y = snapY(x, ry);
            if (y < 0f) y = 0f; // never above the screen (the title bar must stay reachable)
        }

        /// <summary>
        /// RemoveSizeLimits, remembered windows: the game keeps a resized window inside the screen (ResizeCommand.ComputeSize clamps
        /// every side to the host); the step is done again from the window before it, without that. The top stays on the screen (the
        /// title bar must stay reachable), the window's minimum is kept by the side that moved.
        /// </summary>
        void ResizeBeyondScreen(object w, Thickness delta, float[] start)
        {
            if (w == null || GetB(w, "Maximized")) return;
            string label;
            if (MatchPattern(w, out label) == null) return;
            if (delta.Left == 0f && delta.Right == 0f && delta.Top == 0f && delta.Bottom == 0f) return;
            float minW = Math.Max(20f, GetF(w, "MinWidth", 0f)), minH = Math.Max(20f, GetF(w, "MinHeight", 0f));
            float l = start[0] + delta.Left, t = Math.Max(0f, start[1] + delta.Top);
            float r = start[0] + start[2] + delta.Right, b = start[1] + start[3] + delta.Bottom;
            // an edge never goes farther beyond the screen than it already was (the mouse may leave the screen / the display while resizing)
            float hw, hh;
            if (HostSize(w, out hw, out hh) && hw > 0f && hh > 0f)
            {
                l = Math.Max(l, Math.Min(start[0], 0f));
                r = Math.Min(r, Math.Max(start[0] + start[2], hw));
                b = Math.Min(b, Math.Max(start[1] + start[3], hh));
            }
            if (r - l < minW) { if (delta.Left != 0f) l = r - minW; else r = l + minW; }
            if (b - t < minH) { if (delta.Top != 0f) t = Math.Max(0f, b - minH); if (b - t < minH) b = t + minH; }
            SetF(w, "X", l);
            SetF(w, "Y", t);
            SetF(w, "Width", r - l);
            SetF(w, "Height", b - t);
        }

        /// <summary>Resizing by an edge / corner (delta: the sides the game moved): each moved side snaps on its own.</summary>
        void SnapResize(object w, Thickness delta, float[] start)
        {
            if (w == null || GetB(w, "Maximized")) return;
            bool mL = delta.Left != 0f, mR = delta.Right != 0f, mT = delta.Top != 0f, mB = delta.Bottom != 0f;
            if (!mL && !mR && !mT && !mB) return;
            float dist = Math.Max(1f, Math.Min(50f, _snapDist.Value));
            var sp = Measure(w);   // brings the layout up to date with the size just set
            if (sp == null) return;
            var ins = VisibleInsets(w);
            float x = GetF(w, "X", 0f), y = GetF(w, "Y", 0f), width = GetF(w, "Width", float.NaN), height = GetF(w, "Height", float.NaN);
            if (float.IsNaN(width) || float.IsNaN(height)) return;
            float minW = Math.Max(20f, GetF(w, "MinWidth", 0f)), minH = Math.Max(20f, GetF(w, "MinHeight", 0f));
            bool changed = false;
            // the insets are measured on the template's root panel, which is larger than Width / Height (the resize zone around the
            // window, e.g. 620 x 420 for 600 x 380): the right / bottom edges are taken from the panel's size, else they were off by
            // that difference (~20 units, ~28 px - right / bottom edges snapped that far beyond their lines)
            var panel = TemplateRootPanel(w);
            float exW = panel != null && panel.ActualWidth > 1f ? panel.ActualWidth - width : 0f;
            float exH = panel != null && panel.ActualHeight > 1f ? panel.ActualHeight - height : 0f;
            Func<float> L = () => x + ins[0], R = () => x + width + exW - ins[2], T = () => y + ins[1], Bt = () => y + height + exH - ins[3];
            if (mL)
            {
                float d = Nearest(L(), EdgeLines(sp, 0, L(), T(), R(), Bt(), dist), dist);
                if (!float.IsNaN(d) && width - d >= minW) { x += d; width -= d; changed = true; }
            }
            if (mR)
            {
                float d = Nearest(R(), EdgeLines(sp, 2, L(), T(), R(), Bt(), dist), dist);
                if (!float.IsNaN(d) && width + d >= minW) { width += d; changed = true; }
            }
            if (mT)
            {
                float d = Nearest(T(), EdgeLines(sp, 1, L(), T(), R(), Bt(), dist), dist);
                if (!float.IsNaN(d) && height - d >= minH && y + d >= 0f) { y += d; height -= d; changed = true; }
            }
            if (mB)
            {
                float d = Nearest(Bt(), EdgeLines(sp, 3, L(), T(), R(), Bt(), dist), dist);
                if (!float.IsNaN(d) && height + d >= minH) { height += d; changed = true; }
            }
            if (!changed) return;
            SetF(w, "X", x);
            SetF(w, "Y", y);
            SetF(w, "Width", width);
            SetF(w, "Height", height);
        }
    }
}
