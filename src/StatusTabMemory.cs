using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// The Unit Status window remembers the tab that was open for a unit (a contact: for its track) and opens it again when that
    /// unit is selected again, when the tab is available then. The tab is noted when the window leaves the unit (UnitStatusViewModel.SwitchUnit, before
    /// the game changes anything) and put back after SwitchUnit and all the other mods' hooks on it; a tab that is not available yet (another mod makes
    /// it available in its own SwitchUnit hook) is put back by the next Refresh of the window.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<int> _lastTab;   // the tab (Kind) that was open last: opened for a unit that has no tab of its own yet (kept between sessions); -1 = none
        static readonly ConditionalWeakTable<object, object> TabMemory = new ConditionalWeakTable<object, object>();     // unit / contact -> tab Kind (boxed int)
        static readonly ConditionalWeakTable<object, object> TabPending = new ConditionalWeakTable<object, object>();    // status -> unit / contact whose tab is to be put back

        void BindTabMemoryConfig()
        {
            _lastTab = Config.Bind("UnitStatus", "LastTab", -1,
                "Unit Status window: the tab that was open last (Kind). A unit (contact) with no tab of its own yet opens with it, if it is available. Written by the mod.");
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

        sealed class TabSeen { public object Key; public int Kind; }
        static readonly ConditionalWeakTable<object, TabSeen> TabSeenBy = new ConditionalWeakTable<object, TabSeen>();   // status -> the unit and tab seen at the last step

        /// <summary>Before SwitchUnit: the key of the unit the window is leaving (the postfix compares it). Nothing is noted here: a tab is a unit's own
        /// (and the preferred one) only when the user changed it while that unit was shown (TabTrackStep); the tab the game falls back to on a unit
        /// that lacks the preferred one (a weapon has no Sensors tab) is neither.</summary>
        object TabNote(object status) { return TabKey(status); }

        void NoteLastTab(int kind) { if (_lastTab != null && _lastTab.Value != kind) _lastTab.Value = kind; }

        /// <summary>A few times a second for a Unit Status window: a tab that changed while the same unit stayed shown is the user's choice - the unit's own
        /// tab and the preferred one (LastTab). A tab seen right after the unit changed is the restore's or the game's fallback: not noted, so a
        /// unit that lacks the preferred tab does not take it over, and the next unit gets the preferred tab again.</summary>
        void TabTrackStep(object w)
        {
            WeakReference r;
            if (!StatusViewByWindow.TryGetValue(w, out r)) return;
            var usv = r.Target as Noesis.DependencyObject;
            if (usv == null) return;
            var status = Get(usv, "DataContext");
            if (status == null || TabPending.TryGetValue(status, out _)) return;   // a restore is under way: the game's own choice is not the user's
            var key = TabKey(status);
            if (key == null) return;
            int kind = SelectedKind(status);
            if (kind < 0) return;
            TabSeen seen;
            if (!TabSeenBy.TryGetValue(status, out seen)) { seen = new TabSeen(); TabSeenBy.Add(status, seen); seen.Key = null; seen.Kind = -1; }
            if (!ReferenceEquals(seen.Key, key)) { seen.Key = key; seen.Kind = kind; return; }   // another unit (or the first look): what is open is not the user's choice
            if (seen.Kind == kind) return;
            seen.Kind = kind;
            TabMemory.Remove(key); TabMemory.Add(key, kind);
            NoteLastTab(kind);
            Trace("remember tab: the user's tab " + kind + " for this unit and as the preferred one");
        }

        /// <summary>After SwitchUnit (and the mod's own tab hooks): another unit is shown - its remembered tab is put back, now or at the next Refresh.</summary>
        void TabRestore(object status, object oldKey)
        {
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
            int kind;
            if (TabMemory.TryGetValue(key, out boxed)) kind = (int)boxed;
            else if (_lastTab != null && _lastTab.Value >= 0) kind = _lastTab.Value;   // no tab of its own: the one of the previous unit
            else { TabPending.Remove(status); return; }
            var tabs = Get(status, "Tabs") as IList;
            if (tabs == null) { TabPending.Remove(status); return; }
            foreach (var t in tabs)
            {
                int k; try { k = Convert.ToInt32(Get(t, "Kind")); } catch { continue; }
                if (k != kind) continue;
                if (GetB(t, "IsAvailable"))
                {
                    if (!GetB(t, "IsSelected")) { SelectOnly(status, t); Trace("remember tab: tab " + kind + " put back (" + (TabMemory.TryGetValue(key, out boxed) ? "the unit's own" : "the preferred one") + ")"); }
                    TabPending.Remove(status);
                }
                else if (final) { TabPending.Remove(status); Trace("remember tab: tab " + kind + " is not available for this unit - the game's choice stays"); }   // not available for this unit (any more)
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
