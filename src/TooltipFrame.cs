using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// [WindowBorder] ToolTips: a frame around the game's tooltips (pop-up hints), on all four sides, with the width and colour of
    /// the window frame. A class handler of ToolTip.Opened (every tooltip of the UI, also the mods' own); the game's tooltip
    /// template (NoesisTheme.Styles.xaml, Template.ToolTip) is a Border with Background and Padding only: its BorderBrush /
    /// BorderThickness are set. Another template: a frame Border is laid over its root panel. Applied each time a tooltip opens,
    /// so a change of the settings shows on the next one.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _tipBorder;
        static bool _tipHooked;
        static readonly ConditionalWeakTable<object, object> TipOwn = new ConditionalWeakTable<object, object>();   // Borders we drew on

        void HookToolTips()
        {
            if (_tipHooked) return;
            _tipHooked = true;
            try
            {
                EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(ToolTipOpened));
                Trace("tooltip frame: ToolTip.Opened class handler registered");
            }
            catch (Exception e) { Logger.LogWarning("Tooltip frames not available: " + e.Message); }
        }

        static void ToolTipOpened(object sender, RoutedEventArgs e)
        {
            var a = Active;
            if ((object)a == null) return;
            try { a.FrameToolTip(sender as ToolTip); } catch (Exception ex) { Trace("tooltip frame: " + ex.Message); }
        }

        void FrameToolTip(ToolTip tip)
        {
            if (tip == null || VisualTreeHelper.GetChildrenCount(tip) == 0) return;
            var root = VisualTreeHelper.GetChild(tip, 0);
            bool on = _tipBorder != null && _tipBorder.Value;
            float t = TipThickness();
            var b = root as Border;
            object own;
            if (b != null)
            {
                bool ours = TipOwn.TryGetValue(b, out own);
                var cur = b.BorderThickness;
                if (!ours && (cur.Left > 0f || cur.Top > 0f || cur.Right > 0f || cur.Bottom > 0f)) return;   // a template with its own frame
                if (!on) { if (ours) { b.BorderThickness = new Thickness(0f); TipOwn.Remove(b); } return; }
                b.BorderBrush = new SolidColorBrush(TipColor());
                b.BorderThickness = new Thickness(t);
                if (!ours) TipOwn.Add(b, b);
                return;
            }
            var panel = root as Panel;
            if (panel == null) return;
            Border frame = null;
            foreach (var c in panel.Children) { var cb = c as Border; if (cb != null && TipOwn.TryGetValue(cb, out own)) { frame = cb; break; } }
            if (!on) { if (frame != null) panel.Children.Remove(frame); return; }
            if (frame == null)
            {
                frame = new Border { IsHitTestVisible = false, Focusable = false, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                var g = panel as Grid;
                if (g != null)
                {
                    Grid.SetRowSpan(frame, Math.Max(1, g.RowDefinitions.Count));
                    Grid.SetColumnSpan(frame, Math.Max(1, g.ColumnDefinitions.Count));
                }
                Panel.SetZIndex(frame, 100000);
                panel.Children.Add(frame);
                TipOwn.Add(frame, frame);
            }
            frame.BorderBrush = new SolidColorBrush(TipColor());
            frame.BorderThickness = new Thickness(t);
        }

        float TipThickness()
        {
            float t = _borderThickness.Value;
            return float.IsNaN(t) || t < 0.5f || t > 10f ? 1f : t;
        }

        Color TipColor()
        {
            var c = (_borderColor.Value ?? "").Trim();
            bool ok = (c.Length == 7 || c.Length == 9) && c[0] == '#';
            for (int i = 1; ok && i < c.Length; i++) ok = Uri.IsHexDigit(c[i]);
            if (!ok) c = DefaultBorderColor;
            uint v = uint.Parse(c.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (c.Length == 7) v |= 0xFF000000u;
            return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
    }
}
