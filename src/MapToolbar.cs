using System;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// WindowedMapAndWorldView: the map draw tools (MapDrawToolbarView) belong to the Tactical Display window. The game shows them in two places: in the window
    /// (hidden while ShowPip3D) and as an overlay in the main view (bottom left, shown while ShowPip3D, "above the world view window"). With the World View
    /// permanently shown (ShowPip3D true) only the overlay was left: it stayed at the corner of the main display when the map window was moved to another
    /// monitor (where the window's own toolbar was hidden too) and did not follow the window on the main display either.
    /// Here, while the map window is loaded, its own toolbar is shown (a local Visibility over the game's binding) and the overlay is collapsed (the overlay's
    /// inner view, not its Grid, so that the game's binding of the Grid stays); the overlay comes back when the window is closed or the mode is off.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        float _mtNext, _mtFindNext; int _mtFinds; WeakReference _mtOverlay, _mtBar, _mtBarWin; bool _mtHid;

        void MapToolbarStep(float now)
        {
            if (now < _mtNext) return;
            _mtNext = now + 0.5f;
            try
            {
                bool want = MapOn() && InMission();
                var mw = want && _mapWin != null ? _mapWin.Target as FrameworkElement : null;
                bool haveWin = mw != null && mw.IsLoaded;
                if (!want) _mtFinds = 0;                                   // (a new mission: the search is allowed again; three searches at most while the overlay is not there)
                UIElement bar = null;
                if (haveWin)
                {
                    bar = _mtBar == null || _mtBarWin == null || !ReferenceEquals(_mtBarWin.Target, mw) ? null : _mtBar.Target as UIElement;
                    if (bar == null)
                    {
                        bar = FindByTypeName(mw, "MapDrawToolbarView");
                        _mtBar = bar == null ? null : new WeakReference(bar); _mtBarWin = new WeakReference(mw);
                    }
                    if (bar != null && bar.Visibility != Visibility.Visible) bar.Visibility = Visibility.Visible;
                }
                var ov = _mtOverlay == null ? null : _mtOverlay.Target as UIElement;
                if (ov == null && (haveWin || _mtHid) && _mtFinds < 3 && now >= _mtFindNext)
                {
                    _mtFindNext = now + 5f; _mtFinds++;
                    var view = MainGameViewInstance() as FrameworkElement;
                    if (view != null && view.IsLoaded) { ov = FindByTypeName(view, "MapDrawToolbarView", true); _mtOverlay = ov == null ? null : new WeakReference(ov); if (ov != null) _mtFinds = 0; }
                }
                if (ov == null) return;
                if (haveWin && bar != null)
                {
                    if (ov.Visibility != Visibility.Collapsed) { ov.Visibility = Visibility.Collapsed; if (!_mtHid) Trace("map tools: the toolbar is in the Tactical Display window (the overlay in the main view is off)"); _mtHid = true; }
                }
                else if (_mtHid) { ov.ClearValue(UIElement.VisibilityProperty); _mtHid = false; Trace("map tools: the overlay toolbar is back"); }
            }
            catch (Exception e) { _mtNext = now + 5f; Trace("MapToolbarStep: " + e.Message); }
        }

        /// <summary>The first element of that type name under a root (breadth first); with outsideWindows only one that is not inside a Window (the overlay of the main view).</summary>
        static UIElement FindByTypeName(DependencyObject root, string typeName, bool outsideWindows = false)
        {
            var queue = new System.Collections.Generic.Queue<DependencyObject>(); queue.Enqueue(root);
            for (int budget = 60000; queue.Count > 0 && --budget > 0;)
            {
                var e = queue.Dequeue();
                if (e.GetType().Name == typeName)
                {
                    if (!outsideWindows) return e as UIElement;
                    bool inWin = false;
                    for (var p = VisualTreeHelper.GetParent(e); p != null; p = VisualTreeHelper.GetParent(p)) if (p.GetType().Name == "Window") { inWin = true; break; }
                    if (!inWin) return e as UIElement;
                }
                for (int k = 0, n = VisualTreeHelper.GetChildrenCount(e); k < n; k++) { var c = VisualTreeHelper.GetChild(e, k); if (c != null) queue.Enqueue(c); }
            }
            return null;
        }
    }
}
