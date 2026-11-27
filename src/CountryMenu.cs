using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// The country drop-down of the Unit Reference (with ImprovedUnitReferenceUI): the game's combo box is kept (hidden, its binding stays) and
    /// replaced by a drop-down of the mod: flag, name and the number of units found in the open tab; its menu has the sort switch on top
    /// ("A-Z" / "9-1") and the countries below it (flag, name, count). The counts come from SyncNations (TreeSearch.cs).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        sealed class NatInfo { public readonly Dictionary<object, int> Count = new Dictionary<object, int>(); public int Total = -1; public Action Refresh; }
        static readonly ConditionalWeakTable<object, NatInfo> NatInfos = new ConditionalWeakTable<object, NatInfo>();

        static readonly Color MenuBack = Color.FromRgb(30, 33, 38), MenuLine = Color.FromRgb(70, 76, 86), MenuText = Color.FromRgb(230, 232, 235),
            MenuDim = Color.FromRgb(150, 156, 166), MenuHot = Color.FromRgb(62, 69, 79), MenuOn = Color.FromRgb(46, 98, 150);
        static SolidColorBrush MB(Color c) { return new SolidColorBrush(c); }

        void NatRefreshFor(object browser)
        {
            NatInfo ni;
            if (browser != null && NatInfos.TryGetValue(browser, out ni) && ni.Refresh != null) ni.Refresh();
        }

        /// <summary>Replaces the game's country combo box by the mod's drop-down. Returns false when it could not.</summary>
        bool BuildCountryMenu(object browser, ComboBox combo, StackPanel host)
        {
            var ni = NatInfos.GetOrCreateValue(browser);
            var bref = new WeakReference(browser);
            float fs = combo.FontSize;

            var flag = new Image { Height = fs + 2f, Stretch = Stretch.Uniform, Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { FontSize = fs, VerticalAlignment = VerticalAlignment.Center, Foreground = MB(MenuText) };
            var arrow = new TextBlock { Text = "▾", FontSize = fs, Margin = new Thickness(8f, 0f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center, Foreground = MB(MenuDim) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(flag); head.Children.Add(name); head.Children.Add(arrow);
            var btn = new Button
            {
                Content = head, Padding = new Thickness(8f, 0f, 8f, 0f), MinWidth = Math.Max(combo.ActualWidth, 120f),
                Height = combo.ActualHeight > 4f ? combo.ActualHeight : float.NaN, HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Margin = combo.Margin
            };
            Popup popup = null;

            Func<object, string> label = n =>
            {
                string t = NationPlainName(n);
                int c = CountFor(ni, n);
                return c >= 0 ? t + " (" + c + ")" : t;
            };
            Action<Image, object> setFlag = (img, n) =>
            {
                try { var f = Get(n, "Flag") as ImageSource; img.Source = f; img.Visibility = f == null ? Visibility.Collapsed : Visibility.Visible; }
                catch { img.Visibility = Visibility.Collapsed; }
            };
            Action header = () =>
            {
                var b = bref.Target; if (b == null) return;
                var sel = Get(b, "SelectedNation");
                name.Text = sel == null ? "" : label(sel);
                setFlag(flag, sel);
            };

            // the menu
            var rows = new StackPanel { Orientation = Orientation.Vertical };
            var segA = new Border(); var segB = new Border();
            var ta = new TextBlock { Text = Loc.T("ui.natAlpha"), FontSize = fs, HorizontalAlignment = HorizontalAlignment.Center };
            var tb = new TextBlock { Text = "9-1", FontSize = fs, HorizontalAlignment = HorizontalAlignment.Center };
            Action paintSwitch = () =>
            {
                bool byCount = _natCount.Value;
                segA.Background = MB(byCount ? Color.FromArgb(0, 0, 0, 0) : MenuOn); segB.Background = MB(byCount ? MenuOn : Color.FromArgb(0, 0, 0, 0));
                ta.Foreground = MB(byCount ? MenuDim : MenuText); tb.Foreground = MB(byCount ? MenuText : MenuDim);
            };
            Action fill = null;
            fill = () =>
            {
                var b = bref.Target; if (b == null) return;
                paintSwitch();
                rows.Children.Clear();
                var list = Get(b, "Nations") as IList; var sel = Get(b, "SelectedNation");
                if (list == null) return;
                var items = new List<object>(); foreach (var it in list) items.Add(it);
                foreach (var n in items)
                {
                    var nation = n;
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var fi = new Image { Height = fs, Stretch = Stretch.Uniform, Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center };
                    setFlag(fi, nation);
                    var nm = new TextBlock { Text = NationPlainName(nation), FontSize = fs, Foreground = MB(MenuText), VerticalAlignment = VerticalAlignment.Center };
                    int c = CountFor(ni, nation);
                    var ct = new TextBlock { Text = c >= 0 ? c.ToString() : "", FontSize = fs, Foreground = MB(MenuDim), Margin = new Thickness(16f, 0f, 0f, 0f), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(fi, 0); Grid.SetColumn(nm, 1); Grid.SetColumn(ct, 2);
                    g.Children.Add(fi); g.Children.Add(nm); g.Children.Add(ct);
                    bool on = sel != null && ReferenceEquals(nation, sel);
                    var row = new Border { Padding = new Thickness(6f, 3f, 6f, 3f), Child = g, Background = MB(on ? MenuOn : Color.FromArgb(0, 0, 0, 0)) };
                    row.MouseEnter += (s, e) => row.Background = MB(on ? MenuOn : MenuHot);
                    row.MouseLeave += (s, e) => row.Background = MB(on ? MenuOn : Color.FromArgb(0, 0, 0, 0));
                    row.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        try { var bb = bref.Target; if (bb != null) SetV(bb, "SelectedNation", nation); } catch (Exception ex) { Trace("country menu: " + ex.Message); }
                        try { if (popup != null) popup.IsOpen = false; } catch { }
                    };
                    rows.Children.Add(row);
                    if (on) { var r = row; try { r.BringIntoView(); } catch { } }
                }
            };
            ni.Refresh = () => { header(); if (popup != null && popup.IsOpen) fill(); };

            Action<Border, bool> wire = (seg, byCountValue) =>
            {
                seg.Padding = new Thickness(0f, 3f, 0f, 3f);
                seg.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    try
                    {
                        var bb = bref.Target; if (bb == null || _natCount.Value == byCountValue) return;
                        SetNatCount(bb, byCountValue);   // the filters run, SyncNations sorts the list and calls Refresh
                        fill();
                    }
                    catch (Exception ex) { Trace("country menu: " + ex.Message); }
                };
                try { ToolTipService.SetToolTip(seg, Loc.T(byCountValue ? "tip.natCount" : "tip.natAlpha")); } catch { }
            };
            segA.Child = ta; segB.Child = tb; wire(segA, false); wire(segB, true);
            var sw = new Grid { Margin = new Thickness(0f, 0f, 0f, 4f) };
            sw.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            sw.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            Grid.SetColumn(segA, 0); Grid.SetColumn(segB, 1); sw.Children.Add(segA); sw.Children.Add(segB);
            var swFrame = new Border { BorderBrush = MB(MenuLine), BorderThickness = new Thickness(1f), Child = sw, Margin = new Thickness(0f, 0f, 0f, 4f) };
            var scroll = new ScrollViewer { MaxHeight = 360f, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows };
            var col = new StackPanel { Orientation = Orientation.Vertical };
            col.Children.Add(swFrame); col.Children.Add(scroll);
            var back = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(6f),
                Background = MB(MenuBack), BorderBrush = MB(MenuLine), BorderThickness = new Thickness(1f), Child = col
            };
            popup = new Popup { PlacementTarget = btn, Placement = PlacementMode.Bottom, StaysOpen = true, Child = back };
            bool entered = false;
            back.MouseEnter += (s, e) => entered = true;
            back.MouseLeave += (s, e) => { if (entered) { try { popup.IsOpen = false; } catch { } } };
            btn.Click += (o, e) =>
            {
                try
                {
                    if (popup.IsOpen) { popup.IsOpen = false; return; }
                    entered = false;
                    back.MinWidth = Math.Max(btn.ActualWidth, 120f);
                    fill();
                    popup.IsOpen = true;
                }
                catch (Exception ex) { Trace("country menu: " + ex.Message); }
            };

            host.Children.Insert(host.Children.IndexOf(combo) + 1, btn);
            host.Children.Add(popup);
            combo.Visibility = Visibility.Collapsed;
            header();
            return true;
        }

        static int CountFor(NatInfo ni, object nation)
        {
            int c;
            if (ni.Count.TryGetValue(nation, out c)) return c;
            return -1;
        }
    }
}
