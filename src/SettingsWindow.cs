using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using Noesis;

namespace SmartWindows
{
    public partial class SmartWindowsPlugin
    {
        /// <summary>
        /// The settings window, opened by the gear in the title bar of a remembered window. Built and styled as the settings window
        /// of Alternative Damage Model: own dark window (title bar to drag, ✕), tabs, a card with grouped rows - label with a yellow
        /// dot when the value was changed in the window and is not applied yet, editor (− / text box / + with step and limits, check box,
        /// text, an editable list of windows), the default, ↺ to it -, a help pane, resizing by the edges. Works on a copy: Apply / OK store and apply the changes, Cancel / ✕ drop them.
        /// The Unit Reference / Unit Status tabs are shown only when the window is opened from such a window.
        /// While a text box has the keyboard, the game's hot keys are off (InputHandler.typingActive).
        /// </summary>
        static class SettingsWindow
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
            }

            const string TWindows = "Windows", TRemembered = "Remembered Windows", TFrame = "Frame", TMission = "In Mission", TFiles = "Game Menus", TDebug = "Debug", TCampaign = "Campaign", TStatus = "Unit Status", TRef = "Unit Reference";
            static readonly string[] Tabs = { TWindows, TRemembered, TFrame, TMission, TFiles, TStatus, TRef, TCampaign, TDebug };
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
            public static void RegisterTexts(SmartWindowsPlugin p)
            {
                Define(p);
                foreach (var t in Tabs) Loc.Register("tab." + t, t);
                foreach (var f in Fields)
                {
                    Loc.Register("grp." + f.Group, f.Group);
                    Loc.Register("lbl." + KeyOf(f), f.Label);
                    if (!string.IsNullOrEmpty(f.Unit)) Loc.Register("unit." + KeyOf(f), f.Unit);
                    if (f.Entry.Description != null) Loc.Register("help." + KeyOf(f), f.Entry.Description.Description);   // also the entries of the other config file (Campaign)
                }
            }

            static void Define(SmartWindowsPlugin p)
            {
                if (Fields.Count > 0) return;
                Add(TWindows, "Language", p._language, 'l', "Language", "");
                Add(TWindows, "Moving, sizing and snapping", p._dragOutside, 'b', "Drag partly off-screen", "");
                Add(TWindows, "Moving, sizing and snapping", p._minVisible, 'f', "Visible at the edge", "px", 5f, 1f, 10000f);
                Add(TWindows, "Moving, sizing and snapping", p._dblClickMax, 'b', "Maximize by double-click", "");
                Add(TWindows, "Moving, sizing and snapping", p._windowedMap, 'b', "Map and World View only in windows", "free move and size, no Tab");
                Add(TWindows, "Moving, sizing and snapping", p._removeLimitsAll, 'b', "No size limits", "");
                Add(TWindows, "Moving, sizing and snapping", p._snapOn, 'b', "Snap edges", "");
                Add(TWindows, "Moving, sizing and snapping", p._snapDist, 'f', "Snap distance", "px", 1f, 1f, 50f);
                Add(TWindows, "Moving, sizing and snapping", p._dockReach, 'b', "Resize docked windows", "zone reaches inside at screen edges");
                Add(TWindows, "Order and title bars", p._toFront, 'b', "Clicked window to front", "");
                Add(TWindows, "Order and title bars", p._cascade, 'f', "Cascade shift", "px · 0 = off", 5f, 0f, 500f);
                Add(TWindows, "Order and title bars", p._fixCursors, 'b', "Fix game resize cursors", "menus and campaign");

                Add(TMission, "In Mission", p._timeDrag, 'b', "Draggable time panel", "double-click: back to the corner");
                Add(TMission, "In Mission", p._unitMenu, 'b', "Menu on the unit panel's flag/name", "");
                if (_contactFixApplies) Add(TMission, "In Mission", p._fixContact, 'b', "Fix contact Information ages", "");   // hidden when this game version has nothing to fix

                Add(TFiles, "Scenarios", p._hideCustom, 'b', "Hide _Custom Mission", "");
                Add(TFiles, "Backgrounds", p._modBackgrounds, 'b', "Backgrounds from mods only", "");
                Add(TFiles, "Mission editor", p._rememberFolder, 'b', "Remember last folder", "");
                Add(TFiles, "Mission editor", p._blueIni, 'b', "Highlight ini files", "");
                Add(TCampaign, "This campaign", p._strictLoadouts, 'b', "Strict campaign loadouts", "");
                Add(TCampaign, "Task Force", p._dismissDamaged, 'b', "Dismiss damaged ships", "refund minus repair");
                Add(TCampaign, "Task Force", p._sortLoadouts, 'b', "Sort loadouts by date", "own versions first");
                Add(TCampaign, "Task Force", p._fixSwapPrice, 'b', "Fix version swap price", "roster price");
                Add(TCampaign, "Task Force", p._upgradeShip, 'b', "Upgrade ship button in the Builder", "crew -1 level");
                Add(TDebug, "Game logs", p._deleteLogs, 'b', "Delete old game logs at start", "");
                Add(TDebug, "Mod log", p._debugEntry, 'b', "Debug trace", "");
                if (!IsWorkshopInstall()) Add(TDebug, "Game UI", p._dumpXaml, 'b', "Dump XAML now", "switches itself off");   // a development tool: not for the Steam Workshop copy

                Add(TRemembered, "Position and size", p._windows, 'w', "Remembered windows", "'*' = any text");
                Add(TRemembered, "Position and size", p._addKey, 'k', "Add window under mouse", "key");
                Add(TRemembered, "Open or closed at mission start", p._openWindows, 'w', "Windows", "");

                Add(TFrame, "Frame around draggable windows", p._borderOn, 'b', "Frame", "");
                Add(TFrame, "Frame around draggable windows", p._borderThickness, 'f', "Width", "px", 0.5f, 0.5f, 10f);
                Add(TFrame, "Frame around draggable windows", p._borderColor, 'c', "Colour", "#AARRGGBB");
                Add(TFrame, "Frame around draggable windows", p._tipBorder, 'b', "Tooltips too", "all four sides");

                Add(TStatus, "Damage Control tab", p._secMin, 'f', "Sections min height", "px · 0 = game", 10f, 0f, 3000f);
                Add(TStatus, "Reference tab", p._statusRef, 'b', "Reference tab", "");
                Add(TStatus, "Track State", p._trackState, 'b', "Track State of a selected contact", "");
                Add(TStatus, "Unit name", p._fullName, 'b', "Full unit name in the header", "");

                Add(TRef, "Window and tree", p._improvedUI, 'b', "Layout Improvements", "");
                Add(TRef, "Window and tree", p._loadoutList, 'b', "Loadout list for all units", "");
                Add(TRef, "Window and tree", p._treeOpt, 'b', "Optimize the tree", "");
                Add(TRef, "Window and tree", p._treeScroll, 'b', "Scroll to the selected unit", "");
                Add(TRef, "Window and tree", p._earliest, 'b', "Class click: earliest variant", "");
                Add(TRef, "Window and tree", p._keepOpened, 'b', "Keep the opened unit in the tree", "");
                Add(TRef, "Window and tree", p._treeWheel, 'b', "Wheel only over the tree", "");
                Add(TRef, "Search and help", p._searchOn, 'b', "Remember search panel", "");
                Add(TRef, "Search and help", p._hideCountries, 'b', "Hide countries with no results", "");
                Add(TRef, "Search and help", p._searchDesc, 'b', "Search in description and id", "");
                Add(TRef, "Search and help", p._linkHelp, 'b', "Help tooltips on links", "");

                var sl = Fields.Find(f => ReferenceEquals(f.Entry, p._strictLoadouts));
                if (sl != null) sl.TfbOnly = true;
                // work only with Layout Improvements (ImprovedUnitReferenceUI): hidden while it is off
                var ui = Fields.Find(f => ReferenceEquals(f.Entry, p._improvedUI));
                foreach (var f in Fields)
                    if (f.Tab == TRef && f != ui && (f.Group == "Kept by the mod" || ReferenceEquals(f.Entry, p._searchOn))) f.Needs = ui;
            }

            // ------------------------------------------------------------ colours (as Alternative Damage Model)
            static readonly Color CBg = Color.FromRgb(30, 33, 38), CPanel = Color.FromRgb(38, 42, 48), CCard = Color.FromRgb(45, 50, 57),
                                  CLine = Color.FromRgb(70, 76, 86), CText = Color.FromRgb(230, 232, 235), CDim = Color.FromRgb(150, 156, 162),
                                  CWarn = Color.FromRgb(236, 212, 110), CTitle = Color.FromRgb(43, 48, 56), CField = Color.FromRgb(21, 24, 28),
                                  CHl = Color.FromRgb(52, 58, 66);

            static SolidColorBrush B(Color c) { return new SolidColorBrush(c); }
            static readonly SolidColorBrush Clear = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

            // ------------------------------------------------------------ state
            static SmartWindowsPlugin P;
            static UIElement _top;             // the screen the window is on (mouse positions for dragging)
            static Grid _root;
            static Panel _host;
            static TranslateTransform _move;
            static TextBlock _help;
            static StackPanel _body;
            static readonly List<Border> _tabButtons = new List<Border>();
            static readonly List<Ellipse> _tabDots = new List<Ellipse>();
            static TextBlock _status;
            static int _tab;
            static bool _unitRef;              // opened from a Unit Reference window: its tab is shown
            static bool _unitStatus;           // opened from a Unit Status window: its tab is shown
            static bool _tfb;                  // opened from the campaign Task Force Builder: its own options are shown
            static bool _saidOpen;

            static bool _showAll;              // "Show all settings": the tabs of the other windows too (until the window closes)
            static Button _showAllButton;

            static bool TabShown(int i) { return _showAll || (Tabs[i] == TRef ? _unitRef : Tabs[i] == TStatus ? _unitStatus : Tabs[i] == TCampaign ? _tfb : true); }

            /// <summary>The button toggles the hidden tabs; not shown when no tab is hidden.</summary>
            static void UpdateShowAll()
            {
                if (_showAllButton == null) return;
                bool hidden = !_unitRef || !_unitStatus || !_tfb;
                _showAllButton.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
                _showAllButton.Content = Loc.T(_showAll ? "btn.showFewer" : "btn.showAll");
            }

            public static bool IsOpen
            {
                get
                {
                    try { return _root != null && _host != null && VisualTreeHelper.GetParent(_root) != null && _root.IsVisible; }
                    catch { return false; }
                }
            }

            // ------------------------------------------------------------ open / close

            /// <summary>
            /// Opens the window in the middle of the screen of 'anchor' (the gear clicked): in the topmost Grid / Canvas on the way up
            /// from it (not an items host), above everything. Open already: only the Unit Reference / Unit Status tabs follow where it was
            /// opened from.
            /// </summary>
            public static void Open(SmartWindowsPlugin p, UIElement anchor, bool unitReference, bool unitStatus, bool taskForce = false)
            {
                if (p == null || anchor == null) return;
                P = p;
                try
                {
                    Define(p);
                    bool tabChange = _unitRef != unitReference || _unitStatus != unitStatus || _tfb != taskForce;
                    _tfb = taskForce;
                    if (taskForce) p.LoadStrictToProxy();
                    _unitRef = unitReference;
                    _unitStatus = unitStatus;
                    // opened from Unit Reference / Unit Status: that window's own tab first
                    if (tabChange && unitReference) _tab = Array.IndexOf(Tabs, TRef);
                    else if (tabChange && unitStatus) _tab = Array.IndexOf(Tabs, TStatus);
                    else if (taskForce) _tab = Array.IndexOf(Tabs, TCampaign);
                    if (IsOpen)
                    {
                        UpdateShowAll();
                        if (tabChange || taskForce) ShowTab(_tab);
                        return;
                    }
                    Close();
                    _pending.Clear();   // a new copy of the settings
                    _resets.Clear();
                    _osStates.Clear();
                    DependencyObject top = anchor; Panel panel = null;
                    for (DependencyObject d = anchor; d != null; d = SafeParent(d))
                    {
                        top = d;
                        var pn = d as Panel;
                        bool itemsHost = false;
                        try { itemsHost = pn != null && pn.IsItemsHost; } catch { }
                        if (pn != null && !itemsHost && (pn is Grid || pn is Canvas)) panel = pn;
                    }
                    if (panel == null) { p.Logger.LogWarning("Smart Windows: no place found for the settings window."); return; }
                    _top = top as UIElement ?? anchor;
                    BuildWindow();
                    _move = new TranslateTransform(0f, 0f);
                    _place = 0;
                    _root.LayoutUpdated += OnLayout;   // size in screen pixels and centring on the screen: once laid out
                    _root.RenderTransform = _move;
                    var grid = panel as Grid;
                    if (grid != null)
                    {
                        _root.HorizontalAlignment = HorizontalAlignment.Center;
                        _root.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetRow(_root, 0); Grid.SetColumn(_root, 0);
                        Grid.SetRowSpan(_root, Math.Max(1, grid.RowDefinitions.Count));
                        Grid.SetColumnSpan(_root, Math.Max(1, grid.ColumnDefinitions.Count));
                    }
                    else
                    {
                        float w = panel.ActualWidth > 0 ? panel.ActualWidth : 1280f, h = panel.ActualHeight > 0 ? panel.ActualHeight : 720f;
                        Canvas.SetLeft(_root, Math.Max(0f, (w - _root.Width) / 2f));
                        Canvas.SetTop(_root, Math.Max(0f, (h - _root.Height) / 2f));
                    }
                    Panel.SetZIndex(_root, 30000);
                    panel.Children.Add(_root);
                    _host = panel;
                    if (!TabShown(_tab)) _tab = 0;
                    ShowTab(_tab);
                    Trace("settings window: in " + panel.GetType().Name + " (" + panel.ActualWidth + " x " + panel.ActualHeight + ")");
                    if (!_saidOpen) { _saidOpen = true; p.Logger.LogInfo("Settings window opened (in " + panel.GetType().Name + ")."); }
                }
                catch (Exception e) { p.Logger.LogWarning("Smart Windows: settings window could not be opened: " + e.Message); Trace("SettingsWindow.Open: " + e); }
            }

            static DependencyObject SafeParent(DependencyObject e) { try { return VisualTreeHelper.GetParent(e); } catch { return null; } }

            public static void Close()
            {
                if (P != null) P.SetTyping(false, null);
                try { var pp = _root == null ? null : SafeParent(_root) as Panel; if (pp != null) pp.Children.Remove(_root); } catch { }
                try { if (_host != null && _root != null) _host.Children.Remove(_root); } catch { }
                _host = null; _root = null; _body = null; _help = null; _status = null; _commitFocused = null; _showAllButton = null; _showAll = false;
            }

            // ------------------------------------------------------------ building

            static TextBlock Text(string s, Color c, float size = 13f)
            {
                return new TextBlock { Text = s, Foreground = B(c), FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            }

            static Button Btn(string s, Action click, float minW = 0f)
            {
                var b = new Button { Content = s, Padding = new Thickness(10f, 2f, 10f, 2f), Margin = new Thickness(0f, 0f, 6f, 0f), MinHeight = 26f, MinWidth = minW };
                b.Click += (o, e) => { try { click(); } catch (Exception ex) { Trace("button " + s + ": " + ex); } };
                return b;
            }

            static TextBox Edit(string text, float width)
            {
                var t = new TextBox { Text = text ?? "", Height = 24f, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                                      Background = B(CField), Foreground = B(CText), BorderBrush = B(CLine), Padding = new Thickness(2f, 0f, 2f, 0f) };
                if (width > 0f) t.Width = width;
                t.GotFocus += (s, e) => { P.SetTyping(true, t); try { t.SelectAll(); } catch { } };
                t.LostFocus += (s, e) => P.SetTyping(false, t);
                return t;
            }

            static void BuildWindow()
            {
                _root = new Grid { Width = _winW, Height = _winH };
                FillWindow();
            }

            /// <summary>The content of the window (its texts are read now): also used to rebuild it after the language changed.</summary>
            static void FillWindow()
            {
                var win = new Border { Background = B(CBg), BorderBrush = B(CLine), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(4f) };
                _root.Children.Add(win);
                var parts = new List<UIElement>();

                // title bar: drag to move, ✕ to close
                var title = new Grid { Background = B(CTitle), Height = 32f };
                title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
                title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tt = Text(Loc.T("ui.title"), CText, 14f); tt.Margin = new Thickness(10f, 0f, 0f, 0f);
                title.Children.Add(tt);
                var x = new Button { Content = Cross(10f), Width = 28f, Height = 24f, Padding = new Thickness(0f), Margin = new Thickness(0f, 0f, 4f, 0f),
                                     HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                x.Click += (o, e) => Cancel();
                Grid.SetColumn(x, 1);
                title.Children.Add(x);
                Draggable(title);
                parts.Add(title);

                // tabs
                var tabs = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 10f, 12f, 0f) };
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
                parts.Add(tabs);

                _body = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0f, 6f, 0f, 6f) };
                var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                var settingsArea = new Border { Child = scroll, Background = B(CCard), BorderBrush = B(CLine), BorderThickness = new Thickness(1f), Margin = new Thickness(12f, 0f, 12f, 0f) };
                parts.Add(settingsArea);

                _help = new TextBlock { Foreground = B(Color.FromRgb(194, 199, 204)), FontSize = 12f, TextWrapping = TextWrapping.Wrap, Text = Loc.T("ui.help") };
                parts.Add(new Border { Child = _help, Background = B(Color.FromRgb(34, 38, 44)), BorderBrush = B(CLine), BorderThickness = new Thickness(1f),
                                       Padding = new Thickness(10f, 8f, 10f, 8f), Margin = new Thickness(12f, 8f, 12f, 0f), Height = 108f, CornerRadius = new CornerRadius(3f) });   // fixed: the window does not jump

                // footer
                var foot = new Grid { Margin = new Thickness(12f, 10f, 12f, 12f) };
                foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
                foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _showAllButton = Btn(Loc.T("btn.showAll"), () => { _showAll = !_showAll; UpdateShowAll(); ShowTab(_tab); }, 70f);
                foot.Children.Add(_showAllButton);
                UpdateShowAll();
                _status = Text("", CDim, 11f);
                _status.HorizontalAlignment = HorizontalAlignment.Right; _status.Margin = new Thickness(0f, 0f, 10f, 0f);
                Grid.SetColumn(_status, 1);
                foot.Children.Add(_status);
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                right.Children.Add(Btn(Loc.T("btn.cancel"), Cancel, 70f));
                right.Children.Add(Btn(Loc.T("btn.apply"), () => Commit(false), 70f));
                var ok = Btn(Loc.T("btn.ok"), () => Commit(true), 70f); ok.Margin = new Thickness(0f);
                right.Children.Add(ok);
                Grid.SetColumn(right, 2); foot.Children.Add(right);
                parts.Add(foot);

                // the rows: all as tall as they need, the settings area takes the rest of the window's height
                var layout = new Grid();
                for (int r = 0; r < parts.Count; r++)
                {
                    layout.RowDefinitions.Add(new RowDefinition { Height = ReferenceEquals(parts[r], settingsArea) ? new GridLength(1f, GridUnitType.Star) : GridLength.Auto });
                    Grid.SetRow(parts[r], r);
                    layout.Children.Add(parts[r]);
                }
                win.Child = layout;
                AddResizeHandles();
            }

            /// <summary>A drawn ✕ for a button: the font's glyph sat low in the button, cut at the bottom and off centre.</summary>
            static Path Cross(float size)
            {
                return new Path { Data = new StreamGeometry("M0,0 L10,10 M10,0 L0,10"), Stroke = B(CText), StrokeThickness = 1.5f, Stretch = Stretch.Uniform,
                                  Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }

            // drag the window by its title bar
            static void Draggable(FrameworkElement handle)
            {
                bool down = false; Point start = new Point(); float h0 = 0f, v0 = 0f;
                handle.MouseLeftButtonDown += (o, e) =>
                {
                    if (_move == null) return;
                    down = true; start = e.GetPosition(_top); h0 = _move.X; v0 = _move.Y;
                    handle.CaptureMouse(); e.Handled = true;
                };
                handle.MouseMove += (o, e) =>
                {
                    if (!down || _move == null) return;
                    var p = e.GetPosition(_top);
                    _move.X = h0 + (p.X - start.X);
                    _move.Y = v0 + (p.Y - start.Y);
                };
                handle.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; handle.ReleaseMouseCapture(); e.Handled = true; };
            }

            // ------------------------------------------------------------ placing on the screen
            // The game scales its UI: the size is set in screen pixels (at most the screen less 40) and the window is centred on
            // the screen, not in the panel it is put into.
            static int _place;
            static bool _sizedOnce;
            const float PixW = 980f, PixH = 830f;
            static float _winW = 980f, _winH = 830f;   // UI units; set from PixW x PixH screen pixels at the first opening

            static void OnLayout(object sender, Noesis.EventArgs e)
            {
                if (_root == null || _place >= 2) return;
                try
                {
                    if (_root.ActualWidth <= 0f) return;
                    var a = _root.PointToScreen(new Point(0f, 0f)); var b = _root.PointToScreen(new Point(100f, 0f));
                    float k = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / 100f;   // screen pixels per UI unit
                    if (k <= 0.01f || float.IsNaN(k)) { _place = 2; return; }
                    float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
                    if (_place == 0)
                    {
                        if (!_sizedOnce) { _winW = PixW / k; _winH = PixH / k; _sizedOnce = true; }
                        _root.Width = Math.Min(_winW, (sw - 40f) / k);
                        _root.Height = Math.Min(_winH, (sh - 40f) / k);
                        _place = 1;
                        Trace("settings window: " + k.ToString("0.##", CultureInfo.InvariantCulture) + " px per unit, " + _root.Width.ToString("0") + " x " + _root.Height.ToString("0") + " units");
                        return;   // laid out again with the new size
                    }
                    float w = _root.Width * k, h = _root.Height * k;
                    _move.X += ((sw - w) / 2f - a.X) / k;
                    _move.Y += ((sh - h) / 2f - a.Y) / k;
                    _place = 2;
                    _root.LayoutUpdated -= OnLayout;
                }
                catch (Exception ex) { _place = 2; Trace("settings window placing: " + ex.Message); }
            }

            /// <summary>
            /// Resizing by the window's edges (5 px strips) and corners (16 px; the bottom right one shows the grip lines):
            /// 520..1600 x 420..1400, the settings area takes the change. Dragging the left or top side keeps the opposite side in place.
            /// The size is kept while the game runs.
            /// </summary>
            static void AddResizeHandles()
            {
                for (int sy = -1; sy <= 1; sy++)
                    for (int sx = -1; sx <= 1; sx++)
                        if (sx != 0 || sy != 0) _root.Children.Add(ResizeHandle(sx, sy));
            }

            /// <param name="sx">-1 left, 1 right, 0 neither</param>
            /// <param name="sy">-1 top, 1 bottom, 0 neither</param>
            static FrameworkElement ResizeHandle(int sx, int sy)
            {
                const float Edge = 5f, Corner = 16f;
                bool corner = sx != 0 && sy != 0;
                var h = new Border { Background = Clear,
                                     HorizontalAlignment = sx < 0 ? HorizontalAlignment.Left : sx > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                                     VerticalAlignment = sy < 0 ? VerticalAlignment.Top : sy > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch };
                if (sx != 0) h.Width = corner ? Corner : Edge;
                if (sy != 0) h.Height = corner ? Corner : Edge;
                if (sx == 0) h.Margin = new Thickness(Corner, 0f, Corner, 0f);        // the corners belong to the corner handles
                else if (sy == 0) h.Margin = new Thickness(0f, Corner, 0f, Corner);
                if (sx > 0 && sy > 0)
                {
                    h.Margin = new Thickness(0f, 0f, 1f, 1f);
                    h.Child = new Path { Data = new StreamGeometry("M15,4 L4,15 M15,8 L8,15 M15,12 L12,15"), Stroke = B(CDim), StrokeThickness = 1f };
                    try { ToolTipService.SetToolTip(h, Loc.T("tip.resize")); } catch { }
                }
                try { h.Cursor = corner ? (sx == sy ? Cursors.SizeNWSE : Cursors.SizeNESW) : sx != 0 ? Cursors.SizeWE : Cursors.SizeNS; } catch { }
                bool down = false; Point start = new Point(); float w0 = 0f, h0 = 0f;
                h.MouseLeftButtonDown += (o, e) =>
                {
                    down = true; start = e.GetPosition(_top); w0 = _winW; h0 = _winH;
                    h.CaptureMouse(); e.Handled = true;
                };
                h.MouseMove += (o, e) =>
                {
                    if (!down || _root == null) return;
                    var p = e.GetPosition(_top);
                    float w = sx == 0 ? _winW : Math.Max(520f, Math.Min(1600f, w0 + sx * (p.X - start.X)));
                    float hh = sy == 0 ? _winH : Math.Max(420f, Math.Min(1400f, h0 + sy * (p.Y - start.Y)));
                    float dw = w - _winW, dh = hh - _winH;
                    if (Math.Abs(dw) < 0.5f && Math.Abs(dh) < 0.5f) return;
                    _winW = w; _winH = hh;
                    _root.Width = w; _root.Height = hh;
                    if (_move == null) return;
                    // a window centred in a Grid grows both ways; one placed by its top left corner grows right / down:
                    // move it so that only the dragged side moves
                    bool centred = _host is Grid;
                    if (sx != 0) _move.X += centred ? sx * dw / 2f : (sx < 0 ? -dw : 0f);
                    if (sy != 0) _move.Y += centred ? sy * dh / 2f : (sy < 0 ? -dh : 0f);
                };
                h.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; h.ReleaseMouseCapture(); e.Handled = true; };
                return h;
            }

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
                    try { ShowTab(_tab); } catch (Exception e) { Trace("settings: " + e.Message); }
            }

            /// <summary>The row is shown: the switch it depends on (if any) is on in the window.</summary>
            static bool Shown(Field f) { return (!f.TfbOnly || _tfb || _showAll) && (f.Needs == null || (Get(f.Needs) is bool && (bool)Get(f.Needs))); }

            static bool Dirty() { return _pending.Count > 0 || _resets.Count > 0 || _osStates.Count > 0; }

            /// <summary>Apply / OK: the changed values are stored (and take effect), then the saved layouts marked are forgotten.</summary>
            static void Commit(bool close)
            {
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
                Trace("settings applied: " + n + " value(s), " + r + " layout(s) forgotten, " + o + " open / closed state(s)");
                if (close) Close();
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
                var root = _root;
                if (root == null) return;
                Noesis.EventHandler h = null;
                h = (o, e) =>
                {
                    root.LayoutUpdated -= h;
                    if (!ReferenceEquals(root, _root)) return;
                    try
                    {
                        _root.Children.Clear();
                        FillWindow();
                        ShowTab(_tab);
                    }
                    catch (Exception ex) { Trace("settings rebuild: " + ex.Message); }
                };
                root.LayoutUpdated += h;
                try { root.InvalidateMeasure(); } catch { }
            }

            /// <summary>Cancel / ✕: what was changed in the window is dropped.</summary>
            static void Cancel()
            {
                _pending.Clear();
                _resets.Clear();
                _osStates.Clear();
                Close();
            }

            static string Show(Field f, object v)
            {
                if (v is bool) return Loc.T((bool)v ? "val.on" : "val.off");
                if (v is float) return ((float)v).ToString("0.##", CultureInfo.InvariantCulture);
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
                for (int t = 0; t < Tabs.Length && t < _tabDots.Count; t++)
                {
                    bool any = Tabs[t] == TRemembered && (_resets.Count > 0 || _osStates.Count > 0);
                    foreach (var f in Fields) if (f.Tab == Tabs[t] && Changed(f)) { any = true; break; }
                    _tabDots[t].Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                }
                if (_status != null) _status.Text = Loc.T(Dirty() ? "st.dirty" : "st.clean");
            }

            // ------------------------------------------------------------ tabs and rows

            static void ShowTab(int t)
            {
                if (_body == null) return;
                _tab = Math.Max(0, Math.Min(Tabs.Length - 1, t));
                if (!TabShown(_tab)) _tab = 0;
                for (int i = 0; i < _tabButtons.Count; i++)
                {
                    _tabButtons[i].Visibility = TabShown(i) ? Visibility.Visible : Visibility.Collapsed;
                    bool on = i == _tab;
                    _tabButtons[i].Background = B(on ? CCard : CPanel);
                    var row = _tabButtons[i].Child as StackPanel;
                    var label = row != null && row.Children.Count > 0 ? row.Children[0] as TextBlock : null;
                    if (label != null) label.Foreground = B(on ? CText : CDim);
                }
                _commitFocused = null;
                _body.Children.Clear();
                string group = null;
                foreach (var f in Fields)
                {
                    if (f.Tab != Tabs[_tab] || !Shown(f)) continue;
                    if (f.Group != group) { group = f.Group; _body.Children.Add(GroupTitle(GroupOf(group))); }
                    _body.Children.Add(f.Kind == 'w' ? ListRow(f) : Row(f));
                }
                UpdateHeader();
            }

            static TextBlock GroupTitle(string s)
            {
                return new TextBlock { Text = s.ToUpperInvariant(), Foreground = B(CDim), FontSize = 11f, Margin = new Thickness(12f, 6f, 12f, 0f) };
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
                FrameworkElement editor = f.Kind == 'b' ? BoolEditor(f, mark, out refresh) : f.Kind == 'f' ? NumberEditor(f, mark, out refresh) : f.Kind == 'l' ? LanguageEditor(f, mark, out refresh) : TextEditor(f, mark, out refresh);
                Grid.SetColumn(editor, 1);
                g.Children.Add(editor);

                var def = new TextBlock { Text = DefaultText(f), Foreground = B(CDim), FontSize = 11f, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(def, 2);
                g.Children.Add(def);
                var rst = BackButton(f, refresh, mark);
                Grid.SetColumn(rst, 3);
                g.Children.Add(rst);
                mark();
                Hover(outer, LabelOf(f), HelpOf(f), null);
                return outer;
            }

            static void Hover(Border row, string label, string text, string extra)
            {
                row.MouseEnter += (o, e) =>
                {
                    row.Background = B(CHl);
                    if (_help == null) return;
                    _help.Inlines.Clear();
                    var r = new Run(label); r.FontWeight = FontWeight.Bold; r.Foreground = B(CText);
                    _help.Inlines.Add(r);
                    _help.Inlines.Add(new Run(" - " + (text ?? "")));
                    if (!string.IsNullOrEmpty(extra)) { var x = new Run("  " + extra); x.Foreground = B(CWarn); _help.Inlines.Add(x); }
                };
                row.MouseLeave += (o, e) => row.Background = Clear;
            }

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
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var box = Edit(Show(f, Get(f)), 66f);
                Action<float> put = v =>
                {
                    v = Math.Max(f.Min, Math.Min(f.Max, v));
                    Set(f, v);
                    box.Text = Show(f, Get(f));
                    mark();
                };
                Func<float> cur = () => { var o = Get(f); return o is float ? (float)o : 0f; };
                var minus = new Button { Content = "−", Width = 24f, Height = 24f, Padding = new Thickness(0f) };
                var plus = new Button { Content = "+", Width = 24f, Height = 24f, Padding = new Thickness(0f) };
                minus.Click += (o, e) => put((float)Math.Round((cur() - f.Step) / f.Step) * f.Step);
                plus.Click += (o, e) => put((float)Math.Round((cur() + f.Step) / f.Step) * f.Step);
                // typed in: a number is taken (within the limits); anything else puts the value back as it was
                Action typed = () => { float v; if (TryNum(box.Text, out v)) { if (Math.Abs(v - cur()) > 1e-6f) put(v); else box.Text = Show(f, Get(f)); } else box.Text = Show(f, Get(f)); };
                Commits(box, typed, () => box.Text = Show(f, Get(f)));
                sp.Children.Add(minus); sp.Children.Add(box); sp.Children.Add(plus);
                if (!string.IsNullOrEmpty(f.Unit)) { var u = Text(UnitOf(f), CDim, 12f); u.Margin = new Thickness(8f, 0f, 0f, 0f); sp.Children.Add(u); }
                refresh = () => box.Text = Show(f, Get(f));
                return sp;
            }

            static FrameworkElement BoolEditor(Field f, Action mark, out Action refresh)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Func<bool> on = () => { var o = Get(f); return o is bool && (bool)o; };
                var cb = new CheckBox { IsChecked = on(), VerticalAlignment = VerticalAlignment.Center };
                cb.Click += (o, e) => { Set(f, cb.IsChecked == true); mark(); };
                sp.Children.Add(cb);
                if (!string.IsNullOrEmpty(f.Unit)) { var u = Text(UnitOf(f), CDim, 12f); u.Margin = new Thickness(8f, 0f, 0f, 0f); sp.Children.Add(u); }
                refresh = () => cb.IsChecked = on();
                return sp;
            }

            /// <summary>The language of the mod's texts: the game's own or one of the game's languages.</summary>
            static FrameworkElement LanguageEditor(Field f, Action mark, out Action refresh)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var codes = new List<string> { "" };
                var cb = new ComboBox { Width = 160f, MinHeight = 22f, VerticalContentAlignment = VerticalAlignment.Center };
                cb.Items.Add(Loc.T("val.gameLang"));
                foreach (var l in Loc.GameLanguages()) { codes.Add(l.Key); cb.Items.Add(l.Value); }
                Func<int> index = () => { int k = codes.IndexOf((Convert.ToString(Get(f), CultureInfo.InvariantCulture) ?? "").Trim()); return k < 0 ? 0 : k; };
                bool busy = false;
                cb.SelectedIndex = index();
                cb.SelectionChanged += (o, e) =>
                {
                    if (busy || cb.SelectedIndex < 0) return;
                    Set(f, codes[cb.SelectedIndex]); mark();
                };
                sp.Children.Add(cb);
                refresh = () => { busy = true; cb.SelectedIndex = index(); busy = false; };
                return sp;
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
