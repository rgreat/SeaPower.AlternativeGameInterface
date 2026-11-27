using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Unit Reference tree, 1.6.0 (the encyclopedia browser only):
    ///  - EarliestVariantOnClick: a click on a class node selects its earliest variant (the game: the one in service in the
    ///    mission year, else the latest);
    ///  - KeepOpenedUnit: the unit the window was opened for stays in the tree (with its class) whatever the filters say;
    ///  - HideEmptyCountries: the country list holds only countries with something left for the current search / date / armed filters
    ///    (the selected country stays);
    ///  - SearchInDescriptionAndId: with "Search by name only" off the search also looks into the unit's description and its ini id.
    /// The filters work as the game's own: FilteredMuseumEntries collections of the browser's copy of the tree, after the game's
    /// filters (postfix of UpdateFilteredMuseumEntries, see ArmedFilter.cs), the units' data untouched.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _earliest, _keepOpened, _hideCountries, _searchDesc;
        ConfigEntry<string> _labelName, _titleName;
        MethodInfo _matchObject, _matchWeapon, _matchSensor, _missionYear;

        sealed class PinState { public object Entry; }
        static readonly ConditionalWeakTable<object, PinState> Pins = new ConditionalWeakTable<object, PinState>();
        sealed class NationMaster { public List<object> All = new List<object>(); }
        static readonly ConditionalWeakTable<object, string> NationBase = new ConditionalWeakTable<object, string>();      // a nation -> its name as the game gave it
        int _collectTotal;
        static readonly ConditionalWeakTable<object, NationMaster> NationMasters = new ConditionalWeakTable<object, NationMaster>();
        [ThreadStatic] static bool _openingForUnit;
        static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        void BindTreeSearchConfig()
        {
            _titleName = Config.Bind("UnitStatus", "WindowTitleName", "FullName",
                new ConfigDescription("Unit Status window, the title of the window (\"Unit Status <name>\"). ShortName (the game's), FullName, ShortClass, FullClass, TypeClass.",
                    new AcceptableValueList<string>("ShortName", "FullName", "ShortClass", "FullClass", "TypeClass")));
            _labelName = Config.Bind("UnitStatus", "UnitLabelName", "TypeClass",
                new ConfigDescription("Unit Status window, the unit's name at the right of the tabs. ShortName (the game's), FullName, ShortClass, FullClass, TypeClass.",
                    new AcceptableValueList<string>("ShortName", "FullName", "ShortClass", "FullClass", "TypeClass")));
            _labelName.SettingChanged += (o, e) => { try { SyncFullNames(); } catch { } };   // the open Unit Status windows follow the choice at once
            _earliest = Config.Bind("UnitReference", "EarliestVariantOnClick", true,
                "Unit Reference tree: a click on a class (a node with several variants) selects its earliest variant. The game selects the one in service in " +
                "the mission year, otherwise the latest.");
            _keepOpened = Config.Bind("UnitReference", "KeepOpenedUnit", true,
                "Unit Reference: the unit the window was opened for stays in the tree, with its class, even when the country / date / search / " +
                "Armed only filters would hide it.");
            _hideCountries = Config.Bind("UnitReference", "HideEmptyCountries", true,
                "Unit Reference: the country list shows only the countries that have units for the current search text, 'Filter by date' and " +
                "'Armed only' (the selected country is always shown).");
            _searchDesc = Config.Bind("UnitReference", "SearchInDescriptionAndId", true,
                "Unit Reference: with 'Search by name only' off the search also finds units by their description and by their ini id (<Id>.ini).");
        }

        void HookTreeSearch(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var utb = asm.GetType("SeapowerUI.ViewModels.UnitTreeBrowserViewModel");
            if (utb == null) { Trace("tree search: UnitTreeBrowserViewModel not found"); return; }
            var best = utb.GetMethod("SelectBestGroupEntry", inst);
            if (best != null) { TryPatch(harmony, best, null, "BestGroupPostfix", null); Trace("patched UnitTreeBrowserViewModel.SelectBestGroupEntry"); }
            else Trace("UnitTreeBrowserViewModel.SelectBestGroupEntry not found: EarliestVariantOnClick not available");

            _matchObject = utb.GetMethod("MatchesTextSearch", inst);
            _matchWeapon = utb.GetMethod("MatchesWeaponTextSearch", inst);
            _matchSensor = utb.GetMethod("MatchesSensorTextSearch", inst);
            _missionYear = utb.GetMethod("GetCurrentMissionYear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (_matchObject != null) { TryPatch(harmony, _matchObject, null, "MatchObjectPostfix", null); Trace("patched UnitTreeBrowserViewModel.MatchesTextSearch"); }
            else Trace("UnitTreeBrowserViewModel.MatchesTextSearch not found: SearchInDescriptionAndId not available");

            var setup = utb.GetMethod("SetupNations", inst);
            if (setup != null) TryPatch(harmony, setup, null, "SetupNationsPostfix", null);
            else Trace("UnitTreeBrowserViewModel.SetupNations not found: HideEmptyCountries not available");

            var reveal = utb.GetMethod("RevealEntry", inst);
            if (reveal != null) TryPatch(harmony, reveal, null, "RevealPostfix", null);
            else Trace("UnitTreeBrowserViewModel.RevealEntry not found: KeepOpenedUnit not available");
            var oivm = asm.GetType("SeapowerUI.ViewModels.ObjectInformationViewModel");
            if (oivm != null)
                foreach (var c in oivm.GetConstructors(inst))
                {
                    var ps = c.GetParameters();
                    if (ps.Length >= 2 && ps[1].Name == "initialIniRef")
                    {
                        TryPatch(harmony, c, "OpenForUnitPrefix", null, "OpenForUnitFinalizer");
                        Trace("patched ObjectInformationViewModel constructor (opened for a unit)");
                    }
                }
        }

        static void TryPatch(Harmony h, MethodBase m, string pre, string post, string fin)
        {
            try { h.Patch(m, prefix: pre == null ? null : Hm(pre), postfix: post == null ? null : Hm(post), finalizer: fin == null ? null : Hm(fin)); }
            catch (Exception e) { Trace("patch of " + m.Name + " failed: " + e.Message); }
        }

        // ---------------------------------------------------------------- earliest variant

        static void BestGroupPostfix(object __instance, object group, ref object __result)
        {
            var a = Active;
            if ((object)a == null || !a._earliest.Value || group == null) return;
            try
            {
                if (!a.IsEncyclopediaBrowser(__instance)) return;
                var kids = a.Get(group, "MuseumEntries") as IList;
                if (kids == null) return;
                object pick = null; int pickDate = int.MaxValue;
                foreach (var k in kids)
                {
                    if (k == null) continue;
                    var d = a.Get(k, "ServiceEntryDate");
                    int date = d is int ? (int)d : 0;
                    if (date > 0 && date < pickDate) { pick = k; pickDate = date; }
                }
                if (pick != null) __result = pick;
            }
            catch (Exception e) { Trace("EarliestVariant: " + e.Message); }
        }

        // ---------------------------------------------------------------- search in description and id

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, string[]> SearchTexts = new System.Runtime.CompilerServices.ConditionalWeakTable<object, string[]>();

        static void MatchObjectPostfix(object entry, string searchLower, bool nameOnly, ref bool __result)
        {
            var a = Active;
            if (__result || nameOnly || (object)a == null || !a._searchDesc.Value || entry == null) return;
            try
            {
                // the lower-cased id and plain description of an entry are made once (a search asks every unit on each key press)
                string[] t;
                if (!SearchTexts.TryGetValue(entry, out t))
                {
                    var id = a.Get(entry, "IniRef") as string;
                    var desc = a.Get(entry, "Description") as string;
                    t = new[] { id == null ? "" : id.ToLowerInvariant(), desc == null ? "" : Tags.Replace(desc, " ").ToLowerInvariant() };
                    SearchTexts.Add(entry, t);
                }
                if (t[0].Contains(searchLower) || t[1].Contains(searchLower)) __result = true;
            }
            catch (Exception e) { Trace("SearchInDescriptionAndId: " + e.Message); }
        }

        // ---------------------------------------------------------------- the opened unit stays in the tree

        static void OpenForUnitPrefix(string initialIniRef) { _openingForUnit = initialIniRef != null; }
        static void OpenForUnitFinalizer() { _openingForUnit = false; }

        static void RevealPostfix(object __instance, object __result)
        {
            var a = Active;
            if (!_openingForUnit || __result == null || (object)a == null || !a._keepOpened.Value) return;
            try
            {
                if (!a.IsEncyclopediaBrowser(__instance)) return;
                Pins.Remove(__instance);
                Pins.Add(__instance, new PinState { Entry = __result });
                a.InjectPinned(__instance);
            }
            catch (Exception e) { Trace("KeepOpenedUnit: " + e.Message); }
        }

        /// <summary>The path (root category ... entry) to 'target' in the full tree.</summary>
        static bool PathTo(IList level, object target, List<object> path)
        {
            if (level == null) return false;
            foreach (var e in level)
            {
                if (e == null) continue;
                path.Add(e);
                if (ReferenceEquals(e, target)) return true;
                if (PathTo(AlternativeGameInterfacePlugin.Active.Get(e, "MuseumEntries") as IList, target, path)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        /// <summary>Puts 'item' into the filtered 'list' at the place it has among 'all'; false when it was there already.</summary>
        static bool EnsureIn(IList list, IList all, object item)
        {
            foreach (var x in list) if (ReferenceEquals(x, item)) return false;
            int idx = all == null ? int.MaxValue : IndexOfRef(all, item);
            int at = list.Count;
            for (int i = 0; i < list.Count; i++)
                if (all != null && IndexOfRef(all, list[i]) > idx) { at = i; break; }
            list.Insert(at, item);
            return true;
        }

        static int IndexOfRef(IList l, object o)
        {
            for (int i = 0; i < l.Count; i++) if (ReferenceEquals(l[i], o)) return i;
            return int.MaxValue;
        }

        void InjectPinned(object browser)
        {
            PinState ps;
            if (!Pins.TryGetValue(browser, out ps) || ps.Entry == null) return;
            var full = Get(browser, "MuseumEntries") as IList;
            var root = Get(browser, "FilteredMuseumEntries") as IList;
            if (full == null || root == null) return;
            var path = new List<object>();
            if (!PathTo(full, ps.Entry, path) || path.Count == 0) return;
            IList filtered = root, all = full;
            bool stale = false;   // below a node that came back the game's filtered lists are left over from before
            for (int i = 0; i < path.Count; i++)
            {
                var node = path[i];
                bool inserted = EnsureIn(filtered, all, node);
                if (i == path.Count - 1) break;
                var nodeAll = Get(node, "MuseumEntries") as IList;
                if (inserted || stale)
                {
                    // the node shows only the way to the unit
                    var only = Activator.CreateInstance(nodeAll.GetType()) as IList;
                    only.Add(path[i + 1]);
                    SetV(node, "FilteredMuseumEntries", only);
                    filtered = only;
                    stale = true;
                }
                else
                {
                    var kids = Get(node, "FilteredMuseumEntries") as IList;
                    if (kids == null || ReferenceEquals(kids, nodeAll)) return;   // not filtered here: everything shows
                    filtered = kids;
                }
                all = nodeAll;
            }
            Verbose("opened unit kept in the tree");
        }

        static bool ContainsRef(IList l, object o) { foreach (var x in l) if (ReferenceEquals(x, o)) return true; return false; }

        // ---------------------------------------------------------------- countries with something to show

        static void SetupNationsPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null) return;
            try
            {
                var list = a.Get(__instance, "Nations") as IList;
                if (list == null) return;
                var m = NationMasters.GetOrCreateValue(__instance);
                m.All.Clear();
                foreach (var n in list)
                {
                    m.All.Add(n);
                    string bn;
                    if (!NationBase.TryGetValue(n, out bn)) NationBase.Add(n, (n == null ? null : a.Get(n, "NationName") as string) ?? "");
                }
                // the game's own order, whatever order the list was in when it was copied (the game's first filter pass may already have sorted it by count): "All" first, then by name as the game does
                if (m.All.Count > 2)
                {
                    var idx = new Dictionary<object, int>();
                    for (int k = 0; k < m.All.Count; k++) idx[m.All[k]] = k;
                    var rest = m.All.GetRange(1, m.All.Count - 1);
                    rest.Sort((x, y) => { int c = string.Compare(a.NationPlainName(x), a.NationPlainName(y), StringComparison.CurrentCulture); return c != 0 ? c : idx[x].CompareTo(idx[y]); });
                    m.All.RemoveRange(1, m.All.Count - 1);
                    m.All.AddRange(rest);
                }
            }
            catch (Exception e) { Trace("SetupNations: " + e.Message); }
        }

        bool _natSorted;
        Func<object, bool> _collectSrc;   // the source filter while the nations are collected (SyncNations)

        void SyncNations(object browser)
        {
            NationMaster m;
            var nations = Get(browser, "Nations") as IList;
            if (nations == null || !NationMasters.TryGetValue(browser, out m) || m.All.Count == 0) return;
            if (!ContainsRef(nations, m.All[0])) return;   // the game has just made the list again (SetupNations): our copy is of the old one until its postfix
            bool hide = _hideCountries.Value, byCount = _improvedUI.Value && _natCount.Value;
            string text = (Get(browser, "SearchText") as string ?? "").Trim();
            bool byDate = GetB(browser, "FilterByDate"), armed = _improvedUI.Value && _armOnly.Value;
            bool anyFilter = false;
            Dictionary<string, int> present = null;   // nation id -> units found (0: only a folder / class of it)
            _collectSrc = _improvedUI.Value ? SourcePredicate(browser) : null;
            anyFilter = text.Length > 0 || byDate || armed || _collectSrc != null;
            if (text.Length > 0 || byDate || armed || _collectSrc != null || byCount || _improvedUI.Value)
            {
                present = new Dictionary<string, int>();
                _collectTotal = 0; _seenTypes.Clear();
                int year = 0;
                if (byDate && _missionYear != null) { var y = _missionYear.Invoke(null, null); if (y is int) year = (int)y; }
                bool nameOnly = GetB(browser, "SearchByNameOnly");
                Collect(browser, CurrentTab(browser), text.ToLowerInvariant(), nameOnly, byDate, year, armed, present, 0);
            }
            var sel = Get(browser, "SelectedNation");
            // the wanted nations in the wanted order: "All" first, then the game's order (alphabetical) or the most units first (ties alphabetical)
            var want = new List<object>();
            for (int k = 0; k < m.All.Count; k++)
            {
                var n = m.All[k];
                string id = Get(n, "NationId") as string;
                if (k == 0 || !hide || present == null || !anyFilter || ReferenceEquals(n, sel) || (id != null && present.ContainsKey(id))) want.Add(n);
            }
            if (byCount && present != null && want.Count > 2)
            {
                var rest = want.GetRange(1, want.Count - 1);
                var order = new Dictionary<object, int>();
                for (int k = 0; k < rest.Count; k++) order[rest[k]] = k;   // the game's order breaks the ties
                rest.Sort((a, c) =>
                {
                    int ca = CountOf(present, a), cc = CountOf(present, c);
                    return ca != cc ? cc.CompareTo(ca) : order[a].CompareTo(order[c]);
                });
                want.RemoveRange(1, want.Count - 1);
                want.AddRange(rest);
            }
            // the counts for the drop-down of the mod (CountryMenu.cs); the nations keep the game's plain names
            var ni = NatInfos.GetOrCreateValue(browser);
            ni.Count.Clear();
            if (_improvedUI.Value && present != null)
                foreach (var n in m.All) ni.Count[n] = ReferenceEquals(n, m.All[0]) ? _collectTotal : CountOf(present, n);
            foreach (var n in m.All)
            {
                var plain = NationPlainName(n);
                if (!string.Equals(Get(n, "NationName") as string, plain)) SetV(n, "NationName", plain);
            }
            _natSorted = byCount && present != null && want.Count > 2;   // when it is switched off the next pass puts the game's order back
            ApplyNationOrder(nations, want, sel);
            NatRefreshFor(browser);
        }

        /// <summary>The units of the tab (category) that is open: the countries are counted over these only.</summary>
        IList CurrentTab(object browser)
        {
            var all = Get(browser, "MuseumEntries") as IList;
            var sel = Get(browser, "SelectedCategory");
            if (all == null || sel == null) return all;
            var name = TextOf(Get(sel, "DisplayName"));
            foreach (var c in all)
                if (ReferenceEquals(c, sel) || (c != null && name != null && TextOf(Get(c, "DisplayName")) == name))
                {
                    var kids = Get(c, "MuseumEntries") as IList;
                    if (kids != null) return kids;
                }
            return all;
        }

        /// <summary>The tab was changed: the counts, the order and the empty countries follow it.</summary>
        static void TabSetPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null || _nationBusy != 0 || _nationRestoring) return;
            try
            {
                if (!a.IsEncyclopediaBrowser(__instance)) return;
                if (a._browserInitializing != null && a._browserInitializing.GetValue(__instance) is bool && (bool)a._browserInitializing.GetValue(__instance)) return;
                if (a._hideCountries.Value || a._improvedUI.Value || a._natSorted) a.SyncNations(__instance);
            }
            catch (Exception e) { Trace("TabSetPostfix: " + e.Message); }
        }

        /// <summary>The nation's name as the game gave it (without the count the mod adds in brackets).</summary>
        string NationPlainName(object nation)
        {
            string bn;
            return nation != null && NationBase.TryGetValue(nation, out bn) ? bn : (Get(nation, "NationName") as string ?? "");
        }

        int CountOf(Dictionary<string, int> present, object nation)
        {
            int n; var id = Get(nation, "NationId") as string;
            return id != null && present.TryGetValue(id, out n) ? n : 0;
        }

        /// <summary>The list shows exactly "want", in that order. The selected nation is never taken out of the list (the combo box would clear its selection): the others are put around it.</summary>
        static void ApplyNationOrder(IList nations, List<object> want, object sel)
        {
            bool same = nations.Count == want.Count;
            for (int k = 0; same && k < want.Count; k++) if (!ReferenceEquals(nations[k], want[k])) same = false;
            if (same) return;
            int selAt = -1;
            for (int k = 0; k < want.Count; k++) if (sel != null && ReferenceEquals(want[k], sel)) { selAt = k; break; }
            for (int k = nations.Count - 1; k >= 0; k--) if (selAt < 0 || !ReferenceEquals(nations[k], sel)) nations.RemoveAt(k);
            for (int k = 0; k < want.Count; k++)
            {
                if (k == selAt) continue;
                if (selAt >= 0 && k > selAt) nations.Add(want[k]); else nations.Insert(Math.Min(k, nations.Count), want[k]);
            }
        }

        void Collect(object browser, IList level, string text, bool nameOnly, bool byDate, int year, bool armed, Dictionary<string, int> present, int depth)
        {
            if (level == null || depth > 12) return;
            foreach (var e in level)
            {
                if (e == null) continue;
                var kids = Get(e, "MuseumEntries") as IList;
                if (kids != null && kids.Count > 0) Collect(browser, kids, text, nameOnly, byDate, year, armed, present, depth + 1);
                if (e.GetType().Name == "MuseumEntry") continue;   // a folder
                if (byDate)
                {
                    var s = Get(e, "ServiceEntryDate"); var x = Get(e, "ServiceExitDate");
                    int se = s is int ? (int)s : 0, sx = x is int ? (int)x : 0;
                    if (!(se <= 0 || (se <= year && (sx <= 0 || sx >= year)))) continue;
                }
                if (text.Length > 0 && !MatchesText(browser, e, text, nameOnly)) continue;
                if (armed && !IsArmed(e)) continue;
                if (_collectSrc != null && !_collectSrc(e)) continue;
                // the count is of unit TYPES (an ini): the variants, squadrons and loadouts of one type are one; weapons and sensors count each
                string ini = IsUnitEntry(e) ? Get(e, "IniRef") as string : null;
                bool leaf = kids == null || kids.Count == 0;
                if (string.IsNullOrEmpty(ini)) { NationsOf(e, present, leaf, null); if (leaf) _collectTotal++; }
                else { NationsOf(e, present, true, ini.ToLowerInvariant()); if (_seenTypes.Add("|" + ini.ToLowerInvariant())) _collectTotal++; }
            }
        }

        bool MatchesText(object browser, object e, string text, bool nameOnly)
        {
            string tn = e.GetType().Name;
            bool unit = false;
            for (var b = e.GetType(); b != null; b = b.BaseType) if (b.Name == "MuseumObjectEntry") { unit = true; break; }
            try
            {
                if (unit) return _matchObject != null && (bool)_matchObject.Invoke(browser, new object[] { e, text, nameOnly });
                if (tn == "MuseumWeaponEntry") return _matchWeapon != null && (bool)_matchWeapon.Invoke(browser, new object[] { e, text });
                if (tn == "MuseumSensorEntry") return _matchSensor != null && (bool)_matchSensor.Invoke(browser, new object[] { e, text });
            }
            catch { return true; }
            foreach (var f in new[] { "DisplayName", "LongName" })
            {
                var v = Get(e, f) as string;
                if (v != null && v.ToLowerInvariant().Contains(text)) return true;
            }
            return false;
        }

        readonly HashSet<string> _seenTypes = new HashSet<string>();   // "nation|ini" (and "|ini" for the total) already counted in this pass

        void NationsOf(object e, Dictionary<string, int> into, bool leaf, string type)
        {
            var one = Get(e, "Nation") as string;
            if (!string.IsNullOrEmpty(one)) Tally(into, one, leaf && Fresh(one, type));
            var many = Get(e, "Nations") as IEnumerable;
            if (many != null && !(many is string)) foreach (var n in many) { var s = n as string; if (!string.IsNullOrEmpty(s)) Tally(into, s, leaf && Fresh(s, type)); }
            var ini = Get(e, "IniRef") as string;
            if (ini != null && ini.StartsWith("civ_", StringComparison.OrdinalIgnoreCase)) Tally(into, "Civilian", leaf && Fresh("Civilian", type));
        }

        bool Fresh(string nation, string type) { return type == null || _seenTypes.Add(nation + "|" + type); }

        static void Tally(Dictionary<string, int> into, string nation, bool leaf)
        {
            int n; into.TryGetValue(nation, out n);
            into[nation] = leaf ? n + 1 : n;   // a class with variants counts through its variants only
        }

        // ---------------------------------------------------------------- after the game's filters (called from ArmedFilter.cs' postfix)

        void AfterFilters(object browser)
        {
            if (_browserInitializing != null && _browserInitializing.GetValue(browser) is bool && (bool)_browserInitializing.GetValue(browser)) return;
            if (!IsEncyclopediaBrowser(browser)) return;
            if (_keepOpened.Value) InjectPinned(browser);
            if (_hideCountries.Value || _improvedUI.Value || _natSorted) SyncNations(browser);
        }
    }
}
