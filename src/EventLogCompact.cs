using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// CompactEventLog (2.3.3): the Event Log window with less empty space - the window margin, the tab headers, the tab content padding and
    /// the padding of each row (Event Log and Intelligence lists) are made smaller. The sizes are chosen in screen pixels and turned into
    /// interface units with the window's own scale (ScreenScale: the game's UI scale x resolution x the window's look scale), so they are
    /// whole pixels on the screen. Everything is set on the window's own elements (the TabItems' Content are the Borders of the XAML, so no
    /// tree search); the rows get a copy of the game's row template with the smaller padding. Switching the option off puts the game's back.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _logCompact, _logWrap;
        readonly List<WeakReference> _logWindows = new List<WeakReference>();

        // the sizes in screen pixels (the game's: window margin 5, tab content 12, tab header 12 x 7.5, row 5 - in units, about 1.5 px each at 1440p)
        const float LcMargin = 3f, LcContent = 4f, LcTabH = 10f, LcTabV = 3f, LcRowH = 6f, LcRowV = 2f, LcGap = 8f;

        void BindEventLogCompactConfig()
        {
            _logCompact = Config.Bind("EventLog", "Compact", true,
                "Event Log window: smaller margins, tab headers and row spacing, so more rows fit (the text size is not changed).");
            _logWrap = Config.Bind("EventLog", "WrapLines", true,
                "Event Log window: a long line of the Event Log tab is wrapped to the window's width (the game cuts it at the window's edge).");
            _logWrap.SettingChanged += (s, e) => { foreach (var r in _logWindows.ToArray()) { var w = r.Target; if (w == null) _logWindows.Remove(r); else LogWrapApply(w); } };
            _logCompact.SettingChanged += (s, e) => { foreach (var r in _logWindows.ToArray()) { var w = r.Target; if (w == null) _logWindows.Remove(r); else LogCompactApply(w); } };
        }

        sealed class LcOrig { public Thickness Margin, TcPadding; public List<Thickness> TabPad = new List<Thickness>(); public DataTemplate T0, T1; public Thickness B2, B3; public bool Have; public bool On; public float Scale; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, LcOrig> LcStates = new System.Runtime.CompilerServices.ConditionalWeakTable<object, LcOrig>();

        /// <summary>Called once when a window turns out to be the Event Log: remembered for the option switch; applied now and when it is loaded (its scale is known on screen).</summary>
        void LogCompactHook(object w)
        {
            _logWindows.RemoveAll(r => r.Target == null);
            _logWindows.Add(new WeakReference(w));
            var fe = w as FrameworkElement;
            if (fe != null) fe.Loaded += (s, e) => LogCompactApply(w);
            LogCompactApply(w);
            LogWrapApply(w);
        }

        /// <summary>The line of one alert (a FormatBindableTextBlock the game makes without wrapping) wraps to the width of its row, or does not.</summary>
        static void LogWrapAlert(object alert, bool wrap)
        {
            var tb = alert == null ? null : Active.Get(alert, "AlertContent") as TextBlock;
            if (tb != null) tb.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }

        /// <summary>All the alerts the window shows now (the ones raised later are done in LogRaisePostfix).</summary>
        void LogWrapApply(object w)
        {
            try
            {
                var grid = Get(w, "Content") as Grid;
                var tc = grid != null && grid.Children.Count > 0 ? grid.Children[0] as TabControl : null;
                if (tc == null || tc.Items.Count == 0) return;
                var ti = tc.Items[0] as TabItem;
                var b = ti == null ? null : ti.Content as Border;
                var lb = b == null ? null : b.Child as ListBox;
                if (lb == null) return;
                bool wrap = _logWrap != null && _logWrap.Value;
                int n = 0;
                foreach (var a in lb.Items) { LogWrapAlert(a, wrap); n++; }
                Trace("event log: lines " + (wrap ? "wrap" : "do not wrap") + " (" + n + " rows)");
            }
            catch (Exception e) { Trace("event log wrap: " + e.Message); }
        }

        static string Num(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        void LogCompactApply(object w)
        {
            try
            {
                var grid = Get(w, "Content") as Grid;
                var tc = grid != null && grid.Children.Count > 0 ? grid.Children[0] as TabControl : null;
                if (tc == null) return;
                var st = LcStates.GetOrCreateValue(w);
                bool on = _logCompact != null && _logCompact.Value;
                float k = ScreenScale(w); if (k <= 0.05f) k = 1f;
                if (on == st.On && (!on || Math.Abs(k - st.Scale) < 0.01f)) return;
                var items = new List<TabItem>();
                foreach (var o in tc.Items) { var ti = o as TabItem; if (ti != null) items.Add(ti); }
                if (!st.Have)
                {
                    st.Have = true;
                    st.Margin = grid.Margin; st.TcPadding = tc.Padding;
                    foreach (var ti in items) st.TabPad.Add(ti.Padding);
                    st.T0 = ListOf(items, 0) == null ? null : ListOf(items, 0).ItemTemplate;
                    st.T1 = ListOf(items, 1) == null ? null : ListOf(items, 1).ItemTemplate;
                    var b2 = items.Count > 2 ? items[2].Content as Border : null; if (b2 != null) st.B2 = b2.Padding;
                    var b3 = items.Count > 3 ? items[3].Content as Border : null; if (b3 != null) st.B3 = b3.Padding;
                }
                st.On = on; st.Scale = k;
                if (on)
                {
                    Func<float, float> u = px => px / k;   // screen pixels -> interface units of this window
                    grid.Margin = new Thickness(u(LcMargin));
                    tc.Padding = new Thickness(u(LcContent));
                    foreach (var ti in items) ti.Padding = new Thickness(u(LcTabH), u(LcTabV), u(LcTabH), u(LcTabV));
                    string pad = Num(u(LcRowH)) + "," + Num(u(LcRowV)), gap = Num(u(LcGap));
                    var l0 = ListOf(items, 0); if (l0 != null) l0.ItemTemplate = RowTemplate(pad, gap, "<ContentControl Grid.Column=\"2\" Content=\"{Binding AlertContent}\"/>");
                    var l1 = ListOf(items, 1); if (l1 != null) l1.ItemTemplate = RowTemplate(pad, gap, "<TextBlock Grid.Column=\"2\" Text=\"{Binding IntelligenceText}\" TextWrapping=\"Wrap\"/>");
                    var b2 = items.Count > 2 ? items[2].Content as Border : null; if (b2 != null) b2.Padding = new Thickness(u(LcContent));
                    var b3 = items.Count > 3 ? items[3].Content as Border : null; if (b3 != null) b3.Padding = new Thickness(u(LcContent));
                }
                else
                {
                    grid.Margin = st.Margin; tc.Padding = st.TcPadding;
                    for (int i = 0; i < items.Count && i < st.TabPad.Count; i++) items[i].Padding = st.TabPad[i];
                    var l0 = ListOf(items, 0); if (l0 != null && st.T0 != null) l0.ItemTemplate = st.T0;
                    var l1 = ListOf(items, 1); if (l1 != null && st.T1 != null) l1.ItemTemplate = st.T1;
                    var b2 = items.Count > 2 ? items[2].Content as Border : null; if (b2 != null) b2.Padding = st.B2;
                    var b3 = items.Count > 3 ? items[3].Content as Border : null; if (b3 != null) b3.Padding = st.B3;
                }
                Trace("event log: compact " + (on ? "on" : "off") + " (" + k.ToString("0.00", CultureInfo.InvariantCulture) + " px per unit)");
            }
            catch (Exception e) { Trace("event log compact: " + e.Message); }
        }

        static ListBox ListOf(List<TabItem> items, int i)
        {
            var b = i < items.Count ? items[i].Content as Border : null;
            return b == null ? null : b.Child as ListBox;
        }

        // the game's row template (EventLogView.xaml) with our padding and column gap
        static DataTemplate RowTemplate(string pad, string gap, string body)
        {
            return (DataTemplate)GUI.ParseXaml(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Border Padding=\"" + pad + "\"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"" + gap + "\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions>" +
                "<TextBlock Grid.Column=\"0\" Text=\"{Binding Time}\"/>" + body + "</Grid></Border></DataTemplate>");
        }
    }
}
