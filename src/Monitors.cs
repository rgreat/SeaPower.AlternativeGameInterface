using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [Monitors] Enabled (desktop mode only): every other monitor is one more desktop surface that works like the main one - windows can be put on it.
    /// The game's own use of other monitors (a second Tactical Display map, the World View moved to a monitor) is switched off then.
    ///
    /// What the game gives: windows are items of WindowHost.Windows (an ObservableCollection of view models; the data templates of the view models, in
    /// MainGameView's resources, make the Window elements), shown by an ItemsControl in MainGameView. The host is a Behavior on the view's root; it handles
    /// the open / close commands for the elements below that root. NoesisView (one per view) reads the mouse through Display.RelativeMouseAt and takes
    /// only the events of the display its camera shows (NoesisView.MousePosition / OnGUI), so a view with its own camera on a display works there.
    ///
    /// Design: for every display i >= 1 a MonitorView = a camera (targetDisplay = i, grey clear) + a NoesisView whose content (made in code, CreateView) is
    /// a Grid {ScaleTransform = UIScale x display height / 1080 like MainGameViewModel.ScaleValueX; ItemsControl bound to the Windows of a second WindowHost
    /// instance (a Behavior of that Grid)}. The main view's Resources are merged into the Grid so the data templates are found; its DataContext is the main
    /// view model (as the game's second display does). A window moves between monitors by moving its view model between the Windows collections of the two
    /// hosts (the element is made again by the template; our Loaded handling restores its layout on the new monitor). A view model is on one host only:
    /// WindowHost.OpenNewWindow / CanCloseWindow / CloseWindow are routed to the host that has it (the game's toggles look for it through the main host).
    /// The monitor of a window pattern is remembered ([Monitors] Windows = pattern|n;...), per desktop (record N| in the desktop text).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _useMonitors;
        ConfigEntry<string> _winMonitors;
        Dictionary<string, int> _monMap;

        sealed class MonitorView
        {
            public int Index;
            public UnityEngine.GameObject Go;
            public UnityEngine.Camera Cam;
            public UnityEngine.Transform Bd; public int BdW, BdH;
            public ContextMenu Menu;
            public UnityEngine.Component View;
            public FrameworkElement Root;
            public object Host;
            public ScaleTransform Scale;
            public int W, H; public float Ui;
        }
        static readonly List<MonitorView> Monitors = new List<MonitorView>();      // index in the list is not the display number: see MonitorView.Index
        static Type _monViewType, _hostType;
        static FieldInfo _hostWindows;
        static bool _monBroken;

        void BindMonitorConfig()
        {
            _useMonitors = Config.Bind("Monitors", "Enabled", true,
                "Desktop mode: the other monitors work like the main one for windows (Monitor in the window's menu). The game's own second monitor map and World View monitor are not used then.");
            _winMonitors = Config.Bind("Monitors", "Windows", "", "Monitor of the remembered windows, written by the plugin: pattern|monitor;... (1 = the main monitor).");
        }

        /// <summary>The feature is on: desktop mode, the option, and more than one display.</summary>
        static bool MonitorsOn
        {
            get
            {
                var a = Active;
                if ((object)a == null || a._useMonitors == null || !a._useMonitors.Value || !MapOn() || _monBroken) return false;
                try { return UnityEngine.Display.displays.Length > 1; } catch { return false; }
            }
        }

        static object SGet(object o, string name)
        {
            if (o == null) return null;
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = o.GetType().GetProperty(name, f);
            if (p != null) return p.GetValue(o, null);
            var fi = o.GetType().GetField(name, f);
            return fi == null ? null : fi.GetValue(o);
        }
        static bool SGetB(object o, string name) { try { var v = SGet(o, name); return v is bool && (bool)v; } catch { return false; } }

        static int DisplayCount() { try { return UnityEngine.Display.displays.Length; } catch { return 1; }}

        // ------------------------------------------------------------ the game's own multi-monitor logic is off

        void HookMonitors(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sd = asm.GetType("SeaPower.SecondDisplayMapHandler");
            var en = sd == null ? null : sd.GetMethod("EnableSecondMonitor", all);
            if (en != null) TryPatch(harmony, en, "MonSecondMapPrefix", null, null);
            var cm = asm.GetType("SeaPower.CameraManager");
            var swd = cm == null ? null : cm.GetMethod("SetWorldDisplay", all);
            if (swd != null) TryPatch(harmony, swd, "MonWorldDisplayPrefix", null, null);
            var main = asm.GetType("SeapowerUI.ViewModels.MainGameViewModel");
            var sync = main == null ? null : main.GetMethod("SyncWorldViewWindow", all);
            if (sync != null) TryPatch(harmony, sync, "MonSyncWorldPrefix", null, null);
            var det = main == null ? null : main.GetProperty("WorldViewDetached", all);
            var detGet = det == null ? null : det.GetGetMethod(true);
            if (detGet != null) TryPatch(harmony, detGet, null, "MonDetachedPostfix", null);
            // the views of the other monitors get their mouse from here (see MonitorInputStep): NoesisView.ProjectPointer flips y by Screen.height
            // (the main window's) and its OnGUI mouse events carry the main window's coordinates
            var nv = typeof(FrameworkElement).Assembly.GetType("NoesisView");
            if (nv != null)
            {
                var pp = nv.GetMethod("ProjectPointer", all);
                var pe = nv.GetMethod("ProcessEvent", all);
                if (pp != null) TryPatch(harmony, pp, "MonProjectPrefix", null, null);
                if (pe != null) TryPatch(harmony, pe, "MonProcessEventPrefix", null, null);
                Trace("monitors: NoesisView.ProjectPointer " + (pp != null) + ", ProcessEvent " + (pe != null));
            }
            _hostType = asm.GetType("SeapowerUI.WindowHost");
            if (_hostType != null)
            {
                _hostWindows = null;
                foreach (var n in new[] { "OpenNewWindow", "CanCloseWindow", "CloseWindow" })
                {
                    var m = _hostType.GetMethod(n, all);
                    if (m != null) TryPatch(harmony, m, "MonHost" + n + "Prefix", null, null);
                }
            }
            Trace("monitors: SecondDisplayMapHandler.EnableSecondMonitor " + (en != null) + ", CameraManager.SetWorldDisplay " + (swd != null) +
                  ", WindowedDetached " + (detGet != null) + ", WindowHost " + (_hostType != null));
        }

        /// <summary>
        /// The game's second-monitor view (SecondDisplayMapHandler: its camera "UI Camera 2" and NoesisView with the second-monitor map) is
        /// switched on by the game only when its options change (Refresh). The prefix below keeps it off while the monitors are in use, but a
        /// view already on (the game's World View monitor or second-monitor map set, then desktop mode switched on) stayed on over the
        /// monitor's own view: a second Tactical Display there that took no proper input. Off: switched off when the monitors' views are
        /// made; back: the game's own Refresh (its options decide) when they are removed.
        /// </summary>
        static void SecondMapHandler(bool restore)
        {
            try
            {
                var asm = GameAssembly();
                var t = asm == null ? null : asm.GetType("SeaPower.SecondDisplayMapHandler");
                var sg = asm == null ? null : asm.GetType("SeaPower.Singleton`1");
                if (t == null || sg == null) return;
                var g = sg.MakeGenericType(t);
                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var ex = g.GetMethod("InstanceExists", sf, null, new[] { typeof(bool) }, null);
                if (ex == null || !(bool)ex.Invoke(null, new object[] { false })) return;
                var h = g.GetProperty("Instance", sf).GetValue(null, null);
                if (h == null) return;
                if (restore)
                {
                    var r = t.GetMethod("Refresh", f);
                    if (r != null) { r.Invoke(h, null); Trace("monitors: the game's second-monitor view back to the game's options"); }
                }
                else
                {
                    var cam = t.GetField("_mapScreenCamera", f);
                    var c = cam == null ? null : cam.GetValue(h) as UnityEngine.Camera;
                    if (c == null || !c.enabled) return;
                    var en = t.GetMethod("EnableSecondMonitor", f);
                    if (en != null) { en.Invoke(h, new object[] { false, c.targetDisplay }); Trace("monitors: the game's second-monitor view (on display " + (c.targetDisplay + 1) + ") switched off"); }
                }
            }
            catch (Exception e) { Trace("monitors: the game's second-monitor view: " + e.Message); }
        }

        // the game's second-monitor map is never enabled
        static void MonSecondMapPrefix(ref bool enabled) { if (MonitorsOn) enabled = false; }
        // the World View stays on the main display
        static void MonWorldDisplayPrefix(ref int index) { if (MonitorsOn) index = 0; }
        static void MonDetachedPostfix(ref bool __result) { if (__result && MonitorsOn) __result = false; }

        /// <summary>The game inserts the World View's window into the main host when it is not there: when it is on another monitor's host it is left alone
        /// (and removed from there by the game's own rule).</summary>
        static bool MonSyncWorldPrefix(object __instance)
        {
            if (Monitors.Count == 0) return true;
            try
            {
                var fi = __instance.GetType().GetField("_worldViewWindow", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var wv = fi == null ? null : fi.GetValue(__instance);
                if (wv == null) return true;
                var owner = OwnerOf(wv);
                if (owner == null && AddToWantedHost(wv)) return false;
                if (owner == null || ReferenceEquals(owner, MainHost())) return true;
                if (!SGetB(__instance, "ShowPip3D"))
                {
                    var list = HostWindows(owner); var map = SGet(__instance, "Map");
                    int iw = list.IndexOf(wv), im = map == null ? -1 : list.IndexOf(map);
                    if (iw >= 0 && im > iw) { list.RemoveAt(iw); CloseUpdate(); }
                }
                return false;
            }
            catch { return true; }
        }

        static void CloseUpdate()
        {
            try
            {
                var cmdT = _hostType.Assembly.GetType("SeapowerUI.WindowHostCommands");
                var c = cmdT.GetProperty("CloseWindowCommand", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                c.GetType().GetMethod("UpdateCanExecute").Invoke(c, null);
            }
            catch { }
        }

        /// <summary>The mouse in pixels of display d (top left origin); false when it is on another display.</summary>
        static bool PointerOn(int d, out float x, out float y)
        {
            x = y = 0f;
            var mp = UnityEngine.Input.mousePosition;
            if (d <= 0 || Monitors.Count == 0)
            {
                if (Monitors.Count > 0 && MouseDisplay() > 0) return false;
                x = mp.x; y = UnityEngine.Screen.height - mp.y; return true;
            }
            try
            {
                var rel = UnityEngine.Display.RelativeMouseAt(mp);
                if (rel == UnityEngine.Vector3.zero || (int)rel.z != d) return false;
                x = rel.x; y = UnityEngine.Display.displays[d].renderingHeight - rel.y; return true;
            }
            catch { return false; }
        }

        static FieldInfo _uiViewField;
        static object UiViewOf(object noesisView)
        {
            if (noesisView == null) return null;
            try
            {
                if (_uiViewField == null) _uiViewField = noesisView.GetType().GetField("_uiView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (_uiViewField == null) { if (_traceOnce.Add("uiview")) Trace("monitors: NoesisView._uiView not found, cursor changes are not filtered by display"); return null; }
                return _uiViewField.GetValue(noesisView);
            }
            catch { return null; }
        }

        /// <summary>A cursor change of a view: only the view of the display the mouse is on counts (a view whose hover is stale - the mouse is elsewhere - must not set the cursor).</summary>
        static bool CursorViewAllowed(object uiView)
        {
            if (Monitors.Count == 0 || uiView == null) return true;
            try
            {
                int d = MouseDisplay();
                foreach (var m in Monitors)
                {
                    var v = UiViewOf(m.View);
                    if (v != null && (ReferenceEquals(v, uiView) || v.Equals(uiView))) return m.Index == d;
                }
                var a = Active; var mainV = (object)a == null || a._mainNoesis == null ? null : UiViewOf(a._mainNoesis);
                if (mainV != null && (ReferenceEquals(mainV, uiView) || mainV.Equals(uiView))) return d == 0;
            }
            catch { }
            return true;
        }

        static MonitorView ViewOf(object noesisView)
        {
            foreach (var m in Monitors) if (ReferenceEquals(m.View, noesisView)) return m;
            return null;
        }

        // pointer of a monitor view: y from the bottom of ITS display
        static bool MonProjectPrefix(object __instance, float x, float y, ref UnityEngine.Vector2 __result)
        {
            if (Monitors.Count == 0) return true;
            var m = ViewOf(__instance);
            if (m == null) return true;
            __result = new UnityEngine.Vector2(x, m.H - y);
            return false;
        }

        // the mouse of a monitor view comes from MonitorInputStep; its OnGUI handles the keyboard only
        static void MonProcessEventPrefix(object __instance, ref bool enableMouse)
        {
            if (Monitors.Count == 0) return;
            if (ViewOf(__instance) != null) { enableMouse = false; return; }
            // the main view: no mouse events while the press began on a monitor, or while the mouse is on a monitor and no main-view press is held
            // (else a press / release over there is taken as one at the same point of the main window and a window is "grabbed")
            var a = Active; if ((object)a == null) return;
            if (a._pressMask != 0 || UnityEngine.Time.frameCount <= _monMouseOffFrame || (a._mainPress == 0 && MouseDisplay() > 0)) enableMouse = false;
        }
        static int _monMouseOffFrame;
        int _mainPress;

        // ------------------------------------------------------------ hosts

        static IList HostWindows(object host)
        {
            if (host == null) return null;
            if (_hostWindows == null || _hostWindows.DeclaringType != host.GetType())
            {
                var p = host.GetType().GetProperty("Windows", BindingFlags.Instance | BindingFlags.Public);
                return p == null ? null : p.GetValue(host, null) as IList;
            }
            return _hostWindows.GetValue(host) as IList;
        }

        /// <summary>The window host of the mission's main view (the one the game's own windows are on): MainGameView.FindName("WindowHost").</summary>
        static object _mainHostCache, _mainHostView;
        static object MainHost()
        {
            try
            {
                var a = Active; var v = a == null ? null : a.MainGameViewInstance() as FrameworkElement;
                if (v == null) { _mainHostCache = _mainHostView = null; return null; }
                if (_mainHostCache != null && ReferenceEquals(_mainHostView, v)) return _mainHostCache;   // the host of a view does not change (asked many times a frame)
                var h = v.FindName("WindowHost");
                if (h != null) { _mainHostCache = h; _mainHostView = v; }
                if (h != null || _hostType == null) return h;
                // the older game build: the manager's current host, when it sits on the main view
                var mgrT = _hostType.Assembly.GetType("SeapowerUI.WindowHostManager");
                var sg = _hostType.Assembly.GetType("SeaPower.Singleton`1").MakeGenericType(mgrT);
                var cur = SGet(sg.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null), "CurrentHost");
                return cur != null && ReferenceEquals(SGet(cur, "AssociatedObject"), v) ? cur : null;
            }
            catch { return null; }
        }

        static object HostOf(int display)
        {
            if (display <= 0) return MainHost();
            foreach (var m in Monitors) if (m.Index == display) return m.Host;
            return null;
        }

        static bool Has(IList list, object vm)
        {
            if (list == null || vm == null) return false;
            foreach (var o in list) if (ReferenceEquals(o, vm) || vm.Equals(o)) return true;
            return false;
        }

        /// <summary>The host whose Windows has the view model (the main one, or a monitor's).</summary>
        static object OwnerOf(object vm)
        {
            var main = MainHost();
            if (Has(HostWindows(main), vm)) return main;
            foreach (var m in Monitors) if (Has(HostWindows(m.Host), vm)) return m.Host;
            return null;
        }

        static object ParamOf(object commandArgs)
        {
            if (commandArgs == null) return null;
            try
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var t = commandArgs.GetType();
                var p = t.GetProperty("Parameter", f);
                if (p != null) return p.GetValue(commandArgs, null);
                var fi = t.GetField("Parameter", f) ?? t.GetField("<Parameter>k__BackingField", f) ?? t.GetField("_parameter", f);
                if (fi != null) return fi.GetValue(commandArgs);
                if (_traceOnce.Add("paramof" + t.FullName)) Trace("monitors: no Parameter in " + t.FullName);
            }
            catch { }
            return null;
        }

        // a view model lives on one host: a host does not open what another one shows, and closes / answers for what another one shows
        static bool MonHostOpenNewWindowPrefix(object __instance, object __1)
        {
            if (Monitors.Count == 0) return true;
            var vm = ParamOf(__1);
            var owner = vm == null ? null : OwnerOf(vm);
            if (owner == null && vm != null && AddToWantedHost(vm)) return false;      // opens straight on its monitor
            return owner == null || ReferenceEquals(owner, __instance);
        }

        // view model type -> the display its window was last wanted on (learned from the windows that loaded / were moved)
        static readonly Dictionary<string, int> _vmDisp = new Dictionary<string, int>();

        /// <summary>A window that is not open yet and was on another monitor last time: added to that monitor's host at once (no flash on the main one).</summary>
        static bool AddToWantedHost(object vm)
        {
            try
            {
                int d;
                if (!_vmDisp.TryGetValue(vm.GetType().Name, out d) || d <= 0) return false;
                var host = HostOf(d); var list = HostWindows(host);
                if (list == null) return false;
                if (!Has(list, vm)) list.Add(vm);
                CloseUpdate();
                return true;
            }
            catch { return false; }
        }

        /// <summary>The view model of the host's Windows a window element shows (its DataContext, or the content of the presenter above it: the World View's window has the main view model).</summary>
        object EntryOf(object w)
        {
            var dc = Get(w, "DataContext");
            if (dc != null && OwnerOf(dc) != null) return dc;
            object p = VtParent(w);
            for (int i = 0; p != null && i < 6; i++, p = VtParent(p))
            {
                var c = Get(p, "Content");
                if (c != null && OwnerOf(c) != null) return c;
                var d = Get(p, "DataContext");
                if (d != null && OwnerOf(d) != null) return d;
            }
            return dc;
        }

        static bool MonHostCanCloseWindowPrefix(object __instance, object __1, ref bool __result)
        {
            if (Monitors.Count == 0) return true;
            var vm = ParamOf(__1);
            var owner = vm == null ? null : OwnerOf(vm);
            if (owner == null || ReferenceEquals(owner, __instance)) return true;
            __result = true;
            return false;
        }

        static bool MonHostCloseWindowPrefix(object __instance, object __0, object __1)
        {
            if (Monitors.Count == 0) return true;
            var vm = ParamOf(__1);
            var owner = vm == null ? null : OwnerOf(vm);
            if (owner == null || ReferenceEquals(owner, __instance)) return true;
            try { owner.GetType().GetMethod("CloseWindow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(owner, new[] { __0, __1 }); }
            catch (Exception e) { Trace("monitors: close on the other host: " + (e.InnerException ?? e).Message); }
            return false;
        }

        // ------------------------------------------------------------ the views of the other monitors

        float _monNext;
        /// <summary>Every frame (cheap checks): the views exist while the feature is on in a mission; size and scale follow the display.</summary>
        void MonitorsStep(float now)
        {
            StartCursorStep(now);
            bool inMission = MainVm() != null;
            bool hostFound = inMission && MainHost() != null;
            bool want = MonitorsOn && hostFound;
            if (inMission)
            {
                // once per reason: why the monitors are (not) in use (the text is made only when something it says has changed)
                int key = (_useMonitors.Value ? 1 : 0) | (MapOn() ? 2 : 0) | (_monBroken ? 4 : 0) | (hostFound ? 8 : 0) | (DisplayCount() << 4);
                if (key != _whyKey)
                {
                    _whyKey = key;
                    string why = "monitors: " + (_useMonitors.Value ? "option on" : "option OFF") + ", desktop mode " + (MapOn() ? "on" : "OFF") + ", displays " + DisplayCount() +
                                 (_monBroken ? ", BROKEN (see above)" : "") + ", main window host " + (hostFound ? "found" : "NOT FOUND");
                    if (_traceOnce.Add(why)) Trace(why);
                }
            }
            if (!want)
            {
                if (Monitors.Count > 0) { DestroyMonitors(); SecondMapHandler(true); }
                return;
            }
            int n = DisplayCount();
            if (Monitors.Count != n - 1)
            {
                if (now < _monNext) return;
                _monNext = now + 1f;
                DestroyMonitors();
                for (int i = 1; i < n; i++) BuildMonitor(i);
                SecondMapHandler(false);
            }
            else
            {
                float ui = UiScaleNow();
                foreach (var m in Monitors)
                {
                    // while a load screen covers the main display the other displays show no windows either (they were drawn there,
                    // e.g. the World View with its 3D picture, before the mission was ready)
                    if (m.Root != null)
                    {
                        var vis = LoadScreenRunning ? Visibility.Hidden : Visibility.Visible;
                        if (m.Root.Visibility != vis) { m.Root.Visibility = vis; Trace("monitors: display " + (m.Index + 1) + " " + (LoadScreenRunning ? "hidden under the load screen" : "shown, the load screen is over")); }
                    }
                    int w = 0, h = 0;
                    try { var d = UnityEngine.Display.displays[m.Index]; w = d.renderingWidth; h = d.renderingHeight; } catch { }
                    MonitorBackdrop(m);
                    if (w == m.W && h == m.H && Math.Abs(ui - m.Ui) < 0.0001f) continue;
                    m.W = w; m.H = h; m.Ui = ui;
                    ApplyMonitorScale(m);
                }
            }
            MonitorMovesStep();
            MonitorInputStep(now);
            MonitorMenuStep();
        }

        int _whyKey = -1;
        bool _mainMenuOpen;
        static FieldInfo _ctxCanvas;
        /// <summary>The game has one context menu (the main view's ContextMenuCanvas); it opens on the main display. When it opens while the mouse is on another
        /// display (a right click on the Tactical Display there), it is closed and the same menu items open in a menu of that display's view.</summary>
        void MonitorMenuStep()
        {
            if (Monitors.Count == 0) return;   // one display: the game's menu opens where it should
            try
            {
                var mv = MainGameViewInstance(); if (mv == null) return;
                var f = _ctxCanvas ?? (_ctxCanvas = mv.GetType().GetField("ContextMenuCanvas", BindingFlags.Instance | BindingFlags.Public));
                var canvas = f == null ? null : f.GetValue(mv) as FrameworkElement;
                var menu = canvas == null ? null : canvas.ContextMenu;
                if (menu == null) return;
                bool open = menu.IsOpen;
                if (open && !_mainMenuOpen)
                {
                    int d = MouseDisplay(); MonitorView m = null;
                    foreach (var x in Monitors) if (x.Index == d) { m = x; break; }
                    if (m != null)
                    {
                        var items = menu.ItemsSource;
                        if (m.Menu == null)
                        {
                            var host = new Canvas { Width = 0f, Height = 0f, IsHitTestVisible = false };
                            m.Menu = new ContextMenu();
                            // like the game's own menu: the right button's release that opens it must not close it
                            m.Menu.PreviewMouseRightButtonDown += (a, b) => b.Handled = true;
                            m.Menu.PreviewMouseRightButtonUp += (a, b) => b.Handled = true;
                            host.ContextMenu = m.Menu;
                            ((Panel)m.Root).Children.Add(host);
                        }
                        var mm = m.Menu;
                        try { if (menu.Style != null) mm.Style = menu.Style; } catch { }
                        try { if (menu.ItemContainerStyle != null) mm.ItemContainerStyle = menu.ItemContainerStyle; } catch { }
                        try { if (menu.ItemTemplate != null) mm.ItemTemplate = menu.ItemTemplate; } catch { }
                        try { if (menu.Template != null) mm.Template = menu.Template; } catch { }
                        menu.IsOpen = false; open = false;
                        mm.ItemsSource = items;
                        mm.IsOpen = true;
                        Trace("monitors: context menu opened on display " + (m.Index + 1));
                    }
                }
                _mainMenuOpen = open;
            }
            catch (Exception e) { if (_traceOnce.Add("menu" + e.Message)) Trace("monitors: context menu: " + e.Message); }
        }

        /// <summary>The picture of the main monitor's backdrop behind the windows of this monitor (own sprite object on layer 31, cover-fitted).</summary>
        static void MonitorBackdrop(MonitorView m)
        {
            try
            {
                if (_backdropSprite == null || m.Go == null) return;
                if (m.Bd != null && m.BdW == m.W && m.BdH == m.H) return;   // nothing to refit (this is asked every frame)
                var srcSr = _backdropSprite.GetComponent<UnityEngine.SpriteRenderer>();
                if (srcSr == null || srcSr.sprite == null) return;
                if (m.Bd == null)
                {
                    var g = new UnityEngine.GameObject("AlternativeGameInterface_MonitorBackdrop" + (m.Index + 1)) { layer = 31 };
                    g.transform.SetParent(m.Go.transform, false);
                    g.transform.localPosition = new UnityEngine.Vector3(0f, 0f, 10f);
                    g.AddComponent<UnityEngine.SpriteRenderer>().sprite = srcSr.sprite;
                    m.Bd = g.transform; m.BdW = 0;
                    m.Go.transform.position = new UnityEngine.Vector3(50000f + 1000f * m.Index, 0f, 0f);
                    m.Cam.orthographic = true; m.Cam.orthographicSize = 5f; m.Cam.nearClipPlane = 0.1f; m.Cam.farClipPlane = 100f;
                    m.Cam.cullingMask = 1 << 31;
                }
                if (m.BdW == m.W && m.BdH == m.H) return;
                m.BdW = m.W; m.BdH = m.H;
                if (m.H <= 0) return;
                var sp = srcSr.sprite;
                float viewW = 10f * m.W / m.H, viewH = 10f, w0 = sp.rect.width / 100f, h0 = sp.rect.height / 100f;
                float k = Math.Max(viewW / w0, viewH / h0);
                m.Bd.localScale = new UnityEngine.Vector3(k, k, 1f);
            }
            catch { }
        }

        MonitorView _pressView; int _pressMask; float[] _lastDown = new float[3]; UnityEngine.Vector2 _lastDownPos;
        MethodInfo _mvDown, _mvUp, _mvDbl, _mvWheel;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDoubleClickTime();
        /// <summary>The system's double-click time (Windows setting, 0.5 s by default; at least 0.25 s).</summary>
        static float DoubleClickSec() { try { return Math.Max(0.25f, GetDoubleClickTime() / 1000f); } catch { return 0.5f; } }

        int _pressN;
        /// <summary>Trace of a left press that reaches a view: on a monitor's view (where it landed, the element under it) and on the MAIN view while the mouse is on another display
        /// (the press that grabs the Tactical Display instead of the window on the monitor). No press line on a monitor = the press never came to its view; a line with the
        /// element chain = it came and the cause is in the element / its command. At most 40 lines.</summary>
        void PressProbe(string where, FrameworkElement view, object args)
        {
            if (_pressN >= 40) return;
            try
            {
                var e = args as MouseButtonEventArgs; if (e == null) return;
                int md = MouseDisplay();
                if (view.Tag == null && md <= 0) return;                  // the main view and the mouse on the main display: nothing to say
                _pressN++;
                var p = e.GetPosition(view);
                var sb = new System.Text.StringBuilder("monitors: press on " + where + " at " + (int)p.X + "," + (int)p.Y + " (mouse display " + (md + 1) + ", RelativeMouseAt " + UnityEngine.Display.RelativeMouseAt(UnityEngine.Input.mousePosition) + "): ");
                DependencyObject d = e.Source as DependencyObject;
                for (int i = 0; i < 9 && d != null; i++, d = VisualTreeHelper.GetParent(d))
                {
                    var fe = d as FrameworkElement;
                    sb.Append(i == 0 ? "" : " < ").Append(d.GetType().Name);
                    if (fe != null && !string.IsNullOrEmpty(fe.Name)) sb.Append('#').Append(fe.Name);
                }
                Trace(sb.ToString());
            }
            catch (Exception ex) { _pressN = 40; Trace("monitors: press probe: " + ex.Message); }
        }

        int _probeN; float _probeAt; int _probeLast = -2; string _probeHead;
        /// <summary>Trace for "windows on another monitor can be seen but not clicked": every move of the mouse to another display (as Display.RelativeMouseAt tells it), and the case that
        /// says the display is not known although the mouse is outside the game's own window rectangle (RelativeMouseAt gives zero there: everything then goes to the main display).
        /// At most 60 lines in a run, a sample every 1.5 s while the mouse stays outside.</summary>
        void MouseProbe(float now, UnityEngine.Vector3 mp, UnityEngine.Vector3 rel)
        {
            if (_probeN >= 60 || Monitors.Count == 0) return;
            try
            {
                int cd = rel == UnityEngine.Vector3.zero ? 0 : (int)rel.z;
                bool outside = mp.x < 0f || mp.y < 0f || mp.x >= UnityEngine.Screen.width || mp.y >= UnityEngine.Screen.height;
                bool lost = outside && cd == 0;                       // outside the main window and still "display 0": the game's mouse is the main display's
                int key = cd * 4 + (lost ? 1 : 0) + (outside ? 2 : 0);
                if (key == _probeLast && now - _probeAt < 1.5f) return;
                if (key == _probeLast && !lost) return;
                _probeLast = key; _probeAt = now; _probeN++;
                if (_probeHead == null)
                {
                    var sb = new System.Text.StringBuilder("monitors: displays");
                    for (int i = 0; i < UnityEngine.Display.displays.Length; i++) { var d = UnityEngine.Display.displays[i]; sb.Append(" [" + (i + 1) + ": system " + d.systemWidth + "x" + d.systemHeight + ", rendering " + d.renderingWidth + "x" + d.renderingHeight + ", active " + d.active + "]"); }
                    sb.Append("; game window " + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height + ", mode " + UnityEngine.Screen.fullScreenMode + ", main display of the game " + UnityEngine.PlayerPrefs.GetInt("UnitySelectMonitor", 0));
                    _probeHead = sb.ToString(); Trace(_probeHead);
                }
                Trace("monitors: mouse " + (int)mp.x + "," + (int)mp.y + " -> RelativeMouseAt " + (int)rel.x + "," + (int)rel.y + " display " + (cd + 1) + (lost ? "  <- outside the game window but taken for the main display" : ""));
            }
            catch { }
        }

        /// <summary>Buttons and wheel for the view of the display the mouse is on (positions from Display.RelativeMouseAt, y from the top of that display).
        /// A button released elsewhere goes to the view it was pressed on. Moves are NoesisView's own.</summary>
        void MonitorInputStep(float now)
        {
            if (Monitors.Count == 0) return;
            if (_mvDown == null)
            {
                var t = _monViewType; var bt = typeof(MouseButton);
                _mvDown = t.GetMethod("MouseButtonDown", new[] { typeof(int), typeof(int), bt });
                _mvUp = t.GetMethod("MouseButtonUp", new[] { typeof(int), typeof(int), bt });
                _mvDbl = t.GetMethod("MouseDoubleClick", new[] { typeof(int), typeof(int), bt });
                _mvWheel = t.GetMethod("MouseWheel", new[] { typeof(int), typeof(int), typeof(int) });
                if (_mvDown == null || _mvUp == null || _mvWheel == null) { _monBroken = true; Trace("monitors: NoesisView mouse methods not found: off"); return; }
            }
            var mp = UnityEngine.Input.mousePosition;
            var rel = UnityEngine.Display.RelativeMouseAt(mp);
            MouseProbe(now, mp, rel);
            {
                int cd = rel == UnityEngine.Vector3.zero ? 0 : (int)rel.z;
                float cx, cy;
                if (cd == 0) { cx = mp.x; cy = UnityEngine.Screen.height - mp.y; }
                else { float dw, dh; DisplayPx(cd, out dw, out dh); cx = rel.x; cy = dh - rel.y; }
                MouseLeftDisplayStep(cd, cx, cy);
                TransferStep(now, cd, cx, cy);
            }
            MonitorView over = null;
            if (rel != UnityEngine.Vector3.zero) foreach (var m in Monitors) if (m.Index == (int)rel.z) { over = m; break; }
            int px = (int)rel.x;
            for (int b = 0; b < 3; b++)
            {
                var btn = (MouseButton)b;
                if (UnityEngine.Input.GetMouseButtonDown(b) && over == null) _mainPress |= 1 << b;
                if (UnityEngine.Input.GetMouseButtonUp(b)) { _mainPress &= ~(1 << b); if ((_pressMask & (1 << b)) != 0) _monMouseOffFrame = UnityEngine.Time.frameCount; }
                if (UnityEngine.Input.GetMouseButtonDown(b) && over != null)
                {
                    int y = over.H - (int)rel.y;
                    bool dbl = now - _lastDown[b] < DoubleClickSec() && ReferenceEquals(_pressView, over) && Math.Abs(_lastDownPos.x - px) <= 10f && Math.Abs(_lastDownPos.y - y) <= 10f;
                    _lastDown[b] = dbl ? 0f : now; _lastDownPos = new UnityEngine.Vector2(px, y);
                    _pressView = over; _pressMask |= 1 << b;
                    (dbl && _mvDbl != null ? _mvDbl : _mvDown).Invoke(over.View, new object[] { px, y, btn });
                }
                if (UnityEngine.Input.GetMouseButtonUp(b) && (_pressMask & (1 << b)) != 0)
                {
                    _pressMask &= ~(1 << b);
                    var v = _pressView;
                    if (v != null)
                    {
                        int ux = ReferenceEquals(v, over) ? px : (int)_lastDownPos.x, uy = ReferenceEquals(v, over) ? v.H - (int)rel.y : (int)_lastDownPos.y;
                        _mvUp.Invoke(v.View, new object[] { ux, uy, btn });
                    }
                }
            }
            float sd = UnityEngine.Input.mouseScrollDelta.y;
            if (over != null && sd != 0f)
                _mvWheel.Invoke(over.View, new object[] { px, over.H - (int)rel.y, (int)(sd * 120f) });
        }

        int _lastMouseDisp; UnityEngine.Component _mainNoesis; MethodInfo _mvMove; bool _mvMoveTried;
        UnityEngine.Component _resetView; int _resetDisp;
        float _px, _py, _dx, _dy;      // the mouse on its display, pixels from the top left, and its last step on that display

        float _startCursorAt = -1f, _scNext; bool _startCursorDone;
        /// <summary>A new mission's view has the mouse at its top left corner (where a window's resize zone often is) until the first real move: the cursor stayed a resize one.
        /// A little after the view exists it gets the mouse where it really is.</summary>
        void StartCursorStep(float now)
        {
            try
            {
                if (MainVm() == null) { _startCursorAt = -1f; _startCursorDone = false; _mainNoesis = null; return; }
                if (_startCursorDone) return;
                if (_monViewType == null) _monViewType = typeof(FrameworkElement).Assembly.GetType("NoesisView");
                if (_monViewType == null) { _startCursorDone = true; return; }
                if (_startCursorAt < 0f && now < _scNext) return;
                var view = _startCursorAt < 0f ? NoesisOf(0) : null;
                if (_startCursorAt < 0f && view == null) { _scNext = now + 0.5f; return; }
                if (_startCursorAt < 0f) { _startCursorAt = now + 0.7f; return; }
                if (now < _startCursorAt) return;
                view = NoesisOf(0);
                _startCursorDone = true;
                if (view == null) return;
                var mm = _monViewType.GetMethod("MouseMove", new[] { typeof(int), typeof(int) });
                if (mm == null) return;
                var mp = UnityEngine.Input.mousePosition;
                bool away = Monitors.Count > 0 && MouseDisplay() > 0;
                mm.Invoke(view, new object[] { away ? -100000 : (int)mp.x, away ? -100000 : (int)(UnityEngine.Screen.height - mp.y) });
                Trace("mission start: the mouse given to the interface");
            }
            catch (Exception e) { _startCursorDone = true; Trace("mission start cursor: " + e.Message); }
        }

        // ------------------------------------------------------------ a window dragged by its title bar from one display to another

        sealed class Xfer { public WeakReference Vm, Win; public int From, To, Btn, Phase, Frame; public float Ox, Oy, Ix, Iy, T; }   // Ox, Oy: the grabbed point, UI units from the window's panel
        Xfer _xfer;

        static float LayoutScale(int disp)
        {
            foreach (var m in Monitors) if (m.Index == disp) return Math.Max(0.05f, m.Ui * m.H / 1080f);
            return Math.Max(0.05f, UiScaleNow() * UnityEngine.Screen.height / 1080f);
        }

        /// <summary>The mouse left the display with the title bar's drag going: the window moves to the host of the display the mouse came to (and is remembered there),
        /// is placed so that the grabbed point is under the mouse, and the drag goes on there with a press on the same place (the button is still down).</summary>
        bool TryStartTransfer(int old, int cur, float lastX, float lastY)
        {
            try
            {
                int ago = UnityEngine.Time.frameCount - _dragFrame;
                var w = _dragWin == null ? null : _dragWin.Target;
                string lab;
                // why a drag across displays did not start (traced when a title bar drag was seen in the last 2 s)
                string why = _xfer != null ? "a transfer still going" : ago > 20 ? "last title bar drag " + ago + " frames ago" : w == null ? "no dragged window" :
                    GetB(w, "Maximized") ? "maximized" : DisplayOf(w) != old ? "window on display " + (DisplayOf(w) + 1) + ", mouse left display " + (old + 1) :
                    HostOf(cur) == null ? "no window host on display " + (cur + 1) : MatchPattern(w, out lab) == null ? "no window pattern" : null;
                if (why != null)
                {
                    if (UnityEngine.Time.unscaledTime - _dragTime < 2f) Trace("monitors: drag to display " + (cur + 1) + " not started: " + why);
                    return false;
                }
                var vm = EntryOf(w); if (vm == null || OwnerOf(vm) == null) { Trace("monitors: drag to display " + (cur + 1) + " not started: no host entry"); return false; }
                var panel = TemplateRootPanel(w); if (panel == null) { Trace("monitors: drag to display " + (cur + 1) + " not started: no template panel"); return false; }
                var s0 = panel.PointToScreen(new Point(0f, 0f)); var s1 = panel.PointToScreen(new Point(100f, 100f));
                float kx = (s1.X - s0.X) / 100f, ky = (s1.Y - s0.Y) / 100f;
                if (kx < 0.01f || ky < 0.01f) { Trace("monitors: drag to display " + (cur + 1) + " not started: scale " + kx + "," + ky); return false; }
                int btn = UnityEngine.Input.GetMouseButton(0) ? 0 : UnityEngine.Input.GetMouseButton(1) ? 1 : 2;
                _xfer = new Xfer
                {
                    Vm = new WeakReference(vm), From = old, To = cur, Btn = btn, Phase = 1, T = UnityEngine.Time.unscaledTime,
                    Ox = (lastX - s0.X) / kx, Oy = (lastY - s0.Y) / ky, Ix = GetF(w, "X", 0f) - s0.X / kx, Iy = GetF(w, "Y", 0f) - s0.Y / ky
                };
                // the press that began on the old display is over for the old view; the mouse events of the main view are not held back any more
                _pressMask &= ~(1 << btn); if (old == 0) _mainPress &= ~(1 << btn);
                SetWindowDisplay(w, cur);
                _dragFrame = -1000;
                Trace("monitors: '" + TextOf(Get(w, "Header")) + "' dragged to display " + (cur + 1));
                return true;
            }
            catch (Exception e) { Trace("monitors: drag to another display: " + e.Message); _xfer = null; return false; }
        }

        float PlaceUnderMouse(object w, Xfer x)
        {
            float sc = LayoutScale(x.To);
            // the grabbed point (UI units from the window's panel) under the mouse: Ox / Oy are in UI units - in pixels of the old display they put
            // the window off by the difference of the two displays' scales (1.333 vs 1.0 px per unit): the press that goes on with the drag
            // landed below the title bar (on the World View picture) and the drag was lost (trace a5259b3b)
            float tx = _px / sc - x.Ox + x.Ix, ty = Math.Max(0f, _py / sc - x.Oy + x.Iy);
            SetF(w, "X", tx); SetF(w, "Y", ty);
            return tx + ty;
        }

        void EndTransfer() { _xfer = null; }

        readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _xferWins = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();

        /// <summary>A window created by dragging to another display: it is placed by the transfer and by the user afterwards, the remembered layout is not applied to it.</summary>
        bool InTransfer(object w) { object o; return w != null && _xferWins.TryGetValue(w, out o); }

        /// <summary>
        /// A window dragged to another display follows the mouse from here on until the button is released - the mod moves it every frame
        /// (no new press on the new display's view: that press had to land on the title bar, and a fast mouse that left the title bar before
        /// it landed lost the drag). The mouse going on to yet another display moves the window there too.
        /// Phase 1: waiting for the window on the new display's host (at most 4 s); 2: following.
        /// </summary>
        void TransferStep(float now, int cd, float cx, float cy)
        {
            var x = _xfer; if (x == null) return;
            try
            {
                var vm = x.Vm.Target; if (vm == null) { EndTransfer(); return; }
                if (!UnityEngine.Input.GetMouseButton(x.Btn)) { Trace("monitors: drag across displays ended on display " + (x.To + 1)); EndTransfer(); return; }
                if (x.Phase == 1)
                {
                    if (now - x.T > 4f) { Trace("monitors: drag across displays: the window did not appear on display " + (x.To + 1)); EndTransfer(); return; }
                    var hostTo = HostOf(x.To);
                    if (x.Win == null || hostTo == null || !ReferenceEquals(OwnerOf(vm), hostTo)) return;
                    x.Phase = 2;
                    var old = NoesisOf(x.From);
                    if (old != null && _mvUp != null) { _mvUp.Invoke(old, new object[] { -1, -1, (MouseButton)x.Btn }); if (_mvMove != null) _mvMove.Invoke(old, new object[] { -100000, -100000 }); }
                }
                var w = x.Win == null ? null : x.Win.Target; if (w == null || !GetB(w, "IsLoaded")) return;
                if (cd != x.To)
                {
                    // on to another display (back to the first one too): the window goes there and keeps following
                    if (HostOf(cd) == null) return;
                    SetWindowDisplay(w, cd);
                    x.From = x.To; x.To = cd; x.Win = null; x.Phase = 1; x.T = now;
                    Trace("monitors: '" + TextOf(Get(w, "Header")) + "' dragged on to display " + (cd + 1));
                    return;
                }
                PlaceUnderMouse(w, x);
            }
            catch (Exception e) { Trace("monitors: drag to another display: " + e.Message); EndTransfer(); }
        }

        static MonitorView ViewOfDisplay(int d) { foreach (var m in Monitors) if (m.Index == d) return m; return null; }

        UnityEngine.Component NoesisOf(int disp)
        {
            if (disp != 0)
            {
                foreach (var m in Monitors) if (m.Index == disp) return m.View;
                return null;
            }
            if (_mainNoesis == null)
            {
                var mv = MainGameViewInstance() as FrameworkElement;
                foreach (var o in UnityEngine.Object.FindObjectsOfType(_monViewType))
                    if (mv != null && ReferenceEquals(_monViewType.GetProperty("Content").GetValue(o, null), mv)) { _mainNoesis = (UnityEngine.Component)o; break; }
            }
            return _mainNoesis;
        }

        static void DisplayPx(int disp, out float w, out float h)
        {
            w = UnityEngine.Screen.width; h = UnityEngine.Screen.height;
            if (disp > 0) { try { var d = UnityEngine.Display.displays[disp]; w = d.renderingWidth; h = d.renderingHeight; } catch { } }
        }

        /// <summary>The mouse went to another display. The view it left gets a move far outside, so that nothing there stays hovered (a resize cursor of a window
        /// edge stayed until the mouse came back) - never in the middle of a drag / resize (a button is held: that would be a jump of the whole window):
        /// then the view gets a move to the edge the mouse left through (the window follows to the edge) and the reset waits for the button's release.</summary>
        void MouseLeftDisplayStep(int cur, float x, float y)
        {
            float lastX = _px, lastY = _py;
            if (cur == _lastMouseDisp) { _dx = x - _px; _dy = y - _py; }
            _px = x; _py = y;
            try
            {
                if (!_mvMoveTried)
                {
                    _mvMoveTried = true;
                    _mvMove = _monViewType.GetMethod("MouseMove", new[] { typeof(int), typeof(int) });
                    if (_mvMove == null) Trace("monitors: NoesisView.MouseMove not found, the cursor is not reset on leaving a display");
                }
                if (_mvMove == null) return;
                bool held = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetMouseButton(2);
                if (_resetView != null && !held)
                {
                    if (cur != _resetDisp) _mvMove.Invoke(_resetView, new object[] { -100000, -100000 });
                    _resetView = null;
                }
                if (cur == _lastMouseDisp) return;
                int old = _lastMouseDisp; _lastMouseDisp = cur;
                var view = NoesisOf(old);
                if (view == null) return;
                if (!held) { _mvMove.Invoke(view, new object[] { -100000, -100000 }); return; }
                if (_xfer != null || TryStartTransfer(old, cur, lastX, lastY)) return;   // a window being carried across displays: TransferStep moves it
                float w, h; DisplayPx(old, out w, out h);
                // the edge the mouse left through: by where it was and where it was heading
                int tx = lastX < w * 0.1f ? 0 : lastX > w * 0.9f ? (int)w - 1 : _dx > 0f ? (int)w - 1 : _dx < 0f ? 0 : (int)lastX;
                int ty = lastY < h * 0.1f ? 0 : lastY > h * 0.9f ? (int)h - 1 : _dy > 0f ? (int)h - 1 : _dy < 0f ? 0 : (int)lastY;
                _mvMove.Invoke(view, new object[] { tx, ty });
                _resetView = view; _resetDisp = old;
            }
            catch (Exception e) { if (_traceOnce.Add("mleft" + e.Message)) Trace("monitors: leaving a display: " + e.Message); }
        }

        static float UiScaleNow()
        {
            float ui = 1f;
            try
            {
                var asm = GameAssembly();
                var om = asm == null ? null : asm.GetType("SeaPower.OptionsManager");
                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
                var exists = om == null ? null : om.GetMethod("InstanceExists", sf, null, new[] { typeof(bool) }, null);
                if (exists != null && (bool)exists.Invoke(null, new object[] { false }))
                {
                    var p = SGet(om.GetProperty("Instance", sf).GetValue(null, null), "UIScale");
                    if (p is float && (float)p > 0f) ui = (float)p;
                }
            }
            catch { }
            return ui;
        }

        static void ApplyMonitorScale(MonitorView m)
        {
            if (m.Scale == null || m.H <= 0) return;
            float k = Math.Max(0.05f, m.Ui * m.H / 1080f);
            m.Scale.ScaleX = k; m.Scale.ScaleY = k;
        }

        const string MonitorXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid.LayoutTransform><ScaleTransform x:Name=\"Sc\" ScaleX=\"1\" ScaleY=\"1\"/></Grid.LayoutTransform>" +
            "<UserControl>" +   // the windows' own bindings look for a second UserControl above them (Formation Manager's right click: AncestorLevel=2, the main view): here this one, with the main view model as its DataContext
            "<ItemsControl x:Name=\"Items\">" +
            "<ItemsControl.ItemsPanel><ItemsPanelTemplate><Grid/></ItemsPanelTemplate></ItemsControl.ItemsPanel>" +
            "<ItemsControl.ItemTemplate><DataTemplate><ContentControl Content=\"{Binding}\" HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\"/></DataTemplate></ItemsControl.ItemTemplate>" +
            "</ItemsControl></UserControl></Grid>";

        void BuildMonitor(int index)
        {
            MonitorView m = null;
            try
            {
                var disp = UnityEngine.Display.displays[index];
                if (!disp.active) disp.Activate();
                if (_monViewType == null) _monViewType = typeof(FrameworkElement).Assembly.GetType("NoesisView");
                if (_monViewType == null || _hostType == null) { _monBroken = true; Trace("monitors: NoesisView / WindowHost not found: off"); return; }

                RemoveClearCamera(index);
                m = new MonitorView { Index = index };
                m.Go = new UnityEngine.GameObject("AlternativeGameInterface_Monitor" + (index + 1));
                UnityEngine.Object.DontDestroyOnLoad(m.Go);
                m.Cam = m.Go.AddComponent<UnityEngine.Camera>();
                m.Cam.targetDisplay = index;
                m.Cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
                m.Cam.backgroundColor = new UnityEngine.Color(0.55f, 0.57f, 0.60f, 1f);
                m.Cam.cullingMask = 0;
                m.Cam.depth = 0f;
                m.Cam.allowHDR = false; m.Cam.allowMSAA = false; m.Cam.useOcclusionCulling = false;
                // only a flat picture and the UI are drawn here: no G-buffer / depth texture / shadows of the game's rendering path
                m.Cam.renderingPath = UnityEngine.RenderingPath.Forward; m.Cam.depthTextureMode = UnityEngine.DepthTextureMode.None;
                m.Cam.forceIntoRenderTexture = false; m.Cam.allowDynamicResolution = false;
                m.View = m.Go.AddComponent(_monViewType);          // after the camera: NoesisView finds it in OnEnable
                CopyViewSettings(m.View);

                m.Root = (FrameworkElement)GUI.ParseXaml(MonitorXaml);
                m.Root.Tag = "AGI_MON" + index;
                { var pr = m.Root; int di = index + 1; pr.PreviewMouseLeftButtonDown += (o, ea) => PressProbe("monitor " + di, pr, ea); }
                m.Scale = (ScaleTransform)m.Root.FindName("Sc");
                var items = (ItemsControl)m.Root.FindName("Items");

                // the window host: a WindowHost of the game on this root (its constructor makes itself the manager's current host: put back)
                var mgrT = _hostType.Assembly.GetType("SeapowerUI.WindowHostManager");
                var sgT = _hostType.Assembly.GetType("SeaPower.Singleton`1").MakeGenericType(mgrT);
                var mgr = sgT.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var curProp = mgrT.GetProperty("CurrentHost", BindingFlags.Instance | BindingFlags.Public);
                var before = curProp.GetValue(mgr, null);
                try { m.Host = Activator.CreateInstance(_hostType); }
                finally { curProp.SetValue(mgr, before, null); }
                Noesis.Interactivity.Interaction.GetBehaviors(m.Root).Add((Noesis.Interactivity.Behavior)m.Host);

                // the data templates of the windows are in the main view's resources
                var mainView = MainGameViewInstance() as FrameworkElement;
                if (mainView != null) m.Root.Resources.MergedDictionaries.Add(mainView.Resources);
                m.Root.DataContext = MainVm();
                items.ItemsSource = HostWindows(m.Host);

                m.Ui = UiScaleNow(); m.W = disp.renderingWidth; m.H = disp.renderingHeight;
                ApplyMonitorScale(m);
                var create = _monViewType.GetMethod("CreateView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                create.Invoke(m.View, new object[] { m.Root });
                try { var mmv = _monViewType.GetMethod("MouseMove", new[] { typeof(int), typeof(int) }); if (mmv != null) mmv.Invoke(m.View, new object[] { -100000, -100000 }); } catch { }      // its mouse is not at the top left corner
                Monitors.Add(m);
                Trace("monitors: display " + (index + 1) + " (" + m.W + "x" + m.H + ") has its window host");
            }
            catch (Exception e)
            {
                Trace("monitors: display " + (index + 1) + ": " + e);
                if (m != null && m.Go != null) UnityEngine.Object.Destroy(m.Go);
                _monBroken = true;
            }
        }

        /// <summary>The new view works like the game's views: the same render settings as the main game view's NoesisView.</summary>
        void CopyViewSettings(UnityEngine.Component view)
        {
            try
            {
                UnityEngine.Component src = null;
                var mv = MainGameViewInstance() as FrameworkElement;
                foreach (var o in UnityEngine.Object.FindObjectsOfType(_monViewType))
                {
                    var c = _monViewType.GetProperty("Content").GetValue(o, null);
                    if (mv != null && ReferenceEquals(c, mv)) { src = (UnityEngine.Component)o; break; }
                }
                const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                foreach (var n in new[] { "_isPPAAEnabled", "_tessellationMaxPixelError", "_renderFlags", "_dpiScale", "_continuousRendering", "_emulateTouch", "_clearStencil", "_enableKeyboard", "_enableMouse", "_enableTouch" })
                {
                    var fi = _monViewType.GetField(n, f);
                    if (fi != null && src != null) fi.SetValue(view, fi.GetValue(src));
                }
                var rt = _monViewType.GetProperty("UseRealTimeClock");
                if (rt != null) rt.SetValue(view, true, null);
                if (src == null) Trace("monitors: the main view's NoesisView not found, default render settings");
            }
            catch (Exception e) { Trace("monitors: view settings: " + e.Message); }
        }

        /// <summary>Every window goes back to the main host and the views are removed (the feature is off, the mission is over).</summary>
        void DestroyMonitors()
        {
            var main = MainHost();
            var mainList = HostWindows(main);
            foreach (var m in Monitors)
            {
                try
                {
                    var list = HostWindows(m.Host);
                    if (list != null && mainList != null)
                        foreach (var vm in new List<object>(Cast(list))) { list.Remove(vm); if (!Has(mainList, vm)) mainList.Add(vm); }
                }
                catch (Exception e) { Trace("monitors: windows back to the main monitor: " + e.Message); }
                try { if (m.Go != null) UnityEngine.Object.Destroy(m.Go); } catch { }
                try { MakeClearCamera(m.Index); } catch { }
            }
            if (Monitors.Count > 0) Trace("monitors: views removed, the displays cleared");
            Monitors.Clear();
        }

        // a display cannot be closed once it is on; without a camera it keeps its last picture. A camera that only clears it (black) takes the views' place.
        static readonly Dictionary<int, UnityEngine.GameObject> _clearCams = new Dictionary<int, UnityEngine.GameObject>();

        static void MakeClearCamera(int index)
        {
            UnityEngine.GameObject g;
            if (_clearCams.TryGetValue(index, out g) && g != null) return;
            g = new UnityEngine.GameObject("AlternativeGameInterface_Clear" + (index + 1));
            UnityEngine.Object.DontDestroyOnLoad(g);
            var c = g.AddComponent<UnityEngine.Camera>();
            c.targetDisplay = index; c.clearFlags = UnityEngine.CameraClearFlags.SolidColor; c.backgroundColor = UnityEngine.Color.black;
            c.cullingMask = 0; c.depth = -100f; c.allowHDR = false; c.allowMSAA = false;
            _clearCams[index] = g;
        }

        static void RemoveClearCamera(int index)
        {
            UnityEngine.GameObject g;
            if (_clearCams.TryGetValue(index, out g)) { if (g != null) UnityEngine.Object.Destroy(g); _clearCams.Remove(index); }
        }

        static IEnumerable<object> Cast(IList l) { foreach (var o in l) yield return o; }

        // ------------------------------------------------------------ the monitor of a window

        Dictionary<string, int> MonMap()
        {
            if (_monMap != null) return _monMap;
            _monMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in (_winMonitors.Value ?? "").Split(';'))
            {
                var p = rec.Split('|'); int n;
                if (p.Length == 2 && !string.IsNullOrWhiteSpace(p[0]) && int.TryParse(p[1], out n) && n > 1) _monMap[p[0].Trim()] = n;
            }
            return _monMap;
        }

        void SaveMonMap()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in MonMap()) { if (sb.Length > 0) sb.Append(';'); sb.Append(kv.Key).Append('|').Append(kv.Value); }
            _winMonitors.Value = sb.ToString();
        }

        /// <summary>The display (0 = main) a window pattern is remembered on; 0 when the monitor is not there.</summary>
        int WantedDisplay(object w)
        {
            try
            {
                string label, key = MatchPattern(w, out label); int n;
                if (key == null || !MonMap().TryGetValue(key, out n)) return 0;
                return n - 1 < DisplayCount() ? n - 1 : 0;
            }
            catch { return 0; }
        }

        /// <summary>The display a window element is on: the monitor view whose root is above it, else the main one (0).</summary>
        static int DisplayOf(object e)
        {
            if (Monitors.Count == 0) return 0;
            try
            {
                var v = e as Visual; if (v == null) return 0;
                for (DependencyObject d = v; d != null; d = VisualTreeHelper.GetParent(d))
                {
                    var fe = d as FrameworkElement;
                    var tag = fe == null ? null : fe.Tag as string;
                    if (tag != null && tag.StartsWith("AGI_MON"))
                    {
                        foreach (var m in Monitors) if (tag == "AGI_MON" + m.Index) return m.Index;
                    }
                }
            }
            catch { }
            return 0;
        }

        /// <summary>Windows in a pattern the user can send to another monitor (the Tactical Display and the World View too).</summary>
        bool MonitorChoosable(object w)
        {
            if (!MonitorsOn || DisplayCount() < 2) return false;
            try { string l, k = MatchPattern(w, out l); return k != null; }
            catch { return false; }
        }

        readonly List<KeyValuePair<WeakReference, int>> _monMoves = new List<KeyValuePair<WeakReference, int>>();

        /// <summary>The user picked a monitor in the window's menu.</summary>
        void SetWindowDisplay(object w, int display)
        {
            string label, key = MatchPattern(w, out label);
            if (key == null) return;
            var m = MonMap();
            if (display <= 0) m.Remove(key); else m[key] = display + 1;
            SaveMonMap();
            try { var e = EntryOf(w); if (e != null) _vmDisp[e.GetType().Name] = display; } catch { }
            QueueMove(w, display);
        }

        /// <summary>Loaded handling of a window: when it is not on the monitor it is remembered on, it moves there.</summary>
        void MonitorCheck(object w)
        {
            if (Monitors.Count == 0 || !MonitorsOn) return;
            try
            {
                var xf = _xfer;
                if (xf != null && xf.Phase == 1 && ReferenceEquals(EntryOf(w), xf.Vm.Target)) { xf.Win = new WeakReference(w); try { _xferWins.Add(w, w); } catch { } PlaceUnderMouse(w, xf); }
            }
            catch { }
            int want = WantedDisplay(w);
            try { var e = EntryOf(w); if (e != null) _vmDisp[e.GetType().Name] = want; } catch { }
            if (want != DisplayOf(w)) QueueMove(w, want);
        }

        /// <summary>The Tactical Display's XAML asks the main view (FindAncestor MainGameView) for DataContext.MapClosable (the X button) and ShowPip3D (the draw
        /// toolbar is hidden while the World View overlay shows the same tools); on a monitor there is no such ancestor and the fallbacks (closable, toolbar
        /// shown) win. The same two values are bound here, with the main view model as the source.</summary>
        void MonitorMapBindings(object w)
        {
            if (DisplayOf(w) <= 0 || TypeName(Get(w, "DataContext")) != "SceneMapViewModel") return;
            var vm = MainVm(); if (vm == null) return;
            var dw = w as DependencyObject; if (dw == null) return;
            try
            {
                var cf = w.GetType().GetField("ClosableProperty", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                var cdp = cf == null ? null : cf.GetValue(null) as DependencyProperty;
                if (cdp != null) BindingOperations.SetBinding(dw, cdp, new Binding("MapClosable") { Source = vm, Mode = BindingMode.OneWay });
                // the toolbar: Visibility = not ShowPip3D (the game's InverseCollapsedConverter); with WindowedMapAndWorldView the toolbar is always in the window (MapToolbar.cs)
                DependencyObject bar = null;
                var stack = new Stack<DependencyObject>(); if (!MapOn()) stack.Push(dw);
                for (int budget = 20000; stack.Count > 0 && --budget > 0;)
                {
                    var e = stack.Pop();
                    if (e.GetType().Name == "MapDrawToolbarView") { bar = e; break; }
                    int n = VisualTreeHelper.GetChildrenCount(e);
                    for (int k = n - 1; k >= 0; k--) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
                }
                var conv = (w as FrameworkElement) == null ? null : ((FrameworkElement)w).TryFindResource("InverseCollapsedConverter") as IValueConverter;
                if (bar != null && conv != null)
                    BindingOperations.SetBinding(bar, UIElement.VisibilityProperty, new Binding("ShowPip3D") { Source = vm, Converter = conv, Mode = BindingMode.OneWay });
                Trace("monitors: Tactical Display on display " + (DisplayOf(w) + 1) + ": closable / toolbar bound to the main view model" + (MapOn() ? " (the toolbar: MapToolbar.cs)" : bar == null ? " (toolbar not found)" : conv == null ? " (converter not found)" : ""));
            }
            catch (Exception e) { TraceOnce("monitor map bindings", e); }
        }

        void QueueMove(object w, int display)
        {
            _monMoves.Add(new KeyValuePair<WeakReference, int>(new WeakReference(w), display));
        }

        /// <summary>The moves queued by Loaded / the menu, done outside the layout pass.</summary>
        float _dupNext;
        /// <summary>A view model must be on a host once: later copies (a window opened twice) are removed.</summary>
        void MonitorDupesStep()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _dupNext) return;
            _dupNext = now + 0.5f;
            var hosts = new List<object> { MainHost() };
            foreach (var m in Monitors) hosts.Add(m.Host);
            var seen = new List<object>();
            foreach (var h in hosts)
            {
                var l = HostWindows(h); if (l == null) continue;
                for (int i = 0; i < l.Count; i++)
                {
                    var vm = l[i]; bool dup = false;
                    foreach (var o in seen) if (ReferenceEquals(o, vm)) { dup = true; break; }
                    if (dup) { l.RemoveAt(i); i--; Trace("monitors: a second copy of '" + TextOf(Get(vm, "Header")) + "' removed"); }
                    else seen.Add(vm);
                }
            }
        }

        void MonitorMovesStep()
        {
            MonitorDupesStep();
            if (_monMoves.Count == 0) return;
            var todo = new List<KeyValuePair<WeakReference, int>>(_monMoves);
            _monMoves.Clear();
            foreach (var kv in todo)
            {
                try
                {
                    var w = kv.Key.Target; if (w == null) continue;
                    var vm = EntryOf(w);
                    var from = vm == null ? null : OwnerOf(vm);
                    var to = HostOf(kv.Value);
                    if (vm == null || from == null || to == null || ReferenceEquals(from, to)) continue;
                    HostWindows(from).Remove(vm);
                    if (!Has(HostWindows(to), vm)) HostWindows(to).Add(vm);
                    try { var t = _hostType.Assembly.GetType("SeapowerUI.WindowHostCommands"); var cmd = t.GetProperty("CloseWindowCommand").GetValue(null, null); cmd.GetType().GetMethod("UpdateCanExecute").Invoke(cmd, null); } catch { }
                    Trace("monitors: '" + TextOf(Get(w, "Header")) + "' moved to display " + (kv.Value + 1));
                }
                catch (Exception e) { Trace("monitors: move: " + e.Message); }
            }
        }

        /// <summary>The monitor row of the window's menu: "Monitor  1 2 3" (the current one marked).</summary>
        UIElement MonitorRow(object w, Action close)
        {
            int n = DisplayCount(), cur = WantedDisplay(w);
            var wref = new WeakReference(w);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0f, 6f, 0f, 0f) };
            row.Children.Add(new TextBlock { Text = Loc.T("look.monitor"), FontSize = 11f, Foreground = new SolidColorBrush(Color.FromRgb(150, 156, 162)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4f, 0f, 6f, 0f) });
            for (int i = 0; i < n; i++)
            {
                int display = i; bool on = display == cur;
                var b = new Border
                {
                    Padding = new Thickness(8f, 2f, 8f, 2f), Margin = new Thickness(0f, 0f, 2f, 0f),
                    Background = new SolidColorBrush(on ? Color.FromRgb(52, 58, 66) : Color.FromArgb(0, 0, 0, 0)),
                    Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 12f, Foreground = new SolidColorBrush(on ? Color.FromRgb(255, 255, 255) : Color.FromRgb(230, 232, 235)) }
                };
                b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(Color.FromRgb(62, 69, 79));
                b.MouseLeave += (s, e) => b.Background = new SolidColorBrush(on ? Color.FromRgb(52, 58, 66) : Color.FromArgb(0, 0, 0, 0));
                b.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    var ow = wref.Target; if (ow == null) return;
                    SetWindowDisplay(ow, display);
                    close();
                };
                row.Children.Add(b);
            }
            return row;
        }

        // ------------------------------------------------------------ geometry of the display a window is on

        /// <summary>Size in pixels of the display a window (or any element of a view) is shown on.</summary>
        static void DisplaySize(object e, out float w, out float h)
        {
            int d = DisplayOf(e);
            if (d > 0)
            {
                try { var disp = UnityEngine.Display.displays[d]; w = disp.renderingWidth; h = disp.renderingHeight; return; } catch { }
            }
            w = UnityEngine.Screen.width; h = UnityEngine.Screen.height;
        }

        /// <summary>The mouse is on another display than the main one: its number (0 when on the main display or unknown).</summary>
        static int MouseDisplay()
        {
            try
            {
                var v = UnityEngine.Display.RelativeMouseAt(UnityEngine.Input.mousePosition);
                return v == UnityEngine.Vector3.zero ? 0 : (int)v.z;
            }
            catch { return 0; }
        }
    }
}
