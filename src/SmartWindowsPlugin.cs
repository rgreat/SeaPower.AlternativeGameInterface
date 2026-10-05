using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SmartWindows
{
    /// <summary>
    /// Smart Windows - Sea Power window helper (SeapowerUI.Window, drawn with NoesisGUI):
    ///  - remembers position and size of chosen windows and restores them when a window is opened again;
    ///  - lets the user drag windows partly outside the visible area (left, right, bottom);
    ///  - double-clicking a window's title bar maximizes it or restores it;
    ///  - remembered windows (WindowPatterns) have no minimum size of their own (RemoveSizeLimits);
    ///  - chosen windows (RememberOpenWindows) are opened / closed after a load screen the way they were left last time;
    ///  - Unit Reference: hiding the unit tree (the '<' button) shrinks the window by the tree's width and moves it right by the
    ///    same amount, so the info panel stays where it is; showing the tree reverses that. The search / filter panel above
    ///    the tree is hidden with it, and the info panel grows upwards into the freed space.
    ///  - Unit Reference: a wider unit tree and a mouse splitter between the tree and the unit information;
    ///    the tree width is kept in the .cfg.
    ///  - Unit Reference: the country chosen in the country drop-down is kept in the .cfg (by its text) and chosen again.
    ///  - Unit Reference: whether the unit tree is shown is kept in the .cfg; a window opens with the tree hidden if it was hidden.
    ///
    /// No compile-time dependency on the game's assemblies: everything is done by reflection, and the hooks are
    /// installed only after the game's load screen has finished (SeaPower.LoadScreen.ExecuteLoadScreen completes).
    /// Saved layouts live in the plugin's .cfg file, section [Layouts].
    /// Hover a window and press AddWindowKey (default F12) to start remembering it.
    /// Debug = true in the .cfg enables verbose logging and BepInEx/SmartWindows.trace.txt.
    /// </summary>
    [BepInPlugin(PluginGuid, "Smart Windows", "1.0.0")]
    public class SmartWindowsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.rgreat.seapower.smartwindows";
        const string OldGuid = "com.rgreat.seapower.windowmemory"; // the mod was called "Window Memory" before

        struct Layout
        {
            public float X, Y, W, H;  // the normal (not maximized) position and size
            public bool Max;          // the window was maximized: it opens maximized

            static bool Near(float a, float b)
            {
                bool an = float.IsNaN(a), bn = float.IsNaN(b);
                if (an || bn) return an && bn;
                return Math.Abs(a - b) < 0.5f;
            }

            public bool Same(Layout o) { return Near(X, o.X) && Near(Y, o.Y) && Near(W, o.W) && Near(H, o.H); }

            public override string ToString()
            {
                var s = string.Join(",", new[] { F(X), F(Y), F(W), F(H) });
                return Max ? s + ",max" : s;
            }

            static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }

            public static bool TryParse(string s, out Layout l)
            {
                l = default(Layout);
                if (string.IsNullOrEmpty(s)) return false;
                var p = s.Split(',');
                if (p.Length != 4 && p.Length != 5) return false;
                var f = new float[4];
                for (int i = 0; i < 4; i++)
                    if (!float.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return false;
                l = new Layout { X = f[0], Y = f[1], W = f[2], H = f[3] };
                if (p.Length == 5)
                {
                    var m = p[4].Trim();
                    l.Max = m.Equals("max", StringComparison.OrdinalIgnoreCase) || m == "1" || m.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                return true;
            }
        }

        sealed class Tracked
        {
            public WeakReference Ref;
            public string Key, Label;
            public bool WasActive, Settled, Warned, Reported, WasMax;
            public bool OsTried, OsOpen, OsKnown;  // open/closed tracking (RememberOpenWindows)
            public string OsKey;
            public int OsTries;
            public float OsNext;
            public int Countdown = -1, Tries;
            public float NextTry;
            public Layout Last;
            public bool UrDone;          // Unit Reference set-up (size limits, tree width, splitter) done for this opening
            public int UrTries;
            public float UrNext;
        }

        /// <summary>The splitter added between the unit tree and the unit information of one Unit Reference window.</summary>
        sealed class SplitState
        {
            public object Block, Container, Thumb, Info;
        }

        /// <summary>Double-click on a window's title bar (the header Thumb): maximize / restore that window.</summary>
        sealed class HeaderHandler
        {
            public SmartWindowsPlugin Plugin;
            public WeakReference Window;
            public void DoubleClick(object sender, object e) { if (Enabled) Plugin.ToggleMaximized(Window.Target, e); }
        }

        /// <summary>Receives the Loaded event of every window (bound to Noesis' RoutedEventHandler by reflection).</summary>
        sealed class WindowLoadedHandler
        {
            public SmartWindowsPlugin Plugin;
            public void Loaded(object sender, object e) { if (Enabled) Plugin.OnWindowLoaded(sender); }
        }

        /// <summary>Receives the splitter's drag events (bound to the Noesis delegate types by reflection).</summary>
        sealed class SplitHandler
        {
            public SmartWindowsPlugin Plugin;
            public SplitState State;
            public void Delta(object sender, object e) { Plugin.SplitterDelta(State, e); }
            public void Completed(object sender, object e) { Plugin.SplitterCompleted(State); }
        }

        /// <summary>How much a window is currently narrower than its saved layout because its unit tree is hidden.</summary>
        sealed class TreeState
        {
            public float Offset;
            public object Panel;          // search / filter panel we collapsed (null: none)
            public object[] PanelOld;     // its Opacity and IsHitTestVisible before we hid it
            public readonly List<object[]> Moved = new List<object[]>(); // { element, old Grid.Row, old Grid.RowSpan }
            public object Container;      // the element holding both the panel and the tree
            public int ProbeFrames;       // > 0: check a few frames later that the panel really is gone
        }

        sealed class EarlyState
        {
            public string Key, Label;
            public bool Done, MaxOpened;
            public int Attempts;
        }

        // ------------------------------------------------------------------ state

        // The mod used to be called "Window Memory" (config com.rgreat.seapower.windowmemory.cfg): carry the settings and
        // the saved layouts over once. Runs before the BaseUnityPlugin constructor loads the new config file.
        static SmartWindowsPlugin()
        {
            try
            {
                var oldCfg = Path.Combine(Paths.ConfigPath, OldGuid + ".cfg");
                var newCfg = Path.Combine(Paths.ConfigPath, PluginGuid + ".cfg");
                if (File.Exists(oldCfg) && !File.Exists(newCfg)) File.Copy(oldCfg, newCfg);
            }
            catch { }
        }

        static SmartWindowsPlugin _inst;
        static readonly ConditionalWeakTable<object, EarlyState> EarlyStates = new ConditionalWeakTable<object, EarlyState>();
        static readonly ConditionalWeakTable<object, TreeState> TreeStates = new ConditionalWeakTable<object, TreeState>();
        static readonly ConditionalWeakTable<object, SplitState> SplitStates = new ConditionalWeakTable<object, SplitState>(); // by window
        static readonly List<WeakReference> Created = new List<WeakReference>();
        static volatile bool LoadScreenDone;
        static volatile bool ModsChanged = true;   // re-read the Mods menu tick in the next Update
        static bool Enabled;                       // ticked in the game's Mods menu (always true for a BepInEx/plugins copy)

        /// <summary>The plugin while it is enabled; null otherwise - every hook body goes through this.</summary>
        static SmartWindowsPlugin Active { get { return Enabled ? _inst : null; } }
        static bool _debug;
        static string _traceFile;

        ConfigEntry<string> _windows;
        ConfigEntry<bool> _debugEntry, _dragOutside, _improvedUI, _dblClickMax, _removeLimitsAll;
        Type _uiElementType;
        ConfigEntry<string> _openWindows;
        readonly List<string> _osPatterns = new List<string>(), _osPatternsSq = new List<string>();
        readonly Dictionary<string, ConfigEntry<string>> _osEntries = new Dictionary<string, ConfigEntry<string>>();
        static volatile int LoadScreenCount;   // finished load screens
        int _osSeenLoads;
        float _osRestoreAt = -1f, _osRecordFrom = float.MaxValue; // restore time; no recording before this (game's own start-up)
        const float OsRestoreDelay = 1f;
        ConfigEntry<float> _treeWidth;
        ConfigEntry<string> _nation;
        ConfigEntry<bool> _treeShown;
        MethodInfo _treeSetter;   // ObjectInformationViewModel.set_MuseumEntriesVisible
        object _setupWindow;      // the window being set up in its Loaded event (not tracked / not "visible" yet)
        static readonly ConditionalWeakTable<object, HeaderHandler> HeaderHooks = new ConditionalWeakTable<object, HeaderHandler>(); // by header Thumb
        WindowLoadedHandler _loadedHandler;
        // Unit Reference country drop-down (SeapowerUI.ViewModels.UnitTreeBrowserViewModel)
        FieldInfo _browserStates, _browserState, _browserInitializing;
        static int _nationBusy;      // > 0 while the game itself sets up / restores the browser
        static bool _nationRestoring; // we are choosing the saved country
        const float TreeExtraWidth = 35f, TreeMinWidth = 100f, InfoMinWidth = 150f, WindowMinWidth = 100f, WindowMinHeight = 60f;
        ConfigEntry<KeyCode> _addKey;
        ConfigEntry<float> _minVisible;

        readonly List<string> _patterns = new List<string>();
        readonly List<string> _patternsSq = new List<string>();   // same patterns, whitespace removed, for matching
        readonly Dictionary<string, Layout> _saved = new Dictionary<string, Layout>(StringComparer.Ordinal);
        readonly Dictionary<string, ConfigEntry<string>> _layoutEntries = new Dictionary<string, ConfigEntry<string>>(StringComparer.Ordinal);
        readonly List<Tracked> _tracked = new List<Tracked>();
        readonly Dictionary<string, PropertyInfo> _props = new Dictionary<string, PropertyInfo>();
        int _registered;
        bool _hooked, _hookFailed, _loadHookTried;
        Type _winType;
        MethodInfo _markUserPositioned, _getHostSize;
        FieldInfo _savedWidthField, _savedHeightField;   // Window.SavedWidth/SavedHeight: size the game returns to after un-maximizing
        bool _dirty;
        float _dirtySince;
        MethodInfo _vtChildrenCount, _vtChild, _vtParent;   // Noesis.VisualTreeHelper
        bool _vtTried;
        // more Noesis API for the filter panel
        bool _nTried, _nOk;
        Type _textBoxType, _gridType, _gridLengthType;
        object _visibilityProp, _textProp, _collapsed, _heightProp;
        readonly List<WeakReference> _probes = new List<WeakReference>(); // windows whose TreeState has ProbeFrames > 0
        MethodInfo _setValue, _clearValue, _getBinding, _getBindingBase, _setBinding, _clearBinding, _gridGetRow, _gridGetRowSpan, _gridSetRow, _gridSetRowSpan,
                   _gridGetColumn, _gridSetColumn, _gridGetColumnSpan, _gridSetColumnSpan, _parseXaml, _updateLayout;
        object _gridLengthAuto, _visible, _colWidthProp;
        Type _thumbType;

        static void Trace(string msg)
        {
            if (!_debug) return;
            try { File.AppendAllText(_traceFile, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine); }
            catch { }
        }

        void Verbose(string msg)
        {
            if (_debug) Logger.LogInfo(msg);
        }

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            _inst = this;
            Config.SaveOnConfigSet = false; // we save explicitly (debounced)
            if (Chainloader.PluginInfos.ContainsKey(OldGuid))
                Logger.LogWarning("The old 'Window Memory' plugin is still installed: delete SeaPowerWindowMemory.dll from BepInEx/plugins, two copies conflict.");

            _windows = Config.Bind("General", "WindowPatterns",
                "Tactical Display,World View,Unit Reference*,Unit Status*,Event Log,Formation Manager,FORMATION EDITOR,LinearCampaignTaskForceBuilderViewModel",
                "Comma-separated names of the windows to remember. Case-insensitive, spaces are ignored when comparing " +
                "('Unit Status*' also matches 'UnitStatusView'), '*' matches any text (also nothing). " +
                "A pattern is compared with the window's name, header text, content type and view-model type and must " +
                "match one of those strings from its beginning ('Unit Reference*' matches 'Unit Reference - XYZ'; " +
                "use '*text*' to match anywhere). Windows matching the same pattern share one saved layout.");
            _addKey = Config.Bind("General", "AddWindowKey", KeyCode.F12,
                "Hover the mouse over a window and press this key to add that window to WindowPatterns.");
            _dragOutside = Config.Bind("General", "AllowDragOutside", true,
                "Allow dragging any window partly outside the visible area (left, right and bottom edge).");
            _minVisible = Config.Bind("General", "MinVisiblePixels", 25f,
                "With AllowDragOutside: at least this many pixels of the window's width stay visible at the left/right edge. " +
                "At the bottom edge the window's title bar always stays visible.");
            _openWindows = Config.Bind("General", "RememberOpenWindows", "Tactical Display,Unit Status*,Event Log,Formation Manager",
                "Comma-separated window patterns (same rules as WindowPatterns) whose open / closed state is remembered: after a " +
                "load screen (mission start, encyclopedia) these windows are opened or closed as they were left. The state is kept " +
                "in [OpenWindows]. Only windows the game has already created can be opened (e.g. Unit Status needs a selected unit).");
            _removeLimitsAll = Config.Bind("General", "RemoveSizeLimits", true,
                "Windows in WindowPatterns (Unit Reference included) have no minimum / maximum size of their own: they can be " +
                "resized down to " + WindowMinWidth + "x" + WindowMinHeight + " px.");
            _dblClickMax = Config.Bind("General", "MaximizeOnDoubleClick", true,
                "Double-click a window's title bar to maximize it to the full screen, double-click again to restore it " +
                "(windows that can be resized). A window closed maximized opens maximized (',max' after its layout in [Layouts]).");
            _improvedUI = BindRenamed("General", "ImprovedUI", "ImprovedUnitReferenceUI", true,
                "Unit Reference window improvements: " +
                "hiding the unit tree (the '<' button at the left edge) also hides the search / filter panel, makes the window " +
                "narrower by the tree's width and moves it right by the same amount, so the unit information stays in place and " +
                "takes the freed space at the top (showing the tree reverses it; saved layouts store the window with the tree shown); " +
                "a wider unit tree with a splitter at its right edge that can be dragged with the mouse (width kept in [UnitReference] TreeWidth); " +
                "the chosen country is remembered ([UnitReference] Country).");
            _treeWidth = Config.Bind("UnitReference", "TreeWidth", 0f,
                "Width of the unit tree in pixels (at least " + TreeMinWidth + "). Written by the plugin when the splitter is dragged; " +
                "0 = the game's width + " + TreeExtraWidth + " px.");
            _treeShown = Config.Bind("UnitReference", "TreeVisible", true,
                "Whether the unit tree of the Unit Reference window is shown. Written by the plugin when the tree is shown or " +
                "hidden with the '<' / '>' button; a new window opens the same way.");
            _nation = Config.Bind("UnitReference", "Country", "",
                "Country chosen in the Unit Reference window's country drop-down, by its text (as shown in the list). " +
                "Written by the plugin when a country is chosen; empty = the game's default.");
            _debugEntry = Config.Bind("General", "Debug", false,
                "Enables verbose logging and BepInEx/SmartWindows.trace.txt.");
            _debug = _debugEntry.Value;

            _traceFile = Path.Combine(Paths.BepInExRootPath, "SmartWindows.trace.txt");
            if (_debug) { try { File.WriteAllText(_traceFile, ""); } catch { } }

            foreach (var n in _windows.Value.Split(','))
            {
                var t = n.Trim();
                if (t.Length == 0) continue;
                _patterns.Add(t);
                _patternsSq.Add(Squash(t));
            }

            foreach (var n in _openWindows.Value.Split(','))
            {
                var t = n.Trim();
                if (t.Length == 0) continue;
                _osPatterns.Add(t);
                _osPatternsSq.Add(Squash(t));
                _osEntries[t] = Config.Bind("OpenWindows", t, "", "open / closed - written by the plugin (RememberOpenWindows).");
            }

            // one config entry per pattern holds that window's saved layout: x,y,w,h
            foreach (var pat in _patterns)
            {
                try
                {
                    var e = BindLayout(pat);
                    Layout l;
                    if (Layout.TryParse(e.Value, out l)) _saved[pat] = l;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Pattern '" + pat + "' cannot be used as a config key (" + ex.Message + "); its layout will not be saved.");
                }
            }

            DropRetiredEntries();
            Config.Save();
            Logger.LogInfo("Smart Windows 1.0.0 loaded; hooks are installed when the load screen finishes" +
                           (ModState.Controlled ? " (follows its tick in the game's Mods menu)." : "."));
        }

        static Dictionary<ConfigDefinition, string> Orphans(ConfigFile cfg)
        {
            var prop = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop == null ? null : prop.GetValue(cfg, null) as Dictionary<ConfigDefinition, string>;
        }

        /// <summary>
        /// Binds a setting that was renamed: if the .cfg still has it under the old key (and not yet under the new one), its value
        /// moves to the new key and the old key is dropped from the file.
        /// </summary>
        ConfigEntry<bool> BindRenamed(string section, string oldKey, string newKey, bool defaultValue, string description)
        {
            Dictionary<ConfigDefinition, string> orphans = null;
            try { orphans = Orphans(Config); } catch (Exception e) { Trace("Orphans: " + e.Message); }
            var oldDef = new ConfigDefinition(section, oldKey);
            var newDef = new ConfigDefinition(section, newKey);
            string oldValue = null;
            bool hadNew = orphans != null && orphans.ContainsKey(newDef);
            if (orphans != null) orphans.TryGetValue(oldDef, out oldValue);

            var entry = Config.Bind(section, newKey, defaultValue, description);
            bool v;
            if (!hadNew && oldValue != null && bool.TryParse(oldValue.Trim(), out v))
            {
                entry.Value = v;
                Logger.LogInfo("Setting " + oldKey + " renamed to " + newKey + " (value " + v + " kept).");
            }
            if (orphans != null) orphans.Remove(oldDef);
            return entry;
        }

        /// <summary>Settings replaced by ImprovedUnitReferenceUI: removed from the .cfg (BepInEx would otherwise keep them as orphans).</summary>
        void DropRetiredEntries()
        {
            try
            {
                var orphans = Orphans(Config);
                if (orphans == null) return;
                foreach (var d in new[] { new ConfigDefinition("General", "ResizeOnTreeToggle"), new ConfigDefinition("General", "HideFilterWithTree"),
                                          new ConfigDefinition("UnitReference", "RemoveSizeLimits"), new ConfigDefinition("UnitReference", "TreeSplitter") })
                    orphans.Remove(d);
            }
            catch (Exception e) { Trace("DropRetiredEntries: " + e.Message); }
        }

        ConfigEntry<string> BindLayout(string pattern)
        {
            var e = Config.Bind("Layouts", pattern, "", "Saved layout x,y,w,h[,max] - written by the plugin ('max': the window was maximized), clear the value to reset this window.");
            _layoutEntries[pattern] = e;
            return e;
        }

        // ------------------------------------------------------- hook installation

        static Assembly GameAssembly()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Seapower-Scripts") return a;
            return null;
        }

        /// <summary>
        /// Patches the iterator behind SeaPower.LoadScreen.ExecuteLoadScreen so we learn when it has finished
        /// (MoveNext returns false). That is the point where the game's UI is fully up.
        /// </summary>
        void TryInstallLoadScreenHook()
        {
            var asm = GameAssembly();
            if (asm == null) return; // not loaded yet; try again next frame

            _loadHookTried = true;
            try
            {
                var ls = asm.GetType("SeaPower.LoadScreen");
                MethodInfo moveNext = null;
                if (ls != null)
                    foreach (var nt in ls.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        if (nt.Name.StartsWith("<ExecuteLoadScreen>", StringComparison.Ordinal))
                        {
                            moveNext = nt.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            break;
                        }
                if (moveNext == null)
                {
                    Logger.LogWarning("LoadScreen.ExecuteLoadScreen not found (game updated?); the plugin will not work.");
                    return;
                }

                var lsHarmony = new Harmony(PluginGuid + ".loadscreen");
                lsHarmony.Patch(moveNext, postfix: Hm("LoadScreenPostfix"));
                Trace("patched " + moveNext.DeclaringType.Name + ".MoveNext");

                // the Mods menu saves its list here (Apply): lets the plugin follow its tick without a restart
                var fm = asm.GetType("SeaPower.FileManager");
                var save = fm == null ? null : fm.GetMethod("SaveDirectories", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (save != null) { lsHarmony.Patch(save, postfix: Hm("ModsSavedPostfix")); Trace("patched FileManager.SaveDirectories"); }
                else Trace("FileManager.SaveDirectories not found: the Mods menu tick is re-read after load screens only");
            }
            catch (Exception e) { Logger.LogWarning("Load-screen hook failed: " + e.Message); Trace("TryInstallLoadScreenHook: " + e); }
        }

        // MoveNext() returns false when the coroutine is finished
        static void LoadScreenPostfix(bool __result)
        {
            if (!__result) { LoadScreenDone = true; ModsChanged = true; LoadScreenCount++; }
        }

        // the game has just written the Mods menu's list (Apply): re-read our tick
        static void ModsSavedPostfix()
        {
            ModsChanged = true;
        }

        void RefreshEnabled()
        {
            ModsChanged = false;
            bool? state = ModState.Read();
            if (state == null) Logger.LogWarning("Could not read the game's mod list; Smart Windows stays on.");
            bool now = state != false;
            if (now == Enabled) return;
            Enabled = now;
            if (ModState.Controlled)
                Logger.LogInfo(now ? "Enabled in the game's Mods menu (" + ModState.ModId + ")." : "Disabled in the game's Mods menu (" + ModState.ModId + "); idle.");
        }

        static HarmonyMethod Hm(string name)
        {
            return new HarmonyMethod(typeof(SmartWindowsPlugin).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        }

        void TryHook()
        {
            _hooked = true; // one attempt only
            try
            {
                var asm = GameAssembly();
                if (asm == null) { Logger.LogWarning("Seapower-Scripts assembly not found."); _hookFailed = true; return; }

                _winType = asm.GetType("SeapowerUI.Window");
                if (_winType == null) { Logger.LogWarning("Type SeapowerUI.Window not found."); _hookFailed = true; return; }

                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _markUserPositioned = _winType.GetMethod("MarkUserPositioned", inst);
                _getHostSize = _winType.GetMethod("GetHostSize", inst);
                _savedWidthField = _winType.GetField("SavedWidth", inst);
                _savedHeightField = _winType.GetField("SavedHeight", inst);

                var ctor = _winType.GetConstructor(inst, null, Type.EmptyTypes, null);
                if (ctor == null) { Logger.LogWarning("SeapowerUI.Window constructor not found."); _hookFailed = true; return; }

                var harmony = new Harmony(PluginGuid);
                harmony.Patch(ctor, postfix: Hm("OnWindowCreated"));

                // Restore a window right after the game initialises / loads / anchors it, i.e. before the first rendered frame.
                PatchPost(harmony, _winType, "OnInitialized", "EarlyPostfix");
                // a window saved maximized is made Maximized before OnInitialized: the game then puts it into its maximized
                // visual state right away (as for a window that is already maximized), without the maximize animation
                try
                {
                    var init = _winType.GetMethod("OnInitialized", inst | BindingFlags.DeclaredOnly);
                    if (init != null) harmony.Patch(init, prefix: Hm("OpenMaximizedPrefix"));
                }
                catch (Exception e) { Trace("patch OnInitialized prefix: " + e.Message); }
                PatchPost(harmony, _winType, "ApplyScreenAnchor", "EarlyPostfix");
                var aip = asm.GetType("SeapowerUI.AbsoluteInitialPosition");
                if (aip != null) PatchPost(harmony, aip, "AssociatedObject_Loaded", "BehaviorPostfix"); // sets Y itself: re-apply on top

                // Maximize/restore (Tab toggles the map/world view full screen): Kfa_Completed stores the current size as
                // SavedWidth/SavedHeight, Restore_Completed applies it again - for a window opened maximized that is the fixed
                // XAML size 400x400. Put our size into the stored fields and our layout over the restored window.
                PatchPost(harmony, _winType, "Kfa_Completed", "MaximizedPostfix");
                // every change of Window.Maximized (button, double-click, Tab) passes this static property-changed callback
                PatchPost(harmony, _winType, "MaximizeChanged", "MaximizeChangedPostfix");
                PatchPost(harmony, _winType, "Restore_Completed", "RestoredPostfix");
                PatchPost(harmony, _winType, "CommandUnmaximizeWindow", "UnmaximizeCommandPostfix");

                // The game keeps a dragged window completely inside the viewport (HeaderDragDelta): relax that.
                if (_dragOutside.Value)
                {
                    var drag = _winType.GetMethod("HeaderDragDelta", inst | BindingFlags.DeclaredOnly);
                    if (drag != null) { harmony.Patch(drag, prefix: Hm("DragPrefix"), postfix: Hm("DragPostfix")); Trace("patched Window.HeaderDragDelta"); }
                    else Logger.LogWarning("Window.HeaderDragDelta not found (game updated?); dragging outside the viewport is not available.");
                }

                // Unit Reference: the tree toggle button flips ObjectInformationViewModel.MuseumEntriesVisible (XAML collapses the tree)
                if (_improvedUI.Value)
                {
                    var oivm = asm.GetType("SeapowerUI.ViewModels.ObjectInformationViewModel");
                    var vis = oivm == null ? null : oivm.GetProperty("MuseumEntriesVisible", inst);
                    var setter = vis == null ? null : vis.GetSetMethod(true);
                    if (setter != null)
                    {
                        harmony.Patch(setter, prefix: Hm("TreeTogglePrefix"), postfix: Hm("TreeTogglePostfix"));
                        _treeSetter = setter;
                        Trace("patched ObjectInformationViewModel.set_MuseumEntriesVisible");
                    }
                    else Logger.LogWarning("ObjectInformationViewModel.MuseumEntriesVisible not found (game updated?); ImprovedUnitReferenceUI is not available.");

                    HookCountry(harmony, asm);
                }

                Logger.LogInfo("Window hooks installed.");
            }
            catch (Exception e)
            {
                _hookFailed = true;
                Logger.LogError("Could not hook SeapowerUI.Window: " + e.Message);
                Trace("TryHook exception: " + e);
            }
        }

        static void PatchPost(Harmony harmony, Type type, string method, string postfixName)
        {
            try
            {
                var m = type.GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (m == null) { Trace("method not found: " + type.Name + "." + method); return; }
                harmony.Patch(m, postfix: Hm(postfixName));
                Trace("patched " + type.Name + "." + method);
            }
            catch (Exception e) { Trace("patch failed " + type.Name + "." + method + ": " + e.Message); }
        }

        // ------------------------------------------------------------------ hook bodies

        static void OnWindowCreated(object __instance)
        {
            if (__instance == null) return;
            Created.Add(new WeakReference(__instance)); // also while disabled: cheap, ready when enabled
            if ((object)Active != null && (Active._improvedUI.Value || Active._dblClickMax.Value || Active._removeLimitsAll.Value)) Active.HookLoaded(__instance);
        }

        static void MaximizeChangedPostfix(object d)
        {
            if ((object)Active != null) Active.OnMaximizedChanged(d);
        }

        static void OpenMaximizedPrefix(object __instance)
        {
            if ((object)Active != null) Active.OpenMaximized(__instance);
        }

        static void EarlyPostfix(object __instance)
        {
            if ((object)Active != null) Active.Early(__instance, false, "early");
        }

        static void BehaviorPostfix(object __instance)
        {
            if ((object)Active == null || __instance == null) return;
            try
            {
                var p = __instance.GetType().GetProperty("AssociatedObject",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                Active.Early(p == null ? null : p.GetValue(__instance, null), true, "early");
            }
            catch (Exception e) { Trace("BehaviorPostfix: " + e.Message); }
        }

        static void MaximizedPostfix(object __instance)
        {
            if ((object)Active != null) Active.AfterMaximize(__instance);
        }

        // the game's own "restore" command (button / key)
        static void UnmaximizeCommandPostfix(object __instance)
        {
            if ((object)Active != null) Active.ClearMaximized(__instance, "restore command");
        }

        static void RestoredPostfix(object __instance)
        {
            if ((object)Active != null) Active.ClearMaximized(__instance, "restore animation");
            if ((object)Active != null) Active.Early(__instance, true, "after un-maximize");
        }

        static void DragPrefix(object __instance, out float[] __state)
        {
            __state = (object)Active == null ? null : new[] { Active.GetF(__instance, "X", 0f), Active.GetF(__instance, "Y", 0f) };
        }

        // runs after the game's own (clamping) drag handling: redo the move with our looser limits
        static void DragPostfix(object __instance, object e, float[] __state)
        {
            if ((object)Active != null && __state != null) Active.AfterDrag(__instance, e, __state);
        }

        static void TreeTogglePrefix(object __instance, out bool __state)
        {
            __state = (object)Active != null && Active.GetB(__instance, "MuseumEntriesVisible");
        }

        // runs after the binding has collapsed / shown the tree, but before the next layout pass: no visible jump
        static void TreeTogglePostfix(object __instance, bool value, bool __state)
        {
            if ((object)Active != null && value != __state) Active.OnTreeToggled(__instance, value);
        }

        static void NationBusyPrefix()
        {
            _nationBusy++;
        }

        // after the game has (re)built the country list / restored its session state: choose the saved country
        // (a finalizer: runs even if the game's method throws, so the busy counter cannot get stuck)
        static void NationBusyFinalizer(object __instance)
        {
            if (--_nationBusy < 0) _nationBusy = 0;
            if (_nationBusy == 0 && (object)Active != null) Active.RestoreCountry(__instance);
        }

        static void NationSetPostfix(object __instance, object value)
        {
            if (_nationBusy == 0 && !_nationRestoring && (object)Active != null) Active.SaveCountry(__instance, value);
        }

        // ---------------------------------------------------------------- restoring

        /// <summary>Applies the saved layout to a freshly created / shown window, as early as possible.</summary>
        void Early(object w, bool force, string why)
        {
            if (w == null || _hookFailed) return;
            try
            {
                var st = EarlyStates.GetOrCreateValue(w);
                if (st.Done && !force) return;
                if (st.Key == null)
                {
                    if (!force && ++st.Attempts > 30) { st.Done = true; return; } // header/content not available yet: the Update loop handles it
                    string label;
                    st.Key = MatchPattern(w, out label);
                    st.Label = label;
                    if (st.Key == null) return;
                }
                if (ApplyLayout(st.Key, w)) Verbose("Restored " + why + " " + st.Label);
                st.Done = true;
            }
            catch (Exception e) { Trace("Early: " + e); }
        }

        /// <summary>
        /// A window saved maximized opens maximized; its return size is the saved normal size. Called before OnInitialized (the
        /// game then shows it maximized without the animation) and again on Loaded for windows whose content arrives later
        /// (Unit Reference): those are recognised only then and maximize with the game's usual animation.
        /// </summary>
        void OpenMaximized(object w, string when = "on initialise")
        {
            if (w == null || _hookFailed || !_dblClickMax.Value) return;
            try
            {
                var st = EarlyStates.GetOrCreateValue(w);
                if (st.Key == null)
                {
                    string label;
                    st.Key = MatchPattern(w, out label);
                    st.Label = label;
                }
                if (st.Key == null) { Trace("OpenMaximized " + when + ": window not recognised yet"); return; }
                Layout s;
                if (!_saved.TryGetValue(st.Key, out s) || !s.Max) return;
                if (!GetB(w, "CanResize") || GetB(w, "Maximized")) return;
                if (st.MaxOpened) return; // once per window: a later restore by the user stays restored
                st.MaxOpened = true;
                if (!float.IsNaN(s.W) && s.W >= 20f && _savedWidthField != null) _savedWidthField.SetValue(w, Math.Max(20f, s.W - TreeOffset(w)));
                if (!float.IsNaN(s.H) && s.H >= 20f && _savedHeightField != null) _savedHeightField.SetValue(w, s.H);
                SetF(w, "X", s.X);
                SetF(w, "Y", s.Y);
                SetV(w, "Maximized", true);
                Verbose("Opened maximized " + st.Label + " (" + when + ")");
            }
            catch (Exception e) { Trace("OpenMaximized: " + e); }
        }

        /// <summary>
        /// A window's Maximized changed: a maximize marks its saved layout with ',max'. A change back is NOT taken as a restore
        /// here - the game also drops Maximized when it closes / re-creates a hosted window (Unit Reference) - the mark is
        /// removed only by a restore the user made (ClearMaximized: double-click, restore command, restore animation).
        /// </summary>
        void OnMaximizedChanged(object w)
        {
            if (w == null || _hookFailed || !_dblClickMax.Value) return;
            try
            {
                if (_winType == null || !_winType.IsInstanceOfType(w)) return;
                var st = EarlyStates.GetOrCreateValue(w);
                if (st.Key == null)
                {
                    string label;
                    st.Key = MatchPattern(w, out label);
                    st.Label = label;
                }
                Layout sv;
                if (st.Key == null || !_saved.TryGetValue(st.Key, out sv)) return; // no normal layout to keep yet
                bool max = GetB(w, "Maximized");
                if (!max) { Trace("'" + st.Key + "' Maximized dropped (not by the user's restore): ',max' kept"); return; }
                if (sv.Max) return;
                sv.Max = true;
                _saved[st.Key] = sv;
                _dirty = true;
                _dirtySince = Time.unscaledTime;
                Verbose("'" + st.Key + "' maximized - saved as ',max'");
            }
            catch (Exception e) { Trace("OnMaximizedChanged: " + e.Message); }
        }

        /// <summary>The user restored a maximized window: it no longer opens maximized.</summary>
        void ClearMaximized(object w, string why)
        {
            if (w == null || _hookFailed || !_dblClickMax.Value) return;
            try
            {
                var st = EarlyStates.GetOrCreateValue(w);
                if (st.Key == null)
                {
                    string label;
                    st.Key = MatchPattern(w, out label);
                    st.Label = label;
                }
                Layout s;
                if (st.Key == null || !_saved.TryGetValue(st.Key, out s) || !s.Max) return;
                s.Max = false;
                _saved[st.Key] = s;
                _dirty = true;
                _dirtySince = Time.unscaledTime;
                Verbose("'" + st.Key + "' restored (" + why + ") - ',max' removed");
            }
            catch (Exception e) { Trace("ClearMaximized: " + e.Message); }
        }

        /// <summary>The maximize animation finished and the game stored the current size as the size to return to: make it ours.</summary>
        void AfterMaximize(object w)
        {
            if (w == null || _hookFailed) return;
            try
            {
                var st = EarlyStates.GetOrCreateValue(w);
                if (st.Key == null)
                {
                    string label;
                    st.Key = MatchPattern(w, out label);
                    st.Label = label;
                }
                Layout s;
                if (st.Key == null || !_saved.TryGetValue(st.Key, out s)) return;
                if (float.IsNaN(s.W) || float.IsNaN(s.H) || s.W < 20f || s.H < 20f) return;
                float rw = Math.Max(20f, s.W - TreeOffset(w)); // saved layouts are "tree shown": a hidden tree makes it narrower
                if (_savedWidthField != null) _savedWidthField.SetValue(w, rw);
                if (_savedHeightField != null) _savedHeightField.SetValue(w, s.H);
                Verbose("Return size of " + st.Label + " set to " + rw.ToString(CultureInfo.InvariantCulture) + "x" + s.H.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e) { Trace("AfterMaximize: " + e); }
        }

        /// <summary>Current layout, expressed as if the unit tree were shown (that is what gets saved).</summary>
        Layout Read(object w)
        {
            var l = new Layout { X = GetF(w, "X", 0f), Y = GetF(w, "Y", 0f), W = GetF(w, "Width", float.NaN), H = GetF(w, "Height", float.NaN) };
            float off = TreeOffset(w);
            if (off > 0f)
            {
                l.X -= off;
                if (!float.IsNaN(l.W)) l.W += off;
            }
            return l;
        }

        static float TreeOffset(object w)
        {
            TreeState st;
            return w != null && TreeStates.TryGetValue(w, out st) ? st.Offset : 0f;
        }

        bool ApplyLayout(string key, object w)
        {
            Layout s;
            if (!_saved.TryGetValue(key, out s)) return false;
            if (GetB(w, "Maximized")) return false;

            float off = TreeOffset(w); // the saved layout is with the tree shown: if it is hidden now, apply the narrow form
            if (off > 0f)
            {
                s.X += off;
                if (!float.IsNaN(s.W)) s.W -= off;
            }

            float x = s.X, y = s.Y;
            ClampToViewport(w, ref x, ref y, float.IsNaN(s.W) ? GetF(w, "ActualWidth", 0f) : s.W);

            if (!float.IsNaN(s.W) && s.W > 20f) SetF(w, "Width", s.W);
            if (!float.IsNaN(s.H) && s.H > 20f) SetF(w, "Height", s.H);
            SetF(w, "X", x);
            SetF(w, "Y", y);
            try { if (_markUserPositioned != null) _markUserPositioned.Invoke(w, null); } catch { } // stop edge-anchor logic overriding us
            return true;
        }

        // ---------------------------------------------------------------- Unit Reference tree toggle

        void OnTreeToggled(object vm, bool treeVisible)
        {
            if (_hookFailed) return;
            try
            {
                object win, tree;
                if (!FindTree(vm, out win, out tree)) { Trace("tree toggle: window not found"); return; }
                var st = TreeStates.GetOrCreateValue(win);

                // remember the choice for the next window (only toggles of a window on screen get here, not the constructor's)
                if (_treeShown.Value != treeVisible)
                {
                    _treeShown.Value = treeVisible;
                    Config.Save();
                }

                SplitState sp;
                if (SplitStates.TryGetValue(win, out sp) && sp.Thumb != null)
                    SetV(sp.Thumb, "Visibility", treeVisible ? _visible : _collapsed);
                ResizeForTree(win, tree, st, treeVisible);
                if (treeVisible) ShowFilterPanel(st);
                else HideFilterPanel(win, tree, vm, st);
            }
            catch (Exception e) { Trace("OnTreeToggled: " + e); }
        }

        void ResizeForTree(object win, object tree, TreeState st, bool treeVisible)
        {
            if (GetB(win, "Maximized"))
            {
                // nothing to resize now; remember how much narrower the window is to be once it is restored
                // (ApplyLayout after Restore_Completed and the return size set on maximize both read this)
                if (treeVisible) st.Offset = 0f;
                else if (st.Offset <= 0f) st.Offset = TreeBlockWidth(win, tree);
                if (_savedWidthField != null)
                {
                    var key = EarlyStates.GetOrCreateValue(win).Key;
                    Layout s;
                    if (key != null && _saved.TryGetValue(key, out s) && !float.IsNaN(s.W) && s.W >= 20f)
                        _savedWidthField.SetValue(win, Math.Max(20f, s.W - st.Offset));
                }
                Verbose("Unit tree " + (treeVisible ? "shown" : "hidden") + " while maximized: width after restore adjusted");
                return;
            }

            float width = GetF(win, "Width", float.NaN);
            if (float.IsNaN(width) || width <= 0f) width = GetF(win, "ActualWidth", 0f);
            if (width <= 0f) return;
            float x = GetF(win, "X", 0f), y = GetF(win, "Y", 0f);

            if (!treeVisible)
            {
                if (st.Offset > 0f) return; // already narrowed
                float tw = TreeBlockWidth(win, tree);
                float newW = Math.Max(Math.Max(GetF(win, "MinWidth", 0f), 20f), width - tw);
                float d = width - newW;
                if (d < 1f) return;
                SetF(win, "Width", newW);
                SetF(win, "X", x + d);
                st.Offset = d;
                Verbose("Unit tree hidden: window narrowed by " + d.ToString("0", CultureInfo.InvariantCulture) + " px");
            }
            else
            {
                float d = st.Offset;
                st.Offset = 0f;
                if (d <= 0f) return; // we did not narrow it (e.g. it was maximized then)
                float newW = width + d;
                x -= d;
                ClampToViewport(win, ref x, ref y, newW);
                SetF(win, "Width", newW);
                SetF(win, "X", x);
                SetF(win, "Y", y);
                Verbose("Unit tree shown: window widened by " + d.ToString("0", CultureInfo.InvariantCulture) + " px");
            }
            try { if (_markUserPositioned != null) _markUserPositioned.Invoke(win, null); } catch { }
        }

        // ---------------------------------------------------------------- Unit Reference search / filter panel

        /// <summary>
        /// Collapses the search / filter panel of the unit tree. The panel is found from its search box (a TextBox bound to
        /// UnitTreeBrowserViewModel.SearchText): going up from it, the panel is the child of the first element that also contains
        /// the tree. If the panel sits in Grid rows of a fixed size, those rows get height 0 so the content below moves up.
        /// </summary>
        void HideFilterPanel(object win, object tree, object vm, TreeState st)
        {
            if (st.Panel != null || !InitNoesis()) return;

            var browser = Get(vm, "Browser");
            var anchor = FindFilterAnchor(win, tree, browser);
            if (anchor == null) { Trace("filter panel: search box not found"); return; }

            var treeLine = new List<object>();
            for (var e = tree; e != null; e = VtParent(e)) { treeLine.Add(e); if (e.Equals(win)) break; }

            object panel = null, container = null, prev = anchor;
            var chain = new StringBuilder(Describe(anchor));
            for (var p = VtParent(anchor); p != null; prev = p, p = VtParent(p))
            {
                chain.Append(" < ").Append(Describe(p));
                if (treeLine.Contains(p)) { container = p; panel = prev; break; }
                if (p.Equals(win)) break;
            }
            Trace("filter panel path: " + chain);
            if (panel == null || panel.Equals(anchor) && container == null) { Trace("filter panel: no common parent with the tree"); return; }

            var toggle = Get(vm, "ToggleTreeCommand");
            if (toggle != null && ContainsCommand(panel, toggle))
            {
                Trace("filter panel " + TypeName(panel) + " also holds the tree button: not hidden");
                return;
            }

            // The panel's Visibility is held by the view (it reads back as Visible whatever is set, even with its binding removed)
            // and its grid row has a fixed height. So: make the panel invisible and let the content of the row below span up over
            // the panel's row(s).
            st.PanelOld = new[] { Get(panel, "Opacity"), Get(panel, "IsHitTestVisible") };
            SetV(panel, "Opacity", 0f);
            SetV(panel, "IsHitTestVisible", false);
            st.Panel = panel;
            st.Container = container;
            if (_gridType.IsInstanceOfType(container)) SpanOverPanel(container, panel, st);
            Trace("filter panel hidden: Opacity=" + TextOf(Get(panel, "Opacity")) + ", " + st.Moved.Count + " element(s) moved up");
            st.ProbeFrames = 3;
            _probes.Add(new WeakReference(win));
            Verbose("Unit tree hidden: filter panel hidden, " + st.Moved.Count + " element(s) moved up");
        }

        /// <summary>Elements in the grid row right below the panel start at the panel's row instead and span over it.</summary>
        void SpanOverPanel(object grid, object panel, TreeState st)
        {
            var r0 = Invoke(_gridGetRow, null, panel);
            var s0 = Invoke(_gridGetRowSpan, null, panel);
            if (r0 == Failed || s0 == Failed) return;
            int r = (int)r0, span = Math.Max(1, (int)s0), below = r + span;
            for (int k = VtChildrenCount(grid) - 1; k >= 0; k--)
            {
                var c = VtChild(grid, k);
                if (c == null || c.Equals(panel)) continue;
                var cr = Invoke(_gridGetRow, null, c);
                var cs = Invoke(_gridGetRowSpan, null, c);
                if (cr == Failed || cs == Failed || (int)cr != below) continue;
                st.Moved.Add(new[] { c, cr, cs });
                Invoke(_gridSetRow, null, c, r);
                Invoke(_gridSetRowSpan, null, c, (int)cs + span);
            }
        }

        void ShowFilterPanel(TreeState st)
        {
            if (st == null || st.Panel == null) return;
            var panel = st.Panel;
            st.Panel = null;
            try
            {
                for (int i = st.Moved.Count - 1; i >= 0; i--)
                {
                    var m = st.Moved[i];
                    Invoke(_gridSetRowSpan, null, m[0], m[2]);
                    Invoke(_gridSetRow, null, m[0], m[1]);
                }
                var old = st.PanelOld;
                if (old != null)
                {
                    if (old[0] != null) SetV(panel, "Opacity", old[0]);
                    if (old[1] != null) SetV(panel, "IsHitTestVisible", old[1]);
                }
                Verbose("Unit tree shown: filter panel restored");
            }
            catch (Exception e) { Trace("ShowFilterPanel: " + e); }
            st.PanelOld = null;
            st.Moved.Clear();
            st.ProbeFrames = 0;
            st.Container = null;
        }

        /// <summary>A few frames after collapsing: log what the layout made of it; if the panel is still shown, squeeze it to height 0.</summary>
        void RunProbes()
        {
            for (int i = _probes.Count - 1; i >= 0; i--)
            {
                var w = _probes[i].Target;
                TreeState st;
                if (w == null || !TreeStates.TryGetValue(w, out st) || st.Panel == null || st.ProbeFrames <= 0) { _probes.RemoveAt(i); continue; }
                if (--st.ProbeFrames > 0) continue;
                _probes.RemoveAt(i);
                try
                {
                    var p = st.Panel;
                    float op = GetF(p, "Opacity", -1f);
                    Trace("filter panel probe: Opacity=" + op.ToString(CultureInfo.InvariantCulture));
                    if (st.Container != null) DumpChildren(st.Container);
                    if (op > 0.01f) Logger.LogWarning("Smart Windows: could not hide the filter panel of the Unit Reference window (set Debug = true and look at SmartWindows.trace.txt).");
                }
                catch (Exception e) { Trace("RunProbes: " + e); }
            }
        }

        void DumpChildren(object container)
        {
            Trace("  container " + Describe(container) + " ActualHeight=" + GetF(container, "ActualHeight", -1f).ToString(CultureInfo.InvariantCulture));
            int n = VtChildrenCount(container);
            for (int k = 0; k < n; k++)
            {
                var c = VtChild(container, k);
                if (c == null) continue;
                var m = Get(c, "Margin");
                var sp = Invoke(_gridGetRowSpan, null, c);
                Trace("    child " + k + ": " + Describe(c) + (sp == Failed ? "" : "/span" + sp) + " Visibility=" + TextOf(Get(c, "Visibility")) +
                      " Margin=" + (m == null ? "-" : GetF(m, "Left", 0f) + "," + GetF(m, "Top", 0f) + "," + GetF(m, "Right", 0f) + "," + GetF(m, "Bottom", 0f)) +
                      " ActualHeight=" + GetF(c, "ActualHeight", -1f).ToString(CultureInfo.InvariantCulture) +
                      " VAlign=" + TextOf(Get(c, "VerticalAlignment")));
            }
        }

        /// <summary>
        /// The visible search box of the tree (a TextBox bound to ...SearchText) outside the tree view; failing that, the topmost
        /// visible element outside the tree view whose DataContext is the tree's view model. Collapsed branches are skipped: the
        /// view also holds layouts for other modes that are not shown in this window.
        /// </summary>
        object FindFilterAnchor(object win, object tree, object browser)
        {
            object fallback = null, found = null;
            int searchBoxes = 0;
            var stack = new Stack<object[]>();
            stack.Push(new[] { win, (object)false });
            int budget = 20000;
            while (stack.Count > 0 && --budget > 0)
            {
                var item = stack.Pop();
                var e = item[0];
                if (e.Equals(tree)) continue;
                var vis = Get(e, "Visibility");
                if (vis != null && vis.ToString() != "Visible") continue; // not shown: neither it nor anything inside
                if (_textBoxType.IsInstanceOfType(e))
                {
                    var b = Invoke(_getBinding, null, e, _textProp);
                    var path = b == null || b == Failed ? null : TextOf(Get(Get(b, "Path"), "Path"));
                    if (path != null && path.EndsWith("SearchText", StringComparison.Ordinal))
                    {
                        searchBoxes++;
                        if (found == null && (GetB(e, "IsVisible") || _setupWindow != null)) found = e;
                    }
                    continue;
                }
                bool parentIsBrowser = (bool)item[1];
                bool isBrowser = browser != null && ReferenceEquals(Get(e, "DataContext"), browser);
                if (isBrowser && !parentIsBrowser && fallback == null) fallback = e;
                for (int k = VtChildrenCount(e) - 1; k >= 0; k--)
                {
                    var c = VtChild(e, k);
                    if (c != null) stack.Push(new[] { c, (object)isBrowser });
                }
            }
            Trace("filter panel: " + searchBoxes + " search box(es) bound to SearchText, visible one " + (found != null ? "found" : "not found"));
            if (found != null) return found;
            if (fallback != null) Trace("filter panel: using the tree view model's element " + TypeName(fallback));
            return fallback;
        }

        string Describe(object e)
        {
            var n = TextOf(Get(e, "Name"));
            string s = TypeName(e) + (string.IsNullOrEmpty(n) ? "" : "'" + n + "'");
            if (_gridType != null && _gridType.IsInstanceOfType(e))
            {
                s += "[rows " + Defs(Get(e, "RowDefinitions"), "Height") + "; cols " + Defs(Get(e, "ColumnDefinitions"), "Width") + "]";
            }
            var parent = VtParent(e);
            if (parent != null && _gridType != null && _gridType.IsInstanceOfType(parent))
            {
                var r = Invoke(_gridGetRow, null, e);
                var c = Invoke(_gridGetColumn, null, e);
                if (r != Failed) s += "@r" + r;
                if (c != Failed) s += "c" + c;
            }
            return s;
        }

        /// <summary>
        /// Item i of a Noesis collection as its real type (RowDefinition, ColumnDefinition...). The untyped IList indexer hands out
        /// bare proxies without the item's properties, so the typed get_Item of UICollection&lt;T&gt; is used.
        /// </summary>
        static object Item(object coll, int i)
        {
            var typed = coll.GetType().GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);
            if (typed != null) return typed.Invoke(coll, new object[] { i });
            return ((System.Collections.IList)coll)[i];
        }

        static int Count(object coll)
        {
            var list = coll as System.Collections.IList;
            return list == null ? -1 : list.Count;
        }

        string Defs(object coll, string prop)
        {
            int n = coll == null ? -1 : Count(coll);
            if (n < 0) return "?";
            var sb = new StringBuilder();
            try { for (int i = 0; i < n; i++) sb.Append(i > 0 ? "," : "").Append(TextOf(Get(Item(coll, i), prop))); }
            catch (Exception e) { sb.Append("!" + (e.InnerException ?? e).Message); }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        /// <summary>Subscribes to the double-click of the window's header Thumb (once per Thumb).</summary>
        void HookHeader(object w)
        {
            try
            {
                var thumb = Get(w, "HeaderThumb");
                HeaderHandler h;
                if (thumb == null || HeaderHooks.TryGetValue(thumb, out h)) return;
                h = new HeaderHandler { Plugin = this, Window = new WeakReference(w) };
                if (Hook(thumb, "MouseDoubleClick", h, "DoubleClick")) HeaderHooks.Add(thumb, h);
            }
            catch (Exception e) { Trace("HookHeader: " + e.Message); }
        }

        void ToggleMaximized(object w, object e)
        {
            try
            {
                if (w == null || !GetB(w, "CanResize")) return; // fixed-size dialogs stay as they are
                bool max = GetB(w, "Maximized");
                SetV(w, "Maximized", !max); // the game's own maximize / restore (visual state + animation, events)
                if (max) ClearMaximized(w, "double-click");
                if (e != null) SetV(e, "Handled", true);
                Verbose("'" + TextOf(Get(w, "Header")) + "' " + (max ? "restored" : "maximized") + " by double-click");
            }
            catch (Exception ex) { Trace("ToggleMaximized: " + ex.Message); }
        }

        void HookLoaded(object w)
        {
            if (_loadedHandler == null) _loadedHandler = new WindowLoadedHandler { Plugin = this };
            Hook(w, "Loaded", _loadedHandler, "Loaded");
        }

        // ---------------------------------------------------------------- Unit Reference: country drop-down

        void HookCountry(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var utb = asm.GetType("SeapowerUI.ViewModels.UnitTreeBrowserViewModel");
            var sel = utb == null ? null : utb.GetProperty("SelectedNation", inst);
            var setter = sel == null ? null : sel.GetSetMethod(true);
            _browserStates = utb == null ? null : utb.GetField("_persistedStates", BindingFlags.Static | BindingFlags.NonPublic);
            _browserState = utb == null ? null : utb.GetField("_state", inst);
            _browserInitializing = utb == null ? null : utb.GetField("_isInitializing", inst);
            if (setter == null || _browserStates == null || _browserState == null)
            {
                Logger.LogWarning("UnitTreeBrowserViewModel country members not found (game updated?); the country is not remembered.");
                return;
            }
            harmony.Patch(setter, postfix: Hm("NationSetPostfix"));
            // FinishInitialization clears _isInitializing: only after it does SelectedNation also filter the tree
            foreach (var name in new[] { "RestoreSessionState", "OnUnitsLoaded", "SetupNations", "FinishInitialization" })
            {
                var m = utb.GetMethod(name, inst | BindingFlags.DeclaredOnly);
                if (m != null) harmony.Patch(m, prefix: Hm("NationBusyPrefix"), finalizer: Hm("NationBusyFinalizer"));
                else Trace("UnitTreeBrowserViewModel." + name + " not found");
            }
            Trace("patched UnitTreeBrowserViewModel country selection");
        }

        /// <summary>Only the Unit Reference (encyclopedia) browser: its session state is stored under "Encyclopedia".</summary>
        bool IsEncyclopediaBrowser(object browser)
        {
            try
            {
                var states = _browserStates.GetValue(null) as System.Collections.IDictionary;
                return states != null && states.Contains("Encyclopedia") && ReferenceEquals(states["Encyclopedia"], _browserState.GetValue(browser));
            }
            catch { return false; }
        }

        void SaveCountry(object browser, object nation)
        {
            try
            {
                if (nation == null || !IsEncyclopediaBrowser(browser)) return;
                if (_browserInitializing != null && (bool)_browserInitializing.GetValue(browser)) return; // the game's own setup
                var text = (TextOf(Get(nation, "NationName")) ?? "").Trim();
                if (text.Length == 0 || text == _nation.Value) return;
                _nation.Value = text;
                Config.Save();
                Verbose("Unit Reference: country '" + text + "' saved");
            }
            catch (Exception e) { Trace("SaveCountry: " + e.Message); }
        }

        void RestoreCountry(object browser)
        {
            try
            {
                var text = (_nation.Value ?? "").Trim();
                if (text.Length == 0 || !IsEncyclopediaBrowser(browser)) return;
                // while the browser initialises, UpdateFilteredMuseumEntries does nothing: wait for FinishInitialization
                if (_browserInitializing != null && (bool)_browserInitializing.GetValue(browser)) return;
                var nations = Get(browser, "Nations") as System.Collections.IEnumerable;
                if (nations == null) return;
                object match = null;
                foreach (var n in nations)
                    if (string.Equals((TextOf(Get(n, "NationName")) ?? "").Trim(), text, StringComparison.OrdinalIgnoreCase)) { match = n; break; }
                if (match == null) { Trace("country '" + text + "' not in the list (yet)"); return; }
                // set even when already selected: a selection made during initialisation did not filter the tree
                _nationRestoring = true;
                try { SetV(browser, "SelectedNation", match); }
                finally { _nationRestoring = false; }
                Verbose("Unit Reference: country '" + text + "' restored");
            }
            catch (Exception e) { Trace("RestoreCountry: " + e.Message); }
        }

        // ---------------------------------------------------------------- Unit Reference: size limits, tree width, splitter

        /// <summary>The ObjectInformationViewModel shown in this window, or null.</summary>
        object InfoViewModel(object w)
        {
            var c = Get(w, "Content");
            var b = c == null ? null : Get(c, "Body");
            foreach (var o in new[] { Get(w, "DataContext"), c, c == null ? null : Get(c, "DataContext"), b, b == null ? null : Get(b, "DataContext") })
                if (o != null && TypeName(o) == "ObjectInformationViewModel") return o;
            return null;
        }

        /// <summary>
        /// Runs a few times after a window opens until it is done: for a Unit Reference window removes the minimum size and sets up
        /// the unit tree width and the splitter; other windows are left alone.
        /// </summary>
        /// <summary>Fallback from Update for a window whose Loaded set-up could not finish (retries for a few seconds).</summary>
        void SetupUnitReference(Tracked t, object w, float now)
        {
            t.UrNext = now + 0.1f;
            if (++t.UrTries > 30) { t.UrDone = true; return; }
            if (SetupUnitReferenceCore(w)) t.UrDone = true;
        }

        /// <summary>
        /// Window Loaded (after its first layout, before its first frame is drawn): set the whole Unit Reference window up right
        /// here - size limits, tree width, splitter, hidden tree / filter panel and the narrowed window - so it appears finished.
        /// </summary>
        void OnWindowLoaded(object w)
        {
            if (w == null || _hookFailed) return;
            if (_dblClickMax.Value)
            {
                HookHeader(w);
                OpenMaximized(w, "on Loaded"); // windows not recognisable yet before OnInitialized (content set later)
            }
            if (_removeLimitsAll.Value)
            {
                try
                {
                    string label;
                    if (InitNoesis() && MatchPattern(w, out label) != null) RemoveSizeLimits(w);
                }
                catch (Exception e) { Trace("RemoveSizeLimits on Loaded: " + e.Message); }
            }
            if (!_improvedUI.Value) return;
            try
            {
                if (InfoViewModel(w) == null || !InitNoesis()) return;
                Invoke(_updateLayout, w);
                _setupWindow = w;
                try { SetupUnitReferenceCore(w); }
                finally { _setupWindow = null; }
                Invoke(_updateLayout, w); // the first frame is drawn with the final layout
            }
            catch (Exception e) { Trace("OnWindowLoaded: " + e); }
        }

        /// <summary>True when done (or nothing to do), false to try again later.</summary>
        bool SetupUnitReferenceCore(object w)
        {
            var vm = InfoViewModel(w);
            if (vm == null) return false;
            if (!InitNoesis()) { return true; }

            // size limits: the global RemoveSizeLimits setting does that for every remembered window, Unit Reference included

            if (!GetB(vm, "ToggleTreeCommandVisible")) { return true; } // no tree in this window
            SplitState old;
            if (SplitStates.TryGetValue(w, out old) && old.Thumb != null && VtParent(old.Thumb) != null) { return true; }

            var tree = FindTreeView(w, vm, Get(vm, "Browser"));
            if (tree == null) return false;
            // The tree's column is a direct child of the Grid that also holds the tree toggle button ('<' / '>') and the unit
            // information; inner grids of the tree view have ScrollViewers too, so the button is what identifies it.
            var toggle = Get(vm, "ToggleTreeCommand");
            object block = null, container = null;
            for (object e = tree, p = VtParent(tree); p != null && !p.Equals(w); e = p, p = VtParent(p))
                if (_gridType.IsInstanceOfType(p) && HasChildWithCommand(p, toggle, e)) { block = e; container = p; break; }
            if (block == null)
            {
                var chain = new StringBuilder();
                for (var p = tree; p != null && !p.Equals(w); p = VtParent(p)) chain.Append(chain.Length > 0 ? " < " : "").Append(Describe(p));
                Trace("splitter: tree block not found; path " + chain);
                return true;
            }

            float actual = GetF(block, "ActualWidth", 0f);
            if (actual < 1f && GetB(vm, "MuseumEntriesVisible"))
            {
                Invoke(_updateLayout, w);
                actual = GetF(block, "ActualWidth", 0f);
            }
            if (actual < 1f) return false; // not laid out yet: next time

            var sp = new SplitState { Block = block, Container = container };
            for (int k = 0, n = VtChildrenCount(container); k < n; k++)
            {
                var c = VtChild(container, k);
                if (c != null && TypeName(c) == "ScrollViewer" && GetB(c, "IsVisible")) { sp.Info = c; break; }
            }
            Trace("splitter: block " + Describe(block) + " width " + actual.ToString(CultureInfo.InvariantCulture) + " in " + Describe(container));

            // the tree's grid column must follow the block's width (and collapse with it when the tree is hidden)
            var colObj = Invoke(_gridGetColumn, null, block);
            int col = colObj == Failed ? 0 : (int)colObj;
            var cols = Get(container, "ColumnDefinitions");
            if (cols != null && col < Count(cols) && _gridLengthAuto != null)
            {
                object cd = null;
                try { cd = Item(cols, col); } catch (Exception e) { Trace("splitter: column " + col + ": " + e.Message); }
                var cw = cd == null ? null : Get(cd, "Width");
                if (cw == null) Trace("splitter: column " + col + " definition not readable (" + TypeName(cd) + ")");
                else
                {
                    // the column width may be data-bound (the binding would win over a plain set): take it off first
                    var b = _colWidthProp == null ? null : Invoke(_getBindingBase, null, cd, _colWidthProp);
                    bool bound = b != null && b != Failed;
                    if (bound) Invoke(_clearBinding, null, cd, _colWidthProp);
                    if (!GetB(cw, "IsAuto") || bound) SetV(cd, "Width", _gridLengthAuto);
                    SetV(cd, "MinWidth", 0f);
                    SetV(cd, "MaxWidth", float.PositiveInfinity);
                    Trace("splitter: column " + col + " was " + TextOf(cw) + (bound ? " (bound)" : "") + ", now " + TextOf(Get(cd, "Width")));
                }
            }

            float width = _treeWidth.Value > 0f ? _treeWidth.Value : actual + TreeExtraWidth;
            SetV(block, "MinWidth", 0f);
            SetV(block, "MaxWidth", float.PositiveInfinity);
            SetV(block, "Width", Math.Max(TreeMinWidth, width));

            sp.Thumb = CreateSplitter(block, container, sp);
            if (sp.Thumb != null && !GetB(vm, "MuseumEntriesVisible")) SetV(sp.Thumb, "Visibility", _collapsed);
            SplitStates.Remove(w);
            SplitStates.Add(w, sp);

            // the tree was hidden last time: hide it again, exactly as the '<' button does (window narrower, filter panel hidden)
            if (!_treeShown.Value && GetB(vm, "MuseumEntriesVisible") && _treeSetter != null)
            {
                Invoke(_updateLayout, w); // lay out the new tree width first: the narrowing measures the tree as shown
                Invoke(_treeSetter, vm, false);
                Verbose("Unit Reference: tree hidden as last time");
            }
            Verbose("Unit Reference: tree width " + Math.Max(TreeMinWidth, width).ToString("0", CultureInfo.InvariantCulture) + " px" + (sp.Thumb != null ? ", splitter added" : ", no splitter"));
            return true;
        }

        void RemoveSizeLimits(object w)
        {
            SetV(w, "MinWidth", WindowMinWidth);
            SetV(w, "MinHeight", WindowMinHeight);
            SetV(w, "MaxWidth", float.PositiveInfinity);
            SetV(w, "MaxHeight", float.PositiveInfinity);

            // the view inside may carry its own minimum: clear it on the way from the view up to the window
            var view = (InfoViewModel(w) != null ? FindByType(w, "ObjectInformationView", 3000) : null) ?? ContentRoot(w);
            for (var e = view; e != null && !e.Equals(w); e = VtParent(e))
            {
                if (GetF(e, "MinWidth", 0f) > 0f) { Trace("size limit: " + Describe(e) + " MinWidth " + GetF(e, "MinWidth", 0f)); SetV(e, "MinWidth", 0f); }
                if (GetF(e, "MinHeight", 0f) > 0f) { Trace("size limit: " + Describe(e) + " MinHeight " + GetF(e, "MinHeight", 0f)); SetV(e, "MinHeight", 0f); }
            }
        }

        /// <summary>
        /// The top element of the window's content: the Content itself when it is an element, otherwise (a view-model shown
        /// through a DataTemplate) the topmost element whose DataContext is that view-model.
        /// </summary>
        object ContentRoot(object w)
        {
            var c = Get(w, "Content");
            if (c == null) return null;
            if (_uiElementType != null && _uiElementType.IsInstanceOfType(c)) return c;
            var stack = new Stack<object>();
            stack.Push(w);
            int budget = 3000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                if (ReferenceEquals(Get(e, "DataContext"), c))
                {
                    var p = VtParent(e);
                    if (p == null || !ReferenceEquals(Get(p, "DataContext"), c)) return e;
                }
                for (int k = VtChildrenCount(e) - 1; k >= 0; k--)
                {
                    var ch = VtChild(e, k);
                    if (ch != null) stack.Push(ch);
                }
            }
            return null;
        }

        object FindByType(object root, string typeName, int budget)
        {
            var stack = new Stack<object>();
            stack.Push(root);
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                if (TypeName(e) == typeName) return e;
                for (int k = VtChildrenCount(e) - 1; k >= 0; k--)
                {
                    var c = VtChild(e, k);
                    if (c != null) stack.Push(c);
                }
            }
            return null;
        }

        bool HasChildWithCommand(object parent, object cmd, object except)
        {
            if (cmd == null) return false;
            for (int k = 0, n = VtChildrenCount(parent); k < n; k++)
            {
                var c = VtChild(parent, k);
                if (c != null && !c.Equals(except) && ReferenceEquals(Get(c, "Command"), cmd)) return true;
            }
            return false;
        }

        bool HasChildOfType(object parent, string typeName, object except)
        {
            for (int k = 0, n = VtChildrenCount(parent); k < n; k++)
            {
                var c = VtChild(parent, k);
                if (c != null && !c.Equals(except) && TypeName(c) == typeName) return true;
            }
            return false;
        }

        const string SplitterXaml =
            "<Thumb xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" HorizontalAlignment=\"Right\" Cursor=\"SizeWE\" Focusable=\"False\">" +
            "<Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Border x:Name=\"Bar\" Background=\"Transparent\"/>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"Bar\" Property=\"Background\" Value=\"#30FFFFFF\"/></Trigger>" +
            "<Trigger Property=\"IsDragging\" Value=\"True\"><Setter TargetName=\"Bar\" Property=\"Background\" Value=\"#50FFFFFF\"/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb>";

        /// <summary>A Thumb in the block's grid cell, on its right edge (the gap the block keeps with its right margin).</summary>
        object CreateSplitter(object block, object container, SplitState sp)
        {
            if (_parseXaml == null || _thumbType == null) return null;
            var thumb = Invoke(_parseXaml, null, SplitterXaml);
            if (thumb == null || thumb == Failed || !_thumbType.IsInstanceOfType(thumb)) { Trace("splitter: thumb not created"); return null; }

            var m = Get(block, "Margin");
            SetV(thumb, "Width", Math.Max(5f, m == null ? 0f : GetF(m, "Right", 0f)));
            foreach (var pair in new[] { new[] { _gridGetRow, _gridSetRow }, new[] { _gridGetRowSpan, _gridSetRowSpan },
                                         new[] { _gridGetColumn, _gridSetColumn }, new[] { _gridGetColumnSpan, _gridSetColumnSpan } })
            {
                var v = Invoke(pair[0], null, block);
                if (v != Failed) Invoke(pair[1], null, thumb, v);
            }
            var children = Get(container, "Children") as System.Collections.IList;
            if (children == null) { Trace("splitter: container has no Children"); return null; }
            try { children.Add(thumb); }
            catch (Exception e) { Trace("splitter: add failed: " + e.Message); return null; }

            var handler = new SplitHandler { Plugin = this, State = sp };
            if (!Hook(thumb, "DragDelta", handler, "Delta") | !Hook(thumb, "DragCompleted", handler, "Completed"))
                Trace("splitter: events not hooked");
            return thumb;
        }

        bool Hook(object target, string evt, object handler, string method)
        {
            try
            {
                var ev = target.GetType().GetEvent(evt, BindingFlags.Instance | BindingFlags.Public);
                var mi = handler.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
                ev.AddEventHandler(target, Delegate.CreateDelegate(ev.EventHandlerType, handler, mi));
                return true;
            }
            catch (Exception e) { Trace("hook " + evt + ": " + e); return false; }
        }

        void SplitterDelta(SplitState sp, object e)
        {
            try
            {
                float dx = GetF(e, "HorizontalChange", 0f);
                if (dx == 0f) return;
                float cur = GetF(sp.Block, "Width", float.NaN);
                if (float.IsNaN(cur) || cur <= 0f) cur = GetF(sp.Block, "ActualWidth", 0f);
                float max = float.PositiveInfinity;
                if (sp.Info != null && GetB(sp.Info, "IsVisible")) max = Math.Max(TreeMinWidth, cur + GetF(sp.Info, "ActualWidth", 0f) - InfoMinWidth);
                float nw = Math.Max(TreeMinWidth, Math.Min(max, cur + dx));
                if (Math.Abs(nw - cur) >= 0.5f) SetV(sp.Block, "Width", nw);
            }
            catch (Exception ex) { Trace("SplitterDelta: " + ex.Message); }
        }

        void SplitterCompleted(SplitState sp)
        {
            try
            {
                float w = GetF(sp.Block, "Width", float.NaN);
                if (float.IsNaN(w) || w <= 0f) return;
                _treeWidth.Value = (float)Math.Round(w);
                Config.Save();
                Verbose("Unit Reference: tree width " + _treeWidth.Value.ToString(CultureInfo.InvariantCulture) + " px saved");
            }
            catch (Exception ex) { Trace("SplitterCompleted: " + ex.Message); }
        }

        bool ContainsCommand(object root, object cmd)
        {
            var stack = new Stack<object>();
            stack.Push(root);
            int budget = 5000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                if (ReferenceEquals(Get(e, "Command"), cmd)) return true;
                for (int k = VtChildrenCount(e) - 1; k >= 0; k--)
                {
                    var c = VtChild(e, k);
                    if (c != null) stack.Push(c);
                }
            }
            return false;
        }

        static readonly object Failed = new object();

        static object Invoke(MethodInfo m, object target, params object[] args)
        {
            try { return m.Invoke(target, args); }
            catch (Exception e) { Trace("invoke " + m.Name + ": " + (e.InnerException ?? e).Message); return Failed; }
        }

        void SetV(object o, string name, object v)
        {
            try { var p = Prop(o, name); if (p != null) p.SetValue(o, v, null); } catch (Exception e) { Trace("set " + name + ": " + e.Message); }
        }

        bool InitNoesis()
        {
            if (_nTried) return _nOk;
            _nTried = true;
            try
            {
                if (!InitVisualTree()) return false;
                var asm = _vtChild.DeclaringType.Assembly;
                const BindingFlags st = BindingFlags.Static | BindingFlags.Public;
                const BindingFlags ins = BindingFlags.Instance | BindingFlags.Public;
                var uiElement = asm.GetType("Noesis.UIElement");
                _uiElementType = uiElement;
                var depObj = asm.GetType("Noesis.DependencyObject");
                var depProp = asm.GetType("Noesis.DependencyProperty");
                var bindOps = asm.GetType("Noesis.BindingOperations");
                var bindingBase = asm.GetType("Noesis.BindingBase");
                _textBoxType = asm.GetType("Noesis.TextBox");
                _gridType = asm.GetType("Noesis.Grid");
                _gridLengthType = asm.GetType("Noesis.GridLength");

                _visibilityProp = uiElement.GetProperty("VisibilityProperty", st).GetValue(null, null);
                _textProp = _textBoxType.GetProperty("TextProperty", st).GetValue(null, null);
                _collapsed = Enum.Parse(asm.GetType("Noesis.Visibility"), "Collapsed");
                var fe = asm.GetType("Noesis.FrameworkElement");
                var hp = fe == null ? null : fe.GetProperty("HeightProperty", st);
                _heightProp = hp == null ? null : hp.GetValue(null, null);
                _setValue = depObj.GetMethod("SetValue", ins, null, new[] { depProp, typeof(object) }, null);
                _clearValue = depObj.GetMethod("ClearValue", ins, null, new[] { depProp }, null);
                _getBinding = bindOps.GetMethod("GetBinding", st, null, new[] { depObj, depProp }, null);
                _getBindingBase = bindOps.GetMethod("GetBindingBase", st, null, new[] { depObj, depProp }, null);
                _setBinding = bindOps.GetMethod("SetBinding", st, null, new[] { depObj, depProp, bindingBase }, null);
                _clearBinding = bindOps.GetMethod("ClearBinding", st, null, new[] { depObj, depProp }, null);
                _gridGetRow = _gridType.GetMethod("GetRow", st);
                _gridGetRowSpan = _gridType.GetMethod("GetRowSpan", st);
                _gridSetRow = _gridType.GetMethod("SetRow", st);
                _gridSetRowSpan = _gridType.GetMethod("SetRowSpan", st);
                _gridGetColumn = _gridType.GetMethod("GetColumn", st);
                _gridSetColumn = _gridType.GetMethod("SetColumn", st);
                _gridGetColumnSpan = _gridType.GetMethod("GetColumnSpan", st);
                _gridSetColumnSpan = _gridType.GetMethod("SetColumnSpan", st);
                var gui = asm.GetType("Noesis.GUI");
                _parseXaml = gui == null ? null : gui.GetMethod("ParseXaml", st, null, new[] { typeof(string) }, null);
                _updateLayout = uiElement.GetMethod("UpdateLayout", ins, null, Type.EmptyTypes, null);
                var autoProp = _gridLengthType.GetProperty("Auto", st);
                _gridLengthAuto = autoProp == null ? null : autoProp.GetValue(null, null);
                _visible = Enum.Parse(asm.GetType("Noesis.Visibility"), "Visible");
                _thumbType = asm.GetType("Noesis.Thumb");
                var colDef = asm.GetType("Noesis.ColumnDefinition");
                var cwp = colDef == null ? null : colDef.GetProperty("WidthProperty", st);
                _colWidthProp = cwp == null ? null : cwp.GetValue(null, null);

                _nOk = _visibilityProp != null && _textProp != null && _setValue != null && _clearValue != null && _getBinding != null &&
                       _getBindingBase != null && _setBinding != null && _clearBinding != null && _gridGetRow != null && _gridGetRowSpan != null && _gridSetRow != null && _gridSetRowSpan != null && _gridLengthType != null;
            }
            catch (Exception e) { Trace("InitNoesis: " + e); _nOk = false; }
            if (!_nOk) Logger.LogWarning("Noesis API changed; ImprovedUnitReferenceUI is not available.");
            return _nOk;
        }

        /// <summary>
        /// Width the tree takes in the window: the element the game collapsed (the tree view or one of its wrappers) plus its
        /// margins. Called right after the binding collapsed it, before the layout pass, so ActualWidth still holds the shown size.
        /// </summary>
        float TreeBlockWidth(object win, object tree)
        {
            object block = null;
            for (var e = tree; e != null && !ReferenceEquals(e, win); e = VtParent(e))
            {
                var v = Get(e, "Visibility");
                if (v != null && v.ToString() != "Visible") { block = e; break; }
            }
            if (block == null)
            {
                // fallback: the outermost wrapper that holds nothing but the tree
                block = tree;
                for (var p = VtParent(tree); p != null && !ReferenceEquals(p, win) && VtChildrenCount(p) == 1; p = VtParent(p)) block = p;
            }

            float w = GetF(block, "ActualWidth", 0f);
            if (w < 1f) { block = tree; w = GetF(tree, "ActualWidth", 0f); }
            var m = Get(block, "Margin");
            if (m != null) w += GetF(m, "Left", 0f) + GetF(m, "Right", 0f);
            Trace("tree block " + TypeName(block) + " width " + w.ToString(CultureInfo.InvariantCulture));
            return w;
        }

        /// <summary>The visible window holding this view model's UnitTreeBrowserView (the one under the mouse is checked first).</summary>
        bool FindTree(object vm, out object win, out object tree)
        {
            win = tree = null;
            if (!InitVisualTree()) return false;
            var browser = Get(vm, "Browser");
            if (_setupWindow != null)
            {
                var st = FindTreeView(_setupWindow, vm, browser);
                if (st != null) { win = _setupWindow; tree = st; return true; }
            }
            for (int pass = 0; pass < 2; pass++)
                for (int i = _tracked.Count - 1; i >= 0; i--)
                {
                    var w = _tracked[i].Ref.Target;
                    if (w == null || !GetB(w, "IsVisible") || GetB(w, "IsMouseOver") != (pass == 0)) continue;
                    var t = FindTreeView(w, vm, browser);
                    if (t != null) { win = w; tree = t; return true; }
                }
            return false;
        }

        object FindTreeView(object root, object vm, object browser)
        {
            var stack = new Stack<object>();
            stack.Push(root);
            int budget = 20000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                if (TypeName(e) == "UnitTreeBrowserView")
                {
                    var dc = Get(e, "DataContext");
                    if (dc != null && (ReferenceEquals(dc, vm) || ReferenceEquals(dc, browser))) return e;
                    continue; // another window's tree: no need to look inside
                }
                int n = VtChildrenCount(e);
                for (int k = n - 1; k >= 0; k--)
                {
                    var c = VtChild(e, k);
                    if (c != null) stack.Push(c);
                }
            }
            return null;
        }

        bool InitVisualTree()
        {
            if (_vtTried) return _vtChild != null;
            _vtTried = true;
            try
            {
                Type vth = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "Noesis.NoesisGUI") { vth = a.GetType("Noesis.VisualTreeHelper"); break; }
                if (vth == null) { Logger.LogWarning("Noesis.VisualTreeHelper not found; ImprovedUnitReferenceUI is not available."); return false; }
                const BindingFlags st = BindingFlags.Static | BindingFlags.Public;
                foreach (var m in vth.GetMethods(st))
                {
                    var ps = m.GetParameters();
                    if (m.Name == "GetChildrenCount" && ps.Length == 1) _vtChildrenCount = m;
                    else if (m.Name == "GetChild" && ps.Length == 2) _vtChild = m;
                    else if (m.Name == "GetParent" && ps.Length == 1) _vtParent = m;
                }
                if (_vtChildrenCount == null || _vtChild == null || _vtParent == null) { _vtChild = null; Logger.LogWarning("Noesis.VisualTreeHelper API changed; ImprovedUnitReferenceUI is not available."); }
            }
            catch (Exception e) { _vtChild = null; Trace("InitVisualTree: " + e); }
            return _vtChild != null;
        }

        int VtChildrenCount(object o)
        {
            try { return (int)_vtChildrenCount.Invoke(null, new[] { o }); } catch { return 0; }
        }

        object VtChild(object o, int i)
        {
            try { return _vtChild.Invoke(null, new[] { o, i }); } catch { return null; }
        }

        object VtParent(object o)
        {
            try { return _vtParent.Invoke(null, new[] { o }); } catch { return null; }
        }

        // ---------------------------------------------------------------- dragging outside the viewport

        void AfterDrag(object w, object e, float[] start)
        {
            try
            {
                if (GetB(w, "Maximized")) return;
                float x = start[0] + GetF(e, "HorizontalChange", 0f);
                float y = start[1] + GetF(e, "VerticalChange", 0f);
                ClampToViewport(w, ref x, ref y, GetF(w, "ActualWidth", 0f));
                SetF(w, "X", x);
                SetF(w, "Y", y);
            }
            catch (Exception ex) { Trace("AfterDrag: " + ex.Message); }
        }

        /// <summary>
        /// Keeps a window reachable: at least MinVisiblePixels of its width visible at the left/right edge, the title bar visible at
        /// the bottom edge, nothing above the top edge. With AllowDragOutside = false the whole window must stay inside.
        /// </summary>
        void ClampToViewport(object w, ref float x, ref float y, float width)
        {
            float hw, hh;
            if (!HostSize(w, out hw, out hh) || hw <= 0f || hh <= 0f) return;

            float height = GetF(w, "ActualHeight", 0f);
            float minX, maxX, maxY;
            if (_dragOutside.Value)
            {
                float vis = Math.Max(1f, _minVisible.Value);
                float header = GetF(Get(w, "HeaderThumb"), "ActualHeight", 0f);
                if (header < 8f) header = 28f;
                minX = vis - width;
                maxX = hw - vis;
                maxY = hh - header;
            }
            else
            {
                minX = 0f;
                maxX = hw - width;
                maxY = hh - height;
            }
            x = Math.Max(minX, Math.Min(x, Math.Max(minX, maxX)));
            y = Math.Max(0f, Math.Min(y, Math.Max(0f, maxY)));
        }

        bool HostSize(object w, out float width, out float height)
        {
            width = height = 0f;
            if (_getHostSize == null) return false;
            try
            {
                var args = new object[] { 0f, 0f };
                _getHostSize.Invoke(w, args);
                width = (float)args[0]; height = (float)args[1];
                return true;
            }
            catch { return false; }
        }

        // ----------------------------------------------------- reflection helpers

        PropertyInfo Prop(object o, string name)
        {
            if (o == null) return null;
            string k = o.GetType().FullName + "." + name;
            PropertyInfo p;
            if (_props.TryGetValue(k, out p)) return p;
            // walk up from the real type: Noesis ColumnDefinition/RowDefinition derive from FrameworkElement and declare their own
            // Width/Height (GridLength) next to the inherited float ones, so a plain GetProperty throws AmbiguousMatchException
            p = null;
            for (var ty = o.GetType(); ty != null && p == null; ty = ty.BaseType)
                p = ty.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            _props[k] = p;
            return p;
        }

        object Get(object o, string name)
        {
            try { var p = Prop(o, name); return p == null ? null : p.GetValue(o, null); }
            catch { return null; }
        }

        float GetF(object o, string name, float def)
        {
            var v = Get(o, name);
            if (v == null) return def;
            try { return Convert.ToSingle(v, CultureInfo.InvariantCulture); } catch { return def; }
        }

        bool GetB(object o, string name)
        {
            var v = Get(o, name);
            return v is bool && (bool)v;
        }

        void SetF(object o, string name, float v)
        {
            try { var p = Prop(o, name); if (p != null) p.SetValue(o, v, null); } catch { }
        }

        static string TypeName(object o) { return o == null ? null : o.GetType().Name; }
        static string TextOf(object o) { return o == null ? null : (o as string ?? o.ToString()); }

        // ----------------------------------------------------------------- update

        void Update()
        {
            if (!_loadHookTried) TryInstallLoadScreenHook();
            if (ModsChanged) RefreshEnabled();
            if (!Enabled)
            {
                if (_dirty) Save();
                return;
            }

            if (_hooked && !_hookFailed && Input.GetKeyDown(_addKey.Value)) AddWindowUnderCursor();

            if (!_hooked && LoadScreenDone) TryHook();
            if (!_hooked || _hookFailed) return;

            for (; _registered < Created.Count; _registered++)
                _tracked.Add(new Tracked { Ref = Created[_registered] });

            float now = Time.unscaledTime;
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                var t = _tracked[i];
                var w = t.Ref.Target;
                if (w == null) { _tracked.RemoveAt(i); continue; } // collected
                try { Step(t, w, now); TrackOpenState(t, w, now); }
                catch (Exception e)
                {
                    if (!t.Warned) { t.Warned = true; Logger.LogWarning("Window '" + t.Label + "': " + e.Message); Trace("Step error: " + e); }
                }
            }

            if (_osPatterns.Count > 0)
            {
                if (_osSeenLoads != LoadScreenCount)
                {
                    _osSeenLoads = LoadScreenCount;
                    _osRestoreAt = now + OsRestoreDelay;
                    _osRecordFrom = float.MaxValue;
                }
                if (_osRestoreAt >= 0f && now >= _osRestoreAt)
                {
                    _osRestoreAt = -1f;
                    RestoreOpenStates();
                    _osRecordFrom = now + 0.5f;
                }
            }

            if (_probes.Count > 0) RunProbes();
            if (_dirty && now - _dirtySince > 1f) Save();
        }

        /// <summary>Per-window state machine: identify, restore (fallback for what the early hooks missed), then record user changes.</summary>
        void Step(Tracked t, object w, float now)
        {
            bool active = GetB(w, "IsVisible");
            if (active && !t.WasActive)
            {
                t.Settled = false; t.Countdown = -1; t.Key = null; t.Tries = 0; t.NextTry = 0f; t.Reported = false;
                t.UrDone = false; t.UrTries = 0; t.UrNext = 0f; t.WasMax = false;
                if (_dblClickMax.Value) HookHeader(w);
            }
            else if (!active && t.WasActive)
            {
                t.Settled = false; t.Countdown = -1;
                TreeState ts; // the window reopens with the tree shown: undo what we did to it
                if (TreeStates.TryGetValue(w, out ts)) ShowFilterPanel(ts);
                TreeStates.Remove(w);
            }
            t.WasActive = active;
            if (!active) return;

            if (_improvedUI.Value && !t.UrDone && now >= t.UrNext) SetupUnitReference(t, w, now);

            // identify (content/header may be assigned shortly after the window becomes visible)
            if (t.Key == null)
            {
                if (t.Tries >= 10)
                {
                    if (_debug && !t.Reported)
                    {
                        t.Reported = true;
                        Logger.LogInfo("Window not remembered (no pattern matches): [" + string.Join(" / ", Candidates(w).ToArray()) + "]");
                    }
                    return;
                }
                if (now < t.NextTry) return;
                t.Tries++;
                t.NextTry = now + 0.4f;
                string label;
                t.Key = MatchPattern(w, out label);
                if (t.Key == null) return;
                t.Label = label;
                if (_removeLimitsAll.Value && InitNoesis()) RemoveSizeLimits(w);
                if (_dblClickMax.Value) OpenMaximized(w, "when recognised");
                t.Countdown = 8;
                Verbose("Tracking window " + label + " as pattern '" + t.Key + "'");
                return;
            }

            if (t.Countdown > 0)
            {
                t.Countdown--;
                if (t.Countdown == 6) ApplyLayout(t.Key, w);
                return;
            }
            if (t.Countdown == 0)
            {
                ApplyLayout(t.Key, w);
                t.Last = Read(w);
                t.Settled = true;
                t.Countdown = -1;
                return;
            }

            if (!t.Settled) return;
            bool max = GetB(w, "Maximized");
            Layout sv;
            bool has = _saved.TryGetValue(t.Key, out sv);
            // the ',max' mark is kept by OnMaximizedChanged only (a real maximize / restore), never by this polling
            if (max) { t.WasMax = true; return; }
            if (t.WasMax)
            {
                // just restored: the size is still animating back; take it as the baseline
                t.WasMax = false;
                t.Last = Read(w);
                return;
            }
            var cur = Read(w);
            if (!cur.Same(t.Last))
            {
                t.Last = cur;
                cur.Max = has && sv.Max;
                _saved[t.Key] = cur;
                _dirty = true;
                _dirtySince = now; // debounce while dragging/resizing
            }
        }

        // ------------------------------------------------------------ open / closed windows (RememberOpenWindows)

        string MatchOpenPattern(object w)
        {
            var cands = Candidates(w);
            for (int i = 0; i < _osPatterns.Count; i++)
                foreach (var c in cands)
                {
                    var sq = Squash(c);
                    if (sq.Length > 0 && GlobMatch(_osPatternsSq[i], sq)) return _osPatterns[i];
                }
            return null;
        }

        /// <summary>Follows the window's open / closed state and saves changes the user made.</summary>
        void TrackOpenState(Tracked t, object w, float now)
        {
            if (_osPatterns.Count == 0) return;
            if (!t.OsTried)
            {
                if (now < t.OsNext) return;
                t.OsNext = now + 0.2f;
                t.OsKey = MatchOpenPattern(w);
                if (t.OsKey == null && ++t.OsTries < 30) return; // header / content may not be there yet: try again
                t.OsTried = true;
            }
            if (t.OsKey == null) return;

            bool open = GetB(w, "IsVisible");
            if (t.OsKnown && open == t.OsOpen) return;
            bool first = !t.OsKnown;
            t.OsKnown = true;
            t.OsOpen = open;
            if (first || now < _osRecordFrom) return; // the game's own start-up / our restore

            if (!open)
            {
                // closed by the user, or is the whole UI going away (end of mission)? then its parent is hidden / gone too
                var parent = VtParent(w);
                if (parent == null || !GetB(parent, "IsVisible")) return;
            }
            SaveOpenState(t.OsKey, open);
        }

        void SaveOpenState(string key, bool open)
        {
            ConfigEntry<string> e;
            if (!_osEntries.TryGetValue(key, out e)) return;
            var v = open ? "open" : "closed";
            if (e.Value == v) return;
            e.Value = v;
            Config.Save();
            Verbose("'" + key + "' " + v + " - remembered");
        }

        /// <summary>After a load screen: open / close the remembered windows the game has created.</summary>
        void RestoreOpenStates()
        {
            if (!InitNoesis()) return;
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                var t = _tracked[i];
                var w = t.Ref.Target;
                if (w == null) continue;
                try
                {
                    if (!t.OsTried) { t.OsKey = MatchOpenPattern(w); t.OsTried = t.OsKey != null; }
                    if (t.OsKey == null) continue;
                    var parent = VtParent(w);
                    if (parent == null || !GetB(parent, "IsVisible")) continue; // not part of the current UI
                    ConfigEntry<string> e;
                    if (!_osEntries.TryGetValue(t.OsKey, out e) || string.IsNullOrEmpty(e.Value)) continue;
                    bool want = e.Value.Trim().Equals("open", StringComparison.OrdinalIgnoreCase);
                    bool open = GetB(w, "IsVisible");
                    if (open == want) continue;
                    if (SetOpen(w, want)) Verbose("'" + t.OsKey + "' " + (want ? "opened" : "closed") + " as last time");
                    t.OsOpen = GetB(w, "IsVisible");
                    t.OsKnown = true;
                }
                catch (Exception ex) { Trace("RestoreOpenStates: " + ex.Message); }
            }
        }

        /// <summary>
        /// Opens / closes a window the way the game does: through the source its Visibility is bound to (a HUD toggle button by
        /// ElementName, or a view-model flag such as MainGameViewModel.MapVisible), so the game's own state follows.
        /// </summary>
        bool SetOpen(object w, bool open)
        {
            var b = Invoke(_getBinding, null, w, _visibilityProp);
            if (b == null || b == Failed)
            {
                Trace("open/close '" + TextOf(Get(w, "Header")) + "': Visibility not bound, left alone");
                return false;
            }
            var path = TextOf(Get(Get(b, "Path"), "Path")) ?? "";
            var elementName = TextOf(Get(b, "ElementName"));
            object source = Get(b, "Source");
            if (source == null && !string.IsNullOrEmpty(elementName))
                for (var p = w; p != null && source == null; p = VtParent(p))
                {
                    var fn = p.GetType().GetMethod("FindName", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                    if (fn != null) try { source = fn.Invoke(p, new object[] { elementName }); } catch { }
                }
            if (source == null && string.IsNullOrEmpty(elementName)) source = Get(w, "DataContext");
            Trace("open/close '" + TextOf(Get(w, "Header")) + "': binding " + (elementName ?? TypeName(source)) + "." + path);
            if (source == null || path.Length == 0) return false;

            // walk a dotted path to the object that holds the last property
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length - 1 && source != null; i++) source = Get(source, parts[i]);
            if (source == null) return false;
            var prop = Prop(source, parts[parts.Length - 1]);
            if (prop == null || !prop.CanWrite) return false;

            object value;
            var pt = prop.PropertyType;
            if (pt == typeof(bool) || pt == typeof(bool?)) value = open;
            else if (pt.IsEnum && pt.Name == "Visibility") value = open ? _visible : _collapsed;
            else { Trace("open/close: unsupported source type " + pt.Name); return false; }
            prop.SetValue(source, value, null);
            Invoke(_updateLayout, w);
            bool ok = GetB(w, "IsVisible") == open;
            if (!ok) Trace("open/close: window state did not follow the source (" + parts[parts.Length - 1] + " = " + value + ")");
            return ok;
        }

        // ------------------------------------------------------------ matching

        static void Add(List<string> list, string s)
        {
            if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s);
        }

        /// <summary>Strings that identify a window: name, header, content type, view-model type...</summary>
        List<string> Candidates(object w)
        {
            var list = new List<string>();
            try
            {
                Add(list, TextOf(Get(w, "Name")));
                var h = Get(w, "Header");
                Add(list, TextOf(h)); Add(list, TypeName(h));
                var c = Get(w, "Content");
                Add(list, TypeName(c));
                if (c != null)
                {
                    Add(list, TextOf(Get(c, "Name")));
                    Add(list, TypeName(Get(c, "DataContext")));
                    var wh = Get(c, "Header");           // SeapowerUI.WindowData
                    if (wh != null) { Add(list, TextOf(wh)); Add(list, TypeName(wh)); }
                    var body = Get(c, "Body");
                    if (body != null)
                    {
                        Add(list, TypeName(body));
                        Add(list, TextOf(Get(body, "Name")));
                        Add(list, TypeName(Get(body, "DataContext")));
                    }
                }
                Add(list, TypeName(Get(w, "DataContext")));
            }
            catch { }
            return list;
        }

        string MatchPattern(object w, out string label)
        {
            label = null;
            var cands = Candidates(w);
            for (int i = 0; i < _patterns.Count; i++)
                foreach (var c in cands)
                {
                    var sq = Squash(c);
                    if (sq.Length > 0 && GlobMatch(_patternsSq[i], sq))
                    {
                        if (_debug) label = "'" + c + "' [" + string.Join(" / ", cands.ToArray()) + "]";
                        return _patterns[i];
                    }
                }
            return null;
        }

        static string Squash(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            return sb.ToString();
        }

        /// <summary>Case-insensitive glob match, '*' = any sequence of characters.</summary>
        static bool GlobMatch(string pattern, string text)
        {
            int p = 0, t = 0, star = -1, mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && pattern[p] != '*' &&
                    char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(text[t])) { p++; t++; }
                else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = t; }
                else if (star != -1) { p = star + 1; t = ++mark; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        // --------------------------------------------------------- persistence

        void Save()
        {
            _dirty = false;
            try
            {
                foreach (var kv in _saved)
                {
                    ConfigEntry<string> e;
                    if (_layoutEntries.TryGetValue(kv.Key, out e)) e.Value = kv.Value.ToString();
                }
                Config.Save();
            }
            catch (Exception e) { Logger.LogError("Saving layouts failed: " + e.Message); }
        }

        void OnApplicationQuit() { if (_dirty) Save(); }
        void OnDestroy() { if (_dirty) Save(); }

        // --------------------------------------------------------- add a window (AddWindowKey)

        static readonly string[] GenericNames = { "String", "TextBlock", "Grid", "ContentPresenter", "WindowData", "HostWindow", "Window", "Border", "StackPanel" };

        /// <summary>Best short identifying string of a window, or null.</summary>
        string SuggestPattern(object w)
        {
            // 1. a real title text
            var h = Get(w, "Header") as string;
            if (!string.IsNullOrWhiteSpace(h)) return h.Trim();
            var c = Get(w, "Content");
            if (c != null)
            {
                var wh = Get(c, "Header") as string;
                if (!string.IsNullOrWhiteSpace(wh)) return wh.Trim();
            }
            // 2. the view type of the content, e.g. UnitStatusView
            var cands = Candidates(w);
            foreach (var x in cands)
                if (x.EndsWith("View", StringComparison.Ordinal) && Array.IndexOf(GenericNames, x) < 0) return x;
            // 3. an explicit element name, 4. any non-generic string
            var name = TextOf(Get(w, "Name"));
            if (!string.IsNullOrWhiteSpace(name) && Array.IndexOf(GenericNames, name) < 0) return name.Trim();
            foreach (var x in cands)
                if (!string.IsNullOrWhiteSpace(x) && Array.IndexOf(GenericNames, x) < 0) return x.Trim();
            return null;
        }

        void AddWindowUnderCursor()
        {
            // topmost visible window the mouse is over (the most recently created one wins if several report it)
            Tracked hit = null;
            foreach (var t in _tracked)
            {
                var w = t.Ref.Target;
                if (w != null && GetB(w, "IsVisible") && GetB(w, "IsMouseOver")) hit = t;
            }
            if (hit == null) { Logger.LogInfo("Smart Windows: no window under the mouse cursor."); return; }

            var win = hit.Ref.Target;
            string label;
            var existing = MatchPattern(win, out label);
            if (existing != null) { Logger.LogInfo("Smart Windows: this window is already remembered (pattern '" + existing + "')."); return; }

            var pattern = SuggestPattern(win);
            if (pattern == null) { Logger.LogWarning("Smart Windows: cannot find a usable name for this window (set Debug = true to see its identifying strings)."); return; }

            try { BindLayout(pattern); }
            catch (Exception ex)
            {
                Logger.LogWarning("Smart Windows: '" + pattern + "' cannot be used as a config key (" + ex.Message + ").");
                return;
            }

            _patterns.Add(pattern);
            _patternsSq.Add(Squash(pattern));
            var cur = _windows.Value.Trim().TrimEnd(',').Trim();
            _windows.Value = cur.Length == 0 ? pattern : cur + "," + pattern;
            Config.Save();

            // start tracking this very window instance right away
            hit.Key = null; hit.Tries = 0; hit.NextTry = 0f; hit.Settled = false; hit.Countdown = -1;
            Logger.LogInfo("Smart Windows: added '" + pattern + "' to WindowPatterns - its position will be remembered.");
        }
    }
}
