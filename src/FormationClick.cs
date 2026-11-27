using System;
using System.Reflection;
using System.Windows.Input;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// FormationManagerClick: in the Formation Manager a single click on a unit (or on a formation) works as the double click of the game - the camera goes to the unit
    /// (MainGameViewModel.SelectUnitCommand = RenderPosition.switchToObject, the command the game's double click runs). The game's own double click stays as it is.
    /// FormationManagerDoubleClick (DoubleClickCentersMap): the double click also centres the Tactical Display map on the unit (CenterMapOn, as the Event Log link does).
    /// One tunnelling handler on the whole MainGameView (HookMainView), it does nothing when the click is not on a row of a Formation Manager tree. Not taken: the
    /// expander arrow, the control-mode list of a formation, the drag handle of a row, the scroll bars. The click itself is not handled: the row is selected, a drag starts as always.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _formClick, _formDbl;
        MouseButtonEventHandler _formDown;
        MethodInfo _formHandleGet;
        bool _formHandleLooked;
        int _formClickLogged, _formCentreLogged;

        void BindFormationClickConfig()
        {
            _formClick = BindMoved("Mission", "FormationManagerClick", "FormationManager", "ClickGoesToUnit", false,
                "Formation Manager: one click on a unit or a formation does what the double click does - the camera goes to the unit. Off by default.");
            _formDbl = Config.Bind("FormationManager", "DoubleClickCentersMap", false,
                "Formation Manager: a double click on a unit also centres the Tactical Display map on it (the game's own 'C' key does that for the selected unit). Off by default.");
        }

        void HookFormationClick(FrameworkElement view)
        {
            if (view == null) return;
            if (_formDown == null) _formDown = OnViewPreviewLeftDown;
            view.PreviewMouseLeftButtonDown -= _formDown;                     // never twice on one view
            view.PreviewMouseLeftButtonDown += _formDown;
        }

        void OnViewPreviewLeftDown(object sender, MouseButtonEventArgs e)
        {
            if (!Enabled || _formClick == null) return;
            bool one = e.ClickCount == 1 && _formClick.Value, two = e.ClickCount == 2 && _formDbl.Value;
            if (!one && !two) return;
            try
            {
                DependencyObject o = null;
                var fe = sender as FrameworkElement;
                if (fe != null) { var hit = VisualTreeHelper.HitTest(fe, e.GetPosition(fe)); o = hit.VisualHit; }     // the element under the mouse (the handler tunnels from the whole view)
                if (o == null) o = e.Source as DependencyObject;
                if (o == null) return;
                TreeViewItem item = null; bool inView = false;
                for (var p = o; p != null; p = VisualTreeHelper.GetParent(p))
                {
                    string tn = p.GetType().Name;
                    if (tn == "FormationManagerView") { inView = true; break; }
                    if (item == null)
                    {
                        // parts of a row with a job of their own
                        if (tn == "ToggleButton" || tn == "ComboBox" || tn == "ComboBoxItem" || tn == "ScrollBar" || tn == "Thumb" || tn == "RepeatButton" || tn == "TextBox") return;
                        if (IsReorderHandle(p)) return;
                        if (p is TreeViewItem) item = (TreeViewItem)p;          // the innermost row: a unit inside a formation, not the formation
                    }
                }
                if (!inView || item == null) return;
                var unit = UnitOfRow(item.DataContext);
                if (unit == null) return;
                if (two)
                {
                    if (item.DataContext != null && item.DataContext.GetType().Name == "UnitFormation") return;   // the double click on a formation row opens / closes its branch: the map stays
                    bool ok = CenterMapAsKeyC(unit);                            // the game's own double click (the camera) runs as always
                    if (_formCentreLogged < 5) { _formCentreLogged++; Trace("formation manager double click: the map " + (ok ? "is centred on " : "was NOT centred on ") + TextOf(Get(unit, "Name"))); }
                    return;
                }
                var asm = GameAssembly(); var g = asm == null ? null : asm.GetType("SeaPower.Globals");
                var f = g == null ? null : g.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var mgvm = f == null ? null : f.GetValue(null);
                var cmd = mgvm == null ? null : Get(mgvm, "SelectUnitCommand") as ICommand;
                if (cmd == null || !cmd.CanExecute(unit)) return;
                cmd.Execute(unit);
                if (_formClickLogged < 5) { _formClickLogged++; Trace("formation manager click: the camera goes to " + TextOf(Get(unit, "Name"))); }
            }
            catch (Exception ex) { TraceOnce("formation manager click", ex); }
        }

        /// <summary>What the game's key C (MiniMapCenterToTarget) does: Globals._mainGameViewModel.Map.DisplayMap.Center = the unit's Position.Value. Set at once - no TargetCenter
        /// animation (it does not run while the game is paused).</summary>
        bool CenterMapAsKeyC(object unit)
        {
            try
            {
                var asm = GameAssembly(); var g = asm == null ? null : asm.GetType("SeaPower.Globals");
                var f = g == null ? null : g.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var mgvm = f == null ? null : f.GetValue(null);
                var map = mgvm == null ? null : Get(Get(mgvm, "Map"), "DisplayMap");
                var pos = Get(Get(unit, "Position"), "Value");
                if (map == null || pos == null) return false;
                SetV(map, "Center", pos);
                return true;
            }
            catch (Exception ex) { TraceOnce("formation manager double click", ex); return false; }
        }

        /// <summary>The unit a row stands for: an ObjectBase itself, a Station's unit, the leader of a UnitFormation (what the game's double click selects).</summary>
        object UnitOfRow(object dc)
        {
            if (dc == null) return null;
            string tn = dc.GetType().Name;
            if (tn == "UnitFormation") { var ls = Get(dc, "LeaderStation"); return ls == null ? null : Get(ls, "UnitObject"); }
            if (tn == "Station") return Get(dc, "UnitObject");
            var asm = GameAssembly(); var ob = asm == null ? null : asm.GetType("SeaPower.ObjectBase");
            return ob != null && ob.IsInstanceOfType(dc) ? dc : null;
        }

        /// <summary>FormationManagerRowReorderBehavior.IsReorderHandle: the grip of a row (a drag starts there, it is not a click on the unit).</summary>
        bool IsReorderHandle(DependencyObject p)
        {
            if (!_formHandleLooked)
            {
                _formHandleLooked = true;
                var asm = GameAssembly(); var t = asm == null ? null : asm.GetType("SeapowerUI.FormationManagerRowReorderBehavior");
                _formHandleGet = t == null ? null : t.GetMethod("GetIsReorderHandle", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_formHandleGet == null) Trace("formation manager click: IsReorderHandle not found (a click on the grip of a row counts as a click on the unit)");
            }
            if (_formHandleGet == null) return false;
            try { return (bool)_formHandleGet.Invoke(null, new object[] { p }); } catch { return false; }
        }
    }
}
