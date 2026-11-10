using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Unit Status, Damage Control tab: the damage lists of the five sections (Aft ... Forward: flooding, fires, damaged systems) scroll with the mouse wheel.
    /// In the game's XAML each list is a ListBox that has no background (the empty parts of a row are not hit by the pointer, so the wheel
    /// reached nothing there).
    /// Done once per tab, when it is shown: the lists get a transparent background, and a MouseWheel handler (bubbling, after the list's own
    /// ScrollViewer) scrolls it by lines when the ScrollViewer did not and there is something to scroll. The lists' scrolling mode is left as
    /// the game has it: CanContentScroll = false on their VirtualizingStackPanel stopped new items (newly damaged systems) from showing.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _dcWheel;
        static readonly ConditionalWeakTable<object, object> DcWheelDone = new ConditionalWeakTable<object, object>();

        void BindDamageScrollConfig()
        {
            _dcWheel = Config.Bind("UnitStatus", "DamageListsWheel", true,
                "Unit Status window, Damage Control tab: the damage lists of the sections (Aft ... Forward) scroll with the mouse wheel. Applies to the next tab shown.");
        }

        /// <summary>Once per tab: the lists found in its visual tree are set up. A tab built without its lists yet is tried again when shown next.</summary>
        void SetupDamageWheel(object view)
        {
            var root = view as FrameworkElement;
            if (root == null || _dcWheel == null || !_dcWheel.Value || DcWheelDone.TryGetValue(view, out _)) return;
            var lists = new List<ListBox>();
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            int budget = 5000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                var lb = e as ListBox;
                if (lb != null) { lists.Add(lb); continue; }   // the lists' own content is not searched
                for (int k = 0, n = VisualTreeHelper.GetChildrenCount(e); k < n; k++) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            if (lists.Count == 0) { Trace("damage control wheel: no lists in the tab yet"); return; }
            DcWheelDone.Add(view, new object());
            foreach (var lb in lists)
            {
                lb.Background = Brushes.Transparent;   // hit-testable everywhere: the wheel reaches the list over an empty part of a row too
                lb.MouseWheel += (sender, e) =>   // bubbling: after the list's own ScrollViewer, which marks the event handled when it scrolled
                {
                    try { if (Enabled && _dcWheel.Value) DcWheel(sender as ListBox, e); }
                    catch (Exception ex) { Trace("damage control wheel: " + ex.Message); }
                };
            }
            Trace("damage control wheel: " + lists.Count + " lists set up");
        }

        static void DcWheel(ListBox lb, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            var sv = lb == null ? null : FindDescendant<ScrollViewer>(lb);
            if (sv == null || sv.ScrollableHeight <= 0f) return;
            // by lines: the same step as the ScrollViewer's own, whatever its unit (items or pixels)
            int n = Math.Max(1, Math.Abs(e.Delta) / 40);
            for (int i = 0; i < n; i++) { if (e.Delta > 0) sv.LineUp(); else sv.LineDown(); }
            e.Handled = true;
        }
    }
}
