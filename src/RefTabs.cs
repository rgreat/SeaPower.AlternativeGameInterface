using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Unit Reference tabs ([UnitReference] ReferenceTabs): a strip of tabs, one per unit, above the unit page (the right pane of the window).
    /// A double click on a unit in the tree adds a tab for it and shows it; a unit already in the strip is not added twice (the tab is just shown).
    /// A Unit Reference that the game would open as a new window for a unit (the unit's menu, the links of a page, the campaign) is added to the
    /// strip of the window that is open already instead. With no such window the new one opens as always, its unit is the first tab.
    /// A tab shows its unit in the same window (the tree follows); the cross on a tab closes it.
    /// The tab is the museum entry only (the unit page of the window, as a click in the tree shows it), no second view model, no second page.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _refTabs;
        const float RefTabsH = 28f;
        static object _openCmd;                       // WindowHostCommands.OpenWindowCommand
        static WeakReference _refHostWin;             // the Unit Reference window that takes the tabs
        static readonly ConditionalWeakTable<object, object> HideTreeVms = new ConditionalWeakTable<object, object>();   // new windows of a Shift double click: the tree is hidden when they open
        static bool _refBypass;                       // the mod itself opens a window: RefOpenPrefix leaves it alone
        static readonly ConditionalWeakTable<object, object> ForUnitVms = new ConditionalWeakTable<object, object>();   // view models made for one unit

        sealed class RefTabState
        {
            public readonly List<object> Entries = new List<object>();
            public WeakReference Win, ViewRef;
            public Border Strip; public StackPanel Panel; public ScrollViewer Scroll, Content; public Thickness ContentMargin;
            public readonly List<Border> Heads = new List<Border>(); public readonly List<TextBlock> Names = new List<TextBlock>();
            public readonly Dictionary<object, bool> Multi = new Dictionary<object, bool>();   // the unit page has a loadout drop-down (seen while the unit was shown): the tab omits the variant name
            public bool Hidden;     // the strip was closed down to one tab: hidden until another unit is added
            public bool Shown, Built; public int Version, Drawn = -1, Tries; public object Current;
        }
        static readonly ConditionalWeakTable<object, RefTabState> RefTabsByVm = new ConditionalWeakTable<object, RefTabState>();

        sealed class RefPred
        {
            readonly object _e; readonly AlternativeGameInterfacePlugin _a;
            public RefPred(AlternativeGameInterfacePlugin a, object e) { _a = a; _e = e; }
            public bool Is(object x) { return _a.SamePage(x, _e); }
        }

        void BindRefTabsConfig()
        {
            _refTabs = Config.Bind("UnitReference", "ReferenceTabs", true,
                "Unit Reference: a strip of tabs above the unit page. A double click on a unit in the tree adds a tab for it (the same unit is not added twice); a Unit Reference the game would open as a new window for a unit is added as a tab of the open window instead.");
        }

        void HookRefTabs(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var cmds = asm.GetType("SeapowerUI.WindowHostCommands");
            var p = cmds == null ? null : cmds.GetProperty("OpenWindowCommand", all);
            _openCmd = p == null ? null : p.GetValue(null, null);
            var crc = asm.GetType("SeapowerUI.CustomRoutedCommand");
            MethodInfo exec = null;
            if (crc != null) foreach (var m in crc.GetMethods(all)) if (m.Name == "Execute" && m.GetParameters().Length == 2) exec = m;
            if (exec != null && _openCmd != null) { TryPatch(harmony, exec, "RefOpenPrefix", null, null); Trace("patched CustomRoutedCommand.Execute (ReferenceTabs: a unit's Unit Reference becomes a tab)"); }
            else Trace("CustomRoutedCommand.Execute / OpenWindowCommand not found: new Unit Reference windows are not turned into tabs");

            var tv = asm.GetType("SeapowerUI.UnitTreeBrowserView");
            var down = tv == null ? null : tv.GetMethod("Tree_PreviewMouseLeftButtonDown", all);
            if (down != null) { TryPatch(harmony, down, "RefTreeDownPrefix", null, null); Trace("patched UnitTreeBrowserView.Tree_PreviewMouseLeftButtonDown (ReferenceTabs: double click)"); }
            else Trace("UnitTreeBrowserView.Tree_PreviewMouseLeftButtonDown not found: ReferenceTabs has no double click");

            var oivm = asm.GetType("SeapowerUI.ViewModels.ObjectInformationViewModel");
            if (oivm != null)
                foreach (var c in oivm.GetConstructors(all))
                {
                    var ps = c.GetParameters();
                    if (ps.Length >= 2 && ps[1].Name == "initialIniRef") TryPatch(harmony, c, null, "RefForUnitIniPostfix", null);
                    else if (ps.Length == 2 && ps[1].Name == "museumEntry") TryPatch(harmony, c, null, "RefForUnitPostfix", null);
                }
        }

        static void RefForUnitIniPostfix(object __instance, string initialIniRef) { if (initialIniRef != null) MarkForUnit(__instance); }
        static void RefForUnitPostfix(object __instance) { MarkForUnit(__instance); }
        static void MarkForUnit(object vm) { try { ForUnitVms.Remove(vm); ForUnitVms.Add(vm, vm); } catch { } }

        /// <summary>A page of the Unit Reference: any museum entry that is a subclass of MuseumEntry (units, weapons, launchers, guns, sensors), as the game's tree selects them; not a folder or a class row.</summary>
        static bool IsPageEntry(object e)
        {
            if (e == null || e.GetType().Name == "MuseumEntry") return false;
            for (var b = e.GetType().BaseType; b != null; b = b.BaseType) if (b.Name == "MuseumEntry") return true;
            return false;
        }

        /// <summary>ObjectInformationViewModel or its weapon / sensor variants.</summary>
        static bool IsRefVm(object vm)
        {
            for (var t = vm == null ? null : vm.GetType(); t != null; t = t.BaseType) if (t.Name == "ObjectInformationViewModel") return true;
            return false;
        }

        // ---------------------------------------------------------------- the tab host

        /// <summary>The Unit Reference window (with a tree) that takes new tabs, or null.</summary>
        object RefTabHost(out object vm)
        {
            vm = null;
            var w = _refHostWin == null ? null : _refHostWin.Target;
            if (w == null || !IsOnScreen(w)) return null;
            vm = InfoViewModel(w);
            if (vm == null || !GetB(vm, "ToggleTreeCommandVisible")) { vm = null; return null; }
            return w;
        }

        bool SamePage(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            if (a.GetType() != b.GetType()) return false;
            var ia = TextOf(Get(a, "IniRef")); var ib = TextOf(Get(b, "IniRef"));
            if (ia == null || ia != ib) return false;
            return Equals(Get(a, "VariantIndex"), Get(b, "VariantIndex"));
        }

        int TabIndex(RefTabState st, object entry)
        {
            for (int i = 0; i < st.Entries.Count; i++) if (SamePage(st.Entries[i], entry)) return i;
            return -1;
        }

        /// <summary>Adds the unit to the strip (once) and shows it.</summary>
        void AddRefTab(object w, object vm, object entry)
        {
            var st = RefTabsByVm.GetOrCreateValue(vm);
            int at = TabIndex(st, entry);
            if (at < 0) { st.Entries.Add(entry); st.Hidden = false; st.Version++; Trace("reference tab added: " + TextOf(Get(entry, "DisplayName"))); }
            st.Win = new WeakReference(w);
            _refHostWin = new WeakReference(w);
            if (!ReferenceEquals(Get(vm, "SelectedMuseumEntry"), entry)) ShowRefEntry(vm, entry);
        }

        /// <summary>The window shows this unit: the tree follows (RevealEntry + SelectedEntry as the game's own opening for a unit), then the page.</summary>
        void ShowRefEntry(object vm, object entry)
        {
            var browser = Get(vm, "Browser");
            try
            {
                var rev = browser == null ? null : browser.GetType().GetMethod("RevealEntry");
                if (rev != null)
                {
                    var d = Delegate.CreateDelegate(rev.GetParameters()[0].ParameterType, new RefPred(this, entry), typeof(RefPred).GetMethod("Is"));
                    var found = rev.Invoke(browser, new object[] { d });
                    if (found != null) SetV(browser, "SelectedEntry", found);
                }
            }
            catch (Exception e) { Trace("reference tab: the tree did not follow: " + (e.InnerException ?? e).Message); }
            try { if (!SamePage(Get(vm, "SelectedMuseumEntry"), entry)) SetV(vm, "SelectedMuseumEntry", entry); }
            catch (Exception e) { Trace("reference tab: " + (e.InnerException ?? e).Message); }
        }

        // ---------------------------------------------------------------- the game opens a Unit Reference for a unit

        static bool RefOpenPrefix(object __instance, object parameter)
        {
            var a = Active;
            if ((object)a == null || a._refTabs == null || !a._refTabs.Value || parameter == null || !ReferenceEquals(__instance, _openCmd)) return true;
            try
            {
                object dummy;
                if (!IsRefVm(parameter) || !ForUnitVms.TryGetValue(parameter, out dummy)) return true;
                var entry = a.Get(parameter, "SelectedMuseumEntry");
                if (entry == null || !IsPageEntry(entry)) return true;
                if (_refBypass) return true;
                if (ShiftHeld())                                              // Shift: a window of its own, the tree hidden (as the '<' button does)
                {
                    if (a.GetB(parameter, "ToggleTreeCommandVisible")) MarkHideTree(parameter);
                    return true;
                }
                object host;
                var w = a.RefTabHost(out host);
                if (w == null)
                {
                    if (a.GetB(parameter, "ToggleTreeCommandVisible"))        // opens as always; its unit is the first tab
                    {
                        var st0 = RefTabsByVm.GetOrCreateValue(parameter);
                        if (st0.Entries.Count == 0) { st0.Entries.Add(entry); st0.Version++; }
                    }
                    return true;
                }
                a.AddRefTab(w, host, entry);
                var dispose = parameter.GetType().GetMethod("Dispose", Type.EmptyTypes);
                if (dispose != null) try { dispose.Invoke(parameter, null); } catch { }
                return false;
            }
            catch (Exception e) { Trace("ReferenceTabs (open): " + (e.InnerException ?? e).Message); return true; }
        }

        static bool ShiftHeld() { return UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift); }
        static void MarkHideTree(object vm) { try { HideTreeVms.Remove(vm); HideTreeVms.Add(vm, vm); } catch { } }

        /// <summary>Shift + double click: the unit gets a new Unit Reference window (as the game opens one for a unit: the same view models), tree hidden.</summary>
        void OpenRefWindow(object w, object vm, object entry)
        {
            try
            {
                var asm = GameAssembly();
                var mgv = asm == null ? null : asm.GetType("SeapowerUI.MainGameView");
                var f = mgv == null ? null : mgv.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
                var ctx = (f == null ? null : f.GetValue(null)) as UIElement ?? w as UIElement;
                if (ctx == null || _openCmd == null) return;
                object nvm = null;
                bool unit = false;
                for (var t = entry.GetType(); t != null; t = t.BaseType) if (t.Name == "MuseumObjectEntry") unit = true;
                if (unit)
                {
                    nvm = Activator.CreateInstance(vm.GetType(), new object[] { ctx, TextOf(Get(entry, "IniRef")), Convert.ToInt32(Get(entry, "VariantIndex")), true, null });
                    MarkHideTree(nvm);
                }
                else
                {
                    string vmName = "ObjectInformationViewModel";
                    for (var t = entry.GetType(); t != null; t = t.BaseType)
                    {
                        if (t.Name == "MuseumWeaponEntry") { vmName = "ObjectWeaponInformationViewModel"; break; }
                        if (t.Name == "MuseumSensorEntry") { vmName = "ObjectSensorInformationViewModel"; break; }
                    }
                    var vt = asm.GetType("SeapowerUI.ViewModels." + vmName);
                    nvm = Activator.CreateInstance(vt, new object[] { ctx, entry });     // a page without the tree, as the game's links open it
                }
                var exec = _openCmd.GetType().GetMethod("Execute", new[] { typeof(object), typeof(UIElement) });
                _refBypass = true;
                try { exec.Invoke(_openCmd, new object[] { nvm, ctx }); } finally { _refBypass = false; }
                Trace("reference: new window for " + TextOf(Get(entry, "DisplayName")) + " (Shift)");
            }
            catch (Exception e) { _refBypass = false; Trace("ReferenceTabs (Shift window): " + (e.InnerException ?? e).Message); }
        }

        // ---------------------------------------------------------------- double click in the tree

        static bool RefTreeDownPrefix(object sender, MouseButtonEventArgs e)
        {
            var a = Active;
            if ((object)a == null || a._refTabs == null || !a._refTabs.Value || e == null || e.ClickCount < 2) return true;
            try
            {
                var tree = sender as TreeView;
                var browser = tree == null ? null : tree.DataContext;
                var w = tree == null ? null : a.WindowAbove(tree);
                var vm = w == null ? null : a.InfoViewModel(w);
                if (vm == null || !a.GetB(vm, "ToggleTreeCommandVisible") || !ReferenceEquals(a.Get(vm, "Browser"), browser)) return true;
                object item = null;
                for (var d = e.Source as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
                {
                    if (d is ToggleButton) return true;                       // the expander arrow: the game's own
                    var tvi = d as TreeViewItem;
                    if (tvi != null) { item = tvi.DataContext; break; }
                }
                object entry = null;
                if (item != null && IsPageEntry(item)) entry = item;
                else if (item != null && a.GetB(item, "IsGroupNode"))        // a class row: the unit the game picked for it on the first click
                {
                    var cur = a.Get(vm, "SelectedMuseumEntry");
                    if (cur != null && IsPageEntry(cur)) entry = cur;
                }
                if (entry == null) return true;                               // a folder: the game's own
                if (ShiftHeld()) { a.OpenRefWindow(w, vm, entry); e.Handled = true; return false; }
                a.AddRefTab(w, vm, entry);
                e.Handled = true;
                return false;
            }
            catch (Exception ex) { Trace("ReferenceTabs (double click): " + (ex.InnerException ?? ex).Message); return true; }
        }

        // ---------------------------------------------------------------- the strip

        /// <summary>Each round for a Unit Reference window on screen: builds the strip once the view is there, keeps the tabs and the shown one up to date.</summary>
        void RefTabsStep(object w, object vm)
        {
            if (_refTabs == null || !GetB(vm, "ToggleTreeCommandVisible")) return;
            RefTabState st;
            if (!RefTabsByVm.TryGetValue(vm, out st))
            {
                if (!_refTabs.Value) return;
                st = RefTabsByVm.GetOrCreateValue(vm);
            }
            if (_refHostWin == null || _refHostWin.Target == null || !IsOnScreen(_refHostWin.Target)) _refHostWin = new WeakReference(w);
            WeakReference vr;
            var view = InfoViewByWindow.TryGetValue(w, out vr) ? vr.Target as UserControl : null;
            if (view == null) return;
            if (st.Built && (st.ViewRef == null || !ReferenceEquals(st.ViewRef.Target, view) || VisualTreeHelper.GetParent(st.Strip) == null)) { st.Built = false; st.Shown = false; st.Drawn = -1; }
            if (!st.Built)
            {
                if (!_refTabs.Value || st.Tries > 40) return;
                st.Tries++;
                if (!BuildRefStrip(st, view)) return;
            }
            st.Win = new WeakReference(w);
            object hid;
            if (!_improvedUI.Value && _treeSetter != null && HideTreeVms.TryGetValue(vm, out hid))      // with Layout Improvements the splitter setup hides it (and narrows the window)
            {
                HideTreeVms.Remove(vm);
                bool prev = _treeShown.Value;
                Invoke(_treeSetter, vm, false);
                if (_treeShown.Value != prev) _treeShown.Value = prev;
            }
            bool want = _refTabs.Value && st.Entries.Count > 0 && !st.Hidden;
            if (want != st.Shown)
            {
                st.Strip.Visibility = want ? Visibility.Visible : Visibility.Collapsed;
                var m = st.ContentMargin;
                st.Content.Margin = want ? new Thickness(m.Left, m.Top + RefTabsH, m.Right, m.Bottom) : m;
                st.Shown = want;
            }
            if (!want) return;
            var cur = Get(vm, "SelectedMuseumEntry");
            int ci = cur == null ? -1 : TabIndex(st, cur);
            if (ci >= 0)
            {
                // the unit is shown: has its page the loadout drop-down? then the variant name "[Level 1]" is left out of the tab
                bool multi = GetB(vm, "HasMultipleLoadouts"), old;
                var key = st.Entries[ci];
                if (!st.Multi.TryGetValue(key, out old) || old != multi) { st.Multi[key] = multi; st.Version++; }
            }
            if (st.Drawn != st.Version) DrawRefTabs(st, vm);
            if (!ReferenceEquals(cur, st.Current)) { st.Current = cur; PaintRefTabs(st, view); }
        }

        bool BuildRefStrip(RefTabState st, UserControl view)
        {
            var root = view.Content as Grid;
            if (root == null) return false;
            ScrollViewer sv = null;
            foreach (var ch in root.Children) { var s = ch as ScrollViewer; if (s != null && Grid.GetColumn(s) == 2 && Grid.GetRow(s) == 1) { sv = s; break; } }
            if (sv == null) { Trace("reference tabs: the unit page's ScrollViewer was not found in " + view.GetType().Name); return false; }
            var dark = view.TryFindResource("Brush.Window.BackgroundDark") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 32, 38));
            st.Panel = new StackPanel { Orientation = Orientation.Horizontal };
            st.Scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = st.Panel };
            var scroll = st.Scroll;
            st.Scroll.PreviewMouseWheel += (o, e) => { try { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta / 2f); e.Handled = true; } catch { } };
            st.Strip = new Border { Background = dark, Height = RefTabsH, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch, Child = st.Scroll, Visibility = Visibility.Collapsed };
            Grid.SetColumn(st.Strip, 2); Grid.SetRow(st.Strip, 1);
            root.Children.Add(st.Strip);
            st.Content = sv; st.ContentMargin = sv.Margin; st.ViewRef = new WeakReference(view);
            st.Built = true; st.Shown = false; st.Drawn = -1;
            Trace("reference tabs: strip added to " + view.GetType().Name);
            return true;
        }

        void DrawRefTabs(RefTabState st, object vm)
        {
            st.Panel.Children.Clear(); st.Heads.Clear(); st.Names.Clear();
            var norm = new SolidColorBrush(Color.FromRgb(170, 176, 186));
            foreach (var entry in st.Entries.ToArray())
            {
                var e1 = entry;
                var name = new TextBlock { Text = RefTabName(st, e1), FontSize = 14f, MaxWidth = 190f, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = norm };
                var close = new TextBlock { Text = "×", FontSize = 16f, Margin = new Thickness(8f, -1f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center, Foreground = norm, Cursor = Cursors.Hand };
                close.MouseEnter += (o, ev) => close.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                close.MouseLeave += (o, ev) => close.Foreground = norm;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(name); row.Children.Add(close);
                var head = new Border { Child = row, Padding = new Thickness(10f, 0f, 8f, 0f), Margin = new Thickness(0f, 0f, 2f, 0f), BorderThickness = new Thickness(0f, 0f, 0f, 2f), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
                head.MouseLeftButtonDown += (o, ev) => { try { RefTabClick(st, vm, e1); ev.Handled = true; } catch (Exception ex) { Trace("reference tab click: " + ex.Message); } };
                close.MouseLeftButtonDown += (o, ev) => { try { RefTabClose(st, vm, e1); ev.Handled = true; } catch (Exception ex) { Trace("reference tab close: " + ex.Message); } };
                st.Panel.Children.Add(head); st.Heads.Add(head); st.Names.Add(name);
            }
            st.Drawn = st.Version; st.Current = null;       // painted next
        }

        static readonly Regex VariantTail = new Regex(@"\s*\[[^\]]*\]\s*$", RegexOptions.Compiled);

        /// <summary>The unit's name; without the trailing "[variant]" when the unit's page has a loadout drop-down.</summary>
        string RefTabName(RefTabState st, object entry)
        {
            var name = TextOf(Get(entry, "DisplayName")) ?? "?";
            bool multi;
            if (st.Multi.TryGetValue(entry, out multi) && multi)
            {
                var cut = VariantTail.Replace(name, "");
                if (cut.Length > 0) name = cut;
            }
            return name;
        }

        /// <summary>The shown tab: lighter ground, orange line under it, white name.</summary>
        void PaintRefTabs(RefTabState st, UserControl view)
        {
            var on = view.TryFindResource("Brush.Window.Background") as Brush ?? new SolidColorBrush(Color.FromRgb(48, 52, 60));
            var line = view.TryFindResource("Brush.Glyph.Emphasis") as Brush ?? new SolidColorBrush(Color.FromRgb(224, 120, 40));
            var white = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            var norm = new SolidColorBrush(Color.FromRgb(170, 176, 186));
            var none = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            for (int i = 0; i < st.Heads.Count && i < st.Entries.Count; i++)
            {
                bool sel = SamePage(st.Entries[i], st.Current);
                st.Heads[i].Background = sel ? on : none;
                st.Heads[i].BorderBrush = sel ? line : none;
                st.Names[i].Foreground = sel ? white : norm;
                if (sel) try { st.Heads[i].BringIntoView(); } catch { }
            }
        }

        void RefTabClick(RefTabState st, object vm, object entry)
        {
            var w = st.Win == null ? null : st.Win.Target;
            if (w != null) _refHostWin = new WeakReference(w);
            ShowRefEntry(vm, entry);
        }

        void RefTabClose(RefTabState st, object vm, object entry)
        {
            int at = TabIndex(st, entry);
            if (at < 0) return;
            bool wasShown = SamePage(entry, Get(vm, "SelectedMuseumEntry"));
            st.Entries.RemoveAt(at); st.Version++;
            if (st.Entries.Count == 1) st.Hidden = true;       // one tab left: the strip goes away
            if (wasShown && st.Entries.Count > 0) ShowRefEntry(vm, st.Entries[Math.Min(at, st.Entries.Count - 1)]);
        }
    }
}
