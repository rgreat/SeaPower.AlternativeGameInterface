using System;
using System.Collections.Generic;
using Noesis;
using Rgreat.Kit;

namespace AlternativeGameInterface
{
    public partial class AlternativeGameInterfacePlugin
    {
        /// <summary>
        /// Pages inside a tab of the settings window (second row of buttons under the tab strip, as in the other mods): a long tab is split by its groups.
        /// <see cref="PageGroups"/> says which page every group belongs to; the order of the pages is the order of the first appearance there.
        /// Tabs without an entry have no pages. The page chosen is kept per tab while the window is open; the search ignores the pages (it filters the whole tab).
        /// </summary>
        static partial class SettingsWindow
        {
            // tab -> (group -> page). The groups of one page stay together (the order of the fields in Define is the order of the rows).
            static readonly Dictionary<string, string[][]> PageGroups = new Dictionary<string, string[][]>
            {
                { "Windows", new[] {
                    new[] { "General", "Performance" },
                    new[] { "Moving and snapping", "Moving, sizing and snapping" },
                    new[] { "Order and look", "Order and title bars", "Text by background", "Frame around draggable windows" } } },
                { "Game Menus", new[] {
                    new[] { "Menus", "Main menu", "Scenarios", "Mods", "Mission editor" },
                    new[] { "Save and load", "Save and load" },
                    new[] { "Backgrounds", "Backgrounds" } } },
            };

            static readonly Dictionary<string, int> _pageSel = new Dictionary<string, int>();
            static readonly List<KeyValuePair<string, Border>> _pageButtons = new List<KeyValuePair<string, Border>>();

            static bool HasPages(string tab) { return PageGroups.ContainsKey(tab); }

            static string[] PageNames(string tab)
            {
                var r = new List<string>();
                foreach (var p in PageGroups[tab]) r.Add(p[0]);
                return r.ToArray();
            }

            /// <summary>The page of a field (null: the tab has no pages).</summary>
            static string PageOf(Field f)
            {
                string[][] pages;
                if (!PageGroups.TryGetValue(f.Tab, out pages)) return null;
                foreach (var p in pages) for (int i = 1; i < p.Length; i++) if (p[i] == f.Group) return p[0];
                return pages[0][0];
            }

            static int PageIndex(string tab) { int i; _pageSel.TryGetValue(tab, out i); string[] n = PageNames(tab); return Math.Max(0, Math.Min(n.Length - 1, i)); }

            static bool PageChanged(string tab, string page)
            {
                foreach (var f in Fields) if (f.Tab == tab && PageOf(f) == page && Changed(f)) return true;
                return false;
            }

            /// <summary>The buttons of the pages above the rows of the tab; returns the page shown.</summary>
            static string PageStrip(string tab)
            {
                string[] names = PageNames(tab);
                int sel = PageIndex(tab);
                _pageButtons.Clear();
                var strip = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 8f, 12f, 2f) };
                for (int i = 0; i < names.Length; i++)
                {
                    int idx = i; string page = names[i];
                    var b = _f.PageButton(Loc.Or("pg." + page, page), i == sel, PageChanged(tab, page), () => { _pageSel[tab] = idx; ShowTab(_tab); });
                    _pageButtons.Add(new KeyValuePair<string, Border>(page, b));
                    strip.Children.Add(b);
                }
                _body.Children.Add(strip);
                return names[sel];
            }

            /// <summary>The dots of the pages follow the edits (called with the tab dots).</summary>
            static void UpdatePageDots()
            {
                if (_tab < 0 || _tab >= Tabs.Length || !HasPages(Tabs[_tab])) return;
                string tab = Tabs[_tab];
                foreach (var kv in _pageButtons)
                {
                    var row = kv.Value.Child as StackPanel;
                    if (row == null) continue;
                    bool has = row.Children.Count > 1 && row.Children[0] is Ellipse;
                    bool want = PageChanged(tab, kv.Key);
                    if (want && !has) row.Children.Insert(0, WinFrame.Dot(5f));
                    else if (!want && has) row.Children.RemoveAt(0);
                }
            }
        }
    }
}
