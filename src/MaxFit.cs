using System;
using System.Runtime.CompilerServices;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// A maximized window stretches over its cell of the game's UI, which can be larger than the screen (and the visible body sits
    /// inside the template's resize zone): its edges went past the monitor. While a window is maximized its Margin is adjusted so the
    /// visible body (template root panel minus VisibleInsets) is exactly inside the screen (UnityEngine.Screen); the window's own
    /// Margin comes back on restore. Checked a few times a second for maximized windows only.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        sealed class FitState { public bool On; public Thickness Orig; public float Next; }
        static readonly ConditionalWeakTable<object, FitState> FitStates = new ConditionalWeakTable<object, FitState>();

        static bool InCanvas(FrameworkElement fe)
        {
            for (var p = VisualTreeHelper.GetParent(fe); p != null; p = VisualTreeHelper.GetParent(p))
            {
                if (p is Canvas) return true;
                if (p is Panel && !(p is Canvas)) return false; // the nearest panel decides
            }
            return false;
        }

        void FitMaximized(object w, float now)
        {
            var fe = w as FrameworkElement;
            if (fe == null) return;
            var fs = FitStates.GetOrCreateValue(w);
            if (!GetB(w, "Maximized"))
            {
                if (fs.On) { fe.Margin = fs.Orig; fs.On = false; }
                return;
            }
            if (now < fs.Next) return;
            fs.Next = now + 0.25f;
            if (fe.HorizontalAlignment != HorizontalAlignment.Stretch) return; // the maximize animation still runs
            if (InCanvas(fe))
            {
                // a window in a Canvas (Task Force Builder of the campaign): a stretched window there takes the size of its content
                // (thousands of units wide) - it gets the size of the screen itself
                float ks = ScreenScale(fe);
                if (ks > 0f)
                {
                    float dw, dh; DisplaySize(fe, out dw, out dh);
                    float ww = dw / ks, hh = dh / ks;
                    if (!fs.On) { fs.Orig = fe.Margin; fs.On = true; }
                    if (Math.Abs(fe.Width - ww) > 0.5f || Math.Abs(fe.Height - hh) > 0.5f || !float.IsNaN(fe.MaxWidth) && fe.MaxWidth < ww)
                    {
                        fe.MaxWidth = float.PositiveInfinity; fe.MaxHeight = float.PositiveInfinity;
                        fe.Width = ww; fe.Height = hh;
                        Verbose("Maximized window in a Canvas set to the screen size " + ww.ToString("0") + "x" + hh.ToString("0"));
                    }
                }
                return;
            }
            if (!float.IsNaN(fe.Width)) return;
            var panel = TemplateRootPanel(w);
            if (panel == null || panel.ActualWidth < 2f || VisualTreeHelper.GetParent(panel) == null) return;
            var ins = VisibleInsets(w) ?? new float[4];
            Point a, b;
            try
            {
                a = panel.PointToScreen(new Point(ins[0], ins[1]));
                b = panel.PointToScreen(new Point(panel.ActualWidth - ins[2], panel.ActualHeight - ins[3]));
            }
            catch { return; }
            float sw, sh; DisplaySize(panel, out sw, out sh);
            float k = ScreenScale(panel);
            if (k <= 0f) return;
            // the body must lie in [0, sw] x [0, sh]: overflow per side in UI units (negative: room to give back)
            float l = -a.X / k, t = -a.Y / k, r = (b.X - sw) / k, bt = (b.Y - sh) / k;
            // a host that is not the mission's overlay (Task Force Builder of the campaign...): the measure does not fit, leave it
            if (Math.Max(Math.Max(Math.Abs(l), Math.Abs(t)), Math.Max(Math.Abs(r), Math.Abs(bt))) > 80f) { if (!fs.On) return; }
            if (!fs.On) { fs.Orig = fe.Margin; fs.On = true; }
            var m = fe.Margin;
            float nl = Math.Max(fs.Orig.Left, m.Left + l), nt = Math.Max(fs.Orig.Top, m.Top + t);
            float nr = Math.Max(fs.Orig.Right, m.Right + r), nb = Math.Max(fs.Orig.Bottom, m.Bottom + bt);
            if (Math.Abs(nl - m.Left) < 0.5f && Math.Abs(nt - m.Top) < 0.5f && Math.Abs(nr - m.Right) < 0.5f && Math.Abs(nb - m.Bottom) < 0.5f) return;
            fe.Margin = new Thickness(nl, nt, nr, nb);
            fs.Next = now; // check the result on the next frame
            Verbose("Maximized window fitted to the screen: margin " + nl.ToString("0") + "," + nt.ToString("0") + "," + nr.ToString("0") + "," + nb.ToString("0"));
        }
    }
}
