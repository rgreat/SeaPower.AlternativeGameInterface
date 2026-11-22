using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Adaptive V-Sync with more than one active display. Unity presents every frame to every active display and, with its vertical sync on,
    /// waits for each: a 60 Hz second monitor holds a 75 Hz main one at 60 frames (every 4th frame shown twice there). So while another
    /// display is active Unity's vertical sync is off and the mod paces the frames by the refreshes of the fastest active monitor itself: at
    /// the end of a frame (WaitForEndOfFrame, just before the frame goes to the screens) it waits for that monitor's next refresh
    /// (D3DKMTWaitForVerticalBlankEvent in a thread per display); the other monitors get the frames without vertical sync (they may tear).
    /// Adaptive as with one display: when the frames do not keep up (a quarter of them missed a refresh) the waiting stops; back when they are
    /// well inside the refresh time again. The game's own frame limit below the monitor's rate: no waiting, the limit paces the frames.
    /// History: a hook on the DXGI Present (vertical sync per monitor past Unity) hung the game (managed code on Unity's render thread) and
    /// still did not give the full rate - dropped.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        static readonly Stopwatch VbSw = Stopwatch.StartNew();
        bool _mvOn, _mvPace, _mvRunning;
        VbWatcher _mvW;
        long _mvUsed;
        float _mvWindow, _mvSwitched, _mvPickAt;
        int _mvFrames, _mvLastPick = -1; float _mvDtSum;

        static int ActiveDisplays() { int n = 0; try { foreach (var d in UnityEngine.Display.displays) if (d.active) n++; } catch { n = 1; } return Math.Max(1, n); }

        /// <summary>Adaptive V-Sync is wanted and more than one display is active: this mode takes the frame (from AdaptiveVSyncStep).</summary>
        void MultiVSyncStep(float now, float dt)
        {
            if (!_mvOn)
            {
                _mvOn = true; _mvPace = true; _mvWindow = now; _mvSwitched = now; _mvFrames = 0; _mvUsed = 0; _mvPickAt = 0f;
                Trace("multi-monitor V-Sync on: " + ActiveDisplays() + " active displays - Unity's vertical sync off, frames paced by the fastest monitor");
                if (!_mvRunning) { _mvRunning = true; try { TimeBeginPeriod(1); } catch { } StartCoroutine(MvPacer()); }
            }
            if (UnityEngine.QualitySettings.vSyncCount != 0) UnityEngine.QualitySettings.vSyncCount = 0;     // also after the game's Apply
            VbStartWatchers();
            if (now >= _mvPickAt) { _mvPickAt = now + 2f; MvPick(); }
            var w = _mvW;
            if (w == null || !w.Ok || w.Period <= 0) return;
            float interval = (float)((double)w.Period / Stopwatch.Frequency);
            double hz = 1.0 / interval;
            int lim = GameFps();
            if (!_fpsNone.Value && lim > 0 && lim < hz * 0.98) { _mvPace = false; return; }     // the game's limit paces the frames (the mod's precise limiter)
            _mvFrames++; _mvDtSum += dt;
            if (now - _mvWindow < (_mvPace ? 2f : 0.5f)) return;
            int frames = _mvFrames; float mean = _mvDtSum / Math.Max(1, frames);
            _mvWindow = now; _mvFrames = 0; _mvDtSum = 0f;
            if (frames < 8) return;
            if (_mvPace)
            {
                if (mean > interval * 1.2f && now - _mvSwitched > 1.5f)
                {
                    _mvPace = false; _mvSwitched = now;
                    Trace("multi-monitor V-Sync: frames do not keep up with " + hz.ToString("0") + " Hz (average " + (mean * 1000f).ToString("0.0") + " ms), not synced");
                }
            }
            else if (AdaptiveFits(mean, interval))
            {
                _mvPace = true; _mvSwitched = now; _mvUsed = 0;
                Trace("multi-monitor V-Sync: frames are well inside the refresh time, synced again");
            }
        }

        /// <summary>The monitor that sets the pace: [General] AdaptiveVSyncMonitor when set and active, else the fastest active one (the shortest measured refresh period).</summary>
        void MvPick()
        {
            VbWatcher best = null;
            int chosen; if (_vsyncMonitor == null || !int.TryParse((_vsyncMonitor.Value ?? "").Trim(), out chosen)) chosen = 0;
            if (chosen > 0) { var c = VbWatcher_(chosen - 1); if (c != null && c.Ok && c.Period > 0) best = c; }     // the display set in the options, when it is active
            if (best == null)
                lock (_vbWatchers) foreach (var w in _vbWatchers.Values) if (w.Ok && w.Period > 0 && (best == null || w.Period < best.Period)) best = w;
            if (best == null) return;
            _mvW = best;
            if (best.Index != _mvLastPick)
            {
                _mvLastPick = best.Index; _mvUsed = 0;
                Trace("multi-monitor V-Sync: paced by display " + (best.Index + 1) + " " + best.Device + " (" + (Stopwatch.Frequency / (double)best.Period).ToString("0.##") + " Hz)");
            }
        }

        /// <summary>"2: ASUS VS229 · 1920x1080 · 60 Hz" for Unity display i (0-based): the system display of the same size (the game window's
        /// display for the first one); just the number and size when none matches.</summary>
        internal static string DisplayLabel(int i)
        {
            string s = (i + 1).ToString();
            try
            {
                var d = UnityEngine.Display.displays[i];
                int w = d.systemWidth, h = d.systemHeight;
                var list = new List<UnityEngine.DisplayInfo>();
                UnityEngine.Screen.GetDisplayLayout(list);
                UnityEngine.DisplayInfo found = default(UnityEngine.DisplayInfo); bool ok = false;
                if (i == 0) { found = UnityEngine.Screen.mainWindowDisplayInfo; ok = found.width > 0; }
                if (!ok)
                {
                    var main = UnityEngine.Screen.mainWindowDisplayInfo;
                    foreach (var di in list) if (di.width == w && di.height == h && !(i > 0 && di.name == main.name && di.width == main.width)) { found = di; ok = true; break; }
                }
                if (ok) return s + ": " + found.name + " · " + found.width + "x" + found.height + " · " + found.refreshRate.value.ToString("0.#") + " Hz";
                return s + ": " + w + "x" + h;
            }
            catch { return s; }
        }

        /// <summary>Back to one display (or Adaptive V-Sync off): the watchers stop, the usual Adaptive V-Sync takes over again.</summary>
        void MultiVSyncOff()
        {
            if (!_mvOn) return;
            _mvOn = false; _mvPace = false; _mvW = null; _mvLastPick = -1;
            VbStopWatchers();
            _avWas = false; _avOff = false;     // the usual mode starts afresh (Unity's vertical sync on)
            Trace("multi-monitor V-Sync off");
        }

        IEnumerator MvPacer()
        {
            var eof = new UnityEngine.WaitForEndOfFrame();
            while (_mvOn)
            {
                yield return eof;
                try { MvWait(); } catch (Exception e) { TraceOnce("multi-monitor V-Sync pacer", e); }
            }
            _mvRunning = false;
        }

        /// <summary>The frame goes after the next refresh of the pacing monitor after the one the last frame went after; a missed refresh: at once.</summary>
        void MvWait()
        {
            var w = _mvW;
            if (!_mvPace || w == null || !w.Ok) return;
            long n = Interlocked.Read(ref w.Count);
            if (_mvUsed == 0 || n > _mvUsed + 1) { _mvUsed = n; return; }
            long t0 = VbSw.ElapsedTicks, period = w.Period;
            long deadline = t0 + Math.Max(period * 3, Stopwatch.Frequency / 20);
            long margin = Stopwatch.Frequency * 2 / 1000;
            while (Interlocked.Read(ref w.Count) <= _mvUsed)
            {
                long now = VbSw.ElapsedTicks;
                if (now > deadline) break;
                long next = Interlocked.Read(ref w.Last) + period;
                if (period > 0 && next - now > margin) Thread.Sleep(1); else Thread.SpinWait(50);
            }
            _mvUsed = Interlocked.Read(ref w.Count);
        }

        // ------------------------------------------------------------ vertical blank watchers (a thread per active display)

        sealed class VbWatcher
        {
            public int Index; public string Device = "?", Error = "";
            public volatile bool Ok, Stop;
            public long Count, Last, Period, TraceCount;
            public uint Adapter, Source;
            public Thread Thread;
        }

        readonly Dictionary<int, VbWatcher> _vbWatchers = new Dictionary<int, VbWatcher>();

        VbWatcher VbWatcher_(int index) { VbWatcher w; lock (_vbWatchers) _vbWatchers.TryGetValue(index, out w); return w; }

        void VbStartWatchers()
        {
            UnityEngine.Display[] ds;
            try { ds = UnityEngine.Display.displays; } catch { return; }
            Dictionary<int, string> names = null;
            for (int i = 0; i < ds.Length; i++)
            {
                if (!ds[i].active || VbWatcher_(i) != null) continue;
                if (names == null) names = VbDeviceNames(ds);
                var w = new VbWatcher { Index = i };
                string dev;
                if (names == null || !names.TryGetValue(i, out dev)) { w.Error = "no Windows display matched"; lock (_vbWatchers) _vbWatchers[i] = w; Trace("multi-monitor V-Sync: display " + (i + 1) + ": no Windows display matched"); continue; }
                w.Device = dev;
                var o = new D3DKMT_OPENADAPTERFROMGDIDISPLAYNAME { DeviceName = dev };
                int st = -1;
                try { st = D3DKMTOpenAdapterFromGdiDisplayName(ref o); } catch (Exception e) { w.Error = e.GetType().Name; }
                if (st != 0) { if (w.Error == "") w.Error = "open adapter 0x" + st.ToString("X"); lock (_vbWatchers) _vbWatchers[i] = w; Trace("multi-monitor V-Sync: display " + (i + 1) + " " + dev + ": " + w.Error); continue; }
                w.Adapter = o.hAdapter; w.Source = o.VidPnSourceId;
                lock (_vbWatchers) _vbWatchers[i] = w;
                w.Thread = new Thread(() => VbWatch(w)) { IsBackground = true, Name = "AGI vblank " + (i + 1), Priority = ThreadPriority.AboveNormal };
                w.Thread.Start();
                Trace("multi-monitor V-Sync: display " + (i + 1) + " = " + dev + " (adapter " + o.hAdapter + ", source " + o.VidPnSourceId + "), vblank watcher started");
            }
        }

        static void VbWatch(VbWatcher w)
        {
            var e = new D3DKMT_WAITFORVERTICALBLANKEVENT { hAdapter = w.Adapter, hDevice = 0, VidPnSourceId = w.Source };
            long prev = 0; int fails = 0;
            while (!w.Stop)
            {
                int st;
                try { st = D3DKMTWaitForVerticalBlankEvent(ref e); } catch (Exception ex) { w.Error = ex.GetType().Name; w.Ok = false; break; }
                if (st != 0) { if (++fails > 50) { w.Error = "wait 0x" + st.ToString("X"); w.Ok = false; break; } Thread.Sleep(5); continue; }
                fails = 0;
                long now = VbSw.ElapsedTicks;
                if (prev != 0)
                {
                    long p = now - prev;
                    long old = Interlocked.Read(ref w.Period);
                    // the period, smoothed; a missed event (twice the period) does not count
                    if (old == 0) Interlocked.Exchange(ref w.Period, p);
                    else if (p < old * 3 / 2) Interlocked.Exchange(ref w.Period, old + (p - old) / 16);
                }
                prev = now;
                Interlocked.Exchange(ref w.Last, now);
                Interlocked.Increment(ref w.Count);
                if (!w.Ok && Interlocked.Read(ref w.Count) > 2) w.Ok = true;
            }
            try { var c = new D3DKMT_CLOSEADAPTER { hAdapter = w.Adapter }; D3DKMTCloseAdapter(ref c); } catch { }
        }

        void VbStopWatchers()
        {
            lock (_vbWatchers)
            {
                foreach (var w in _vbWatchers.Values) w.Stop = true;
                _vbWatchers.Clear();
            }
            _mvUsed = 0;
        }

        /// <summary>Unity display index -> Windows display device (\\.\DISPLAYn): display 1 is the monitor of the game window; the others by their
        /// pixel size, then in the order Windows lists them.</summary>
        Dictionary<int, string> VbDeviceNames(UnityEngine.Display[] ds)
        {
            var mons = new List<KeyValuePair<string, RECT>>();
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr data) =>
                {
                    var mi = new MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                    if (GetMonitorInfo(h, ref mi)) mons.Add(new KeyValuePair<string, RECT>(mi.szDevice, mi.rcMonitor));
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception e) { Trace("multi-monitor V-Sync: monitors not listed: " + e.Message); return null; }
            var res = new Dictionary<int, string>();
            var used = new HashSet<string>();
            try
            {
                var hm = MonitorFromWindow(GetActiveWindow(), 2 /* MONITOR_DEFAULTTONEAREST */);
                var mi = new MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (hm != IntPtr.Zero && GetMonitorInfo(hm, ref mi)) { res[0] = mi.szDevice; used.Add(mi.szDevice); }
            }
            catch { }
            var sb = new System.Text.StringBuilder("multi-monitor V-Sync: Windows monitors:");
            foreach (var m in mons) sb.Append(' ').Append(m.Key).Append(' ').Append(m.Value.Right - m.Value.Left).Append('x').Append(m.Value.Bottom - m.Value.Top).Append(" at ").Append(m.Value.Left).Append(',').Append(m.Value.Top);
            Trace(sb.ToString());
            for (int i = 0; i < ds.Length; i++)
            {
                if (res.ContainsKey(i)) continue;
                string best = null;
                foreach (var m in mons)
                    if (!used.Contains(m.Key) && m.Value.Right - m.Value.Left == ds[i].systemWidth && m.Value.Bottom - m.Value.Top == ds[i].systemHeight) { best = m.Key; break; }
                if (best == null) foreach (var m in mons) if (!used.Contains(m.Key)) { best = m.Key; break; }
                if (best != null) { res[i] = best; used.Add(best); }
            }
            return res;
        }

        // ------------------------------------------------------------ Win32

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct D3DKMT_OPENADAPTERFROMGDIDISPLAYNAME
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public uint hAdapter;
            public uint LuidLow; public int LuidHigh;
            public uint VidPnSourceId;
        }
        [StructLayout(LayoutKind.Sequential)] struct D3DKMT_WAITFORVERTICALBLANKEVENT { public uint hAdapter, hDevice, VidPnSourceId; }
        [StructLayout(LayoutKind.Sequential)] struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("gdi32.dll")] static extern int D3DKMTOpenAdapterFromGdiDisplayName(ref D3DKMT_OPENADAPTERFROMGDIDISPLAYNAME p);
        [DllImport("gdi32.dll")] static extern int D3DKMTWaitForVerticalBlankEvent(ref D3DKMT_WAITFORVERTICALBLANKEVENT p);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER p);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX mi);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    }
}
