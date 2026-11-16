using System.Diagnostics;
using System.Text;

namespace AlternativeGameInterface
{
    /// <summary>
    /// A cheap profiler of the mod's own work, for the question "what does the mod cost per frame". Works only while Debug = true (then every 10 seconds one
    /// line "profile: ..." goes to the trace: the average frame time of the game and, per section, milliseconds per frame spent in the mod's code). The CPU
    /// work of the mod's steps and of its patches on the game's methods is measured; what the mod makes the GPU do (the World View texture, the extra camera,
    /// text effects) is not in the figures: if the frame time is high and the sections add up to little, the cost is in the rendering.
    /// </summary>
    static class Prof
    {
        public static bool On;
        public const int Update = 0, Late = 1, Monitors = 2, Misc = 3, Loop = 4, Step = 5, Open = 6, Fit = 7, Pin = 8, Look = 9, MapMode = 10, KeepMap = 11,
            EvLog = 12, DcFit = 13, Picker = 14, Sono = 15, UStat = 16, RCur = 17, Dock = 18, Upg = 19, LOrd = 20, OsPoll = 21, RefTabs = 22,
            PMouse = 23, PGui = 24, PVeh = 25, PPipR = 26, PRemap = 27, PRects = 28, PBlit = 29, Pre = 30, Fps = 31, Tail = 32, Count = 33;
        static readonly string[] Names =
        {
            "Update (all)", "LateUpdate (World View picture)", "MonitorsStep", "other steps before the windows", "windows loop (all)", "Step", "TrackOpenState", "FitMaximized", "PinStep", "LookStep",
            "MapModeStep", "KeepMapViewStep", "EventLogStep", "DcFitStep", "PickerStep", "SonobuoyStep", "Unit Status steps", "ResizeCursorStep", "DockReachStep", "UpgradeStep",
            "LoadoutOrderStep", "open-state polling", "RefTabsStep", "patch MouseControlState", "patch NoesisView.OnGUI", "patch Vehicle transfer", "patch PIP render", "patch mouse remap", "patch SetRects", "patch PIP blit", "steps before the hook (menu background, load screen)", "limit / scan / dump / snapshot steps", "after the windows (probes, saving)"
        };
        static readonly long[] Ticks = new long[Count];
        static readonly int[] Calls = new int[Count];
        static readonly long[] Max = new long[Count];
        static int _gc0, _gc1, _gc2;
        static int _frames; static float _frameSum, _frameMax, _next;
        static readonly double ToMs = 1000.0 / Stopwatch.Frequency;

        public static long T() { return On ? Stopwatch.GetTimestamp() : 0L; }

        /// <summary>Adds the time since t0 to the section; returns the new "now" (so that the next section can follow).</summary>
        public static long Lap(int id, long t0)
        {
            if (!On || t0 == 0L) return 0L;
            long n = Stopwatch.GetTimestamp();
            long d = n - t0; if (id == Update) _curUpd = d; else if (d > _curTopD) { _curTopD = d; _curTop = id; } Ticks[id] += d; Calls[id]++; if (d > Max[id]) Max[id] = d;
            return n;
        }

        // the stamps of the last three frames (index 0 = the running one): the first Update of the frame, the mod's Update start and end, the last LateUpdate, the end of rendering
        const int SFirst = 0, SUs = 1, SUe = 2, SLast = 3, SEof = 4;
        static readonly long[,] Ring = new long[3, 5];
        public static void MarkFirst() { if (!On) return; for (int k = 0; k < 5; k++) { Ring[2, k] = Ring[1, k]; Ring[1, k] = Ring[0, k]; Ring[0, k] = 0; } Ring[0, SFirst] = Stopwatch.GetTimestamp(); }
        public static void MarkUpdateStart() { if (On) Ring[0, SUs] = Stopwatch.GetTimestamp(); }
        public static void MarkLast() { if (On) Ring[0, SLast] = Stopwatch.GetTimestamp(); }
        public static void MarkLate() { }
        public static void MarkEof() { if (On) Ring[0, SEof] = Stopwatch.GetTimestamp(); }
        static string Parts()
        {
            // the frame that has just ended (1): from its start to the mod's Update, the Update, to the last LateUpdate, to the end of rendering, to the start of the next frame (0)
            long f = Ring[1, SFirst], us = Ring[1, SUs], ue = Ring[1, SUe], la = Ring[1, SLast], eo = Ring[1, SEof], nx = Ring[0, SFirst];
            if (f == 0 || us == 0 || ue == 0 || la == 0 || eo == 0 || nx == 0) return "(no stamps)";
            System.Func<long, long, string> ms = (a, b) => ((b - a) * ToMs).ToString("0");
            return "start of frame -> mod's Update " + ms(f, us) + " ms, Update " + ms(us, ue) + ", -> last LateUpdate " + ms(ue, la) + ", -> end of rendering " + ms(la, eo) + ", -> start of next frame " + ms(eo, nx);
        }

        static long _curUpd, _prevUpd, _curTopD, _prevTopD; static int _curTop, _prevTop, _hitches, _hitchGc;

        public static void Frame(float now, float dt)
        {
            if (!On) { _next = 0f; return; }
            // every long frame on its own line (the averages hide a hitch once a second): when, how long, how much of it was the mod's Update, garbage collections since the last one
            // dt is the length of the PREVIOUS frame, so the previous frame's Update and its longest section are the ones to show
            Ring[0, SUe] = Stopwatch.GetTimestamp();
            if (dt > 0.025f && _hitches < 400)
            {
                _hitches++;
                int g = System.GC.CollectionCount(0);
                AlternativeGameInterface.AlternativeGameInterfacePlugin.TraceProfile("hitch: frame " + (dt * 1000f).ToString("0") + " ms at " + now.ToString("0.00") + " s, mod's Update in it " + (_prevUpd * ToMs).ToString("0.0") + " ms (longest section: " + Names[_prevTop] + " " + (_prevTopD * ToMs).ToString("0.0") + " ms); " + Parts() + "; collections since the last hitch " + (g - _hitchGc));
                _hitchGc = g;
            }
            _prevUpd = _curUpd; _prevTop = _curTop; _prevTopD = _curTopD; _curUpd = 0; _curTop = 0; _curTopD = 0;
            _frames++; _frameSum += dt; if (dt > _frameMax) _frameMax = dt;
            if (_next == 0f) { _next = now + 10f; return; }
            if (now < _next || _frames < 30) return;
            _next = now + 10f;
            int g0 = System.GC.CollectionCount(0), g1 = System.GC.CollectionCount(1), g2 = System.GC.CollectionCount(2);
            var sb = new StringBuilder();
            sb.Append("profile: ").Append(_frames).Append(" frames, frame ").Append((_frameSum / _frames * 1000f).ToString("0.00")).Append(" ms (").Append((_frames / _frameSum).ToString("0")).Append(" fps, worst ").Append((_frameMax * 1000f).ToString("0.0")).Append(" ms); garbage collections in these ").Append(_frames).Append(" frames: gen0 ").Append(g0 - _gc0).Append(", gen1 ").Append(g1 - _gc1).Append(", gen2 ").Append(g2 - _gc2).Append("; mod ms per frame (worst single call in brackets):");
            _gc0 = g0; _gc1 = g1; _gc2 = g2;
            for (int i = 0; i < Count; i++)
            {
                double ms = Ticks[i] * ToMs / _frames;
                if (ms >= 0.003 || Max[i] * ToMs >= 0.5) sb.Append("\n    ").Append(Names[i]).Append(" = ").Append(ms.ToString("0.000")).Append(" (").Append((Calls[i] / (float)_frames).ToString("0.0")).Append(" calls/frame, worst ").Append((Max[i] * ToMs).ToString("0.00")).Append(" ms)");
                Ticks[i] = 0; Calls[i] = 0; Max[i] = 0;
            }
            _frames = 0; _frameSum = 0f; _frameMax = 0f;
            AlternativeGameInterfacePlugin.TraceProfile(sb.ToString());
        }
    }
}
