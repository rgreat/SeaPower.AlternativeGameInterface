using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// ResizeDockedWindows (1.7.0): a window's resize zone (ResizeThumb, ResizeHandleWidth wide) lies around its visible body; a window docked to
    /// an edge of the screen (or to the top of the mission HUD's bottom bar) has that zone off the screen / under the bar, and the mouse cannot take
    /// the edge. While an edge of the visible body is on the screen's edge, the thumbs of that edge reach a few units inside the body instead
    /// (a negative margin on the inner side: the thumbs are drawn after the content, so they are above it).
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _dockReach;
        const float ReachIn = 4f;                 // units the zone reaches into the body at a docked edge
        sealed class ReachState { public List<FrameworkElement> Thumbs; public List<int> Sides; public float Next; public int Mask = -1; }
        static readonly ConditionalWeakTable<object, ReachState> Reach = new ConditionalWeakTable<object, ReachState>();

        void BindDockReachConfig()
        {
            _dockReach = Config.Bind("General", "ResizeDockedWindows", true,
                "A window docked to an edge of the screen (or to the mission HUD's bottom bar) has its invisible resize zone off the screen, and the mouse cannot take that edge. " +
                "On: at such an edge the zone reaches a few pixels inside the window.");
        }

        void DockReachStep(object w, float now)
        {
            if (_dockReach == null || !_dockReach.Value) return;
            var st = Reach.GetOrCreateValue(w);
            if (now < st.Next) return;
            st.Next = now + 0.12f;
            try
            {
                var root = TemplateRootPanel(w);
                if (root == null || root.ActualWidth < 20f || !IsOnScreen(w)) return;
                if (st.Thumbs == null)
                {
                    st.Thumbs = new List<FrameworkElement>(); st.Sides = new List<int>();
                    var stack = new Stack<DependencyObject>(); stack.Push(root);
                    int budget = 600;
                    while (stack.Count > 0 && --budget > 0)
                    {
                        var e = stack.Pop();
                        var fe = e as FrameworkElement;
                        if (fe != null && e.GetType().Name == "ResizeThumb")
                        {
                            object sd = Get(fe, "SidesInt");
                            if (sd is int && (int)sd != 0) { st.Thumbs.Add(fe); st.Sides.Add((int)sd); }
                        }
                        for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) stack.Push(c); }
                    }
                    if (st.Thumbs.Count == 0) st.Next = now + 5f;
                    else Trace("dock reach: " + st.Thumbs.Count + " resize thumbs in '" + TextOf(Get(w, "Header")) + "'");
                }
                if (st.Thumbs.Count == 0) return;
                int mask = 0;
                if (!GetB(w, "Maximized"))
                {
                    var ins = VisibleInsets(w);
                    var a = root.PointToScreen(new Point(ins[0], ins[1]));
                    var b = root.PointToScreen(new Point(root.ActualWidth - ins[2], root.ActualHeight - ins[3]));
                    float sw = UnityEngine.Screen.width, bl = BottomLimit(), tol = 2f;
                    if (b.X > 0f && a.X < sw && b.Y > 0f && a.Y < bl)
                    {
                        if (a.X <= tol) mask |= 2;
                        if (a.Y <= tol) mask |= 1;
                        if (b.X >= sw - tol) mask |= 8;
                        if (b.Y >= bl - tol) mask |= 4;
                    }
                }
                if (mask == st.Mask) return;
                st.Mask = mask;
                float W = GetF(w, "ResizeHandleWidth", 10f);
                if (W < 1f) W = 10f;
                for (int i = 0; i < st.Thumbs.Count; i++)
                {
                    var t = st.Thumbs[i]; int sd = st.Sides[i];
                    bool hx = (sd & 2) != 0 || (sd & 8) != 0, vy = (sd & 1) != 0 || (sd & 4) != 0;
                    float el = (sd & 2) != 0 && (mask & 2) != 0 ? ReachIn : 0f, er = (sd & 8) != 0 && (mask & 8) != 0 ? ReachIn : 0f;
                    float et = (sd & 1) != 0 && (mask & 1) != 0 ? ReachIn : 0f, eb = (sd & 4) != 0 && (mask & 4) != 0 ? ReachIn : 0f;
                    // the thumb's slot grows into the body on the docked side(s): negative margin on the inner side, the size grows with it
                    t.Margin = new Thickness(-er, -eb, -el, -et);
                    if (hx) t.Width = W + el + er;
                    if (vy) t.Height = W + et + eb;
                }
                Trace("dock reach: '" + TextOf(Get(w, "Header")) + "' docked edges " + ((mask & 2) != 0 ? "L" : "") + ((mask & 1) != 0 ? "T" : "") + ((mask & 8) != 0 ? "R" : "") + ((mask & 4) != 0 ? "B" : "") + (mask == 0 ? "-" : ""));
            }
            catch (Exception e) { st.Next = now + 5f; Trace("DockReachStep: " + e.Message); }
        }
    }
}
