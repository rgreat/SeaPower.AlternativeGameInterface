using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [Map] FormationLines: with a unit of a formation selected, lines are drawn on the map from the leader to every unit of the formation (the units within the
    /// spread limit of the leader, see MapLabelFilter.cs). Drawn by the mod: a Canvas with Line shapes is a child of the game's MapLabelLayer (a Panel that measures
    /// all its children) and is arranged over the whole layer at every ArrangeLabels, using the pixel positions of the icon labels (_active / _anchorPx), which are in
    /// the layer's own coordinates. Stroke / thickness: the game's membership line (resources Map_FormationMembershipLine, FormationMembershipLineThickness).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _glLines;
        sealed class GlLineSet { public Noesis.Canvas Canvas; public readonly List<Noesis.Line> Lines = new List<Noesis.Line>(); }
        static readonly ConditionalWeakTable<object, GlLineSet> GlLineSets = new ConditionalWeakTable<object, GlLineSet>();
        static bool _glLinesFailed;

        void BindFormationLinesConfig()
        {
            _glLines = Config.Bind("Map", "FormationLines", true, "Map: with a unit of a formation selected, lines are drawn from the leader to every unit of the formation.");
        }

        static void LinesPrefix(object __instance, Noesis.Size finalSize)
        {
            var a = Active;
            if ((object)a == null || !_glHooked || _glLinesFailed || a._glLines == null) return;
            try { a.LinesStep(__instance, finalSize); }
            catch (Exception e) { _glLinesFailed = true; Trace("formation lines: " + e.Message + " (switched off until the game is restarted)"); }
        }

        void LinesStep(object layer, Noesis.Size fs)
        {
            GlLineSet gl; bool have = GlLineSets.TryGetValue(layer, out gl);
            if (!_glLines.Value || !MapImp)
            {
                if (have && gl.Canvas.Visibility != Noesis.Visibility.Collapsed) gl.Canvas.Visibility = Noesis.Visibility.Collapsed;
                return;
            }
            var active = _glActive.GetValue(layer) as IList; var px = _glPx.GetValue(layer) as IList;
            if (active == null || px == null || active.Count != px.Count) return;
            var pairs = new List<float[]>();
            var reg = _gcRegistry.GetValue(layer, null);
            var groups = reg == null || _grGroups == null ? null : _grGroups.GetValue(reg, null) as IEnumerable;
            if (groups != null)
            {
                Dictionary<object, int> iconAt = null;                                       // unit view model -> index of its icon label in the active list
                foreach (var gr in groups)
                {
                    var key = _ggKey.GetValue(gr, null); if (key == null || key.GetType().Name != "UnitFormation") continue;
                    var mem = _gmMembers.GetValue(gr) as IList; if (mem == null || mem.Count < 2) continue;
                    var near = GlNearSet(gr);
                    object lead = null; GlName nm;
                    if (_glNames.TryGetValue(gr, out nm)) lead = nm.Leader;
                    if (lead == null) for (int k = 0; k < mem.Count; k++) { var l = mem[k]; if (l != null && !GlIcon(l) && _gcHead.GetValue(l) != null) { lead = l; break; } }
                    if (lead == null || !GlGroupSel(gr)) continue;
                    if (iconAt == null)
                    {
                        iconAt = new Dictionary<object, int>();
                        for (int i = 0; i < active.Count; i++) if (GlIcon(active[i])) { var c = _gcContent.GetValue(active[i], null); if (c != null) iconAt[c] = i; }
                    }
                    int li; var lvm = _gcContent.GetValue(lead, null);
                    if (lvm == null || !iconAt.TryGetValue(lvm, out li)) continue;
                    var lp = px[li]; float x0 = (float)_f2x.GetValue(lp), y0 = (float)_f2y.GetValue(lp);
                    for (int k = 0; k < mem.Count; k++)
                    {
                        var l = mem[k]; if (l == null || GlIcon(l) || l == lead || (near != null && !near.Contains(l))) continue;
                        int mi; var mvm = _gcContent.GetValue(l, null);
                        if (mvm == null || !iconAt.TryGetValue(mvm, out mi)) continue;
                        var mp = px[mi];
                        pairs.Add(new[] { x0, y0, (float)_f2x.GetValue(mp), (float)_f2y.GetValue(mp) });
                    }
                }
            }
            if (pairs.Count == 0) { if (have && gl.Canvas.Visibility != Noesis.Visibility.Collapsed) gl.Canvas.Visibility = Noesis.Visibility.Collapsed; return; }      // nothing to draw: no resources, no layout
            if (!have)
            {
                var panel = layer as Noesis.Panel; if (panel == null) return;
                gl = new GlLineSet { Canvas = new Noesis.Canvas { IsHitTestVisible = false } };
                panel.Children.Add(gl.Canvas);
                GlLineSets.Add(layer, gl);
            }
            var brush = ((Noesis.FrameworkElement)layer).TryFindResource("Map_FormationMembershipLine") as Noesis.Brush ?? new Noesis.SolidColorBrush(Noesis.Color.FromRgb(170, 190, 255));
            float th = 1f; var tr = ((Noesis.FrameworkElement)layer).TryFindResource("FormationMembershipLineThickness");
            if (tr is float) th = (float)tr; else if (tr is double) th = (float)(double)tr;
            while (gl.Lines.Count < pairs.Count)
            {
                var ln = new Noesis.Line { IsHitTestVisible = false };
                gl.Canvas.Children.Add(ln); gl.Lines.Add(ln);
            }
            for (int i = 0; i < gl.Lines.Count; i++)
            {
                var ln = gl.Lines[i];
                if (i >= pairs.Count) { if (ln.Visibility != Noesis.Visibility.Collapsed) ln.Visibility = Noesis.Visibility.Collapsed; continue; }
                var p = pairs[i];
                ln.X1 = p[0]; ln.Y1 = p[1]; ln.X2 = p[2]; ln.Y2 = p[3]; ln.Stroke = brush; ln.StrokeThickness = th;
                if (ln.Visibility != Noesis.Visibility.Visible) ln.Visibility = Noesis.Visibility.Visible;
            }
            gl.Canvas.Visibility = Noesis.Visibility.Visible;
            gl.Canvas.Measure(fs);
            gl.Canvas.Arrange(new Noesis.Rect(0f, 0f, fs.Width, fs.Height));
        }
    }
}
