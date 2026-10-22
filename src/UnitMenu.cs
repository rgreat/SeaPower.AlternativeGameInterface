using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// UnitPanelMenu (1.6.0): a right click on the flag or the name of the selected unit in the unit panel at the bottom of the mission opens
    /// the same menu as a right click on the unit's icon / model: ObjectBase.ViewModel.GetContextMenuItems() on the game's context menu canvas
    /// (InputHandler does the same for the selected unit). The elements are found in the MainGameView tree by their bindings:
    /// the flag Image (Source: ObjectNation.Value) and the name TextBlock (Text: ObjectName.Value), once per element.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _unitMenu;
        Type _mgvType, _rpType;
        MouseButtonEventHandler _rightDown;
        static readonly ConditionalWeakTable<object, object> MenuHooked = new ConditionalWeakTable<object, object>();

        void BindUnitMenuConfig()
        {
            _unitMenu = Config.Bind("Mission", "UnitPanelMenu", true,
                "Mission: a right click on the flag or the name of the selected unit in the unit panel at the bottom opens the same menu as a right click on the unit on the map.");
        }

        /// <summary>The MainGameView has just loaded (or is up already when the mod starts): one tunnelling handler on the whole view (the panel's elements are
        /// recreated whenever the template changes with the selection).</summary>
        void HookMainView(FrameworkElement view)
        {
            if (!_unitMenu.Value || view == null) return;
            var asm = GameAssembly();
            if (asm == null) return;
            if (_mgvType == null) _mgvType = asm.GetType("SeapowerUI.MainGameView");
            if (_rpType == null) _rpType = asm.GetType("SeaPower.RenderPosition");
            if (_mgvType == null || _rpType == null) return;
            object x;
            if (MenuHooked.TryGetValue(view, out x)) return;
            MenuHooked.Add(view, new object());
            if (_rightDown == null) _rightDown = OnViewPreviewRightDown;
            view.PreviewMouseRightButtonDown += _rightDown;
        }

        void OnViewPreviewRightDown(object sender, MouseButtonEventArgs e)
        {
            if (!Enabled || !_unitMenu.Value) return;
            try
            {
                DependencyObject o = null;
                var fe = sender as FrameworkElement;
                if (fe != null) { var hit = VisualTreeHelper.HitTest(fe, e.GetPosition(fe)); o = hit.VisualHit; }
                if (o == null) o = e.Source as DependencyObject;
                for (int up = 0; o != null && up < 4; up++, o = VisualTreeHelper.GetParent(o))
                {
                    var img = o as Image; var tb = o as TextBlock;
                    if ((img != null && BindingPath(o, Image.SourceProperty) == "ObjectNation.Value") ||
                        (tb != null && BindingPath(o, TextBlock.TextProperty) == "ObjectName.Value"))
                    {
                        OnUnitPanelRightDown(sender, e);
                        return;
                    }
                }
            }
            catch (Exception ex) { Trace("UnitPanelMenu: " + ex.Message); }
        }

        static string BindingPath(DependencyObject o, DependencyProperty p)
        {
            try
            {
                var x = BindingOperations.GetBindingExpression(o, p);
                var b = x != null ? x.ParentBinding : null;
                return b == null || b.Path == null ? null : b.Path.Path;
            }
            catch { return null; }
        }

        /// <summary>What the unit panel shows: MainGameViewModel.SelectedObject.Value (its SelectedViewModel is made from it) - an own unit's ObjectBase,
        /// or the Vehicle (contact) of the player's plotting table.</summary>
        object PanelSubject()
        {
            var asm = GameAssembly();
            var g = asm == null ? null : asm.GetType("SeaPower.Globals");
            var mgvm = g == null ? null : g.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            var so = mgvm == null ? null : Get(mgvm, "SelectedObject");
            return so == null ? null : Get(so, "Value");
        }

        /// <summary>The menu of the contact's MapUnitViewModel (the one a right click on the contact on the map opens); target: the MapUnitViewModel, its Vehicle or its ObjectBase.</summary>
        object ContactMenu(object target)
        {
            if (target == null) return null;
            if (target.GetType().Name == "MapUnitViewModel") return Invoke0(target, "GetContextMenuItems");
            var asm = GameAssembly();
            var g = asm == null ? null : asm.GetType("SeaPower.Globals");
            var mgvm = g == null ? null : g.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            var map = mgvm == null ? null : Get(mgvm, "Map");
            var known = map == null ? null : Get(map, "MapKnownUnits");
            var all = known == null ? null : Get(known, "AllUnits") as System.Collections.IEnumerable;
            if (all == null) return null;
            foreach (var u in all)
            {
                var unit = Get(u, "Unit");
                if (unit == null) continue;
                if (!ReferenceEquals(unit, target) && !ReferenceEquals(Get(unit, "BaseObject"), target)) continue;
                return Invoke0(u, "GetContextMenuItems");
            }
            return null;
        }

        static object Invoke0(object o, string name)
        {
            var m = o.GetType().GetMethod(name, Type.EmptyTypes);
            return m == null ? null : m.Invoke(o, null);
        }

        void OnUnitPanelRightDown(object sender, MouseButtonEventArgs e)
        {
            if (!Enabled || !_unitMenu.Value) return;
            try
            {
                // what the map does: a contact (Vehicle) -> the menu of its MapUnitViewModel (right click on its marker); an own controllable unit ->
                // ObjectBase.ViewModel's menu (right click on the selected unit); a weapon -> its view model's menu
                var sub = PanelSubject();
                if (sub == null) return;
                object items = null;
                // the panel's subject is a Vehicle (plotting table entry) for anything the player's plotting table knows - own units included
                var obj = sub.GetType().Name == "Vehicle" ? (Get(sub, "BaseObject") ?? sub) : sub;
                var isUnit = Invoke0(obj, "isUnit");
                var vm = Get(obj, "ViewModel");
                if (isUnit is bool && (bool)isUnit && GetB(obj, "AcceptsOrdersFromPlayer") && GetB(obj, "IsPlayerObject"))
                    items = vm == null ? null : Invoke0(vm, "GetContextMenuItems");   // own controllable unit: as a right click on the selected unit
                else if (sub.GetType().Name == "Vehicle" || (isUnit is bool && (bool)isUnit)) items = ContactMenu(sub);   // contact
                else items = vm == null ? null : Invoke0(vm, "GetContextMenuItems");   // weapon
                if (items == null) return;
                var f = _mgvType.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
                var view = f == null ? null : f.GetValue(null);
                var canvas = view == null ? null : _mgvType.GetField("ContextMenuCanvas", BindingFlags.Instance | BindingFlags.Public).GetValue(view) as FrameworkElement;
                var menu = canvas == null ? null : canvas.ContextMenu;
                if (menu == null) return;
                menu.ItemsSource = (System.Collections.IEnumerable)items;
                menu.IsOpen = true;
                e.Handled = true;
            }
            catch (Exception ex) { Trace("UnitPanelMenu: " + ex.Message); }
        }
    }
}
