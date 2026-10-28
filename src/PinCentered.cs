using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SmartWindows
{
    /// <summary>
    /// Pinned centre (1.7.0): some of the game's windows (News in the main menu) put X and Y in bindings that centre the window on its canvas
    /// (CanvasCenterConverter over the canvas size and the window's own ActualWidth / ActualHeight). Resizing such a window changes its
    /// size, so the binding moves it: it jumps while it is dragged by an edge. For a remembered window the bindings are replaced by the
    /// values they gave, once the window has been laid out.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        static readonly ConditionalWeakTable<object, object> Pinned = new ConditionalWeakTable<object, object>();   // window -> time first seen laid out (boxed float)

        void PinStep(Tracked t, object w, float now)
        {
            if (t.Key == null || !t.WasActive || _winType == null) return;
            object first;
            if (Pinned.TryGetValue(w, out first))
            {
                if (first == null) return;   // done
            }
            if (GetF(w, "ActualWidth", 0f) < 1f || GetB(w, "Maximized")) return;
            if (!Pinned.TryGetValue(w, out first)) { Pinned.Add(w, now); return; }
            if (now - (float)first < 0.8f) return;   // the centring has happened
            Pinned.Remove(w); Pinned.Add(w, null);
            bool bound = false;
            foreach (var name in new[] { "X", "Y" })
            {
                var f = _winType.GetField(name + "Property", BindingFlags.Static | BindingFlags.Public);
                var dp = f == null ? null : f.GetValue(null) as Noesis.DependencyProperty;
                if (dp != null && Noesis.BindingOperations.GetBindingExpression((Noesis.DependencyObject)w, dp) != null) bound = true;
            }
            if (!bound) return;
            float x = GetF(w, "X", 0f), y = GetF(w, "Y", 0f);
            SetF(w, "X", x);
            SetF(w, "Y", y);
            Trace("pinned '" + TextOf(Get(w, "Header")) + "' at " + Fmt(x) + "," + Fmt(y) + " (the game centred it with bindings)");
        }
    }
}
