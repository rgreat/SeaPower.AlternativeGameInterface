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
            lock (_vbWatchers) foreach (var rw in _vbWatchers.Values)
            {
                if (rw.ReplacedLong && !rw.ReplacedLongLogged) { rw.ReplacedLongLogged = true; Trace("multi-monitor V-Sync: display " + (rw.Index + 1) + ": the measured rate stays away from the system's: the measured one is used now (" + (Stopwatch.Frequency / (double)Math.Max(1, rw.Period)).ToString("0.##") + " Hz)"); }
                if (rw.Replaced && !rw.ReplacedLogged) { rw.ReplacedLogged = true; Trace("multi-monitor V-Sync: display " + (rw.Index + 1) + ": measured " + (Stopwatch.Frequency / (double)Math.Max(1, rw.Measured)).ToString("0.##") + " Hz, the system says " + (Stopwatch.Frequency / (double)rw.Expected).ToString("0.##") + " Hz (over 5% apart): the system's is used (always for this monitor)"); }
            }
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
            public long Expected, Measured;                // the system's refresh period of the monitor in ticks (0 = not known), the measured one when it was replaced by it
            public volatile bool Replaced, ReplacedLong; public bool ReplacedLogged, ReplacedLongLogged;
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
                // the system's refresh rate of that very device (the one the watcher waits on, so no matching by size): the period; the measure is the fallback
                double wh = WinRefresh(dev), hz = wh;
                if (i == 0) { try { double uh = UnityEngine.Screen.mainWindowDisplayInfo.refreshRate.value; if (uh >= 20.0 && uh <= 1000.0 && (wh < 20.0 || Math.Abs(uh - wh) <= wh * 0.05 + 1.0)) hz = uh; } catch { } }      // the main monitor: Unity's rate is fractional (74.96)
                if (hz >= 20.0 && hz <= 1000.0) { w.Expected = (long)(Stopwatch.Frequency / hz); w.Period = w.Expected; }
                var o = new D3DKMT_OPENADAPTERFROMGDIDISPLAYNAME { DeviceName = dev };
                int st = -1;
                try { st = D3DKMTOpenAdapterFromGdiDisplayName(ref o); } catch (Exception e) { w.Error = e.GetType().Name; }
                if (st != 0) { if (w.Error == "") w.Error = "open adapter 0x" + st.ToString("X"); lock (_vbWatchers) _vbWatchers[i] = w; Trace("multi-monitor V-Sync: display " + (i + 1) + " " + dev + ": " + w.Error); continue; }
                w.Adapter = o.hAdapter; w.Source = o.VidPnSourceId;
                lock (_vbWatchers) _vbWatchers[i] = w;
                w.Thread = new Thread(() => VbWatch(w)) { IsBackground = true, Name = "AGI vblank " + (i + 1), Priority = ThreadPriority.AboveNormal };
                w.Thread.Start();
                Trace("multi-monitor V-Sync: display " + (i + 1) + " = " + dev + " (adapter " + o.hAdapter + ", source " + o.VidPnSourceId + "), vblank watcher started, " + (w.Expected > 0 ? "the system's refresh rate " + (Stopwatch.Frequency / (double)w.Expected).ToString("0.##") + " Hz" : "no system refresh rate (measured)"));
            }
        }

        static void VbWatch(VbWatcher w)
        {
            var e = new D3DKMT_WAITFORVERTICALBLANKEVENT { hAdapter = w.Adapter, hDevice = 0, VidPnSourceId = w.Source };
            long prev = 0; int fails = 0; var ring = new long[32]; var sorted = new long[32]; int ringN = 0, apart = 0;
            while (!w.Stop)
            {
                int st;
                try { st = D3DKMTWaitForVerticalBlankEvent(ref e); } catch (Exception ex) { w.Error = ex.GetType().Name; w.Ok = false; break; }
                if (st != 0) { if (++fails > 50) { w.Error = "wait 0x" + st.ToString("X"); w.Ok = false; break; } Thread.Sleep(5); continue; }
                fails = 0;
                long now = VbSw.ElapsedTicks;
                if (prev != 0)
                {
                    // the period: the median of the last intervals, then the mean of those within 10% of it. The old estimate took the first interval as it came and
                    // moved to later ones only when they were not over 1.5 times it: a first interval that was too short (the first events come at once after the
                    // wait started: trace 8f315db7, 263 Hz on a 75 Hz monitor) was never corrected - the frames were never held, 107 fps at a limit of 75 Hz.
                    ring[ringN++ % ring.Length] = now - prev;
                    int cnt = Math.Min(ringN, ring.Length);
                    if (cnt >= 8 && (ringN & 7) == 0)                    // every 8th refresh: the sort of 32 values is not needed on every one
                    {
                        for (int k = 0; k < cnt; k++) sorted[k] = ring[k];
                        Array.Sort(sorted, 0, cnt);
                        long med = sorted[cnt / 2], sum = 0; int sn = 0;
                        for (int k = 0; k < cnt; k++) if (Math.Abs(sorted[k] - med) * 10 <= med) { sum += sorted[k]; sn++; }
                        if (sn > 0)
                        {
                            long avg = sum / sn;
                            if (w.Expected > 0)                                  // the system's rate is the period; the measure only tells (trace) when it is over 5% away
                            {
                                w.Measured = avg; w.Replaced = Math.Abs(avg - w.Expected) * 20 > w.Expected;
                                // the system's rate and the measure stay apart for long (the monitor's mode changed while the game runs): the measure is used then
                                if (w.Replaced) { if (++apart >= 40) { w.ReplacedLong = true; w.Expected = 0; Interlocked.Exchange(ref w.Period, avg); } } else apart = 0;
                            }
                            else Interlocked.Exchange(ref w.Period, avg);
                        }
                    }
                }
                prev = now;
                Interlocked.Exchange(ref w.Last, now);
                Interlocked.Increment(ref w.Count);
                if (!w.Ok && Interlocked.Read(ref w.Count) > 2) w.Ok = true;
            }
            try { var c = new D3DKMT_CLOSEADAPTER { hAdapter = w.Adapter }; D3DKMTCloseAdapter(ref c); } catch { }
        }

        /// <summary>The refresh rate Windows reports for a display device (\\.\DISPLAYn), whole Hz; 0 when not known (also the "hardware default" 0 / 1 of the driver).</summary>
        static double WinRefresh(string device)
        {
            try
            {
                var dm = new DEVMODE(); dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(device, -1 /* ENUM_CURRENT_SETTINGS */, ref dm) && dm.dmDisplayFrequency > 1) return dm.dmDisplayFrequency;
            }
            catch { }
            return 0.0;
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
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public uint dmFields;
            public int dmPositionX, dmPositionY; public uint dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE dm);
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
