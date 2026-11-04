using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Windows of other mods drawn with Unity's IMGUI (OnGUI + GUI.Window; Camera Offset) lie over the game's Noesis interface but could not be grabbed over it:
    /// the NoesisViews (several) handle the mouse events in their OnGUI before IMGUI and call Event.Use() on a press that hits a Noesis element.
    /// GUI.depth does not change that (NoesisView has useGUILayout = false; trace: Noesis still first), so the press is delayed instead (NoesisGuiPrefix):
    /// decided at the next event by GUIUtility.hotControl. GUI.depth is still set behind (harmless). Nothing is polled; the Noesis drawing is not touched.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        const int ImguiBehind = 10000;

        void HookImguiOrder(Harmony harmony, Assembly asm)
        {
            try
            {
                Type t = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = a.GetType("NoesisView", false); } catch { }
                    if (t != null) break;
                }
                var m = t == null ? null : t.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) { Trace("NoesisView.OnGUI not found: IMGUI windows of other mods keep the old mouse order"); return; }
                _noesisViewType = t;
                TryPatch(harmony, m, "NoesisGuiPrefix", "NoesisGuiBehind", null);
                Trace("patched NoesisView.OnGUI (IMGUI windows of other mods get the mouse before the interface)");
            }
            catch (Exception e) { Trace("HookImguiOrder: " + e.Message); }
        }

        static Func<int> _hotControl;
        static Func<object> _evCurrent;
        static PropertyInfo _evType;
        static bool _imguiInit;
        static Type _noesisViewType;
        static int _imguiTraces;

        static void ImguiInit()
        {
            _imguiInit = true;
            var gu = Type.GetType("UnityEngine.GUIUtility, UnityEngine.IMGUIModule");
            var ev = Type.GetType("UnityEngine.Event, UnityEngine.IMGUIModule");
            var hc = gu == null ? null : gu.GetProperty("hotControl", BindingFlags.Static | BindingFlags.Public);
            var cur = ev == null ? null : ev.GetProperty("current", BindingFlags.Static | BindingFlags.Public);
            if (hc != null) _hotControl = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), hc.GetGetMethod());
            if (cur != null) { var g = cur.GetGetMethod(); _evCurrent = () => g.Invoke(null, null); }
            _evType = ev == null ? null : ev.GetProperty("type", BindingFlags.Instance | BindingFlags.Public);
            _evCopy = ev == null ? null : ev.GetConstructor(new[] { ev });
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var nv = _noesisViewType;
            _processEvent = nv == null || ev == null ? null : nv.GetMethod("ProcessEvent", inst, null, new[] { ev, typeof(bool), typeof(bool) }, null);
            _enKeyboard = nv == null ? null : nv.GetField("_enableKeyboard", inst);
            _enMouse = nv == null ? null : nv.GetField("_enableMouse", inst);
            _uiViewF = nv == null ? null : nv.GetField("_uiView", inst);
            if (_processEvent == null || _enKeyboard == null || _enMouse == null || _uiViewF == null) { _processEvent = null; Trace("NoesisView.ProcessEvent / fields not found: presses go to the interface first as before"); }
        }

        /// <summary>While an IMGUI control holds the mouse (GUIUtility.hotControl != 0: another mod's window is being dragged) Noesis gets no press / release / drag:
        /// it would use the release over a window of ours and the IMGUI window would never learn that the drag ended (it then follows the mouse or stops at random).
        /// The first presses that reach Noesis are traced with the hot control, so the order of the two can be checked in the trace.</summary>
        // a press waits for the next event: views (several NoesisViews get each event) and the copies of the event, in their order
        static readonly List<KeyValuePair<object, object>> _pendingDown = new List<KeyValuePair<object, object>>();
        static int _pendingFrame = -1;
        static MethodInfo _processEvent;
        static FieldInfo _enKeyboard, _enMouse, _uiViewF;
        static ConstructorInfo _evCopy;

        /// <summary>
        /// Several NoesisViews handle each mouse event before the IMGUI windows of other mods, whatever GUI.depth says (trace: "IMGUI order"), and use a press
        /// that hits a Noesis element, so an IMGUI window lying over the interface could not be grabbed. A press is therefore not given to Noesis at once: a copy
        /// is kept and the event goes on to IMGUI. At the next event (the same frame: Layout / Repaint follow the input events) the press is decided:
        /// an IMGUI control took the mouse (GUIUtility.hotControl != 0: a window title, button, field) -> Noesis does not get it; otherwise every view gets
        /// the copy through its own ProcessEvent, in the original order and stopping at the view that uses it, as the game had it. While an IMGUI control holds the
        /// mouse Noesis gets no release / drag either (a release over a window of ours would never reach the dragged window).
        /// </summary>
        static bool NoesisGuiPrefix(object __instance)
        {
            try
            {
                if (!_imguiInit) ImguiInit();
                if (_evCurrent == null || _evType == null || _hotControl == null || _processEvent == null || _evCopy == null) return true;
                var ev = _evCurrent();
                if (ev == null) return true;
                int t = Convert.ToInt32(_evType.GetValue(ev, null));
                bool sameDown = t == 0 && _pendingFrame == UnityEngine.Time.frameCount && !PendingHas(__instance);
                if (_pendingDown.Count > 0 && !sameDown) ResolveDown();
                if (t == 0)                                                     // MouseDown: keep, decide at the next event
                {
                    if (_uiViewF.GetValue(__instance) == null) return true;
                    _pendingFrame = UnityEngine.Time.frameCount;
                    _pendingDown.Add(new KeyValuePair<object, object>(__instance, _evCopy.Invoke(new[] { ev })));
                    return false;
                }
                if ((t == 1 || t == 3) && _hotControl() != 0) return false;   // MouseUp / MouseDrag of an IMGUI drag
                return true;
            }
            catch (Exception e) { if (_imguiTraces++ < 4) Trace("NoesisGuiPrefix: " + e.Message); _pendingDown.Clear(); return true; }
        }

        static bool PendingHas(object view)
        {
            foreach (var p in _pendingDown) if (ReferenceEquals(p.Key, view)) return true;
            return false;
        }

        static void ResolveDown()
        {
            var list = _pendingDown.ToArray();
            _pendingDown.Clear();
            int hot = _hotControl();
            if (hot != 0)
            {
                if (_imguiTraces++ < 8) Trace("IMGUI window took a press (hot control " + hot + "): not given to the interface");
                return;
            }
            foreach (var p in list)
            {
                if (_uiViewF.GetValue(p.Key) == null) continue;
                _processEvent.Invoke(p.Key, new object[] { p.Value, _enKeyboard.GetValue(p.Key), _enMouse.GetValue(p.Key) });
                if (Convert.ToInt32(_evType.GetValue(p.Value, null)) == 12) break;   // EventType.Used: the later views do not get it (as in the game)
            }
        }

        static Action<int> _guiDepth;
        /// <summary>GUI.depth by reflection (the IMGUI module is not referenced by the mod).</summary>
        static void NoesisGuiBehind()
        {
            try
            {
                if (_guiDepth == null)
                {
                    var g = Type.GetType("UnityEngine.GUI, UnityEngine.IMGUIModule");
                    var pr = g == null ? null : g.GetProperty("depth", BindingFlags.Static | BindingFlags.Public);
                    _guiDepth = pr == null ? null : (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), pr.GetSetMethod());
                }
                if (_guiDepth != null) _guiDepth(ImguiBehind);
            }
            catch { }
        }
    }
}
