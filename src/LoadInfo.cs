using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;


namespace AlternativeGameInterface
{
    /// <summary>
    /// [General] LoadScreenInfo: a line under the progress bar of the loading screen with this mod's name and version (made once, kept after
    /// every change of the load screen's text, LoadScreen.LoadText).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _loadInfo;
        static string _loadInfoText = "";
        static WeakReference _loadInfoBlock = new WeakReference(null);
        static Type _lsViewType;
        static bool _loadInfoWarned;

        void HookLoadInfo(Harmony harmony, Assembly asm)
        {
            _loadInfo = Config.Bind("General", "LoadScreenInfo", true, "Show this mod's name and version under the progress bar of the loading screen.");
            if (!_loadInfo.Value) return;
            var ls = asm.GetType("SeaPower.LoadScreen");
            var set = ls == null ? null : ls.GetProperty("LoadText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null ? null
                : ls.GetProperty("LoadText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetSetMethod(true);
            _lsViewType = asm.GetType("SeapowerUI.LoadScreenView");
            if (set == null || _lsViewType == null) { Trace("loading screen info: LoadScreen.LoadText / LoadScreenView not found"); return; }
            harmony.Patch(set, postfix: Hm("LoadTextPostfix"));
            Trace("patched LoadScreen.LoadText (mods on the loading screen)");
            LoadTextPostfix();   // the screen that is already up gets the line (and a mod's picture) at once, not at the next load step
        }

        static void LoadTextPostfix()
        {
            try
            {
                var a = Active;
                if ((object)a == null) return;
                a.RerollFirstBackground();   // the very first loading screen is running: its picture is replaced as soon as the mod can act
                if (a._loadInfo == null || !a._loadInfo.Value) return;
                string text = ModsLine();
                var box = _loadInfoBlock.Target as Border;
                if (box != null && text == _loadInfoText) return;
                if (box == null)
                {
                    var inst = _lsViewType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    var view = inst == null ? null : inst.GetValue(null, null) as UserControl;
                    var grid = view == null ? null : view.Content as Grid;
                    if (grid == null) { if (!_loadInfoWarned) { _loadInfoWarned = true; Trace("loading screen info: no view yet (view " + (view != null) + ")"); } return; }
                    // the look of the game's loading tip (dark grey panel, thin border, 14 pt light text), but made of opaque solid colours: the tip's own translucent
                    // background / border brush and Opacity gave coloured dashes along the edges and a brighter, denser panel when copied (screenshots of 2.4.0 builds)
                    var tb = new TextBlock { FontSize = 14f, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, IsHitTestVisible = false, Foreground = new SolidColorBrush(Color.FromRgb(214, 216, 220)), UseLayoutRounding = true };
                    box = new Border
                    {
                        Child = tb, Background = new SolidColorBrush(Color.FromRgb(44, 48, 52)), BorderBrush = new SolidColorBrush(Color.FromRgb(70, 74, 80)), BorderThickness = new Thickness(1f),
                        Padding = new Thickness(10f, 6f, 10f, 6f), Margin = new Thickness(0f, 12f, 0f, 0f), UseLayoutRounding = true,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
                    };
                    Grid.SetRow(box, 2); Grid.SetColumn(box, 1);
                    grid.Children.Add(box);
                    _loadInfoBlock = new WeakReference(box);
                    Trace("loading screen info: line added");
                }
                _loadInfoText = text;
                ((TextBlock)box.Child).Text = text;
                box.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception e) { Trace("loading screen info: " + e.Message); }
        }

        static float _startupDoneSince = -1f;

        /// <summary>
        /// The mod was started after the game's first load screen had already finished (Keel waits for its end; Anchor Chain starts the mods
        /// at its "Done"): the postfix of the load coroutine will never see that end. LoadScreen.LoadText stays "Done" after it; seen for more
        /// than a second with no finished screen counted, the first load screen is taken as finished.
        /// </summary>
        void StartupLoadScreenStep()
        {
            if (LoadScreenDone || LoadScreenCount > 0 || _lsType == null) return;
            try
            {
                var sg = _lsType.Assembly.GetType("SeaPower.Singleton`1");
                var gt = sg == null ? null : sg.MakeGenericType(_lsType);
                var exists = gt == null ? null : gt.GetMethod("InstanceExists", BindingFlags.Static | BindingFlags.Public);
                if (exists == null || !(bool)exists.Invoke(null, new object[] { false })) return;
                var inst = gt.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var text = inst == null ? null : Get(inst, "LoadText") as string;
                if (text != "Done") { _startupDoneSince = -1f; return; }
                float now = UnityEngine.Time.unscaledTime;
                if (_startupDoneSince < 0f) { _startupDoneSince = now; return; }
                if (now - _startupDoneSince < 1f) return;
                LoadScreenDone = true; ModsChanged = true; LoadScreenCount++;
                Trace("the first load screen was already finished when the mod started (LoadText Done): hooks go in now");
                // the settings hub waits for the end of the load-screen coroutine, which is already over: give it its signal
                try
                {
                    var m = typeof(Rgreat.Hub.SettingsHub).GetMethod("LoadScreenPostfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    if (m != null) { m.Invoke(null, new object[] { false }); Trace("settings hub: menu entries installed"); }
                    else Trace("settings hub: LoadScreenPostfix not found");
                }
                catch (Exception e2) { Trace("settings hub late install: " + e2.Message); }
            }
            catch (Exception e) { _startupDoneSince = -1f; Trace("startup load screen check: " + e.Message); }
        }

        /// <summary>The line: this mod's name and version.</summary>
        static string ModsLine()
        {
            return "Alternative Game Interface " + PluginVersion;
        }
    }
}
