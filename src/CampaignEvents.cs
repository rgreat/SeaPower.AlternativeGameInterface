using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Campaign map, a briefing / newspaper event (a FreeEvent, not a mission) is selected: the game passes "mission 0" to the Task Force status
    /// (<c>(_selectedMission as LinearCampaignMission)?._index ?? 0</c>), and FindLatestPriorUnitStateSection(..., 0) then looks at mission 1 only
    /// (the loop starts at Math.Max(1, target - 1)). The ships show the state they had before the first mission: ammunition full, no damage.
    /// The status window also keeps the index of the previously selected mission, so it may show the state from before that mission.
    /// Fix: for an index of 0 the state of the latest played mission is used (the same as for the next mission to play).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _eventState;
        static MethodInfo _openStateIni;
        static PropertyInfo _iniData;

        void BindCampaignEventsConfig()
        {
            _eventState = Config.Bind("Campaign", "EventShipState", true,
                "Campaign map: while a briefing or newspaper event is selected, the Task Force shows the ships as they are after the latest mission (the game shows the state they had before the first mission: ammunition full, damage repaired).");
        }

        void HookCampaignEvents(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var sm = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceSaveManager");
            var rules = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceModeRules");
            var st = asm.GetType("SeapowerUI.ViewModels.LinearCampaignTaskForceStatusViewModel");
            var find = sm == null ? null : sm.GetMethod("FindLatestPriorUnitStateSection", all);
            var load = st == null ? null : st.GetMethod("LoadFromSnapshot", all);
            _openStateIni = rules == null ? null : rules.GetMethod("OpenCampaignStateIni", all, null, Type.EmptyTypes, null);
            if (find == null || _openStateIni == null) { Trace("FindLatestPriorUnitStateSection / OpenCampaignStateIni not found: EventShipState not available"); return; }
            TryPatch(harmony, find, "FindLatestStatePrefix", null, null);
            Trace("patched FindLatestPriorUnitStateSection (EventShipState)");
            if (load != null) { TryPatch(harmony, load, "StatusLoadPrefix", null, null); Trace("patched LinearCampaignTaskForceStatusViewModel.LoadFromSnapshot (EventShipState)"); }
        }

        /// <summary>The number after the latest mission that has a saved unit state (TaskForceMode_Mission{N}_PostState_ / _LaunchState_), 0 if none.</summary>
        static int NextStateMission(object ini)
        {
            try
            {
                if (ini == null) return 0;
                if (_iniData == null) _iniData = ini.GetType().GetProperty("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var data = _iniData == null ? null : _iniData.GetValue(ini, null) as System.Collections.IDictionary;
                if (data == null) return 0;
                int best = 0;
                const string head = "TaskForceMode_Mission";
                foreach (var k in data.Keys)
                {
                    var s = k as string;
                    if (s == null || s.Length < head.Length + 3 || !s.StartsWith(head, StringComparison.Ordinal)) continue;
                    int i = head.Length, n = 0;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9' && n < 100000) { n = n * 10 + (s[i] - '0'); i++; }
                    if (i == head.Length || n <= best) continue;
                    if (string.CompareOrdinal(s, i, "_PostState_", 0, 11) == 0 || string.CompareOrdinal(s, i, "_LaunchState_", 0, 13) == 0) best = n;
                }
                return best == 0 ? 0 : best + 1;
            }
            catch { return 0; }
        }

        static int NextStateMission()
        {
            try { return _openStateIni == null ? 0 : NextStateMission(_openStateIni.Invoke(null, null)); } catch { return 0; }
        }

        static void FindLatestStatePrefix(object campaignStateIni, ref int targetMissionIndex)
        {
            var a = Active;
            if ((object)a == null || a._eventState == null || !a._eventState.Value || targetMissionIndex > 0) return;
            int n = NextStateMission(campaignStateIni);
            if (n > 0) targetMissionIndex = n;
        }

        static void StatusLoadPrefix(ref int targetMissionIndex)
        {
            var a = Active;
            if ((object)a == null || a._eventState == null || !a._eventState.Value || targetMissionIndex > 0) return;
            int n = NextStateMission();
            if (n > 0) targetMissionIndex = n;
        }
    }
}
