using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// UpgradeShip (1.7.0): the campaign Task Force Builder gets an "Upgrade ship..." button under "Unit Reference". For the ship selected in the
    /// roster it offers a menu of the ships of the task force that can be replaced: the chosen ship is sold at the Dismiss price (the
    /// Decommission price when only that is possible), the selected ship is bought, and the new crew has its skill one level lower than the old one.
    /// It is done through the game's own code in one go: the Status window's removal (points, snapshot, aircraft assignments) and then the
    /// Builder's purchase and confirmation, so everything else in the game keeps its usual state.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _upgradeShip;

        void BindUpgradeConfig()
        {
            _upgradeShip = Config.Bind("Campaign", "UpgradeShip", true,
                "Campaign Task Force Builder: an \"Upgrade ship\" button replaces a ship of the task force by the selected one (sold at the Dismiss / Decommission price, the crew loses one skill level).");
        }

        sealed class UpState { public Button Btn; public float Next; public string Tip; }
        static readonly ConditionalWeakTable<object, UpState> UpStates = new ConditionalWeakTable<object, UpState>();

        const BindingFlags UpAll = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static object UpCall(object o, string name, params object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i] == null ? typeof(object) : args[i].GetType();
            MethodInfo m = null;
            for (var t = o.GetType(); t != null && m == null; t = t.BaseType)
                foreach (var c in t.GetMethods(UpAll | BindingFlags.DeclaredOnly))
                    if (c.Name == name && c.GetParameters().Length >= args.Length) { m = c; break; }
            if (m == null) throw new MissingMethodException(o.GetType().Name + "." + name);
            var ps = m.GetParameters();
            var full = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++) full[i] = i < args.Length ? args[i] : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
            try { return m.Invoke(o, full); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }

        static object UpField(object o, string name)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, UpAll | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        static object UpStatic(string typeName, string method, params object[] args)
        {
            var t = GameAssembly().GetType(string.Join(".", "SeapowerUI", "ViewModels", typeName));   // (not one literal: Keel would list it as a type this game lacks)
            if (t == null) throw new MissingMemberException(typeName);
            foreach (var m in t.GetMethods(UpAll))
                if (m.Name == method && m.IsStatic && m.GetParameters().Length == args.Length)
                {
                    try { return m.Invoke(null, args); }
                    catch (TargetInvocationException e) { throw e.InnerException ?? e; }
                }
            throw new MissingMethodException(typeName + "." + method);
        }

        sealed class UpCandidate { public object Item; public string Name; public int Refund; public int RefundBase; public bool Decommission; public int OldSkill, NewSkill; public string Why; public int Cost, Sur, OldCap, NewCap, Avail, Proj, Cap, Aircraft, Slots, RepairCost; }

        static string SkillName(int skill)
        {
            try { return (string)UpStatic("LinearCampaignTaskForceCrewSkillRules", "GetDisplayName", Enum.ToObject(GameAssembly().GetType("SeaPower.ObjectBase+CrewSkill"), skill)); }
            catch { return skill.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>Ships of the task force that the selected roster ship may replace (with why not, when it may not).</summary>
        List<UpCandidate> UpgradeCandidates(object vm, object sel)
        {
            var res = new List<UpCandidate>();
            var status = UpField(vm, "_statusViewModel");
            var items = status == null ? null : UpField(status, "_items") as IEnumerable;
            if (items == null || sel == null || GetB(sel, "IsAirUnit")) return res;
            string selType = Get(sel, "UnitType") as string, selVar = Get(sel, "VariantReference") as string, selCat = Get(sel, "Category") as string;
            int avail = (int)Get(vm, "AvailablePoints"), cap = (int)Get(vm, "PointCap"), proj = (int)Get(vm, "ProjectedFleetValue"), pending = (int)Get(vm, "PendingPurchaseCost");
            int cost = (int)Get(sel, "Cost");
            int sur = (int)UpStatic("LinearCampaignTaskForceBuilderViewModel", "GetItemScarcitySurcharge", sel);
            var ini = UpStatic("LinearCampaignTaskForceModeRules", "OpenCampaignIni");
            int newCap = 0;
            if ((bool)UpStatic("LinearCampaignTaskForceModeRules", "CountsAgainstFleetCap", sel))
                newCap = (int)UpStatic("LinearCampaignTaskForceModeRules", "GetFleetCapValue", sel) + (int)UpStatic("LinearCampaignTaskForceModeRules", "GetAppliedLoadoutCapValue", ini, sel)
                         + (int)UpStatic("LinearCampaignTaskForceModeRules", "GetScarcitySurchargeCapValue", sel);
            foreach (var it in items)
            {
                if (it == null || GetB(it, "IsAirUnit")) continue;
                if (!string.Equals(Get(it, "Category") as string, selCat, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(Get(it, "UnitType") as string, selType, StringComparison.OrdinalIgnoreCase) && string.Equals(Get(it, "VariantReference") as string, selVar, StringComparison.OrdinalIgnoreCase)) continue;
                var c = new UpCandidate { Item = it, Name = Get(it, "InspectionTitle") as string ?? (Get(it, "UnitType") as string) };
                bool dismiss = (bool)UpCall(status, "CanDismiss", it), decom = !dismiss && (bool)UpCall(status, "CanDecommission", it);
                if (!dismiss && !decom) continue;
                c.Decommission = decom;
                c.RefundBase = (int)(decom ? UpCall(status, "CalculateDecommissionRefund", it) : UpCall(status, "CalculateDismissRefund", it, 1));
                c.Refund = (int)(decom ? UpCall(status, "CalculateDecommissionRefundTotal", it) : UpCall(status, "CalculateDismissRefundTotal", it, 1));
                try { bool rep; int rc; if (!decom && _dismissDamaged.Value && RepairInfo(status, it, out rep, out rc)) c.RepairCost = rc; } catch { }
                c.OldSkill = Convert.ToInt32(Get(it, "CrewSkill"), CultureInfo.InvariantCulture);
                c.NewSkill = Math.Max(0, c.OldSkill - 1);
                int oldCap = 0;
                if ((bool)UpStatic("LinearCampaignTaskForceModeRules", "CountsAgainstFleetCap", it)) oldCap = (int)UpStatic("LinearCampaignTaskForceModeRules", "GetFleetCapValue", it);
                if (pending + cost + sur > avail + c.RefundBase) c.Why = Loc.T("up.points");
                else if (cap > 0 && proj - oldCap + newCap > cap) c.Why = Loc.T("up.cap");
                try { c.Slots = Convert.ToInt32(Get(sel, "HangarSlots"), CultureInfo.InvariantCulture); } catch { }
                c.Cost = cost; c.Sur = sur; c.OldCap = oldCap; c.NewCap = newCap; c.Avail = avail; c.Proj = proj; c.Cap = cap;
                foreach (var a in items)
                {
                    var asg = a == null || !GetB(a, "IsAirUnit") ? null : Get(a, "AircraftAssignments") as IDictionary<string, int>;
                    int n; if (asg != null && asg.TryGetValue(Get(it, "InstanceId") as string ?? "", out n)) c.Aircraft += n;
                }
                res.Add(c);
            }
            return res;
        }

        void UpgradeStep(object w, float now)
        {
            if (!_upgradeShip.Value) return;
            var vm = Get(w, "DataContext");
            if (vm == null || TypeName(vm) != "LinearCampaignTaskForceBuilderViewModel") return;
            var st = UpStates.GetOrCreateValue(w);
            if (now < st.Next) return;
            st.Next = now + 0.4f;
            if (st.Btn == null || VisualTreeHelper.GetParent(st.Btn) == null)
            {
                st.Btn = null;
                var cmd = Get(vm, "OpenSelectedUnitReferenceCommand");
                var refBtn = cmd == null ? null : FindFirst((DependencyObject)w, e => e is Button && ReferenceEquals(((Button)e).Command, cmd)) as Button;
                var panel = refBtn == null ? null : VisualTreeHelper.GetParent(refBtn) as Panel;
                if (panel == null) return;
                var b = new Button { Content = Loc.T("up.button"), Height = 34f, Margin = new Thickness(0f, 8f, 0f, 0f) };
                try { b.Style = refBtn.Style; } catch { }
                b.Click += (o, ev) => { try { OpenUpgradeMenu(w, vm); } catch (Exception e) { Trace("UpgradeShip (menu): " + e); } };
                panel.Children.Insert(panel.Children.IndexOf(refBtn) + 1, b);
                st.Btn = b;
                Trace("Task Force Builder: Upgrade ship button added");
            }
            string why = null;
            try
            {
                var sel = Get(vm, "SelectedRosterItem");
                if (sel == null || GetB(sel, "IsAirUnit")) why = Loc.T("up.noSel");
                else if (UpField(vm, "_statusViewModel") == null) why = Loc.T("up.noStatus");
                else if (GetB(UpField(vm, "_statusViewModel"), "UsesHistoricalReinforcements")) why = Loc.T("up.hist");
                else if (((System.Collections.ICollection)Get(vm, "PendingPurchaseItems")).Count > 0 || GetB(vm, "HasPendingTransactions")) why = Loc.T("up.pending");
                else
                {
                    var list = UpgradeCandidates(vm, sel);
                    bool any = false; foreach (var c in list) if (c.Why == null) any = true;
                    if (list.Count == 0) why = Loc.T("up.none");
                    else if (!any) why = list[0].Why;
                }
            }
            catch (Exception e) { why = null; Trace("UpgradeShip (state): " + e.Message); }
            st.Btn.IsEnabled = why == null;
            st.Btn.Opacity = why == null ? 1f : 0.5f;
            var tip = why == null ? Loc.T("up.tip") : Loc.T("up.tip") + "\n" + why;
            if (tip != st.Tip) { st.Tip = tip; try { ToolTipService.SetToolTip(st.Btn, tip); } catch { } }
        }

        // ------------------------------------------------------------ the window

        static readonly Color UBg = Color.FromRgb(30, 33, 38), UPanel = Color.FromRgb(38, 42, 48), UCard = Color.FromRgb(45, 50, 57), ULine = Color.FromRgb(70, 76, 86),
                              UText = Color.FromRgb(230, 232, 235), UDim = Color.FromRgb(150, 156, 162), UWarn = Color.FromRgb(236, 212, 110), UTitle = Color.FromRgb(43, 48, 56),
                              UHl = Color.FromRgb(62, 84, 112), UGood = Color.FromRgb(120, 200, 130), UBad = Color.FromRgb(230, 120, 110);
        /// <summary>The brush of the game's theme for the colours of the window (so it looks like the game's own windows); our own colour when the theme has no such brush.</summary>
        static Brush UB(Color c)
        {
            string key = c.Equals(UBg) || c.Equals(UPanel) ? "Brush.Window.BackgroundDark" : c.Equals(UTitle) ? "Brush.Window.Header" : c.Equals(ULine) ? "Brush.Border.Normal"
                       : c.Equals(UCard) ? "Brush.Window.Background" : c.Equals(UText) ? "Brush.Foreground.Highlight" : c.Equals(UDim) ? "Brush.Foreground.Normal" : null;
            try
            {
                var b = key == null ? null : GUI.GetApplicationResources()[key] as Brush;
                if (b != null) return b;
            }
            catch { }
            return new SolidColorBrush(c);
        }
        static Grid _upRoot;
        static float _upW = 860f, _upH = 560f, _upX, _upY; static bool _upSized;   // size and place are kept while the game runs
        static Panel _upHost;

        static TextBlock UT(string s, Color c, float size = 13f, bool bold = false, bool wrap = false)
        {
            var t = new TextBlock { Text = s, Foreground = UB(c), FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
            if (bold) t.FontWeight = FontWeights.Bold;
            return t;
        }

        void CloseUpgradeWindow()
        {
            try { if (_upRoot != null && _upHost != null) _upHost.Children.Remove(_upRoot); } catch { }
            _upRoot = null; _upHost = null;
        }

        /// <summary>The object the game's crew badge glyph template binds to (the Show...CrewSkillBadge flags of the skill).</summary>
        public sealed class CrewGlyph
        {
            readonly int _s;
            public CrewGlyph(int s) { _s = s; }
            public bool ShowGreenCrewSkillBadge { get { return _s == 0; } }
            public bool ShowTrainedCrewSkillBadge { get { return _s == 1; } }
            public bool ShowSeasonedCrewSkillBadge { get { return _s == 2; } }
            public bool ShowVeteransCrewSkillBadge { get { return _s == 3; } }
            public bool ShowUltraCrewSkillBadge { get { return _s == 4; } }
        }

        const string ShipBoxXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"190\">" +
            "<Grid.RowDefinitions><RowDefinition Height=\"84\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>" +
            "<Image Stretch=\"Uniform\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"><Image.Source><MultiBinding Converter=\"{StaticResource IniToProfileConverter}\">" +
            "<Binding Path=\"UnitType\"/><Binding Path=\"VariantIndex\"/></MultiBinding></Image.Source></Image>" +
            "<TextBlock Grid.Row=\"1\" Text=\"{Binding InspectionTitle}\" FontSize=\"14\" FontWeight=\"Bold\" Foreground=\"#E6E8EB\" TextAlignment=\"Center\" TextWrapping=\"Wrap\" Margin=\"0,4,0,4\"/>" +
            "<Border Grid.Row=\"2\" Background=\"{Binding CrewSkill, Converter={StaticResource CrewSkillToBrushConverter}}\" Padding=\"8,2\" MinWidth=\"72\" HorizontalAlignment=\"Center\">" +
            "<StackPanel Orientation=\"Horizontal\" HorizontalAlignment=\"Center\"><ContentControl x:Name=\"Glyph\" ContentTemplate=\"{StaticResource CrewSkillBadgeGlyphTemplate}\" VerticalAlignment=\"Center\"/>" +
            "<TextBlock Text=\"{Binding CrewSkill, Converter={StaticResource CrewSkillDisplayNameConverter}}\" FontSize=\"12\" FontWeight=\"Bold\" Foreground=\"#000000\" TextAlignment=\"Center\" VerticalAlignment=\"Center\"/></StackPanel></Border></Grid>";

        /// <summary>The ship as the Task Force window shows it: its picture, name and crew badge (the game's converters); plain text when the XAML cannot be made.</summary>
        static FrameworkElement ShipBox(object item, string name, string crew, int skill)
        {
            try
            {
                var g = GUI.ParseXaml(ShipBoxXaml) as FrameworkElement;
                if (g != null) { g.DataContext = item; var gl = g.FindName("Glyph") as ContentControl; if (gl != null) gl.Content = new CrewGlyph(skill); return g; }
            }
            catch (Exception e) { Trace("UpgradeShip (ship box): " + e.Message); }
            var sp = new StackPanel { Width = 190f };
            var t1 = UT(name, UText, 14f, true, true); t1.TextAlignment = TextAlignment.Center; sp.Children.Add(t1);
            var t2 = UT(crew, UDim); t2.TextAlignment = TextAlignment.Center; sp.Children.Add(t2);
            return sp;
        }

        void OpenUpgradeMenu(object w, object vm)
        {
            var st = UpStates.GetOrCreateValue(w);
            var sel = Get(vm, "SelectedRosterItem");
            var list = UpgradeCandidates(vm, sel);
            if (list.Count == 0 || st.Btn == null) return;
            CloseUpgradeWindow();
            Panel panel = null;
            for (DependencyObject d = st.Btn; d != null; d = VisualTreeHelper.GetParent(d))
            {
                var pn = d as Panel;
                bool itemsHost = false;
                try { itemsHost = pn != null && pn.IsItemsHost; } catch { }
                if (pn != null && !itemsHost && (pn is Grid || pn is Canvas)) panel = pn;
            }
            if (panel == null) { Trace("UpgradeShip: no place for the window"); return; }
            string newName = Get(sel, "InspectionTitle") as string;

            var root = new Grid { Background = UB(Color.FromArgb(1, 0, 0, 0)) };   // not dimmed, but it takes the mouse: the window below is blocked
            if (!_upSized) { _upW = 860f; _upH = Math.Min(760f, 32f + 16f + list.Count * 164f + 58f); }
            var host = new Grid { Width = _upW, Height = _upH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var move = new TranslateTransform(_upX, _upY);
            host.RenderTransform = move;
            var win = new Border { Background = UB(UBg), BorderBrush = UB(ULine), BorderThickness = new Thickness(1f), };
            host.Children.Add(win);
            root.Children.Add(host);
            var col = new Grid();
            win.Child = col;
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1f, GridUnitType.Star) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            

            var title = new Grid { Background = UB(Color.FromArgb(1, 0, 0, 0)), Height = 24f };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tt = UT(Loc.T("up.title") + " — " + newName, UDim, 12f); tt.Margin = new Thickness(8f, 0f, 0f, 0f); tt.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(tt);
            // the game's windows have a plain "X" at the right of the header
            var x = UT("X", UDim, 12f); x.Margin = new Thickness(8f, 0f, 10f, 0f); x.VerticalAlignment = VerticalAlignment.Center; x.Background = UB(Color.FromArgb(1, 0, 0, 0));
            x.MouseEnter += (o, e) => x.Foreground = UB(UText);
            x.MouseLeave += (o, e) => x.Foreground = UB(UDim);
            x.MouseLeftButtonUp += (o, e) => { CloseUpgradeWindow(); e.Handled = true; };
            Grid.SetColumn(x, 1); title.Children.Add(x);
            col.Children.Add(title);
            {
                bool down = false; Point st0 = new Point(); float x0 = 0f, y0 = 0f;
                title.MouseLeftButtonDown += (o, e) => { down = true; st0 = e.GetPosition(root); x0 = move.X; y0 = move.Y; title.CaptureMouse(); e.Handled = true; };
                title.MouseMove += (o, e) => { if (!down) return; var q = e.GetPosition(root); move.X = x0 + (q.X - st0.X); move.Y = y0 + (q.Y - st0.Y); _upX = move.X; _upY = move.Y; };
                title.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; title.ReleaseMouseCapture(); e.Handled = true; };
            }

            var listPanel = new StackPanel { Margin = new Thickness(12f, 10f, 12f, 6f) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listPanel };
            Grid.SetRow(sv, 1); col.Children.Add(sv);

            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12f, 4f, 12f, 12f) };
            Grid.SetRow(foot, 2); col.Children.Add(foot);
            var cancel = new Button { Content = Loc.T("up.cancel"), MinWidth = 100f, Height = 30f };
            cancel.Click += (o, e) => CloseUpgradeWindow();
            foot.Children.Add(cancel);

            // resizing: the edges (6 px) and corners (16 px); the opposite side stays where it is
            Action<HorizontalAlignment, VerticalAlignment> edge = (ha, va) =>
            {
                int sx = ha == HorizontalAlignment.Right ? 1 : ha == HorizontalAlignment.Left ? -1 : 0, sy = va == VerticalAlignment.Bottom ? 1 : va == VerticalAlignment.Top ? -1 : 0;
                bool corner = sx != 0 && sy != 0;
                var h = new Border { Background = UB(Color.FromArgb(1, 0, 0, 0)), HorizontalAlignment = ha, VerticalAlignment = va,
                                     Width = sx == 0 ? float.NaN : (corner && va != VerticalAlignment.Top ? 16f : 6f), Height = sy == 0 ? float.NaN : (corner && va != VerticalAlignment.Top ? 16f : 6f) };
                try { h.Cursor = sx != 0 && sy != 0 ? (sx * sy > 0 ? Cursors.SizeNWSE : Cursors.SizeNESW) : (sx != 0 ? Cursors.SizeWE : Cursors.SizeNS); } catch { }
                if (sx == 0) h.Margin = new Thickness(16f, 0f, 16f, 0f);
                if (sy == 0) h.Margin = new Thickness(0f, 16f, 0f, 16f);
                bool down = false; Point st0 = new Point(); float w0 = 0f, h0 = 0f, x0 = 0f, y0 = 0f;
                h.MouseLeftButtonDown += (o, e) => { down = true; st0 = e.GetPosition(root); w0 = host.Width; h0 = host.Height; x0 = move.X; y0 = move.Y; h.CaptureMouse(); e.Handled = true; };
                h.MouseMove += (o, e) =>
                {
                    if (!down) return;
                    var q = e.GetPosition(root);
                    float maxW = root.ActualWidth > 0 ? root.ActualWidth - 20f : 3000f, maxH = root.ActualHeight > 0 ? root.ActualHeight - 20f : 2000f;
                    float nw = Math.Max(560f, Math.Min(maxW, w0 + sx * (q.X - st0.X))), nh = Math.Max(300f, Math.Min(maxH, h0 + sy * (q.Y - st0.Y)));
                    if (sx != 0) { host.Width = nw; move.X = x0 + sx * (nw - w0) / 2f; }
                    if (sy != 0) { host.Height = nh; move.Y = y0 + sy * (nh - h0) / 2f; }
                    _upSized = true; _upW = host.Width; _upH = host.Height; _upX = move.X; _upY = move.Y;
                };
                h.MouseLeftButtonUp += (o, e) => { if (!down) return; down = false; h.ReleaseMouseCapture(); e.Handled = true; };
                host.Children.Add(h);
            };
            edge(HorizontalAlignment.Right, VerticalAlignment.Stretch); edge(HorizontalAlignment.Left, VerticalAlignment.Stretch);
            edge(HorizontalAlignment.Stretch, VerticalAlignment.Bottom); edge(HorizontalAlignment.Stretch, VerticalAlignment.Top);
            edge(HorizontalAlignment.Right, VerticalAlignment.Bottom); edge(HorizontalAlignment.Left, VerticalAlignment.Bottom);
            edge(HorizontalAlignment.Right, VerticalAlignment.Top); edge(HorizontalAlignment.Left, VerticalAlignment.Top);

            foreach (var cand in list)
            {
                var c = cand;
                // the ship that will be bought, with the lowered crew (for the picture and the badge only: it is never added anywhere)
                object shown = null;
                try
                {
                    shown = UpCall(sel, "CreatePurchasedCopy", 1);
                    SetBacking(shown, "CrewSkill", Enum.ToObject(GameAssembly().GetType("SeaPower.ObjectBase+CrewSkill"), c.NewSkill));
                }
                catch (Exception e) { Trace("UpgradeShip (display copy): " + e.Message); shown = sel; }

                var g = new Grid { Opacity = c.Why == null ? 1f : 0.6f };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40f) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1f, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var oldBox = ShipBox(c.Item, c.Name, SkillName(c.OldSkill), c.OldSkill);
                oldBox.Margin = new Thickness(10f, 8f, 0f, 8f);
                g.Children.Add(oldBox);
                var arrow = UT("→", UDim, 26f); arrow.HorizontalAlignment = HorizontalAlignment.Center; arrow.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(arrow, 1); g.Children.Add(arrow);
                var newBox = ShipBox(shown, newName, SkillName(c.NewSkill), c.NewSkill);
                newBox.Margin = new Thickness(0f, 8f, 0f, 8f);
                Grid.SetColumn(newBox, 2); g.Children.Add(newBox);

                var d = new StackPanel { Margin = new Thickness(16f, 8f, 8f, 8f), VerticalAlignment = VerticalAlignment.Center };
                d.Children.Add(UT(string.Format(CultureInfo.InvariantCulture, Loc.T(c.Decommission ? "up.saleDecom" : "up.saleDismiss"), c.Refund), UText, 13f, false, true));
                if (c.RepairCost > 0) d.Children.Add(UT(string.Format(CultureInfo.InvariantCulture, Loc.T("up.damaged"), c.RepairCost), UWarn, 12f, false, true));
                d.Children.Add(UT(string.Format(CultureInfo.InvariantCulture, Loc.T(c.Sur > 0 ? "up.priceSur" : "up.price"), c.Cost, c.Sur), UText, 13f, false, true));
                d.Children.Add(UT(Loc.T("up.crew") + ": " + SkillName(c.OldSkill) + " → " + SkillName(c.NewSkill) + (c.NewSkill == c.OldSkill ? " (" + Loc.T("up.min") + ")" : ""), c.NewSkill < c.OldSkill ? UWarn : UDim, 13f));
                int after = c.Avail + c.RefundBase - c.Cost - c.Sur;
                d.Children.Add(UT(string.Format(CultureInfo.InvariantCulture, Loc.T("up.points2"), c.Avail, after), after >= 0 ? UGood : UBad, 13f));
                if (c.Cap > 0)
                {
                    int fa = c.Proj - c.OldCap + c.NewCap;
                    d.Children.Add(UT(string.Format(CultureInfo.InvariantCulture, Loc.T("up.fleet"), c.Proj, fa, c.Cap), fa <= c.Cap ? UGood : UBad, 13f));
                }
                if (c.Aircraft > 0) d.Children.Add(UT(string.Format(CultureInfo.InvariantCulture, Loc.T("up.aircraft"), c.Aircraft, Math.Min(c.Aircraft, Math.Max(0, c.Slots))), UWarn, 12f, false, true));
                if (c.Why != null) d.Children.Add(UT(c.Why, UBad, 13f, true, true));
                Grid.SetColumn(d, 3); g.Children.Add(d);

                var go = new Button { Content = Loc.T("up.do"), MinWidth = 110f, Height = 32f, Margin = new Thickness(0f, 0f, 12f, 0f), VerticalAlignment = VerticalAlignment.Center, IsEnabled = c.Why == null };
                go.Click += (o, e) =>
                {
                    CloseUpgradeWindow();
                    try { DoUpgrade(vm, c); } catch (Exception ex) { Trace("UpgradeShip: " + ex); }
                };
                Grid.SetColumn(go, 4); g.Children.Add(go);
                listPanel.Children.Add(new Border { Child = g, Background = UB(UCard), BorderBrush = UB(ULine), BorderThickness = new Thickness(1f), Margin = new Thickness(0f, 0f, 0f, 8f) });
            }

            var grid = panel as Grid;
            if (grid != null)
            {
                Grid.SetRow(root, 0); Grid.SetColumn(root, 0);
                Grid.SetRowSpan(root, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(root, Math.Max(1, grid.ColumnDefinitions.Count));
            }
            else
            {
                root.Width = panel.ActualWidth > 0 ? panel.ActualWidth : 1920f; root.Height = panel.ActualHeight > 0 ? panel.ActualHeight : 1080f;
                Canvas.SetLeft(root, 0f); Canvas.SetTop(root, 0f);
            }
            Panel.SetZIndex(root, 30000);
            panel.Children.Add(root);
            _upRoot = root; _upHost = panel;
        }

        void DoUpgrade(object vm, UpCandidate c)
        {
            var status = UpField(vm, "_statusViewModel");
            var sel = Get(vm, "SelectedRosterItem");
            string selName = Get(sel, "InspectionTitle") as string;
            Trace("UpgradeShip: " + c.Name + " (" + SkillName(c.OldSkill) + ", " + (c.Decommission ? "decommission" : "dismiss") + " +" + c.RefundBase + ") -> " + selName);
            // the aircraft assigned to the old ship (type, variant, count): they are put on the new ship afterwards, as far as its hangar goes
            var moves = new List<KeyValuePair<string[], int>>();
            string oldId = Get(c.Item, "InstanceId") as string ?? "";
            foreach (var a in (IEnumerable)UpField(status, "_items"))
            {
                var asg = a == null || !GetB(a, "IsAirUnit") ? null : Get(a, "AircraftAssignments") as IDictionary<string, int>;
                int n; if (asg != null && asg.TryGetValue(oldId, out n) && n > 0) moves.Add(new KeyValuePair<string[], int>(new[] { Get(a, "UnitType") as string, Get(a, "VariantReference") as string }, n));
            }
            // 1. sell the old ship through the Status window (points, snapshot, aircraft assignments, the game's events)
            if (c.Decommission) UpCall(status, "RemoveFromTaskForce", c.Item, c.RefundBase, false, true);
            else UpCall(status, "DismissFromTaskForce", c.Item, c.RefundBase, 1);
            // 2. buy the selected ship through the Builder
            UpCall(vm, "RefreshCurrentTaskForceOwnershipKeys");
            var pending = (IList)Get(vm, "PendingPurchaseItems");
            int before = pending.Count;
            UpCall(vm, "AddSelectedItem", false);
            if (pending.Count <= before) { Trace("UpgradeShip: the Builder did not take the new ship (the old one is already sold, points returned)"); return; }
            // the new crew: one level lower; the pending ships (the hull and the aircraft that came with it) are committed together
            string newId = null;
            foreach (var p in pending)
            {
                if (GetB(p, "IsAirUnit")) continue;
                newId = Get(p, "InstanceId") as string;
                SetBacking(p, "CrewSkill", Enum.ToObject(GameAssembly().GetType("SeaPower.ObjectBase+CrewSkill"), c.NewSkill));
                SetBacking(p, "CrewSkillSurvivalCredits", UpStatic("LinearCampaignTaskForceCrewSkillRules", "GetMinimumSurvivalCreditsForSkill", Enum.ToObject(GameAssembly().GetType("SeaPower.ObjectBase+CrewSkill"), c.NewSkill)));
            }
            UpCall(vm, "ConfirmTaskForceSelection");
            if (pending.Count > 0) Trace("UpgradeShip: the purchase was not confirmed (" + pending.Count + " item(s) stay in the Builder's list to confirm by hand)");
            else Trace("UpgradeShip: done");
            if (moves.Count > 0 && newId != null)
            {
                try
                {
                    int moved = 0;
                    foreach (var mv in moves)
                        for (int k = 0; k < mv.Value; k++)
                        {
                            object ship = null, air = null;
                            foreach (var it in (IEnumerable)UpField(status, "_items"))
                            {
                                if (it == null) continue;
                                if (!GetB(it, "IsAirUnit") && string.Equals(Get(it, "InstanceId") as string, newId, StringComparison.OrdinalIgnoreCase)) ship = it;
                                else if (GetB(it, "IsAirUnit") && air == null && string.Equals(Get(it, "UnitType") as string, mv.Key[0], StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(Get(it, "VariantReference") as string, mv.Key[1], StringComparison.OrdinalIgnoreCase)) air = it;
                            }
                            if (ship == null || air == null) break;
                            var asg = Get(ship, "InstanceId") as string;
                            int before2 = AssignedTo(air, asg);
                            UpCall(status, "AssignAircraft", air, ship, false);
                            if (AssignedTo(air, asg) <= before2) break;   // no free hangar slot / nothing left unassigned
                            moved++;
                        }
                    Trace("UpgradeShip: " + moved + " aircraft moved to the new ship");
                }
                catch (Exception e) { Trace("UpgradeShip (aircraft): " + e.Message); }
            }
        }

        static int AssignedTo(object air, string shipId)
        {
            var d = air.GetType().GetProperty("AircraftAssignments").GetValue(air, null) as IDictionary<string, int>;
            int n; return d != null && d.TryGetValue(shipId, out n) ? n : 0;
        }

        static void SetBacking(object o, string prop, object value)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField("<" + prop + ">k__BackingField", UpAll | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(o, value); return; }
            }
            throw new MissingFieldException(o.GetType().Name, prop);
        }
    }
}
