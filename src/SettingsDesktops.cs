using System;
using System.Collections.Generic;
using System.Globalization;
using Noesis;

namespace AlternativeGameInterface
{
    public partial class AlternativeGameInterfacePlugin
    {
        /// <summary>
        /// The "Desktops" tab of the settings window (desktop mode): the list of the desktops (their number is the DesktopCount setting, the list itself
        /// is not edited here) and, for the chosen one, what is saved for each window - position, size and open / closed - to edit, and ↺ to bring a
        /// window (or the whole desktop) back to the mod's built-in arrangement. Works on a copy: Apply / OK store it (and put the active desktop on
        /// the windows), Cancel drops it.
        /// </summary>
        static partial class SettingsWindow
        {
            static readonly DeskData[] _dk = new DeskData[MaxDesktops + 1];     // the copies being edited (loaded when the desktop is first shown)
            static readonly HashSet<int> _dkEdited = new HashSet<int>();
            static int _dkSel = 1;
            static readonly string[] DeskOrder = { "World View", "Tactical Display", "Unit Status*", "Unit Reference*", "Event Log", "Formation Manager", "Time Panel" };

            static int _mapSub;      // the tab control inside the "Desktop mode" tab: 0 Settings, 1 Desktops

            /// <summary>The tab control at the top of the "Desktop mode" tab (only while desktop mode is on); true: the Desktops page is chosen.</summary>
            static bool MapSubTabs()
            {
                bool on = _mapField != null && Get(_mapField) is bool && (bool)Get(_mapField);
                if (!on) { _mapSub = 0; return false; }
                var strip = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 6f, 12f, 2f) };
                string[] names = { DT("Settings"), DT("Desktops") };
                for (int i = 0; i < names.Length; i++)
                {
                    int idx = i;
                    bool sel = i == _mapSub;
                    var b = new Border { Child = Text(names[i] + (i == 1 && _dkEdited.Count > 0 ? "  •" : ""), sel ? CText : CDim), Padding = new Thickness(14f, 5f, 14f, 5f), Margin = new Thickness(0f, 0f, 2f, 0f),
                                         Background = B(sel ? CBg : CPanel), BorderBrush = B(CLine), BorderThickness = new Thickness(1f, 1f, 1f, 0f), CornerRadius = new CornerRadius(3f, 3f, 0f, 0f) };
                    try { b.Cursor = Cursors.Hand; } catch { }
                    b.MouseLeftButtonUp += (o, e) => { _mapSub = idx; ShowTab(_tab); e.Handled = true; };
                    strip.Children.Add(b);
                }
                _body.Children.Add(strip);
                return _mapSub == 1;
            }

            static string DT(string en) { return Loc.Or("dk." + en, en); }

            static int DeskN()
            {
                var f = Fields.Find(x => ReferenceEquals(x.Entry, P._deskCount));
                float v = P._deskCount.Value;
                if (f != null && Get(f) is float) v = (float)Get(f);
                return Math.Max(2, Math.Min(MaxDesktops, (int)Math.Round(v)));
            }

            static void ClearDesktops()
            {
                for (int i = 0; i < _dk.Length; i++) _dk[i] = null;
                _dkEdited.Clear();
            }

            static DeskData Desk(int i)
            {
                if (_dk[i] == null)
                {
                    // the active desktop is the live settings, the others are in their slots (empty: the built-in arrangement)
                    string text = i == P.ActiveDesk() ? P.SnapshotSet() : P._deskSlot[i].Value;
                    if (string.IsNullOrEmpty(text)) text = P.PresetSet(i);
                    var dd = DeskData.Parse(text);
                    // windows the stored / built-in arrangement does not mention keep what they have now: listed too
                    if (i != P.ActiveDesk())
                    {
                        var live = DeskData.Parse(P.SnapshotSet());
                        foreach (var kv in live.L) if (!dd.L.ContainsKey(kv.Key)) dd.L[kv.Key] = kv.Value;
                        foreach (var kv in live.O) if (!dd.O.ContainsKey(kv.Key)) dd.O[kv.Key] = kv.Value;
                    }
                    _dk[i] = dd;
                }
                return _dk[i];
            }

            static void Edited(int i) { _dkEdited.Add(i); UpdateHeader(); }

            /// <summary>Apply / OK: the edited desktops are stored; the active one is put on the windows.</summary>
            static void CommitDesktops()
            {
                if (_dkEdited.Count == 0) return;
                int act = P.ActiveDesk(), n = 0;
                foreach (int i in new List<int>(_dkEdited))
                {
                    var d = _dk[i];
                    if (d == null) continue;
                    try
                    {
                        P._deskSlot[i].Value = d.ToText();
                        if (i == act)
                        {
                            P.ApplySet(d);
                            foreach (var k in d.Removed) P.ResetLayouts(k);
                        }
                        n++;
                    }
                    catch (Exception e) { Trace("settings: desktop " + i + ": " + e.Message); }
                }
                P.Config.Save();
                ClearDesktops();    // shown again from what is stored
                Trace("settings applied: " + n + " desktop(s)");
            }

            /// <summary>The windows listed for a desktop: the usual ones in a fixed order, then the others with something saved.</summary>
            static List<string> DeskKeys(DeskData d)
            {
                var keys = new List<string>();
                foreach (var k in DeskOrder)
                    if (d.L.ContainsKey(k) || d.O.ContainsKey(k) || k == "World View" || k == "Tactical Display") keys.Add(k);
                var rest = new List<string>();
                foreach (var k in d.L.Keys) if (!keys.Contains(k) && !rest.Contains(k)) rest.Add(k);
                foreach (var k in d.O.Keys) if (!keys.Contains(k) && !rest.Contains(k)) rest.Add(k);
                rest.Sort(StringComparer.OrdinalIgnoreCase);
                keys.AddRange(rest);
                return keys;
            }

            // ------------------------------------------------------------ the tab

            static void BuildDesktops()
            {
                int n = DeskN();
                if (_dkSel < 1 || _dkSel > n) _dkSel = 1;
                int active = P.ActiveDesk();

                _body.Children.Add(GroupTitle(DT("Desktops")));
                var strip = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 4f, 12f, 4f) };
                for (int i = 1; i <= n; i++)
                {
                    int idx = i;
                    bool sel = i == _dkSel;
                    string label = DT("Desktop") + " " + i + (i == active ? "  ● " + DT("active") : "") + (_dkEdited.Contains(i) ? "  •" : "");
                    var b = new Border { Child = Text(label, sel ? CText : CDim), Padding = new Thickness(12f, 6f, 12f, 6f), Margin = new Thickness(0f, 0f, 4f, 4f),
                                         Background = B(sel ? CBg : CPanel), BorderBrush = B(CLine), BorderThickness = new Thickness(1f), CornerRadius = new CornerRadius(3f) };
                    try { b.Cursor = Cursors.Hand; } catch { }
                    b.MouseLeftButtonUp += (o, e) => { _dkSel = idx; ShowTab(_tab); e.Handled = true; };
                    strip.Children.Add(b);
                }
                _body.Children.Add(strip);

                int sid = _dkSel;
                var d = Desk(sid);
                var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12f, 2f, 12f, 4f) };
                bar.Children.Add(Btn(DT("Reset desktop to default"), () => ResetDesk(sid)));
                var note = Text(sid == active ? DT("the active desktop: Apply puts it on the windows") : DT("stored; used when you switch to it"), CDim, 11f);
                note.Margin = new Thickness(6f, 0f, 0f, 0f);
                bar.Children.Add(note);
                _body.Children.Add(bar);

                _body.Children.Add(GroupTitle(DT("Saved windows") + " - " + DT("Desktop") + " " + sid));
                var head = DeskGrid();
                string[] cols = { DT("Window"), "X", "Y", DT("Width"), DT("Height"), DT("Open") };
                for (int c = 0; c < cols.Length; c++)
                {
                    var t = Text(cols[c], CDim, 11f);
                    t.HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                    Grid.SetColumn(t, c);
                    head.Children.Add(t);
                }
                _body.Children.Add(new Border { Child = head, Padding = new Thickness(12f, 2f, 6f, 2f) });
                foreach (var key in DeskKeys(d)) _body.Children.Add(DeskRow(sid, d, key));
            }

            static Grid DeskGrid()
            {
                var g = new Grid { Background = Clear };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200f) });
                for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76f) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64f) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34f) });
                return g;
            }

            static string NumText(float v) { return float.IsNaN(v) ? "auto" : v.ToString("0.#", CultureInfo.InvariantCulture); }

            static FrameworkElement DeskRow(int di, DeskData d, string key)
            {
                var g = DeskGrid();
                var outer = new Border { Child = g, Padding = new Thickness(12f, 3f, 6f, 3f), Background = Clear };
                var name = Text(key, CText);
                g.Children.Add(name);

                Layout l0;
                bool has = d.L.TryGetValue(key, out l0);
                for (int c = 0; c < 4; c++)
                {
                    int ci = c;
                    FrameworkElement cell;
                    if (!has || (ci >= 2 && key == "Time Panel"))      // the time panel has no size
                    {
                        var dash = Text("-", CDim);
                        dash.HorizontalAlignment = HorizontalAlignment.Center;
                        cell = dash;
                    }
                    else
                    {
                        var box = Edit("", 66f);
                        Action show = () =>
                        {
                            Layout cur;
                            if (!d.L.TryGetValue(key, out cur)) return;
                            box.Text = NumText(ci == 0 ? cur.X : ci == 1 ? cur.Y : ci == 2 ? cur.W : cur.H);
                        };
                        Action typed = () =>
                        {
                            Layout cur;
                            if (!d.L.TryGetValue(key, out cur)) return;
                            string s = (box.Text ?? "").Trim();
                            float v;
                            bool ok = false;
                            if (ci >= 2 && (s.Length == 0 || s.Equals("auto", StringComparison.OrdinalIgnoreCase))) { v = float.NaN; ok = true; }   // the width / height the window has by itself
                            else ok = TryNum(s, out v);
                            if (ok)
                            {
                                float old = ci == 0 ? cur.X : ci == 1 ? cur.Y : ci == 2 ? cur.W : cur.H;
                                bool same = float.IsNaN(old) ? float.IsNaN(v) : !float.IsNaN(v) && Math.Abs(old - v) < 0.05f;
                                if (!same)
                                {
                                    if (ci == 0) cur.X = v; else if (ci == 1) cur.Y = v; else if (ci == 2) cur.W = v; else cur.H = v;
                                    d.L[key] = cur;
                                    Edited(di);
                                }
                            }
                            show();      // not a number: back to the value as it was
                        };
                        Commits(box, typed, show);
                        show();
                        cell = box;
                    }
                    Grid.SetColumn(cell, 1 + c);
                    g.Children.Add(cell);
                }

                // open / closed: World View - the World View window; the others - the windows of RememberOpenWindows
                bool isWv = key == "World View";
                string st = isWv ? d.W : (d.O.ContainsKey(key) ? d.O[key] : null);
                if (st != null && (st == "open" || st == "closed"))
                {
                    var cb = new CheckBox { IsChecked = st == "open", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (o, e) =>
                    {
                        string v = cb.IsChecked == true ? "open" : "closed";
                        if (isWv) d.W = v; else d.O[key] = v;
                        Edited(di);
                    };
                    Grid.SetColumn(cb, 5);
                    g.Children.Add(cb);
                }

                var rst = SmallButton("↺", DT("Back to the built-in arrangement"), () => ResetRow(di, key));
                Grid.SetColumn(rst, 6);
                g.Children.Add(rst);
                Hover(outer, key, DT("What is saved for this window on this desktop: the position and size (interface units; width and height 'auto' = the window's own) and whether it is open. The ↺ button brings it back to the built-in arrangement."), null);
                return outer;
            }

            /// <summary>One window back to the built-in arrangement of the desktop (a window it does not place loses its saved position).</summary>
            static void ResetRow(int di, string key)
            {
                var d = Desk(di);
                var pre = DeskData.Parse(P.PresetSet(di));
                Layout pl; string po;
                if (pre.L.TryGetValue(key, out pl)) d.L[key] = pl;
                else if (d.L.Remove(key)) d.Removed.Add(key);
                if (pre.O.TryGetValue(key, out po)) d.O[key] = po;
                if (key == "World View" && pre.W != null) d.W = pre.W;
                Edited(di);
                ShowTab(_tab);
            }

            /// <summary>All the windows the built-in arrangement places: back to it. Other windows (the Task Force Builder, ...) are not touched.</summary>
            static void ResetDesk(int di)
            {
                var d = Desk(di);
                var pre = DeskData.Parse(P.PresetSet(di));
                foreach (var kv in pre.L) d.L[kv.Key] = kv.Value;
                foreach (var kv in pre.O) d.O[kv.Key] = kv.Value;
                if (pre.W != null) d.W = pre.W;
                Edited(di);
                ShowTab(_tab);
            }
        }
    }
}
