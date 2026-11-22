using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;

namespace AlternativeGameInterface
{
    /// <summary>
    /// ResizeCursor (1.7.0): the game draws its own cursors (Unity cursors from the [Cursors] section of the UI ini) from a Noesis cursor
    /// callback that only MouseOverManager installs, and that exists in the mission screens, not in the main menu. A window of the main menu
    /// (News) therefore shows the plain arrow on its resize edges. Here the edges' (ResizeThumb) Noesis cursor is turned into the game's
    /// cursor of that name on mouse enter / leave, in the main menu only (no main game view model).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        static readonly ConditionalWeakTable<object, object> CursorDone = new ConditionalWeakTable<object, object>();   // window -> last scan time (boxed float)

        bool InMainMenu()
        {
            try
            {
                var asm = GameAssembly();
                var g = asm == null ? null : asm.GetType("SeaPower.Globals");
                var f = g == null ? null : g.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return f != null && f.GetValue(null) == null;
            }
            catch { return false; }
        }

        /// <summary>A mission is running (the main game view model exists): its own cursor callback is the one to keep.</summary>
        static bool InMission()
        {
            try
            {
                var g = GameAssembly().GetType("SeaPower.Globals");
                var f = g == null ? null : g.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return f != null && f.GetValue(null) != null;
            }
            catch { return false; }
        }

        /// <summary>The campaign screen (map, Task Force Builder) is open: also no mission cursor callback there.</summary>
        static bool CampaignScreen()
        {
            try
            {
                var t = GameAssembly().GetType("SeapowerUI.LinearCampaignView");
                var f = t == null ? null : t.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
                return f != null && f.GetValue(null) != null;
            }
            catch { return false; }
        }

        /// <summary>Debug: the data context type, visibility, size, 3D views and name of a window, for the "window loaded" trace.</summary>
        string WindowInfo(object w)
        {
            try
            {
                var dc = Get(w, "DataContext");
                int pips = 0, budget = 800;
                var stack = new Stack<Noesis.DependencyObject>();
                stack.Push((Noesis.DependencyObject)w);
                while (stack.Count > 0 && --budget > 0)
                {
                    var e = stack.Pop();
                    if (e.GetType().Name == "Pip3DView") pips++;
                    for (int k = Noesis.VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--) { var c = Noesis.VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
                }
                return " [dc " + (dc == null ? "none" : dc.GetType().Name) + ", " + Get(w, "Visibility") + ", name '" + Get(w, "Name") + "', 3D views " + pips + ", " + Fmt(GetF(w, "ActualWidth", 0f)) + "x" + Fmt(GetF(w, "ActualHeight", 0f)) + "]";
            }
            catch (Exception e) { return " [info: " + e.Message + "]"; }
        }

        ConfigEntry<bool> _fixCursors;

        ConfigEntry<float> _fpsMax;
        ConfigEntry<bool> _fpsNone;
        ConfigEntry<bool> _vsyncAdaptive;
        ConfigEntry<bool> _vsyncItem;
        ConfigEntry<string> _vsyncMonitor;

        void BindCursorConfig()
        {
            _fpsMax = Config.Bind("General", "FrameRateOptionMax", 250f,
                new ConfigDescription("The top of the game's Frame Rate Limit slider in the video options (the game's own is 120). The limit is the game's option: set it there. " +
                "With vertical sync on the screen's refresh rate still caps it.", new AcceptableValueRange<float>(120f, 10000f)));
            _fpsNone = Config.Bind("General", "NoFrameLimit", false,
                "No frame rate limit at all (the check box under the Frame Rate Limit slider in the video options, which switches the slider off). The game's own value stays as it is and comes back when this is off.");
            _vsyncItem = Config.Bind("General", "AddAdaptiveVSync", true,
                "Adds the item \"Adaptive V-Sync\" to the V-Sync list of the game's video options: vertical sync is on while the frames keep up with the screen's refresh rate and off while they do not. " +
                "The game saves it as ordinary vertical sync, so it works without this mod too.");
            _vsyncAdaptive = Config.Bind("General", "AdaptiveVSync", false,
                "Set by the extra item \"Adaptive V-Sync\" of the V-Sync list in the game's video options (it is saved there as ordinary vertical sync, so the game works without this mod too): " +
                "vertical sync is on while the frames keep up with the screen and off while they do not.");
            _vsyncMonitor = Config.Bind("General", "AdaptiveVSyncDisplay", "Auto",
                "Adaptive V-Sync with more than one active display (Unity would wait for every monitor): the frames are synced to one monitor, " +
                "the others get them without vertical sync. Auto = the fastest monitor (the highest refresh rate); 1, 2 ... = that display (the list in the settings window shows their names, sizes and rates).");
            _fixCursors = Config.Bind("General", "FixGameResizeCursors", true,
                "Outside a mission the game shows the plain arrow on the resize edges of its windows (News, Task Force Builder): its cursors are not loaded and no cursor handler is installed. " +
                "On: the mod reads the game's cursors and installs the handler the missions use.");
        }

        static float _cbNext;
        static bool _cbOnce;

        /// <summary>Outside a mission nobody installs the Noesis cursor callback that sets the game's cursors (the main menu: none; the campaign
        /// map: an empty one). Here ours does what the mission's MouseOverManager.CursorCallback does, with the cursors of the ini read by the mod;
        /// it is put (again) about once a second while no mission is running, so the campaign's empty one does not stay.</summary>
        void ResizeCursorStep(object w, float now)
        {
            if (_fixCursors == null || !_fixCursors.Value || now < _cbNext) return;
            _cbNext = now + 1f;
            bool menu = InMainMenu(), campaign = !menu && CampaignScreen();
            if (!(menu || campaign) || InMission()) { _cbState = 0; _cbLast = null; return; }
            // put (again) when the screen changed (the campaign installs an empty callback of its own), else every ten seconds (a native call and a new delegate each time)
            int state = menu ? 1 : 2;
            if (state == _cbState && now < _cbAgain) return;
            _cbState = state; _cbAgain = now + 10f;
            try
            {
                LoadGameCursors();
                if (_gameCursors == null || _gameCursors.Count == 0) return;
                Noesis.GUI.SetCursorCallback(CursorCb);
                if (!_cbOnce) { _cbOnce = true; Trace("cursor callback installed outside a mission (main menu " + InMainMenu() + ", campaign " + CampaignScreen() + ")"); }
            }
            catch (Exception e) { if (!_cbOnce) { _cbOnce = true; Trace("cursor callback: " + e.Message); } }
        }

        int _cbState; float _cbAgain;
        static UnityEngine.Texture2D _cbLast;

        static void CursorCb(Noesis.View view, Noesis.Cursor cursor)
        {
            try
            {
                if (!CursorViewAllowed(view)) return;
                string name = cursor.Type != Noesis.CursorType.Custom ? cursor.Type.ToString() : (cursor.Filename == null ? null : cursor.Filename.OriginalString);
                KeyValuePair<UnityEngine.Texture2D, UnityEngine.Vector2> gc;
                if ((name != null && _gameCursors.TryGetValue(name.ToLowerInvariant(), out gc)) || _gameCursors.TryGetValue("default", out gc))
                {
                    if (ReferenceEquals(gc.Key, _cbLast)) return;   // the same cursor again: SetCursor is a call into the OS each time
                    _cbLast = gc.Key;
                    UnityEngine.Cursor.SetCursor(gc.Key, gc.Value, UnityEngine.CursorMode.Auto);
                }
            }
            catch { }
        }

        static Dictionary<string, KeyValuePair<UnityEngine.Texture2D, UnityEngine.Vector2>> _gameCursors;

        /// <summary>The game's own cursors ([Cursors] of Settings_UI_Tactical.ini, read the way UI_Parameters does it): the game reads them in a mission only.</summary>
        static void LoadGameCursors()
        {
            if (_gameCursors != null) return;
            _gameCursors = new Dictionary<string, KeyValuePair<UnityEngine.Texture2D, UnityEngine.Vector2>>();
            try
            {
                var asm = GameAssembly();
                var utils = asm.GetType("SeaPower.Utils");
                var ci = asm.GetType("SeaPower.CursorInfo");
                var open = utils.GetMethod("openIniFile", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }, null);
                string ui = "Default";
                try { var g = asm.GetType("SeaPower.Globals").GetProperty("UI_Folder", BindingFlags.Static | BindingFlags.Public).GetValue(null, null) as string; if (!string.IsNullOrEmpty(g)) ui = g; } catch { }
                foreach (var folder in new[] { "Default", ui })
                {
                    var ini = open.Invoke(null, new object[] { "/ui/" + folder, "/Settings_UI_Tactical.ini", false, false });
                    if (ini == null) { Trace("game cursors: /ui/" + folder + "/Settings_UI_Tactical.ini not found"); continue; }
                    var sec = ini.GetType().GetMethod("GetSectionKeyValues").Invoke(ini, new object[] { "Cursors" }) as IDictionary;
                    var close = ini.GetType().GetMethod("close", Type.EmptyTypes); if (close != null) close.Invoke(ini, null);
                    if (sec == null) continue;
                    foreach (DictionaryEntry kv in sec)
                    {
                        var info = Activator.CreateInstance(ci, new object[] { (string)kv.Value });
                        var tex = ci.GetProperty("Texture").GetValue(info, null) as UnityEngine.Texture2D;
                        var off = (UnityEngine.Vector2)ci.GetProperty("Offset").GetValue(info, null);
                        if (tex != null) _gameCursors[((string)kv.Key).ToLowerInvariant()] = new KeyValuePair<UnityEngine.Texture2D, UnityEngine.Vector2>(tex, off);
                    }
                }
                Trace("game cursors read for the menus: " + _gameCursors.Count + " (" + string.Join(", ", new List<string>(_gameCursors.Keys).ToArray()) + ")");
            }
            catch (Exception e) { var ex = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e; Trace("game cursors: " + ex.Message); }
        }

    }
}
