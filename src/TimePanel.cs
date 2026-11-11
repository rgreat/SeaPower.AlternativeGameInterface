using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// DragTimePanel (1.7.0): the mission HUD's time panel (TimeCompressionView: date / time, x speed, rewind, play / pause, fast forward) sits in the
    /// top right corner (Canvas.Top / Canvas.Right of its Grid). Pressing it anywhere but on a button and dragging moves it (no resizing); a
    /// double-click puts it back. The place is remembered (TimePanelPosition).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _timeDrag;
        const string TimeKey = "Time Panel";      // its place is kept like a window's: [Layouts] and the desktops (X, Y from the left / top; no size)
        const float TimeDefR = 6.4f, TimeDefT = 26f;   // desktop mode, nothing saved: a little lower and to the left of the corner (distance from the right / top)
        WeakReference _timeGrid;
        float _timeNext; bool _timeBusy;
        static readonly ConditionalWeakTable<object, object> TimeAttached = new ConditionalWeakTable<object, object>();

        void BindTimePanelConfig()
        {
            _timeDrag = Config.Bind("InMission", "DragTimePanel", true,
                "The time panel of the mission HUD (date / time, speed, play / pause) can be dragged with the mouse by its frame, the speed text or the gaps between its buttons; " +
                "double-click it to put it back in the corner.");
            // the place: [Layouts] "Time Panel" = x,y,auto,auto (written by the plugin; empty = the game's place, in desktop mode a little lower and to the left)
            var e = BindLayout(TimeKey);
            Layout l;
            if (Layout.TryParse(e.Value, out l)) _saved[TimeKey] = l;
        }

        void TimePanelStep(float now)
        {
            if (_timeDrag == null || !_timeDrag.Value || now < _timeNext) return;
            _timeNext = now + 0.5f;
            try
            {
                var grid = _timeGrid == null ? null : _timeGrid.Target as Grid;
                if (grid != null && !grid.IsLoaded) grid = null;
                if (grid == null)
                {
                    if (!InMission()) return;
                    var view = MainGameViewInstance() as FrameworkElement;
                    if (view == null || !view.IsLoaded) return;
                    grid = FindTimeGrid(view);
                    if (grid == null) return;
                    _timeGrid = new WeakReference(grid);
                }
                object done;
                if (!TimeAttached.TryGetValue(grid, out done))
                {
                    TimeAttached.Add(grid, this);
                    Attach(grid);
                    Trace("time panel: draggable");
                }
                if (!_timeBusy) ApplyTimePos(grid);
            }
            catch (Exception e) { _timeNext = now + 5f; Trace("TimePanelStep: " + e.Message); }
        }

        static Grid FindTimeGrid(FrameworkElement view)
        {
            var stack = new Stack<DependencyObject>(); stack.Push(view);
            int budget = 6000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                if (e is Canvas)
                {
                    var dc = ((FrameworkElement)e).DataContext;
                    if (dc != null && dc.GetType().Name == "TimeCompressionViewModel" && VisualTreeHelper.GetChildrenCount(e) > 0)
                    {
                        var g = VisualTreeHelper.GetChild(e, 0) as Grid;
                        if (g != null) return g;
                    }
                }
                for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            return null;
        }

        /// <summary>The time panel's place now (X from the left, Y from the top), for the desktop snapshot.</summary>
        bool TimePanelNow(out Layout l)
        {
            l = default(Layout);
            var grid = _timeGrid == null ? null : _timeGrid.Target as Grid;
            if (grid == null || !grid.IsLoaded) return false;
            var canvas = VisualTreeHelper.GetParent(grid) as FrameworkElement;
            if (canvas == null || canvas.ActualWidth < 1f) return false;
            l = new Layout { X = (float)Math.Round(canvas.ActualWidth - NaN0(Canvas.GetRight(grid)) - grid.ActualWidth), Y = (float)Math.Round(NaN0(Canvas.GetTop(grid))), W = float.NaN, H = float.NaN };
            return true;
        }

        static float NaN0(float v) { return float.IsNaN(v) ? 0f : v; }

        // the saved place, kept inside the canvas
        void ApplyTimePos(Grid grid)
        {
            var canvas = VisualTreeHelper.GetParent(grid) as FrameworkElement;
            if (canvas == null || canvas.ActualWidth < 1f) return;
            float r = NaN0(Canvas.GetRight(grid)), t = NaN0(Canvas.GetTop(grid));
            Layout l;
            if (_saved.TryGetValue(TimeKey, out l) && !float.IsNaN(l.X) && !float.IsNaN(l.Y)) { r = canvas.ActualWidth - l.X - grid.ActualWidth; t = l.Y; }
            else if (_windowedMap != null && _windowedMap.Value) { r = TimeDefR; t = TimeDefT; }
            ClampTime(grid, canvas, ref r, ref t);
            if (Math.Abs(NaN0(Canvas.GetRight(grid)) - r) > 0.01f) Canvas.SetRight(grid, r);
            if (Math.Abs(NaN0(Canvas.GetTop(grid)) - t) > 0.01f) Canvas.SetTop(grid, t);
            MoveTimeFollowers(grid);
        }

        // 0.9.0: the mission timer is a second child of the same Canvas, pinned to the corner under the controls (Canvas.Right = 0, Canvas.Top bound to the
        // controls' height). It follows the panel: the same Canvas.Right, and the panel's Canvas.Top added to its own top margin (its Top binding stays).
        static readonly ConditionalWeakTable<object, object> TimeFollowerMargin = new ConditionalWeakTable<object, object>();

        static void MoveTimeFollowers(Grid grid)
        {
            var canvas = VisualTreeHelper.GetParent(grid) as Canvas;
            if (canvas == null) return;
            float r = NaN0(Canvas.GetRight(grid)), t = NaN0(Canvas.GetTop(grid));
            foreach (var c in canvas.Children)
            {
                var f = c as FrameworkElement;
                if (f == null || ReferenceEquals(f, grid)) continue;
                object m0;
                if (!TimeFollowerMargin.TryGetValue(f, out m0)) { m0 = f.Margin; TimeFollowerMargin.Add(f, m0); }
                var m = (Thickness)m0;
                if (Math.Abs(NaN0(Canvas.GetRight(f)) - r) > 0.01f) Canvas.SetRight(f, r);
                var want = new Thickness(m.Left, m.Top + t, m.Right, m.Bottom);
                if (Math.Abs(f.Margin.Top - want.Top) > 0.01f) f.Margin = want;
            }
        }

        static void ClampTime(Grid grid, FrameworkElement canvas, ref float r, ref float t)
        {
            r = Math.Max(0f, Math.Min(r, Math.Max(0f, canvas.ActualWidth - grid.ActualWidth)));
            t = Math.Max(0f, Math.Min(t, Math.Max(0f, canvas.ActualHeight - grid.ActualHeight)));
        }

        void Attach(Grid grid)
        {
            bool down = false; Point st0 = new Point(); float r0 = 0f, t0 = 0f;
            grid.MouseLeftButtonDown += (o, e) =>
            {
                var canvas = VisualTreeHelper.GetParent(grid) as FrameworkElement;
                if (canvas == null) return;
                if (e.ClickCount >= 2)
                {
                    _saved.Remove(TimeKey);
                    ConfigEntry<string> le;
                    if (_layoutEntries.TryGetValue(TimeKey, out le)) le.Value = "";
                    Canvas.SetRight(grid, 0f); Canvas.SetTop(grid, 0f);
                    ApplyTimePos(grid);        // the place it has without a saved one
                    _dirty = true; _dirtySince = UnityEngine.Time.unscaledTime;
                    e.Handled = true; return;
                }
                down = true; _timeBusy = true; st0 = e.GetPosition(canvas); r0 = NaN0(Canvas.GetRight(grid)); t0 = NaN0(Canvas.GetTop(grid));
                grid.CaptureMouse(); e.Handled = true;
            };
            grid.MouseMove += (o, e) =>
            {
                if (!down) return;
                var canvas = VisualTreeHelper.GetParent(grid) as FrameworkElement;
                if (canvas == null) return;
                var q = e.GetPosition(canvas);
                float r = r0 - (q.X - st0.X), t = t0 + (q.Y - st0.Y);
                ClampTime(grid, canvas, ref r, ref t);
                Canvas.SetRight(grid, r); Canvas.SetTop(grid, t);
                MoveTimeFollowers(grid);
            };
            grid.MouseLeftButtonUp += (o, e) =>
            {
                if (!down) return;
                down = false; _timeBusy = false; grid.ReleaseMouseCapture(); e.Handled = true;
                var canvas = VisualTreeHelper.GetParent(grid) as FrameworkElement;
                if (canvas == null) return;
                _saved[TimeKey] = new Layout { X = (float)Math.Round(canvas.ActualWidth - NaN0(Canvas.GetRight(grid)) - grid.ActualWidth), Y = (float)Math.Round(NaN0(Canvas.GetTop(grid))), W = float.NaN, H = float.NaN };
                _dirty = true; _dirtySince = UnityEngine.Time.unscaledTime;
            };
        }
    }
}
