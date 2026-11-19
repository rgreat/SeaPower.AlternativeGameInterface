﻿﻿﻿using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using Noesis;
using Rgreat.Kit;

namespace AlternativeGameInterface
{
    public partial class AlternativeGameInterfacePlugin
    {
        /// <summary>
        /// The settings window, opened by the gear in the title bar of a remembered window. Built and styled as the settings window
        /// of Alternative Damage Model: own dark window (title bar to drag, ✕), tabs, a card with grouped rows - label with a yellow
        /// dot when the value was changed in the window and is not applied yet, editor (− / text box / + with step and limits, check box,
        /// text, an editable list of windows), the default, ↺ to it -, a help pane, resizing by the edges. Works on a copy: Apply / OK store and apply the changes, Cancel / ✕ drop them.
        /// The Unit Reference / Unit Status tabs are shown only when the window is opened from such a window.
        /// While a text box has the keyboard, the game's hot keys are off (InputHandler.typingActive).
        /// </summary>
        static partial class SettingsWindow
        {
            // ------------------------------------------------------------ what is shown
            sealed class Field
            {
                public string Tab, Group, Label, Unit;
                public char Kind;            // b bool, f number, k key, s text, c colour, w wide text (a list)
                public ConfigEntryBase Entry;
                public float Step, Min, Max;
                public Field Needs;          // shown only while this (bool) field is on
                public bool TfbOnly;         // shown only when the window is opened from the campaign Task Force Builder
                public Field Pair;           // kind 'p' (a 2D pad): the field of the second axis (that field itself is not shown as a row)
                public bool Hidden;          // never a row of its own (the second axis of a pad)
                public Func<string> Conflict;   // not null and returns a text: greyed out, the text says why (another mod does it)
            }

            const string TWindows = "Windows", TMap = "Desktop mode", TTact = "Tactical Display", TLog = "Event Log", TRemembered = "Remembered Windows", TFrame = "Frame", TMission = "In Mission", TFiles = "Game Menus", TDebug = "Debug", TCampaign = "Campaign", TStatus = "Unit Status", TTrack = "Track State", TRef = "Unit Reference", TWorld = "World View";
            static readonly string[] Tabs = { TWindows, TMap, TTact, TWorld, TLog, TMission, TFiles, TStatus, TTrack, TRef, TCampaign, TDebug };
            static readonly List<Field> Fields = new List<Field>();

            static void Add(string tab, string group, ConfigEntryBase e, char kind, string label, string unit, float step = 0f, float min = 0f, float max = 0f)
            {
                if (e != null) Fields.Add(new Field { Tab = tab, Group = group, Entry = e, Kind = kind, Label = label, Unit = unit, Step = step, Min = min, Max = max });
            }

            // texts of a row in the current language (the English ones in the Field stay the ids)
            static string KeyOf(Field f) { return f.Entry.Definition.Section + "/" + f.Entry.Definition.Key; }
            static string LabelOf(Field f) { return Loc.Or("lbl." + KeyOf(f), f.Label); }
            static string UnitOf(Field f) { return Loc.Or("unit." + KeyOf(f), f.Unit); }
            static string HelpOf(Field f) { return Loc.Or("help." + KeyOf(f), f.Entry.Description == null ? "" : f.Entry.Description.Description); }
            static string GroupOf(string g) { return Loc.Or("grp." + g, g); }
            static string TabOf(string t) { return Loc.Or("tab." + t, t); }

            /// <summary>Puts the settings table's texts into the translation table (once, when the config is bound).</summary>
            public static void RegisterTexts(AlternativeGameInterfacePlugin p)
            {
                Define(p);
                foreach (var t in Tabs) Loc.Register("tab." + t, t);
                foreach (var kv in PageGroups) foreach (var pg in kv.Value) Loc.Register("pg." + pg[0], pg[0]);
                foreach (var f in Fields)
                {
                    Loc.Register("grp." + f.Group, f.Group);
                    Loc.Register("lbl." + KeyOf(f), f.Label);
                    if (!string.IsNullOrEmpty(f.Unit)) Loc.Register("unit." + KeyOf(f), f.Unit);
                    if (f.Entry.Description != null) Loc.Register("help." + KeyOf(f), f.Entry.Description.Description);   // also the entries of the other config file (Campaign)
                }
            }

            static void Define(AlternativeGameInterfacePlugin p)
            {
                if (Fields.Count > 0) return;
                Add(TWindows, "Moving, sizing and snapping", p._dragOutside, 'b', "Drag partly off-screen", "");
                Add(TWindows, "Moving, sizing and snapping", p._minVisible, 'f', "Visible at the edge", "px", 5f, 1f, 10000f);
                Add(TWindows, "Moving, sizing and snapping", p._dblClickMax, 'b', "Maximize by double-click", "");
                Add(TWindows, "Moving, sizing and snapping", p._removeLimitsAll, 'b', "No size limits", "");
                Add(TWindows, "Moving, sizing and snapping", p._snapOn, 'b', "Snap edges", "");
                Add(TWindows, "Moving, sizing and snapping", p._snapDist, 'f', "Snap distance", "px", 1f, 1f, 50f);
                Add(TWindows, "Moving, sizing and snapping", p._dockReach, 'b', "Resize docked windows", "zone reaches inside at screen edges");
                Add(TWindows, "Order and title bars", p._toFront, 'b', "Clicked window to front", "");
                Add(TWindows, "Order and title bars", p._cascade, 'f', "Cascade shift", "px · 0 = off", 5f, 0f, 500f);
                Add(TWindows, "Order and title bars", p._fixCursors, 'b', "Fix game resize cursors", "menus and campaign");
                Add(TWindows, "Text by background", p._textUpE, 'f', "Text turns dark above", "brightness 0 - 1", 0.01f, 0.05f, 0.95f);
                Add(TWindows, "Text by background", p._textDownE, 'f', "Text turns light below", "brightness 0 - 1", 0.01f, 0.05f, 0.95f);
                Add(TWindows, "Performance", p._vsyncItem, 'b', "Add Adaptive V-Sync", "in the V-Sync list");
                Add(TWindows, "Performance", p._fpsNone, 'b', "Remove frame rate limit", "slider off");

                {
                    int from = Fields.Count;
                    Add(TWorld, "Camera offset", p._camOn, 'b', "Shift the view", "");
                    Add(TWorld, "Camera offset", p._camX, 'p', "Followed object on the screen", "% of the view", 0.5f, -25f, 25f);
                    Add(TWorld, "Camera offset", p._camY, 'f', "Followed object on the screen: vertical", "% of the view", 0.5f, -25f, 25f);
                    Fields[Fields.Count - 2].Pair = Fields[Fields.Count - 1];
                    Fields[Fields.Count - 1].Hidden = true;
                    Func<string> camConflict = () => !p.CamModWorking() ? null : Loc.Or("cam.conflict", "The Camera Offset mod is loaded: it shifts the camera, set it in its own window (F11). These settings are not used.");
                    for (int i = from; i < Fields.Count; i++) Fields[i].Conflict = camConflict;
                }
                Add(TLog, "Links", p._logLinks, 'b', "All contacts in the log are links", "track numbers");
                Add(TLog, "Links", p._logCenter, 'b', "Center map on repeated click", "Tactical Display");
                Add(TLog, "Layout", p._logCompact, 'b', "Compact layout", "less spacing");
                Add(TTact, "Map", p._fineZoom, 'b', "Fine zoom with Shift", "wheel steps 5 times smaller");
                Add(TTact, "Map", p._keepMapView, 'b', "Keep map view on resize", "zoom follows the window size");
                Add(TTact, "Map drawing", p._clipEllipse, 'b', "Cut contact ellipses", "behind the sensor");
                Add(TTact, "Map drawing", p._mapImp, 'b', "Improved drawing of units and labels", "master switch");
                Add(TTact, "Map drawing", p._sonoOn, 'b', "Compact sonobuoys", "half size, no text");
                Add(TTact, "Map drawing", p._sonoMax, 'f', "Sonobuoys threshold", "visible at once", 1f, 5f, 300f);
                Add(TTact, "Map drawing", p._glPlace, 'm', "Label position", "");
                Add(TTact, "Map drawing", p._glSpread, 'f', "Formation spread limit: ships, ground", "nm from the leader", 0.5f, 0.5f, 100f);
                Add(TTact, "Map drawing", p._glSpreadAir, 'f', "Formation spread limit: aircraft", "nm from the leader", 0.1f, 0.1f, 100f);
                Add(TTact, "Map drawing", p._lead, 'b', "Formation update by the leader", "aircraft move together");
                Add(TTact, "Map drawing", p._glLines, 'b', "Formation lines", "leader to units, on selection");
                Add(TTact, "Map drawing", p._glGroup, 'b', "Formation name only", "until selected");
                Add(TTact, "Map drawing", p._glAirInfo, 'b', "Aircraft formation: distance and altitude", "under the name");
                Add(TTact, "Map drawing", p._glBadge, 'b', "FRND badge in the label", "a line of its own");
                Add(TTact, "Map drawing", p._glOverlap, 'b', "Filter overlapping labels", "keep the biggest unit");

                Add(TMap, "Desktop mode", p._windowedMap, 'b', "Enable desktop mode", "free move and size");
                Add(TMap, "Desktop mode", p._deskCount, 'f', "Number of desktops", "2 - 5", 1f, 2f, 5f);
                Add(TMap, "Desktop mode", p._wvKey, 'k', "World View on / off key", "Insert by default");
                Add(TMap, "Desktop mode", p._useMonitors, 'b', "Other monitors for windows", "Monitor in the window's menu");

                Add(TMission, "In Mission", p._soundReload, 'b', "No errors when loading a save", "");
                Add(TMission, "In Mission", p._timeDrag, 'b', "Draggable time panel", "double-click: back to the corner");
                Add(TMission, "In Mission", p._unitMenu, 'b', "Menu on the unit panel's flag/name", "");
                Add(TMission, "In Mission", p._binoDown, 'f', "Ship binoculars below the horizon", "degrees · 0 = game", 1f, 0f, 45f);
                Add(TMission, "In Mission", p._binoNormal, 'b', "Normal view in binoculars at max zoom", "");
                Add(TMission, "In Mission", p._binoNormalDay, 'b', "Normal view only by day", "");

                Add(TFiles, "Main menu", p._encSplit, 'b', "Encyclopedia and Encyclopedia 3D", "");
                Add(TFiles, "Main menu", p._newsFit, 'b', "News window fills the free area", "");
                Add(TFiles, "Scenarios", p._hideCustom, 'b', "Hide _Custom Mission", "");
                Add(TFiles, "Save and load", p._rememberSave, 'b', "Remember last folder", "");
                Add(TFiles, "Save and load", p._autoLoad, 'b', "Open Load game on entering Scenarios and Campaign", "");
                Add(TFiles, "Save and load", p._menuAside, 'b', "Menu windows beside the buttons", "Load game ... not over the menu");
                Add(TFiles, "Save and load", p._pickerTall, 'b', "File picker without a height limit", "");
                Add(TFiles, "Save and load", p._dblOpen, 'b', "Double click opens a file or scenario", "");
                Add(TFiles, "Mods", p._modCreateFill, 'b', "Fill in Create Mod", "Update Existing");
                Add(TFiles, "Backgrounds", p._realPhotos, 'b', "Real photos as backgrounds", "");
                Add(TFiles, "Backgrounds", p._modBackgrounds, 'm', "Background pictures", "");
                Add(TFiles, "Backgrounds", p._menuBg, 'm', "Main menu background", "");
                Add(TFiles, "Backgrounds", p._slideSec, 'f', "Slideshow interval", "sec", 5f, 5f, 3600f);
                Add(TFiles, "Mission editor", p._rememberFolder, 'b', "Remember last folder", "");
                Add(TFiles, "Mission editor", p._blueIni, 'b', "Highlight ini files", "");
                Add(TCampaign, "This campaign", p._strictLoadouts, 'b', "Strict campaign loadouts", "");
                Add(TCampaign, "Task Force", p._dismissDamaged, 'b', "Dismiss damaged ships", "refund minus repair");
                Add(TCampaign, "Task Force", p._sortLoadouts, 'b', "Sort loadouts by date", "own versions first");
                Add(TCampaign, "Task Force", p._fixSwapPrice, 'b', "Fix version swap price", "roster price");
                Add(TCampaign, "Task Force", p._upgradeShip, 'b', "Upgrade ship button in the Builder", "crew -1 level");
                Add(TCampaign, "Task Force", p._fixSubs, 'b', "Fix submarines sent for service", "");
                Add(TCampaign, "Task Force", p._eventState, 'b', "Ship state on briefing and newspaper events", "after the latest mission");
                Add(TDebug, "Game logs", p._deleteLogs, 'b', "Delete old game logs at start", "");
                Add(TDebug, "Game", p._showVersion, 'm', "Game version watermark", "");
                Add(TDebug, "Mod log", p._debugEntry, 'b', "Debug trace", "");
                Add(TDebug, "Mod log", p._debugAllEntry, 'b', "Trace of periodic data", "");
                Add(TDebug, "Diagnostics", p._snapshot, 'a', "Take a snapshot now", "");
                Add(TDebug, "Diagnostics", p._collectGarbage, 'a', "Collect garbage now", "");
                Add(TDebug, "Diagnostics", p._shotTabs, 'a', "Screenshot all tabs of all mods now", "");
                if (!IsWorkshopInstall()) Add(TDebug, "Game UI", p._dumpXaml, 'a', "Dump XAML now", "");   // a development tool: not for the Steam Workshop copy

                Add(TRemembered, "Position and size", p._windows, 'w', "Remembered windows", "'*' = any text");
                Add(TRemembered, "Open or closed at mission start", p._openWindows, 'w', "Windows", "");

                Add(TWindows, "Frame around draggable windows", p._borderOn, 'b', "Frame", "");
                Add(TWindows, "Frame around draggable windows", p._borderThickness, 'f', "Width", "px", 0.5f, 0.5f, 10f);
                Add(TWindows, "Frame around draggable windows", p._borderColor, 'c', "Colour", "#AARRGGBB");
                Add(TWindows, "Frame around draggable windows", p._tipBorder, 'b', "Tooltips too", "all four sides");

                Add(TStatus, "Damage Control tab", p._secMin, 'f', "Sections min height", "px · 0 = game", 10f, 0f, 3000f);
                Add(TStatus, "Damage Control tab", p._dcWheel, 'b', "Wheel scrolls the section lists", "");
                Add(TStatus, "Reference tab", p._statusRef, 'b', "Reference tab", "");
                Add(TTrack, "Track State", p._trackState, 'b', "Track State of a selected contact", "");
                Add(TTrack, "Track State", p._trackLayout, 'b', "Recomposed Track State layout", "");
                Add(TTrack, "Track State", p._trackProgress, 'b', "Identification progress", "rough, tens of percent");
                Add(TStatus, "Unit name", p._titleName, 'm', "Window title name", "");
                Add(TStatus, "Unit name", p._labelName, 'm', "Name at the right of the tabs", "");

                Add(TRef, "Window and tree", p._improvedUI, 'b', "Layout Improvements", "");
                Add(TRef, "Window and tree", p._loadoutList, 'b', "Loadout list for all units", "");
                Add(TRef, "Window and tree", p._propulsion, 'b', "Propulsion in ship data", "");
                Add(TRef, "Window and tree", p._treeOpt, 'b', "Optimize the tree", "");
                Add(TRef, "Window and tree", p._treeScroll, 'b', "Scroll to the selected unit", "");
                Add(TRef, "Window and tree", p._earliest, 'b', "Class click: earliest variant", "");
                Add(TRef, "Window and tree", p._keepOpened, 'b', "Keep the opened unit in the tree", "");
                Add(TRef, "Window and tree", p._treeWheel, 'b', "Wheel only over the tree", "");
                Add(TRef, "Search and help", p._searchOn, 'b', "Remember search panel", "");
                Add(TRef, "Search and help", p._hideCountries, 'b', "Hide countries with no results", "");
                Add(TRef, "Search and help", p._searchDesc, 'b', "Search in description and id", "");
                Add(TRef, "Search and help", p._linkHelp, 'b', "Help tooltips on links", "");

                var wm = Fields.Find(f => ReferenceEquals(f.Entry, p._windowedMap));
                var dc = Fields.Find(f => ReferenceEquals(f.Entry, p._deskCount));
                _mapField = wm;
                var wk = Fields.Find(f => ReferenceEquals(f.Entry, p._wvKey));
                if (wm != null && wk != null) wk.Needs = wm;
                if (wm != null && dc != null) dc.Needs = wm;       // the desktops exist only in this mode

                var mi = Fields.Find(f => ReferenceEquals(f.Entry, p._mapImp));
                foreach (var f in Fields) if (mi != null && f != mi && f.Tab == TTact && f.Group == "Map drawing") f.Needs = mi;      // the children are hidden while the master switch is off

                var sl = Fields.Find(f => ReferenceEquals(f.Entry, p._strictLoadouts));
                if (sl != null) sl.TfbOnly = true;
                // work only with Layout Improvements (ImprovedUnitReferenceUI): hidden while it is off
                var ui = Fields.Find(f => ReferenceEquals(f.Entry, p._improvedUI));
                foreach (var f in Fields)
                    if (f.Tab == TRef && f != ui && (f.Group == "Kept by the mod" || ReferenceEquals(f.Entry, p._searchOn))) f.Needs = ui;
            }

            // ------------------------------------------------------------ colours (as Alternative Damage Model)
            static readonly Color CBg = WinStyle.Bg, CPanel = WinStyle.Panel, CCard = WinStyle.Card, CLine = WinStyle.Line, CText = WinStyle.Text, CDim = WinStyle.Dim,
                                  CWarn = WinStyle.Warn, CTitle = WinStyle.Title, CField = WinStyle.Field, CHl = WinStyle.Hl;

            static SolidColorBrush B(Color c) { return WinStyle.B(c); }
            static readonly SolidColorBrush Clear = WinStyle.Clear;

            // ------------------------------------------------------------ state
            static AlternativeGameInterfacePlugin P;
            // the window itself (Rgreat.Kit): where it is, its size, dragging, resizing
            static readonly WinFrame _f = MakeFrame();

            static WinFrame MakeFrame()
            {
                var f = new WinFrame(1060f, 830f);   // screen pixels: converted to UI units at the first opening
                f.MinW = 520f;
                f.Id = SelfId;
                f.Trace = s => Trace(s);
                f.ResizeTip = () => Loc.T("tip.resize");
                f.LanguageLabel = () => Loc.T("lang.caption");
                f.Languages = () => { var l = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", Loc.T("lang.default")) }; l.AddRange(Loc.GameLanguages()); return l; };
                f.LanguageChanged = () => RebuildAfterLayout();
                f.Keyboard = (on, el) => { if (P != null) P.SetTyping(on, el); };   // the plugin keeps the game's hot keys off while a text box has the keyboard
                return f;
            }
            static TextBlock _help;
            static StackPanel _body;
            static ScrollViewer _bodyScroll;   // the scroll area of the tab's options
            static readonly List<Border> _tabButtons = new List<Border>();
            static readonly List<Ellipse> _tabDots = new List<Ellipse>();
            static TextBlock _status;
            static int _tab;
            static bool _unitRef;              // opened from a Unit Reference window: its tab is shown
            static bool _unitStatus;           // opened from a Unit Status window: its tab is shown
            static bool _tact;                 // opened from the Tactical Display window: its tab is shown
            static bool _log;                  // opened from the Event Log window: its tab is shown
            static bool _world;                // opened from the World View window: its tab is shown
            static bool _tfb;                  // opened from the campaign Task Force Builder: its own options are shown
            static bool _saidOpen;

            static bool _showAll;              // "Show all settings": the tabs of the other windows too. Off at the start of the game; the choice made by the user is kept until the game is closed (not saved)
            static Button _showAllButton;
            static TabScroll _tabScroll;   // the one-line strip of tabs (Rgreat.Kit)

            static Field _mapField;
            /// <summary>Desktop mode is on in the window (the edited value). The camera offset is then set in the World View window's own look menu, per desktop, with the picture in view: no tab for it here.</summary>
            static bool DeskOn() { return _mapField != null && Get(_mapField) is bool && (bool)Get(_mapField); }
            static bool TabShown(int i) { return !(Tabs[i] == TWorld && DeskOn()) && (_showAll || (Tabs[i] == TRef ? _unitRef : (Tabs[i] == TStatus || Tabs[i] == TTrack) ? _unitStatus : Tabs[i] == TCampaign ? _tfb : Tabs[i] == TTact ? _tact : Tabs[i] == TLog ? _log : Tabs[i] == TWorld ? _world : true)); }

            /// <summary>The button toggles the hidden tabs; not shown when no tab is hidden.</summary>
            static void UpdateShowAll()
            {
                if (_showAllButton == null) return;
                bool hidden = !_unitRef || !_unitStatus || !_tfb || !_tact || !_log || (!_world && !DeskOn());
                _showAllButton.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
                _showAllButton.Content = Loc.T(_showAll ? "btn.showFewer" : "btn.showAll");
            }

            public static bool Exists { get { return _f.Root != null; } }   // built and not closed (maybe hidden with the game's windows)

            public static bool IsOpen
            {
                get
                {
                    try { return _f.Root != null && _f.Host != null && VisualTreeHelper.GetParent(_f.Root) != null && _f.Root.IsVisible; }
                    catch { return false; }
                }
            }


            // ------------------------------------------------------------ the switch between the mods' windows (Rgreat.Hub.SettingsHub)
            const string SelfId = "alternative-game-interface";
            static bool _kept;   // taken off the screen by the switch to another mod's window, the edits kept (Open puts them back)
            public static bool IsDirty() { return Dirty(); }
            public static bool Kept() { return _kept; }

            /// <summary>The screenshots of the tabs of this window (the hub entry's [11]; also what the Debug button runs for the Game Interface itself).</summary>
            internal static System.Collections.IEnumerator ShotsRun() { return SettingsShots.Run(new ShotAdapter(), System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "AlternativeGameInterface_screens"), s => Trace(s), s => P.Logger.LogWarning(s)); }

            /// <summary>Where the window of another mod is put when it takes this one's place (the screen's panel).</summary>
            internal static UIElement ShotAnchor { get { return _f.Host != null ? (UIElement)_f.Host : _f.Top; } }
            internal static bool ShotOpen { get { return _f.Root != null && _f.Attached; } }
            public static void Hide() { Close(); _kept = true; }
            public static void CommitKept() { Commit(true); _kept = false; }
            public static void DiscardKept() { Cancel(); _kept = false; }

            // ------------------------------------------------------------ open / close

            /// <summary>
            /// Opens the window in the middle of the screen of 'anchor' (the gear clicked): in the topmost Grid / Canvas on the way up
            /// from it (not an items host), above everything. Open already: only the Unit Reference / Unit Status tabs follow where it was
            /// opened from. showAll: not used any more (the menus' entry opens in the same "fewer settings" mode; the mode is the user's choice, kept for the game session).
            /// </summary>
            public static void Open(AlternativeGameInterfacePlugin p, UIElement anchor, bool unitReference, bool unitStatus, bool taskForce = false, bool tactical = false, bool eventLog = false, bool showAll = false, bool worldView = false)
            {
                if (p == null || anchor == null) return;
                P = p;
                try
                {
                    Define(p);
                    bool tabChange = _unitRef != unitReference || _unitStatus != unitStatus || _tfb != taskForce || _tact != tactical || _log != eventLog || _world != worldView;
                    _tact = tactical; _log = eventLog; _world = worldView;
                    _tfb = taskForce;
                    if (taskForce) p.LoadStrictToProxy();
                    _unitRef = unitReference;
                    _unitStatus = unitStatus;
                    // opened from Unit Reference / Unit Status: that window's own tab first
                    if (tabChange && unitReference) _tab = Array.IndexOf(Tabs, TRef);
                    else if (tabChange && unitStatus) _tab = Array.IndexOf(Tabs, TStatus);
                    else if (taskForce) _tab = Array.IndexOf(Tabs, TCampaign);
                    else if (tabChange && tactical) _tab = Array.IndexOf(Tabs, TTact);
                    else if (tabChange && eventLog) _tab = Array.IndexOf(Tabs, TLog);
                    else if (tabChange && worldView) _tab = Array.IndexOf(Tabs, TWorld);
                    var panel = _f.Locate(anchor, null);
                    // still "open" but in another screen's tree (a mission / menu was loaded meanwhile, the old screen is not drawn any more):
                    // that copy is dropped and the window is built anew where the gear is
                    if (IsOpen && panel != null && !ReferenceEquals(panel, _f.Host)) { Trace("settings window: the open one belongs to another screen - built anew"); Close(); }
                    if (IsOpen)
                    {
                        UpdateShowAll();
                        if (tabChange || taskForce) ShowTab(_tab);
                        return;
                    }
                    Close();
                    if (_kept) _kept = false;   // put back after a switch to another mod's window: the edits are kept
                    else
                    {
                        _pending.Clear();   // a new copy of the settings
                        ClearDesktops();
                        _resets.Clear();
                        _osStates.Clear();
                    }
                    if (panel == null) { p.Logger.LogWarning("Alternative Game Interface: no place found for the settings window."); return; }
                    BuildWindow(); Trace("settings window: built");
                    _f.Mount(panel); Trace("settings window: mounted");
                    if (!TabShown(_tab)) _tab = 0;
                    ShowTab(_tab); Trace("settings window: tab shown");
                    Trace("settings window: in " + panel.GetType().Name + " (" + panel.ActualWidth + " x " + panel.ActualHeight + ")");
                    if (!_saidOpen) { _saidOpen = true; p.Logger.LogInfo("Settings window opened (in " + panel.GetType().Name + ")."); }
                }
                catch (Exception e) { p.Logger.LogWarning("Alternative Game Interface: settings window could not be opened: " + e.Message); Trace("SettingsWindow.Open: " + e); }
            }


            public static void Close()
            {
                if (P != null) P.SetTyping(false, null);
                _f.Unmount();
                _f.Root = null; _body = null; _help = null; _status = null; _commitFocused = null; _showAllButton = null; _tabScroll = null; _bodyScroll = null;
            }

            // ------------------------------------------------------------ building

            static TextBlock Text(string s, Color c, float size = 13f) { return _f.Text(s, c, size); }
            static Button Btn(string s, Action click, float minW = 0f) { return _f.Btn(s, click, minW); }
            static TextBox Edit(string text, float width) { return _f.Edit(text, width); }
            static Path Cross(float size) { return WinStyle.Cross(size); }
            static TextBlock GroupTitle(string s) { return WinStyle.GroupTitle(s, 6f, 0f); }
            static DependencyObject SafeParent(DependencyObject e) { return WinStyle.SafeParent(e); }
            static void Hover(Border row, string label, string text, string extra) { _f.Hover(row, () => label, () => text ?? "", () => extra); }




            static void BuildWindow()
            {
                _f.NewRoot();
                FillWindow();
            }

            /// <summary>The content of the window (its texts are read now): also used to rebuild it after the language changed.</summary>
            static void FillWindow()
            {
                var win = _f.Window();
                var parts = new List<UIElement>();

                parts.Add(_f.TitleBar(Loc.T("ui.title"), Cancel));   // drag to move, ✕ to close
                var modRow = _f.ModRow();   // the installed mods: a click switches to that mod's window
                if (modRow != null) parts.Add(modRow);

                // tabs
                var tabs = new StackPanel { Orientation = Orientation.Horizontal };   // one line, scrolled sideways (wheel / bar)
                _tabButtons.Clear(); _tabDots.Clear();
                for (int i = 0; i < Tabs.Length; i++)
                {
                    int idx = i;
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(Text(TabOf(Tabs[i]), CDim));
                    var dot = new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(5f, 0f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
                    row.Children.Add(dot);
                    _tabDots.Add(dot);
                    var tb = new Border { Child = row, Padding = new Thickness(10f, 6f, 10f, 6f), Margin = new Thickness(0f, 0f, 2f, 0f), BorderBrush = B(CLine),
                                          BorderThickness = new Thickness(1f, 1f, 1f, 0f), CornerRadius = new CornerRadius(3f, 3f, 0f, 0f), Background = B(CPanel) };
                    try { tb.Cursor = Cursors.Hand; } catch { }
                    tb.MouseLeftButtonUp += (o, e) => { ShowTab(idx); e.Handled = true; };
                    _tabButtons.Add(tb);
                    tabs.Children.Add(tb);
                }
                // the empty part of the last row is filled too
                _tabScroll = new TabScroll(tabs, B(CPanel), B(CLine), B(CText));   // one line, scrolled by the arrows at its ends / the wheel; the chosen tab is kept in view
                parts.Add(_f.ToolRow(_tabScroll.Root, Loc.Or("ui.search", "Search..."), () => Redraw(), () => TabChanged(_tab), RevertTab, Loc.Or("ui.revertTab", "Take back the edits of this tab that are not applied yet")));   // the strip of tabs, the search, the revert of the tab

                _body = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0f, 6f, 0f, 6f) };
                var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                _bodyScroll = scroll;
                var settingsArea = _f.Card(scroll);
                parts.Add(settingsArea);

                parts.Add(_f.HelpPane(Loc.T("ui.help")));   // fixed height: the window does not jump
                _help = _f.Help;

                // footer
                _showAllButton = Btn(Loc.T("btn.showAll"), () => { _showAll = !_showAll; UpdateShowAll(); Redraw(); }, 70f);
                parts.Add(_f.Footer(_showAllButton, out _status, Loc.T("btn.cancel"), Cancel, Loc.T("btn.apply"), () => Commit(false), Loc.T("btn.ok"), () => Commit(true)));
                UpdateShowAll();

                _f.Assemble(win, parts, settingsArea);   // all as tall as they need, the settings area takes the rest of the window's height
            }



            // ------------------------------------------------------------ placing on the screen
            // The game scales its UI: the size is set in screen pixels (at most the screen less 40) and the window is centred on
            // the screen, not in the panel it is put into.




            // ------------------------------------------------------------ values (a copy: nothing changes before Apply / OK)

            static readonly Dictionary<Field, object> _pending = new Dictionary<Field, object>();   // changed in the window, not applied yet
            static readonly HashSet<string> _resets = new HashSet<string>();                       // saved layouts to forget on Apply
            static readonly Dictionary<string, string> _osStates = new Dictionary<string, string>(); // RememberOpenWindows: open / closed to set on Apply

            /// <summary>The open / closed state kept for a window of RememberOpenWindows ("" = not known yet).</summary>
            static string OsState(string name)
            {
                ConfigEntry<string> e;
                return name.Length > 0 && P._osEntries.TryGetValue(name, out e) ? (e.Value ?? "").Split(',')[0].Trim().ToLowerInvariant() : "";
            }
            static Action _commitFocused;      // the text box with the keyboard: its text is taken before Apply / OK

            /// <summary>The value shown: changed in the window, else the one in use.</summary>
            static object Get(Field f) { object v; return _pending.TryGetValue(f, out v) ? v : f.Entry.BoxedValue; }

            /// <summary>For live previews (the camera offset): the value a setting has in the open window, changed or not, else its own.</summary>
            public static object Shown(ConfigEntryBase e)
            {
                if (_f.Root != null && _pending.Count > 0)
                    foreach (var kv in _pending) if (ReferenceEquals(kv.Key.Entry, e)) return kv.Value;
                return e.BoxedValue;
            }

            /// <summary>Changed in the window and not applied yet: the yellow dot.</summary>
            static bool Changed(Field f) { return _pending.ContainsKey(f); }

            static void Set(Field f, object v)
            {
                bool was = Get(f) is bool && (bool)Get(f);
                if (Equals(v, f.Entry.BoxedValue)) _pending.Remove(f);
                else _pending[f] = v;
                UpdateHeader();
                // a switch other rows depend on: show / hide them
                if ((Get(f) is bool && (bool)Get(f)) != was && Fields.Exists(x => x.Needs == f))
                    try { Redraw(); } catch (Exception e) { Trace("settings: " + e.Message); }
            }

            /// <summary>The row is shown: the switch it depends on (if any) is on in the window.</summary>
            static bool Shown(Field f) { return !f.Hidden && (!f.TfbOnly || _tfb || _showAll) && (f.Needs == null || (Get(f.Needs) is bool && (bool)Get(f.Needs))); }

            static bool Dirty() { return _pending.Count > 0 || _resets.Count > 0 || _osStates.Count > 0 || _dkEdited.Count > 0; }

            /// <summary>Apply / OK: the changed values are stored (and take effect), then the saved layouts marked are forgotten.</summary>
            static void Commit(bool close)
            {
                Rgreat.Hub.SettingsHub.CommitOthers(SelfId);   // the edits kept in the other mods' windows are applied too
                if (_commitFocused != null) { try { _commitFocused(); } catch (Exception e) { Trace("commit focused: " + e.Message); } }
                int n = 0;
                bool langChanged = false;
                foreach (var f in Fields)
                {
                    object v;
                    if (!_pending.TryGetValue(f, out v)) continue;
                    if (ReferenceEquals(f.Entry, P._language)) langChanged = true;
                    try { f.Entry.BoxedValue = v; P.AfterChange(f.Entry); n++; }
                    catch (Exception e) { Trace("settings: " + f.Entry.Definition.Key + ": " + e.Message); }
                }
                _pending.Clear();
                foreach (var k in _resets) P.ResetLayouts(k);
                int r = _resets.Count;
                _resets.Clear();
                // open / closed after the list itself: a window just added has its [OpenWindows] entry now
                int o = 0;
                foreach (var kv in _osStates)
                {
                    ConfigEntry<string> e;
                    if (!P._osEntries.TryGetValue(kv.Key, out e)) continue;
                    e.Value = kv.Value; o++;
                }
                _osStates.Clear();
                if (o > 0) P.Config.Save();
                CommitDesktops();
                Trace("settings applied: " + n + " value(s), " + r + " layout(s) forgotten, " + o + " open / closed state(s)");
                if (close) { Trace("settings window: closed (OK)"); Close(); }
                else
                {
                    ShowTab(_tab);
                    if (langChanged) RebuildAfterLayout();
                }
            }

            /// <summary>The language changed: the window is built again with the new texts - not inside the click, on the next layout pass.</summary>
            static void RebuildAfterLayout()
            {
                Loc.Refresh();
                var root = _f.Root;
                if (root == null) return;
                Noesis.EventHandler h = null;
                h = (o, e) =>
                {
                    root.LayoutUpdated -= h;
                    if (!ReferenceEquals(root, _f.Root)) return;
                    try
                    {
                        _f.Root.Children.Clear();
                        FillWindow();
                        ShowTab(_tab);
                    }
                    catch (Exception ex) { Trace("settings rebuild: " + ex.Message); }
                };
                root.LayoutUpdated += h;
                try { root.InvalidateMeasure(); } catch { }
            }

            /// <summary>Closed from outside (a new screen is loaded): as Cancel.</summary>
            public static void Discard() { Cancel(); }

            /// <summary>Cancel / ✕: what was changed in the window is dropped.</summary>
            static void Cancel()
            {
                Rgreat.Hub.SettingsHub.DiscardOthers(SelfId);   // the edits kept in the other mods' windows are dropped too
                if (_f.Root != null) Trace("settings window: closed (Cancel / X / discarded)");
                _pending.Clear();
                _resets.Clear();
                _osStates.Clear();
                ClearDesktops();
                Close();
            }

            static string Show(Field f, object v)
            {
                if (v is bool) return Loc.T((bool)v ? "val.on" : "val.off");
                if (v is float) return ((float)v).ToString("0.##", CultureInfo.InvariantCulture);
                if (f.Kind == 'm') return Loc.Or("val." + KeyOf(f) + "." + v, Convert.ToString(v, CultureInfo.InvariantCulture));
                return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
            }

            static string DefaultText(Field f)
            {
                var d = Show(f, f.Entry.DefaultValue);
                if (f.Kind == 'l' && d.Length == 0) d = Loc.T("val.gameLang");
                var text = Loc.F("val.default", d.Length == 0 ? Loc.T("val.empty") : d);
                // the game's own minimum height (0 = as in the game) is known once the Damage Control tab has been open
                if (ReferenceEquals(f.Entry, P._secMin))
                    text += "\n" + (P.SectionsGamePx > 0f ? Loc.F("val.gamePx", Fmt(P.SectionsGamePx)) : Loc.T("val.gameOpenTab"));
                return text;
            }

            static bool TryNum(string s, out float v)
            {
                return float.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
            }

            static bool ValidColour(string c)
            {
                c = (c ?? "").Trim();
                bool ok = (c.Length == 7 || c.Length == 9) && c[0] == '#';
                for (int i = 1; ok && i < c.Length; i++) ok = Uri.IsHexDigit(c[i]);
                return ok;
            }

            static Color ParseColour(string c)
            {
                c = c.Trim().Substring(1);
                byte a = 255; int o = 0;
                if (c.Length == 8) { a = byte.Parse(c.Substring(0, 2), NumberStyles.HexNumber); o = 2; }
                return Color.FromArgb(a, byte.Parse(c.Substring(o, 2), NumberStyles.HexNumber), byte.Parse(c.Substring(o + 2, 2), NumberStyles.HexNumber),
                                      byte.Parse(c.Substring(o + 4, 2), NumberStyles.HexNumber));
            }

            static List<string> Split(object v)
            {
                var list = new List<string>();
                foreach (var s in (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Split(','))
                {
                    var t = s.Trim();
                    if (t.Length > 0 && !list.Contains(t)) list.Add(t);
                }
                return list;
            }

            static string Join(List<string> list)
            {
                var parts = new List<string>();
                foreach (var s in list) { var t = (s ?? "").Trim(); if (t.Length > 0 && !parts.Contains(t)) parts.Add(t); }
                return string.Join(",", parts.ToArray());
            }

            /// <summary>Tab dots (something changed there), the status line and the buttons follow the changes.</summary>
            static void UpdateHeader()
            {
                _f.UpdateModDots();
                for (int t = 0; t < Tabs.Length && t < _tabDots.Count; t++)
                {
                    bool any = Tabs[t] == TMap && (_dkEdited.Count > 0 || _resets.Count > 0 || _osStates.Count > 0);
                    foreach (var f in Fields) if ((f.Tab == Tabs[t] || (Tabs[t] == TMap && f.Tab == TRemembered)) && Changed(f)) { any = true; break; }
                    _tabDots[t].Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                }
                UpdatePageDots();
                if (_status != null) _status.Text = Loc.T(Dirty() ? "st.dirty" : "st.clean");
                _f.UpdateTools();
            }

            // ------------------------------------------------------------ tabs and rows

            // ---- screenshots of every tab (SettingsShots.cs)
            static int _shotTab, _shotSub; static bool _shotAll;
            internal static bool ShotBegin()
            {
                if (_f.Root == null || _body == null || !_f.Root.IsVisible) return false;
                _shotTab = _tab; _shotSub = _mapSub; _shotAll = _showAll;
                _showAll = true; UpdateShowAll();
                return true;
            }
            internal static int ShotTabCount() { return Tabs.Length; }
            internal static int ShotPages(int tab)
            {
                if (HasPages(Tabs[tab])) return PageNames(Tabs[tab]).Length;
                if (Tabs[tab] != TMap) return 1;
                bool on = _mapField != null && Get(_mapField) is bool && (bool)Get(_mapField);
                return on ? 3 : 2;
            }
            internal static void ShotShow(int tab, int page) { if (HasPages(Tabs[tab])) _pageSel[Tabs[tab]] = page; else _mapSub = page; ShowTab(tab); }
            internal static string ShotName(int tab, int page) { return TabOf(Tabs[tab]) + (Tabs[tab] == TMap || HasPages(Tabs[tab]) ? "_p" + (page + 1) : ""); }
            internal static bool ShotRect(out float x, out float y, out float w, out float h) { return ShotFit.Rect(_f.Root, out x, out y, out w, out h); }
            internal static void ShotFitMax() { _f.ClearHelp(); ShotFit.Max(_f.Root, _f.Move); }
            internal static void ShotFitWidth() { ShotFit.Width(_f.Root, _body, _bodyScroll, _f.Move, _f.ShotMinW); }
            internal static void ShotFitHeight() { ShotFit.Height(_f.Root, _bodyScroll, _f.Move, 420f); }
            internal static void ShotEnd()
            {
                _showAll = _shotAll; _mapSub = _shotSub; UpdateShowAll();
                ShotFit.Restore(_f.Root, _f.Move, _f.W, _f.H);
                if (_body != null) ShowTab(_shotTab);
            }

            /// <summary>The window for the screenshot tool of the settings kit.</summary>
            internal sealed class ShotAdapter : IShotWindow
            {
                public bool Begin() { return ShotBegin(); }
                public int TabCount { get { return ShotTabCount(); } }
                public bool TabUsable(int tab) { return true; }   // every tab is shown (ShotBegin: "show all")
                public int Pages(int tab) { return ShotPages(tab); }
                public void Show(int tab, int page) { ShotShow(tab, page); }
                public string Name(int tab, int page) { return ShotName(tab, page); }
                public bool Rect(out float x, out float y, out float w, out float h) { return ShotRect(out x, out y, out w, out h); }
                public void FitMax() { ShotFitMax(); }
                public void FitWidth() { ShotFitWidth(); }
                public void FitHeight() { ShotFitHeight(); }
                public void End() { ShotEnd(); }
            }

            /// <summary>The tab is built again where it stands (a switch, the search): the scroll position stays.</summary>
            static void Redraw() { _f.KeepScroll(_bodyScroll, () => ShowTab(_tab)); }

            static bool FieldHit(Field f) { return Shown(f) && _f.Match(LabelOf(f)); }
            static bool TabHasHits(int i) { foreach (var f in Fields) if (InTab(f, Tabs[i]) && FieldHit(f)) return true; return false; }

            static void ShowFound()
            {
                string group = null; int n = 0;
                foreach (var f in Fields)
                {
                    if (!InTab(f, Tabs[_tab]) || !FieldHit(f)) continue;
                    FrameworkElement row;
                    try { row = f.Kind == 'w' ? ListRow(f) : Row(f); } catch (Exception e) { Trace("settings row '" + f.Label + "' failed: " + e); continue; }   // the title of a group goes in only with a row under it
                    if (f.Group != group) { group = f.Group; _body.Children.Add(GroupTitle(GroupOf(group))); }
                    _body.Children.Add(row); n++;
                }
                if (n == 0) { var t = Text(Loc.Or("ui.nothing", "Nothing found."), CDim); t.Margin = new Thickness(12f, 14f, 12f, 0f); _body.Children.Add(t); }
            }

            static bool InTab(Field f, string tab) { return f.Tab == tab || (tab == TMap && f.Tab == TRemembered); }
            static bool TabChanged(int t) { foreach (var f in Fields) if (InTab(f, Tabs[t]) && Changed(f)) return true; return false; }
            static bool GroupChanged(string tab, string group) { foreach (var f in Fields) if (f.Tab == tab && f.Group == group && Changed(f)) return true; return false; }

            /// <summary>The saved values back into the options picked (the edits not applied are dropped; nothing closes).</summary>
            static void RevertWhere(Func<Field, bool> pick)
            {
                foreach (var f in Fields) if (pick(f)) _pending.Remove(f);
                Redraw();
            }

            static void RevertTab() { string t = Tabs[_tab]; RevertWhere(x => InTab(x, t)); }

            static void ShowTab(int t)
            {
                if (_body == null) return;
                _tab = Math.Max(0, Math.Min(Tabs.Length - 1, t));
                if (!TabShown(_tab)) _tab = 0;
                bool searching = !string.IsNullOrEmpty(_f.Query);
                if (searching && !TabHasHits(_tab)) for (int i0 = 0; i0 < Tabs.Length; i0++) if (TabShown(i0) && TabHasHits(i0)) { _tab = i0; break; }   // the search: a tab with nothing found is left
                for (int i = 0; i < _tabButtons.Count; i++)
                {
                    _tabButtons[i].Visibility = TabShown(i) && (!searching || TabHasHits(i)) ? Visibility.Visible : Visibility.Collapsed;   // the tabs with nothing found are hidden
                    bool on = i == _tab;
                    _tabButtons[i].Background = B(on ? CCard : CPanel);
                    var row = _tabButtons[i].Child as StackPanel;
                    var label = row != null && row.Children.Count > 0 ? row.Children[0] as TextBlock : null;
                    if (label != null) label.Foreground = B(on ? CText : CDim);
                }
                if (_tabScroll != null && _tab < _tabButtons.Count) _tabScroll.Reveal(_tabButtons[_tab]);   // the chosen tab is kept in view in the scrolled strip
                _commitFocused = null;
                _body.Children.Clear();
                if (!string.IsNullOrEmpty(_f.Query)) { ShowFound(); UpdateHeader(); return; }
                string group = null;
                // desktop mode: its own tab control (Settings / Desktops) inside the tab
                string tabKey = Tabs[_tab];
                if (Tabs[_tab] == TMap)
                {
                    int sub = MapSubTabs();
                    if (sub == 2) { BuildDesktops(); UpdateHeader(); return; }
                    if (sub == 1) tabKey = TRemembered;   // the remembered windows: a page of the same tab
                }
                if (tabKey == TMap && _mapField != null && Changed(_mapField) && Get(_mapField) is bool && (bool)Get(_mapField)) _body.Children.Add(ApplyDesktopOffer());
                string page = HasPages(tabKey) ? PageStrip(tabKey) : null;   // a long tab: split into pages by its groups
                foreach (var f in Fields)
                {
                    if (f.Tab != tabKey || !Shown(f) || (page != null && PageOf(f) != page)) continue;
                    string why = ConflictOf(f);
                    if (f.Group != group)
                    {
                        group = f.Group;
                        { string gr = group, tk = f.Tab; _body.Children.Add(_f.GroupHeader(GroupOf(gr), () => GroupChanged(tk, gr), () => RevertWhere(x => x.Tab == tk && x.Group == gr), Loc.Or("ui.revertGroup", "Take back the edits of this group that are not applied yet"))); }
                        if (why != null) _body.Children.Add(new TextBlock { Text = why, Foreground = B(CWarn), FontSize = 11f, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12f, 2f, 12f, 2f) });
                    }
                    var row = f.Kind == 'w' ? ListRow(f) : Row(f);
                    if (why != null) { var rb = row as Border; if (rb != null && rb.Child is UIElement) ((UIElement)rb.Child).IsEnabled = false; else row.IsEnabled = false; row.Opacity = 0.45f; }
                    _body.Children.Add(row);
                }
                UpdateHeader();
            }

            /// <summary>Desktop mode was just switched on in the window: it starts with Apply, offered right here.</summary>
            static FrameworkElement ApplyDesktopOffer()
            {
                var g = new Grid { Margin = new Thickness(12f, 4f, 12f, 4f) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var t = Text(Loc.Or("dk.offer", "Desktop mode starts when the settings are applied."), CText);
                t.TextWrapping = TextWrapping.Wrap; t.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(t);
                var b = Btn(Loc.Or("dk.applyNow", "Apply now"), () => { Commit(false); try { Redraw(); } catch (Exception e) { Trace("settings: " + e.Message); } }, 90f);
                b.Margin = new Thickness(10f, 0f, 0f, 0f);
                Grid.SetColumn(b, 1);
                g.Children.Add(b);
                return new Border { Child = g, Background = B(Color.FromRgb(34, 38, 44)), BorderBrush = B(CLine), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f), Padding = new Thickness(10f, 8f, 10f, 8f), Margin = new Thickness(0f, 4f, 0f, 4f) };
            }


            static Grid RowGrid()
            {
                var g = new Grid { Background = Clear };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260f) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100f) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30f) });
                return g;
            }

            static Ellipse Dot()
            {
                return new Ellipse { Width = 6f, Height = 6f, Fill = B(CWarn), Margin = new Thickness(0f, 0f, 6f, 0f), VerticalAlignment = VerticalAlignment.Center };
            }

            /// <summary>Label with the "changed" dot; returns the action that shows / hides the dot.</summary>
            static Action Label(Grid g, Field f)
            {
                var lab = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var dot = Dot();
                lab.Children.Add(dot);
                lab.Children.Add(Text(LabelOf(f), CText));
                g.Children.Add(lab);
                return () => { dot.Visibility = Changed(f) ? Visibility.Visible : Visibility.Hidden; };
            }

            static Button SmallButton(object content, string tip, Action click)
            {
                var b = new Button { Content = content, Width = 24f, Height = 22f, Padding = new Thickness(0f), HorizontalAlignment = HorizontalAlignment.Right,
                                     VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
                                     VerticalContentAlignment = VerticalAlignment.Center };
                try { ToolTipService.SetToolTip(b, tip); } catch { }
                b.Click += (o, e) => { try { click(); } catch (Exception ex) { Trace("button: " + ex.Message); } };
                return b;
            }

            static Button BackButton(Field f, Action refresh, Action mark)
            {
                return SmallButton("↺", Loc.T("tip.default"), () => { Set(f, f.Entry.DefaultValue); refresh(); mark(); });
            }

            static FrameworkElement Row(Field f)
            {
                var g = RowGrid();
                var outer = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 4f), Background = Clear };
                var mark = Label(g, f);
                Action refresh;
                if (f.Kind == 'p') return PadRow(f);
                FrameworkElement editor = f.Kind == 'a' ? ActionEditor(f, out refresh) : f.Kind == 'b' ? BoolEditor(f, mark, out refresh) : f.Kind == 'f' ? NumberEditor(f, mark, out refresh) : f.Kind == 'l' ? LanguageEditor(f, mark, out refresh) : f.Kind == 'm' ? ChoiceEditor(f, mark, out refresh) : TextEditor(f, mark, out refresh);
                Grid.SetColumn(editor, 1);
                g.Children.Add(editor);

                if (f.Kind == 'a') { mark(); Hover(outer, LabelOf(f), HelpOf(f), null); return outer; }   // a button: no default value, nothing to reset
                var def = new TextBlock { Text = DefaultText(f), Foreground = B(CDim), FontSize = 11f, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(def, 2);
                g.Children.Add(def);
                var rst = BackButton(f, refresh, mark);
                Grid.SetColumn(rst, 3);
                g.Children.Add(rst);
                mark();
                Hover(outer, LabelOf(f), HelpOf(f), ConflictOf(f));
                return outer;
            }

            /// <summary>A 2D pad for two numbers (x = f, y = f.Pair): a sight with the camera dot on it, dragged with the mouse (also a click
            /// anywhere on the pad), arrow buttons at the edges step by f.Step (held: repeat). The pad's edges are the limits.</summary>
            static FrameworkElement PadRow(Field f)
            {
                var fy = f.Pair;
                var g = RowGrid();
                var outer = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 4f), Background = Clear };
                var lab = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var dotMark = Dot();
                lab.Children.Add(dotMark); lab.Children.Add(Text(LabelOf(f), CText));
                g.Children.Add(lab);
                Action mark = () => { dotMark.Visibility = Changed(f) || Changed(fy) ? Visibility.Visible : Visibility.Hidden; };
                Func<Field, float> cur = x => { var o = Get(x); return o is float ? (float)o : 0f; };
                Action refresh;
                var ed = Pad(() => cur(f), () => cur(fy), (x, y) => { Set(f, x); Set(fy, y); mark(); }, f.Step, f.Max, out refresh);
                Grid.SetColumn(ed, 1);
                g.Children.Add(ed);
                var def = new TextBlock { Text = Loc.F("val.default", Show(f, f.Entry.DefaultValue) + ", " + Show(fy, fy.Entry.DefaultValue)), Foreground = B(CDim), FontSize = 11f,
                                          TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(def, 2); g.Children.Add(def);
                var rst = SmallButton("↺", Loc.T("tip.default"), () => { Set(f, f.Entry.DefaultValue); Set(fy, fy.Entry.DefaultValue); refresh(); mark(); });
                Grid.SetColumn(rst, 3); g.Children.Add(rst);
                mark();
                Hover(outer, LabelOf(f), HelpOf(f), ConflictOf(f));
                return outer;
            }

            /// <summary>A setting changed elsewhere (the look menu) while the window is open: its unapplied value here is dropped, the shown tab rebuilt.</summary>
            public static void Forget(ConfigEntryBase e)
            {
                Field hit = null;
                foreach (var kv in _pending) if (ReferenceEquals(kv.Key.Entry, e)) { hit = kv.Key; break; }
                if (hit != null) _pending.Remove(hit);
                if (IsOpen && Fields.Exists(f => ReferenceEquals(f.Entry, e) && f.Tab == Tabs[_tab])) try { ShowTab(_tab); } catch { }
            }

            public static bool PadDragging;   // the dot is being dragged (the look menu stays open meanwhile)

            /// <summary>The 2D pad itself (also in the World View window's look menu): a sight with the camera dot, dragged with the mouse (or a
            /// click anywhere on it), arrows at the edges step by <paramref name="step"/> (held: repeat); the edges are ±<paramref name="max"/>; + y = up.</summary>
            public static FrameworkElement Pad(Func<float> gx, Func<float> gy, Action<float, float> set, float step, float max, out Action refresh, float size = 1f)
            {
                float W = 168f * size, H = 96f * size, Dot2 = size < 1f ? 12f : 14f, Ring = size < 1f ? 20f : 26f, A = size < 1f ? 16f : 18f, AL = size < 1f ? 32f : 40f;
                var canvas = new Canvas { Width = W, Height = H, Background = B(CField), ClipToBounds = true };
                Action<float, float, float, float, Color> rect = (x, y, w, h, c) => { var r = new Rectangle { Width = w, Height = h, Fill = B(c), IsHitTestVisible = false }; Canvas.SetLeft(r, x); Canvas.SetTop(r, y); canvas.Children.Add(r); };
                // the sight: centre lines, ticks every 5 %, a ring
                rect(0f, H / 2f, W, 1f, CLine); rect(W / 2f, 0f, 1f, H, CLine);
                for (int i = -5; i <= 5; i++)
                {
                    if (i == 0) continue;
                    float tx = W / 2f + i * (W / 2f) / 5f, ty = H / 2f + i * (H / 2f) / 5f;
                    rect(tx, H / 2f - 3f, 1f, 7f, CLine); rect(W / 2f - 3f, ty, 7f, 1f, CLine);
                }
                var ring = new Ellipse { Width = Ring, Height = Ring, Stroke = B(CDim), StrokeThickness = 1f, IsHitTestVisible = false };
                Canvas.SetLeft(ring, W / 2f - Ring / 2f); Canvas.SetTop(ring, H / 2f - Ring / 2f); canvas.Children.Add(ring);
                var cam = new Ellipse { Width = Dot2, Height = Dot2, Fill = B(CWarn), Stroke = B(CBg), StrokeThickness = 2f, IsHitTestVisible = false };
                canvas.Children.Add(cam);
                // the values sit in the bottom corners, beside ▼ (no empty column)
                var valX = Text("", CText, 12f); valX.VerticalAlignment = VerticalAlignment.Center; valX.HorizontalAlignment = HorizontalAlignment.Left; valX.Margin = new Thickness(2f, 0f, 0f, 0f);
                var valY = Text("", CText, 12f); valY.VerticalAlignment = VerticalAlignment.Center; valY.HorizontalAlignment = HorizontalAlignment.Right; valY.Margin = new Thickness(0f, 0f, 2f, 0f);
                Func<float, string> num = v => v.ToString("0.##", CultureInfo.InvariantCulture);
                Action place = () =>
                {
                    float x = gx(), y = gy();
                    Canvas.SetLeft(cam, W / 2f + x / max * (W / 2f) - Dot2 / 2f);
                    Canvas.SetTop(cam, H / 2f - y / max * (H / 2f) - Dot2 / 2f);
                    valX.Text = "↔ " + num(x) + " %"; valY.Text = "↕ " + num(y) + " %";
                };
                Func<float, float> fit = v => Math.Max(-max, Math.Min(max, (float)Math.Round(v / step) * step));
                Action<float, float> put = (x, y) =>
                {
                    x = fit(x); y = fit(y);
                    if (Math.Abs(x - gx()) > 1e-6f || Math.Abs(y - gy()) > 1e-6f) set(x, y);
                    place();
                };
                bool down = false;
                Action<Point> at = pt => put((pt.X - W / 2f) / (W / 2f) * max, -(pt.Y - H / 2f) / (H / 2f) * max);
                canvas.MouseLeftButtonDown += (o, e) => { down = true; PadDragging = true; canvas.CaptureMouse(); at(e.GetPosition(canvas)); e.Handled = true; };
                canvas.MouseMove += (o, e) => { if (down) at(e.GetPosition(canvas)); };
                canvas.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; PadDragging = false; canvas.ReleaseMouseCapture(); e.Handled = true; };
                canvas.LostMouseCapture += (o, e) => { down = false; PadDragging = false; };

                var pad = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                pad.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(A + 2f) });
                pad.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                pad.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(A + 2f) });
                pad.RowDefinitions.Add(new RowDefinition { Height = new GridLength(A + 2f) });
                pad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                pad.RowDefinitions.Add(new RowDefinition { Height = new GridLength(A + 2f) });
                Action<string, int, int, float, float> arrow = (s, row, col, dx, dy) =>
                {
                    // the arrow is drawn (the font draws ◀ ▶ as coloured emoji)
                    object glyph = s;
                    try
                    {
                        string data = s == "▲" ? "M0,6 L5,0 L10,6 Z" : s == "▼" ? "M0,0 L10,0 L5,6 Z" : s == "◀" ? "M6,0 L6,10 L0,5 Z" : "M0,0 L6,5 L0,10 Z";
                        glyph = GUI.ParseXaml("<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"" + data + "\" " +
                                              "Fill=\"{Binding Foreground, RelativeSource={RelativeSource AncestorType=RepeatButton}}\"/>");
                    }
                    catch { }
                    var b = new RepeatButton { Content = glyph, Padding = new Thickness(0f), FontSize = 10f, Delay = 350, Interval = 60, Foreground = B(CDim),
                                               Width = col == 1 ? AL : A, Height = row == 1 ? AL : A,
                                               HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    var tpl = ArrowTemplate();
                    if (tpl != null) b.Template = tpl;
                    b.Click += (o, e) => put(gx() + dx * step, gy() + dy * step);
                    Grid.SetRow(b, row); Grid.SetColumn(b, col); pad.Children.Add(b);
                };
                arrow("▲", 0, 1, 0f, 1f); arrow("▼", 2, 1, 0f, -1f); arrow("◀", 1, 0, -1f, 0f); arrow("▶", 1, 2, 1f, 0f);
                var frame = new Border { Child = canvas, BorderBrush = B(CLine), BorderThickness = new Thickness(1f) };
                Grid.SetRow(frame, 1); Grid.SetColumn(frame, 1); pad.Children.Add(frame);
                Grid.SetRow(valX, 2); Grid.SetColumn(valX, 0); Grid.SetColumnSpan(valX, 2); pad.Children.Insert(0, valX);
                Grid.SetRow(valY, 2); Grid.SetColumn(valY, 1); Grid.SetColumnSpan(valY, 2); pad.Children.Insert(0, valY);
                refresh = place;
                place();
                return pad;
            }

            /// <summary>One look for the pad's arrows (the game's button style draws narrow and wide buttons differently): a flat box, lighter under the mouse / pressed.</summary>
            static ControlTemplate _arrowTpl; static bool _arrowTried;
            static ControlTemplate ArrowTemplate()
            {
                if (_arrowTried) return _arrowTpl;
                _arrowTried = true;
                try
                {
                    _arrowTpl = (ControlTemplate)GUI.ParseXaml(
                        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"RepeatButton\">" +
                        "<Border x:Name=\"b\" Background=\"#2D3239\" BorderBrush=\"#464C56\" BorderThickness=\"1\" CornerRadius=\"2\">" +
                        "<ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Border>" +
                        "<ControlTemplate.Triggers>" +
                        "<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"b\" Property=\"Background\" Value=\"#3C434D\"/><Setter Property=\"Foreground\" Value=\"#E6E8EB\"/></Trigger>" +
                        "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"b\" Property=\"Background\" Value=\"#505965\"/></Trigger>" +
                        "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter TargetName=\"b\" Property=\"Opacity\" Value=\"0.5\"/></Trigger>" +
                        "</ControlTemplate.Triggers></ControlTemplate>");
                }
                catch (Exception e) { Trace("settings: arrow template: " + e.Message); }
                return _arrowTpl;
            }

            static string ConflictOf(Field f) { try { return f.Conflict == null ? null : f.Conflict(); } catch { return null; } }


            /// <summary>A text box whose text is taken on Enter / leaving it, and before Apply / OK while it has the keyboard.</summary>
            static void Commits(TextBox box, Action typed, Action undo)
            {
                box.GotFocus += (o, e) => _commitFocused = typed;
                box.LostFocus += (o, e) => { if (_commitFocused == typed) _commitFocused = null; typed(); };
                box.KeyDown += (o, e) =>
                {
                    if (e.Key == Key.Return) { typed(); e.Handled = true; }
                    else if (e.Key == Key.Escape) { undo(); e.Handled = true; }
                };
            }

            static FrameworkElement NumberEditor(Field f, Action mark, out Action refresh)
            {
                return _f.Number(() => Show(f, Get(f)), () => { var o = Get(f); return o is float ? (float)o : 0f; }, v => Set(f, v), f.Step, f.Min, f.Max,
                                 string.IsNullOrEmpty(f.Unit) ? null : UnitOf(f), mark, out refresh, Commits);
            }

            /// <summary>A button for a one-shot action: it switches the (self-clearing) entry on at once, the plugin runs the action and clears it.</summary>
            static FrameworkElement ActionEditor(Field f, out Action refresh)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(Btn(Loc.T("btn.run"), () => { f.Entry.BoxedValue = true; }, 70f));
                if (!string.IsNullOrEmpty(f.Unit)) { var u = Text(UnitOf(f), CDim, 12f); u.Margin = new Thickness(4f, 0f, 0f, 0f); sp.Children.Add(u); }
                refresh = () => { };
                return sp;
            }

            static FrameworkElement BoolEditor(Field f, Action mark, out Action refresh)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(_f.Check(() => { var o = Get(f); return o is bool && (bool)o; }, on => { Set(f, on); mark(); }, out refresh));
                if (!string.IsNullOrEmpty(f.Unit)) { var u = Text(UnitOf(f), CDim, 12f); u.Margin = new Thickness(8f, 0f, 0f, 0f); sp.Children.Add(u); }
                return sp;
            }

            static FrameworkElement LanguageEditor(Field f, Action mark, out Action refresh)
            {
                var codes = new List<string> { "" };
                var labels = new List<string> { Loc.T("val.gameLang") };
                foreach (var l in Loc.GameLanguages()) { codes.Add(l.Key); labels.Add(l.Value); }
                return CodeCombo(f, codes, labels, 160f, mark, out refresh);
            }

            /// <summary>A drop-down over a list of values (codes) with their texts; the editor of a field.</summary>
            static FrameworkElement CodeCombo(Field f, List<string> codes, List<string> labels, float width, Action mark, out Action refresh)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(_f.Combo(labels, () => { int k = codes.IndexOf((Convert.ToString(Get(f), CultureInfo.InvariantCulture) ?? "").Trim()); return k < 0 ? 0 : k; },
                                         k => { Set(f, codes[k]); mark(); }, width, 22f, out refresh));
                return sp;
            }

            static FrameworkElement ChoiceEditor(Field f, Action mark, out Action refresh)
            {
                var list = f.Entry.Description == null ? null : f.Entry.Description.AcceptableValues as AcceptableValueList<string>;
                var codes = list == null ? new List<string>() : new List<string>(list.AcceptableValues);
                var labels = new List<string>();
                foreach (var c in codes) labels.Add(Show(f, c));
                return CodeCombo(f, codes, labels, 180f, mark, out refresh);
            }

            /// <summary>Key name, colour (with a swatch) or a short text.</summary>
            static FrameworkElement TextEditor(Field f, Action mark, out Action refresh)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Border swatch = null;
                if (f.Kind == 'c')
                {
                    swatch = new Border { Width = 18f, Height = 18f, Margin = new Thickness(0f, 0f, 6f, 0f), BorderBrush = B(CLine), BorderThickness = new Thickness(1f), VerticalAlignment = VerticalAlignment.Center };
                    sp.Children.Add(swatch);
                }
                var box = Edit(Show(f, Get(f)), f.Kind == 's' ? 160f : 110f);
                Action show = () =>
                {
                    box.Text = Show(f, Get(f));
                    if (swatch != null && ValidColour(box.Text)) swatch.Background = B(ParseColour(box.Text));
                };
                Action typed = () =>
                {
                    var text = (box.Text ?? "").Trim();
                    object v = null;
                    if (f.Kind == 'k') { try { v = Enum.Parse(typeof(UnityEngine.KeyCode), text, true); } catch { } }
                    else if (f.Kind == 'c') { if (ValidColour(text)) v = text.ToUpperInvariant(); }
                    else v = text;
                    if (v != null) Set(f, v);
                    show(); mark();   // not valid: back to the value as it was
                };
                Commits(box, typed, show);
                sp.Children.Add(box);
                if (!string.IsNullOrEmpty(f.Unit)) { var u = Text(UnitOf(f), CDim, 12f); u.Margin = new Thickness(8f, 0f, 0f, 0f); sp.Children.Add(u); }
                show();
                refresh = show;
                return sp;
            }

            // ------------------------------------------------------------ window lists

            /// <summary>
            /// A list of window patterns (WindowPatterns, RememberOpenWindows): a line per window - its name to edit, what the mod keeps
            /// for it (the saved position / open or closed), ✕ to remove it; '+ Add window' below. Remembered windows also get ↺ to forget
            /// the saved position (on Apply). A new or renamed name has the yellow dot.
            /// </summary>
            static FrameworkElement ListRow(Field f)
            {
                var panel = new StackPanel { Orientation = Orientation.Vertical };
                var g = RowGrid();
                var head = new Border { Child = g, Padding = new Thickness(12f, 4f, 6f, 2f), Background = Clear };
                var mark = Label(g, f);
                var unit = Text(UnitOf(f), CDim, 12f);
                Grid.SetColumn(unit, 1); Grid.SetColumnSpan(unit, 2);
                g.Children.Add(unit);
                panel.Children.Add(head);

                var items = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(24f, 0f, 6f, 2f) };
                panel.Children.Add(items);
                bool layouts = ReferenceEquals(f.Entry, P._windows);
                var list = Split(Get(f));
                Action rebuild = null;
                Action<int> focus = null;
                rebuild = () =>
                {
                    items.Children.Clear();
                    var applied = Split(f.Entry.BoxedValue);
                    for (int i = 0; i < list.Count; i++) items.Children.Add(ListItem(f, list, i, applied, layouts, rebuild, mark, i == list.Count - 1 ? focus : null));
                    var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0f, 4f, 0f, 4f) };
                    bar.Children.Add(Btn(Loc.T("btn.addWindow"), () =>
                    {
                        if (list.Count > 0 && list[list.Count - 1].Length == 0) return; // an empty one waits already
                        list.Add("");
                        focus = k => { };
                        rebuild();
                    }));
                    if (layouts)
                    {
                        bool any = false;
                        foreach (var k in list) { Layout l; if (P._saved.TryGetValue(k, out l) && !_resets.Contains(k)) any = true; }
                        var all = Btn(Loc.T("btn.forgetAll"), () =>
                        {
                            foreach (var k in list) { Layout l; if (P._saved.TryGetValue(k, out l)) _resets.Add(k); }
                            UpdateHeader(); rebuild();
                        });
                        all.IsEnabled = any;
                        try { ToolTipService.SetToolTip(all, Loc.T("tip.forgetAll")); } catch { }
                        bar.Children.Add(all);
                    }
                    var hint = Text(Loc.T("hint.emptyName"), CDim, 11f);
                    hint.Margin = new Thickness(6f, 0f, 0f, 0f);
                    bar.Children.Add(hint);
                    items.Children.Add(bar);
                    focus = null;
                    mark();
                };
                var back = SmallButton("↺", Loc.T("tip.defaultList"), () =>
                {
                    list.Clear(); list.AddRange(Split(f.Entry.DefaultValue));
                    Set(f, Join(list)); rebuild();
                });
                Grid.SetColumn(back, 3);
                g.Children.Add(back);
                rebuild();
                Hover(head, LabelOf(f), HelpOf(f), Loc.F("val.defaultCap", Show(f, f.Entry.DefaultValue)));
                return panel;
            }

            static FrameworkElement ListItem(Field f, List<string> list, int i, List<string> applied, bool layouts, Action rebuild, Action mark, Action<int> focus)
            {
                var g = new Grid { Background = Clear };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12f) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230f) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28f) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28f) });
                var outer = new Border { Child = g, Padding = new Thickness(0f, 1f, 0f, 1f), Background = Clear };
                var name = list[i];

                Layout l = default(Layout);
                bool saved = layouts && name.Length > 0 && P._saved.TryGetValue(name, out l);
                bool forget = saved && _resets.Contains(name);
                var dot = Dot();
                dot.Visibility = name.Length == 0 || !applied.Contains(name) || forget || (!layouts && _osStates.ContainsKey(name)) ? Visibility.Visible : Visibility.Hidden;
                g.Children.Add(dot);

                var box = Edit(name, 0f);
                box.TextAlignment = TextAlignment.Left;
                box.Padding = new Thickness(4f, 0f, 4f, 0f);
                Grid.SetColumn(box, 1);
                g.Children.Add(box);
                // by value, not by index: the list may have been rebuilt meanwhile (a box loses the keyboard when its row is replaced)
                Action typed = () =>
                {
                    int k = list.IndexOf(name);
                    if (k < 0) return;            // this row is gone already
                    var t = (box.Text ?? "").Trim().Replace(",", "");
                    if (t == name) { box.Text = t; return; }
                    if (t.Length > 0 && list.Contains(t)) { box.Text = name; return; }   // already in the list
                    if (t.Length == 0) list.RemoveAt(k); else list[k] = t;
                    Set(f, Join(list));
                    rebuild();
                };
                Commits(box, typed, () => box.Text = name);
                if (focus != null) box.Loaded += (o, e) => { try { box.Focus(); } catch { } };

                if (layouts)
                {
                    var info = name.Length == 0 ? "" : forget ? Loc.T("info.forgotten")
                             : saved ? (float.IsNaN(l.W) ? "" : Loc.F("info.at", Fmt(l.W), Fmt(l.H), Fmt(l.X), Fmt(l.Y))) + (l.Max ? " · " + Loc.T("info.max") : "")
                             : Loc.T("info.notSaved");
                    var it = Text(info, forget ? CWarn : CDim, 12f);
                    it.Margin = new Thickness(8f, 0f, 4f, 0f);
                    it.TextTrimming = TextTrimming.CharacterEllipsis;
                    Grid.SetColumn(it, 2);
                    g.Children.Add(it);
                }
                else if (name.Length > 0)
                {
                    // open / closed at the next mission: the kept state, editable (set on Apply)
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8f, 0f, 4f, 0f), VerticalAlignment = VerticalAlignment.Center };
                    var cb = new ComboBox { Width = 96f, MinHeight = 22f, VerticalContentAlignment = VerticalAlignment.Center };
                    cb.Items.Add(Loc.T("val.open"));
                    cb.Items.Add(Loc.T("val.closed"));
                    string st;
                    if (!_osStates.TryGetValue(name, out st)) st = OsState(name);
                    cb.SelectedIndex = st == "open" ? 0 : st == "closed" ? 1 : -1;
                    try { ToolTipService.SetToolTip(cb, Loc.T(st.Length == 0 ? "tip.notSeen" : "tip.asLeft")); } catch { }
                    cb.SelectionChanged += (o, e) =>
                    {
                        if (cb.SelectedIndex < 0) return;
                        var v = cb.SelectedIndex == 0 ? "open" : "closed";
                        if (v == OsState(name)) _osStates.Remove(name); else _osStates[name] = v;
                        dot.Visibility = !applied.Contains(name) || _osStates.ContainsKey(name) ? Visibility.Visible : Visibility.Hidden;
                        UpdateHeader();
                    };
                    sp.Children.Add(cb);
                    Grid.SetColumn(sp, 2);
                    g.Children.Add(sp);
                }

                if (layouts)
                {
                    var fg = SmallButton("↺", Loc.T(forget ? "tip.keepPos" : "tip.forgetPos"), () =>
                    {
                        if (!_resets.Remove(name)) _resets.Add(name);
                        UpdateHeader(); rebuild();
                    });
                    fg.IsEnabled = saved;
                    Grid.SetColumn(fg, 3);
                    g.Children.Add(fg);
                }
                var rm = SmallButton(Cross(8f), Loc.T("tip.remove"), () =>
                {
                    if (!list.Remove(name)) return;
                    Set(f, Join(list));
                    rebuild();
                });
                Grid.SetColumn(rm, 4);
                g.Children.Add(rm);
                Hover(outer, name.Length == 0 ? Loc.T("hov.newWindow") : name, Loc.T(layouts ? "hov.layoutHelp" : "hov.openHelp"), null);
                return outer;
            }
        }
    }
}
