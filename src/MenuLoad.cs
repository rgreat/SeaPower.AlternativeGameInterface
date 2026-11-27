using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Main menu, Scenarios and Campaign screens:
    ///  - [General] AutoOpenLoad: the screen's own Load command (ScenarioMenuViewModel.LoadCommand / CampaignMenuViewModel.LoadCommand - the game's Load game file picker) is run once when the
    ///    screen has been shown. The view models are noted by a postfix on their constructors; AutoLoadStep (every frame, cheap) waits until the view (ScenarioMenuView.Instance /
    ///    CampaignMenuView.Instance) is loaded, visible and has the view model as DataContext. Scenarios: only when the game's own SaveLoadEnabled is on (its Load button is hidden otherwise).
    ///  - [General] MenuWindowsAside: a window of such a screen (the file picker, message boxes: Windows of the MenuPage overlay Canvas) is put in the area right of the menu column
    ///    (the 560-wide left pane with the list and the buttons), centred in what is left; the game centres it in the whole page, over the buttons. Done once per window, when the
    ///    Canvas and the window have a size (tried for 2 s).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _autoLoad, _menuAside, _pickerTall, _autoLoadClose;
        // the Load game window that AutoOpenLoad opened on the Campaign screen: its view model (noted while the command runs), its window (when loaded), the Campaign screen's view model
        static bool _autoOpening; static float _autoAt;
        static WeakReference _autoPickerVm, _autoPickerWin, _autoCampaignVm;
        sealed class MenuVmNote { public WeakReference Vm; public string Kind; public float At; public int Frame; }
        static readonly List<MenuVmNote> _menuVms = new List<MenuVmNote>();
        sealed class AsideNote { public WeakReference Win; public float At; }
        static readonly List<AsideNote> _asideWins = new List<AsideNote>();

        static Noesis.DependencyObject MenuParent(Noesis.DependencyObject e) { try { return Noesis.VisualTreeHelper.GetParent(e); } catch { return null; } }

        void BindMenuLoadConfig()
        {
            _autoLoad = Config.Bind("General", "AutoOpenLoad", true,
                "Main menu: entering Scenarios or Campaign opens the Load game window at once.");
            _autoLoadClose = Config.Bind("General", "AutoLoadCloseOnCampaign", true,
                "Campaign screen: the Load game window opened by \"Open Load game on entering Scenarios and Campaign\" is closed when you click a campaign (the starting conditions open); a window you opened yourself stays.");
            _pickerTall = Config.Bind("General", "FilePickerTall", true,
                "The Load / Save game and other file picker windows have no maximum height (the game stops them at 400).");
            _menuAside = Config.Bind("General", "MenuWindowsAside", true,
                "Main menu screens (Scenarios, Campaign ...): a window (Load game ...) opens beside the menu buttons, not over them.");
        }

        /// <summary>[General] FilePickerTall: the game's file picker window (FilePickerViewModel) has MaxHeight 400 (MaxWidth 600) in its data template; the height limit is taken off, so the window can be made taller.</summary>
        void PickerLoaded(object w)
        {
            if (w == null) return;
            var dc = Get(w, "DataContext");
            if (dc == null || dc.GetType().Name != "FilePickerViewModel") return;
            if (_autoPickerWin == null && _autoPickerVm != null && ReferenceEquals(_autoPickerVm.Target, dc) && Time.unscaledTime - _autoAt < 10f)
                _autoPickerWin = new WeakReference(w);   // the window of the Load game that the mod opened itself
            if (_pickerTall == null || !_pickerTall.Value) return;
            SetV(w, "MaxHeight", float.PositiveInfinity);
        }

        void HookMenuLoad(Harmony harmony, Assembly asm)
        {
            foreach (var n in new[] { "ScenarioMenuViewModel", "CampaignMenuViewModel" })
            {
                var t = asm.GetType("SeapowerUI.ViewModels." + n);
                if (t == null) { Trace("menu load: " + n + " not found (AutoOpenLoad not available there)"); continue; }
                int k = 0;
                foreach (var c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) { if (c.GetParameters().Length == 0) { TryPatch(harmony, c, null, "MenuVmPostfix", null); k++; } }
                Trace("patched " + n + " constructor: " + k);
            }
            var cm = asm.GetType("SeapowerUI.ViewModels.CampaignMenuViewModel");
            var sel = cm == null ? null : cm.GetProperty("SelectedMission", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var selSet = sel == null ? null : sel.GetSetMethod(true);
            if (selSet != null) { TryPatch(harmony, selSet, null, "CampaignChosenPostfix", null); Trace("patched CampaignMenuViewModel.SelectedMission (AutoLoadCloseOnCampaign)"); }
            else Trace("CampaignMenuViewModel.SelectedMission not found: AutoLoadCloseOnCampaign not available");
        }

        static void MenuVmPostfix(object __instance)
        {
            if (__instance == null) return;
            _menuVms.Add(new MenuVmNote { Vm = new WeakReference(__instance), Kind = __instance.GetType().Name, At = Time.unscaledTime, Frame = Time.frameCount });
        }

        /// <summary>A campaign is clicked in the Campaign screen (its starting conditions open): the Load game window the mod opened itself is closed, it covers them.</summary>
        static void CampaignChosenPostfix(object __instance, object value)
        {
            var a = Active;
            if ((object)a == null || value == null || a._autoLoadClose == null || !a._autoLoadClose.Value) return;
            try
            {
                if (_autoPickerWin == null || _autoCampaignVm == null || !ReferenceEquals(_autoCampaignVm.Target, __instance)) return;
                var win = _autoPickerWin.Target as Noesis.FrameworkElement;
                _autoPickerWin = null; _autoPickerVm = null; _autoCampaignVm = null;
                if (win == null || !win.IsLoaded) return;   // the user has closed it already
                if (a.CloseWindowLikeGame(win)) Trace("auto Load: a campaign is chosen, the Load game window the mod opened is closed");
            }
            catch (Exception e) { Trace("AutoLoadCloseOnCampaign: " + (e.InnerException ?? e).Message); }
        }

        static object StaticField(object vm, string viewType)
        {
            var t = vm.GetType().Assembly.GetType("SeapowerUI." + viewType);
            var f = t == null ? null : t.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(null);
        }

        /// <summary>Every frame: the screens noted by the constructors (Load once the view is ready) and the windows waiting to be put aside.</summary>
        void AutoLoadStep()
        {
            if (_menuVms.Count == 0 && _asideWins.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _menuVms.Count - 1; i >= 0; i--)
            {
                var n = _menuVms[i]; var vm = n.Vm.Target;
                if (vm == null || now - n.At > 10f) { _menuVms.RemoveAt(i); continue; }
                if (Time.frameCount - n.Frame < 3) continue;
                try
                {
                    var view = StaticField(vm, n.Kind == "ScenarioMenuViewModel" ? "ScenarioMenuView" : "CampaignMenuView") as Noesis.FrameworkElement;
                    if (view == null || !view.IsLoaded || !view.IsVisible || !ReferenceEquals(view.DataContext, vm)) continue;
                    _menuVms.RemoveAt(i);
                    if (_autoLoad == null || !_autoLoad.Value) continue;
                    if (n.Kind == "ScenarioMenuViewModel") { var en = Get(vm, "SaveLoadEnabled"); if (en is bool && !(bool)en) { Trace("auto Load: the scenarios' Load is off in the game's config"); continue; } }
                    var cmd = Get(vm, "LoadCommand"); if (cmd == null) continue;
                    var ex = cmd.GetType().GetMethod("Execute", new[] { typeof(object) }); if (ex == null) continue;
                    bool campaign = n.Kind == "CampaignMenuViewModel";
                    _autoPickerVm = null; _autoPickerWin = null; _autoCampaignVm = null;
                    _autoOpening = campaign; _autoAt = Time.unscaledTime;
                    try { ex.Invoke(cmd, new object[] { null }); }
                    finally { _autoOpening = false; }
                    if (campaign) _autoCampaignVm = new WeakReference(vm);
                    Trace("auto Load: " + n.Kind + " opened the Load game window");
                }
                catch (Exception e) { _menuVms.RemoveAt(i); Trace("auto Load: " + (e.InnerException ?? e).Message); }
            }
            for (int i = _asideWins.Count - 1; i >= 0; i--)
            {
                var n = _asideWins[i]; var w = n.Win.Target as Noesis.FrameworkElement;
                if (w == null || now - n.At > 2f) { _asideWins.RemoveAt(i); continue; }
                try { if (PutAside(w)) _asideWins.RemoveAt(i); }
                catch (Exception e) { _asideWins.RemoveAt(i); Trace("window aside: " + e.Message); }
            }
        }

        /// <summary>A window has been loaded: when it lies in the overlay of a MenuPage it is queued to be put beside the menu column.</summary>
        void MenuAsideLoaded(object w)
        {
            if (_menuAside == null || !_menuAside.Value) return;
            var fe = w as Noesis.FrameworkElement; if (fe == null) return;
            for (Noesis.DependencyObject d = fe; d != null; d = MenuParent(d))
                if (d.GetType().Name == "MenuPage") { _asideWins.Add(new AsideNote { Win = new WeakReference(w), At = Time.unscaledTime }); return; }
        }

        /// <summary>True when done, false to try again (no size yet).</summary>
        bool PutAside(Noesis.FrameworkElement w)
        {
            Noesis.Canvas canvas = null; Noesis.Control page = null;
            for (Noesis.DependencyObject d = w; d != null; d = MenuParent(d))
            {
                if (canvas == null) canvas = d as Noesis.Canvas;
                if (d.GetType().Name == "MenuPage") { page = d as Noesis.Control; break; }
            }
            if (canvas == null || page == null || page.Template == null) return true;
            var left = page.Template.FindName("leftContentPresenter", page) as Noesis.FrameworkElement;
            if (left == null) return true;
            float cw = canvas.ActualWidth, ww = w.ActualWidth;
            if (cw <= 1f || ww <= 1f || left.ActualWidth <= 1f) return false;
            var lp = left.TranslatePoint(new Noesis.Point(0f, 0f), canvas);
            float minX = lp.X + left.ActualWidth + 10f;                                  // the right edge of the menu column
            float x = minX + (cw - minX - ww) * 0.5f;
            if (x < minX) x = Math.Min(minX, Math.Max(0f, cw - ww));                     // too narrow: as far right as the page allows
            SetF(w, "X", x);
            Trace("window aside: '" + TextOf(Get(w, "Header")) + "' X " + x.ToString("0") + " (menu column ends at " + minX.ToString("0") + ", page " + cw.ToString("0") + ")");
            return true;
        }
    }
}
