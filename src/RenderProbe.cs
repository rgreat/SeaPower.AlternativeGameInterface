using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Trace only (Debug + TracePeriodic): what draws where and how often. A "render: setup" line whenever the setup changes (vertical sync,
    /// frame limit, the displays with their refresh rates, the game's monitor / World View options, the CameraManager's pause / picture
    /// state, the enabled cameras with their display, rectangle and texture), and every 5 s a "render: pacing" line: the frames by their length
    /// in refresh intervals of the main display and how many times each camera has rendered in those 5 s (Camera.onPreRender).
    /// Made to find why the 3D picture can update less often than the frame rate says.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        static bool _rpHooked;
        static readonly Dictionary<UnityEngine.Camera, int> _rpRenders = new Dictionary<UnityEngine.Camera, int>();
        float _rpNext, _rpWindow;
        int _rpFrames, _rpB1, _rpB2, _rpB3;
        string _rpSetup;
        static readonly List<UnityEngine.DisplayInfo> _rpLayout = new List<UnityEngine.DisplayInfo>();

        static void RpPreRender(UnityEngine.Camera c)
        {
            if (!Periodic || c == null) return;
            int n; _rpRenders.TryGetValue(c, out n); _rpRenders[c] = n + 1;
        }

        void RenderProbeStep(float now, float dt)
        {
            if (!Periodic) return;
            if (!_rpHooked) { _rpHooked = true; UnityEngine.Camera.onPreRender += RpPreRender; _rpWindow = now; _rpNext = now + 5f; }
            double hz = 60.0;
            try { hz = UnityEngine.Screen.currentResolution.refreshRateRatio.value; } catch { }
            if (hz < 24.0) hz = 60.0;
            float iv = (float)(1.0 / hz);
            _rpFrames++;
            if (dt < iv * 1.5f) _rpB1++; else if (dt < iv * 2.5f) _rpB2++; else _rpB3++;
            if (now < _rpNext) return;
            _rpNext = now + 5f;
            try
            {
                var s = RpSetup(hz);
                if (s != _rpSetup) { _rpSetup = s; Trace("render: setup " + s); }
                var sb = new StringBuilder();
                float span = Math.Max(0.001f, now - _rpWindow);
                sb.Append("render: pacing, last ").Append(span.ToString("0.0")).Append(" s: frames ").Append(_rpFrames)
                  .Append(" (").Append((_rpFrames / span).ToString("0")).Append(" fps), timeScale ").Append(UnityEngine.Time.timeScale.ToString("0.##")).Append(", load screen ").Append(LoadScreenRunning ? "on" : "off").Append(", by refresh intervals of ").Append(hz.ToString("0.#")).Append(" Hz: 1x ")
                  .Append(_rpB1).Append(", 2x ").Append(_rpB2).Append(", 3x+ ").Append(_rpB3).Append("; camera renders:");
                foreach (var kv in _rpRenders)
                {
                    var c = kv.Key;
                    if (c == null) continue;
                    sb.Append(" '").Append(c.name).Append("' ").Append(kv.Value).Append(" (").Append((kv.Value / span).ToString("0")).Append("/s)");
                }
                Trace(sb.ToString());
            }
            catch (Exception e) { TraceOnce("render probe", e); }
            _rpRenders.Clear();
            _rpWindow = now; _rpFrames = _rpB1 = _rpB2 = _rpB3 = 0;
        }

        static string RpSetup(double hz)
        {
            var sb = new StringBuilder();
            sb.Append("vSyncCount ").Append(UnityEngine.QualitySettings.vSyncCount).Append(", targetFrameRate ").Append(UnityEngine.Application.targetFrameRate).Append(", maxQueuedFrames ").Append(UnityEngine.QualitySettings.maxQueuedFrames)
              .Append(", main refresh ").Append(hz.ToString("0.##")).Append(" Hz, ").Append(UnityEngine.Screen.fullScreenMode).Append(", desktop mode ").Append(MapOn() ? "on" : "off");
            try
            {
                _rpLayout.Clear(); UnityEngine.Screen.GetDisplayLayout(_rpLayout);
                sb.Append("; system displays:");
                foreach (var d in _rpLayout) sb.Append(" [").Append(d.name).Append(' ').Append(d.width).Append('x').Append(d.height).Append(' ').Append(d.refreshRate.value.ToString("0.##")).Append(" Hz]");
            }
            catch (Exception e) { sb.Append("; system displays: ").Append(e.GetType().Name); }
            try
            {
                sb.Append("; Unity displays:");
                var ds = UnityEngine.Display.displays;
                for (int i = 0; i < ds.Length; i++) sb.Append(' ').Append(i + 1).Append(ds[i].active ? " active" : " off");
            }
            catch { }
            try
            {
                var om = RpSingleton("SeaPower.OptionsManager");
                if (om != null)
                    sb.Append("; game options: WorldViewMonitor ").Append(RpGet(om, "WorldViewMonitor")).Append(", SecondMonitorEnabled ").Append(RpGet(om, "SecondMonitorEnabled"))
                      .Append(", SecondMonitorIndex ").Append(RpGet(om, "SecondMonitorIndex")).Append(", PipRenderRate ").Append(RpGet(om, "PipRenderRate"));
                var cm = RpSingleton("SeaPower.CameraManager");
                if (cm != null)
                    sb.Append("; CameraManager: PipShown ").Append(RpGet(cm, "PipShown")).Append(", IsPipActive ").Append(RpGet(cm, "IsPipActive"))
                      .Append(", paused ").Append(RpGet(cm, "_renderingPaused")).Append(", skip frame ").Append(RpGet(cm, "_pipSkipFrame"))
                      .Append(", world display ").Append(RpGet(cm, "_worldDisplayIndex"));
            }
            catch { }
            sb.Append("; cameras:");
            foreach (var c in UnityEngine.Camera.allCameras)
            {
                var r = c.rect; var t = c.targetTexture;
                sb.Append(" ['").Append(c.name).Append("' display ").Append(c.targetDisplay + 1).Append(" depth ").Append(c.depth.ToString("0.#"));
                if (r.x != 0f || r.y != 0f || r.width != 1f || r.height != 1f) sb.Append(" rect ").Append(r.x.ToString("0.##")).Append(',').Append(r.y.ToString("0.##")).Append(' ').Append(r.width.ToString("0.##")).Append('x').Append(r.height.ToString("0.##"));
                if (t != null) sb.Append(" -> texture ").Append(t.width).Append('x').Append(t.height);
                sb.Append(']');
            }
            return sb.ToString();
        }

        static readonly Dictionary<string, KeyValuePair<MethodInfo, PropertyInfo>> _rpSingle = new Dictionary<string, KeyValuePair<MethodInfo, PropertyInfo>>();
        static readonly object[] _rpNoLog = { false };
        /// <summary>The game's singleton of that type, only when it exists (its Instance getter would make one).</summary>
        static object RpSingleton(string type)
        {
            KeyValuePair<MethodInfo, PropertyInfo> e;
            if (!_rpSingle.TryGetValue(type, out e))
            {
                var asm = GameAssembly();
                var t = asm == null ? null : asm.GetType(type);
                var sg = asm == null ? null : asm.GetType("SeaPower.Singleton`1");
                var g = t == null || sg == null ? null : sg.MakeGenericType(t);
                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                e = g == null ? default(KeyValuePair<MethodInfo, PropertyInfo>) : new KeyValuePair<MethodInfo, PropertyInfo>(g.GetMethod("InstanceExists", sf, null, new[] { typeof(bool) }, null), g.GetProperty("Instance", sf));
                _rpSingle[type] = e;
            }
            if (e.Key == null || e.Value == null) return null;
            try { return (bool)e.Key.Invoke(null, _rpNoLog) ? e.Value.GetValue(null, null) : null; } catch { return null; }
        }

        static string RpGet(object o, string name)
        {
            try
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var t = o.GetType();
                var p = t.GetProperty(name, f);
                if (p != null) return Convert.ToString(p.GetValue(o, null));
                var fi = t.GetField(name, f);
                return fi == null ? "?" : Convert.ToString(fi.GetValue(o));
            }
            catch { return "!"; }
        }
    }
}
