using System;
using System.Runtime.CompilerServices;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Elements of the game's views are caught as they appear, not searched for in the visual tree: class handlers of the Loaded event (and MouseEnter
    /// for the link tooltips) hand every loaded CheckBox / Button / TreeView / UserControl to the mod, which keeps the few it needs (found by their
    /// bindings and types): the tree and the filter boxes of a Unit Reference, its Replace button, the Unit Status view, the campaign start screen's
    /// check box, the MainGameView. No loop over the tree anywhere on these paths.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        /// <summary>The parts of one Unit Reference (the window's ObjectInformationViewModel).</summary>
        sealed class VmParts { public WeakReference Date, Name, Replace; }
        static readonly ConditionalWeakTable<object, VmParts> PartsByVm = new ConditionalWeakTable<object, VmParts>();
        static readonly ConditionalWeakTable<object, WeakReference> TreeByBrowser = new ConditionalWeakTable<object, WeakReference>();         // UnitTreeBrowserViewModel -> TreeView
        static readonly ConditionalWeakTable<object, WeakReference> StatusViewByWindow = new ConditionalWeakTable<object, WeakReference>();   // window -> UnitStatusView
        static readonly ConditionalWeakTable<object, WeakReference> InfoViewByWindow = new ConditionalWeakTable<object, WeakReference>();     // window -> ObjectInformationView
        static bool _capHooked;

        void HookCapture()
        {
            if (_capHooked) return;
            _capHooked = true;
            try
            {
                var loaded = FrameworkElement.LoadedEvent;
                EventManager.RegisterClassHandler(typeof(ToggleButton), loaded, new RoutedEventHandler(OnToggleLoaded));
                EventManager.RegisterClassHandler(typeof(Button), loaded, new RoutedEventHandler(OnButtonLoaded));
                EventManager.RegisterClassHandler(typeof(TreeView), loaded, new RoutedEventHandler(OnTreeLoaded));
                EventManager.RegisterClassHandler(typeof(UserControl), loaded, new RoutedEventHandler(OnUserControlLoaded));
                EventManager.RegisterClassHandler(typeof(ItemsControl), loaded, new RoutedEventHandler(OnItemsControlLoaded));
                EventManager.RegisterClassHandler(typeof(Button), UIElement.MouseEnterEvent, new MouseEventHandler(OnButtonEnter));
                Trace("view capture: Loaded / MouseEnter class handlers registered");
                // a view that is up already (the mod was switched on later)
                var asm = GameAssembly();
                var mgv = asm == null ? null : asm.GetType("SeapowerUI.MainGameView");
                var f = mgv == null ? null : mgv.GetField("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                var view = f == null ? null : f.GetValue(null) as FrameworkElement;
                if (view != null) HookMainView(view);
            }
            catch (Exception e) { Logger.LogWarning("View capture not available: " + e.Message); }
        }

        static void OnToggleLoaded(object sender, RoutedEventArgs e)
        {
            var a = Active;
            var cb = sender as ToggleButton;
            if ((object)a == null || cb == null) return;
            try
            {
                var path = BindingPath(cb, ToggleButton.IsCheckedProperty);
                if (path != null && (path.EndsWith("FilterByDate", StringComparison.Ordinal) || path.EndsWith("SearchByNameOnly", StringComparison.Ordinal)))
                {
                    var dc = cb.DataContext;
                    if (dc != null && TypeName(dc) == "ObjectInformationViewModel")
                    {
                        var parts = PartsByVm.GetOrCreateValue(dc);
                        if (path.EndsWith("FilterByDate", StringComparison.Ordinal)) parts.Date = new WeakReference(cb); else parts.Name = new WeakReference(cb);
                    }
                }
                // the campaign start screen: "Limit unit dates" - Strict loadouts goes under it
                if (path == "TaskForceLimitUnitDates" || BindingPath(cb, ContentControl.ContentProperty) == "TaskForceLimitUnitDatesLabel") a.CampaignBoxFor(cb);
            }
            catch (Exception ex) { Trace("capture (check box): " + ex.Message); }
        }

        static void OnButtonLoaded(object sender, RoutedEventArgs e)
        {
            var a = Active;
            var b = sender as Button;
            if ((object)a == null || b == null) return;
            try
            {
                var path = BindingPath(b, ButtonBase.CommandProperty);
                if (path != null && path.EndsWith("TestCommand", StringComparison.Ordinal))
                {
                    var dc = b.DataContext;
                    if (dc != null && TypeName(dc) == "ObjectInformationViewModel") PartsByVm.GetOrCreateValue(dc).Replace = new WeakReference(b);
                }
                if (a._linkHelp.Value) a.TipButton(b);   // a link of a Unit Reference page
            }
            catch (Exception ex) { Trace("capture (button): " + ex.Message); }
        }

        static void OnButtonEnter(object sender, MouseEventArgs e)
        {
            var a = Active;
            var b = sender as Button;
            if ((object)a == null || b == null || !a._linkHelp.Value) return;
            try { a.TipButton(b); } catch (Exception ex) { Trace("link help: " + ex.Message); }
        }

        static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
        {
            var a = Active;
            var ic = sender as ItemsControl;
            if ((object)a == null || ic == null) return;
            try { a.CaptureAlerts(ic); } catch (Exception ex) { Trace("capture (items): " + ex.Message); }
        }

        static void OnTreeLoaded(object sender, RoutedEventArgs e)
        {
            var tv = sender as TreeView;
            if ((object)Active == null || tv == null) return;
            var dc = tv.DataContext;
            if (dc == null || TypeName(dc) != "UnitTreeBrowserViewModel") return;
            TreeByBrowser.Remove(dc);
            TreeByBrowser.Add(dc, new WeakReference(tv));
        }

        static void OnUserControlLoaded(object sender, RoutedEventArgs e)
        {
            var a = Active;
            var uc = sender as UserControl;
            if ((object)a == null || uc == null) return;
            try
            {
                var tn = uc.GetType().Name;
                if (tn == "MainGameView") a.HookMainView(uc);
                else if (tn == "UnitStatusView" || tn == "ObjectInformationView")
                {
                    // the window that holds it: up the parents (a few steps)
                    object w = a.WindowAbove(uc);
                    var table = tn == "UnitStatusView" ? StatusViewByWindow : InfoViewByWindow;
                    if (w != null) { table.Remove(w); table.Add(w, new WeakReference(uc)); }
                }
            }
            catch (Exception ex) { Trace("capture (view): " + ex.Message); }
        }

        /// <summary>The SeapowerUI.Window this element lies in (up the visual parents), or null.</summary>
        object WindowAbove(object e)
        {
            if (_winType == null) return null;
            for (int up = 0; e != null && up < 80; up++, e = VtParent(e))
                if (_winType.IsInstanceOfType(e)) return e;
            return null;
        }

        /// <summary>The TreeView of this Unit Reference browser, or null.</summary>
        static TreeView TreeViewOf(object browser)
        {
            WeakReference r;
            var tv = browser != null && TreeByBrowser.TryGetValue(browser, out r) ? r.Target as TreeView : null;
            return tv != null && VisualTreeHelper.GetParent(tv) != null ? tv : null;
        }

        static UIElement PartOf(WeakReference r) { var e = r == null ? null : r.Target as UIElement; return e != null && VisualTreeHelper.GetParent(e) != null ? e : null; }
    }
}
