using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Desktops (1.7.1), desktop mode (WindowedMapAndWorldView) only: the game's "Toggle fullscreen minimap" key (Tab by default) switches between
    /// 2 - 5 desktops (DesktopCount, 2 by default): sets of window settings - position and size, open / closed, opacity / scale / outline and level
    /// of the remembered windows, and whether the World View is open. The live settings ([Layouts], [OpenWindows], WindowLooks, WindowLevels) are
    /// always those of the active desktop, so everything else works as before; the others are kept in [WindowSets] Set1 .. Set5 as text. A switch
    /// writes the live settings into the active desktop's slot, reads the next slot and applies it to the windows. An empty slot starts as the
    /// built-in arrangement of that desktop (PresetSet). The active desktop is remembered ([WindowSets] Active). The settings window has a
    /// "Desktops" tab with the saved position, size and open / closed of the windows of each desktop (edit, reset to the built-in arrangement).
    /// Record format (separated by ";;"): L|pattern|x,y,w,h[,max]   O|pattern|open / closed   K|WindowLooks   V|WindowLevels   W|open / closed (World View).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<float> _deskCount;
        ConfigEntry<int> _setActive;
        readonly ConfigEntry<string>[] _deskSlot = new ConfigEntry<string>[6];      // 1 .. 5
        ConfigEntry<string> _wvOpenLive;
        const string SetSep = ";;";
        const int MaxDesktops = 5;

        void BindWindowSetsConfig()
        {
            _deskCount = Config.Bind("General", "DesktopCount", 2f,
                "Desktop mode: the number of desktops (2 - 5). The game's Toggle fullscreen minimap key (Controls - General, Tab by default) switches between them: " +
                "each desktop keeps the position and size, open / closed, opacity, scale and level of the windows, and whether the World View is open. " +
                "The desktops start as the mod's built-in arrangements (1: the World View (3D) on top, 2: the Tactical Display (map) on top, 3: the map on the whole screen, 4: the 3D view on the whole screen, 5: the map and the 3D view side by side).");
            _setActive = Config.Bind("WindowSets", "Active", 1, "The active desktop (1 - 5); it is used again at the next start. Written by the plugin.");
            for (int i = 1; i <= MaxDesktops; i++)
                _deskSlot[i] = Config.Bind("WindowSets", "Set" + i, "", "Desktop " + i + " while it is not the active one. Written by the plugin.");
            _wvOpenLive = Config.Bind("WindowSets", "WorldViewOpen", "", "Whether the World View window was open (open / closed) on the active desktop. Written by the plugin.");
        }

        /// <summary>The number of desktops in use.</summary>
        int DeskCount() { return Math.Max(2, Math.Min(MaxDesktops, (int)Math.Round(_deskCount == null ? 2f : _deskCount.Value))); }

        /// <summary>The active desktop (1 .. DeskCount).</summary>
        int ActiveDesk() { return Math.Max(1, Math.Min(DeskCount(), _setActive.Value)); }

        // ------------------------------------------------------------ the data of a desktop

        sealed class DeskData
        {
            public readonly Dictionary<string, Layout> L = new Dictionary<string, Layout>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> O = new Dictionary<string, string>(StringComparer.Ordinal);
            public string K, V, W;                                           // looks, levels, World View open / closed (null: not kept)
            public readonly HashSet<string> Removed = new HashSet<string>(StringComparer.Ordinal);    // saved layouts forgotten by a reset

            public static DeskData Parse(string text)
            {
                var d = new DeskData();
                foreach (var rec in (text ?? "").Split(new[] { SetSep }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (rec.Length < 2 || rec[1] != '|') continue;
                    string rest = rec.Substring(2);
                    switch (rec[0])
                    {
                        case 'L':
                            {
                                int i = rest.LastIndexOf('|'); Layout l;
                                if (i > 0 && Layout.TryParse(rest.Substring(i + 1), out l)) d.L[rest.Substring(0, i)] = l;
                                break;
                            }
                        case 'O':
                            {
                                int i = rest.LastIndexOf('|');
                                if (i > 0) d.O[rest.Substring(0, i)] = rest.Substring(i + 1);
                                break;
                            }
                        case 'K': d.K = rest; break;
                        case 'V': d.V = rest; break;
                        case 'W': d.W = rest; break;
                    }
                }
                return d;
            }

            public string ToText()
            {
                var sb = new System.Text.StringBuilder();
                Action<string> add = s => { if (sb.Length > 0) sb.Append(SetSep); sb.Append(s); };
                foreach (var kv in L) add("L|" + kv.Key + "|" + kv.Value.ToString());
                foreach (var kv in O) add("O|" + kv.Key + "|" + kv.Value);
                if (K != null) add("K|" + K);
                if (V != null) add("V|" + V);
                if (W != null) add("W|" + W);
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------ the switch

        static void SwitchWindowSetNow()
        {
            var a = Active;
            if ((object)a != null) a.SwitchWindowSet();
        }

        /// <summary>The "Toggle fullscreen minimap" key in desktop mode: it switches the desktops.</summary>
        static bool WindowSetsOn()
        {
            var a = Active;
            return (object)a != null && a._windowedMap != null && a._windowedMap.Value && InMission();
        }

        void SwitchWindowSet()
        {
            try
            {
                int cur = ActiveDesk(), next = cur % DeskCount() + 1;
                _deskSlot[cur].Value = SnapshotSet();
                _setActive.Value = next;
                bool fresh = string.IsNullOrEmpty(_deskSlot[next].Value);
                ApplySet(fresh ? PresetSet(next) : _deskSlot[next].Value);      // an empty slot: the mod's built-in arrangement of that desktop
                Config.Save();
                Trace("desktop " + next + " of " + DeskCount() + (fresh ? " (the built-in arrangement, the first time)" : ""));
            }
            catch (Exception e) { Trace("SwitchWindowSet: " + e); }
        }

        // ------------------------------------------------------------ the built-in arrangements

        /// <summary>The window set an empty slot starts with: the game keeps no start positions of its windows (some are numbers in XAML, some are
        /// bindings to the screen's centre), so the mod has its own arrangements, in fractions of the interface size: 1 - the World View (3D) on
        /// the top, 2 - the Tactical Display (map) on the top, the other of the two and Unit Status under it (on an ultrawide screen: three columns
        /// of 30 / 40 / 30 % of the width), 3 - the map on the whole screen, 4 - the 3D view on the whole screen, 5 - both side by side.
        /// No look changes, no levels.</summary>
        string PresetSet(int index)
        {
            float W = 1920f, H = 1050f;
            foreach (var t in _tracked)
            {
                var w = t.Ref.Target;
                float hw, hh;
                if (w != null && t.WasActive && HostSize(w, out hw, out hh) && hw > 100f && hh > 100f) { W = hw; H = hh; break; }
            }
            Func<string, float, float, float, float, string> L = (key, x, y, w, h) =>
                "L|" + key + "|" + new Layout { X = (float)Math.Round(x * W), Y = (float)Math.Round(y * H), W = (float)Math.Round(w * W), H = (float)Math.Round(h * H) }.ToString();
            var rec = new List<string>();
            string lists = "O|Event Log|closed" + SetSep + "O|Formation Manager|closed";
            float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
            if (index <= 2 && sh > 0f && sw / sh >= 2.1f)
            {
                // an ultrawide screen: the main view (World View on desktop 1, Tactical Display on 2) in the middle, the other one at the upper left,
                // Unit Status and Unit Reference on the right, the lists at the lower left
                string main = index == 2 ? "Tactical Display" : "World View", side = index == 2 ? "World View" : "Tactical Display";
                rec.Add(L(main, 0.30f, 0f, 0.40f, 1f));
                rec.Add(L(side, 0f, 0f, 0.30f, 0.55f));
                rec.Add(L("Unit Status*", 0.70f, 0f, 0.30f, 0.52f));
                rec.Add(L("Unit Reference*", 0.70f, 0.52f, 0.30f, 0.48f));
                rec.Add(L("Event Log", 0f, 0.55f, 0.30f, 0.22f));
                rec.Add(L("Formation Manager", 0f, 0.55f, 0.30f, 0.45f));
                rec.Add("O|Tactical Display|open"); rec.Add("O|Unit Status*|open"); rec.Add(lists); rec.Add("W|open");
            }
            else if (index <= 2)
            {
                rec.Add(index == 2 ? L("Tactical Display", 0f, 0f, 1f, 0.6f) : L("World View", 0f, 0f, 1f, 0.6f));
                rec.Add(index == 2 ? L("World View", 0f, 0.6f, 0.49f, 0.4f) : L("Tactical Display", 0f, 0.6f, 0.49f, 0.4f));
                rec.Add(L("Unit Status*", 0.49f, 0.6f, 0.51f, 0.4f));
                rec.Add(L("Unit Reference*", 0.53f, 0.46f, 0.47f, 0.54f));
                rec.Add(L("Event Log", 0f, 0f, 0.28f, 0.19f));
                rec.Add(L("Formation Manager", 0.02f, 0.03f, 0.18f, 0.45f));
                rec.Add("O|Tactical Display|open"); rec.Add("O|Unit Status*|open"); rec.Add(lists); rec.Add("W|open");
            }
            else if (index == 3)
            {
                // the map on the whole screen, the 3D view closed
                rec.Add(L("Tactical Display", 0f, 0f, 1f, 1f));
                rec.Add(L("World View", 0f, 0.6f, 0.49f, 0.4f));
                rec.Add(L("Unit Status*", 0.62f, 0.58f, 0.38f, 0.42f));
                rec.Add("O|Tactical Display|open"); rec.Add("O|Unit Status*|open"); rec.Add(lists); rec.Add("W|closed");
            }
            else if (index == 4)
            {
                // the 3D view on the whole screen, the map closed
                rec.Add(L("World View", 0f, 0f, 1f, 1f));
                rec.Add(L("Tactical Display", 0f, 0.6f, 0.49f, 0.4f));
                rec.Add(L("Unit Status*", 0.62f, 0.58f, 0.38f, 0.42f));
                rec.Add("O|Tactical Display|closed"); rec.Add("O|Unit Status*|open"); rec.Add(lists); rec.Add("W|open");
            }
            else
            {
                // the map and the 3D view side by side
                rec.Add(L("Tactical Display", 0f, 0f, 0.5f, 1f));
                rec.Add(L("World View", 0.5f, 0f, 0.5f, 1f));
                rec.Add(L("Unit Status*", 0.62f, 0.58f, 0.38f, 0.42f));
                rec.Add("O|Tactical Display|open"); rec.Add("O|Unit Status*|closed"); rec.Add(lists); rec.Add("W|open");
            }
            rec.Add("K|"); rec.Add("V|");
            return string.Join(SetSep, rec.ToArray());
        }

        // ------------------------------------------------------------ snapshot

        /// <summary>The live settings (those of the active desktop) as text.</summary>
        string SnapshotSet()
        {
            var d = new DeskData();
            foreach (var kv in _saved) d.L[kv.Key] = kv.Value;
            // the windows of the mission that were never moved or closed: as they are now
            foreach (var t in _tracked)
            {
                var tw = t.Ref == null ? null : t.Ref.Target;
                if (tw == null || string.IsNullOrEmpty(t.Key) || d.L.ContainsKey(t.Key) || IsCascaded(tw)) continue;
                try { d.L[t.Key] = Read(tw); } catch { }
            }
            Layout tp;
            if (!d.L.ContainsKey(TimeKey) && TimePanelNow(out tp)) d.L[TimeKey] = tp;
            foreach (var t in _tracked)
            {
                if (string.IsNullOrEmpty(t.OsKey) || !t.OsKnown || !_osEntries.ContainsKey(t.OsKey) || d.O.ContainsKey(t.OsKey)) continue;
                var cur = _osEntries[t.OsKey].Value;
                if (string.IsNullOrEmpty(cur)) d.O[t.OsKey] = t.OsOpen ? "open" : "closed";
            }
            foreach (var kv in _osEntries)
            {
                if (string.IsNullOrEmpty(kv.Value.Value)) continue;
                d.O[kv.Key] = kv.Value.Value.Split(',')[0].Trim();
            }
            d.K = _looks.Value ?? "";
            LevelMap();
            d.V = _levels == null ? "" : (_levels.Value ?? "");
            var w = WorldViewOpenNow();
            if (w.HasValue) d.W = w.Value ? "open" : "closed";
            return d.ToText();
        }

        bool? WorldViewOpenNow()
        {
            try
            {
                var vm = MainVm();
                var v = vm == null ? null : Get(vm, "PipOpen");
                return v is bool ? (bool?)(bool)v : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------ apply

        void ApplySet(string text) { ApplySet(DeskData.Parse(text)); }

        void ApplySet(DeskData d)
        {
            float now = UnityEngine.Time.unscaledTime;

            // 1. levels (the old ones are needed to see which windows change)
            var oldLevels = new Dictionary<string, int>(LevelMap(), StringComparer.OrdinalIgnoreCase);
            if (d.V != null) { _levels.Value = d.V; _levelMap = null; }

            // 2. looks
            if (d.K != null) { _looks.Value = d.K; _lookMap = null; }

            // 3. layouts
            foreach (var kv in d.L) _saved[kv.Key] = kv.Value;
            _dirty = true; _dirtySince = now;

            // 4. windows on screen: position, look, level
            var newLevels = LevelMap();
            foreach (var t in _tracked)
            {
                var w = t.Ref.Target;
                if (w == null || t.Key == null || !t.Settled || !t.WasActive) continue;
                try
                {
                    if (!IsCascaded(w) && d.L.ContainsKey(t.Key))
                    {
                        ApplyLayout(t.Key, w);
                        t.Last = Read(w);
                    }
                    LookState ls;
                    if (Looks.TryGetValue(w, out ls) && ls.Applied)
                    {
                        var panel = ls.Panel == null ? null : ls.Panel.Target as Panel;
                        if (panel != null) { ls.Next = 0f; ApplyLook(w, panel); }
                    }
                    int a, b;
                    oldLevels.TryGetValue(t.Key, out a); newLevels.TryGetValue(t.Key, out b);
                    if (a != b) BringToFront(w, "level", false);
                }
                catch (Exception e) { Trace("desktop, '" + t.Key + "': " + e.Message); }
            }

            // 5. open / closed: each window once, now - the remembered state is replaced and the window is opened / closed through the game's own
            // switch; nothing is held or waited for afterwards (OsSoft), so a window the user closes stays closed
            foreach (var kv in d.O)
            {
                ConfigEntry<string> e;
                if (!_osEntries.TryGetValue(kv.Key, out e)) continue;
                e.Value = kv.Value;
                OsSoft = true;
                try { RestoreOpenStates(kv.Key); }
                catch (Exception ex) { Trace("desktop, open/close '" + kv.Key + "': " + ex.Message); }
                finally { OsSoft = false; }
            }

            // 6. the World View
            if (d.W != null)
            {
                _wvOpenLive.Value = d.W;
                SetWorldViewOpen(d.W == "open");
            }
        }

        void SetWorldViewOpen(bool open)
        {
            try
            {
                var vm = MainVm();
                var p = vm == null ? null : Prop(vm, "PipOpen");
                if (p == null || !p.CanWrite) return;
                if ((bool)p.GetValue(vm, null) != open) p.SetValue(vm, open, null);
            }
            catch (Exception e) { Trace("World View open/close: " + e.Message); }
        }

        // ------------------------------------------------------------ the World View's own state (live): remembered, restored at the start of a mission

        object _pfVm; bool _pfStarted;

        /// <summary>MainGameViewModel.PipOpen was set (the X button of the World View, its menu item, the key, a desktop): the state of the active desktop.</summary>
        static void MapPipOpenPostfix(bool value)
        {
            var a = Active;
            if ((object)a == null || !a._pfStarted || a._windowedMap == null || !a._windowedMap.Value) return;
            var v = value ? "open" : "closed";
            if (a._wvOpenLive.Value == v) return;
            a._wvOpenLive.Value = v;
            a.Config.Save();
        }

        /// <summary>Once per mission, as soon as the map windows are set up: the first mission with desktop mode stores the built-in arrangements and
        /// applies desktop 1, later ones put the World View as it was left.</summary>
        void WindowSetsStep()
        {
            if (_windowedMap == null || !_windowedMap.Value) { _pfStarted = false; _pfVm = null; return; }
            var vm = MainVm();
            if (vm == null || !MapMissionOn()) { _pfStarted = false; _pfVm = null; return; }
            if (!ReferenceEquals(vm, _pfVm)) { _pfVm = vm; _pfStarted = false; }     // a new mission
            if (_pfStarted || _mapWin == null || _mapWin.Target == null) return;      // (the map windows are set up first)
            _pfStarted = true;
            bool any = false;
            for (int i = 1; i <= MaxDesktops; i++) if (!string.IsNullOrEmpty(_deskSlot[i].Value)) any = true;
            if (!any)
            {
                for (int i = 1; i <= MaxDesktops; i++) _deskSlot[i].Value = PresetSet(i);
                _setActive.Value = 1;
                ApplySet(_deskSlot[1].Value);
                Config.Save();
                Trace("desktops: built-in arrangements stored, desktop 1 applied");
                return;
            }
            if (_wvOpenLive.Value == "open" || _wvOpenLive.Value == "closed") SetWorldViewOpen(_wvOpenLive.Value == "open");
        }
    }
}
