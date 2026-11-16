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
        ConfigEntry<bool> _trackState, _trackLayout, _trackProgress;

        void BindTrackStateConfig()
        {
            _trackState = Config.Bind("UnitStatus", "TrackState", true,
                "Unit Status window: while a contact of another side (a track, not your own unit) is selected, the window shows its Track State " +
                "(the Information window of the contact: status, confidence, detections, signatures) instead of staying empty.");
            _trackLayout = Config.Bind("UnitStatus", "TrackLayout", true,
                "Track State: the window is recomposed (compact facts, the detectors in a scrolling area of limited height, the candidate table takes the rest). " +
                "Off: the game's own layout.");
            _trackProgress = Config.Bind("UnitStatus", "TrackIdentProgress", true,
                "Track State: a rough progress of the identification of the contact (tens of percent): the observation time of its emitters by your ESM against the time the game needs to " +
                "identify them. When the ESM has done its part but the class is still unknown, the row says not enough data. Hidden when the contact is identified. Shown only in the recomposed layout, and only when some ESM of yours observes the contact.");
        }

        const int TrackTabKind = 107;   // far past the game's values (see RefTabKind); 105 = Reference, 6 = Alternative Weapons Control's Weapons tab

        sealed class TrackTab
        {
            public object Tab;                 // UnitStatusTabViewModel
            public object Vm;                  // VehicleInfoViewModel while shown
            public Noesis.FrameworkElement View;
            public object Vehicle;             // the contact the tab is for (null: not available)
            public bool TookOver;              // this mod set HasUnit / the name, not the game or another mod
            public Noesis.Border Host;
            public bool Failed;
            public int ProgTick; public string ProgText;   // identification progress: when it was read (ms) and what is shown ("" = hidden)
            public Delegate Upd; public object UpdFor; public string LastIni;   // the contact's Updated handler (the Reference tab follows its type)
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

        // The composed body (TrackLayout, 2.3.0): the game's own VehicleInfo window gives every part an equal share (4 equal columns, 3 equal rows: a lot of empty space;
        // "Detected By" grows without a limit and pushes the table away). Here: the facts in one compact block, "Detected By" under its caption in a scrolling area that takes
        // as much as its content needs but not more than a part of the window (DetBox.MaxHeight, set when the size changes), the candidate table gets everything that is left
        // (it scrolls itself), Reset TMA at the bottom. The table keeps the game's own template (generated columns).
        const string TrackXaml2 =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Margin=\"6\">" +
            "<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>" +
            "<Grid Grid.Row=\"0\" DataContext=\"{Binding BaseInfo}\" Margin=\"2,0,2,6\">" +
            "<Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"36\"/><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
            "<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>" +
            "<TextBlock x:Name=\"LblState\" Grid.Row=\"0\" Grid.Column=\"0\" Opacity=\"0.65\" Margin=\"0,1,14,1\"/>" +
            "<TextBlock Grid.Row=\"0\" Grid.Column=\"1\" Text=\"{Binding Status}\" Margin=\"0,1,0,1\"/>" +
            "<TextBlock x:Name=\"LblIdent\" Grid.Row=\"1\" Grid.Column=\"0\" Opacity=\"0.65\" Margin=\"0,1,14,1\"/>" +
            "<TextBlock Grid.Row=\"1\" Grid.Column=\"1\" Text=\"{Binding Identification}\" Margin=\"0,1,0,1\"/>" +
            "<TextBlock x:Name=\"LblProg\" Grid.Row=\"2\" Grid.Column=\"0\" Opacity=\"0.65\" Margin=\"0,1,14,1\" Visibility=\"Collapsed\"/>" +
            "<TextBlock x:Name=\"TxtProg\" Grid.Row=\"2\" Grid.Column=\"1\" Margin=\"0,1,0,1\" Visibility=\"Collapsed\"/>" +
            "<TextBlock x:Name=\"LblLast\" Grid.Row=\"0\" Grid.Column=\"3\" Opacity=\"0.65\" Margin=\"0,1,14,1\"/>" +
            "<TextBlock Grid.Row=\"0\" Grid.Column=\"4\" Text=\"{Binding LastDetected}\" Margin=\"0,1,0,1\"/>" +
            "<TextBlock x:Name=\"LblFirst\" Grid.Row=\"1\" Grid.Column=\"3\" Opacity=\"0.65\" Margin=\"0,1,14,1\"/>" +
            "<TextBlock Grid.Row=\"1\" Grid.Column=\"4\" Text=\"{Binding FirstDetected}\" Margin=\"0,1,0,1\"/>" +
            "</Grid>" +
            "<Border Grid.Row=\"1\" Background=\"{StaticResource Brush.Window.BackgroundDark}\" Padding=\"6,4,6,2\" Margin=\"0,0,0,6\">" +
            "<Grid><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>" +
            "<TextBlock x:Name=\"LblDet\" Opacity=\"0.65\" Margin=\"0,0,0,4\"/>" +
            "<ScrollViewer x:Name=\"DetBox\" Grid.Row=\"1\" VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\" MaxHeight=\"160\">" +
            "<ItemsControl DataContext=\"{Binding BaseInfo}\" ItemsSource=\"{Binding DetectedBy}\">" +
            "<ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel Orientation=\"Horizontal\"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>" +
            "<ItemsControl.ItemTemplate><DataTemplate><Grid Margin=\"0,0,6,6\">" +
            "<Grid.Resources>" +
            "<Style TargetType=\"TextBlock\"><Setter Property=\"Padding\" Value=\"4,2\"/></Style>" +
            "<Style TargetType=\"TextBlock\" x:Key=\"UpperRow\"><Setter Property=\"Padding\" Value=\"4,2\"/><Setter Property=\"Background\" Value=\"{StaticResource Brush.Window.Background}\"/></Style>" +
            "<Style TargetType=\"TextBlock\" x:Key=\"LowerRow\"><Setter Property=\"Padding\" Value=\"4,2\"/><Setter Property=\"Background\" Value=\"{StaticResource Brush.Window.Header}\"/></Style>" +
            "<Style TargetType=\"Border\" x:Key=\"RowBorder\"><Setter Property=\"BorderBrush\" Value=\"{StaticResource Brush.Border.Normal}\"/></Style>" +
            "</Grid.Resources>" +
            "<Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
            "<Border Style=\"{StaticResource RowBorder}\" BorderThickness=\"1,1,0,1\" Background=\"{StaticResource Brush.Window.Background}\"><StackPanel VerticalAlignment=\"Center\">" +
            "<TextBlock Text=\"{Binding UnitName}\"/><TextBlock Text=\"{Binding Provenance}\"><TextBlock.Style><Style TargetType=\"TextBlock\"><Setter Property=\"Padding\" Value=\"4,0,4,2\"/><Style.Triggers>" +
            "<DataTrigger Binding=\"{Binding Provenance}\" Value=\"{x:Null}\"><Setter Property=\"Visibility\" Value=\"Collapsed\"/></DataTrigger></Style.Triggers></Style></TextBlock.Style></TextBlock></StackPanel></Border>" +
            "<StackPanel Grid.Column=\"1\" Orientation=\"Vertical\">" +
            "<Border Style=\"{StaticResource RowBorder}\" BorderThickness=\"1,1,1,1\"><TextBlock Text=\"{Binding LanguageDictionary[windowsvehicleinfosensornameheader], Source={StaticResource LanguageResourceInterface}}\" Style=\"{StaticResource UpperRow}\"/></Border>" +
            "<Border Style=\"{StaticResource RowBorder}\" BorderThickness=\"1,0,1,1\"><TextBlock Text=\"{Binding LanguageDictionary[windowsvehicleinfodetectionageheader], Source={StaticResource LanguageResourceInterface}}\" Style=\"{StaticResource UpperRow}\"/></Border>" +
            "</StackPanel>" +
            "<ItemsControl Grid.Column=\"2\" ItemsSource=\"{Binding Detections}\"><ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation=\"Horizontal\"/></ItemsPanelTemplate></ItemsControl.ItemsPanel>" +
            "<ItemsControl.ItemTemplate><DataTemplate><StackPanel Orientation=\"Vertical\">" +
            "<Border Style=\"{StaticResource RowBorder}\" BorderThickness=\"0,1,1,1\"><TextBlock Text=\"{Binding SensorName}\" Style=\"{StaticResource LowerRow}\"/></Border>" +
            "<Border Style=\"{StaticResource RowBorder}\" BorderThickness=\"0,0,1,1\"><TextBlock Text=\"{Binding DetectionAge}\" Style=\"{StaticResource LowerRow}\"/></Border>" +
            "</StackPanel></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>" +
            "</Grid></DataTemplate></ItemsControl.ItemTemplate></ItemsControl></ScrollViewer></Grid></Border>" +
            "<ContentControl Grid.Row=\"2\" Content=\"{Binding SignatureInfo}\" HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\"/>" +
            "<Button x:Name=\"ResetBtn\" Grid.Row=\"3\" Command=\"{Binding ResetTma}\" HorizontalAlignment=\"Right\" Margin=\"5\" Padding=\"10,3\"/>" +
            "</Grid>";

        /// <summary>A field or a property of any visibility, own or inherited (null when absent).</summary>
        static readonly Dictionary<string, MemberInfo> FieldVCache = new Dictionary<string, MemberInfo>();

        static object FieldV(object o, string name)
        {
            if (o == null) return null;
            var t0 = o.GetType();
            string ck = t0.FullName + "|" + name;
            MemberInfo mi;
            if (!FieldVCache.TryGetValue(ck, out mi))
            {
                for (var ty = t0; ty != null && mi == null; ty = ty.BaseType)
                {
                    const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    mi = ty.GetField(name, bf);
                    if (mi == null) { var pr = ty.GetProperty(name, bf); if (pr != null && pr.GetIndexParameters().Length == 0) mi = pr; }
                }
                FieldVCache[ck] = mi;
            }
            var f = mi as FieldInfo;
            if (f != null) return f.GetValue(o);
            var p = mi as PropertyInfo;
            return p == null ? null : p.GetValue(o, null);
        }

        static readonly string[] TrackUnitLists = { "_taskforceVessels", "_taskforceSubmarines", "_taskforceAircraft", "_taskforceHelicopters", "_taskforceLandUnits" };

        /// <summary>
        /// Identification progress of a contact, 0..1, 2 when the contact has its class (identified); else the best ratio "observation time / identification time" over the emitters
        /// of the contact's real object that ESMs of the player's task force observe (the game's own debug panel shows the same numbers). -1: nothing to show.
        /// </summary>
        float TrackIdentProgress(object vehicle)
        {
            try
            {
                if (FieldV(vehicle, "Class") != null) return 2f;   // identified: nothing to show
                var target = FieldV(vehicle, "Object");
                if (target == null) return -1f;
                var g = GameAssembly().GetType("SeaPower.Globals");
                var gf = g == null ? null : g.GetField("_playerTaskforce", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var tf = gf == null ? null : gf.GetValue(null);
                if (tf == null) return -1f;
                float best = -1f;
                foreach (var listName in TrackUnitLists)
                {
                    var list = FieldV(tf, listName) as IEnumerable;
                    if (list == null) continue;
                    foreach (var unit in list)
                    {
                        var obp = FieldV(unit, "_obp");
                        var esms = obp == null ? null : FieldV(obp, "_esmSystems") as IEnumerable;
                        if (esms == null) continue;
                        foreach (var sys in esms)
                        {
                            var data = FieldV(FieldV(sys, "_esm"), "_esmObservationData") as IDictionary;
                            if (data == null) continue;
                            foreach (DictionaryEntry de in data)
                            {
                                if (!ReferenceEquals(FieldV(de.Key, "_baseObject"), target)) continue;
                                float idt = Convert.ToSingle(FieldV(de.Key, "_identificationTime") ?? 0f);
                                float t = Convert.ToSingle(FieldV(de.Value, "_observationTime") ?? 0f);
                                if (idt > 0f) best = Math.Max(best, Math.Min(1f, Math.Max(0f, t / idt)));
                            }
                        }
                    }
                }
                return best;
            }
            catch { return -1f; }
        }

        /// <summary>Twice a second: the progress row of the recomposed layout (rounded to tens of percent; hidden when there is nothing to show).</summary>
        void TrackProgressStep(TrackTab tt)
        {
            if (tt.View == null || tt.Vehicle == null) return;
            int now = Environment.TickCount;
            if (now - tt.ProgTick < 500 && tt.ProgText != null) return;
            tt.ProgTick = now;
            string text = "";
            if (_trackProgress.Value)
            {
                float pr = TrackIdentProgress(tt.Vehicle);
                if (pr >= 0.95f && pr < 1.5f) text = "!" + Loc.Or("track.identNoData", "not enough data");   // the ESM is done but the class is still not known: say so
                else if (pr >= 0f && pr < 0.95f) text = ((int)Math.Round(pr * 10f) * 10) + " %";
            }
            if (text == tt.ProgText) return;
            tt.ProgText = text;
            var lbl = tt.View.FindName("LblProg") as Noesis.FrameworkElement;
            var val = tt.View.FindName("TxtProg") as Noesis.TextBlock;
            if (val == null) return;
            val.Text = text.Length == 0 ? text : text[0] == '!' ? text.Substring(1) : "~ " + text;
            var vis = text.Length > 0 ? Noesis.Visibility.Visible : Noesis.Visibility.Collapsed;
            val.Visibility = vis;
            if (lbl != null) lbl.Visibility = vis;
        }

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

        void TrackLabel(Noesis.FrameworkElement root, string name, string key, string fallback)
        {
            var tb = root.FindName(name) as Noesis.TextBlock;
            if (tb != null) tb.Text = GameText(key, fallback);
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
            RefTab rt; int at = RefTabs.TryGetValue(status, out rt) ? tabs.IndexOf(rt.Tab) : -1;
            if (at >= 0) tabs.Insert(at, tab); else tabs.Add(tab);   // the Reference tab stays the last one
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
                WatchContact(status, tt);
                if (!GetB(status, "HasUnit"))
                {
                    tt.TookOver = true;
                    SetV(status, "HasUnit", true);
                    if (eng != null) { SetV(eng, "IsSelected", false); SetV(eng, "IsAvailable", false); }
                    SetV(tt.Tab, "IsAvailable", true);
                    SelectOnly(status, tt.Tab);
                }
                else
                {
                    SetV(tt.Tab, "IsAvailable", true);
                    if (tt.TookOver && changed) SelectOnly(status, tt.Tab);
                }
                var nm = Get(contact, "DisplayNameShort") as string;
                if (tt.TookOver && !string.IsNullOrEmpty(nm)) SetV(status, "UnitName", nm);
                return;
            }
            if (tt.Vehicle == null && !GetB(tt.Tab, "IsAvailable")) { tt.TookOver = false; return; }
            bool wasSelected = GetB(tt.Tab, "IsSelected");
            TrackDrop(tt);
            tt.Vehicle = null;
            UnwatchContact(tt);
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

        /// <summary>The tab selected and every other one not (the Reference tab, available for a contact too, may have been the selected one).</summary>
        void SelectOnly(object status, object tab)
        {
            var tabs = Get(status, "Tabs") as IList;
            if (tabs != null) foreach (var o in tabs) if (!ReferenceEquals(o, tab) && GetB(o, "IsSelected")) SetV(o, "IsSelected", false);
            SetV(tab, "IsSelected", true);
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
            if (TrackTabs.TryGetValue(status, out tt)) { TrackDrop(tt); UnwatchContact(tt); }
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
                    Noesis.FrameworkElement content = null;
                    try { if (_trackLayout.Value) content = Noesis.GUI.ParseXaml(TrackXaml2) as Noesis.FrameworkElement; }
                    catch (Exception e2) { Trace("Track State layout: " + e2.Message + " (the game's layout is used)"); }
                    if (content == null) content = Noesis.GUI.ParseXaml(TrackXaml) as Noesis.FrameworkElement;
                    if (content == null) throw new InvalidOperationException("the Track State body was not made");
                    var btn = content.FindName("ResetBtn") as Noesis.Button;
                    if (btn != null) btn.Content = GameText("windowsvehicleinforesettma", "Reset TMA");
                    TrackLabel(content, "LblState", "windowscurrentstatus", "State");
                    TrackLabel(content, "LblIdent", "windowsidentification", "Identification");
                    TrackLabel(content, "LblLast", "windowslastdetected", "Last detection age");
                    TrackLabel(content, "LblFirst", "windowsfirstdetected", "First detection age");
                    TrackLabel(content, "LblDet", "windowsdetectedby", "Detected By");
                    var lblProg = content.FindName("LblProg") as Noesis.TextBlock;
                    if (lblProg != null) lblProg.Text = Loc.Or("track.identProgress", "Identification progress");
                    tt.ProgTick = Environment.TickCount - 10000; tt.ProgText = null;
                    var det = content.FindName("DetBox") as Noesis.FrameworkElement;
                    if (det != null) content.SizeChanged += (o, ev) => { try { det.MaxHeight = Math.Max(64f, ev.NewSize.Height * 0.36f); } catch { } };   // Detected By: its content, up to about a third of the window
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
                TrackProgressStep(tt);
                if (!ReferenceEquals(tt.Host.Child, tt.View)) tt.Host.Child = tt.View;
                if (tt.Host.Visibility != Noesis.Visibility.Visible) tt.Host.Visibility = Noesis.Visibility.Visible;
            }
            else if (tt.Host.Visibility != Noesis.Visibility.Collapsed) tt.Host.Visibility = Noesis.Visibility.Collapsed;
        }
    }
}
