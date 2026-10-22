using System.Runtime.CompilerServices;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// Messages in the bottom right corner no longer block clicks (1.6.0, always on): the game's messages (MainGameView: an ItemsControl over
    /// MainGameViewModel.TopAlerts) stay hit-testable after they fade (opacity 0 does not stop hit-testing), so they took the clicks of whatever lies
    /// under them. Fix, once when the list loads (ViewCapture): the list is made not hit-testable; messages are display-only. No timers.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        static readonly ConditionalWeakTable<object, object> AlertListsHooked = new ConditionalWeakTable<object, object>();

        /// <summary>An ItemsControl has loaded: if it is the messages list, it takes no clicks any more.</summary>
        void CaptureAlerts(ItemsControl ic)
        {
            if (BindingPath(ic, ItemsControl.ItemsSourceProperty) != "TopAlerts") return;
            object x;
            if (AlertListsHooked.TryGetValue(ic, out x)) return;
            AlertListsHooked.Add(ic, new object());
            ic.IsHitTestVisible = false;
            Trace("alerts: the messages list caught; hit-testing off");
        }
    }
}
