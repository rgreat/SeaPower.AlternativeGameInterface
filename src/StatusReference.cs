using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// UnitReferenceTab: a "Reference" tab (last) in the Unit Status window with the Unit Reference of the selected unit, as the
    /// unit's "Information" context menu item shows it (its ship / variant and loadout), without the tree, the search panel and
    /// the '&lt;' button (ObjectInformationViewModel with showTree: false in a new ObjectInformationView).
    /// The tab lives in UnitStatusViewModel.Tabs like the game's ones: added in its constructor, available for a unit with
    /// parameters (not a weapon), content made when the tab is shown (EnsureContent) and dropped when the unit changes
    /// (DisposeOwnedContent); the inner tab (Overview, Weapons, ...) is kept from one unit to the next.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _statusRef;
        // a Kind far past the game's UnitStatusTab values: the game's EnsureContent builds the content of a tab by its Kind, and a newer game build
        // took 5 for its Supplies tab (our tab then showed the Supplies view model and no Reference); 6 = Alternative Weapons Control's Weapons tab
        const int RefTabKind = 105;

        Type _ustType, _ustKind, _oivmType, _oivType, _weaponBaseType;
        FieldInfo _ustUnit;
        MethodInfo _ustSelectFirst, _refVariantIndex, _refLoadout, _oivmEnsureTab;

        sealed class RefTab
        {
            public object Tab;           // UnitStatusTabViewModel
            public object Vm, View;      // ObjectInformationViewModel / ObjectInformationView shown
            public int InnerTab = -1;    // SelectedTabIndex kept between units
            public string ShownIni;      // the ini the content is made for (a unit's own or a contact's class)
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
            harmony.Patch(sw, prefix: Hm("StatusSwitchPrefix"), postfix: Hm("StatusSwitchPostfix"));
            HookTabMemory(harmony, usvm);
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
            try { a.AddTrackTab(__instance); } catch (Exception e) { Trace("Track State tab: " + e); }
        }

        static void StatusSwitchPostfix(object __instance, object __state)
        {
            var a = Active;
            if ((object)a == null) return;
            try { a.TrackSwitch(__instance); } catch (Exception e) { Trace("Track State tab: " + e); }
            try { a.UpdateRefTab(__instance); } catch (Exception e) { Trace("Reference tab: " + e); }   // after Track State: a contact's type
            try { a.FullUnitName(__instance); } catch (Exception e) { Trace("full unit name: " + e.Message); }
            try { a.TabRestore(__instance, __state); } catch (Exception e) { Trace("remember tab: " + e.Message); }
        }

        /// <summary>The unit's name in one of the five forms (Short unit name = the game's; for a Sverdlov cruiser: Lazarev / Admiral Lazarev CL-626 /
        /// Sverdlov / Prj.68bis "Sverdlov" / Light Cruiser Prj.68bis "Sverdlov"); the short unit name when the form is empty.</summary>
        string UnitText(object unit, string mode)
        {
            string r = null;
            switch (mode)
            {
                case "FullName": r = Get(unit, "DisplayNameFull") as string; break;
                case "ShortClass": r = Get(unit, "ClassNameShort") as string; break;
                case "FullClass": r = Get(unit, "ClassNameFull") as string; break;
                case "TypeClass":
                    {
                        string cls = Get(unit, "ClassNameFull") as string;
                        string type = null;
                        try
                        {
                            var of = unit.GetType().GetField("_obp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            var obp = of == null ? null : of.GetValue(unit);
                            var tf = obp == null ? null : obp.GetType().GetField("_typeFullName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            type = tf == null ? null : tf.GetValue(obp) as string;
                        }
                        catch { }
                        r = string.IsNullOrEmpty(type) ? cls : string.IsNullOrEmpty(cls) ? type : type + " " + cls;
                        break;
                    }
            }
            return string.IsNullOrEmpty(r) ? (Get(unit, "DisplayNameShort") as string ?? "") : r;
        }

        /// <summary>The label at the right of the tabs: the game binds it to UnitStatusViewModel.UnitName (set on a unit switch).</summary>
        void FullUnitName(object status)
        {
            var unit = StatusUnit(status);
            if (unit == null) return;
            var text = UnitText(unit, _labelName.Value);
            if (!string.Equals(Get(status, "UnitName") as string, text)) SetV(status, "UnitName", text);
        }

        static readonly ConditionalWeakTable<object, object> TitleSet = new ConditionalWeakTable<object, object>();

        /// <summary>The title of the window is "<Run WindowHeader/> <Run UnitName/>" (MainGameView.xaml): the second Run is bound to the same UnitName as the label,
        /// so the last Run gets its own text (a local value replaces the binding) from WindowTitleName.</summary>
        void StatusTitleStep(object w)
        {
            WeakReference r;
            if (!StatusViewByWindow.TryGetValue(w, out r)) return;
            var usv = r.Target as Noesis.DependencyObject;
            if (usv == null) return;
            var tb = Get(w, "Header") as Noesis.TextBlock;
            if (tb == null) return;
            Noesis.Run run = null;
            int n = 0;
            foreach (var inl in (System.Collections.IEnumerable)tb.Inlines)   // WindowHeader, the space between the Runs (a Run too), UnitName: the last one
            {
                if (inl is Noesis.Run) { run = (Noesis.Run)inl; n++; }
            }
            if (run == null || n < 2) return;
            var unit = StatusUnit(Get(usv, "DataContext"));
            object mark;
            if (unit == null)
            {
                if (TitleSet.TryGetValue(run, out mark) && run.Text != "") run.Text = "";
                return;
            }
            var text = UnitText(unit, _titleName.Value);
            if (run.Text != text) run.Text = text;
            if (!TitleSet.TryGetValue(run, out mark)) TitleSet.Add(run, run);
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
            try { a.DropTrackContent(__instance); } catch (Exception e) { Trace("Track State tab: " + e); }
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
            var obp = unit.GetType().GetField("_obp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return obp != null && obp.GetValue(unit) != null;
        }

        static FieldInfo _vehClass;

        /// <summary>The unit type of a contact (Vehicle) once it is determined: Vehicle.Class, the ini name the game itself shows the type of the
        /// contact by (set when one class is left among the candidates, or when the identification is forced). Null while unknown.</summary>
        static string ContactClassIni(object vehicle)
        {
            if (vehicle == null) return null;
            try
            {
                var f = _vehClass ?? (_vehClass = vehicle.GetType().GetField("Class", BindingFlags.Instance | BindingFlags.Public));
                var boxed = f == null ? null : f.GetValue(vehicle);   // Nullable<SourcedProperty<string>>: null while there is no value
                if (boxed == null) return null;
                var vf = boxed.GetType().GetField("Value", BindingFlags.Instance | BindingFlags.Public);
                return vf == null ? null : vf.GetValue(boxed) as string;
            }
            catch { return null; }
        }

        /// <summary>What the Reference tab shows for the window's status: the unit's own ini, variant and loadout; for a contact of another side
        /// (Track State's vehicle) the type the game has determined (the variant and loadout of the real object are not given away).
        /// False: no Reference for it (option off, a weapon, an unknown type).</summary>
        bool RefTarget(object status, out string ini, out int variant, out string loadout)
        {
            ini = null; variant = 0; loadout = null;
            if (!_statusRef.Value) return false;
            var unit = StatusUnit(status);
            if (unit != null)
            {
                if (!RefAvailableFor(unit)) return false;
                var iniM = unit.GetType().GetMethod("getObjectIniName", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                ini = iniM == null ? null : iniM.Invoke(unit, null) as string;
                try { if (_refVariantIndex != null) variant = (int)_refVariantIndex.Invoke(null, new[] { unit }); } catch { }
                try { if (_refLoadout != null) loadout = _refLoadout.Invoke(null, new[] { unit }) as string; } catch { }
                return !string.IsNullOrEmpty(ini);
            }
            TrackTab tt;
            if (!TrackTabs.TryGetValue(status, out tt) || tt.Vehicle == null) return false;
            ini = ContactClassIni(tt.Vehicle);
            return !string.IsNullOrEmpty(ini);
        }

        // The type of a contact becomes known while it is selected: the contact's own Updated event (not a poll) tells when it changes.
        void WatchContact(object status, TrackTab tt)
        {
            if (ReferenceEquals(tt.UpdFor, tt.Vehicle)) return;
            UnwatchContact(tt);
            var v = tt.Vehicle;
            if (v == null) return;
            try
            {
                var ev = v.GetType().GetEvent("Updated", BindingFlags.Instance | BindingFlags.Public);
                if (ev == null) return;
                tt.LastIni = ContactClassIni(v);
                var wr = new WeakReference(status);
                EventHandler<EventArgs> h = (snd, args) => { var st = wr.Target; if (st != null) ContactUpdated(st, tt); };
                ev.AddEventHandler(v, h);
                tt.Upd = h; tt.UpdFor = v;
            }
            catch (Exception e) { Trace("Reference tab: watching the contact: " + e.Message); }
        }

        void UnwatchContact(TrackTab tt)
        {
            if (tt.Upd == null) return;
            try { tt.UpdFor.GetType().GetEvent("Updated", BindingFlags.Instance | BindingFlags.Public).RemoveEventHandler(tt.UpdFor, tt.Upd); } catch { }
            tt.Upd = null; tt.UpdFor = null; tt.LastIni = null;
        }

        void ContactUpdated(object status, TrackTab tt)
        {
            var ini = ContactClassIni(tt.Vehicle);
            if (string.Equals(ini, tt.LastIni, StringComparison.Ordinal)) return;
            tt.LastIni = ini;
            try { UpdateRefTab(status); } catch (Exception e) { Trace("Reference tab: " + e); }
        }

        void UpdateRefTab(object status)
        {
            RefTab rt;
            if (!RefTabs.TryGetValue(status, out rt)) return;
            string ini, loadout; int variant;
            bool avail = RefTarget(status, out ini, out variant, out loadout);
            SetV(rt.Tab, "IsAvailable", avail);
            // the content was made for another unit / type (a contact selected after another one, its type determined meanwhile): made again
            if (rt.Vm != null && (!avail || !string.Equals(rt.ShownIni, ini, StringComparison.Ordinal))) DropRefContent(status);
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
            if (!GetB(tab, "IsAvailable") || GetB(tab, "HasContentError") || Get(tab, "Content") != null)
            {
                if (GetB(tab, "IsSelected")) RefWhy(status, "not made: available " + GetB(tab, "IsAvailable") + ", content error " + GetB(tab, "HasContentError") + ", content " + (Get(tab, "Content") == null ? "none" : Get(tab, "Content").GetType().Name));
                return;
            }
            string ini, loadout; int variant;
            if (!RefTarget(status, out ini, out variant, out loadout)) { RefWhy(status, "not made: no target (" + (StatusUnit(status) == null ? "no unit, contact type unknown or none" : StatusUnit(status).GetType().Name) + ")"); return; }
            try
            {
                var mgv = GameAssembly().GetType("SeapowerUI.MainGameView");
                var ctx = mgv == null ? null : mgv.GetField("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
                // (context, initialIniRef, initialVariantIndex, showTree, initialLoadoutVariant)
                object vm;
                var wEntry = WeaponEntryOf(ini);
                // a missile / torpedo / gun round has no unit entry (ResolveUnitEntry finds only units): its page is a weapon entry, as the game opens it for a weapon
                if (wEntry != null) vm = Activator.CreateInstance(_oivmType.Assembly.GetType("SeapowerUI.ViewModels.ObjectWeaponInformationViewModel"), ctx, wEntry);
                else vm = Activator.CreateInstance(_oivmType, ctx, ini, variant, false, loadout);
                if (rt.InnerTab > 0)
                {
                    SetV(vm, "SelectedTabIndex", rt.InnerTab);
                    if (_oivmEnsureTab != null) Invoke(_oivmEnsureTab, vm);
                }
                var view = Activator.CreateInstance(_oivType);
                SetV(view, "DataContext", vm);
                rt.Vm = vm;
                rt.View = view;
                rt.ShownIni = ini;
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

        /// <summary>The encyclopedia's weapon entry (MuseumWeaponEntry) with this ini, when the ini is not a unit's.</summary>
        object WeaponEntryOf(string ini)
        {
            try
            {
                var asm = _oivmType.Assembly;
                var edm = asm.GetType("SeaPower.EncyclopedicDataManager");
                var inst = asm.GetType("SeaPower.Singleton`1").MakeGenericType(edm).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                if (inst == null) return null;
                var unitEntry = _oivmType.GetMethod("ResolveUnitEntry", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { ini, 0, null });
                if (unitEntry != null) return null;
                var list = edm.GetProperty("WeaponMuseumEntries").GetValue(inst, null) as IEnumerable;
                if (list == null) return null;
                foreach (var e in list) if (string.Equals(Get(e, "IniRef") as string, ini, StringComparison.OrdinalIgnoreCase)) return e;
            }
            catch (Exception e) { Trace("Reference tab: weapon entry: " + e.Message); }
            return null;
        }

        static readonly HashSet<string> RefWhySeen = new HashSet<string>();
        /// <summary>The reason the selected Reference tab got no content: once per reason (the tab is checked on every refresh).</summary>
        static void RefWhy(object status, string why)
        {
            if (RefWhySeen.Add(why)) Trace("Reference tab: " + why);
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
            if (GetB(rt.Tab, "IsSelected") && view == null) RefWhy(status, "selected, but no view made (available " + GetB(rt.Tab, "IsAvailable") + ")");

            if (rh.Host == null || Noesis.VisualTreeHelper.GetParent(rh.Host) == null)
            {
                if (!selected || view == null) return;
                var vp = Noesis.VisualTreeHelper.GetParent(view);
                if (vp != null) { RefWhy(status, "the view already has a parent (" + vp.GetType().Name + "): no host added"); return; }   // the game shows it
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
