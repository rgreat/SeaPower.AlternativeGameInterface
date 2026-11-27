using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [General] MonitorFps: a frame counter on every monitor: the game's frames per second (Unity presents every frame to every active
    /// monitor), the monitor's refresh rate and, on the main one, what the multi-monitor V-Sync paces the frames by. Updated twice a second. Each counter is a small box in the view of its monitor (the
    /// mission HUD view / the monitor's view), dragged by the mouse; its place is kept in [Debug] MonitorFpsPlaces.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _monFps, _monFpsInfo;
        float _mfAt; int _mfFrames;

        void BindMonitorFps()
        {
            _monFpsPlaces = Config.Bind("Debug", "MonitorFpsPlaces", "", "Places of the frame counters, written by the plugin: display[.view]:x,y;... (view: MainMenuView, PauseMenuView, LinearCampaignMapView; none = the mission) (UI units from the top left).");
            _monFps = Config.Bind("General", "MonitorFps", false, "A frame counter on every monitor: frames per second of the game.");
            _monFpsInfo = Config.Bind("Debug", "MonitorFpsInfo", false,
                "Diagnostic lines in the frame counter: the monitor, its refresh rate and what V-Sync is synced to.");
        }

        void MonitorFpsStep()
        {
            if (_monFps == null) return;
            if (!_monFps.Value || !Enabled) { if (_mfBox.Count > 0) MfClear(); return; }
            _mfFrames++;
            float now = UnityEngine.Time.unscaledTime;
            if (_mfAt == 0f) { _mfAt = now; return; }
            float span = now - _mfAt;
            if (span < 0.5f) return;
            double fps = _mfFrames / span;
            _mfFrames = 0; _mfAt = now;
            bool info = _monFpsInfo != null && _monFpsInfo.Value;
            string head = "FPS: " + fps.ToString("0");
            string mode; var mp = MfMainPanel(out mode);
            MfBox(0, mode, mp, info ? head + "\nmonitor 1" + MfHz(0) + MfPace() : head);
            foreach (var m in Monitors)
                MfBox(m.Index, "", m.Root as Panel, info ? head + "\nmonitor " + (m.Index + 1) + MfHz(m.Index) : head);
        }

        readonly Dictionary<int, Border> _mfBox = new Dictionary<int, Border>();
        readonly HashSet<int> _mfDrag = new HashSet<int>();                          // the boxes being dragged now (the half-second step must not put them back to the old place)
        readonly Dictionary<int, Point> _mfWant = new Dictionary<int, Point>();     // where the user put the box (it may sit more inside while the text is wider)

        /// <summary>The panel of the main view the counter goes in: the first panel of the mission HUD view (also over the windows), outside a mission
        /// the first one of the main menu / pause menu / campaign screen that is on the screen.</summary>
        Panel MfMainPanel(out string mode)
        {
            mode = "";
            var cands = new List<object> { MainGameViewInstance() };
            var modes = new List<string> { "" };    // "" = the mission (the place kept as before), the others have their own
            foreach (var n in new[] { "SeapowerUI.MainMenuView", "SeapowerUI.PauseMenuView", "SeapowerUI.LinearCampaignMapView" })
            {
                var asm = GameAssembly(); var t = asm == null ? null : asm.GetType(n);
                var f = t == null ? null : t.GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                cands.Add(f == null ? null : f.GetValue(null));
                modes.Add(n.Substring(n.LastIndexOf('.') + 1));
            }
            for (int ci = 0; ci < cands.Count; ci++)
            {
                var c = cands[ci];
                var fe = c as FrameworkElement;
                if (fe == null || !fe.IsLoaded || fe.ActualWidth < 10) continue;
                var v = fe as DependencyObject;
                for (int i = 0; v != null && i < 4; i++)
                {
                    if (v is Panel) { mode = modes[ci]; return (Panel)v; }
                    var cc = v as ContentControl;
                    if (cc != null) { v = cc.Content as DependencyObject; continue; }
                    v = VisualTreeHelper.GetChildrenCount(v) > 0 ? VisualTreeHelper.GetChild(v, 0) : null;
                }
            }
            return null;
        }

        /// <summary>The small counter window of display d in the panel: made on the first call (dragged by the mouse, its place kept in
        /// [Debug] MonitorFpsPlaces), its text set.</summary>
        void MfBox(int d, string mode, Panel panel, string text)
        {
            if (panel == null) return;
            Border b;
            if (_mfBox.TryGetValue(d, out b) && b != null && ReferenceEquals(b.Parent, panel))
            {
                ((TextBlock)b.Child).Text = text;
                if (_mfDrag.Contains(d)) return;                       // dragged now: the box follows the mouse, _mfWant is the place before the drag
                Point want; if (!_mfWant.TryGetValue(d, out want)) want = new Point(b.Margin.Left, b.Margin.Top);
                var m = MfInside(b, (float)want.X, (float)want.Y);     // the view got smaller / the text longer: back inside, and back to the place when it fits again
                if (Math.Abs(m.Left - b.Margin.Left) > 0.5f || Math.Abs(m.Top - b.Margin.Top) > 0.5f) b.Margin = m;
                return;
            }
            if (b != null) { var old = b.Parent as Panel; if (old != null) old.Children.Remove(b); }
            var t = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 230, 0)), FontSize = 14 };
            b = new Border
            {
                Child = t, Background = new SolidColorBrush(Color.FromArgb(190, 20, 20, 20)), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                BorderThickness = new Thickness(1), Padding = new Thickness(6, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.SizeAll
            };
            float x, y;
            string key = d + (mode.Length > 0 ? "." + mode : "");
            if (!MfPlace(key, out x, out y)) { x = 8f; y = 40f; }
            b.Margin = new Thickness(x, y, 0, 0); _mfWant[d] = new Point(x, y);
            Panel.SetZIndex(b, 100000);
            if (panel is Grid) { Grid.SetRowSpan(b, 99); Grid.SetColumnSpan(b, 99); }
            bool down = false; Point p0 = new Point(); Thickness m0 = new Thickness();
            b.MouseLeftButtonDown += (o, e) =>
            {
                var par = b.Parent as UIElement; if (par == null) return;
                down = true; _mfDrag.Add(d); p0 = e.GetPosition(par); m0 = b.Margin; b.CaptureMouse(); e.Handled = true;
            };
            b.LostMouseCapture += (o, e) => { if (down) { down = false; _mfDrag.Remove(d); _mfWant[d] = new Point(b.Margin.Left, b.Margin.Top); } };
            b.MouseMove += (o, e) =>
            {
                if (!down) return;
                var par = b.Parent as FrameworkElement; if (par == null) return;
                var q = e.GetPosition(par);
                b.Margin = MfInside(b, m0.Left + q.X - p0.X, m0.Top + q.Y - p0.Y);
            };
            b.MouseLeftButtonUp += (o, e) =>
            {
                if (!down) return;
                down = false; _mfDrag.Remove(d); b.ReleaseMouseCapture(); e.Handled = true;
                _mfWant[d] = new Point(b.Margin.Left, b.Margin.Top);
                MfSavePlace(key, b.Margin.Left, b.Margin.Top);
            };
            panel.Children.Add(b);
            _mfBox[d] = b;
        }

        /// <summary>The margin that keeps the box wholly inside the screen of its view (the top element of its visual tree, not the panel it sits in:
        /// the panel can be bigger than the screen or offset), for the wanted margin x, y. Worked out from the screen rectangles, so a scale above it is allowed for.</summary>
        static Thickness MfInside(Border b, float x, float y)
        {
            var cur = b.Margin;
            float w0 = (float)b.ActualWidth, h0 = (float)b.ActualHeight;
            if (w0 < 1f || h0 < 1f) return new Thickness(x, y, 0, 0);          // not laid out yet: the next call corrects it
            b.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));  // the text just changed (more digits): the size it is going to have
            float w = Math.Max(1f, (float)(b.DesiredSize.Width - cur.Left - cur.Right)), h = Math.Max(1f, (float)(b.DesiredSize.Height - cur.Top - cur.Bottom));     // DesiredSize includes the margin
            FrameworkElement root = null;
            for (DependencyObject v = b; v != null; v = VisualTreeHelper.GetParent(v)) { var fe = v as FrameworkElement; if (fe != null) root = fe; }
            if (root == null || root.ActualWidth < 1 || root.ActualHeight < 1) return new Thickness(x, y, 0, 0);
            var a = b.PointToScreen(new Point(0f, 0f)); var c0 = b.PointToScreen(new Point(w0, h0));
            var r0 = root.PointToScreen(new Point(0f, 0f)); var r1 = root.PointToScreen(new Point((float)root.ActualWidth, (float)root.ActualHeight));
            float sx = (c0.X - a.X) / w0, sy = (c0.Y - a.Y) / h0;
            if (sx < 0.0001f) sx = 1f;
            if (sy < 0.0001f) sy = 1f;
            var c = new Point(a.X + w * sx, a.Y + h * sy);                        // the right / bottom edge with the new size
            float dx = (x - cur.Left) * sx, dy = (y - cur.Top) * sy;             // the wanted move in screen pixels
            if (c.X + dx > r1.X) dx -= c.X + dx - r1.X;
            if (a.X + dx < r0.X) dx += r0.X - (a.X + dx);                         // the left edge wins when the box is wider than the screen
            if (c.Y + dy > r1.Y) dy -= c.Y + dy - r1.Y;
            if (a.Y + dy < r0.Y) dy += r0.Y - (a.Y + dy);
            return new Thickness(cur.Left + dx / sx, cur.Top + dy / sy, 0, 0);
        }

        /// <summary>The main counter's second line: what paces the frames (multi-monitor V-Sync: the monitor it syncs to).</summary>
        string MfPace()
        {
            if (!_mvOn) return "";
            var w = _mvW;
            if (w == null || w.Period <= 0) return "\nV-Sync: waiting for the monitors";
            return "\nV-Sync " + (_mvPace ? "by" : "off, fast frames; monitor") + " " + (w.Index + 1) + " (" + (System.Diagnostics.Stopwatch.Frequency / (double)w.Period).ToString("0") + " Hz)";
        }

        /// <summary>A monitor's refresh rate, when its watcher measured it.</summary>
        string MfHz(int d)
        {
            var w = _mvOn ? VbWatcher_(d) : null;
            return w == null || w.Period <= 0 ? "" : " (" + (System.Diagnostics.Stopwatch.Frequency / (double)w.Period).ToString("0") + " Hz)";
        }

        ConfigEntry<string> _monFpsPlaces;

        bool MfPlace(string d, out float x, out float y)
        {
            x = y = 0f;
            foreach (var rec in (_monFpsPlaces.Value ?? "").Split(';'))
            {
                var p = rec.Split(':');
                if (p.Length != 2 || p[0] != d) continue;
                var xy = p[1].Split(',');
                return xy.Length == 2 && float.TryParse(xy[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
                       float.TryParse(xy[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
            }
            return false;
        }

        void MfSavePlace(string d, float x, float y)
        {
            var keep = new List<string>();
            foreach (var rec in (_monFpsPlaces.Value ?? "").Split(';')) if (rec.Length > 0 && !rec.StartsWith(d + ":")) keep.Add(rec);
            keep.Add(d + ":" + Math.Round(x).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + Math.Round(y).ToString(System.Globalization.CultureInfo.InvariantCulture));
            _monFpsPlaces.Value = string.Join(";", keep.ToArray());
        }

        void MfClear()
        {
            _mfAt = 0f; _mfFrames = 0;
            foreach (var kv in _mfBox)
                try { var p = kv.Value == null ? null : kv.Value.Parent as Panel; if (p != null) p.Children.Remove(kv.Value); } catch { }
            _mfBox.Clear(); _mfDrag.Clear();
        }
    }
}
