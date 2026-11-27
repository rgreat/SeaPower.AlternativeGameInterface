using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// CenterMapOnLogClick (1.8.0): a click on a contact / unit link in the Event Log (the Hyperlink of an Alert: SelectRaisingObject / SelectAboutObject, which
    /// the game answers by switching the 3D camera to that object) also centres the Tactical Display on it. The place is the one the player's side has for the
    /// object (its map track); an object that is not the player's and has no track is not shown (its real place is not given away).
    /// The Click event of the Hyperlinks bubbles up to the Event Log window: one handler on the window, no searching.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _logCenter, _logLinks;
        WeakReference _lastLogObj;
        static readonly ConditionalWeakTable<object, object> LogHooked = new ConditionalWeakTable<object, object>();
        static readonly ConditionalWeakTable<object, object> LogOther = new ConditionalWeakTable<object, object>();   // windows that are not the Event Log

        void BindEventLogConfig()
        {
            _logLinks = Config.Bind("EventLog", "AllTracksAreLinks", true,
                "Every track number in the Event Log is a link, as the ones the game makes itself (New contact, Track classified, Track faded and the like are plain text in the game): " +
                "a click selects the object, with CenterMapOnClick a repeated click centres the map on it. Only tracks of the player's own list are linked.");
            _logCenter = Config.Bind("EventLog", "CenterMapOnClick", true,
                "A repeated click on the same contact or unit link in the Event Log centres the Tactical Display map on it (the first click only selects it, as in the game).");
        }

        // ---------------------------------------------------------------- every track number is a link

        // Track numbers: 1001 and up (own tracks from 1001, other sides' from 7001). The game makes a link only where the message has the object (${TrackId}
        // of the two-object message, ${OwnTrackId}); "New contact ... track N", "Track N classified ..." and "Track N faded" are plain text.
        static readonly Regex TrackNumber = new Regex(@"(?<![\d.,])\d{4,6}(?![\d])", RegexOptions.Compiled);
        static readonly Regex LinkSpan = new Regex(@"<Hyperlink\b.*?</Hyperlink>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        void HookEventLog(HarmonyLib.Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var main = asm.GetType("SeapowerUI.ViewModels.MainGameViewModel");
            if (main == null) return;
            int n = 0;
            foreach (var m in main.GetMethods(all))
                if (m.Name == "RaiseAlert" && m.DeclaringType == main)
                {
                    try { harmony.Patch(m, prefix: Hm("LogRaisePrefix"), postfix: Hm("LogRaisePostfix")); n++; }
                    catch (Exception e) { Trace("patch of RaiseAlert failed: " + e.Message); }
                }
            Trace("event log: " + n + " RaiseAlert overloads watched");
        }

        static object TopAlert(object vm)
        {
            var log = Active.Get(vm, "EventLog");
            var alerts = log == null ? null : Active.Get(log, "Alerts") as IList;
            return alerts != null && alerts.Count > 0 ? alerts[0] : null;
        }

        static void LogRaisePrefix(object __instance, out object __state)
        {
            __state = null;
            var a = Active;
            if ((object)a == null || !LogWatch(a)) return;
            try { __state = TopAlert(__instance); } catch { }
        }

        static void LogRaisePostfix(object __instance, object __state)
        {
            var a = Active;
            if ((object)a == null || !LogWatch(a)) return;
            try
            {
                var alert = TopAlert(__instance);
                if (alert == null || ReferenceEquals(alert, __state)) return;                      // the alert this call has just added
                if (a._logLinks != null && a._logLinks.Value) a.LinkTracks(alert);
                if (a._logWrap != null && a._logWrap.Value) LogWrapAlert(alert, true);
            }
            catch (Exception e) { Trace("event log links: " + e.Message); }
        }

        static bool LogWatch(AlternativeGameInterfacePlugin a)
        {
            return (a._logLinks != null && a._logLinks.Value) || (a._logWrap != null && a._logWrap.Value);
        }

        /// <summary>The player's own list of tracks: track number -> the object.</summary>
        object ObjectOfTrack(int id)
        {
            var asm = GameAssembly();
            var tGlobals = asm == null ? null : asm.GetType("SeaPower.Globals");
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var tf = tGlobals == null ? null : tGlobals.GetField("_playerTaskforce", all);
            var tfv = tf == null ? null : tf.GetValue(null);
            var table = tfv == null ? null : Get(tfv, "PlottingTable");
            var list = table == null ? null : Get(table, "Vehicles") as IEnumerable;
            if (list == null) return null;
            foreach (var v in list)
            {
                if (v == null || !(Get(v, "Id") is int) || (int)Get(v, "Id") != id) continue;
                return Get(v, "BaseObject");
            }
            return null;
        }

        void LinkTracks(object alert)
        {
            var content = Get(alert, "AlertContent");
            var text = content == null ? null : Get(content, "FormattedContent") as string;
            if (string.IsNullOrEmpty(text)) return;
            var spans = new List<int[]>();
            foreach (Match s in LinkSpan.Matches(text)) spans.Add(new[] { s.Index, s.Index + s.Length });
            var found = new List<KeyValuePair<Match, object>>();
            foreach (Match m in TrackNumber.Matches(text))
            {
                bool inLink = false;
                foreach (var s in spans) if (m.Index >= s[0] && m.Index < s[1]) { inLink = true; break; }
                if (inLink) continue;
                int id;
                if (!int.TryParse(m.Value, out id) || id < 1001) continue;
                var obj = ObjectOfTrack(id);
                if (obj != null) found.Add(new KeyValuePair<Match, object>(m, obj));
            }
            if (found.Count == 0) return;
            // an alert has two places for an object: the one it is raised by and the one it is about
            object raising = Get(alert, "RaisingObject"), about = Get(alert, "AboutObject");
            var slot = new Dictionary<object, string>();
            var edits = new List<KeyValuePair<Match, string>>();
            foreach (var f in found)
            {
                string cmd;
                if (!slot.TryGetValue(f.Value, out cmd))
                {
                    if (ReferenceEquals(f.Value, raising)) cmd = "SelectRaisingObject";
                    else if (ReferenceEquals(f.Value, about)) cmd = "SelectAboutObject";
                    else if (raising == null) { raising = f.Value; cmd = "SelectRaisingObject"; SetAlertObject(alert, true, raising); }
                    else if (about == null) { about = f.Value; cmd = "SelectAboutObject"; SetAlertObject(alert, false, about); }
                    else continue;
                    slot[f.Value] = cmd;
                }
                edits.Add(new KeyValuePair<Match, string>(f.Key, cmd));
            }
            if (edits.Count == 0) return;
            var sb = new System.Text.StringBuilder(text);
            for (int i = edits.Count - 1; i >= 0; i--)
            {
                var m = edits[i].Key;
                sb.Remove(m.Index, m.Length).Insert(m.Index, "<Hyperlink Command=\"{Binding " + edits[i].Value + "}\">" + m.Value + "</Hyperlink>");
            }
            SetV(content, "FormattedContent", sb.ToString());
        }

        /// <summary>The object (and the command that selects it) of an Alert that had none: the fields behind its get-only properties.</summary>
        void SetAlertObject(object alert, bool raising, object obj)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var t = alert.GetType();
            // the game's own command for that object: made by an Alert that is built with it
            object tmp = raising ? Activator.CreateInstance(t, new object[] { "", obj, 0 })
                                 : Activator.CreateInstance(t, new object[] { "", obj, obj, 0 });
            string cmdName = raising ? "SelectRaisingObject" : "SelectAboutObject", objName = raising ? "RaisingObject" : "AboutObject";
            var cmdF = t.GetField("<" + cmdName + ">k__BackingField", f);
            var objF = t.GetField("<" + objName + ">k__BackingField", f);
            if (cmdF == null || objF == null) throw new InvalidOperationException("Alert fields not found");
            cmdF.SetValue(alert, Get(tmp, cmdName));
            objF.SetValue(alert, obj);
        }

        bool IsEventLogWindow(object w)
        {
            try { var dc = Get(w, "DataContext"); return dc != null && dc.GetType().Name == "EventLogViewModel"; }
            catch { return false; }
        }

        /// <summary>Per Event Log window: the handler is put on once.</summary>
        void EventLogStep(object w)
        {
            object done;
            if (LogHooked.TryGetValue(w, out done) || LogOther.TryGetValue(w, out done)) return;
            var el = w as UIElement;
            if (el == null) return;
            if (!IsEventLogWindow(w))
            {
                if (Get(w, "DataContext") != null) LogOther.Add(w, w);   // it is another window: not asked again (a window without its view model yet is)
                return;
            }
            LogHooked.Add(w, this);
            LogCompactHook(w);
            el.AddHandler(Hyperlink.ClickEvent, new RoutedEventHandler(OnLogLinkClick));
            Trace("event log: clicks on links centre the map");
        }

        void OnLogLinkClick(object sender, RoutedEventArgs e)
        {
            if (_logCenter == null || !_logCenter.Value) return;
            try
            {
                var link = e.Source as Hyperlink;
                if (link == null) { Trace("event log click: source is " + (e.Source == null ? "null" : e.Source.GetType().Name)); return; }
                var alert = Get(link, "DataContext");
                if (alert == null || alert.GetType().Name != "Alert") { Trace("event log click: data context is " + (alert == null ? "null" : alert.GetType().Name)); return; }
                var cmd = link.Command;
                object obj = null;
                if (cmd != null && ReferenceEquals(cmd, Get(alert, "SelectRaisingObject"))) obj = Get(alert, "RaisingObject");
                else if (cmd != null && ReferenceEquals(cmd, Get(alert, "SelectAboutObject"))) obj = Get(alert, "AboutObject");
                if (obj == null) { Trace("event log click: the link's command is not the alert's select command"); return; }
                // the first click only selects (the game's own command); a click on the same object again centres the map on it
                var last = _lastLogObj == null ? null : _lastLogObj.Target;
                _lastLogObj = new WeakReference(obj);
                if (ReferenceEquals(last, obj)) CenterMapOn(obj);
            }
            catch (Exception ex) { Trace("event log click: " + ex.Message); }
        }

        /// <summary>The Tactical Display is put on the object's place (its track for the player's side).</summary>
        void CenterMapOn(object obj)
        {
            var asm = GameAssembly();
            var tObj = asm == null ? null : asm.GetType("SeaPower.ObjectBase");
            var tGlobals = asm == null ? null : asm.GetType("SeaPower.Globals");
            if (tObj == null || tGlobals == null) return;
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var tf = tGlobals.GetField("_playerTaskforce", all);
            var tfv = tf == null ? null : tf.GetValue(null);
            var getVeh = tObj.GetMethod("GetMapVehicle", all);
            var getPos = tObj.GetMethod("GetMapPositionForTaskforce", all);
            if (tfv == null || getVeh == null || getPos == null) return;
            if (getVeh.Invoke(obj, new[] { tfv }) == null && !GetB(obj, "IsPlayerObject")) { Trace("event log click: no track of the clicked object, the map stays"); return; }
            var pos = getPos.Invoke(obj, new object[] { tfv, Activator.CreateInstance(getPos.GetParameters()[1].ParameterType), false });
            int done = 0;
            foreach (var t in _tracked)
            {
                var w = t.Ref.Target;
                if (w == null || !GetB(w, "IsLoaded")) continue;                  // a closed window of an earlier open is not the map on the screen
                var dc = Get(w, "DataContext");
                if (dc == null || dc.GetType().Name != "SceneMapViewModel") continue;
                var map = FindMapBase(w, 0);
                if (map == null) continue;
                var locT = Prop(map, "Center").PropertyType;
                var loc = Activator.CreateInstance(locT, new[] { pos });
                var tc = Prop(map, "TargetCenter");
                if (tc != null) tc.SetValue(map, loc, null); else SetV(map, "Center", loc);
                done++;
            }
            if (done == 0) Trace("event log click: no Tactical Display window to centre");
        }
    }
}
