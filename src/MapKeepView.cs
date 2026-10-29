using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;

namespace AlternativeGameInterface
{
    /// <summary>
    /// KeepMapView (1.7.0): the Tactical Display keeps its scale (ZoomLevel) and centre when the window is resized, so a bigger window shows more of the sea
    /// and a smaller one less. With the option on the zoom follows the size instead: the area that was visible stays visible (the zoom changes by
    /// log2 of the size ratio (the smaller of the width / height ratios when the window shrinks, the bigger when it grows; the centre is not touched), so the map keeps its proportions.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _keepMapView;

        sealed class KeepViewState { public object Map; public float W, H; public float Next; public bool Done; }
        static readonly ConditionalWeakTable<object, KeepViewState> KeepView = new ConditionalWeakTable<object, KeepViewState>();

        void BindKeepMapViewConfig()
        {
            _keepMapView = Config.Bind("General", "KeepMapView", false,
                "When the size of the Tactical Display window changes, the map zoom changes with it so that the area that was visible stays visible (the map keeps its proportions). " +
                "Off: the zoom stays, a bigger window shows more of the map and a smaller one less.");
        }

        void KeepMapViewStep(Tracked t, object w, float now)
        {
            if (_keepMapView == null || !_keepMapView.Value || !t.Settled) return;
            var st = KeepView.GetOrCreateValue(w);
            if (st.Done || now < st.Next) return;
            st.Next = now + 0.05f;
            try
            {
                if (st.Map == null)
                {
                    var dc = Get(w, "DataContext");
                    if (dc == null || dc.GetType().Name != "SceneMapViewModel") { st.Done = true; return; }     // only the Tactical Display
                    st.Map = FindMapBase(w, 0);
                    if (st.Map == null) { st.Next = now + 0.5f; return; }
                }
                float cw = GetF(st.Map, "ActualWidth", 0f), ch = GetF(st.Map, "ActualHeight", 0f);
                if (cw < 8f || ch < 8f) return;
                if (st.W < 8f || st.H < 8f) { st.W = cw; st.H = ch; return; }       // the size the map is first seen with
                if (Math.Abs(cw - st.W) < 0.5f && Math.Abs(ch - st.H) < 0.5f) return;
                double rw = cw / st.W, rh = ch / st.H;
                double k = rw < 1.0 || rh < 1.0 ? Math.Min(rw, rh) : Math.Max(rw, rh);     // smaller: the whole area stays visible; bigger: the picture grows with the window
                st.W = cw; st.H = ch;
                if (k <= 0.0 || Math.Abs(k - 1.0) < 1e-4) return;
                float z = GetF(st.Map, "ZoomLevel", float.NaN), tz = GetF(st.Map, "TargetZoomLevel", float.NaN);
                if (float.IsNaN(z)) return;
                float d = (float)Math.Log(k, 2.0);
                // a zoom animation is running (the wheel): its target moves, otherwise the zoom itself
                bool anim = !float.IsNaN(tz) && Math.Abs(tz - z) > 1e-3f;
                SetF(st.Map, anim ? "TargetZoomLevel" : "ZoomLevel", (anim ? tz : z) + d);
            }
            catch (Exception e) { st.Done = true; Trace("KeepMapView: " + e.Message); }
        }

        object FindMapBase(object e, int depth)
        {
            if (e == null || depth > 60) return null;
            for (var ty = e.GetType(); ty != null; ty = ty.BaseType)
                if (ty.Name == "MapBase") return e;
            for (int k = 0, n = VtChildrenCount(e); k < n; k++)
            {
                var f = FindMapBase(VtChild(e, k), depth + 1);
                if (f != null) return f;
            }
            return null;
        }
    }
}
