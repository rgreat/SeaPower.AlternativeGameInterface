using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// DismissDamaged (1.7.0): in the campaign Task Force status the game refuses to dismiss a ship that has damage (CanDismiss = no damage).
    /// On: a damaged ship (not heavily damaged, not destroyed) can be dismissed when repair is possible for it at all (the mission has repair
    /// facilities, the damage is of a repairable kind and a saved damage state exists); the refund is the usual Dismiss refund minus the repair
    /// cost, never below 0. Menu text and confirmation show the reduced figure (they read the same refund).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _dismissDamaged;

        void BindDismissConfig()
        {
            _dismissDamaged = Config.Bind("Campaign", "DismissDamaged", true,
                "Campaign Task Force status: a damaged ship that can be repaired may be dismissed; the refund is reduced by the repair cost.");
        }

        static MethodInfo _repairEval;

        void HookCampaignDismiss(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var s = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceStatusViewModel");
            var it = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceBuilderRosterItem");
            if (s == null || it == null) { Trace("Task Force status not found: DismissDamaged not available"); return; }
            var can = s.GetMethod("CanDismiss", all, null, new[] { it }, null);
            var refund = s.GetMethod("CalculateDismissRefund", all, null, new[] { it, typeof(int) }, null);
            _repairEval = s.GetMethod("GetRepairEvaluation", all, null, new[] { it }, null);
            if (can == null || refund == null || _repairEval == null) { Trace("CanDismiss / CalculateDismissRefund / GetRepairEvaluation not found: DismissDamaged not available"); return; }
            TryPatch(harmony, can, null, "CanDismissPostfix", null);
            TryPatch(harmony, refund, null, "DismissRefundPostfix", null);
            Trace("patched CanDismiss, CalculateDismissRefund (DismissDamaged)");
        }

        /// <summary>The repair evaluation of a ship: damage, repairable at all, cost.</summary>
        bool RepairInfo(object status, object item, out bool repairable, out int cost)
        {
            repairable = false; cost = 0;
            var ev = _repairEval.Invoke(status, new[] { item });
            if (ev == null || !GetB(ev, "IsShipOrSubmarine") || !GetB(ev, "HasDamage")) return false;
            var c = Get(ev, "Cost");
            cost = c is int ? (int)c : 0;
            repairable = GetB(ev, "FacilitiesAvailable") && GetB(ev, "IsAllowedDamage") && !GetB(ev, "IsDisallowedDamage") && GetB(ev, "HasStateSection");
            return true;
        }

        static void CanDismissPostfix(object __instance, object item, ref bool __result)
        {
            var a = Active;
            if (__result || (object)a == null || !a._dismissDamaged.Value || item == null) return;
            try
            {
                // what the game checked before the damage: not an airbase unit, not a historical campaign, not destroyed
                if (!string.IsNullOrWhiteSpace(a.Get(item, "CampaignAirbaseId") as string) || a.GetB(__instance, "UsesHistoricalReinforcements") || a.GetB(item, "IsAirUnit")) return;
                var gl = __instance.GetType().GetMethod("GetLogisticsState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var ls = gl == null ? null : gl.Invoke(__instance, new[] { item });
                if (ls == null || a.GetB(ls, "IsDestroyed")) return;
                bool repairable; int cost;
                if (a.RepairInfo(__instance, item, out repairable, out cost) && repairable) __result = true;
            }
            catch (Exception e) { Trace("DismissDamaged (CanDismiss): " + e.Message); }
        }

        static void DismissRefundPostfix(object __instance, object item, ref int __result)
        {
            var a = Active;
            if ((object)a == null || !a._dismissDamaged.Value || item == null || __result <= 0) return;
            try
            {
                bool repairable; int cost;
                if (!a.RepairInfo(__instance, item, out repairable, out cost) || cost <= 0) return;
                __result = Math.Max(0, __result - cost);
            }
            catch (Exception e) { Trace("DismissDamaged (refund): " + e.Message); }
        }
    }
}
