using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;

namespace AlternativeGameInterface
{
    /// <summary>
    /// TrackState (1.7.0): the Unit Status window shows only units the player controls; a selected contact of another side (a track,
    /// ContactBaseViewModel) leaves it empty ("no unit"). A "Track State" tab (UnitStatusTabViewModel of kind 7, last, like Reference) with the
    /// content of the contact's Information window (VehicleInfoViewModel: status, confidence, detections, signatures, Reset TMA) is available
    /// while such a contact is selected. When the window has no unit shown (HasUnit false) the mod shows the tab strip itself (HasUnit true,
    /// name "Track N", the Engagement tab unavailable, ours selected); when another mod already did (Alternative Damage Model: HasUnit true
    /// for a contact, its Damage State tab) only our tab is made available, and the user picks it. The view model is the game's own, made
    /// when the tab is shown and disposed when the selection changes; the body is the game's own item templates (VehicleInfo.xaml).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _trackState;

        void BindTrackStateConfig()
        {
            _trackState = Config.Bind("UnitStatus", "TrackState", true,
                "Unit Status window: while a contact of another side (a track, not your own unit) is selected, the window shows its Track State " +
                "(the Information window of the contact: status, confidence, detections, signatures) instead of staying empty.");
        }

        const int TrackTabKind = 7;   // 5 = Reference, 6 = Alternative Weapons Control's Weapons tab

        sealed class TrackTab
        {
            public object Tab;                 // UnitStatusTabViewModel
            public object Vm;                  // VehicleInfoViewModel while shown
            public Noesis.FrameworkElement View;
            public object Vehicle;             // the contact the tab is for (null: not available)
            public bool TookOver;              // this mod set HasUnit / the name, not the game or another mod
            public Noesis.Border Host;
            public bool Failed;
        }
        static readonly ConditionalWeakTable<object, TrackTab> TrackTabs = new ConditionalWeakTable<object, TrackTab>();
        static readonly ConditionalWeakTable<object, object> TrackHostFailed = new ConditionalWeakTable<object, object>();

        const string TrackXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Margin=\"6\">" +
            "<Grid.RowDefinitions><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>" +
            "<ItemsControl ItemsSource=\"{Binding StateComponents}\">" +
            "<ItemsControl.ItemsPanel><ItemsPanelTemplate><DockPanel LastChildFill=\"True\"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>" +
            "<ItemsControl.ItemContainerStyle><Style TargetType=\"{x:Type ContentPresenter}\"><Setter Property=\"DockPanel.Dock\" Value=\"Top\"/></Style></ItemsControl.ItemContainerStyle>" +
            "<ItemsControl.ItemTemplate><DataTemplate><ContentControl Content=\"{Binding}\" Padding=\"5\" HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\"/></DataTemplate></ItemsControl.ItemTemplate>" +
            "</ItemsControl>" +
            "<Button x:Name=\"ResetBtn\" Grid.Row=\"1\" Command=\"{Binding ResetTma}\" HorizontalAlignment=\"Right\" Margin=\"5\" Padding=\"10,3\"/>" +
            "</Grid>";

        /// <summary>The selected contact (a Vehicle of the player's plotting table) when it is of another side than friendly, else null.</summary>
        object SelectedContact()
        {
            try
            {
                var asm = GameAssembly();
                var g = asm == null ? null : asm.GetType("SeaPower.Globals");
                var f = g == null ? null : g.GetField("_mainGameViewModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var mgvm = f == null ? null : f.GetValue(null);
                var so = mgvm == null ? null : Get(mgvm, "SelectedObject");
                var v = so == null ? null : Get(so, "Value");
                if (v == null || v.GetType().Name != "Vehicle") return null;
                var rel = v.GetType().GetMethod("CurrentRelationship", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                var r = rel == null ? null : rel.Invoke(v, null);
                return r != null && r.ToString() == "Friendly" ? null : v;
            }
            catch { return null; }
        }

        string GameText(string key, string fallback)
        {
            try
            {
                var asm = GameAssembly();
                var lh = asm == null ? null : asm.GetType("SeaPower.LanguageResourceHandler");
                var sg = asm == null ? null : asm.GetType("SeaPower.Singleton`1");
                var inst = lh == null || sg == null ? null : sg.MakeGenericType(lh).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var dict = inst == null ? null : Get(inst, "LanguageDictionary") as IDictionary;
                return dict != null && dict.Contains(key) ? dict[key] as string : fallback;
            }
            catch { return fallback; }
        }

        void AddTrackTab(object status)
        {
            TrackTab tt;
            if (TrackTabs.TryGetValue(status, out tt)) return;
            var tabs = Get(status, "Tabs") as IList;
            if (tabs == null) return;
            var tab = Activator.CreateInstance(_ustType, status, Enum.ToObject(_ustKind, TrackTabKind), Loc.T("tab.track"));
            SetV(tab, "IsAvailable", false);
            tabs.Add(tab);
            TrackTabs.Add(status, new TrackTab { Tab = tab });
        }

        /// <summary>After UnitStatusViewModel.SwitchUnit: the tab for a selected contact, or put things back.</summary>
        void TrackSwitch(object status)
        {
            TrackTab tt;
            if (!TrackTabs.TryGetValue(status, out tt)) return;
            bool noUnit = StatusUnit(status) == null;
            object contact = _trackState.Value && noUnit ? SelectedContact() : null;
            var eng = Get(status, "EngagementTab");
            if (contact != null)
            {
                bool changed = !ReferenceEquals(tt.Vehicle, contact);
                if (changed) TrackDrop(tt);
                tt.Vehicle = contact;
                if (!GetB(status, "HasUnit"))
                {
                    tt.TookOver = true;
                    SetV(status, "HasUnit", true);
                    if (eng != null) { SetV(eng, "IsSelected", false); SetV(eng, "IsAvailable", false); }
                    SetV(tt.Tab, "IsAvailable", true);
                    SetV(tt.Tab, "IsSelected", true);
                }
                else
                {
                    SetV(tt.Tab, "IsAvailable", true);
                    if (tt.TookOver && changed) SetV(tt.Tab, "IsSelected", true);
                }
                var nm = Get(contact, "DisplayNameShort") as string;
                if (tt.TookOver && !string.IsNullOrEmpty(nm)) SetV(status, "UnitName", nm);
                return;
            }
            if (tt.Vehicle == null && !GetB(tt.Tab, "IsAvailable")) { tt.TookOver = false; return; }
            bool wasSelected = GetB(tt.Tab, "IsSelected");
            TrackDrop(tt);
            tt.Vehicle = null;
            SetV(tt.Tab, "IsSelected", false);
            SetV(tt.Tab, "IsAvailable", false);
            if (tt.TookOver)
            {
                tt.TookOver = false;
                if (noUnit)
                {
                    // nothing of the player's selected: the window as the game leaves it
                    SetV(status, "HasUnit", false);
                    SetV(status, "UnitName", "");
                    if (eng != null) { SetV(eng, "IsAvailable", true); SetV(eng, "IsSelected", true); }
                }
            }
            else if (wasSelected && _ustSelectFirst != null) Invoke(_ustSelectFirst, status);
        }

        void TrackDrop(TrackTab tt)
        {
            if (tt.Host != null) tt.Host.Child = null;
            tt.View = null;
            var d = tt.Vm as IDisposable;
            tt.Vm = null;
            if (d != null) { try { d.Dispose(); } catch (Exception e) { Trace("Track State dispose: " + e.Message); } }
        }

        void DropTrackContent(object status)
        {
            TrackTab tt;
            if (TrackTabs.TryGetValue(status, out tt)) TrackDrop(tt);
        }

        /// <summary>Each frame for a window on screen: if it is a Unit Status window, show / hide our host of the Track State tab.</summary>
        void TrackStateStep(object w)
        {
            if (!_trackState.Value) return;
            object bad;
            if (TrackHostFailed.TryGetValue(w, out bad)) return;
            WeakReference r;
            var usv = StatusViewByWindow.TryGetValue(w, out r) ? r.Target as Noesis.DependencyObject : null;
            if (usv == null || Noesis.VisualTreeHelper.GetParent(usv) == null) return;
            var status = Get(usv, "DataContext");
            TrackTab tt;
            if (status == null || !TrackTabs.TryGetValue(status, out tt)) return;
            bool selected = GetB(tt.Tab, "IsSelected") && GetB(tt.Tab, "IsAvailable") && tt.Vehicle != null;
            if (tt.Host == null || Noesis.VisualTreeHelper.GetParent(tt.Host) == null)
            {
                if (!selected) return;
                tt.Host = MakeRefHost(usv, status);
                if (tt.Host == null) { TrackHostFailed.Add(w, w); return; }
            }
            if (selected && tt.Vm == null && !tt.Failed)
            {
                try
                {
                    var vmT = GameAssembly().GetType("SeapowerUI.ViewModels.VehicleInfoViewModel");
                    var vm = Activator.CreateInstance(vmT, tt.Vehicle);
                    var content = Noesis.GUI.ParseXaml(TrackXaml) as Noesis.FrameworkElement;
                    if (content == null) throw new InvalidOperationException("the Track State body was not made");
                    var btn = content.FindName("ResetBtn") as Noesis.Button;
                    if (btn != null) btn.Content = GameText("windowsvehicleinforesettma", "Reset TMA");
                    content.DataContext = vm;
                    tt.Vm = vm; tt.View = content;
                    if (tt.TookOver)
                    {
                        var vname = Get(vm, "Name") as string;
                        if (!string.IsNullOrEmpty(vname)) SetV(status, "UnitName", vname);
                    }
                    Trace("Track State: " + Get(vm, "Name") + " shown in Unit Status");
                }
                catch (Exception e)
                {
                    var ex = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    Trace("Track State: " + ex);
                    tt.Failed = true;
                }
            }
            if (selected && tt.View != null)
            {
                if (!ReferenceEquals(tt.Host.Child, tt.View)) tt.Host.Child = tt.View;
                if (tt.Host.Visibility != Noesis.Visibility.Visible) tt.Host.Visibility = Noesis.Visibility.Visible;
            }
            else if (tt.Host.Visibility != Noesis.Visibility.Collapsed) tt.Host.Visibility = Noesis.Visibility.Collapsed;
        }
    }
}
