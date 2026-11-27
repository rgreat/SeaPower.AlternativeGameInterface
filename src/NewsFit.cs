using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// FitNewsWindow (2.3.3): the main menu's News window takes the free area of the menu screen - from the right edge of the menu column to
    /// the right edge of the screen, from the top of the screen down to the loading tip bar (above the version line when there is no tip).
    /// Done once per window after the game has centred it, and again when the screen size changes. The first time (no look of the News
    /// window remembered yet) it also gets a look: transparent (0 %), content 120 %, text contrast on; changed looks are kept.
    /// The places are measured on the screen (the menu column, the tip bar) and turned into the window canvas' units (PointFromScreen),
    /// with the window's visible insets (its template keeps a resize zone around it).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _newsFit;
        sealed class NewsState { public float CW, CH, K, L, T, R, B, At; public int Phase; }
        static readonly ConditionalWeakTable<object, NewsState> NewsFitted = new ConditionalWeakTable<object, NewsState>();   // window -> the canvas size it was fitted for, the target on the screen

        const float NewsGapPx = 6f;   // screen pixels left free above the tip bar

        void BindNewsFitConfig()
        {
            _newsFit = Config.Bind("General", "FitNewsWindow", true,
                "Main menu: the News window fills the free area right of the menu, from the top down to the loading tip bar, fully transparent with the content at 120 %. Switched off by itself when the user moves, resizes or restyles the News window after it was switched on; switching it on forgets the window's own place, size and look.");
            _newsFit.SettingChanged += (s, e) =>
            {
                if (!_newsFit.Value || _newsSwitching) return;
                // switched on: the user's own place, size and look of the News window are forgotten, the mode is applied again
                foreach (var t in _tracked) { var w = t.Ref.Target; if (w != null && IsNewsWindow(w)) { NewsFitted.Remove(w); NewsReset(w); NewsFitNow(w, UnityEngine.Time.unscaledTime); } }
            };
        }

        bool IsNewsWindow(object w)
        {
            try { var dc = Get(w, "DataContext"); return dc != null && dc.GetType().Name == "NewsWindowViewModel"; }
            catch { return false; }
        }

        static bool _newsSwitching;

        /// <summary>The user moved, resized or restyled the News window: the mode is switched off (his window is his).</summary>
        void NewsUserChanged(object w, string what)
        {
            try
            {
                if (_newsFit == null || !_newsFit.Value || w == null || !IsNewsWindow(w)) return;
                _newsSwitching = true;
                try { _newsFit.Value = false; } finally { _newsSwitching = false; }
                NewsShow(w);
                Trace("news fit: switched off - the News window was " + what + " by the user");
            }
            catch (Exception e) { Trace("news fit (user change): " + e.Message); }
        }

        /// <summary>Forgets the News window's own remembered place / size and look (switching the mode on).</summary>
        void NewsReset(object w)
        {
            string label, key = MatchPattern(w, out label);
            if (key == null) return;
            ResetLayouts(key);
            if (LookMap().Remove(key)) SaveLooks();
            Trace("news fit: the News window's own place, size and look forgotten");
        }

        /// <summary>Window Loaded (after its first layout, before its first frame is drawn): the News window is hidden and fitted right here,
        /// so it does not show first where the game put it and jump; it is shown again by the next step, once laid out at its new place.</summary>
        void NewsFitLoaded(object w)
        {
            if (_newsFit == null || !_newsFit.Value || !IsNewsWindow(w)) return;
            var fe = w as FrameworkElement;
            if (fe == null) return;
            if (!NewsHidden.TryGetValue(w, out _)) { NewsHidden.Add(w, new[] { fe.Opacity, UnityEngine.Time.unscaledTime }); fe.Opacity = 0f; }
            NewsFitNow(w, UnityEngine.Time.unscaledTime);
        }

        static readonly ConditionalWeakTable<object, object> NewsHidden = new ConditionalWeakTable<object, object>();   // window -> its opacity while it waits to be fitted

        void NewsShow(object w)
        {
            object o;
            if (!NewsHidden.TryGetValue(w, out o)) return;
            NewsHidden.Remove(w);
            var fe = w as FrameworkElement;
            var v = o as float[];
            if (fe != null) fe.Opacity = v != null ? v[0] : 1f;
        }

        void NewsFitStep(Tracked t, object w, float now)
        {
            if (_newsFit == null || !_newsFit.Value) { NewsShow(w); return; }
            if (!t.WasActive || GetF(w, "ActualWidth", 0f) < 1f || !IsNewsWindow(w)) return;
            NewsState ns;
            if (NewsFitted.TryGetValue(w, out ns) && ns.Phase == 1 && now >= ns.At) NewsCorrect(w, ns);
            NewsFitNow(w, now);
            if (NewsFitted.TryGetValue(w, out ns) && ns.Phase != 1) NewsShow(w);
            else
            {
                // never left hidden: shown after 2 s whatever happened (the menu not found, no room)
                object o; var v = NewsHidden.TryGetValue(w, out o) ? o as float[] : null;
                if (v != null && now - v[1] > 2f) { Trace("news fit: shown without a fit"); NewsShow(w); }
            }
        }

        void NewsFitNow(object w, float now)
        {
            var fe = w as FrameworkElement;
            if (fe == null || fe.ActualWidth < 1f) return;
            var canvas = VisualTreeHelper.GetParent(fe) as FrameworkElement;
            while (canvas != null && !(canvas is Canvas)) canvas = VisualTreeHelper.GetParent(canvas) as FrameworkElement;
            if (canvas == null || canvas.ActualWidth < 1f) return;
            NewsState ns;
            if (NewsFitted.TryGetValue(w, out ns) && Math.Abs(ns.CW - canvas.ActualWidth) < 0.5f && Math.Abs(ns.CH - canvas.ActualHeight) < 0.5f) return;
            try
            {
                var root = canvas;
                for (var p = VisualTreeHelper.GetParent(root) as FrameworkElement; p != null; p = VisualTreeHelper.GetParent(p) as FrameworkElement) root = p;
                // the menu column (MainMenuView's ScrollViewer "scrollViewer", in column 0 with the dark panel) and the tip bar (the 800 wide Border on its Canvas)
                var menu = FindFirst(root, d => d is ScrollViewer && ((ScrollViewer)d).Name == "scrollViewer") as FrameworkElement;
                var tip = FindFirst(root, d => { var b = d as Border; return b != null && Math.Abs(b.Width - 800f) < 0.5f && VisualTreeHelper.GetParent(b) is Canvas && b.Visibility == Visibility.Visible; }) as FrameworkElement;
                if (menu == null || menu.ActualWidth < 1f) return;   // the menu is not laid out yet: the next step tries again
                NewsFitted.Remove(w); ns = new NewsState { CW = canvas.ActualWidth, CH = canvas.ActualHeight, Phase = 2 }; NewsFitted.Add(w, ns);
                float k = ScreenScale(canvas); if (k <= 0.05f) k = 1f;
                var mRight = menu.PointToScreen(new Point(menu.ActualWidth, 0f));
                var cTop = canvas.PointToScreen(new Point(0f, 0f));
                var cEnd = canvas.PointToScreen(new Point(canvas.ActualWidth, canvas.ActualHeight));
                float bottomPx = tip != null && tip.ActualHeight > 1f ? tip.PointToScreen(new Point(0f, 0f)).Y - NewsGapPx : cEnd.Y - 30f * k;
                var a = canvas.PointFromScreen(new Point(mRight.X, cTop.Y));
                var b2 = canvas.PointFromScreen(new Point(cEnd.X, bottomPx));
                // the window's visible edges as they are now (the title bar's top, the frame's sides and bottom), in canvas units, against its X / Y / Width / Height:
                // the offsets carry over to the new place exactly (no estimate, no second step)
                float x0 = GetF(w, "X", 0f), y0 = GetF(w, "Y", 0f), w0 = GetF(w, "Width", fe.ActualWidth), h0 = GetF(w, "Height", fe.ActualHeight);
                float oL, oT, oR, oB; bool measured = false;
                object fb; var fr = Borders.TryGetValue(w, out fb) ? fb as FrameworkElement : null;
                var thumb = Get(w, "HeaderThumb") as FrameworkElement;
                if (fr != null && fr.ActualWidth > 1f && thumb != null && thumb.ActualHeight > 1f && !float.IsNaN(w0) && !float.IsNaN(h0))
                {
                    var f0 = canvas.PointFromScreen(fr.PointToScreen(new Point(0f, 0f)));
                    var f1 = canvas.PointFromScreen(fr.PointToScreen(new Point(fr.ActualWidth, fr.ActualHeight)));
                    var th = canvas.PointFromScreen(thumb.PointToScreen(new Point(0f, 0f)));
                    oL = f0.X - x0; oT = th.Y - y0; oR = x0 + w0 - f1.X; oB = y0 + h0 - f1.Y;
                    measured = true;
                }
                else
                {
                    var ins = VisibleInsets(w);
                    oL = ins[0]; oT = ins[1]; oR = ins[2]; oB = ins[3];
                }
                float x = a.X - oL, y = a.Y - oT, wdt = b2.X - a.X + oL + oR, hgt = b2.Y - a.Y + oT + oB;
                if (wdt < 200f || hgt < 150f) { Trace("news fit: no room (" + Fmt(wdt) + " x " + Fmt(hgt) + ")"); return; }
                SetF(w, "MaxWidth", float.PositiveInfinity); SetF(w, "MaxHeight", float.PositiveInfinity);
                SetF(w, "X", x); SetF(w, "Y", y); SetF(w, "Width", wdt); SetF(w, "Height", hgt);
                ns.K = k; ns.L = mRight.X; ns.T = cTop.Y; ns.R = cEnd.X; ns.B = bottomPx;
                if (!measured) { ns.Phase = 1; ns.At = now + 0.2f; }   // the insets are an estimate: checked against the frame once laid out
                Trace("news fit: " + Fmt(x) + "," + Fmt(y) + " " + Fmt(wdt) + " x " + Fmt(hgt) + " (tip bar " + (tip != null ? "found" : "none") + ", edges " + (measured ? "measured" : "estimated") + ")");
                // the look: fully transparent, content 120 %, text contrast (the window's own place and look, if any are kept, are forgotten first:
                // a tick means the fit, whatever was there before; the user's own change of the window switches the tick off)
                string label, key = MatchPattern(w, out label);
                if (key != null)
                {
                    bool own = _saved.ContainsKey(key);
                    float[] lk; bool same = LookMap().TryGetValue(key, out lk) && lk != null && lk.Length >= 3 && lk[0] == 0f && lk[1] == 1.2f && lk[2] == 1f;
                    if (own) ResetLayouts(key);
                    if (!same)
                    {
                        LookMap()[key] = new[] { 0f, 1.2f, 1f };
                        SaveLooks();
                        LookState ls;
                        var panel = Looks.TryGetValue(w, out ls) && ls.Panel != null ? ls.Panel.Target as Panel : null;
                        if (panel != null) { ls.Next = 0f; ApplyLook(w, panel); }
                    }
                    if (own || !same) Trace("news fit: " + (own ? "the window's own place forgotten, " : "") + "look 0 % / 120 % / contrast");
                }
            }
            catch (Exception e) { Trace("news fit: " + e.Message); }
        }

        /// <summary>The window as laid out: its visible part (the mod's frame around it for the sides and the bottom, the title bar for the top) is measured on the screen and the rest of the
        /// difference to the target is taken off (the visible insets are an estimate: header, frame and resize zone).</summary>
        void NewsCorrect(object w, NewsState ns)
        {
            ns.Phase = 2;
            try
            {
                object b;
                var fr = Borders.TryGetValue(w, out b) ? b as FrameworkElement : null;
                if (fr == null || fr.ActualWidth < 1f) { Trace("news fit: no frame to check against"); return; }
                var p0 = fr.PointToScreen(new Point(0f, 0f));
                var p1 = fr.PointToScreen(new Point(fr.ActualWidth, fr.ActualHeight));
                float dl = p0.X - ns.L, dt = p0.Y - ns.T, dr = p1.X - ns.R, db = p1.Y - ns.B, k = ns.K;
                // the frame sits on the body, below the title bar: the top is the title bar's (HeaderThumb) top (the frame's put the title bar above the screen)
                var thumb = Get(w, "HeaderThumb") as FrameworkElement;
                if (thumb != null && thumb.ActualHeight > 1f) dt = thumb.PointToScreen(new Point(0f, 0f)).Y - ns.T;
                if (Math.Abs(dl) < 0.5f && Math.Abs(dt) < 0.5f && Math.Abs(dr) < 0.5f && Math.Abs(db) < 0.5f) return;
                SetF(w, "X", GetF(w, "X", 0f) - dl / k); SetF(w, "Y", GetF(w, "Y", 0f) - dt / k);
                SetF(w, "Width", GetF(w, "Width", 0f) - (dr - dl) / k); SetF(w, "Height", GetF(w, "Height", 0f) - (db - dt) / k);
                Trace("news fit: corrected by the frame " + Fmt(dl) + "," + Fmt(dt) + "," + Fmt(dr) + "," + Fmt(db) + " px");
            }
            catch (Exception e) { Trace("news fit (check): " + e.Message); }
        }
    }
}
