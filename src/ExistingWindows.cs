using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// Windows made before the mod hooked the game (1.7.0): the mod learns of a window from Window's constructor / OnInitialized, but the
    /// main menu is built (and its News window opened, MenuSystemViewModel) in the first frames after the load screen - sometimes before
    /// the hooks are in, so News never got its gear, frame or remembered position. After the hooks are in, the windows already on the
    /// game's window host (WindowHostManager.CurrentHost) are found in the tree and handled as if they had just been loaded.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        int _scanLeft = -1;
        float _scanNext;
        static readonly ConditionalWeakTable<object, object> Scanned = new ConditionalWeakTable<object, object>();

        void ScanStep(float now)
        {
            if (_scanLeft < 0) _scanLeft = 30;   // thirty tries, a second apart, from the moment the hooks are in
            if (_scanLeft == 0 || now < _scanNext) return;
            _scanLeft--; _scanNext = now + 1f;
            try
            {
                var asm = GameAssembly();
                if (asm == null || _winType == null || !InitNoesis()) return;
                var mgrT = asm.GetType("SeapowerUI.WindowHostManager");
                var sgT = asm.GetType("SeaPower.Singleton`1");
                if (mgrT == null || sgT == null) return;
                var gt = sgT.MakeGenericType(mgrT);
                var exists = gt.GetMethod("InstanceExists", BindingFlags.Static | BindingFlags.Public);
                if (exists == null || !(bool)exists.Invoke(null, new object[] { false })) return;
                var mgr = gt.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var host = mgr == null ? null : Get(mgr, "CurrentHost");
                var root = host == null ? null : Get(host, "AssociatedObject") as DependencyObject;
                if (root == null) return;
                int found = 0, budget = 20000;
                var stack = new Stack<DependencyObject>();
                stack.Push(root);
                while (stack.Count > 0 && --budget > 0)
                {
                    var e = stack.Pop();
                    if (ReferenceEquals(e.GetType(), _winType))
                    {
                        object seen;
                        if (Scanned.TryGetValue(e, out seen)) continue;
                        Scanned.Add(e, e);
                        if (!IsKnownWindow(e)) { found++; AdoptWindow(e); }
                        continue;   // windows inside windows are none
                    }
                    for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
                    {
                        var c = VisualTreeHelper.GetChild(e, k);
                        if (c != null) stack.Push(c);
                    }
                }
                if (found > 0) Trace("existing windows: " + found + " found on the window host and taken over");
            }
            catch (Exception ex) { Trace("ScanStep: " + ex.Message); }
        }

        bool IsKnownWindow(object w)
        {
            foreach (var r in Created) if (ReferenceEquals(r.Target, w)) return true;
            return false;
        }

        void AdoptWindow(object w)
        {
            Created.Add(new WeakReference(w));   // the Update loop starts tracking it
            Trace("existing window '" + TextOf(Get(w, "Header")) + "' taken over");
            Early(w, true, "existing");
            OnWindowLoaded(w);
        }
    }
}
