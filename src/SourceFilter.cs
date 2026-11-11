using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Source filter (with ImprovedUnitReferenceUI): a "Source" drop-down on the search panel of the Unit Reference tree, a list of the
    /// sources the units come from - the base game and every mod that brings units (MuseumEntry.ModSource, set by the game from the folder
    /// the unit's ini lies in) - with a tick for each. Any number can be ticked, at least one stays. Units of the other sources are left out
    /// like the "Armed only" filter does (see ArmedFilter.cs); the nations without a unit left leave the nation list. Kept in
    /// [UnitReference] SourcesOff (the sources switched off, so a new mod shows up ticked).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<string> _srcOff;
        const string BaseToken = "<base>";

        sealed class SrcState { public bool Done; public int Tries; public Button Button; public string Label; public WeakReference Menu; }
        static readonly ConditionalWeakTable<object, SrcState> SrcStates = new ConditionalWeakTable<object, SrcState>();
        static readonly ConditionalWeakTable<object, List<string>> SrcLists = new ConditionalWeakTable<object, List<string>>();

        string SourceOf(object e) { var s = Get(e, "ModSource") as string; return string.IsNullOrEmpty(s) ? "" : s; }
        static string TokenOf(string source) { return source.Length == 0 ? BaseToken : source; }

        HashSet<string> SourcesOff()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in (_srcOff.Value ?? "").Split('|')) { var t = p.Trim(); if (t.Length > 0) set.Add(t); }
            return set;
        }

        /// <summary>The sources of the units in the browser's tree: the base game ("" first), then the mods by name. Null while the tree is not built.</summary>
        List<string> SourcesOf(object browser)
        {
            List<string> list;
            if (SrcLists.TryGetValue(browser, out list)) return list;
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            bool baseSeen = false;
            CollectSources(Get(browser, "MuseumEntries") as IList, names, ref baseSeen, 0);
            if (!baseSeen && names.Count == 0) return null;
            list = new List<string>();
            if (baseSeen) list.Add("");
            list.AddRange(names);
            SrcLists.Add(browser, list);
            Trace("sources of the Unit Reference tree: " + string.Join(", ", list.Select(TokenOf).ToArray()));
            return list;
        }

        void CollectSources(IList level, SortedSet<string> names, ref bool baseSeen, int depth)
        {
            if (level == null || depth > 12) return;
            foreach (var e in level)
            {
                if (e == null) continue;
                if (e.GetType().Name == "MuseumEntry")
                {
                    CollectSources(Get(e, "MuseumEntries") as IList, names, ref baseSeen, depth + 1);
                    continue;
                }
                if (!IsUnitEntry(e)) continue;
                var s = SourceOf(e);
                if (s.Length == 0) baseSeen = true; else names.Add(s);
                var kids = Get(e, "MuseumEntries") as IList;
                if (kids != null && kids.Count > 0) CollectSources(kids, names, ref baseSeen, depth + 1);
            }
        }

        /// <summary>The unit test of the filter, or null when nothing is left out (all ticked, not the encyclopedia, tree not built).</summary>
        Func<object, bool> SourcePredicate(object browser)
        {
            if (!IsEncyclopediaBrowser(browser)) return null;
            var off = SourcesOff();
            if (off.Count == 0) return null;
            var all = SourcesOf(browser);
            if (all == null || all.Count < 2) return null;
            int shown = all.Count(s => !off.Contains(TokenOf(s)));
            if (shown == 0 || shown == all.Count) return null;   // a source list that no longer fits what is installed: no filter
            return e => !IsUnitEntry(e) || !off.Contains(TokenOf(SourceOf(e)));
        }

        // ---------------------------------------------------------------- the drop-down

        /// <summary>Each round for a Unit Reference window on screen: puts the drop-down after the search box once the panel is there; keeps its text up to date.</summary>
        void SourceBoxStep(object w, object vm)
        {
            var st = SrcStates.GetOrCreateValue(w);
            var browser = vm == null ? null : Get(vm, "Browser");
            if (browser == null || !IsEncyclopediaBrowser(browser)) return;
            if (!st.Done)
            {
                VmParts parts;
                if (!PartsByVm.TryGetValue(vm, out parts)) return;
                var date = PartOf(parts.Date) as ToggleButton;
                if (date == null) return;
                var panel = VisualTreeHelper.GetParent(date) as Panel;
                if (panel == null) { st.Done = true; return; }
                int at = -1;
                for (int i = 0; i < panel.Children.Count; i++)   // the search box: the grid holding the TextBox
                {
                    var g = panel.Children[i] as Grid;
                    if (g == null) continue;
                    foreach (var c in g.Children) if (c is TextBox) { at = i; break; }
                    if (at >= 0) break;
                }
                if (at < 0) { if (++st.Tries < 10) return; st.Done = true; Trace("sources: no search box found in " + Describe(panel)); return; }
                st.Done = true;
                var btn = new Button
                {
                    Margin = new Thickness(0f, 0f, 10f, 0f), Padding = new Thickness(6f, 0f, 6f, 0f),
                    VerticalAlignment = VerticalAlignment.Center, FontSize = date.FontSize, Visibility = Visibility.Collapsed
                };
                try { ToolTipService.SetToolTip(btn, Loc.T("tip.sources")); } catch { }
                var owner = new WeakReference(w);
                var bref = new WeakReference(browser);
                btn.Click += (o, e) =>
                {
                    try
                    {
                        var s = SrcStates.GetOrCreateValue(owner.Target ?? w);
                        if (s.Menu != null && s.Menu.Target != null) { CloseSourceMenu(s.Menu.Target); s.Menu = null; return; }
                        var b = bref.Target; if (b == null) return;
                        s.Menu = new WeakReference(BuildSourceMenu(b, panel, btn, s));
                    }
                    catch (Exception ex) { Trace("sources menu: " + ex.Message); }
                };
                panel.Children.Insert(at + 1, btn);
                st.Button = btn;
                Trace("sources: drop-down added to " + Describe(panel) + " after the search box");
            }
            if (st.Button == null) return;
            var list = SourcesOf(browser);
            string label = null;
            if (list != null && list.Count >= 2)
            {
                var off = SourcesOff();
                int shown = list.Count(s => !off.Contains(TokenOf(s)));
                if (shown == 0 || shown == list.Count) label = Loc.T("ui.sources") + ": " + Loc.T("ui.sourcesAll") + " ▾";
                else label = Loc.T("ui.sources") + ": " + shown + "/" + list.Count + " ▾";
            }
            if (label == st.Label) return;
            st.Label = label;
            if (label == null) st.Button.Visibility = Visibility.Collapsed;
            else { st.Button.Content = label; st.Button.Visibility = Visibility.Visible; }
        }

        UIElement BuildSourceMenu(object browser, Panel host, UIElement target, SrcState st)
        {
            var list = SourcesOf(browser) ?? new List<string>();
            var back = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(6f),
                Background = new SolidColorBrush(Color.FromRgb(30, 33, 38)), BorderBrush = new SolidColorBrush(Color.FromRgb(70, 76, 86)),
                BorderThickness = new Thickness(1f)
            };
            var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = true, Child = back };
            var rows = new StackPanel { Orientation = Orientation.Vertical };
            var scroll = new ScrollViewer { MaxHeight = 360f, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows };
            back.Child = scroll;
            var texts = new List<TextBlock>();
            Action refresh = null;
            refresh = () =>
            {
                var off = SourcesOff();
                for (int i = 0; i < list.Count; i++)
                    texts[i].Text = (off.Contains(TokenOf(list[i])) ? "☐ " : "☑ ") + (list[i].Length == 0 ? Loc.T("ui.baseGame") : list[i]);
            };
            Func<UIElement, Action, UIElement> hot = (row, click) =>
            {
                var b = new Border { Padding = new Thickness(4f, 2f, 4f, 2f), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Child = row };
                b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(Color.FromRgb(62, 69, 79));
                b.MouseLeave += (s, e) => b.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                b.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; click(); };
                return b;
            };
            Func<string, TextBlock> tb = t => new TextBlock { Text = t, FontSize = 12f, Foreground = new SolidColorBrush(Color.FromRgb(230, 232, 235)) };
            var bref = new WeakReference(browser);
            // all sources
            rows.Children.Add(hot(tb("✓ " + Loc.T("ui.sourcesAll")), () =>
            {
                if (_srcOff.Value.Length == 0) return;
                _srcOff.Value = ""; Config.Save(); refresh(); Refilter(bref.Target);
            }));
            foreach (var src in list)
            {
                var t = tb(""); texts.Add(t);
                string token = TokenOf(src);
                rows.Children.Add(hot(t, () =>
                {
                    var off = SourcesOff();
                    if (off.Contains(token)) off.Remove(token);
                    else
                    {
                        if (list.Count(s => !off.Contains(TokenOf(s))) <= 1) return;   // at least one source stays ticked
                        off.Add(token);
                    }
                    _srcOff.Value = string.Join("|", off.ToArray()); Config.Save();
                    refresh(); Refilter(bref.Target);
                }));
            }
            refresh();
            try { popup.IsOpen = true; } catch { }
            bool entered = false;
            back.MouseEnter += (s, e) => entered = true;
            back.MouseLeave += (s, e) => { if (entered) { CloseSourceMenu(popup); st.Menu = null; } };
            host.Children.Add(popup);
            return popup;
        }

        static void CloseSourceMenu(object m)
        {
            var p = m as Popup;
            try { if (p != null) p.IsOpen = false; } catch { }
            RemoveFromParent(m);
        }

        /// <summary>As the game's own filter switches do: keep the tree's expansion, then filter again.</summary>
        void Refilter(object browser)
        {
            if (browser == null) return;
            try
            {
                if (_saveExpansion != null && !GetB(browser, "IsSearching")) Invoke(_saveExpansion, browser, Get(browser, "FilteredMuseumEntries"));
                if (_updateFiltered != null) Invoke(_updateFiltered, browser);
            }
            catch (Exception e) { Trace("sources refilter: " + e.Message); }
        }
    }
}
