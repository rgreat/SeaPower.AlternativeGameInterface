using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// KeepReserveSubmarines (2.3.2): the game loses a submarine that waits in the Reserve force for its service (Rearm/Repair order).
    /// Cause (game code): the management snapshot that the Status / Builder windows work with leaves out "campaign support" units (aircraft,
    /// helicopters AND submarines) of the selected force, a submarine is in it only through its own Submarine-... force. So a submarine in the
    /// Reserve is invisible in the windows, and when the Reserve is the selected force and anything changes the roster (a purchase, a sale,
    /// an upgrade), LinearCampaignMapViewModel.UpdateSelectedStandardTaskForceSnapshot replaces the Reserve snapshot by the one without it
    /// and persists it: the submarine is gone from the save, only its service order stays.
    /// The patch of LinearCampaignStandardTaskForceEntry.ReplaceSnapshot puts the submarines of the Reserve that the new snapshot lacks back.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _fixSubs;   // one option for the three parts: keep, show, restore

        void BindReserveSubsConfig()
        {
            _fixSubs = Config.Bind("Campaign", "FixReserveSubmarines", true,
                "Campaign: fixes for a submarine sent for service (a game bug): it is not lost from the Reserve force when the roster is changed, it is listed in the Reserve card, and a submarine already lost is put back into the Reserve when the campaign map loads.");
        }

        static object RsGet(object o, string name)
        {
            if (o == null) return null;
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (p != null) return p.GetValue(o, null);
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        static bool RsIsSub(object item)
        {
            if (item == null) return false;
            var air = RsGet(item, "IsAirUnit");
            return !(air is bool && (bool)air) && string.Equals(RsGet(item, "Category") as string, "Submarine", StringComparison.OrdinalIgnoreCase);
        }

        void PatchReserveSubmarines(Harmony harmony, Assembly asm)
        {
            try
            {
                var t = asm.GetType(string.Join(".", "SeapowerUI", "ViewModels", "LinearCampaignStandardTaskForceEntry"));   // (not one literal: Keel would list it as a type this game lacks)
                var sm = asm.GetType(string.Join(".", "SeapowerUI", "ViewModels", "LinearCampaignTaskForceSaveManager"));
                var mv = asm.GetType(string.Join(".", "SeapowerUI", "ViewModels", "LinearCampaignMapViewModel"));
                // every write of the forces goes through the private WriteCurrentTaskForces(IniHandler, collection)
                MethodInfo wr = null;
                if (sm != null)
                    foreach (var m in sm.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                        if (m.Name == "WriteCurrentTaskForces" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.Name == "IniHandler") wr = m;
                if (wr != null) { harmony.Patch(wr, prefix: Hm("ServiceSubsWritePrefix")); Trace("patched LinearCampaignTaskForceSaveManager.WriteCurrentTaskForces (FixReserveSubmarines: keep)"); }
                else Trace("FixReserveSubmarines: WriteCurrentTaskForces(IniHandler, ...) not found");
                // in memory too: the Reserve card keeps the submarine when the game rewrites the selected force from the Status window
                var rs = t == null ? null : t.GetMethod("ReplaceSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var up = mv == null ? null : mv.GetMethod("UpdateSelectedStandardTaskForceSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (rs != null && up != null)
                {
                    harmony.Patch(up, prefix: Hm("ReserveSubsScopePrefix"), finalizer: Hm("ReserveSubsScopeFinalizer"));
                    harmony.Patch(rs, prefix: Hm("ReserveSubsReplacePrefix"));
                    Trace("patched LinearCampaignStandardTaskForceEntry.ReplaceSnapshot (FixReserveSubmarines: keep in memory)");
                }
                var rf = mv == null ? null : mv.GetMethod("RefreshStandardTaskForcesFromStore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (rf != null) { harmony.Patch(rf, postfix: Hm("RestoreSubsPostfix")); Trace("patched LinearCampaignMapViewModel.RefreshStandardTaskForcesFromStore (FixReserveSubmarines: restore)"); }
                var rv = mv == null ? null : mv.GetMethod("RefreshStandardManagerVisibleUnits", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (rv != null) { harmony.Patch(rv, postfix: Hm("ReserveSubsVisiblePostfix")); Trace("patched LinearCampaignMapViewModel.RefreshStandardManagerVisibleUnits (FixReserveSubmarines: show)"); }
            }
            catch (Exception e) { Trace("patch FixReserveSubmarines: " + e.Message); }
        }

        // ------------------------------------------------------------ keep
        // A submarine with a service order that is not acknowledged yet must stay in the forces: it is in the Reserve while in service and
        // goes to its own Submarine- force when the game returns it (that acknowledges the order first). Any write of the forces that would
        // drop it (the game removes it in several places: the support-unit filter of the selected force, its own roster rebuilds) gets it
        // back into the Reserve. The record comes from the forces in the save, or from the last mission it took part in.

        static IEnumerable<string> RsPendingSubOrders(Dictionary<string, Dictionary<string, string>> data)
        {
            var ids = new List<string>();
            foreach (var kv in data)
            {
                if (!kv.Key.StartsWith("TaskForceMode_RepairOrder_", StringComparison.Ordinal) || kv.Value == null) continue;
                string id = RsIni(data, kv.Key, "InstanceId");
                if (id.Length == 0 || RsIni(data, kv.Key, "Ready").Length == 0) continue;
                if (string.Equals(RsIni(data, kv.Key, "Acknowledged"), "True", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(RsIni(data, RsIni(data, kv.Key, "Source"), "IsDestroyed"), "True", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        static object RsFindSubRecord(object state, Dictionary<string, Dictionary<string, string>> data, string id)
        {
            // 1. the forces as saved now
            int count; int.TryParse(RsIni(data, "TaskForceMode_Forces", "Count"), out count);
            for (int i = 1; i <= count; i++)
            {
                string sec = "TaskForceMode_Force" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach (var it in (IEnumerable)RsGet(UpStatic("LinearCampaignTaskForceSaveManager", "ReadSnapshot", state, sec, sec + "_Unit"), "Items"))
                    if (RsIsSub(it) && string.Equals(RsGet(it, "InstanceId") as string, id, StringComparison.OrdinalIgnoreCase)) return it;
            }
            // 2. the latest mission roster that has it
            int best = -1;
            foreach (var kv in data)
            {
                var m = System.Text.RegularExpressions.Regex.Match(kv.Key, "^TaskForceMode_Mission(\\d+)_Post_Unit\\d+$");
                if (!m.Success || !string.Equals(RsIni(data, kv.Key, "InstanceId"), id, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(RsIni(data, kv.Key, "Category"), "Submarine", StringComparison.OrdinalIgnoreCase)) continue;
                int n = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (n > best) best = n;
            }
            if (best < 0) return null;
            string ps = "TaskForceMode_Mission" + best.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_Post";
            foreach (var it in (IEnumerable)RsGet(UpStatic("LinearCampaignTaskForceSaveManager", "ReadSnapshot", state, ps, ps + "_Unit"), "Items"))
                if (RsIsSub(it) && string.Equals(RsGet(it, "InstanceId") as string, id, StringComparison.OrdinalIgnoreCase)) return it;
            return null;
        }

        // the collection with the pending-service submarines it lacks added to the Reserve (null: nothing to add)
        static object RsWithServiceSubs(object state, object coll, out List<string> added)
        {
            added = new List<string>();
            var data = RsGet(state, "Data") as Dictionary<string, Dictionary<string, string>>;
            if (data == null || coll == null) return null;
            var forces = new List<object>();
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in (IEnumerable)RsGet(coll, "Forces"))
            {
                if (f == null) continue;
                forces.Add(f);
                foreach (var it in (IEnumerable)RsGet(RsGet(f, "Snapshot"), "Items")) owned.Add(RsGet(it, "InstanceId") as string ?? "");
            }
            if (forces.Count == 0) return null;
            var lost = new List<object>();
            foreach (var id in RsPendingSubOrders(data))
            {
                if (owned.Contains(id)) continue;
                var rec = RsFindSubRecord(state, data, id);
                if (rec != null) lost.Add(rec);
            }
            if (lost.Count == 0) return null;
            Type groupType = forces[0].GetType(), snapType = RsGet(forces[0], "Snapshot").GetType(), itemType = lost[0].GetType();
            int ri = forces.FindIndex(f => string.Equals(RsGet(f, "ForceId") as string, "Reserve", StringComparison.OrdinalIgnoreCase));
            var items = new List<object>(); var order = new List<string>();
            var first = RsGet(forces[0], "Snapshot");
            string name = "Reserve"; object ap = RsGet(first, "AvailablePoints"), cp = RsGet(first, "CapPoints");
            if (ri >= 0)
            {
                var rs = RsGet(forces[ri], "Snapshot");
                foreach (var it in (IEnumerable)RsGet(rs, "Items")) items.Add(it);
                foreach (var o in (IEnumerable)RsGet(forces[ri], "UnitDisplayOrder")) order.Add(o as string);
                name = RsGet(rs, "TaskForceName") as string ?? name; ap = RsGet(rs, "AvailablePoints"); cp = RsGet(rs, "CapPoints");
            }
            foreach (var it in lost) { items.Add(it); order.Add(RsGet(it, "InstanceId") as string); added.Add((RsGet(it, "UnitType") as string) + "#" + (RsGet(it, "InstanceId") as string)); }
            var reserve = RsCtor(groupType, "Reserve", RsCtor(snapType, name, RsArray(itemType, items), ap, cp), order.ToArray());
            if (ri >= 0) forces[ri] = reserve; else forces.Add(reserve);
            return RsCtor(coll.GetType(), RsArray(groupType, forces), RsGet(coll, "SelectedForceId"), RsGet(coll, "NextNameIndex"));
        }

        static int _rsDepth;
        static void ReserveSubsScopePrefix() { _rsDepth++; }
        static void ReserveSubsScopeFinalizer() { if (_rsDepth > 0) _rsDepth--; }

        static void ReserveSubsReplacePrefix(object __instance, object[] __args)
        {
            try
            {
                var me = _inst;
                if (_rsDepth <= 0 || me == null || me._fixSubs == null || !me._fixSubs.Value || __args == null || __args.Length < 1 || __args[0] == null) return;
                if (!string.Equals(RsGet(__instance, "ForceId") as string, "Reserve", StringComparison.OrdinalIgnoreCase)) return;
                var oldItems = RsGet(RsGet(__instance, "Snapshot"), "Items") as IEnumerable;
                var neu = __args[0];
                var newItems = RsGet(neu, "Items") as IEnumerable;
                if (oldItems == null || newItems == null) return;
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var all = new List<object>();
                foreach (var it in newItems) { all.Add(it); ids.Add(RsGet(it, "InstanceId") as string ?? ""); }
                int n = 0;
                foreach (var it in oldItems)
                    if (RsIsSub(it) && !ids.Contains(RsGet(it, "InstanceId") as string ?? "")) { all.Add(it); n++; }
                if (n == 0) return;
                // the new snapshot object itself gets the items (does not depend on Harmony writing __args back)
                var elem = all[0].GetType();
                var listType = typeof(List<>).MakeGenericType(elem);
                var list = (IList)Activator.CreateInstance(listType);
                foreach (var it in all) list.Add(it);
                var roType = typeof(System.Collections.ObjectModel.ReadOnlyCollection<>).MakeGenericType(elem);
                var bf = neu.GetType().GetField("<Items>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (bf == null) return;
                bf.SetValue(neu, Activator.CreateInstance(roType, list));
                Trace("FixReserveSubmarines: the Reserve card keeps " + n + " submarine(s)");
            }
            catch (Exception e) { Trace("FixReserveSubmarines (keep in memory): " + (e.InnerException ?? e).Message); }
        }

        static void ServiceSubsWritePrefix(object[] __args)
        {
            try
            {
                var me = _inst;
                if (me == null || me._fixSubs == null || !me._fixSubs.Value || __args == null || __args.Length < 2) return;
                List<string> added;
                var fixedColl = RsWithServiceSubs(__args[0], __args[1], out added);
                if (fixedColl == null) return;
                // the argument object itself gets the new forces (does not depend on Harmony writing __args back)
                var bf = __args[1].GetType().GetField("<Forces>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (bf != null) bf.SetValue(__args[1], bf.GetValue(fixedColl));
                __args[1] = fixedColl;
                // the caller, for the report to the game's developers
                var st = new System.Diagnostics.StackTrace(1, false);
                var who = new List<string>();
                for (int i = 0; i < st.FrameCount && who.Count < 4; i++)
                {
                    var m = st.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null || m.DeclaringType.Namespace == null || !m.DeclaringType.Namespace.StartsWith("SeapowerUI", StringComparison.Ordinal)) continue;
                    who.Add(m.DeclaringType.Name + "." + m.Name);
                }
                Trace("FixReserveSubmarines: kept in the Reserve " + string.Join(", ", added.ToArray()) + " (the write came from " + string.Join(" <- ", who.ToArray()) + ")");
            }
            catch (Exception e) { Trace("FixReserveSubmarines (keep): " + (e.InnerException ?? e).Message); }
        }

        // ------------------------------------------------------------ show
        // The Submarines tab of the Task Force Manager lists the submarines of the Submarine- forces only; a submarine in service sits in the
        // Reserve and the tab stays empty. Its row (the game's own, greyed with "Returning to operations ...") is added to the tab's list,
        // as the ships in service are listed in the Reserve of the Surface tab.

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, KeyValuePair<object, object>>> RsRows =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, KeyValuePair<object, object>>>();
        static int _rsShown = -1;

        static void ReserveSubsVisiblePostfix(object __instance)
        {
            try
            {
                var me = _inst;
                if (me == null || me._fixSubs == null || !me._fixSubs.Value) return;
                var sel = RsGet(__instance, "IsStandardSubmarineCategorySelected");
                if (!(sel is bool) || !(bool)sel) return;
                var status = RsGet(__instance, "TaskForceStatusWindow");
                var visible = RsGet(__instance, "StandardManagerVisibleUnits") as IList;
                if (status == null || visible == null) return;
                object reserve = null;
                foreach (var f in (IEnumerable)RsGet(__instance, "StandardTaskForces"))
                    if (string.Equals(RsGet(f, "ForceId") as string, "Reserve", StringComparison.OrdinalIgnoreCase)) reserve = f;
                var snap = RsGet(reserve, "Snapshot");
                var items = RsGet(snap, "Items") as IEnumerable;
                if (items == null) return;
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var u in visible) have.Add(RsGet(RsGet(u, "Item"), "InstanceId") as string ?? "");
                var mission = RsGet(__instance, "_selectedMission");
                object mi = mission == null ? null : RsGet(mission, "_index");
                int missionIndex = mi is int ? (int)mi : 0;
                var cache = RsRows.GetOrCreateValue(__instance);
                int n = 0;
                foreach (var it in items)
                {
                    if (!RsIsSub(it)) continue;
                    string id = RsGet(it, "InstanceId") as string ?? "";
                    if (have.Contains(id)) continue;
                    KeyValuePair<object, object> c;
                    object row = null;
                    if (cache.TryGetValue(id, out c) && ReferenceEquals(c.Key, it)) row = c.Value;
                    if (row == null)
                    {
                        var one = RsCtor(snap.GetType(), RsGet(snap, "TaskForceName") as string ?? "Reserve", RsArray(it.GetType(), new List<object> { it }), 0, 0);
                        var mp = status.GetType().GetMethod("CreateManagerRosterPreview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mp == null) return;
                        foreach (var r in (IEnumerable)mp.Invoke(status, new object[] { one, missionIndex })) { row = r; break; }
                        if (row == null) continue;
                        var rp = row.GetType().GetProperty("IsReserveUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (rp != null && rp.CanWrite) rp.SetValue(row, true, null);
                        cache[id] = new KeyValuePair<object, object>(it, row);
                    }
                    visible.Add(row);
                    n++;
                }
                if (n > 0)
                {
                    UpCall(__instance, "NotifyPropertyChanged", "HasStandardManagerVisibleUnits");
                    UpCall(__instance, "NotifyPropertyChanged", "HasStandardManagerEntries");
                }
                if (n != _rsShown) { _rsShown = n; Trace("FixReserveSubmarines (show): " + n + " submarine(s) in service listed on the Submarines tab"); }
            }
            catch (Exception e) { Trace("FixReserveSubmarines (show): " + (e.InnerException ?? e).Message); }
        }

        // ------------------------------------------------------------ restore
        // A submarine lost before (an older save) has only its service order and its mission record left: on the campaign map load the
        // forces are written once more, the keep step above puts it back into the Reserve, and the game's return dialog then gives it its
        // Submarine- force when the order is ready.

        static bool _rsRestoring;

        static object RsCtor(Type t, params object[] args)
        {
            foreach (var c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var ps = c.GetParameters();
                if (ps.Length < args.Length) continue;
                var full = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) full[i] = i < args.Length ? args[i] : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
                return c.Invoke(full);
            }
            throw new MissingMethodException(t.Name + " ctor");
        }

        static Array RsArray(Type elem, List<object> items)
        {
            var arr = Array.CreateInstance(elem, items.Count);
            for (int i = 0; i < items.Count; i++) arr.SetValue(items[i], i);
            return arr;
        }

        static string RsIni(Dictionary<string, Dictionary<string, string>> data, string section, string key)
        {
            Dictionary<string, string> sec; string v;
            return section != null && data.TryGetValue(section, out sec) && sec != null && sec.TryGetValue(key, out v) ? (v ?? "").Trim() : "";
        }

        static void RestoreSubsPostfix(object __instance)
        {
            if (_rsRestoring) return;
            try
            {
                var me = _inst;
                if (me == null || me._fixSubs == null || !me._fixSubs.Value) return;
                var state = UpStatic("LinearCampaignTaskForceModeRules", "OpenCampaignStateIni");
                var coll = UpStatic("LinearCampaignTaskForceSaveManager", "ReadCurrentTaskForces");
                List<string> added;
                var fixedColl = RsWithServiceSubs(state, coll, out added);
                if (fixedColl == null) return;
                UpStatic("LinearCampaignTaskForceSaveManager", "WriteCurrentTaskForces", fixedColl, null, null, null);
                Trace("FixReserveSubmarines: put back into the Reserve: " + string.Join(", ", added.ToArray()));
                _rsRestoring = true;
                try { __instance.GetType().GetMethod("RefreshStandardTaskForcesFromStore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(__instance, new object[] { null }); }
                finally { _rsRestoring = false; }
            }
            catch (Exception e) { Trace("FixReserveSubmarines (restore): " + (e.InnerException ?? e).Message); }
        }
    }
}
