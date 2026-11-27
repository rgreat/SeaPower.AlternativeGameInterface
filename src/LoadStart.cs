using System;
using System.Reflection;
using BepInEx.Configuration;

namespace AlternativeGameInterface
{
    /// <summary>
    /// A loaded save: "Pause after loading a save" (default on) and "Hide briefing when loading a save" (default off). The game shows the launch
    /// briefing as always (nothing of its start is changed); when the load screen is over the mod, as a player would, presses the briefing's own Play
    /// button (its PlayCommand: the game closes the briefing, shows the HUD, unpauses) and then pauses with GameTime.Pause(), as the pause key does.
    /// With only the pause option and a briefing up the pause is made right after the player's Play (CancelAction.UnPause postfix); with no briefing
    /// (nothing to show in that save) the mission is paused at once. Event driven: StartTimer (the end of LoadMission) arms it, the load screen's end fires it.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _pauseAfterLoad, _hideBriefLoad;
        static bool _startArmed, _holdAfterPlay;

        void BindLoadStartConfig()
        {
            _pauseAfterLoad = Config.Bind("Mission", "PauseAfterLoad", true,
                "A saved mission is paused as soon as it is loaded (after its briefing if the briefing is shown), as with the pause key.");
            _hideBriefLoad = Config.Bind("Mission", "HideBriefingOnLoad", false,
                "The briefing of a saved mission is closed at once (as if Play was pressed); a mission started from the beginning still shows it.");
        }

        /// <summary>SaveLoadManager.StartTimer after LoadMetadata: a save was loaded and the mission is ready (the briefing is up, if it has content).</summary>
        static void LoadStartArm()
        {
            var a = Active;
            if ((object)a == null || a._pauseAfterLoad == null || (!a._pauseAfterLoad.Value && !a._hideBriefLoad.Value)) return;
            try
            {
                var slm = GameAssembly().GetType("SeaPower.SaveLoadManager");
                var p = slm == null ? null : slm.GetProperty("IsASaveFile", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null) { Trace("save load: SaveLoadManager.IsASaveFile not found, the load options do nothing"); return; }
                if (!(bool)p.GetValue(null, null)) return;
                _startArmed = true;
            }
            catch (Exception e) { Trace("LoadStartArm: " + e.Message); }
        }

        /// <summary>Every frame (a flag check): the load screen is over after a save was loaded.</summary>
        void LoadStartStep()
        {
            if (!_startArmed || LoadScreenRunning) return;
            _startArmed = false;
            try
            {
                bool hide = _hideBriefLoad.Value, pause = _pauseAfterLoad.Value;
                var win = LaunchBriefingWindow();
                if (win != null && hide)
                {
                    _holdAfterPlay = pause;
                    var vm = Get(win, "DataContext");
                    var cmd = vm == null ? null : Get(vm, "PlayCommand");
                    var ex = cmd == null ? null : cmd.GetType().GetMethod("Execute", new[] { typeof(object) });
                    if (ex == null) { _holdAfterPlay = false; Trace("save load: the briefing's PlayCommand not found - the briefing stays"); if (pause) _holdAfterPlay = true; return; }
                    Trace("save load: the briefing is closed with its Play command" + (pause ? ", then the game is paused" : ""));
                    ex.Invoke(cmd, new object[] { null });
                }
                else if (win != null && pause) { _holdAfterPlay = true; Trace("save load: the briefing is shown, the game is paused after its Play"); }
                else if (win == null && pause) { GamePause("no briefing"); }
            }
            catch (Exception e) { Trace("LoadStartStep: " + (e.InnerException ?? e).Message); }
        }

        /// <summary>CancelAction.UnPause postfix (Play, also called by the Play command above): the pause that was asked for.</summary>
        static void LoadStartAfterPlay()
        {
            if (!_holdAfterPlay) return;
            _holdAfterPlay = false;
            var a = Active;
            if ((object)a != null) a.GamePause("after Play");
        }

        void GamePause(string why)
        {
            try
            {
                var gt = GameAssembly().GetType("SeaPower.GameTime");
                var m = gt == null ? null : gt.GetMethod("Pause", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(bool) }, null);
                if (m == null) { Trace("save load: GameTime.Pause not found"); return; }
                m.Invoke(null, new object[] { true });
                Trace("save load: the game is paused (" + why + ")");
            }
            catch (Exception e) { Trace("GamePause: " + (e.InnerException ?? e).Message); }
        }
    }
}
