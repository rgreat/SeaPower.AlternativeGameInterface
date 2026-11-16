using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Configuration;

namespace AlternativeGameInterface
{
    public partial class AlternativeGameInterfacePlugin
    {
        float _fpsNext;
        bool _pacerOn;
        int _gameFps;                       // the limit the game set (its video option), kept while the mod's pacer holds the engine's limiter off
        static readonly Stopwatch PaceSw = Stopwatch.StartNew();
        static long _paceNext;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] static extern uint TimeBeginPeriod(uint ms);

        /// <summary>The frame limit is the game's own option (Target Frame Rate of the video options; the mod raises its slider's top, see OnSliderLoaded). The mod keeps
        /// it precisely (a timed wait instead of the engine's coarse one; only with vertical sync off), or drops it (NoFrameLimit).</summary>
        void FpsLimitStep(float now)
        {
            if (_fpsNone == null) return;
            if (now < _fpsNext) return;
            _fpsNext = now + 1f;
            bool precise = UnityEngine.QualitySettings.vSyncCount == 0;
            int cur = UnityEngine.Application.targetFrameRate;
            if (precise)
            {
                if (cur > 0) { _gameFps = cur; UnityEngine.Application.targetFrameRate = -1; }   // the game (re)set its own limit: taken over
                if (!_pacerOn)
                {
                    _pacerOn = true;
                    try { TimeBeginPeriod(1); } catch { }
                    StartCoroutine(Pacer());
                    Trace("frame limit kept by the mod (precise)");
                }
                return;
            }
            _pacerOn = false;       // the coroutine stops itself; the engine's limiter gets the game's value back
            if (_fpsNone.Value)
            {
                if (cur != -1) { if (cur > 0) _gameFps = cur; UnityEngine.Application.targetFrameRate = -1; Trace("frame limit none"); }
                return;
            }
            if (_gameFps > 0 && cur < 0) { UnityEngine.Application.targetFrameRate = _gameFps; _gameFps = 0; }
        }

        /// <summary>The frame limit the game has set (its video option), also while the mod's pacer holds the engine's limiter off.</summary>
        int GameFps() { return _gameFps > 0 ? _gameFps : UnityEngine.Application.targetFrameRate; }

        /// <summary>The mod is switched off in the game's Mods menu: the game gets its own limit and vertical sync back, nothing of the mod is left running.</summary>
        void ReleaseFrameControl()
        {
            try
            {
                _pacerOn = false;
                if (_gameFps > 0 && UnityEngine.Application.targetFrameRate < 0) UnityEngine.Application.targetFrameRate = _gameFps;
                _gameFps = 0;
                AdaptiveVSyncRelease();
            }
            catch (Exception e) { Trace("ReleaseFrameControl: " + e.Message); }
        }

        IEnumerator Pacer()
        {
            var eof = new UnityEngine.WaitForEndOfFrame();
            long traced = -1;
            while (_pacerOn)
            {
                yield return eof;
                int want = (_fpsNone.Value || _avOff) ? 0 : _gameFps;
                int cur = UnityEngine.Application.targetFrameRate;
                if (cur > 0) { _gameFps = cur; UnityEngine.Application.targetFrameRate = -1; if (!_fpsNone.Value && !_avOff) want = cur; }
                if (traced != want) { traced = want; Trace("frame limit " + (want > 0 ? want.ToString() : "none")); }
                long now = PaceSw.ElapsedTicks;
                if (want <= 0) { _paceNext = now; continue; }
                long step = Stopwatch.Frequency / want;
                if (_paceNext == 0 || now - _paceNext > step * 3) _paceNext = now;      // far behind (a hitch, a load): no catching up in a burst
                _paceNext += step;
                long sleepMargin = Stopwatch.Frequency * 2 / 1000;                     // sleep until two milliseconds are left, spin for the rest
                while (true)
                {
                    long left = _paceNext - PaceSw.ElapsedTicks;
                    if (left <= 0) break;
                    if (left > sleepMargin) System.Threading.Thread.Sleep(1);
                }
            }
        }

        // ---------------------------------------------------------------- Adaptive V-Sync

        // The extra item of the game's V-Sync list (see OnComboLoaded). It has the value of "Every" (1), so the game itself writes 1 to its options and,
        // with the mod off or gone, simply has ordinary vertical sync. That it is the adaptive one is kept here (General/AdaptiveVSync).
        bool _avOff;                        // adaptive mode has switched the vertical sync off for now (the frames do not keep up with the screen)
        bool _avWas;                        // the mode was active in the last check
        float _avWindow, _avSwitched;
        int _avFrames, _avLate, _avOver;
        static MethodInfo _iniReadInt;

        /// <summary>The game's saved V-Sync option (0 off, 1 every, 2 every other): its options.ini in memory.</summary>
        static int GameVsyncOption()
        {
            try
            {
                var asm = GameAssembly();
                var om = asm == null ? null : asm.GetType("SeaPower.OptionsManager");
                if (om == null) return 0;
                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
                var exists = om.GetMethod("InstanceExists", sf, null, new[] { typeof(bool) }, null);
                if (exists == null || !(bool)exists.Invoke(null, new object[] { false })) return 0;
                var inst = om.GetProperty("Instance", sf).GetValue(null, null);
                var ini = inst.GetType().GetProperty("OptionsIni", BindingFlags.Instance | BindingFlags.Public).GetValue(inst, null);
                if (_iniReadInt == null)
                    foreach (var m in ini.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                    {
                        var p = m.GetParameters();
                        if (m.Name == "readValue" && !m.IsGenericMethodDefinition && p.Length == 3 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string) && p[2].ParameterType == typeof(int)) { _iniReadInt = m; break; }
                    }
                return _iniReadInt == null ? 0 : (int)_iniReadInt.Invoke(ini, new object[] { "QualitySettings", "VSync", 0 });
            }
            catch { return 0; }
        }

        /// <summary>Called every frame. Adaptive V-Sync: the screen's vertical sync is on while the frames keep up with it, and off while they do not (a missed
        /// refresh with it on costs half the rate). Back on when the frames are well inside the refresh time again.</summary>
        void AdaptiveVSyncStep(float now, float dt)
        {
            if (_vsyncAdaptive == null) return;
            bool want = _vsyncItem.Value && _vsyncAdaptive.Value && GameVsyncOptionCached(now) == 1;
            if (!want) { if (_avWas) AdaptiveVSyncRelease(); return; }
            if (!_avWas) { _avWas = true; _avOff = false; _avWindow = now; _avSwitched = now; _avFrames = _avLate = _avOver = 0; UnityEngine.QualitySettings.vSyncCount = 1; Trace("adaptive V-Sync on"); }

            double hz = 60.0;
            try { hz = UnityEngine.Screen.currentResolution.refreshRateRatio.value; } catch { }
            if (hz < 24.0) hz = 60.0;
            float interval = (float)(1.0 / hz);
            int lim = GameFps();
            if (!_fpsNone.Value && lim > 0 && lim < hz * 0.98)      // the game's own limit is below the screen's rate: the frames are short by that limit, nothing to adapt
            {
                if (_avOff) { _avOff = false; _avSwitched = now; UnityEngine.QualitySettings.vSyncCount = 1; }
                return;
            }
            _avFrames++;
            if (dt > interval * 1.5f) _avLate++;
            if (dt > interval * 0.85f) _avOver++;
            if (now - _avWindow < 0.5f) return;
            int frames = _avFrames, late = _avLate, over = _avOver;
            _avWindow = now; _avFrames = _avLate = _avOver = 0;
            if (frames < 8) return;
            if (!_avOff)
            {
                if (late * 4 >= frames && now - _avSwitched > 1.5f)
                {
                    _avOff = true; _avSwitched = now; UnityEngine.QualitySettings.vSyncCount = 0;
                    Trace("adaptive V-Sync: frames do not keep up with " + hz.ToString("0") + " Hz (" + late + " of " + frames + " late), vertical sync off");
                }
            }
            else if (over == 0 && now - _avSwitched > 3f)
            {
                _avOff = false; _avSwitched = now; UnityEngine.QualitySettings.vSyncCount = 1;
                Trace("adaptive V-Sync: frames are well inside the refresh time, vertical sync on");
            }
            else if (UnityEngine.QualitySettings.vSyncCount != 0) UnityEngine.QualitySettings.vSyncCount = 0;   // the game's Apply set it again
        }

        float _avIniAt; int _avIni;
        int GameVsyncOptionCached(float now)
        {
            if (now >= _avIniAt) { _avIniAt = now + 1f; _avIni = GameVsyncOption(); }
            return _avIni;
        }

        /// <summary>Back to the game's own setting.</summary>
        void AdaptiveVSyncRelease()
        {
            if (!_avWas) return;
            _avWas = false; _avOff = false;
            UnityEngine.QualitySettings.vSyncCount = GameVsyncOption();
            Trace("adaptive V-Sync off: the game's setting is back");
        }
    }
}
