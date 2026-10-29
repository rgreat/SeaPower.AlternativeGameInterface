using System;
using System.Collections.Generic;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// WindowedMapAndWorldView: the compass tape (heading scale at the top centre of the mission HUD, MainGameView) is moved into the World View window,
    /// over the picture, at its top centre. The game makes it black or white by whether the camera is under water; here by the brightness of the picture
    /// behind it: a few points of the 3D view's texture under the tape are averaged (a small copy of that part, read back asynchronously, about ten times a second),
    /// white on a dark picture, black on a light one (with a hysteresis). With the option off, or when the window is gone, the tape goes back.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        WeakReference _tapeCanvas, _tapeHome, _tapeStyled;      // the tape, its original panel
        int _tapeHomeIndex;
        readonly List<Action> _tapeUndo = new List<Action>();
        float _tapeNext; bool _tapeReady;
        readonly List<TextBlock> _tapeTexts = new List<TextBlock>();
        readonly List<Path> _tapePaths = new List<Path>();
        UnityEngine.RenderTexture _lumRt; float _lumNext; bool _lumBusy; bool _tapeWhite = true, _lumKnown; float _lum;

        void CompassTapeStep(float now)
        {
            // the brightness is followed every frame the tape is in the window (its own pace); the moving of the tape is checked less often
            if (_tapeReady) { var t0 = _tapeCanvas == null ? null : _tapeCanvas.Target as Canvas; if (t0 != null && t0.IsLoaded) TapeBrightnessStep(now, t0); }
            if (now < _tapeNext) return;
            _tapeNext = now + 0.4f;
            try
            {
                bool want = _windowedMap != null && _windowedMap.Value && InMission();
                var tape = _tapeCanvas == null ? null : _tapeCanvas.Target as Canvas;
                if (!want)
                {
                    if (tape != null) TapeHome(tape);
                    else if (_tapeReady || _tapeStyled != null) { _tapeUndo.Clear(); _tapeStyled = null; _tapeReady = false; _tapeCanvas = null; }      // the tape of a finished mission is gone
                    return;
                }
                var ww = _wvWin == null ? null : _wvWin.Target;
                var host = ww == null || !GetB(ww, "IsLoaded") ? null : WvContentGrid(ww);
                if (host == null) return;
                if (tape == null || !tape.IsLoaded)
                {
                    var view = MainGameViewInstance() as FrameworkElement;
                    if (view == null || !view.IsLoaded) return;
                    tape = FindTape(view);
                    if (tape == null) return;
                    _tapeCanvas = new WeakReference(tape);
                }
                if (ReferenceEquals(VisualTreeHelper.GetParent(tape), host)) { _tapeReady = _tapeStyled != null; return; }
                var home = VisualTreeHelper.GetParent(tape) as Panel;
                if (home == null) return;
                if (!IsInWv(tape)) { _tapeHome = new WeakReference(home); _tapeHomeIndex = home.Children.IndexOf(tape); }
                home.Children.Remove(tape);
                tape.HorizontalAlignment = HorizontalAlignment.Center; tape.VerticalAlignment = VerticalAlignment.Top;
                host.Children.Add(tape);
                if (_tapeStyled == null || !ReferenceEquals(_tapeStyled.Target, tape))
                {
                    _tapeUndo.Clear();            // (the old ones belong to a tape of a finished mission)
                    _tapeStyled = new WeakReference(tape);
                    var vm = MainVm();
                    if (vm != null) { tape.DataContext = vm; var t0 = tape; _tapeUndo.Add(() => t0.ClearValue(FrameworkElement.DataContextProperty)); }   // the game's view model, whatever the window's is
                    Restyle(tape);
                    _tapeReady = true;
                    Trace("compass tape: moved into the World View window");
                }
            }
            catch (Exception e) { _tapeNext = now + 5f; Trace("CompassTapeStep: " + e.Message); }
        }

        bool IsInWv(Canvas tape)
        {
            var ww = _wvWin == null ? null : _wvWin.Target as DependencyObject;
            for (var p = VisualTreeHelper.GetParent(tape); p != null; p = VisualTreeHelper.GetParent(p)) if (ReferenceEquals(p, ww)) return true;
            return false;
        }

        // the Grid of the World View window's content (the Pip3DView's parent)
        Panel WvContentGrid(object ww)
        {
            var pip = FindPipView(ww, 0) as DependencyObject;
            return pip == null ? null : VisualTreeHelper.GetParent(pip) as Panel;
        }

        static Canvas FindTape(FrameworkElement view)
        {
            var stack = new Stack<DependencyObject>(); stack.Push(view);
            int budget = 4000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                var cv = e as Canvas;
                if (cv != null && Math.Abs(cv.Width - 480f) < 0.5f && Math.Abs(cv.Height - 64f) < 0.5f && cv.OpacityMask != null && cv.ClipToBounds) return cv;
                for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
            }
            return null;
        }

        /// <summary>The tape goes back to the HUD with its own colours.</summary>
        void TapeHome(Canvas tape)
        {
            var home = _tapeHome == null ? null : _tapeHome.Target as Panel;
            var cur = VisualTreeHelper.GetParent(tape) as Panel;
            if (cur != null && home != null && !ReferenceEquals(cur, home))
            {
                cur.Children.Remove(tape);
                home.Children.Insert(Math.Max(0, Math.Min(_tapeHomeIndex, home.Children.Count)), tape);
                tape.HorizontalAlignment = HorizontalAlignment.Center; tape.VerticalAlignment = VerticalAlignment.Top;
            }
            foreach (var u in _tapeUndo) { try { u(); } catch { } }
            _tapeUndo.Clear(); _tapeStyled = null; _tapeReady = false;
        }

        /// <summary>Remembers the tape's texts and lines; the colour is set by the brightness step.</summary>
        void Restyle(Canvas tape)
        {
            _tapeTexts.Clear(); _tapePaths.Clear();
            var stack = new Stack<UIElement>(); stack.Push(tape);
            while (stack.Count > 0)
            {
                var e = stack.Pop();
                var tb = e as TextBlock; if (tb != null) _tapeTexts.Add(tb);
                var p = e as Path; if (p != null) _tapePaths.Add(p);
                var cv = e as Canvas;
                if (cv != null) for (int k = 0; k < cv.Children.Count; k++) stack.Push(cv.Children[k]);
            }
            _tapeUndo.Add(() =>
            {
                foreach (var t in _tapeTexts) t.ClearValue(TextBlock.ForegroundProperty);
                foreach (var p in _tapePaths) { p.ClearValue(Shape.StrokeProperty); p.ClearValue(Shape.FillProperty); }
                _tapeTexts.Clear(); _tapePaths.Clear();
            });
            _lumKnown = false;
            PaintTape(_tapeWhite);
        }

        void PaintTape(bool white)
        {
            var br = new SolidColorBrush(white ? Color.FromRgb(255, 255, 255) : Color.FromRgb(0, 0, 0));
            foreach (var t in _tapeTexts) t.Foreground = br;
            foreach (var p in _tapePaths) { p.Stroke = br; if (p.Fill != null) p.Fill = br; }
            _tapeWhite = white;
        }

        /// <summary>Brightness of the picture behind the tape: a copy of that part of the texture (32 x 4 points), averaged when it is read back.</summary>
        void TapeBrightnessStep(float now, Canvas tape)
        {
            if (now < _lumNext || _lumBusy || _rtShown == null || !_rtShown.IsCreated() || _wvPx == null || PhotoMode()) return;
            _lumNext = now + 0.1f;
            try
            {
                var a = tape.PointToScreen(new Point(0f, 0f)); var b = tape.PointToScreen(new Point(tape.ActualWidth, 40f * tape.ActualHeight / 64f));
                float x0 = a.X - _wvPx[0], x1 = b.X - _wvPx[0], y0 = a.Y - _wvPx[1], y1 = b.Y - _wvPx[1];
                float w = x1 - x0;
                x0 += w * 0.1f; x1 -= w * 0.1f;                           // the faded ends of the tape are not counted
                float rw = _rtShown.width, rh = _rtShown.height;
                x0 = Math.Max(0f, x0); y0 = Math.Max(0f, y0); x1 = Math.Min(rw, x1); y1 = Math.Min(rh, y1);
                if (x1 - x0 < 4f || y1 - y0 < 2f) return;
                if (_lumRt == null) { _lumRt = new UnityEngine.RenderTexture(32, 4, 0, UnityEngine.RenderTextureFormat.ARGB32); _lumRt.filterMode = UnityEngine.FilterMode.Bilinear; _lumRt.Create(); }
                // the texture's v runs from the bottom
                UnityEngine.Graphics.Blit(_rtShown, _lumRt, new UnityEngine.Vector2((x1 - x0) / rw, (y1 - y0) / rh), new UnityEngine.Vector2(x0 / rw, 1f - y1 / rh));
                if (UnityEngine.SystemInfo.supportsAsyncGPUReadback)
                {
                    _lumBusy = true;
                    UnityEngine.Rendering.AsyncGPUReadback.Request(_lumRt, 0, UnityEngine.TextureFormat.RGBA32, req =>
                    {
                        _lumBusy = false;
                        if (req.hasError) return;
                        var d = req.GetData<byte>();
                        long r = 0, g = 0, bl = 0; int n = d.Length / 4;
                        for (int i = 0; i < n; i++) { r += d[i * 4]; g += d[i * 4 + 1]; bl += d[i * 4 + 2]; }
                        if (n > 0) OnTapeBrightness((0.2126f * r + 0.7152f * g + 0.0722f * bl) / (255f * n));
                    });
                }
                else
                {
                    var prev = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = _lumRt;
                    var t2 = new UnityEngine.Texture2D(32, 4, UnityEngine.TextureFormat.RGBA32, false);
                    t2.ReadPixels(new UnityEngine.Rect(0, 0, 32, 4), 0, 0); UnityEngine.RenderTexture.active = prev;
                    var px = t2.GetPixels32(); float sum = 0f;
                    foreach (var q in px) sum += 0.2126f * q.r + 0.7152f * q.g + 0.0722f * q.b;
                    UnityEngine.Object.Destroy(t2);
                    OnTapeBrightness(sum / (255f * px.Length));
                }
            }
            catch (Exception e) { _lumNext = now + 5f; _lumBusy = false; Trace("TapeBrightnessStep: " + e.Message); }
        }

        // smoothed; white on a dark picture, black on a light one, with a hysteresis so that it does not flicker around the middle
        void OnTapeBrightness(float y)
        {
            _lum = _lumKnown ? _lum * 0.5f + y * 0.5f : y;
            bool first = !_lumKnown; _lumKnown = true;
            bool white = _tapeWhite ? _lum < 0.55f : _lum < 0.42f;
            if (first || white != _tapeWhite) PaintTape(white);
        }
    }
}
