using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace AlternativeGameInterface
{
    /// <summary>
    /// OptimizeTree: the unit tree of the Unit Reference window (its own copy of the encyclopedia tree, made in
    /// UnitTreeBrowserViewModel - set_MuseumEntries in the constructor and again when the units have loaded):
    ///  - the classes of a unit type (category -> type -> class) are sorted by service entry date (undated last, then by name);
    ///  - a class group that holds loadout units ("Class [Loadout]", one per loadout of a vessel/sub except Default/Empty/Ferry)
    ///    is reordered: units by date (then label), each unit followed by its loadout units (by date; Early, then the base
    ///    unit = Default, then the rest in the ini's order). Labels: the unit's qualifier (outer ()/[] stripped; the class name
    ///    when empty) + " [loadout]" for the loadout units, no suffix for Default or when the unit has a single variant;
    ///    a unit without a Default loadout loses its own leaf (only its loadout units remain).
    /// Ships (children of a unit) are not touched. Only the encyclopedia browser ("Encyclopedia" state), only unit categories;
    /// applies to windows opened afterwards. The game's own data (EncyclopedicDataManager) is not touched: the browser works on a copy.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _treeOpt;
        ConfigEntry<bool> _loadoutList;

        /// <summary>
        /// ShowLoadoutList: ObjectInformationViewModel.HasMultipleLoadouts (the loadout selector's visibility) is true only for
        /// aircraft; the list itself (AvailableLoadouts) is filled for every unit. The postfix makes it true for any unit with > 1.
        /// </summary>
        void HookLoadoutList(HarmonyLib.Harmony harmony, Assembly asm)
        {
            var oivm = asm.GetType("SeapowerUI.ViewModels.ObjectInformationViewModel");
            var pr = oivm == null ? null : oivm.GetProperty("HasMultipleLoadouts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var get = pr == null ? null : pr.GetGetMethod(true);
            if (get == null) { Logger.LogWarning("ObjectInformationViewModel.HasMultipleLoadouts not found (game updated?); ShowLoadoutList is not available."); return; }
            harmony.Patch(get, postfix: Hm("LoadoutListPostfix"));
            Trace("patched ObjectInformationViewModel.get_HasMultipleLoadouts");
            // a loadout unit (and its ships) gets only its own loadout in the list: let it have all of the unit's, its own chosen
            var upd = oivm.GetMethod("UpdateLoadoutsForEntry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (upd != null) { harmony.Patch(upd, prefix: Hm("LoadoutsForEntryPrefix"), finalizer: Hm("LoadoutsForEntryFinalizer")); Trace("patched ObjectInformationViewModel.UpdateLoadoutsForEntry"); }
            else Trace("ObjectInformationViewModel.UpdateLoadoutsForEntry not found: a loadout unit lists only its own loadout");
        }

        // the game's filter is "not a loadout unit, or this loadout"; for the call the entry counts as the base unit
        // (the loadout chosen is still its own: PredefinedLoadoutVariant, unless the window asks for another)
        static void LoadoutsForEntryPrefix(object entry, out object __state)
        {
            __state = null;
            var a = Active;
            if ((object)a == null || !a._loadoutList.Value || entry == null || !a.GetB(entry, "IsLoadoutUnit")) return;
            try { a.CompleteLoadoutData(entry); } catch (Exception e) { Trace("loadout data: " + e.Message); }
            a.SetV(entry, "IsLoadoutUnit", false);
            __state = entry;
        }

        // A loadout unit is built from parameters that hold only its own loadout (SimpleObjectParameters.CreateLoadoutUnit:
        // _loadouts = { that one }), so its entry knows the sensors to remove (LoadoutSensorsToRemove, ini "<loadout>_SensorsToRemove"),
        // the weapon mounts (LoadoutWeaponSystemEntries) and pod ammunition of that loadout only. With the other loadouts in the list,
        // choosing one of them showed the sensors of every loadout (nothing to remove found for it). The entry's tables get the other
        // loadouts' rows from the entries of the same ini (the base unit and its other loadout units) - once per table.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> LoadoutDataDone = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();      // weak: the tables of every unit were kept for good
        static bool DataDone(object d) { object x; return LoadoutDataDone.TryGetValue(d, out x); }

        void CompleteLoadoutData(object entry)
        {
            var ini = Get(entry, "IniRef") as string;
            if (string.IsNullOrEmpty(ini)) return;
            var sensors = Get(entry, "LoadoutSensorsToRemove") as IDictionary;
            var mounts = Get(entry, "LoadoutWeaponSystemEntries") as IDictionary;
            var pods = Get(entry, "LoadoutPodAmmunition") as IDictionary;
            if ((sensors == null || DataDone(sensors)) && (mounts == null || DataDone(mounts)) && (pods == null || DataDone(pods))) return;
            var edm = GameAssembly().GetType("SeaPower.EncyclopedicDataManager");
            var inst = edm == null ? null : edm.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null, null);
            var all = Get(inst, "MuseumObjectEntries") as IEnumerable;
            if (all == null) return;
            int added = 0;
            foreach (var other in all)
            {
                if (other == null || ReferenceEquals(other, entry) || (Get(other, "IniRef") as string) != ini) continue;
                added += Merge(sensors, Get(other, "LoadoutSensorsToRemove") as IDictionary);
                added += Merge(mounts, Get(other, "LoadoutWeaponSystemEntries") as IDictionary);
                added += Merge(pods, Get(other, "LoadoutPodAmmunition") as IDictionary);
            }
            foreach (var d in new[] { sensors, mounts, pods }) if (d != null && !DataDone(d)) LoadoutDataDone.Add(d, d);
            if (added > 0) Trace("loadout data: " + ini + " (loadout unit) got " + added + " row(s) of its other loadouts");
        }

        static int Merge(IDictionary into, IDictionary from)
        {
            if (into == null || from == null || ReferenceEquals(into, from)) return 0;
            int n = 0;
            foreach (DictionaryEntry kv in from) if (!into.Contains(kv.Key)) { into[kv.Key] = kv.Value; n++; }
            return n;
        }

        static Exception LoadoutsForEntryFinalizer(Exception __exception, object __state)
        {
            if (__state != null && (object)Active != null) Active.SetV(__state, "IsLoadoutUnit", true);
            return __exception;
        }

        static void LoadoutListPostfix(object __instance, ref bool __result)
        {
            var a = Active;
            if (__result || (object)a == null || !a._loadoutList.Value) return;
            var list = a.Get(__instance, "AvailableLoadouts") as ICollection;
            if (list != null && list.Count > 1) __result = true;
        }
        // The "Replace" button next to the loadout selector (TestCommand: gives the unit shown in the scene the loadout chosen)
        // only makes sense in the game's Encyclopedia mode (IsTestMode = Globals._inMuseum, the EncyclopediaMission scene);
        // with the selector shown for every unit it is kept hidden elsewhere, together with whatever holds only it (its
        // backing). SetCurrentValue: the game's binding stays and takes over again in the Encyclopedia.
        sealed class ReplaceState { public WeakReference Element; public int Tries; public bool Done, Hidden; public Noesis.Visibility Before; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ReplaceState> ReplaceStates = new System.Runtime.CompilerServices.ConditionalWeakTable<object, ReplaceState>();

        void ReplaceButtonStep(object w, object vm)
        {
            if (vm != null) ReplaceStep(w, w as Noesis.DependencyObject, vm);
        }

        /// <summary>One Unit Reference view (a window, or the Reference tab of Unit Status): 'key' holds its state.</summary>
        void ReplaceStep(object key, Noesis.DependencyObject root, object vm)
        {
            var st = ReplaceStates.GetOrCreateValue(key);
            var el = st.Element == null ? null : st.Element.Target as Noesis.UIElement;
            if (el == null)
            {
                if (st.Done) return;
                el = ReplaceHolderOf(vm);
                if (el == null) return;
                st.Element = new WeakReference(el);
                st.Done = true;
            }
            if (GetB(vm, "IsTestMode"))
            {
                if (!st.Hidden) return;   // the game's own visibility
                st.Hidden = false;
                Noesis.BindingExpression x = null;
                try { x = Noesis.BindingOperations.GetBindingExpression(el, Noesis.UIElement.VisibilityProperty); } catch { }
                if (x != null) x.UpdateTarget(); else el.SetCurrentValue(Noesis.UIElement.VisibilityProperty, st.Before);
                return;
            }
            if (el.Visibility != Noesis.Visibility.Collapsed)
            {
                if (!st.Hidden) st.Before = el.Visibility;
                st.Hidden = true;
                el.SetCurrentValue(Noesis.UIElement.VisibilityProperty, Noesis.Visibility.Collapsed);
            }
        }

        /// <summary>The Replace button (Command bound to ...TestCommand; caught when it loaded) or, going up, the element holding only it and decoration.</summary>
        Noesis.UIElement ReplaceHolderOf(object vm)
        {
            VmParts parts;
            var btn = vm != null && PartsByVm.TryGetValue(vm, out parts) ? PartOf(parts.Replace) as Noesis.Button : null;
            if (btn == null) return null;
            Noesis.UIElement holder = btn;
            var chain = Describe(btn);
            for (var p = Noesis.VisualTreeHelper.GetParent(holder) as Noesis.Panel; p != null; p = Noesis.VisualTreeHelper.GetParent(holder) as Noesis.Panel)
            {
                bool onlyDecor = true;
                foreach (var c in p.Children)
                    if (!ReferenceEquals(c, holder) && !IsDecoration(c as Noesis.DependencyObject)) { onlyDecor = false; break; }
                if (!onlyDecor) break;
                holder = p;
                chain += " < " + Describe(p);
            }
            // a Border (no Panel) right above: the backing itself
            var bd = Noesis.VisualTreeHelper.GetParent(holder) as Noesis.Border;
            if (bd != null && ReferenceEquals(bd.Child, holder)) { holder = bd; chain += " < " + Describe(bd); }
            Trace("loadout list: Replace (hidden outside the Encyclopedia) is " + chain);
            return holder;
        }

        /// <summary>Shapes, empty borders / panels: no text, no controls inside.</summary>
        static bool IsDecoration(Noesis.DependencyObject e)
        {
            if (e == null) return true;
            if (e is Noesis.Control || e is Noesis.TextBlock || e is Noesis.ContentPresenter) return false;
            int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
            for (int k = 0; k < n; k++) if (!IsDecoration(Noesis.VisualTreeHelper.GetChild(e, k))) return false;
            return true;
        }

        static readonly string[] UnitCategories = { "Vessels", "Submarines", "Aircrafts", "Helicopters", "LandUnits", "Biologic" };
        Dictionary<string, IDictionary> _loadoutsByIni;   // unit ini name -> its Dictionary<string, Loadout> (ini order, display names)

        static void TreeEntriesPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null || !a._treeOpt.Value) return;
            try { a.OptimizeTree(__instance); }
            catch (Exception e) { Trace("OptimizeTree: " + e); }
        }

        void OptimizeTree(object browser)
        {
            if (!IsEncyclopediaBrowser(browser)) return;
            var cats = Get(browser, "MuseumEntries") as IList;
            if (cats == null) return;
            int sorted = 0, groups = 0;
            foreach (var cat in cats)
            {
                if (cat == null || Array.IndexOf(UnitCategories, TextOf(Get(cat, "CategoryKey"))) < 0) continue;
                var types = Get(cat, "MuseumEntries") as IList;
                if (types == null) continue;
                foreach (var type in types)
                {
                    var classes = IsFolder(type) ? Get(type, "MuseumEntries") as IList : null;
                    if (classes == null) continue;
                    if (Reorder(classes, SortedByDate(classes))) sorted++;
                    foreach (var c in classes)
                        if (IsFolder(c) && GetB(c, "IsGroupNode") && OptimizeGroup(c)) groups++;
                }
            }
            Trace("tree: " + sorted + " type(s) sorted by date, " + groups + " class group(s) with loadout variants arranged");
        }

        bool IsFolder(object e) { return e != null && e.GetType().Name == "MuseumEntry"; }

        int DateKey(object e)
        {
            var v = Get(e, "ServiceEntryDate");
            int d = v is int ? (int)v : 0;
            return d <= 0 ? int.MaxValue : d;
        }

        List<object> SortedByDate(IList list)
        {
            var order = new List<object>();
            foreach (var o in list) order.Add(o);
            StableSort(order, (a, b) =>
            {
                int r = DateKey(a).CompareTo(DateKey(b));
                return r != 0 ? r : string.Compare(TextOf(Get(a, "DisplayName")), TextOf(Get(b, "DisplayName")), StringComparison.OrdinalIgnoreCase);
            });
            return order;
        }

        static void StableSort<T>(List<T> list, Comparison<T> cmp)
        {
            var idx = new Dictionary<T, int>();
            for (int i = 0; i < list.Count; i++) idx[list[i]] = i;
            list.Sort((a, b) => { int r = cmp(a, b); return r != 0 ? r : idx[a].CompareTo(idx[b]); });
        }

        /// <summary>Puts the collection (an ObservableCollection of the copy) into the given order, dropping items not in it; true when it changed.</summary>
        static bool Reorder(IList list, List<object> order)
        {
            bool changed = false;
            for (int k = list.Count - 1; k >= 0; k--)
                if (!order.Contains(list[k])) { list.RemoveAt(k); changed = true; }
            for (int i = 0; i < order.Count; i++)
            {
                int at = -1;
                for (int k = i; k < list.Count; k++) if (ReferenceEquals(list[k], order[i])) { at = k; break; }
                if (at < 0 || at == i) continue;
                list.RemoveAt(at);
                list.Insert(i, order[i]);
                changed = true;
            }
            return changed;
        }

        class TreeUnit
        {
            public string Ini;
            public object Base;                              // the unit's own entry (= Default), null when dropped / absent
            public List<object> Loadouts = new List<object>();
            public IDictionary LoadoutDefs;                  // name -> Loadout, ini order
            public string Qualifier;
            public int Date;
        }

        /// <summary>A class group (IsGroupNode): arranges its units and their loadout units; true when it holds loadout units.</summary>
        bool OptimizeGroup(object group)
        {
            var kids = Get(group, "MuseumEntries") as IList;
            if (kids == null || kids.Count == 0) return false;
            bool any = false;
            foreach (var k in kids) if (k != null && GetB(k, "IsLoadoutUnit")) { any = true; break; }
            if (!any) return false;

            string key = TextOf(Get(group, "DisplayName")) ?? "";
            var units = new List<TreeUnit>();
            var byIni = new Dictionary<string, TreeUnit>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in kids)
            {
                if (k == null) continue;
                var ini = TextOf(Get(k, "IniRef")) ?? "";
                TreeUnit u;
                if (ini.Length == 0 || !byIni.TryGetValue(ini, out u))
                {
                    u = new TreeUnit { Ini = ini, LoadoutDefs = LoadoutsOf(ini) };
                    units.Add(u);
                    if (ini.Length > 0) byIni[ini] = u;
                }
                if (GetB(k, "IsLoadoutUnit")) u.Loadouts.Add(k); else if (u.Base == null) u.Base = k; else { u = new TreeUnit { Ini = ini, Base = k }; units.Add(u); }
            }

            foreach (var u in units)
            {
                // a unit without a Default loadout: only its loadout units
                if (u.Base != null && u.Loadouts.Count > 0 && u.LoadoutDefs != null && !u.LoadoutDefs.Contains("Default")) u.Base = null;
                string name = u.Base != null ? TextOf(Get(u.Base, "DisplayName")) : BaseName(u, u.Loadouts.Count > 0 ? u.Loadouts[0] : null);
                u.Qualifier = ClassQualifier(name ?? "", key);
                if (u.Qualifier.Length == 0) u.Qualifier = key;
                int d = int.MaxValue;
                if (u.Base != null) d = DateKey(u.Base);
                else foreach (var l in u.Loadouts) d = Math.Min(d, DateKey(l));
                u.Date = d;
                var lo = u.Loadouts;
                var ord = new Dictionary<object, int>();
                foreach (var l in lo) ord[l] = LoadoutRank(u, TextOf(Get(l, "PredefinedLoadoutVariant")));
                StableSort(lo, (a, b) => { int r = DateKey(a).CompareTo(DateKey(b)); return r != 0 ? r : ord[a].CompareTo(ord[b]); });
            }
            units.RemoveAll(u => u.Base == null && u.Loadouts.Count == 0);
            StableSort(units, (a, b) => { int r = a.Date.CompareTo(b.Date); return r != 0 ? r : string.Compare(a.Qualifier, b.Qualifier, StringComparison.OrdinalIgnoreCase); });

            var order = new List<object>();
            foreach (var u in units)
            {
                int variants = u.Loadouts.Count + (u.Base != null ? 1 : 0);
                // the base unit (Default) goes among its loadout units by date: after Early of the same year, before the others
                var seq = new List<object>(u.Loadouts);
                if (u.Base != null)
                {
                    int at = 0;
                    int bd = DateKey(u.Base);
                    while (at < seq.Count && (DateKey(seq[at]) < bd || (DateKey(seq[at]) == bd && IsEarly(seq[at])))) at++;
                    seq.Insert(at, u.Base);
                }
                foreach (var e in seq)
                {
                    if (u.Loadouts.Count > 0 || variants > 1)
                    {
                        string label = u.Qualifier;
                        if (variants > 1 && GetB(e, "IsLoadoutUnit")) label += " [" + LoadoutDisplayName(u.LoadoutDefs, TextOf(Get(e, "PredefinedLoadoutVariant"))) + "]";
                        SetV(e, "TreeLabelOverride", label);
                    }
                    order.Add(e);
                }
            }
            Reorder(kids, order);
            return true;
        }

        bool IsEarly(object e) { return string.Equals(TextOf(Get(e, "PredefinedLoadoutVariant")), "Early", StringComparison.OrdinalIgnoreCase); }

        /// <summary>Order of a loadout among a unit's loadout units of the same year: Early first, then the ini's order.</summary>
        int LoadoutRank(TreeUnit u, string name)
        {
            if (string.Equals(name, "Early", StringComparison.OrdinalIgnoreCase)) return -1;
            int i = 0;
            if (u.LoadoutDefs != null)
                foreach (DictionaryEntry kv in u.LoadoutDefs) { if (string.Equals(kv.Key as string, name, StringComparison.OrdinalIgnoreCase)) return i; i++; }
            return int.MaxValue;
        }

        /// <summary>The unit's name from one of its loadout units: its DisplayName without the game's " [loadout]" suffix.</summary>
        string BaseName(TreeUnit u, object loadoutUnit)
        {
            var dn = TextOf(Get(loadoutUnit, "DisplayName")) ?? "";
            var suffix = " [" + LoadoutDisplayName(u.LoadoutDefs, TextOf(Get(loadoutUnit, "PredefinedLoadoutVariant"))) + "]";
            return dn.EndsWith(suffix, StringComparison.Ordinal) ? dn.Substring(0, dn.Length - suffix.Length) : dn;
        }

        /// <summary>As UnitTreeGrouping.GetClassQualifier: the text after the group key, outer ( ) / [ ] stripped.</summary>
        static string ClassQualifier(string name, string key)
        {
            if (name.Length <= key.Length) return "";
            if (!name.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return name;
            var t = name.Substring(key.Length).Trim();
            while (t.Length >= 2 && ((t[0] == '(' && t.IndexOf(')') == t.Length - 1) || (t[0] == '[' && t.IndexOf(']') == t.Length - 1)))
                t = t.Substring(1, t.Length - 2).Trim();
            return t;
        }

        /// <summary>The loadout's display name (Loadout.DisplayName, else its name), as the game shows it for loadout units.</summary>
        string LoadoutDisplayName(IDictionary defs, string loadout)
        {
            loadout = loadout ?? "";
            var l = defs != null && defs.Contains(loadout) ? defs[loadout] : null;
            var dn = l == null ? null : TextOf(Get(l, "DisplayName"));
            return string.IsNullOrWhiteSpace(dn) ? loadout : dn.Trim();
        }

        /// <summary>The unit's loadouts (Dictionary&lt;string, Loadout&gt; of its base SimpleObjectParameters), from UnitMetadataProvider.UnitData.</summary>
        IDictionary LoadoutsOf(string iniRef)
        {
            try
            {
                if (_loadoutsByIni == null || _loadoutsByIni.Count == 0)
                {
                    _loadoutsByIni = new Dictionary<string, IDictionary>(StringComparer.OrdinalIgnoreCase);
                    var asm = GameAssembly();
                    var ump = asm == null ? null : asm.GetType("SeaPower.UnitMetadataProvider");
                    const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
                    var exists = ump == null ? null : ump.GetMethod("InstanceExists", sf, null, new[] { typeof(bool) }, null);
                    if (exists != null && (bool)exists.Invoke(null, new object[] { false }))
                    {
                        var data = Get(ump.GetProperty("Instance", sf).GetValue(null, null), "UnitData") as IDictionary;
                        if (data != null)
                            foreach (DictionaryEntry kv in data)
                            {
                                var sop = kv.Value == null ? null : kv.Value.GetType().GetField("Item2").GetValue(kv.Value);
                                if (sop == null || GetB(sop, "IsLoadoutUnit")) continue;
                                var name = sop.GetType().GetField("_objectIniName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                var lo = sop.GetType().GetField("_loadouts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                var n = name == null ? null : name.GetValue(sop) as string;
                                var d = lo == null ? null : lo.GetValue(sop) as IDictionary;
                                if (!string.IsNullOrEmpty(n) && d != null && !_loadoutsByIni.ContainsKey(n)) _loadoutsByIni[n] = d;
                            }
                    }
                }
                IDictionary r;
                return !string.IsNullOrEmpty(iniRef) && _loadoutsByIni.TryGetValue(iniRef, out r) ? r : null;
            }
            catch (Exception e) { Trace("LoadoutsOf: " + e.Message); return null; }
        }
    }
}
