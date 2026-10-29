using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Campaign Task Force, Loadouts tab (1.7.0):
    /// SortLoadoutsByDate: the ship's own loadouts are sorted by their service year and, when the ship's own version is not newer than the other
    /// versions of its class, they are shown first (the game puts the own loadouts under the other versions' ones: 1970 after 1975..1984).
    /// FixVersionSwapPrice: the price of a hull swap (Prj.1135 -> 1135M) is the difference of the hull prices; the game takes the price of the
    /// ship you own from the campaign's roster (orbat) and the price of the new hull from the unit's ini only, so a roster price that differs from
    /// the ini made the swap look like a refund. On: the new hull is priced from the roster too (the same as buying it in the Builder).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _sortLoadouts, _fixSwapPrice;

        void BindLoadoutsOrderConfig()
        {
            _sortLoadouts = Config.Bind("Campaign", "SortLoadoutsByDate", true,
                "Campaign Task Force, Loadouts tab: the ship's own loadouts are sorted by date and shown before the newer versions of the class (the game shows them last).");
            _fixSwapPrice = Config.Bind("Campaign", "FixVersionSwapPrice", true,
                "Campaign Task Force, Loadouts tab: a hull swap to another version of the class is priced from the campaign's roster like buying it (the game uses the unit's ini price for the new hull, " +
                "and the roster price for the ship you have: a different roster price turned an upgrade into a refund).");
        }

        void HookLoadoutsOrder(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var l = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceLoadoutsViewModel");
            var s = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceStatusViewModel");
            var ro = l == null ? null : l.GetMethod("RebuildOptions", all);
            var ev = s == null ? null : s.GetMethod("EvaluateUnitVersionSwap", all);
            if (ro != null) { TryPatch(harmony, ro, null, "RebuildOptionsPostfix", null); Trace("patched RebuildOptions (SortLoadoutsByDate)"); } else Trace("RebuildOptions not found: SortLoadoutsByDate not available");
            if (ev != null) { TryPatch(harmony, ev, null, "SwapPricePostfix", null); Trace("patched EvaluateUnitVersionSwap (FixVersionSwapPrice)"); } else Trace("EvaluateUnitVersionSwap not found: FixVersionSwapPrice not available");
        }

        // ------------------------------------------------------------ order

        static void RebuildOptionsPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null || !a._sortLoadouts.Value) return;
            try
            {
                var opts = a.Get(__instance, "Options") as IList;
                if (opts == null || opts.Count < 2) return;
                var move = opts.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
                if (move == null) return;
                // stable insertion sort by the service year of the option (0 = unknown: last)
                for (int i = 1; i < opts.Count; i++)
                    for (int j = i; j > 0 && YearOf(a, opts[j]) < YearOf(a, opts[j - 1]); j--) move.Invoke(opts, new object[] { j, j - 1 });
            }
            catch (Exception e) { Trace("SortLoadoutsByDate: " + e.Message); }
        }

        static int YearOf(AlternativeGameInterfacePlugin a, object row)
        {
            var o = a.Get(row, "Option");
            var y = o == null ? null : a.Get(o, "ServiceEntryYear");
            int v = y is int ? (int)y : 0;
            return v <= 0 ? int.MaxValue : v;
        }

        sealed class OrderState { public float Next; }
        static readonly ConditionalWeakTable<object, OrderState> OrderStates = new ConditionalWeakTable<object, OrderState>();

        /// <summary>Builder window: the own loadouts block goes before the other versions' block when the ship's own version is not newer than them.</summary>
        void LoadoutOrderStep(object w, float now)
        {
            if (_sortLoadouts == null || !_sortLoadouts.Value) return;
            var vm = Get(w, "DataContext");
            if (vm == null || TypeName(vm) != "LinearCampaignTaskForceBuilderViewModel") return;
            var st = OrderStates.GetOrCreateValue(w);
            if (now < st.Next) return;
            st.Next = now + 0.4f;
            try
            {
                var lt = Get(vm, "LoadoutsTab");
                var opts = lt == null ? null : Get(lt, "Options");
                var vopts = lt == null ? null : Get(lt, "VersionOptions");
                var unit = lt == null ? null : Get(lt, "SelectedUnit");
                var item = unit == null ? null : Get(unit, "Item");
                if (opts == null || vopts == null || item == null || (vopts as ICollection) == null || ((ICollection)vopts).Count == 0) return;
                var root = (Noesis.DependencyObject)w;
                var own = FindFirst(root, e => e is Noesis.ItemsControl && ReferenceEquals(((Noesis.ItemsControl)e).ItemsSource, opts)) as Noesis.ItemsControl;
                var ver = FindFirst(root, e => e is Noesis.ItemsControl && ReferenceEquals(((Noesis.ItemsControl)e).ItemsSource, vopts)) as Noesis.ItemsControl;
                if (own == null || ver == null) return;
                var outer = Noesis.VisualTreeHelper.GetParent(own) as Noesis.Panel;
                if (outer == null) return;
                Noesis.DependencyObject block = ver;   // the block of the versions: the panel with the header, a child of the same panel
                while (block != null && !ReferenceEquals(Noesis.VisualTreeHelper.GetParent(block), outer)) block = Noesis.VisualTreeHelper.GetParent(block);
                var blockEl = block as Noesis.UIElement;
                if (blockEl == null) return;
                bool ownFirst = OwnVersionFirst(item);
                int io = outer.Children.IndexOf(own), ib = outer.Children.IndexOf(blockEl);
                if (io < 0 || ib < 0) return;
                if (ownFirst && io > ib) { outer.Children.RemoveAt(io); outer.Children.Insert(ib, own); Trace("loadouts: own versions shown first"); }
                else if (!ownFirst && io < ib) { outer.Children.RemoveAt(io); outer.Children.Insert(ib, own); }
            }
            catch (Exception e) { Trace("SortLoadoutsByDate (order): " + e.Message); }
        }

        /// <summary>The ship's own version is not newer than the oldest of the other versions of its class.</summary>
        bool OwnVersionFirst(object item)
        {
            string type = Get(item, "UnitType") as string, folder = Get(item, "UnitFolder") as string;
            int own = (int)UpStatic("LinearCampaignTaskForceUnitLookup", "GetAuthoredClassServiceYear", folder, type);
            if (own <= 0) return false;
            var l = GameAssembly().GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceLoadoutsViewModel");
            var fam = l.GetMethod("GetFamilyVersions", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var list = fam == null ? null : fam.Invoke(null, new[] { item }) as IEnumerable;
            int min = int.MaxValue;
            if (list != null)
                foreach (var v in list)
                {
                    var t = v.GetType();
                    var vt = t.GetField("Item1").GetValue(v) as string; int y = (int)t.GetField("Item3").GetValue(v);
                    if (string.Equals(vt, type, StringComparison.OrdinalIgnoreCase) || y <= 0) continue;
                    if (y < min) min = y;
                }
            return own <= min;
        }

        // ------------------------------------------------------------ price of the swap

        static readonly HashSet<string> SwapTraced = new HashSet<string>();

        /// <summary>The new hull is priced from the roster (the campaign's orbat) like the ship you have and like buying it in the Builder.</summary>
        static void SwapPricePostfix(object __instance, object item, string newUnitType, ref int costDelta, ref object replacement, ref string blockedReason, ref bool __result)
        {
            var a = Active;
            if ((object)a == null || !a._fixSwapPrice.Value || item == null || replacement == null) return;
            try
            {
                string folder = a.Get(item, "UnitFolder") as string, cat = a.Get(item, "Category") as string, variant = a.Get(replacement, "VariantReference") as string;
                var ini = UpStatic("LinearCampaignTaskForceModeRules", "OpenCampaignIni");
                int iniNew = (int)UpStatic("LinearCampaignTaskForceModeRules", "ReadUnitIniTaskForceCost", folder, newUnitType);
                int rosterNew = (int)UpStatic("LinearCampaignTaskForceModeRules", "ReadRosterTaskForceCost", ini, folder, newUnitType, cat, variant);
                int rosterOld = (int)UpStatic("LinearCampaignTaskForceModeRules", "ReadRosterTaskForceCost", ini, folder, a.Get(item, "UnitType") as string, cat, a.Get(item, "VariantReference") as string);
                int oldBase = (int)a.Get(item, "BaseCostPerUnit"), newBase = (int)a.Get(replacement, "BaseCostPerUnit");
                string key = a.Get(item, "UnitType") + ">" + newUnitType + ">" + rosterNew + ">" + oldBase;
                if (SwapTraced.Add(key))
                    Trace("version swap " + a.Get(item, "UnitType") + " -> " + newUnitType + ": ship you have: base price " + oldBase + " (roster " + rosterOld + "); new hull: game's price " + newBase + " (ini " + iniNew + "), roster " + rosterNew + "; price difference as the game shows " + costDelta);
                if (rosterNew == newBase) return;
                // a new replacement with the roster price (the same constructor call the game makes)
                var t = replacement.GetType();
                ConstructorInfo ctor = null;
                foreach (var c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) if (c.GetParameters().Length == 14) ctor = c;
                if (ctor == null) return;
                var np = new object[] { folder, newUnitType, cat, variant, rosterNew, a.Get(item, "IsFlagshipEligible"), a.Get(replacement, "SelectedLoadoutVariant"), a.Get(item, "QuantityPurchased"),
                                        string.Empty, a.Get(item, "InstanceId"), null, a.Get(item, "CrewSkill"), a.Get(item, "CrewSkillSurvivalCredits"), ini };
                var fresh = ctor.Invoke(np);
                var copy = t.GetMethod("CopyEmptyPurchaseStateFrom", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (copy != null) copy.Invoke(fresh, new[] { item });
                int delta = (int)a.Get(fresh, "CostPerUnit") - (int)a.Get(replacement, "CostPerUnit");
                costDelta += delta;
                replacement = fresh;
                // the points check of the game was made with the old price
                var pf = __instance.GetType().GetField("_availablePoints", BindingFlags.Instance | BindingFlags.NonPublic);
                int points = pf == null ? int.MaxValue : (int)pf.GetValue(__instance);
                if (costDelta > 0 && costDelta > points) { blockedReason = "Requires " + costDelta + " pts - " + points + " available"; __result = false; }
                else if (!__result && costDelta <= 0 && string.IsNullOrEmpty(blockedReason)) __result = true;
            }
            catch (Exception e) { Trace("FixVersionSwapPrice: " + e.Message); }
        }
    }
}
