using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// ScrollToSelected: keeps the selected entry of a Unit Reference window's unit tree in view. The game selects the entry
    /// (and expands its parents) but does not scroll the tree to it. Checked every frame for each Unit Reference window on
    /// screen with the tree shown; the scroll happens when the selected entry changes (opening on a unit, type-ahead, a click),
    /// when the tree is shown again, or when the tree's items change (category, filters). On opening / showing / new items the
    /// entry is centred; otherwise the tree scrolls just enough to show it whole. An entry inside a collapsed node: that node.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _treeScroll;

        sealed class ScrollState
        {
            public WeakReference Tree;     // Noesis.TreeView of the window
            public object Entry;           // the selected entry last brought into view (or given up on)
            public object Items;           // the browser's TreeItems collection last seen
            public bool Shown;             // the tree was shown last frame
            public bool Center;            // the next scroll centres the entry
            public int Tries;
        }

        static readonly ConditionalWeakTable<object, ScrollState> ScrollStates = new ConditionalWeakTable<object, ScrollState>();

        void TreeScrollStep(object w)
        {
            var vm = InfoViewModel(w);
            if (vm == null) return;
            var st = ScrollStates.GetOrCreateValue(w);
            bool shown = GetB(vm, "ToggleTreeCommandVisible") && GetB(vm, "MuseumEntriesVisible");
            if (!shown) { st.Shown = false; return; }
            var browser = Get(vm, "Browser");
            var entry = browser == null ? null : Get(browser, "SelectedEntry");
            var items = browser == null ? null : Get(browser, "TreeItems");
            if (!st.Shown || !ReferenceEquals(items, st.Items)) { st.Center = true; st.Entry = null; st.Tries = 0; }
            st.Shown = true;
            st.Items = items;
            if (entry == null || ReferenceEquals(entry, st.Entry)) return;
            if (st.Tries == 0 && st.Entry != null) st.Center = false;   // the selection changed: a click, type-ahead
            if (++st.Tries > 60) { Trace("tree scroll: gave up on " + TextOf(Get(entry, "TreeLabel"))); Done(st, entry); return; }

            var tv = st.Tree == null ? null : st.Tree.Target as TreeView;
            if (tv == null || VisualTreeHelper.GetParent(tv) == null)
            {
                var view = FindTreeView(w, vm, browser) as DependencyObject;
                tv = view == null ? null : FindDescendant<TreeView>(view);
                st.Tree = tv == null ? null : new WeakReference(tv);
                if (tv == null) return;
            }
            if (tv.ActualHeight <= 0f) return;   // not laid out yet

            var path = new List<object>();
            if (!PathTo(items as IEnumerable, entry, path)) { Done(st, entry); return; }   // not in the tree (filtered out)

            // the containers down the path; stop at a collapsed node (its children have no containers)
            ItemsControl parent = tv;
            TreeViewItem target = null;
            foreach (var item in path)
            {
                var c = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (c == null) break;
                target = c;
                if (!ReferenceEquals(item, entry) && !c.IsExpanded) break;
                parent = c;
            }
            if (target == null) { tv.UpdateLayout(); return; }   // containers not made yet: next frame
            if (target.ActualHeight <= 0f) { tv.UpdateLayout(); return; }

            var sv = FindAncestor<ScrollViewer>(target, tv) ?? FindDescendant<ScrollViewer>(tv);
            if (sv == null || sv.ViewportHeight <= 0f) return;
            if (sv.CanContentScroll) target.BringIntoView();   // logical (item) scrolling: let the panel do it
            else
            {
                var header = target.Template == null ? null : target.Template.FindName("PART_Header", target) as FrameworkElement;
                FrameworkElement row = header != null && header.ActualHeight > 0f ? (FrameworkElement)header : target;
                float y = row.TranslatePoint(new Point(0f, 0f), sv).Y;
                float h = Math.Min(row.ActualHeight, sv.ViewportHeight);
                float view = sv.ViewportHeight, off = sv.VerticalOffset, to = off;
                if (st.Center) { if (y < 0f || y + h > view) to = off + y - (view - h) / 2f; }
                else if (y < 0f) to = off + y;
                else if (y + h > view) to = off + y + h - view;
                to = Math.Max(0f, Math.Min(to, sv.ScrollableHeight));
                if (Math.Abs(to - off) > 0.5f) sv.ScrollToVerticalOffset(to);
            }
            Verbose("tree scroll: " + TextOf(Get(entry, "TreeLabel")) + (ReferenceEquals(target.DataContext, entry) ? "" : " (via its collapsed node)") + (st.Center ? ", centred" : ""));
            Done(st, entry);
        }

        static void Done(ScrollState st, object entry) { st.Entry = entry; st.Tries = 0; st.Center = false; }

        /// <summary>The path of tree items (top level first) to the entry, through the filtered children the tree shows.</summary>
        bool PathTo(IEnumerable items, object entry, List<object> path)
        {
            if (items == null) return false;
            foreach (var it in items)
            {
                if (it == null) continue;
                path.Add(it);
                if (ReferenceEquals(it, entry) || PathTo(Get(it, "FilteredMuseumEntries") as IEnumerable, entry, path)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        static T FindDescendant<T>(DependencyObject root) where T : class
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            int budget = 20000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                if (!ReferenceEquals(e, root) && e is T) return e as T;
                int n = VisualTreeHelper.GetChildrenCount(e);
                for (int k = n - 1; k >= 0; k--) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            return null;
        }

        static T FindAncestor<T>(DependencyObject e, DependencyObject stop) where T : class
        {
            for (var p = VisualTreeHelper.GetParent(e); p != null && !ReferenceEquals(p, stop); p = VisualTreeHelper.GetParent(p))
                if (p is T) return p as T;
            return null;
        }
    }
}
