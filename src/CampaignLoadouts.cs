using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// StrictCampaignLoadouts (1.6.0): in the campaign Task Force Builder the ship versions / loadouts offered for a ship are found by
    /// the encyclopedia tag shared by the units of a family (LinearCampaignTaskForceLoadoutsViewModel.GetFamilyVersions), so a ship
    /// could be swapped into any other unit with the same tag, even one the mission never offers. On: the list keeps only the units
    /// the mission's roster allows (the builder's mission restriction) or that the player already has (current task force) - plus the ship itself.
    /// The builder is captured in a postfix of LoadCampaignTaskForceData; no builder / no restriction (allow all) = the game's list.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _strictLoadouts;
        static object _builderVm;
        static FieldInfo _restrictionField;
        static MethodInfo _unitTypesGet, _ownedKeys;

        const string StrictKey = "SmartWindows_StrictLoadouts";
        static bool _strictStaged;   // the start screen's check box (a new campaign): written into the campaign when it is seeded

        /// <summary>The settings window's row for the option edits this proxy (a config file that is never saved): the value itself lives in the
        /// campaign's persistent data (saved with the campaign), so it is per campaign, default off.</summary>
        void BindCampaignLoadoutsConfig()
        {
            var proxy = new ConfigFile(System.IO.Path.Combine(BepInEx.Paths.CachePath, "AlternativeGameInterface.proxy.cfg"), false);
            proxy.SaveOnConfigSet = false;
            _strictLoadouts = proxy.Bind("Campaign", "StrictLoadouts", false,
                "Campaign Task Force Builder: ship versions and loadouts are offered only for ships the mission lists as available or that you already own " +
                "(the game also offers every ship sharing the encyclopedia tag). Saved with the campaign; chosen at the start of a new campaign.");
        }

        static Dictionary<string, string> CampaignData()
        {
            try
            {
                var a = Active;
                var asm = (object)a == null ? null : GameAssembly();
                var lc = asm == null ? null : asm.GetType("SeaPower.LinearCampaign");
                var sg = lc == null ? null : asm.GetType("SeaPower.Singleton`1");
                var inst = sg == null ? null : sg.MakeGenericType(lc).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                var c = inst == null ? null : inst.GetValue(null, null);
                return c == null ? null : a.Get(c, "PersistentData") as Dictionary<string, string>;
            }
            catch { return null; }
        }

        /// <summary>The current campaign's option (off when there is no campaign / no key).</summary>
        static bool StrictActive()
        {
            var d = CampaignData(); string v;
            return d != null && d.TryGetValue(StrictKey, out v) && string.Equals(v, "True", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The settings window opened from the Task Force Builder: its row shows this campaign's value.</summary>
        void LoadStrictToProxy() { _strictLoadouts.BoxedValue = StrictActive(); }

        /// <summary>Apply in the settings window: the value goes into the campaign's persistent data.</summary>
        void SaveStrictFromProxy()
        {
            var d = CampaignData();
            if (d == null) return;
            d[StrictKey] = _strictLoadouts.Value ? "True" : "False";
        }

        /// <summary>A new campaign is seeded: the start screen's choice is written into its persistent data.</summary>
        static void SeedPostfix(IDictionary<string, string> persistentData, bool __result)
        {
            if ((object)Active == null || !__result || persistentData == null) return;
            persistentData[StrictKey] = _strictStaged ? "True" : "False";
            _strictStaged = false;
        }

        void HookCampaignLoadouts(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var b = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceBuilderViewModel");
            var l = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceLoadoutsViewModel");
            var sm = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceSaveManager");
            var load = b == null ? null : b.GetMethod("LoadCampaignTaskForceData", all);
            var fam = l == null ? null : l.GetMethod("GetFamilyVersions", all);
            _restrictionField = b == null ? null : b.GetField("_missionDefaultRestriction", all);
            _ownedKeys = sm == null ? null : sm.GetMethod("ReadCurrentTaskForceUnitKeys", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (load == null || fam == null || _restrictionField == null)
            {
                Trace("Task Force Builder (LoadCampaignTaskForceData / GetFamilyVersions) not found: StrictLoadouts not available");
                return;
            }
            TryPatch(harmony, load, "BuilderLoadedPrefix", "BuilderLoadedPostfix", null);
            TryPatch(harmony, fam, null, "FamilyVersionsPostfix", null);
            var rules = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceDifficultyRules");
            var seed = rules == null ? null : rules.GetMethod("SeedPersistentDataForNewCampaign", all);
            if (seed != null) TryPatch(harmony, seed, null, "SeedPostfix", null); else Trace("SeedPersistentDataForNewCampaign not found: a new campaign gets no Strict loadouts choice");
            var merge = b.GetMethod("MergeRelatedClassGroups", all);
            if (merge != null) { TryPatch(harmony, merge, null, "MergedGroupsPostfix", null); Trace("patched MergeRelatedClassGroups (StrictLoadouts)"); }
            Trace("patched GetFamilyVersions (StrictLoadouts)");
        }

        // the builder is known before the lists are built (the first build of the groups happens inside LoadCampaignTaskForceData, before its postfix)
        static void BuilderLoadedPrefix(object __instance) { _builderVm = __instance; }
        static void BuilderLoadedPostfix(object __instance) { _builderVm = __instance; }

        /// <summary>The unit's loadout (a ship position of the builder is one unit with one fixed loadout) is one the campaign has not opened yet
        /// (a loadout the campaign hands out as a reward: "Level 2" in Task Force Grom; the game's own names, Early / Late ..., are never locked).</summary>
        static bool LoadoutLocked(AlternativeGameInterfacePlugin a, object v)
        {
            try
            {
                var fixedP = v.GetType().GetProperty("HasFixedLoadout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fixedP == null || !(bool)fixedP.GetValue(v, null)) return false;
                string sel = a.Get(v, "SelectedLoadoutVariant") as string;
                var opts = a.Get(v, "LoadoutOptions") as IEnumerable;
                if (string.IsNullOrEmpty(sel) || opts == null) return false;
                int n = 0, closed = 0;
                foreach (var o in opts)
                {
                    bool lk = a.GetB(o, "IsLocked");
                    if (string.Equals(a.Get(o, "Reference") as string, sel, StringComparison.OrdinalIgnoreCase)) return lk;
                    n++; if (lk) closed++;
                }
                // the position's loadout is not one of the unit's own (the game fell back to "Default" for a unit whose loadouts it did not know when it built
                // the list), and every loadout the unit has is still closed: the unit cannot be taken yet
                return n > 0 && closed == n;
            }
            catch { }
            return false;
        }

        /// <summary>Builder tab: the groups merged by tag keep only the variants of units the mission allows or the player owns, and not the ones whose
        /// loadout the campaign has not opened yet.</summary>
        static void MergedGroupsPostfix(ref object __result)
        {
            var a = Active;
            if ((object)a == null) return;
            bool strict = StrictActive();
            if (!strict || _builderVm == null) { Trace("strict loadouts: builder list not filtered (" + (!strict ? "the option is off in this campaign" : "builder not known yet") + ")"); return; }
            try
            {
                var groups = __result as IList;
                if (groups == null || groups.Count == 0) return;
                // the mission's roster restriction (null: none / unrestricted mode - only the loadout lock applies)
                object restr = null; MethodInfo allows = null; string why = null;
                var allowAllProp = _builderVm.GetType().GetProperty("AllowAllRosterUnits", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (allowAllProp != null && (bool)allowAllProp.GetValue(_builderVm, null)) why = "unrestricted mode";
                else
                {
                    restr = _restrictionField.GetValue(_builderVm);
                    if (restr == null) why = "no mission restriction";
                    else
                    {
                        var aa = restr.GetType().GetProperty("IsAllowAll", BindingFlags.Instance | BindingFlags.Public);
                        if (aa != null && (bool)aa.GetValue(restr, null)) { why = "the mission allows all units"; restr = null; }
                        else allows = restr.GetType().GetMethod("Allows", BindingFlags.Instance | BindingFlags.Public);
                    }
                }
                int removed = 0, locked = 0;
                for (int gi = groups.Count - 1; gi >= 0; gi--)
                {
                    var vars = a.Get(groups[gi], "Variants") as IList;
                    if (vars == null) continue;
                    for (int i = vars.Count - 1; i >= 0; i--)
                    {
                        var v = vars[i];
                        if (a.GetB(v, "IsCurrentTaskForceUnit")) continue;
                        if (LoadoutLocked(a, v)) { vars.RemoveAt(i); locked++; continue; }
                        if (allows == null || (bool)allows.Invoke(restr, new object[] { a.Get(v, "UnitType") as string, a.Get(v, "VariantReference") as string })) continue;
                        vars.RemoveAt(i); removed++;
                    }
                    if (vars.Count == 0) groups.RemoveAt(gi);
                }
                int left = 0; foreach (var g0 in groups) { var v0 = a.Get(g0, "Variants") as IList; if (v0 != null) left += v0.Count; }
                Trace("strict loadouts: builder list " + removed + " unit(s) the mission does not offer and " + locked + " with a loadout not opened yet left out, " + left + " kept in "
                      + groups.Count + " group(s)" + (why == null ? "" : " (mission restriction not applied: " + why + ")"));
            }
            catch (Exception e) { Trace("MergedGroupsPostfix: " + e.Message); }
        }

        // ---------------------------------------------------------------- check box on the campaign start screen

        static readonly ConditionalWeakTable<object, CheckBox> CampBoxes = new ConditionalWeakTable<object, CheckBox>();   // panel -> our check box

        /// <summary>The game's "Limit unit dates" check box of the campaign start screen has loaded: adds "Strict loadouts" under it (once per panel).</summary>
        void CampaignBoxFor(ToggleButton c)
        {
            var panel = VisualTreeHelper.GetParent(c) as Panel;
            if (panel == null) return;
            CheckBox have;
            if (CampBoxes.TryGetValue(panel, out have)) { have.IsChecked = _strictStaged; return; }
            var box = new CheckBox { Content = Loc.T("ui.strict"), IsChecked = _strictStaged };
            box.Margin = c.Margin;
            box.Style = c.Style;
            try { ToolTipService.SetToolTip(box, Loc.T("tip.strict")); } catch { }
            box.Click += (o, ev) => { _strictStaged = box.IsChecked == true; };
            panel.Children.Insert(panel.Children.IndexOf(c) + 1, box);
            CampBoxes.Add(panel, box);
            Trace("campaign start: Strict loadouts check box added");
        }

        /// <summary>__result: List&lt;(string Type, string Name, int Year)&gt; (ValueTuple) - filtered in place through IList.</summary>
        static void FamilyVersionsPostfix(object item, ref object __result)
        {
            var a = Active;
            if ((object)a == null || !StrictActive() || _builderVm == null) return;
            try
            {
                var list = __result as IList;
                if (list == null || list.Count == 0) return;
                var restr = _restrictionField.GetValue(_builderVm);
                if (restr == null) return;
                var rt = restr.GetType();
                var allowAll = rt.GetProperty("IsAllowAll", BindingFlags.Instance | BindingFlags.Public);
                if (allowAll != null && (bool)allowAll.GetValue(restr, null)) return;   // no restriction in the mission: nothing to enforce
                var types = rt.GetProperty("UnitTypes", BindingFlags.Instance | BindingFlags.Public);
                if (types == null) return;
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in (IEnumerable)types.GetValue(restr, null)) allowed.Add((string)t);
                var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // keys "Type:Variant"
                if (_ownedKeys != null)
                {
                    var keys = _ownedKeys.Invoke(null, null) as IEnumerable;
                    if (keys != null) foreach (var k in keys) { var s = k as string; if (s == null) continue; int i = s.IndexOf(':'); owned.Add(i < 0 ? s : s.Substring(0, i)); }
                }
                string self = a.Get(item, "UnitType") as string;
                if (list.Count == 0) return;
                var f0 = list[0].GetType().GetField("Item1");
                if (f0 == null) return;
                int fr = 0;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var ty = f0.GetValue(list[i]) as string;
                    if (ty == null) continue;
                    if (string.Equals(ty, self, StringComparison.OrdinalIgnoreCase) || allowed.Contains(ty) || owned.Contains(ty)) continue;
                    list.RemoveAt(i); fr++;
                }
                if (fr > 0) Trace("strict loadouts: " + fr + " version(s) left out of the loadouts list of " + self);
            }
            catch (Exception e) { Trace("FamilyVersionsPostfix: " + e.Message); }
        }
    }
}
