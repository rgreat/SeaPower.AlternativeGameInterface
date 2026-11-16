using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Text by background (WindowLook, look[2] = 3): the brightness of what the screen shows where a window is - the 3D view, the map, the menu
    /// background, other windows, whatever is there. Up to ten times a second, and only while a window asked for it in the last 3 seconds, a coroutine
    /// (at the end of the frame) has Unity copy the finished screen into a texture (ScreenCapture.CaptureScreenshotIntoRenderTexture, found by
    /// reflection), shrinks it to 64 x 36 points with two blits and reads that back asynchronously (about 9 KB). A window averages the points
    /// under it. Nothing runs while no window uses the option. Every display with a window that uses it is captured: the main one in a coroutine at
    /// the end of the frame, the others (Monitors.cs, one camera each) in Camera.onPostRender of that monitor's camera; a monitor without a capture
    /// (yet) is taken as its flat clear colour. The capture texture is 10 times smaller than the
    /// screen when Unity scales the picture into it (checked once against a full-size capture); otherwise it is screen sized.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        const int SlW = 64, SlH = 36;

        /// <summary>What is known about the picture of one display.</summary>
        sealed class SlDisplay
        {
            public RenderTexture Full, Small, Rt, RtB;
            public readonly float[] Lum = new float[SlW * SlH];    // luminance 0..1, in the order of the readback
            public readonly float[] LumB = new float[SlW * SlH];   // the same from the small capture, while the mode is being found out
            public bool Valid;
            public int Pend;                                        // readbacks in flight
            public int Mode;                                        // 0 = not known yet (both captures are compared), 1 = a capture 10 times smaller than the screen works, 2 = only the full one
            public float Asked, Next;
            public int Index;
        }
        static readonly Dictionary<int, SlDisplay> SlDisplays = new Dictionary<int, SlDisplay>();
        static bool _slRunning, _slTopFirst, _slOrientKnown, _slNoCapture, _slHooked;
        static MethodInfo _slCapture;

        static SlDisplay SlOf(int display)
        {
            SlDisplay d;
            if (!SlDisplays.TryGetValue(display, out d)) { d = new SlDisplay { Index = display }; SlDisplays[display] = d; }
            return d;
        }

        /// <summary>Called by a window with the option on (on the given display): starts / keeps the sampling going.</summary>
        void SceneLumAsk(int display)
        {
            SlOf(display).Asked = Time.unscaledTime;
            if (_slNoCapture) return;
            try
            {
                if (_slCapture == null)
                {
                    var t = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
                    _slCapture = t == null ? null : t.GetMethod("CaptureScreenshotIntoRenderTexture", new[] { typeof(RenderTexture) });
                    if (_slCapture == null || !SystemInfo.supportsAsyncGPUReadback) { _slNoCapture = true; Trace("text by background: screen capture / async readback not available"); return; }
                }
                if (display > 0)
                {
                    if (!_slHooked) { _slHooked = true; Camera.onPostRender += SlPostRender; }
                }
                else if (!_slRunning) { _slRunning = true; StartCoroutine(SceneLumLoop()); }
            }
            catch (Exception e) { _slNoCapture = true; Trace("SceneLumAsk: " + e.Message); }
        }

        static bool SlDue(SlDisplay d, float now) { return now - d.Asked <= 3f && now >= d.Next && d.Pend == 0; }

        // ten times a second with the small capture, four with the full one; a new capture starts only when the last readback is in
        static float SlGap(SlDisplay d) { float g = d.Mode == 1 ? 0.2f : 0.5f; return InMission() ? g : g * 4f; }   // the menus: the picture behind changes slowly, and a full capture costs the GPU a frame

        // the main display: at the end of the frame
        IEnumerator SceneLumLoop()
        {
            var end = new WaitForEndOfFrame();
            var d = SlOf(0);
            while (true)
            {
                yield return end;
                float now = Time.unscaledTime;
                if (now - d.Asked > 10f || !Enabled) break;                   // nobody uses it: the loop ends, the next ask starts it again
                if (!SlDue(d, now)) continue;
                d.Next = now + SlGap(d);
                try { SceneLumCapture(d, Screen.width, Screen.height, 0); }
                catch (Exception e) { d.Next = now + 5f; d.Pend = 0; d.Valid = false; Trace("SceneLumCapture: " + e.Message); }
            }
            _slRunning = false;
        }

        // another display: when the camera of its monitor has finished
        static void SlPostRender(Camera cam)
        {
            if ((object)Active == null || SlDisplays.Count == 0) return;
            try
            {
                int idx = -1;
                for (int i = 0; i < Monitors.Count; i++) if (Monitors[i].Cam == cam) { idx = Monitors[i].Index; break; }
                SlDisplay d;
                if (idx <= 0 || !SlDisplays.TryGetValue(idx, out d)) return;
                float now = Time.unscaledTime;
                if (!SlDue(d, now)) return;
                d.Next = now + SlGap(d);
                var disp = Display.displays[idx];
                try { SceneLumCapture(d, disp.renderingWidth, disp.renderingHeight, idx); }
                catch (Exception e) { d.Next = now + 5f; d.Pend = 0; d.Valid = false; Trace("SceneLumCapture(" + idx + "): " + e.Message); }
            }
            catch { }
        }

        static RenderTexture SlMake(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32); rt.filterMode = FilterMode.Bilinear; rt.Create();
            return rt;
        }

        static void SlDrop(ref RenderTexture rt) { if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; } }

        /// <summary>The screen (or a smaller copy of it) into the 64 x 36 texture 'to', read back into 'dst'.</summary>
        static void SlShrink(SlDisplay d, RenderTexture src, RenderTexture to, float[] dst)
        {
            if (src.width > 256)
            {
                var tmp = RenderTexture.GetTemporary(256, 144, 0, RenderTextureFormat.ARGB32);
                tmp.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, tmp);
                Graphics.Blit(tmp, to);
                RenderTexture.ReleaseTemporary(tmp);
            }
            else Graphics.Blit(src, to);
            d.Pend++;
            UnityEngine.Rendering.AsyncGPUReadback.Request(to, 0, TextureFormat.RGBA32, req =>
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try { SlReadback(d, dst, req); }
                finally { SlLog("readback callback", t0, d); }
            });
        }

        // a long capture / readback goes to the trace (Debug): the cost of the "text by background" in the frame
        static int _slLogged;
        static void SlLog(string what, long t0, SlDisplay d)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms >= 3.0 && _slLogged < 300) { _slLogged++; Trace("text by background: " + what + " took " + ms.ToString("0.0") + " ms (mode " + d.Mode + ")"); }
        }

        static void SlReadback(SlDisplay d, float[] dst, UnityEngine.Rendering.AsyncGPUReadbackRequest req)
        {
            {
                d.Pend--;
                bool ok = !req.hasError;
                if (ok)
                {
                    var data = req.GetData<byte>();
                    int n = Math.Min(SlW * SlH, data.Length / 4);
                    for (int i = 0; i < n; i++) dst[i] = (0.2126f * data[i * 4] + 0.7152f * data[i * 4 + 1] + 0.0722f * data[i * 4 + 2]) / 255f;
                    ok = n == SlW * SlH;
                }
                if (ReferenceEquals(dst, d.Lum)) { d.Valid = ok; if (ok) AutoLookRefresh(d.Index); }   // the texts follow at once, not at the next window step
                if (d.Pend == 0 && d.Mode == 0) SlDecideMode(d, ok);
            }
        }

        // the first captures of a display: the full one and the one 10 times smaller are compared; if the small one shows the same picture, only it is used from then on
        static void SlDecideMode(SlDisplay d, bool ok)
        {
            if (!ok) return;
            float mean = 0f, diff = 0f;
            for (int i = 0; i < SlW * SlH; i++) { mean += d.Lum[i]; diff += Math.Abs(d.Lum[i] - d.LumB[i]); }
            mean /= SlW * SlH; diff /= SlW * SlH;
            float var = 0f;
            for (int i = 0; i < SlW * SlH; i++) { float e = d.Lum[i] - mean; var += e * e; }
            var /= SlW * SlH;
            if (var < 0.0016f) return;   // an even picture says nothing: compared again later
            d.Mode = diff < 0.06f ? 1 : 2;
            Trace("text by background: small capture " + (d.Mode == 1 ? "works" : "differs - the full capture is used") + " (difference " + diff.ToString("0.000") + ")");
            if (d.Mode == 1) SlDrop(ref d.Full); else { SlDrop(ref d.Small); SlDrop(ref d.RtB); }
        }

        static void SceneLumCapture(SlDisplay d, int sw, int sh, int display)
        {
            if (sw < 16 || sh < 16) return;
            long tc = System.Diagnostics.Stopwatch.GetTimestamp();
            try { SceneLumCaptureCore(d, sw, sh, display); }
            finally { SlLog("capture", tc, d); }
        }

        static void SceneLumCaptureCore(SlDisplay d, int sw, int sh, int display)
        {
            int smw = Mathf.Max(16, sw / 10), smh = Mathf.Max(9, sh / 10);
            if (d.Rt == null) d.Rt = SlMake(SlW, SlH);
            if (d.Mode != 1)
            {
                if (d.Full != null && (d.Full.width != sw || d.Full.height != sh)) SlDrop(ref d.Full);
                if (d.Full == null) d.Full = SlMake(sw, sh);
                _slCapture.Invoke(null, new object[] { d.Full });
                SlShrink(d, d.Full, d.Rt, d.Lum);
            }
            if (d.Mode != 2)
            {
                if (d.Small != null && (d.Small.width != smw || d.Small.height != smh)) SlDrop(ref d.Small);
                if (d.Small == null) d.Small = SlMake(smw, smh);
                _slCapture.Invoke(null, new object[] { d.Small });
                if (d.Mode == 1) SlShrink(d, d.Small, d.Rt, d.Lum);
                else { if (d.RtB == null) d.RtB = SlMake(SlW, SlH); SlShrink(d, d.Small, d.RtB, d.LumB); }
            }
            if (!_slOrientKnown)
            {
                // Unity copies the screen as it is in memory: on Direct3D / Metal / Vulkan (UV starts at the top) the first row is the top of the screen
                string o = (Active._slOrient.Value ?? "auto").Trim().ToLowerInvariant();
                _slTopFirst = o == "topfirst" || (o != "bottomfirst" && SystemInfo.graphicsUVStartsAtTop);
                _slOrientKnown = true;
                Trace("text by background: capture " + sw + "x" + sh + " (small " + smw + "x" + smh + "), first row = " + (_slTopFirst ? "top" : "bottom") + " (" + SystemInfo.graphicsDeviceType + "), display " + display);
            }
        }

        /// <summary>The mean brightness of the screen of 'display' under the panel, false when it is not known.</summary>
        static bool SceneLumUnder(Noesis.FrameworkElement panel, int display, out float lum)
        {
            lum = 0f;
            SlDisplay d;
            if (!SlDisplays.TryGetValue(display, out d)) return false;
            if (!d.Valid)
            {
                // a monitor that has no picture yet: its flat clear colour
                if (display > 0)
                    for (int i = 0; i < Monitors.Count; i++)
                        if (Monitors[i].Index == display && Monitors[i].Cam != null) { var c = Monitors[i].Cam.backgroundColor; lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; return true; }
                return false;
            }
            try
            {
                var a = panel.PointToScreen(new Noesis.Point(0f, 0f)); var b = panel.PointToScreen(new Noesis.Point(panel.ActualWidth, panel.ActualHeight));
                float sw = Math.Max(1, Screen.width), sh = Math.Max(1, Screen.height);
                if (display > 0) { var disp = Display.displays[display]; sw = Math.Max(1, disp.renderingWidth); sh = Math.Max(1, disp.renderingHeight); }
                int x0 = Mathf.Clamp((int)(Math.Min(a.X, b.X) / sw * SlW), 0, SlW - 1), x1 = Mathf.Clamp((int)(Math.Max(a.X, b.X) / sw * SlW), 0, SlW - 1);
                int yt = Mathf.Clamp((int)(Math.Min(a.Y, b.Y) / sh * SlH), 0, SlH - 1), yb = Mathf.Clamp((int)(Math.Max(a.Y, b.Y) / sh * SlH), 0, SlH - 1);
                float sum = 0f; int cnt = 0;
                for (int y = yt; y <= yb; y++)
                {
                    int row = _slTopFirst ? y : SlH - 1 - y;
                    for (int x = x0; x <= x1; x++) { sum += d.Lum[row * SlW + x]; cnt++; }
                }
                if (cnt == 0) return false;
                lum = sum / cnt;
                return true;
            }
            catch { return false; }
        }
    }
}
