using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// The gear button in the title bar of every remembered window, and what the settings window (SettingsWindow.cs) needs from
    /// the plugin: re-reading the window lists, redrawing frames and gears, applying a changed setting at once.
    /// Noesis is used directly here (as in Alternative Damage Model); the game's own types still only through reflection.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        static readonly ConditionalWeakTable<object, object> Gears = new ConditionalWeakTable<object, object>(); // window -> its gear
        readonly List<WeakReference> _borderOwners = new List<WeakReference>(); // windows given a frame (to redraw it)

        // ------------------------------------------------------------------ patterns (also re-read when changed in the game)

        /// <summary>WindowPatterns -> _patterns; a [Layouts] entry is bound for every new pattern and its saved layout read.</summary>
        void ReloadPatterns()
        {
            _patterns.Clear();
            _patternsSq.Clear();
            foreach (var n in (_windows.Value ?? "").Split(','))
            {
                var t = n.Trim();
                if (t.Length == 0 || _patterns.Contains(t)) continue;
                _patterns.Add(t);
                _patternsSq.Add(Squash(t));
            }
            // one config entry per pattern holds that window's saved layout: x,y,w,h
            foreach (var pat in _patterns)
            {
                if (_layoutEntries.ContainsKey(pat)) continue;
                try
                {
                    var e = BindLayout(pat);
                    Layout l;
                    if (Layout.TryParse(e.Value, out l)) _saved[pat] = l;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Pattern '" + pat + "' cannot be used as a config key (" + ex.Message + "); its layout will not be saved.");
                }
            }
        }

        /// <summary>RememberOpenWindows -> _osPatterns; an [OpenWindows] entry is bound for every new pattern.</summary>
        void ReloadOpenPatterns()
        {
            _osPatterns.Clear();
            _osPatternsSq.Clear();
            foreach (var n in (_openWindows.Value ?? "").Split(','))
            {
                var t = n.Trim();
                if (t.Length == 0 || _osPatterns.Contains(t)) continue;
                _osPatterns.Add(t);
                _osPatternsSq.Add(Squash(t));
                if (!_osEntries.ContainsKey(t))
                    _osEntries[t] = Config.Bind("OpenWindows", t, "", "open / closed - written by the plugin (RememberOpenWindows).");
            }
        }

        /// <summary>After WindowPatterns changed: every window is matched again (a window that keeps its pattern stays where it is).</summary>
        void RematchWindows()
        {
            foreach (var t in _tracked)
            {
                var w = t.Ref.Target;
                if (w == null) continue;
                var st = EarlyStates.GetOrCreateValue(w);
                st.Key = null;
                st.Label = null;
                t.Key = null; t.Tries = 0; t.NextTry = 0f; t.Settled = false; t.Countdown = -1; t.Reported = false;
            }
        }

        void RematchOpenWindows()
        {
            foreach (var t in _tracked) { t.OsTried = false; t.OsTries = 0; t.OsKey = null; t.OsKnown = false; t.OsNext = 0f; }
            _osSeen.Clear();
            _osPending.Clear();
            _osWantOpen.Clear();
            _osEnforce.Clear();
        }

        /// <summary>A setting was changed in the settings window: save it and apply what is not read live.</summary>
        void AfterChange(ConfigEntryBase e)
        {
            Config.Save();
            if (ReferenceEquals(e, _windows))
            {
                ReloadPatterns();
                RematchWindows();
                RefreshGears();
            }
            else if (ReferenceEquals(e, _openWindows)) { ReloadOpenPatterns(); RematchOpenWindows(); }
            else if (ReferenceEquals(e, _borderOn) || ReferenceEquals(e, _borderThickness) || ReferenceEquals(e, _borderColor)) RefreshBorders();
            else if (ReferenceEquals(e, _strictLoadouts)) SaveStrictFromProxy();
            else if (ReferenceEquals(e, _removeLimitsAll)) RefreshSizeLimits();
            else if (ReferenceEquals(e, _secMin)) ReapplyDamageControl();
            else if (ReferenceEquals(e, _debugEntry))
            {
                _debug = _debugEntry.Value;
                Trace("Smart Windows " + PluginVersion + " build " + BuildId + " - debug switched on in the settings window");
            }
            Verbose("Setting " + e.Definition.Section + "." + e.Definition.Key + " = " + TextOf(e.BoxedValue));
        }

        /// <summary>Forgets the saved layout of one pattern (null: of all). A window of it now open keeps its place until moved.</summary>
        void ResetLayouts(string key)
        {
            try
            {
                foreach (var k in key == null ? new List<string>(_layoutEntries.Keys) : new List<string> { key })
                {
                    _saved.Remove(k);
                    ConfigEntry<string> e;
                    if (_layoutEntries.TryGetValue(k, out e)) e.Value = "";
                }
                Save(); // writes the other layouts still waiting, and the file
                // windows of these patterns on screen: what they show now becomes the baseline, so they are not saved right back
                foreach (var t in _tracked)
                {
                    var w = t.Ref.Target;
                    if (w != null && t.Key != null && (key == null || t.Key == key)) t.Last = Read(w);
                }
                Verbose(key == null ? "All saved layouts reset" : "Saved layout of '" + key + "' reset");
            }
            catch (Exception e) { Trace("ResetLayouts: " + e.Message); }
        }

        /// <summary>While a text box of the settings window has the keyboard, the game's own keys are off (InputHandler.typingActive).</summary>
        void SetTyping(bool on, object element)
        {
            try
            {
                var asm = GameAssembly();
                var ih = asm == null ? null : asm.GetType("SeaPower.InputHandler");
                if (ih == null) return;
                const System.Reflection.BindingFlags sf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
                var exists = ih.GetMethod("InstanceExists", sf, null, new[] { typeof(bool) }, null);
                var instP = ih.GetProperty("Instance", sf);
                if (exists == null || instP == null || !(bool)exists.Invoke(null, new object[] { false })) return;
                var inst = instP.GetValue(null, null);
                const System.Reflection.BindingFlags inf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
                var active = ih.GetField("typingActive", inf);
                var elem = ih.GetField("typingElement", inf);
                if (active == null) return;
                if (!on && elem != null && element != null)
                {
                    var cur = elem.GetValue(inst);
                    if (cur != null && !ReferenceEquals(cur, element) && !cur.Equals(element)) return; // another box took it meanwhile
                }
                active.SetValue(inst, on);
                if (elem != null) elem.SetValue(inst, on ? element : null);
            }
            catch (Exception e) { Trace("SetTyping: " + e.Message); }
        }

        // ------------------------------------------------------------------ the gear button

        /// <summary>The panel at the root of the window's template (or the first panel below it through single-child wrappers).</summary>
        static Panel TemplateRootPanel(object w)
        {
            var e = w as DependencyObject;
            e = e != null && VisualTreeHelper.GetChildrenCount(e) > 0 ? VisualTreeHelper.GetChild(e, 0) : null;
            for (int depth = 0; e != null && depth < 6; depth++)
            {
                var p = e as Panel;
                if (p != null) return p;
                if (VisualTreeHelper.GetChildrenCount(e) != 1) break;
                e = VisualTreeHelper.GetChild(e, 0);
            }
            return null;
        }

        static readonly Color GearGrey = Color.FromRgb(170, 176, 184);   // as the gear of Alternative Damage Model

        /// <summary>
        /// Puts the gear (as the one of Alternative Damage Model: grey, 14 x 14, a little dimmer until the mouse is on it) into the
        /// title bar of a remembered window - once; again if it left the tree. It goes into the panel at the root of the window's
        /// template, aligned right, just left of the title bar's own buttons (found by position: visible buttons on the header's
        /// line in its right part), centred on the header Thumb.
        /// </summary>
        void EnsureGear(object w)
        {
            try
            {
                if (w == null || _hookFailed || !(w is FrameworkElement)) return;
                object old;
                if (Gears.TryGetValue(w, out old))
                {
                    if (VisualTreeHelper.GetParent((DependencyObject)old) != null) return; // still there
                    Gears.Remove(w);
                    RemoveLookButton(w);
                }
                string label;
                if (MatchPattern(w, out label) == null) { GearNote(w, "no pattern of WindowPatterns matches it; names tried: " + string.Join(" | ", Candidates(w).ToArray())); return; }
                var win = (FrameworkElement)w;
                var thumb = Get(w, "HeaderThumb") as FrameworkElement;
                if (thumb == null || !thumb.IsVisible) { GearNote(w, "no title bar yet (HeaderThumb " + (thumb == null ? "missing" : "not visible") + ")"); return; } // not built yet: tried again when recognised
                var panel = TemplateRootPanel(w);
                if (panel == null) { Trace("gear: no panel at the root of " + Describe(w)); return; }
                if (thumb.ActualWidth < 1f) win.UpdateLayout();
                float th = thumb.ActualHeight, tw = thumb.ActualWidth;
                if (th < 8f || tw < 30f) { Trace("gear: header of " + Describe(w) + " not laid out (" + tw + "x" + th + ")"); return; }
                var tp = thumb.TranslatePoint(new Point(0f, 0f), panel);
                float tx = tp.X, ty = tp.Y, mid = ty + th / 2f, right = tx + tw;

                // the title bar's own buttons (close, maximize...), by position in the panel
                var found = new System.Text.StringBuilder();
                var stack = new Stack<KeyValuePair<DependencyObject, int>>();
                stack.Push(new KeyValuePair<DependencyObject, int>(panel, 0));
                int budget = 1500;
                while (stack.Count > 0 && --budget > 0)
                {
                    var item = stack.Pop();
                    var e = item.Key;
                    var fe = e as FrameworkElement;
                    if (!ReferenceEquals(e, panel) && fe != null)
                    {
                        if (fe.Visibility != Visibility.Visible) continue;
                        Point p;
                        try { p = fe.TranslatePoint(new Point(0f, 0f), panel); } catch { continue; }
                        if (p.Y >= ty + th) continue; // starts below the title bar (the window's content): nothing of the header inside
                        if (fe is ButtonBase)
                        {
                            float ew = fe.ActualWidth, eh = fe.ActualHeight;
                            if (ew > 0f && eh > 0f && p.Y <= mid && p.Y + eh >= mid && p.Y >= ty - 2f && p.Y + eh <= ty + th + 2f && p.X > tx + tw * 0.3f && p.X < right)
                            {
                                right = p.X;
                                found.Append(" " + fe.GetType().Name + "@" + Fmt(p.X) + "+" + Fmt(ew));
                            }
                            continue; // nothing to look for inside a button
                        }
                    }
                    if (item.Value < 12)
                        for (int k = VisualTreeHelper.GetChildrenCount(e) - 1; k >= 0; k--)
                        {
                            var c = VisualTreeHelper.GetChild(e, k);
                            if (c != null) stack.Push(new KeyValuePair<DependencyObject, int>(c, item.Value + 1));
                        }
                }

                const float size = 14f;
                float pw = panel.ActualWidth;
                var gear = new Border
                {
                    Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0f, ty + (th - size) / 2f, Math.Max(0f, pw - right) + 6f, 0f),
                    Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Opacity = 0.9f,
                    Child = new Path
                    {
                        Data = new StreamGeometry("F0 M10.3,2 L13.7,2 L14.2,4.6 C14.8,4.8 15.4,5.1 15.9,5.5 L18.4,4.6 L20.1,7.5 L18.1,9.2 C18.2,9.8 18.2,10.4 18.1,11 " +
                                                  "L20.1,12.7 L18.4,15.6 L15.9,14.7 C15.4,15.1 14.8,15.4 14.2,15.6 L13.7,18.2 L10.3,18.2 L9.8,15.6 C9.2,15.4 8.6,15.1 8.1,14.7 " +
                                                  "L5.6,15.6 L3.9,12.7 L5.9,11 C5.8,10.4 5.8,9.8 5.9,9.2 L3.9,7.5 L5.6,4.6 L8.1,5.5 C8.6,5.1 9.2,4.8 9.8,4.6 Z " +
                                                  "M15.4,10.1 A3.4,3.4 0 1 1 8.6,10.1 A3.4,3.4 0 1 1 15.4,10.1 Z"),
                        Fill = new SolidColorBrush(GearGrey), Stretch = Stretch.Uniform, Width = size, Height = size
                    }
                };
                gear.MouseEnter += (s, e) => gear.Opacity = 1f;
                gear.MouseLeave += (s, e) => gear.Opacity = 0.9f;
                try { gear.Cursor = Cursors.Hand; ToolTipService.SetToolTip(gear, Loc.T("tip.gear")); } catch { }
                var owner = new WeakReference(w);
                gear.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (!Enabled) return;
                    var ow = owner.Target;
                    Trace("settings gear clicked");
                    // the Unit Reference / Unit Status tabs are shown only when the window is opened from such a window
                    SettingsWindow.Open(this, gear, ow != null && InfoViewModel(ow) != null, ow != null && IsUnitStatusWindow(ow), ow != null && IsTaskForceBuilderWindow(ow), ow != null && TypeName(Get(ow, "DataContext")) == "SceneMapViewModel", ow != null && TypeName(Get(ow, "DataContext")) == "EventLogViewModel");
                };
                Panel.SetZIndex(gear, 100001);
                var grid = panel as Grid;
                if (grid != null)
                {
                    Grid.SetRowSpan(gear, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(gear, Math.Max(1, grid.ColumnDefinitions.Count));
                }
                panel.Children.Add(gear);
                Gears.Add(w, gear);
                AddLookButton(w, panel, ty + (th - size) / 2f, Math.Max(0f, pw - right) + 6f, size);
                Trace("gear added to '" + TextOf(Get(w, "Header")) + "': header " + Fmt(tw) + "x" + Fmt(th) + " at " + Fmt(tx) + "," + Fmt(ty) +
                      " in " + Describe(panel) + " " + Fmt(pw) + " wide; right edge " + Fmt(right) + (found.Length > 0 ? "; buttons" + found : "; no header buttons found"));
            }
            catch (Exception ex) { Trace("EnsureGear: " + ex); }
        }

        static readonly ConditionalWeakTable<object, object> GearNotes = new ConditionalWeakTable<object, object>();

        /// <summary>Debug trace: why a window got no gear (once per reason and window).</summary>
        void GearNote(object w, string why)
        {
            object seen;
            if (GearNotes.TryGetValue(w, out seen) && (string)seen == why) return;
            GearNotes.Remove(w); GearNotes.Add(w, why);
            Trace("gear: '" + TextOf(Get(w, "Header")) + "': " + why);
        }

        /// <summary>A Unit Status window: one of its identifying strings is 'Unit Status...' (header, UnitStatusView, UnitStatusViewModel).</summary>
        bool IsTaskForceBuilderWindow(object w)
        {
            foreach (var c in Candidates(w))
                if (GlobMatch("LinearCampaignTaskForceBuilder*", Squash(c))) return true;
            return false;
        }

        bool IsUnitStatusWindow(object w)
        {
            foreach (var c in Candidates(w))
                if (GlobMatch("UnitStatus*", Squash(c))) return true;
            return false;
        }

        static string Fmt(float v) { return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }

        static void RemoveFromParent(object e)
        {
            var d = e as DependencyObject;
            var parent = d == null ? null : VisualTreeHelper.GetParent(d) as Panel;
            try { if (parent != null) parent.Children.Remove((UIElement)d); } catch { }
        }

        void RemoveGear(object w)
        {
            object g;
            if (w == null || !Gears.TryGetValue(w, out g)) return;
            RemoveFromParent(g);
            Gears.Remove(w);
            RemoveLookButton(w);
        }

        /// <summary>SettingsButton or WindowPatterns changed: gears on the windows on screen follow.</summary>
        void RefreshGears()
        {
            foreach (var t in _tracked)
            {
                var w = t.Ref.Target;
                if (w == null || !IsOnScreen(w)) continue;
                string label;
                if (MatchPattern(w, out label) == null) RemoveGear(w);
                else EnsureGear(w);
            }
        }

        /// <summary>[WindowBorder] changed: the frames of the windows on screen are drawn again.</summary>
        void RefreshBorders()
        {
            _borderXaml = null;
            for (int i = _borderOwners.Count - 1; i >= 0; i--)
            {
                var w = _borderOwners[i].Target;
                object b;
                if (w == null || !Borders.TryGetValue(w, out b)) { _borderOwners.RemoveAt(i); continue; }
                RemoveFromParent(b);
                Borders.Remove(w);
                _borderOwners.RemoveAt(i);
            }
            if (!_borderOn.Value) return;
            foreach (var t in _tracked)
            {
                var w = t.Ref.Target;
                if (w != null && IsOnScreen(w)) AddBorder(w);
            }
        }
    }
}
