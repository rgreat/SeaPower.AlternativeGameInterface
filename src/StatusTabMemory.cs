using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [UnitStatus] RememberTab: the Unit Status window remembers the tab that was open for a unit (a contact: for its track) and opens it again when that
    /// unit is selected again, when the tab is available then. The tab is noted when the window leaves the unit (UnitStatusViewModel.SwitchUnit, before
    /// the game changes anything) and put back after SwitchUnit and all the other mods' hooks on it; a tab that is not available yet (another mod makes
    /// it available in its own SwitchUnit hook) is put back by the next Refresh of the window.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _rememberTab;
        static readonly ConditionalWeakTable<object, object> TabMemory = new ConditionalWeakTable<object, object>();     // unit / contact -> tab Kind (boxed int)
        static readonly ConditionalWeakTable<object, object> TabPending = new ConditionalWeakTable<object, object>();    // status -> unit / contact whose tab is to be put back

        void BindTabMemoryConfig()
        {
            _rememberTab = Config.Bind("UnitStatus", "RememberTab", true,
                "Unit Status window: remembers the tab that was open for each unit (contact) and opens it again when the unit is selected again, if that tab is available.");
        }

        void HookTabMemory(Harmony harmony, Type usvm)
        {
            var refresh = usvm.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (refresh != null) TryPatch(harmony, refresh, null, "StatusRefreshPostfix", null);
        }

        object TabKey(object status)
        {
            var unit = StatusUnit(status);
            if (unit != null) return unit;
            TrackTab tt;
            return TrackTabs.TryGetValue(status, out tt) ? tt.Vehicle : null;
        }

        int SelectedKind(object status)
        {
            var tabs = Get(status, "Tabs") as IList;
            if (tabs == null) return -1;
            foreach (var t in tabs)
                if (GetB(t, "IsSelected") && GetB(t, "IsAvailable")) { try { return Convert.ToInt32(Get(t, "Kind")); } catch { return -1; } }
            return -1;
        }

        /// <summary>Before SwitchUnit: the tab of the unit the window is leaving is noted; the result is the key of that unit (the postfix compares it).</summary>
        object TabNote(object status)
        {
            if (!_rememberTab.Value) return null;
            var key = TabKey(status);
            if (key == null) return null;
            int kind = SelectedKind(status);
            if (kind >= 0) { TabMemory.Remove(key); TabMemory.Add(key, kind); }
            return key;
        }

        /// <summary>After SwitchUnit (and the mod's own tab hooks): another unit is shown - its remembered tab is put back, now or at the next Refresh.</summary>
        void TabRestore(object status, object oldKey)
        {
            if (!_rememberTab.Value) return;
            var key = TabKey(status);
            if (key == null || ReferenceEquals(key, oldKey)) return;
            TabPending.Remove(status); TabPending.Add(status, key);
            TabApply(status, final: false);
        }

        void TabApply(object status, bool final)
        {
            object key;
            if (!TabPending.TryGetValue(status, out key)) return;
            object boxed;
            if (!ReferenceEquals(key, TabKey(status))) { TabPending.Remove(status); return; }   // the window has moved on to another unit
            if (!TabMemory.TryGetValue(key, out boxed)) { TabPending.Remove(status); return; }
            int kind = (int)boxed;
            var tabs = Get(status, "Tabs") as IList;
            if (tabs == null) { TabPending.Remove(status); return; }
            foreach (var t in tabs)
            {
                int k; try { k = Convert.ToInt32(Get(t, "Kind")); } catch { continue; }
                if (k != kind) continue;
                if (GetB(t, "IsAvailable"))
                {
                    if (!GetB(t, "IsSelected")) SelectOnly(status, t);
                    TabPending.Remove(status);
                }
                else if (final) TabPending.Remove(status);   // not available for this unit (any more): the game's choice stays
                return;
            }
            TabPending.Remove(status);
        }

        static void StatusSwitchPrefix(object __instance, ref object __state)
        {
            var a = Active;
            if ((object)a == null) return;
            try { __state = a.TabNote(__instance); } catch (Exception e) { Trace("remember tab: " + e.Message); }
        }

        static void StatusRefreshPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null) return;
            try { a.TabApply(__instance, final: true); } catch (Exception e) { Trace("remember tab: " + e.Message); }
        }
    }
}
