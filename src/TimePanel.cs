using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// DragTimePanel (1.7.0): the mission HUD's time panel (TimeCompressionView: date / time, x speed, rewind, play / pause, fast forward) sits in the
    /// top right corner (Canvas.Top / Canvas.Right of its Grid). Pressing it anywhere but on a button and dragging moves it (no resizing); a
    /// double-click puts it back. The place is remembered (TimePanelPosition).
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _timeDrag;
        ConfigEntry<string> _timePos;
        WeakReference _timeGrid;
        float _timeNext; bool _timeBusy;
        static readonly ConditionalWeakTable<object, object> TimeAttached = new ConditionalWeakTable<object, object>();

        void BindTimePanelConfig()
        {
            _timeDrag = Config.Bind("InMission", "DragTimePanel", true,
                "The time panel of the mission HUD (date / time, speed, play / pause) can be dragged with the mouse by its frame, the speed text or the gaps between its buttons; " +
                "double-click it to put it back in the corner.");
            _timePos = Config.Bind("InMission", "TimePanelPosition", "",
                "Written by the plugin: distance of the time panel from the right and from the top edge (interface units). Empty = the game's place.");
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

        static float NaN0(float v) { return float.IsNaN(v) ? 0f : v; }

        // the saved place, kept inside the canvas
        void ApplyTimePos(Grid grid)
        {
            var canvas = VisualTreeHelper.GetParent(grid) as FrameworkElement;
            if (canvas == null || canvas.ActualWidth < 1f) return;
            float r = NaN0(Canvas.GetRight(grid)), t = NaN0(Canvas.GetTop(grid));
            string s = _timePos.Value;
            if (!string.IsNullOrEmpty(s))
            {
                var p = s.Split(',');
                float a, b;
                if (p.Length == 2 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a) && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b)) { r = a; t = b; }
            }
            ClampTime(grid, canvas, ref r, ref t);
            if (Math.Abs(NaN0(Canvas.GetRight(grid)) - r) > 0.01f) Canvas.SetRight(grid, r);
            if (Math.Abs(NaN0(Canvas.GetTop(grid)) - t) > 0.01f) Canvas.SetTop(grid, t);
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
                    _timePos.Value = "";
                    Canvas.SetRight(grid, 0f); Canvas.SetTop(grid, 0f);
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
            };
            grid.MouseLeftButtonUp += (o, e) =>
            {
                if (!down) return;
                down = false; _timeBusy = false; grid.ReleaseMouseCapture(); e.Handled = true;
                _timePos.Value = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#},{1:0.#}", NaN0(Canvas.GetRight(grid)), NaN0(Canvas.GetTop(grid)));
            };
        }
    }
}
