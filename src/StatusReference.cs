using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace SmartWindows
{
    /// <summary>
    /// UnitReferenceTab: a "Reference" tab (last) in the Unit Status window with the Unit Reference of the selected unit, as the
    /// unit's "Information" context menu item shows it (its ship / variant and loadout), without the tree, the search panel and
    /// the '&lt;' button (ObjectInformationViewModel with showTree: false in a new ObjectInformationView).
    /// The tab lives in UnitStatusViewModel.Tabs like the game's ones: added in its constructor, available for a unit with
    /// parameters (not a weapon), content made when the tab is shown (EnsureContent) and dropped when the unit changes
    /// (DisposeOwnedContent); the inner tab (Overview, Weapons, ...) is kept from one unit to the next.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _statusRef;
        const int RefTabKind = 5;   // past the game's UnitStatusTab values (Engagement ... Seeker); 6 = Alternative Weapons Control's Weapons tab

        Type _ustType, _ustKind, _oivmType, _oivType, _weaponBaseType;
        FieldInfo _ustUnit;
        MethodInfo _ustSelectFirst, _refVariantIndex, _refLoadout, _oivmEnsureTab;

        sealed class RefTab
        {
            public object Tab;           // UnitStatusTabViewModel
            public object Vm, View;      // ObjectInformationViewModel / ObjectInformationView shown
            public int InnerTab = -1;    // SelectedTabIndex kept between units
        }
        static readonly ConditionalWeakTable<object, RefTab> RefTabs = new ConditionalWeakTable<object, RefTab>();
        readonly List<WeakReference> _refViews = new List<WeakReference>();   // views made, for the Replace button (RefTab objects)

        void HookStatusReference(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var usvm = asm.GetType("SeapowerUI.ViewModels.UnitStatusViewModel");
            _ustType = asm.GetType("SeapowerUI.ViewModels.UnitStatusTabViewModel");
            _ustKind = asm.GetType("SeapowerUI.ViewModels.UnitStatusTab");
            _oivmType = asm.GetType("SeapowerUI.ViewModels.ObjectInformationViewModel");
            _oivType = asm.GetType("SeapowerUI.ObjectInformationView");
            _weaponBaseType = asm.GetType("SeaPower.WeaponBase");
            if (usvm == null || _ustType == null || _ustKind == null || _oivmType == null || _oivType == null)
            {
                Logger.LogWarning("Unit Status / Unit Reference types not found (game version?); UnitReferenceTab is not available.");
                return;
            }
            _ustUnit = usvm.GetField("_unit", inst);
            _ustSelectFirst = usvm.GetMethod("SelectFirstAvailableTab", inst);
            _refVariantIndex = _oivmType.GetMethod("GetReferenceVariantIndex", BindingFlags.Static | BindingFlags.Public);
            _refLoadout = _oivmType.GetMethod("GetReferenceLoadoutVariant", BindingFlags.Static | BindingFlags.Public);
            _oivmEnsureTab = _oivmType.GetMethod("EnsureSelectedTabAvailable", inst);
            var ctor = usvm.GetConstructors(inst);
            var sw = usvm.GetMethod("SwitchUnit", inst);
            var ens = usvm.GetMethod("EnsureContent", inst);
            var disp = usvm.GetMethod("DisposeOwnedContent", inst);
            if (ctor.Length == 0 || sw == null || ens == null || disp == null || _ustUnit == null)
            {
                Logger.LogWarning("UnitStatusViewModel members not found (game updated?); UnitReferenceTab is not available.");
                return;
            }
            foreach (var c in ctor) harmony.Patch(c, postfix: Hm("StatusCreatedPostfix"));
            harmony.Patch(sw, postfix: Hm("StatusSwitchPostfix"));
            harmony.Patch(ens, postfix: Hm("StatusEnsurePostfix"));
            harmony.Patch(disp, prefix: Hm("StatusDisposePrefix"));
            var cv = _oivmType.GetMethod("ChangeVariant", inst);
            if (cv != null) TryPatch(harmony, cv, null, "ChangeVariantPostfix", null);
            Trace("patched UnitStatusViewModel (Reference tab)");
        }

        // ---------------------------------------------------------------- hooks

        static readonly List<WeakReference> Statuses = new List<WeakReference>();

        /// <summary>FullUnitName: the game sets the header name only when the window switches to another unit; Replace (Unit Reference, encyclopedia
        /// mode) swaps / changes the unit under the same window, and ObjectInformationViewModel.ChangeVariant (its last step) triggers this.</summary>
        void SyncFullNames()
        {
            if (!_fullName.Value) return;
            lock (Statuses)
            {
                for (int i = Statuses.Count - 1; i >= 0; i--)
                {
                    var st = Statuses[i].Target;
                    if (st == null) { Statuses.RemoveAt(i); continue; }
                    try { FullUnitName(st); } catch { }
                }
            }
        }

        static void ChangeVariantPostfix()
        {
            var a = Active;
            if ((object)a == null) return;
            a.SyncFullNames();
        }

        static void StatusCreatedPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null) return;
            lock (Statuses) Statuses.Add(new WeakReference(__instance));
            try { a.AddRefTab(__instance); } catch (Exception e) { Trace("Reference tab: " + e); }
        }

        static void StatusSwitchPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null) return;
            try { a.UpdateRefTab(__instance); } catch (Exception e) { Trace("Reference tab: " + e); }
            try { a.FullUnitName(__instance); } catch (Exception e) { Trace("full unit name: " + e.Message); }
        }

        /// <summary>FullUnitName: the unit's full name (DisplayNameFull) in the Unit Status header instead of the short one.</summary>
        void FullUnitName(object status)
        {
            if (!_fullName.Value) return;
            var unit = StatusUnit(status);
            if (unit == null) return;
            var full = Get(unit, "DisplayNameFull") as string;
            if (!string.IsNullOrEmpty(full) && !string.Equals(Get(status, "UnitName") as string, full)) SetV(status, "UnitName", full);
        }

        static void StatusEnsurePostfix(object __instance, object tab)
        {
            var a = Active;
            if ((object)a == null || tab == null) return;
            try { a.EnsureRefContent(__instance, tab); } catch (Exception e) { Trace("Reference tab: " + e); }
        }

        // before the game's: the content of its tabs and of ours goes when the unit changes (or the window's model is disposed)
        static void StatusDisposePrefix(object __instance)
        {
            var a = Active;
            if ((object)a == null) return;
            try { a.DropRefContent(__instance); } catch (Exception e) { Trace("Reference tab: " + e); }
        }

        // ---------------------------------------------------------------- the tab

        void AddRefTab(object status)
        {
            RefTab rt;
            if (RefTabs.TryGetValue(status, out rt)) return;
            var tabs = Get(status, "Tabs") as IList;
            if (tabs == null) return;
            var tab = Activator.CreateInstance(_ustType, status, Enum.ToObject(_ustKind, RefTabKind), "Reference");
            tabs.Add(tab);
            RefTabs.Add(status, new RefTab { Tab = tab });
            UpdateRefTab(status);
        }

        object StatusUnit(object status) { return _ustUnit == null ? null : _ustUnit.GetValue(status); }

        bool RefAvailableFor(object unit)
        {
            if (!_statusRef.Value || unit == null) return false;
            if (_weaponBaseType != null && _weaponBaseType.IsInstanceOfType(unit)) return false;
            var obp = unit.GetType().GetField("_obp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return obp != null && obp.GetValue(unit) != null;
        }

        void UpdateRefTab(object status)
        {
            RefTab rt;
            if (!RefTabs.TryGetValue(status, out rt)) return;
            bool avail = RefAvailableFor(StatusUnit(status));
            SetV(rt.Tab, "IsAvailable", avail);
            if (!avail && GetB(rt.Tab, "IsSelected") && _ustSelectFirst != null)
            {
                SetV(rt.Tab, "IsSelected", false);
                Invoke(_ustSelectFirst, status);
            }
        }

        void EnsureRefContent(object status, object tab)
        {
            RefTab rt;
            if (!RefTabs.TryGetValue(status, out rt) || !ReferenceEquals(rt.Tab, tab)) return;
            if (!GetB(tab, "IsAvailable") || GetB(tab, "HasContentError") || Get(tab, "Content") != null) return;
            var unit = StatusUnit(status);
            if (!RefAvailableFor(unit)) return;
            try
            {
                var iniM = unit.GetType().GetMethod("getObjectIniName", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                var ini = iniM == null ? null : iniM.Invoke(unit, null) as string;
                if (string.IsNullOrEmpty(ini)) return;
                int variant = 0;
                string loadout = null;
                try { if (_refVariantIndex != null) variant = (int)_refVariantIndex.Invoke(null, new[] { unit }); } catch { }
                try { if (_refLoadout != null) loadout = _refLoadout.Invoke(null, new[] { unit }) as string; } catch { }
                var mgv = GameAssembly().GetType("SeapowerUI.MainGameView");
                var ctx = mgv == null ? null : mgv.GetField("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
                // (context, initialIniRef, initialVariantIndex, showTree, initialLoadoutVariant)
                var vm = Activator.CreateInstance(_oivmType, ctx, ini, variant, false, loadout);
                if (rt.InnerTab > 0)
                {
                    SetV(vm, "SelectedTabIndex", rt.InnerTab);
                    if (_oivmEnsureTab != null) Invoke(_oivmEnsureTab, vm);
                }
                var view = Activator.CreateInstance(_oivType);
                SetV(view, "DataContext", vm);
                rt.Vm = vm;
                rt.View = view;
                SetV(tab, "Content", view);
                lock (_refViews) { _refViews.RemoveAll(w => !w.IsAlive); _refViews.Add(new WeakReference(rt)); }
                Verbose("Reference tab: " + ini + " #" + variant + (string.IsNullOrEmpty(loadout) ? "" : " [" + loadout + "]"));
            }
            catch (Exception e)
            {
                var ex = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                Trace("Reference tab: " + ex);
                SetV(tab, "Content", null);
                SetV(tab, "ContentError", "The unit reference could not be opened for the selected unit.");
            }
        }

        void DropRefContent(object status)
        {
            RefTab rt;
            if (!RefTabs.TryGetValue(status, out rt) || rt.Vm == null) return;
            var idx = Get(rt.Vm, "SelectedTabIndex");
            if (idx is int) rt.InnerTab = (int)idx;
            SetV(rt.Tab, "Content", null);
            var d = rt.Vm as IDisposable;
            rt.Vm = null;
            rt.View = null;
            if (d != null) { try { d.Dispose(); } catch (Exception e) { Trace("Reference tab dispose: " + e.Message); } }
        }

        // ---------------------------------------------------------------- showing it
        // The Unit Status view shows each game tab's content in a place of its own (bound to EngagementTab.Content, ...), not
        // a generic "selected tab" presenter, so a new tab's content is not shown by the game: the mod puts a host of its own
        // next to the place of the Engagement tab's content (same panel, grid cell, margins) and shows it while the Reference
        // tab is selected. If the game does show the view itself (it has a visual parent), nothing is added.

        sealed class RefHost { public WeakReference View; public Noesis.Border Host; public bool Failed; }
        static readonly ConditionalWeakTable<object, RefHost> RefHosts = new ConditionalWeakTable<object, RefHost>();

        /// <summary>Each frame for a window on screen: if it is a Unit Status window, show / hide our host of the Reference tab.</summary>
        void StatusRefStep(object w)
        {
            if (RefTabs == null) return;
            var rh = RefHosts.GetOrCreateValue(w);
            if (rh.Failed) return;
            // the UnitStatusView of this window was caught when it loaded (ViewCapture): no search
            var usv = rh.View == null ? null : rh.View.Target as Noesis.DependencyObject;
            if (usv == null || Noesis.VisualTreeHelper.GetParent(usv) == null)
            {
                WeakReference r;
                usv = StatusViewByWindow.TryGetValue(w, out r) ? r.Target as Noesis.DependencyObject : null;
                if (usv == null || Noesis.VisualTreeHelper.GetParent(usv) == null) return;
                rh.View = new WeakReference(usv);
            }
            var status = Get(usv, "DataContext");
            RefTab rt;
            if (status == null || !RefTabs.TryGetValue(status, out rt)) return;
            bool selected = GetB(rt.Tab, "IsSelected") && GetB(rt.Tab, "IsAvailable");
            var view = rt.View as Noesis.UIElement;

            if (rh.Host == null || Noesis.VisualTreeHelper.GetParent(rh.Host) == null)
            {
                if (!selected || view == null) return;
                var vp = Noesis.VisualTreeHelper.GetParent(view);
                if (vp != null) return;   // the game shows it
                rh.Host = MakeRefHost(usv, status);
                if (rh.Host == null) { rh.Failed = true; return; }
            }
            if (selected && view != null)
            {
                if (!ReferenceEquals(rh.Host.Child, view))
                {
                    var vp = Noesis.VisualTreeHelper.GetParent(view) as Noesis.Border;
                    if (vp != null && !ReferenceEquals(vp, rh.Host)) vp.Child = null;
                    rh.Host.Child = view;
                }
                if (rh.Host.Visibility != Noesis.Visibility.Visible) rh.Host.Visibility = Noesis.Visibility.Visible;
            }
            else
            {
                if (rh.Host.Visibility != Noesis.Visibility.Collapsed) rh.Host.Visibility = Noesis.Visibility.Collapsed;
                if (view == null && rh.Host.Child != null) rh.Host.Child = null;
            }
        }

        /// <summary>A Border next to where the game shows another tab's content (the Engagement board first), same cell and margins.</summary>
        Noesis.Border MakeRefHost(Noesis.DependencyObject usv, object status)
        {
            var contents = new List<object>();
            foreach (var name in new[] { "EngagementTab", "SensorsTab", "DamageControlTab", "FlightDeckTab", "SeekerTab" })
            {
                var c = Get(Get(status, name), "Content");
                if (c != null) contents.Add(c);
            }
            Noesis.FrameworkElement place = null;
            // UnitStatusView: one Grid per tab, Visibility bound to "<Name>Tab.IsSelected", all in the same cell of one Grid
            place = FindFirst(usv, e =>
            {
                if (!(e is Noesis.FrameworkElement)) return false;
                Noesis.BindingExpression x = null;
                try { x = Noesis.BindingOperations.GetBindingExpression(e, Noesis.UIElement.VisibilityProperty); } catch { }
                var path = x == null || x.ParentBinding == null || x.ParentBinding.Path == null ? null : x.ParentBinding.Path.Path;
                return path != null && path.EndsWith("Tab.IsSelected", StringComparison.Ordinal) && !path.StartsWith("Reference", StringComparison.Ordinal);
            }) as Noesis.FrameworkElement;
            var stack = new Stack<Noesis.DependencyObject>();
            stack.Push(usv);
            int budget = 30000;
            while (place == null && stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                var cc = e as Noesis.ContentControl;
                var cp = e as Noesis.ContentPresenter;
                var content = cc != null ? cc.Content : cp != null ? cp.Content : null;
                if (content != null && contents.Contains(content) && !(Noesis.VisualTreeHelper.GetParent(e) is Noesis.TabPanel)) { place = (Noesis.FrameworkElement)e; break; }
                int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                for (int k = n - 1; k >= 0; k--) { var c = Noesis.VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            if (place == null) { Trace("Reference tab: no place of another tab's content found in UnitStatusView; its tree:\n" + DumpTree(usv)); return null; }
            // up to a panel that can take one more child
            var chain = Describe(place);
            Noesis.FrameworkElement refEl = place;
            var parent = Noesis.VisualTreeHelper.GetParent(refEl);
            while (parent != null && !(parent is Noesis.Panel) && !ReferenceEquals(parent, usv))
            {
                refEl = parent as Noesis.FrameworkElement;
                if (refEl == null) break;
                chain += " < " + Describe(parent);
                parent = Noesis.VisualTreeHelper.GetParent(refEl);
            }
            var panel = parent as Noesis.Panel;
            if (panel == null || refEl == null) { Trace("Reference tab: no panel above " + chain); return null; }
            var host = new Noesis.Border { Visibility = Noesis.Visibility.Collapsed, Margin = refEl.Margin,
                                           HorizontalAlignment = Noesis.HorizontalAlignment.Stretch, VerticalAlignment = Noesis.VerticalAlignment.Stretch };
            Noesis.Grid.SetRow(host, Noesis.Grid.GetRow(refEl));
            Noesis.Grid.SetColumn(host, Noesis.Grid.GetColumn(refEl));
            Noesis.Grid.SetRowSpan(host, Noesis.Grid.GetRowSpan(refEl));
            Noesis.Grid.SetColumnSpan(host, Noesis.Grid.GetColumnSpan(refEl));
            int at = panel.Children.IndexOf(refEl as Noesis.UIElement);
            panel.Children.Insert(at < 0 ? panel.Children.Count : at + 1, host);
            Trace("Reference tab: host added to " + Describe(panel) + " next to " + chain);
            return host;
        }

        static Noesis.DependencyObject FindFirst(Noesis.DependencyObject root, Func<Noesis.DependencyObject, bool> match)
        {
            var stack = new Stack<Noesis.DependencyObject>();
            stack.Push(root);
            int budget = 30000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                if (!ReferenceEquals(e, root) && match(e)) return e;
                int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                for (int k = n - 1; k >= 0; k--) { var c = Noesis.VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            return null;
        }

        /// <summary>Diagnostics: the visual tree under 'root' (type, name, visibility, data context, content, bindings).</summary>
        static string DumpTree(Noesis.DependencyObject root)
        {
            var sb = new System.Text.StringBuilder();
            int lines = 0;
            Action<Noesis.DependencyObject, int> walk = null;
            walk = (e, d) =>
            {
                if (lines++ > 600 || d > 18) return;
                sb.Append(new string(' ', d * 2)).Append(e.GetType().Name);
                var fe = e as Noesis.FrameworkElement;
                if (fe != null)
                {
                    if (!string.IsNullOrEmpty(fe.Name)) sb.Append(" '").Append(fe.Name).Append("'");
                    if (fe.Visibility != Noesis.Visibility.Visible) sb.Append(" ").Append(fe.Visibility);
                    var dc = fe.DataContext; if (dc != null && (Noesis.VisualTreeHelper.GetParent(e) as Noesis.FrameworkElement == null || !ReferenceEquals(((Noesis.FrameworkElement)Noesis.VisualTreeHelper.GetParent(e)).DataContext, dc))) sb.Append(" dc=").Append(dc.GetType().Name);
                    if (fe.Parent == null && Noesis.VisualTreeHelper.GetParent(e) is Noesis.Panel) { }
                    sb.Append(" r").Append(Noesis.Grid.GetRow(fe)).Append("c").Append(Noesis.Grid.GetColumn(fe));
                }
                var cc = e as Noesis.ContentControl; var cp = e as Noesis.ContentPresenter; var ic = e as Noesis.ItemsControl;
                object content = cc != null ? cc.Content : cp != null ? cp.Content : null;
                if (content != null) sb.Append(" content=").Append(content is string ? "\"" + content + "\"" : content.GetType().Name);
                if (ic != null && ic.ItemsSource != null) sb.Append(" items=").Append(ic.ItemsSource.GetType().Name);
                foreach (var dp in new[] { Noesis.ContentControl.ContentProperty, Noesis.ContentPresenter.ContentProperty, Noesis.ItemsControl.ItemsSourceProperty, Noesis.UIElement.VisibilityProperty })
                {
                    try
                    {
                        var x = Noesis.BindingOperations.GetBindingExpression(e, dp);
                        if (x != null && x.ParentBinding != null && x.ParentBinding.Path != null) sb.Append(" {").Append(x.ParentBinding.Path.Path).Append("}");
                    }
                    catch { }
                }
                sb.Append('\n');
                int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                for (int k = 0; k < n; k++) { var c = Noesis.VisualTreeHelper.GetChild(e, k); if (c != null) walk(c, d + 1); }
            };
            walk(root, 0);
            return sb.ToString();
        }

        /// <summary>Each frame: the Replace button of the Reference tabs on screen (hidden outside the Encyclopedia).</summary>
        void RefTabsStep()
        {
            if (_refViews.Count == 0 || !_loadoutList.Value) return;
            for (int i = _refViews.Count - 1; i >= 0; i--)
            {
                var rt = _refViews[i].Target as RefTab;
                if (rt == null) { _refViews.RemoveAt(i); continue; }
                var view = rt.View as Noesis.UIElement;
                if (view == null || rt.Vm == null || !view.IsVisible) continue;
                ReplaceStep(view, view, rt.Vm);
            }
        }
    }
}
