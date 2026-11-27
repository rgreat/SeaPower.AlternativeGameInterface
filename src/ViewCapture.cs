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
                EventManager.RegisterClassHandler(typeof(Slider), loaded, new RoutedEventHandler(OnSliderLoaded));
                EventManager.RegisterClassHandler(typeof(ComboBox), loaded, new RoutedEventHandler(OnComboLoaded));
                EventManager.RegisterClassHandler(typeof(Button), UIElement.MouseEnterEvent, new MouseEventHandler(OnButtonEnter));
                EventManager.RegisterClassHandler(typeof(TextBlock), loaded, new RoutedEventHandler(OnTextBlockLoaded));
                EventManager.RegisterClassHandler(typeof(TreeViewItem), Control.MouseDoubleClickEvent, new MouseButtonEventHandler(OnTreeItemDoubleClick));
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

        /// <summary>The main menu's scenario list: a double click on a mission does what the Play button does (option General/DoubleClickOpen).</summary>
        static void OnTreeItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var a = Active;
            if ((object)a == null || !a._dblOpen.Value || e.Handled) return;
            try
            {
                var item = sender as TreeViewItem;
                var mission = item == null ? null : item.DataContext;
                if (mission == null || mission.GetType().Name != "Mission") return;
                DependencyObject d = item;
                while (d != null && !(d is TreeView)) d = VisualTreeHelper.GetParent(d);
                var vm = d == null ? null : ((TreeView)d).DataContext;
                if (vm == null || vm.GetType().Name != "ScenarioMenuViewModel") return;
                var enabled = a.Get(mission, "PlayButtonEnabled");
                if (enabled is bool && !(bool)enabled) return;
                var sel = a.Get(vm, "SelectedMission");
                if (!ReferenceEquals(sel, mission)) return;
                var cmd = a.Get(vm, "PlayCommand");
                if (cmd == null) return;
                var exec = cmd.GetType().GetMethod("Execute", new[] { typeof(object) });
                if (exec == null) return;
                e.Handled = true;
                exec.Invoke(cmd, new object[] { null });
            }
            catch (Exception ex) { Trace("DoubleClickPlay: " + (ex.InnerException ?? ex).Message); }
        }

        static readonly ConditionalWeakTable<object, CheckBox> FpsBoxes = new ConditionalWeakTable<object, CheckBox>();   // panel -> our check box

        /// <summary>The video options' Frame Rate Limit slider stops at 120: its top is raised to General/FrameRateOptionMax, so the limit can be set in the game's own option.
        /// The slider has cut a saved higher value to 120 on the way in (two-way binding): the game's value is put back. Under it a check box
        /// "Remove frame rate limit" (General/NoFrameLimit) that switches the slider off.</summary>
        static void OnSliderLoaded(object sender, RoutedEventArgs e)
        {
            var a = Active;
            var sl = sender as Slider;
            if ((object)a != null && sl != null && sl.Name != "sliderTargetFrameRate") { ScaleSliderLoaded(a, sl); return; }
            if ((object)a == null || sl == null || a._fpsMax == null || sl.Name != "sliderTargetFrameRate") return;
            try
            {
                float max = a._fpsMax.Value;
                if (sl.Maximum != max)
                {
                    sl.Maximum = max;
                    var grid = sl.Parent as Panel;
                    if (grid != null && max >= 1000f)
                        foreach (var child in grid.Children)
                        {
                            var num = child as Border;
                            if (num != null && num.Width < 48f) num.Width = 48f;       // the number next to it: four digits
                        }
                    Trace("video options: Frame Rate Limit slider up to " + max);
                }
                int game = a.GameFps();
                if (game > 0 && game <= max && (int)sl.Value != game) sl.Value = game;

                var row = sl.Parent as FrameworkElement;
                var panel = row == null ? null : VisualTreeHelper.GetParent(row) as Panel;
                if (panel != null)
                {
                    CheckBox box;
                    if (!FpsBoxes.TryGetValue(panel, out box))
                    {
                        box = new CheckBox { Content = Loc.T("ui.noFpsLimit") };
                        box.Margin = new Thickness(0);
                        var m = row.Margin; row.Margin = new Thickness(m.Left, m.Top, m.Right, 8f);       // the gap under the slider row moves to the box: no extra gap under it
                        try { ToolTipService.SetToolTip(box, Loc.T("tip.noFpsLimit")); } catch { }
                        box.Click += (o, ev) => { a._fpsNone.Value = box.IsChecked == true; FpsSliderState(sl, a._fpsNone.Value); };
                        panel.Children.Insert(panel.Children.IndexOf(row) + 1, box);
                        // the gap under the box is the game's own: the top margin (33) of the separator line that follows; it is cut, the box has its own place
                        int at = panel.Children.IndexOf(box) + 1;
                        var line = at < panel.Children.Count ? panel.Children[at] as Rectangle : null;
                        if (line != null) { var lm = line.Margin; line.Margin = new Thickness(lm.Left, 8f, lm.Right, lm.Bottom); }
                        FpsBoxes.Add(panel, box);
                        Trace("video options: Remove frame rate limit check box added");
                    }
                    box.IsChecked = a._fpsNone.Value;
                }
                FpsSliderState(sl, a._fpsNone.Value);
            }
            catch (Exception ex) { Trace("capture (frame rate slider): " + ex.Message); }
        }

        /// <summary>The slider and its number are switched off and dimmed (the game's style has no look for a disabled slider).</summary>
        static void FpsSliderState(Slider sl, bool off)
        {
            sl.IsEnabled = !off;
            sl.Opacity = off ? 0.35f : 1f;
            var grid = sl.Parent as Panel;
            if (grid != null) foreach (var child in grid.Children) { var num = child as Border; if (num != null) num.Opacity = off ? 0.35f : 1f; }
        }

        static readonly ConditionalWeakTable<object, Tuple<int, string>> AdaptiveItems = new ConditionalWeakTable<object, Tuple<int, string>>();   // the V-Sync list -> the extra item

        /// <summary>The V-Sync list of the video options (ItemsSource VsyncModes): the extra item "Adaptive V-Sync". Its value is that of "Every" (1): the game saves
        /// ordinary vertical sync, which is what it does without this mod; the mod only remembers (General/AdaptiveVSync) that this item was the chosen one.</summary>
        static void OnComboLoaded(object sender, RoutedEventArgs e)
        {
            var a = Active;
            var cb = sender as ComboBox;
            if ((object)a == null || cb == null || a._vsyncAdaptive == null || !a._vsyncItem.Value) return;
            try
            {
                if (BindingPath(cb, ItemsControl.ItemsSourceProperty) != "VsyncModes") return;
                var vm = cb.DataContext;
                var list = vm == null ? null : a.Get(vm, "VsyncModes") as System.Collections.Generic.List<Tuple<int, string>>;
                if (list == null) return;
                Tuple<int, string> ours;
                if (!AdaptiveItems.TryGetValue(list, out ours))
                {
                    ours = Tuple.Create(1, Loc.T("ui.adaptiveVsync"));
                    list.Add(ours);
                    AdaptiveItems.Add(list, ours);
                    var current = a.Get(vm, "VsyncMode") as Tuple<int, string>;
                    cb.ItemsSource = null;
                    cb.ItemsSource = list;                      // the list is a plain List: the combo box is given it again
                    var pick = current != null && current.Item1 == 1 && a._vsyncAdaptive.Value ? ours : current;
                    if (pick != null) cb.SelectedItem = pick;
                    cb.SelectionChanged += (o, ev) =>
                    {
                        var sel = cb.SelectedItem as Tuple<int, string>;
                        if (sel == null) return;
                        a._vsyncAdaptive.Value = ReferenceEquals(sel, ours);
                    };
                    Trace("video options: Adaptive V-Sync item added to the V-Sync list");
                }
            }
            catch (Exception ex) { Trace("capture (V-Sync list): " + ex.Message); }
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
