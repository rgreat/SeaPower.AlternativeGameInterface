using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// [Map] GroupNameOnly / LabelOverlapFilter: two filters of the unit text labels of the Tactical Display, run inside the game's own label pipeline.
    /// MapLabelLayer.ArrangeOverride calls Gate1 (the list _active of the labels that are on screen, with _anchorPx in the same order) and then clusters,
    /// measures and solves only the labels of that list; the postfix of Gate1 takes labels out of it (Eligible = false, ActiveIndex = -1, like the game does for
    /// a label that is off screen) and renumbers the rest. No timer, no walk of the visual tree.
    /// 1. GroupNameOnly: the labels of the units of an unselected formation are dropped (aircraft) or put last (ships: drawn only where there is room); the formation name stays. In a selected formation
    ///    of aircraft only the selected aircraft keeps its label.
    /// 2. LabelOverlapFilter: the text labels are placed in order (selected unit, leaders / names, units, ...; the biggest first) on the chosen side, the opposite one, then the other two; the lines
    ///    are cut from the bottom until the box fits among the boxes already placed and the drawn icons; a label that does not fit at all is dropped. A decision holds for 1 s.
    /// Lines are cut per label, never in the text: the natural sizes of the lines come from the game's own measure (ReadSizes postfix; the hidden host measures a label again when the shape of its
    /// text changes), and the cut is applied to LineSizes (the box) and to the presenter's _empty flags (its SetPrefix collapses those lines).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _glGroup;
        ConfigEntry<bool> _glOverlap;
        ConfigEntry<string> _glPlace;
        ConfigEntry<float> _glSpread, _glSpreadAir;
        static string _glPlaceDone;                                  // the placement the descriptor holds now (null: the game's own, not touched yet)
        static object _glContactDescr;
        static PropertyInfo _gdSide, _gcRegistry, _grLabels;
        static FieldInfo _gcNeedsSeed;
        static object _glGameSide;

        /// <summary>The last placement decision of a label (hysteresis: it holds for 1 s).</summary>
        sealed class GlDec { public int C, Si, Sh; public bool Drop; public float T; }
        static readonly ConditionalWeakTable<object, GlDec> GlDecs = new ConditionalWeakTable<object, GlDec>();
        /// <summary>The natural sizes of the lines of a label (all lines, as the game measured them in ReadSizes) and the shape of the unit text they were measured with (per contact line: length, line breaks).</summary>
        sealed class GlNat { public float[] W, H; public int[] Shape; public int Gen; }
        /// <summary>The cut applied to a label now: bit q = contact line q is not drawn (its size is zero in LineSizes, its presenter line is collapsed).</summary>
        sealed class GlCutState { public int Applied; }
        static readonly ConditionalWeakTable<object, GlCutState> GlCuts = new ConditionalWeakTable<object, GlCutState>();
        static readonly Dictionary<object, int> _glCut = new Dictionary<object, int>();      // label -> the cut wanted in this pass (rebuilt by every filter pass)
        sealed class GlSize { public float Size; public float At; }
        static readonly ConditionalWeakTable<object, GlNat> GlNats = new ConditionalWeakTable<object, GlNat>();      // label -> its natural line sizes
        static readonly ConditionalWeakTable<object, GlSize> GlSizes = new ConditionalWeakTable<object, GlSize>();
        static FieldInfo _glActive, _glPx;
        static FieldInfo _gcEligible, _gcRealized, _gcActiveIndex, _gcRep, _gcRepScore, _gcHead, _gcLineSizes, _gcViewPx, _gcAggOf, _gmMembers;
        static PropertyInfo _gcGroup, _gcContent, _gcDescr, _gdIcon, _ggKey;
        static FieldInfo _f2x, _f2y;
        static bool _glHooked, _glFailed, _glNamesOk;
        static FieldInfo _gcHeadContent, _gcAnchor, _gcSlot, _gcIndex;
        static MethodInfo _gcBump, _grUnreg, _grSetAnchor, _grRegister;
        static PropertyInfo _grGroups;

        // ---- the name of a formation as a label of its own (with a side other than Game): the game's name is the HeadLine of the leader's label, so the leader would lose
        // its own text when the name moves around the formation. The HeadLine is taken off the leader's label and a second candidate (X) with the same line and
        // content (the MapFormationViewModel) is registered; X is placed around the formation, the leader's label keeps only the leader's lines at the leader.
        sealed class GlName { public object X, Leader, Line, Vm, XVm; public int Form; }
        static readonly Dictionary<object, GlName> _glNames = new Dictionary<object, GlName>();       // MapLabelGroup -> its name (of the registry _glNamesReg)
        static object _glNamesReg;
        static readonly HashSet<object> _glXs = new HashSet<object>();
        static readonly Dictionary<object, object> _glXGroup = new Dictionary<object, object>();      // X -> MapLabelGroup
        static readonly Dictionary<object, float> _glShift = new Dictionary<object, float>();          // name label -> extra distance chosen by the overlap filter

        static object GlGroupOf(object lab) { object g; return _glXs.Contains(lab) && _glXGroup.TryGetValue(lab, out g) ? g : _gcGroup.GetValue(lab, null); }
        static PropertyInfo _glLinesP;
        /// <summary>The number of lines a label has now: the head line and the lines of its descriptor (a unit label 4, the name label X 1).</summary>
        static int GlLineCount(object lab, bool head)
        {
            try
            {
                var d = _gcDescr.GetValue(lab, null); if (d == null) return -1;
                if (_glLinesP == null) _glLinesP = d.GetType().GetProperty("Lines");
                var l = _glLinesP == null ? null : _glLinesP.GetValue(d, null) as ICollection;
                return l == null ? -1 : (head ? 1 : 0) + l.Count;
            }
            catch { return -1; }
        }
        static bool GlAirInfoOn() { var a = Active; return (object)a != null && a._glAirInfo != null && a._glAirInfo.Value && MapImp; }
        static object _glNameDescr0, _glNameDescr1; static MethodInfo _grSetHead;
        /// <summary>The descriptor of the name label X: form 0 = the name line only (the formation is its content); form 1 (a formation of aircraft) = the four contact lines of the Contact descriptor, content = the leader's view model, the name is the head line: like the game's own leader label; the filter cuts its contact lines (all of them, or all but line 3 = distance and altitude).</summary>
        static object GlNameDescr(int form)
        {
            if (form == 0 ? _glNameDescr0 != null : _glNameDescr1 != null) return form == 0 ? _glNameDescr0 : _glNameDescr1;
            var dt = _gcDescr.PropertyType; var d = Activator.CreateInstance(dt);
            dt.GetProperty("AnchorRadius").SetValue(d, 12f, null);
            dt.GetProperty("PreferredSide").SetValue(d, _gdSide.GetValue(_glContactDescr, null), null);
            var pol = dt.GetProperty("Policy"); pol.SetValue(d, Enum.ToObject(pol.PropertyType, 0), null);
            dt.GetProperty("KernelReach").SetValue(d, 0f, null);
            var lines = (IList)dt.GetProperty("Lines").GetValue(d, null);
            if (form == 0) lines.Add(_glNameLine);
            else foreach (var cl in (IList)dt.GetProperty("Lines").GetValue(_glContactDescr, null)) lines.Add(cl);
            if (form == 0) _glNameDescr0 = d; else _glNameDescr1 = d;
            return d;
        }
        static object _glNameLine;
        /// <summary>A unit of the formation (within the spread limit of its leader) is selected: the one rule for the name, the filters and the formation lines.</summary>
        static bool GlGroupSel(object gr)
        {
            var mem = _gmMembers.GetValue(gr) as IList; if (mem == null) return false;
            for (int k = 0; k < mem.Count; k++) { var l = mem[k]; if (l != null && !GlIcon(l) && GlNear(gr, l) && GlSelected(_gcContent.GetValue(l, null))) return true; }
            return false;
        }
        static bool GlIsName(object lab) { return _glXs.Contains(lab) || _gcHead.GetValue(lab) != null; }

        // ---- the units of a formation that count as in it: those within [Map] GroupSpreadMiles of the leader; the farther ones are independent units. Per frame cache.
        static int _glNearFrame = -1;
        static readonly Dictionary<object, HashSet<object>> _glNear = new Dictionary<object, HashSet<object>>();

        /// <summary>The member labels (text and icon) of the formation within the limit of the leader's label; null when no leader label is known (then every member counts).</summary>
        static HashSet<object> GlNearSet(object gr)
        {
            int fr = UnityEngine.Time.frameCount;
            if (fr != _glNearFrame) { _glNearFrame = fr; _glNear.Clear(); }
            HashSet<object> set;
            if (_glNear.TryGetValue(gr, out set)) return set;
            var mem = _gmMembers.GetValue(gr) as IList;
            object lead = null; GlName nm;
            if (_glNames.TryGetValue(gr, out nm)) lead = nm.Leader;
            if (lead == null && mem != null) for (int k = 0; k < mem.Count; k++) if (mem[k] != null && !GlIcon(mem[k]) && _gcHead.GetValue(mem[k]) != null) { lead = mem[k]; break; }
            if (lead != null && mem != null && (bool)_gcAnchorHas(lead))
            {
                set = new HashSet<object>(); var la = _gcAnchor.GetValue(lead);
                float lim = _inst == null || _inst._glSpread == null ? 5f : _inst._glSpread.Value, limAir = _inst == null || _inst._glSpreadAir == null ? 1f : _inst._glSpreadAir.Value;
                for (int k = 0; k < mem.Count; k++)
                {
                    var l = mem[k]; if (l == null) continue;
                    if (!(bool)_gcAnchorHas(l)) { set.Add(l); continue; }
                    var vt = FieldV(_gcContent.GetValue(l, null), "VisualType"); int vi = vt == null ? 0 : Convert.ToInt32(vt);     // MapVisualType: 1 Air, 2 Helicopter
                    if (GlNm(_gcAnchor.GetValue(l), la) <= (vi == 1 || vi == 2 ? limAir : lim)) set.Add(l);
                }
            }
            _glNear[gr] = set;
            return set;
        }

        static bool GlNear(object gr, object lab) { var set = GlNearSet(gr); return set == null || set.Contains(lab); }

        static int GlUnits(object gr)
        {
            var mem = _gmMembers.GetValue(gr) as IList; int n = 0;
            if (mem != null) for (int k = 0; k < mem.Count; k++) if (mem[k] != null && !GlIcon(mem[k])) n++;
            return n;
        }

        static void GlNameRemove(object reg, GlName nm)
        {
            if (nm.X != null) { try { _grUnreg.Invoke(reg, new[] { nm.X }); } catch { } _glXs.Remove(nm.X); _glXGroup.Remove(nm.X); GlGroupExt.Remove(nm.X); nm.X = null; }
        }

        /// <summary>Called after every Gate1 (and when the option changes). on = a side other than Game is chosen.</summary>
        static void GlNamesSync(object layer, bool on)
        {
            if (!_glNamesOk) return;
            var reg = _gcRegistry.GetValue(layer, null); if (reg == null) return;
            if (!ReferenceEquals(reg, _glNamesReg)) { _glNames.Clear(); _glXs.Clear(); _glXGroup.Clear(); _glNamesReg = reg; }      // another registry (another map): the old entries belong to it
            if (!on)
            {
                if (_glNames.Count == 0) return;
                foreach (var nm in _glNames.Values)
                {
                    GlNameRemove(reg, nm);
                    if (nm.Leader != null && (int)_gcIndex.GetValue(nm.Leader) >= 0 && _gcHead.GetValue(nm.Leader) == null) { _gcHead.SetValue(nm.Leader, nm.Line); _gcBump.Invoke(nm.Leader, null); }
                }
                _glNames.Clear(); return;
            }
            var groups = _grGroups.GetValue(reg, null) as IEnumerable; if (groups == null) return;
            _glGroupsNow.Clear(); foreach (var g in groups) _glGroupsNow.Add(g);
            if (_glNames.Count > 0)                                                               // the groups the registry no longer has (cleared at the end of a scenario): forget them
            {
                _glGone.Clear(); foreach (var k in _glNames.Keys) if (!_glGroupsNow.Contains(k)) _glGone.Add(k);
                foreach (var k in _glGone) { GlNameRemove(reg, _glNames[k]); _glNames.Remove(k); }
            }
            foreach (var gr in _glGroupsNow)
            {
                var key = _ggKey.GetValue(gr, null); if (key == null || key.GetType().Name != "UnitFormation") continue;
                var mem = _gmMembers.GetValue(gr) as IList; if (mem == null) continue;
                object head = null;
                for (int k = 0; k < mem.Count; k++) { var l = mem[k]; if (l == null || GlIcon(l)) continue; if (_gcHead.GetValue(l) != null) head = l; }
                GlName nm; _glNames.TryGetValue(gr, out nm);
                if (head != null)                                                             // the game (re)set the name: take it off the leader's label
                {
                    if (nm == null) { nm = new GlName(); _glNames[gr] = nm; }
                    nm.Leader = head; nm.Line = _gcHead.GetValue(head); nm.Vm = _gcHeadContent.GetValue(head);
                    _gcHead.SetValue(head, null); _gcBump.Invoke(head, null);
                    _glNearFrame = -1;                                                           // the leader is known now: the near set of this frame is built again
                }
                if (nm == null) continue;
                int units = GlUnits(gr);                                                         // all the units of the formation: one unit - no name
                if (units < 2 || nm.Vm == null || nm.Leader == null || (int)_gcIndex.GetValue(nm.Leader) < 0) { GlNameRemove(reg, nm); continue; }     // one unit: no formation name
                // the name label X: form 0 = the name only; form 1 (a formation of aircraft) = the name over the leader's contact lines, which the filter cuts (it keeps line 3 for an unselected formation).
                // The form does not follow the selection; X is made again only when the form or the leader changes.
                object lvm = _gcContent.GetValue(nm.Leader, null);
                int form = lvm != null && lvm.GetType().Name == "MapUnitViewModel" && _grSetHead != null && GlAirInfoOn() && GlAirLabel(nm.Leader) ? 1 : 0;
                if (nm.X != null && (nm.Form != form || (form == 1 && !ReferenceEquals(nm.XVm, lvm)))) GlNameRemove(reg, nm);
                if (nm.X == null)
                {
                    if (_glNameLine == null) _glNameLine = nm.Line;
                    var d = GlNameDescr(form);
                    nm.X = _grRegister.Invoke(reg, new object[] { d, form == 1 ? lvm : nm.Vm, null });
                    if (form == 1) _grSetHead.Invoke(reg, new object[] { nm.X, nm.Line, nm.Vm });         // the name is the head line, its content is the formation; the lines are the leader's
                    nm.Form = form; nm.XVm = form == 1 ? lvm : null;
                    _glXs.Add(nm.X); _glXGroup[nm.X] = gr;
                }
                if ((bool)_gcAnchorHas(nm.Leader)) _grSetAnchor.Invoke(reg, new[] { nm.X, _gcAnchor.GetValue(nm.Leader) });
            }
        }
        static readonly HashSet<object> _glGroupsNow = new HashSet<object>(); static readonly List<object> _glGone = new List<object>();
        static FieldInfo _gcHasAnchor;
        static object _gcAnchorHas(object lab) { if (_gcHasAnchor == null) _gcHasAnchor = lab.GetType().GetField("HasAnchor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); return _gcHasAnchor == null ? (object)false : _gcHasAnchor.GetValue(lab); }

        ConfigEntry<bool> _mapImp, _glBadge, _glAirInfo;
        /// <summary>[Map] DrawingImprovements: the master switch of the map drawing options (sonobuoys, label filters / placement, formation lines, contact ellipses).</summary>
        static bool MapImp { get { var a = Active; return (object)a == null || a._mapImp == null || a._mapImp.Value; } }
        void BindLabelFilterConfig()
        {
            _glAirInfo = Config.Bind("Map", "AirGroupInfo", true, "Map: the label of an unselected formation of aircraft (helicopters) shows, under the name, the distance and the altitude of its leader.");
            _glBadge = Config.Bind("Map", "FriendlyBadgeInLabel", true,
                "Map: the 'FRND' badge of a friendly unit of another side is put in front of the first line of its label, not drawn beside the icon.");
            _mapImp = Config.Bind("Map", "DrawingImprovements", true,
                "Map: the master switch of the improved drawing of units and labels (compact sonobuoys, label placement and filters, formation lines, cut contact ellipses).");
            _glGroup = Config.Bind("Map", "GroupNameOnly", true,
                "Map: the units of a formation show only the name of the formation until the formation is selected.");
            _glOverlap = Config.Bind("Map", "LabelOverlapFilter", true,
                "Map: when the labels of ships or aircraft (and formation names) overlap, only the label of the biggest unit or formation is left.");
        }

        void BindLabelPlaceConfig()
        {
            _glSpreadAir = Config.Bind("Map", "GroupSpreadMilesAir", 1f, new ConfigDescription("Map: the same for aircraft and helicopters: a unit of the formation that flies farther than this many nautical miles from its leader is an independent unit.", new AcceptableValueRange<float>(0.1f, 100f)));
            _glSpread = Config.Bind("Map", "GroupSpreadMiles", 5f, new ConfigDescription("Map: a unit of the formation (ships, ground units) farther than this many nautical miles from its leader is an independent unit: its own label, not in the box the formation name is put around.", new AcceptableValueRange<float>(0.5f, 100f)));
            _glPlace = Config.Bind("Map", "LabelPlacement", "Bottom",
                new ConfigDescription("Map: where the text label of a unit is put relative to its icon (it can still move aside when crowded). Game (the game's own: right), Bottom, Top, Right, Left; centred on that side.",
                    new AcceptableValueList<string>("Game", "Bottom", "Top", "Right", "Left")));
        }

        void HookLabelFilter(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type layer = null, cand = null, grp = null;
            foreach (var t in SafeTypes(asm))
            {
                if (t.Name == "MapLabelLayer") layer = t;
                else if (t.Name == "MapLabelCandidate") cand = t;
                else if (t.Name == "MapLabelGroup") grp = t;
            }
            if (layer == null || cand == null || grp == null) { Trace("label filter: label types not found (GroupNameOnly / LabelOverlapFilter not available)"); return; }
            var gate = layer.GetMethod("Gate1", all);
            _glActive = layer.GetField("_active", all); _glPx = layer.GetField("_anchorPx", all);
            _gcEligible = cand.GetField("Eligible", all); _gcRealized = cand.GetField("Realized", all); _gcActiveIndex = cand.GetField("ActiveIndex", all);
            _gcRep = cand.GetField("Representative", all); _gcRepScore = cand.GetField("RepScore", all); _gcHead = cand.GetField("HeadLine", all);
            _gcLineSizes = cand.GetField("LineSizes", all); _gcViewPx = cand.GetField("ViewPx", all); _gcAggOf = cand.GetField("AggregateOf", all);
            _gcGroup = cand.GetProperty("Group", all); _gcContent = cand.GetProperty("Content", all); _gcDescr = cand.GetProperty("Descriptor", all);
            _gcNeedsSeed = cand.GetField("NeedsSeed", all); _gcRegistry = layer.GetProperty("Registry", all);
            _grLabels = _gcRegistry == null ? null : _gcRegistry.PropertyType.GetProperty("Labels", all);
            Type descs = null; foreach (var t in SafeTypes(asm)) if (t.Name == "MapLabelDescriptors") { descs = t; break; }
            var cf = descs == null ? null : descs.GetField("Contact", all);
            _glContactDescr = cf == null ? null : cf.GetValue(null);
            _gdSide = _glContactDescr == null ? null : _glContactDescr.GetType().GetProperty("PreferredSide", all);
            if (_gdSide != null) _glGameSide = _gdSide.GetValue(_glContactDescr, null);
            _gcHeadContent = cand.GetField("HeadContent", all); _gcAnchor = cand.GetField("Anchor", all); _gcSlot = cand.GetField("Slot", all); _gcIndex = cand.GetField("Index", all);
            _gcBump = cand.GetMethod("BumpContentVersion", all);
            var regT = _gcRegistry == null ? null : _gcRegistry.PropertyType;
            if (regT != null)
            {
                _grGroups = regT.GetProperty("Groups", all); _grUnreg = regT.GetMethod("Unregister", all);
                _grSetAnchor = regT.GetMethod("SetAnchor", all);
                foreach (var m in regT.GetMethods(all)) { if (m.Name == "Register" && m.GetParameters().Length == 3) _grRegister = m; if (m.Name == "SetHeadLine" && m.GetParameters().Length == 3) _grSetHead = m; }
            }
            _glNamesOk = _gcHeadContent != null && _gcAnchor != null && _gcSlot != null && _gcIndex != null && _gcBump != null && _grGroups != null && _grUnreg != null && _grSetAnchor != null && _grRegister != null;
            if (!_glNamesOk) Trace("label filter: registry members for the formation name not found (the name stays on the leader's label)");
            _gmMembers = grp.GetField("Members", all); _ggKey = grp.GetProperty("Key", all);
            _gdIcon = _gcDescr == null ? null : _gcDescr.PropertyType.GetProperty("IsIcon", all);
            if (_gcViewPx != null) { _f2x = _gcViewPx.FieldType.GetField("x", all); _f2y = _gcViewPx.FieldType.GetField("y", all); }
            if (gate == null || _glActive == null || _glPx == null || _gcEligible == null || _gcRealized == null || _gcActiveIndex == null || _gcRep == null || _gcRepScore == null ||
                _gcHead == null || _gcLineSizes == null || _gcViewPx == null || _gcAggOf == null || _gcGroup == null || _gcContent == null || _gcDescr == null || _gdIcon == null ||
                _gmMembers == null || _ggKey == null || _f2x == null || _f2y == null)
            { Trace("label filter: a member of the label solver was not found (GroupNameOnly / LabelOverlapFilter not available)"); return; }
            TryPatch(harmony, gate, null, "LabelFilterPostfix", null);
            _glLegacy = layer.GetMethod("PlaceLegacy", all);
            if (_glLegacy != null)
            {
                var cl = layer.GetMethod("Cluster", all); var at = layer.GetMethod("Attribute", all); var so = layer.GetMethod("Solve", all);
                var rp = cand.GetProperty("AnchorRadiusPx", all);
                var rg = rp == null ? null : rp.GetGetMethod(true);
                if (rg != null) TryPatch(harmony, rg, null, "RadiusPostfix", null); else Trace("label placement: AnchorRadiusPx not found (distance as in the game)");
                if (layer.GetMethod("ArrangeLabels", all) == null) _glLegacy = null;
                if (cl != null) TryPatch(harmony, cl, "SolverPartPrefix", null, null);
                if (at != null) TryPatch(harmony, at, "SolverPartPrefix", null, null);
                if (so != null) TryPatch(harmony, so, "SolvePrefix", null, null);
                if (cl == null || at == null || so == null) _glLegacy = null;
            }
            var rs = layer.GetMethod("ReadSizes", all);
            _glPresF = layer.GetField("_presenters", all);
            if (rs != null && _glPresF != null) TryPatch(harmony, rs, null, "ReadSizesPostfix", null); else Trace("label filter: MapLabelLayer.ReadSizes not found (lines are not cut)");
            var arr = layer.GetMethod("ArrangeLabels", all);
            if (arr != null) TryPatch(harmony, arr, "ArrangePrefix", null, null);
            if (arr != null) TryPatch(harmony, arr, "LinesPrefix", null, null); else Trace("label filter: ArrangeLabels not found (FormationLines not available)");
            var mvmT = asm.GetType("SeapowerUI.MapUnitViewModel");
            var mtp = mvmT == null ? null : mvmT.GetProperty("RelationshipMarkerTop", all);
            var mts = mtp == null ? null : mtp.GetSetMethod(true);
            var mtg = mtp == null ? null : mtp.GetGetMethod(true);
            if (mtg != null) TryPatch(harmony, mtg, null, "MarkerTopGetPostfix", null);
            if (mts != null) TryPatch(harmony, mts, "MarkerTopPrefix", null, null); else Trace("label filter: RelationshipMarkerTop setter not found (the FRND badge stays at the text line)");
            Type pres = null;
            foreach (var t in SafeTypes(asm)) if (t.Name == "MapLabelPresenter") { pres = t; break; }
            var sla = pres == null ? null : pres.GetMethod("SetLineAlignment", all);
            _gpLabel = pres == null ? null : pres.GetField("Label", all);
            if (sla != null && _gpLabel != null) TryPatch(harmony, sla, "LineAlignPrefix", null, null); else Trace("label filter: MapLabelPresenter.SetLineAlignment not found (line alignment not available)");
            _glHooked = true;
            Trace("patched MapLabelLayer.Gate1 (GroupNameOnly / LabelOverlapFilter)");
        }

        static PropertyInfo _glFriendlyP;
        /// <summary>MapUnitViewModel.IsFriendlyOtherSide: the unit shows the 'FRND' badge left of its icon.</summary>
        static bool GlFriendlyMarker(object vm)
        {
            try
            {
                if (_glFriendlyP == null || _glFriendlyP.DeclaringType != vm.GetType()) _glFriendlyP = vm.GetType().GetProperty("IsFriendlyOtherSide", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var v = _glFriendlyP == null ? null : _glFriendlyP.GetValue(vm, null); return v is bool && (bool)v;
            }
            catch { return false; }
        }
        /// <summary>The game puts the badge at the height of the first text line (ArrangeLabels sets RelationshipMarkerTop); with a chosen label side that line is far from the icon, so the badge goes beside the icon.</summary>
        /// <summary>The game's own FRND badge (UnitOverlay.xaml, at Canvas.Top = RelationshipMarkerTop) is out of the picture for every unit while the badge is in the label: the setter is called only for labels that ArrangeLabels arranges.</summary>
        static void MarkerTopGetPostfix(ref float __result)
        {
            var a = Active;
            if (_glBadgeTplOn) __result = -10000f;                                     // the badge is drawn in the label
        }
        sealed class MarkSeen { public int Gen; public bool Friendly; }
        static readonly ConditionalWeakTable<object, MarkSeen> GlMarks = new ConditionalWeakTable<object, MarkSeen>();
        static int _glMarkGen; static bool _glMarkOn;
        /// <summary>The game's badge reads RelationshipMarkerTop when its binding is made and when the property raises a change; MarkerTopGetPostfix hides it for the units with the badge in the label, but a binding made before
        /// (or a unit that became a friendly one of another side later) keeps the value it read. So every unit of the active list gets one PropertyChanged: when it is first seen, when the option / master switch changes, and when it becomes (or stops being) a friendly unit of another side.</summary>
        static void GlMarkRefresh(object layer)
        {
            bool on = _glBadgeTplOn;
            if (on != _glMarkOn) { _glMarkOn = on; _glMarkGen++; }
            if (_sonoRaise == null) return;
            var act = _glActive.GetValue(layer) as IList; if (act == null) return;
            var arg = new object[] { "RelationshipMarkerTop" };
            for (int i = 0; i < act.Count; i++)
            {
                var vm = _gcContent.GetValue(act[i], null); if (vm == null || vm.GetType().Name != "MapUnitViewModel") continue;
                bool fr = GlFriendlyMarker(vm);
                MarkSeen m; if (!GlMarks.TryGetValue(vm, out m)) { m = new MarkSeen { Gen = -1 }; GlMarks.Add(vm, m); }
                if (m.Gen == _glMarkGen && m.Friendly == fr) continue;
                m.Gen = _glMarkGen; m.Friendly = fr;
                if (fr) try { _sonoRaise.Invoke(vm, arg); } catch { }
            }
        }
        static void MarkerTopPrefix(ref float value)
        {
            if (GlFixed()) value = 0f;
        }
        static string _glBadgeText; static float _glBadgeAt;
        /// <summary>The abbreviation of the badge (the game's mapfeaturesfriendlysideabbreviation, 'FRND'), read again every 5 s.</summary>
        static string GlBadgeText()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (_glBadgeText == null || now - _glBadgeAt > 5f) { _glBadgeAt = now; var a = Active; _glBadgeText = (object)a == null ? "FRND" : a.GameText("mapfeaturesfriendlysideabbreviation", "FRND"); if (string.IsNullOrEmpty(_glBadgeText)) _glBadgeText = "FRND"; }
            return _glBadgeText;
        }
        static object _glIdLine; static PropertyInfo _glLineTpl; static Noesis.DataTemplate _glBadgeTpl; static bool _glBadgeTplOn, _glBadgeTplFailed; static int _glNatGen; static MethodInfo _glUnrealize;
        /// <summary>The FRND badge in the label: the first contact line (MapLabelDescriptors.Contact.Lines[0], the template MapLabel.Contact.Id) gets a template of the mod: the badge over the id,
        /// the badge shown only for a friendly unit of another side (IsFriendlyOtherSide). The text of ContactInfoLine1 is not changed, so nothing else that shows it gets the badge. The game's Bind uses
        /// MapLabelLine.Template before TemplateKey; null gives the game's own back. On a change the presenters are taken back (the game realizes them again with the template) and every label is measured again.</summary>
        static void GlBadgeTemplateSync(object layer)
        {
            var a = Active;
            bool on = (object)a != null && a._glBadge != null && a._glBadge.Value && MapImp;
            if (on == _glBadgeTplOn) return;
            if (_glIdLine == null)
            {
                var lines = _glContactDescr == null ? null : _glContactDescr.GetType().GetProperty("Lines").GetValue(_glContactDescr, null) as IList;
                _glIdLine = lines == null || lines.Count == 0 ? null : lines[0];
                _glLineTpl = _glIdLine == null ? null : _glIdLine.GetType().GetProperty("Template");
                _glUnrealize = layer.GetType().GetMethod("Unrealize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (_glLineTpl == null) Trace("FRND badge: MapLabelLine.Template not found (the badge stays beside the icon)");
            }
            if (_glLineTpl == null) { _glBadgeTplOn = on; return; }
            if (on && _glBadgeTpl == null)
            {
                string t = System.Security.SecurityElement.Escape(GlBadgeText());
                _glBadgeTpl = (Noesis.DataTemplate)Noesis.GUI.ParseXaml(
                    "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                    "<StackPanel IsHitTestVisible=\"False\">" +
                    "<TextBlock Text=\"" + t + "\" FontSize=\"10\" Foreground=\"{Binding UnitColorBrush}\" IsHitTestVisible=\"False\"><TextBlock.Style><Style TargetType=\"TextBlock\">" +
                    "<Setter Property=\"Visibility\" Value=\"Collapsed\"/><Style.Triggers><DataTrigger Binding=\"{Binding IsFriendlyOtherSide}\" Value=\"True\"><Setter Property=\"Visibility\" Value=\"Visible\"/></DataTrigger></Style.Triggers>" +
                    "</Style></TextBlock.Style></TextBlock>" +
                    "<TextBlock Text=\"{Binding ContactInfoLine1}\" FontSize=\"10\" Foreground=\"{Binding UnitColorBrush}\" IsHitTestVisible=\"False\"/>" +
                    "</StackPanel></DataTemplate>");
            }
            _glLineTpl.SetValue(_glIdLine, on ? _glBadgeTpl : null, null);
            _glBadgeTplOn = on; _glNatGen++;
            if (_glUnrealize != null && _glPresF != null)
            {
                var pres = _glPresF.GetValue(layer) as IDictionary;
                if (pres != null) { var keys = new List<object>(); foreach (var k in pres.Keys) keys.Add(k); foreach (var k in keys) try { _glUnrealize.Invoke(layer, new[] { k }); } catch { } }
            }
        }
        static float _glDbgAt;
        static FieldInfo _gpLabel;
        /// <summary>The alignment of the lines of a text label, in one place: the blocks (the game's SetLineAlignment, called by ArrangeLabels for every presenter) and the text inside them (the templates of
        /// MapLabels.xaml fix TextAlignment=Left; a line of two rows, FRND over the id, shows it). Fixed placement: centred above / below the icon, right on the left side, left on the right side;
        /// otherwise the game's block alignment and the text left, as the templates have it.</summary>
        static void LineAlignPrefix(object __instance, ref Noesis.HorizontalAlignment alignment)
        {
            try
            {
                if (_gpLabel == null) return;
                var lab = _gpLabel.GetValue(__instance); if (lab == null || GlIcon(lab)) return;
                var ta = Noesis.TextAlignment.Left;
                if (GlFixed())
                {
                    int side = (int)_gcSlot.GetValue(lab);
                    alignment = side == 2 || side == 6 ? Noesis.HorizontalAlignment.Center : side >= 3 && side <= 5 ? Noesis.HorizontalAlignment.Right : Noesis.HorizontalAlignment.Left;
                    ta = alignment == Noesis.HorizontalAlignment.Center ? Noesis.TextAlignment.Center : alignment == Noesis.HorizontalAlignment.Right ? Noesis.TextAlignment.Right : Noesis.TextAlignment.Left;
                }
                var p = __instance as Noesis.Panel; if (p == null) return;
                int n = Noesis.VisualTreeHelper.GetChildrenCount(p);
                for (int i = 0; i < n; i++)
                {
                    var cp = Noesis.VisualTreeHelper.GetChild(p, i);
                    if (cp == null || Noesis.VisualTreeHelper.GetChildrenCount(cp) == 0) continue;
                    var root = Noesis.VisualTreeHelper.GetChild(cp, 0);
                    var tb = root as Noesis.TextBlock;
                    if (tb != null) { if (tb.TextAlignment != ta) tb.TextAlignment = ta; continue; }
                    var rp = root as Noesis.StackPanel; if (rp == null || rp.Orientation != Noesis.Orientation.Vertical) continue;      // the badge over the id (a horizontal panel, the formation summary, is left as it is)
                    int rn = Noesis.VisualTreeHelper.GetChildrenCount(rp);
                    for (int k = 0; k < rn; k++) { var t2 = Noesis.VisualTreeHelper.GetChild(rp, k) as Noesis.TextBlock; if (t2 != null && t2.TextAlignment != ta) t2.TextAlignment = ta; }
                }
            }
            catch { }
        }

        static void LabelFilterPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null || !_glHooked || _glFailed) return;
            if (GlFixed())
            {
                try
                {
                    var act = _glActive == null ? null : _glActive.GetValue(__instance) as IList;
                    if (act != null) for (int i = 0; i < act.Count && !_glNeedExt; i++)
                    {
                        var lab = act[i]; if (GlIcon(lab)) continue;
                        var vm = _gcContent.GetValue(lab, null); if (vm == null || vm.GetType().Name != "MapUnitViewModel") continue;
                        GlExt e; if (!GlExts.TryGetValue(vm, out e) || !Equals(e.Type, FieldV(vm, "VisualType")) || !Equals(e.Posture, FieldV(vm, "VisualPosture"))) _glNeedExt = true;
                    }
                }
                catch { }
            }
            try { GlNamesSync(__instance, GlFixed()); } catch (Exception e) { _glNamesOk = false; Trace("formation name: " + e.Message + " (the name stays on the leader's label)"); }
            try { a.LabelPlace(__instance); } catch (Exception e) { _glPlaceDone = a._glPlace == null ? null : a._glPlace.Value; Trace("label placement: " + e.Message); }
            bool g = MapImp && a._glGroup != null && a._glGroup.Value, o = MapImp && a._glOverlap != null && a._glOverlap.Value;
            _glSides.Clear();
            if (!o && _glShift.Count > 0) _glShift.Clear();
            if (!_glBadgeTplFailed) try { GlBadgeTemplateSync(__instance); } catch (Exception e) { _glBadgeTplFailed = true; Trace("FRND badge: " + e.Message + " (the badge stays beside the icon until the game is restarted)"); }
            try { GlMarkRefresh(__instance); } catch { }
            if (!g && !o) _glCut.Clear();
            else
            {
                try { LabelFilter(__instance, g, o); }
                catch (Exception e) { _glFailed = true; _glCut.Clear(); Trace("label filter: " + e.Message + " (switched off until the game is restarted)"); }
            }
            if (MapImp && !o)                                                           // without the overlap filter (which does it for its labels) the sizes still follow the text
                try { var act = _glActive.GetValue(__instance) as IList; if (act != null) for (int i = 0; i < act.Count; i++) if (!GlIcon(act[i])) GlSyncSizes(__instance, act[i]); }
                catch (Exception e) { if (!_glHostErr) { _glHostErr = true; Trace("label measure: " + e.GetType().Name + ": " + e.Message); } }
        }

        /// <summary>The preferred side of the Contact text labels (MapLabelDescriptors.Contact.PreferredSide: East, South, North, West); a change reseeds the slots of all labels.</summary>
        void LabelPlace(object layer)
        {
            if (_gdSide == null || _glPlace == null) return;
            string v = MapImp ? _glPlace.Value : "Game";
            if (v == (_glPlaceDone ?? "Game")) return;
            _glPlaceDone = v;
            object side = _glGameSide;
            if (v != "Game")
            {
                string n = v == "Bottom" ? "South" : v == "Top" ? "North" : v == "Left" ? "West" : "East";
                side = Enum.Parse(_gdSide.PropertyType, n);
            }
            _gdSide.SetValue(_glContactDescr, side, null);
            foreach (var nd in new[] { _glNameDescr0, _glNameDescr1 }) if (nd != null) nd.GetType().GetProperty("PreferredSide").SetValue(nd, side, null);
            var reg = _gcRegistry == null ? null : _gcRegistry.GetValue(layer, null);
            var all = reg == null || _grLabels == null ? null : _grLabels.GetValue(reg, null) as IEnumerable;
            if (all != null && _gcNeedsSeed != null) foreach (var l in all) if (l != null) _gcNeedsSeed.SetValue(l, true);
            Trace("label placement: " + v);
        }

        static bool GlIcon(object lab) { var d = _gcDescr.GetValue(lab, null); return d != null && (bool)_gdIcon.GetValue(d, null); }

        static bool GlSelected(object vm)
        {
            if (vm == null || vm.GetType().Name != "MapUnitViewModel") return false;
            return FieldV(vm, "Selected") is bool && (bool)FieldV(vm, "Selected");
        }

        /// <summary>Size of the unit of a view model: displacement of a vessel that is known (ours or identified), else 0. Kept for two seconds.</summary>
        static float GlUnitSize(object vm)
        {
            if (vm == null || vm.GetType().Name != "MapUnitViewModel") return 0f;
            float now = UnityEngine.Time.unscaledTime;
            GlSize s;
            if (!GlSizes.TryGetValue(vm, out s)) { s = new GlSize { At = -100f }; GlSizes.Add(vm, s); }
            if (now - s.At < 2f) return s.Size;
            s.At = now; s.Size = 0f;
            try
            {
                var unit = FieldV(vm, "Unit"); var bo = FieldV(unit, "BaseObject");
                if (bo == null) return 0f;
                var vp = FieldV(bo, "VP");
                if (vp == null) return 0f;                                           // not a vessel: no size
                bool known = FieldV(bo, "IsPlayerObject") is bool && (bool)FieldV(bo, "IsPlayerObject");
                if (!known) { var idn = FieldV(unit, "Identified"); var v = FieldV(idn, "Value"); known = v is bool && (bool)v; }
                if (!known) return 0f;
                var d = FieldV(vp, "_displacement");
                if (d is float) s.Size = (float)d;
            }
            catch { }
            return s.Size;
        }

        /// <summary>A unit of the player's side (BaseObject.IsPlayerObject).</summary>
        static bool GlOurs(object vm)
        {
            try { var bo = FieldV(FieldV(vm, "Unit"), "BaseObject"); var o = FieldV(bo, "IsPlayerObject"); return o is bool && (bool)o; } catch { return false; }
        }

        static MethodInfo _glLegacy;

        /// <summary>A side other than "Game" is chosen: the solver (Cluster / Solve / Attribute) is switched off, the labels get the descriptor's side and the lines the layers permit (the game's own PlaceLegacy).</summary>
        static bool GlFixed()
        {
            var a = Active;
            return (object)a != null && MapImp && _glHooked && !_glFailed && _glLegacy != null && a._glPlace != null && a._glPlace.Value != "Game";
        }

        /// <summary>With the solver switched off in the game's map settings (SolvedLabels) ArrangeLabels ignores the slot and puts the box at the old top-left place; _wasSolved = true makes it use the slot.</summary>
        static readonly Dictionary<object, int> _glSides = new Dictionary<object, int>();           // label -> the side chosen by the overlap filter (the game's Slot: 0 E, 2 S, 4 W, 6 N)
        static MethodInfo[] _glLineGet; static Type _glLineGetType;
        /// <summary>Current text of the contact line q (0-3) of a unit view model, as the label shows it; null when unknown.</summary>
        static string GlLineText(object vm, int q)
        {
            try
            {
                var t = vm.GetType();
                if (_glLineGet == null || _glLineGetType != t)
                {
                    _glLineGet = new MethodInfo[4]; _glLineGetType = t;
                    for (int k = 0; k < 4; k++) { var pr = t.GetProperty("ContactInfoLine" + (k + 1)); _glLineGet[k] = pr == null ? null : pr.GetGetMethod(); }
                }
                var m = _glLineGet[q]; if (m == null) return null; var r = m.Invoke(vm, null); return r == null ? "" : r.ToString();       // the getter postfix has removed the empty lines already
            }
            catch { return null; }
        }
        /// <summary>The shape of the 4 contact lines of a unit: per line its length and its line breaks. A size is measured again only when the shape changes (the digits of the distance / altitude change all the time, their width does not).</summary>
        static int[] GlShape(object vm)
        {
            var r = new int[9];
            for (int q = 0; q < 4; q++) { var t = GlLineText(vm, q) ?? ""; int nl = 0; foreach (char c in t) if (c == '\n') nl++; r[q * 2] = t.Length; r[q * 2 + 1] = nl; }
            r[8] = _glBadgeTplOn && GlFriendlyMarker(vm) ? 1 : 0;                      // the badge row of the first line
            return r;
        }
        static bool GlSameShape(int[] a, int[] b) { if (a == null || b == null) return false; for (int k = 0; k < 9; k++) if (a[k] != b[k]) return false; return true; }
        /// <summary>The text without empty lines (the text of one label line may carry a line break before / between / after its parts).</summary>
        static string GlNoEmptyLines(string t)
        {
            var parts = t.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries); var r = new List<string>();
            foreach (var x in parts) if (x.Trim().Length > 0) r.Add(x);
            return string.Join("\n", r);
        }
        static bool _glHostErr; static FieldInfo _glHostF; static MethodInfo _glAttach, _glShowAll, _glDetach, _glReadSizes;
        /// <summary>The same as the game does for a label that is not shown (RealizeAndMeasure): the hidden host presenter takes the label, shows all lines, measures, and ReadSizes writes LineSizes.</summary>
        static bool GlHostMeasure(object layer, object lab)
        {
            var lt = layer.GetType();
            if (_glHostF == null)
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                _glHostF = lt.GetField("_host", f); _glReadSizes = lt.GetMethod("ReadSizes", f);
                var ht = _glHostF == null ? null : _glHostF.FieldType;
                if (ht != null) { _glAttach = ht.GetMethod("Attach", f); _glShowAll = ht.GetMethod("ShowAllForMeasure", f); _glDetach = ht.GetMethod("Detach", f); }
            }
            if (_glHostF == null || _glReadSizes == null || _glAttach == null || _glShowAll == null || _glDetach == null) return false;
            var host = _glHostF.GetValue(layer) as Noesis.UIElement; if (host == null) return false;
            try
            {
                _glAttach.Invoke(host, new object[] { lab, layer }); _glShowAll.Invoke(host, null);
                host.Measure(new Noesis.Size(float.PositiveInfinity, float.PositiveInfinity));
                _glReadSizes.Invoke(layer, new object[] { lab, host });             // ReadSizesPostfix keeps the sizes as the natural ones
            }
            finally { try { _glDetach.Invoke(host, null); } catch { } }
            return true;
        }
        /// <summary>The natural sizes of a label, measured now (the game's hidden host) when there are none, the label has another number of lines, or the shape of its unit text changed since the last measure; null when unknown.</summary>
        static GlNat GlSyncSizes(object layer, object lab)
        {
            int lc = GlLineCount(lab, _gcHead.GetValue(lab) != null);
            var vm = _gcContent.GetValue(lab, null); bool unit = vm != null && vm.GetType().Name == "MapUnitViewModel";
            GlNat nat; GlNats.TryGetValue(lab, out nat);
            if (nat == null || nat.W == null || nat.W.Length != lc || nat.Gen != _glNatGen || (unit && !GlSameShape(nat.Shape, GlShape(vm))))
                if (GlHostMeasure(layer, lab)) GlNats.TryGetValue(lab, out nat);
            return nat != null && nat.W != null && nat.W.Length == lc ? nat : null;
        }
        static bool _glCutErr;
        static List<string> GlDbgSizes(object lab) { var r = new List<string>(); try { var ls = _gcLineSizes.GetValue(lab) as Array; if (ls != null) foreach (var e in ls) r.Add(((float)_f2x.GetValue(e)).ToString("0") + "x" + ((float)_f2y.GetValue(e)).ToString("0")); } catch { } return r; }
        static FieldInfo _glPresF, _glEmptyF; static object _glZero;

        /// <summary>After every measure of a label (the game's own: a presenter or the hidden host; and the mod's GlHostMeasure): the sizes the game read are the natural ones (all lines shown, the text as it is),
        /// they are kept, then the cut of the label is applied to them.</summary>
        static void ReadSizesPostfix(object __instance, object label)
        {
            try
            {
                var ls = _gcLineSizes.GetValue(label) as Array; if (ls == null) return;
                GlNat nat; if (!GlNats.TryGetValue(label, out nat)) { nat = new GlNat(); GlNats.Add(label, nat); }
                if (nat.W == null || nat.W.Length != ls.Length) { nat.W = new float[ls.Length]; nat.H = new float[ls.Length]; }
                for (int q = 0; q < ls.Length; q++) { var e = ls.GetValue(q); nat.W[q] = (float)_f2x.GetValue(e); nat.H[q] = (float)_f2y.GetValue(e); }
                var vm = _gcContent.GetValue(label, null);
                nat.Shape = vm != null && vm.GetType().Name == "MapUnitViewModel" ? GlShape(vm) : null; nat.Gen = _glNatGen;
                GlCutState st; if (GlCuts.TryGetValue(label, out st)) st.Applied = 0;      // LineSizes and the presenter's flags are fresh: nothing is cut in them
                int want; _glCut.TryGetValue(label, out want);
                GlApplyCut(__instance, label, want);
            }
            catch (Exception e) { if (!_glCutErr) { _glCutErr = true; Trace("line sizes: " + e.GetType().Name + ": " + e.Message); } }
        }

        /// <summary>The cut of a label: the natural sizes with the cut contact lines zero go to LineSizes (the box of ArrangeLabels), and the presenter's _empty flags (its SetPrefix collapses those lines) follow:
        /// a line is not drawn when it is cut or has no size (an empty text). Only the label is changed: the text of the view model stays as it is.</summary>
        static void GlApplyCut(object layer, object lab, int mask)
        {
            GlCutState st; if (!GlCuts.TryGetValue(lab, out st)) { if (mask == 0) return; st = new GlCutState(); GlCuts.Add(lab, st); }
            if (st.Applied == mask) return;
            GlNat nat; if (!GlNats.TryGetValue(lab, out nat) || nat.W == null) return;
            var ls = _gcLineSizes.GetValue(lab) as Array; if (ls == null || ls.Length != nat.W.Length) return;
            int first = _gcHead.GetValue(lab) != null ? 1 : 0;
            if (_glZero == null) _glZero = Activator.CreateInstance(ls.GetType().GetElementType());
            IList em = null;
            var pres = _glPresF.GetValue(layer) as IDictionary; var pr = pres != null && pres.Contains(lab) ? pres[lab] : null;
            if (pr != null) { if (_glEmptyF == null) _glEmptyF = pr.GetType().GetField("_empty", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); em = _glEmptyF == null ? null : _glEmptyF.GetValue(pr) as IList; }
            for (int q = 0; q < ls.Length; q++)
            {
                bool c = q >= first && (mask & (1 << (q - first))) != 0;
                bool none = c || nat.W[q] <= 0.5f || nat.H[q] <= 0.5f;
                if (none) ls.SetValue(_glZero, q);
                else { var v = Activator.CreateInstance(ls.GetType().GetElementType()); _f2x.SetValue(v, nat.W[q]); _f2y.SetValue(v, nat.H[q]); ls.SetValue(v, q); }
                if (em != null && q < em.Count) em[q] = none;
            }
            st.Applied = mask;
        }

        static void ArrangePrefix(object __instance, ref bool ____wasSolved)
        {
            try                                                                          // the cut of every text label wanted in this pass (none when the filters are off)
            {
                var act = _glActive.GetValue(__instance) as IList;
                if (act != null) for (int i = 0; i < act.Count; i++) { var lab = act[i]; if (GlIcon(lab)) continue; int m; _glCut.TryGetValue(lab, out m); GlApplyCut(__instance, lab, m); }
            }
            catch (Exception e) { if (!_glCutErr) { _glCutErr = true; Trace("line sizes: " + e.GetType().Name + ": " + e.Message); } }
            if (!GlFixed()) return;
            ____wasSolved = true;
            try
            {
                GlGroupAround(__instance);
                if (_glSides.Count > 0) foreach (var kv in _glSides) _gcSlot.SetValue(kv.Key, kv.Value);
            }
            catch (Exception e) { _glFailed = true; Trace("group label: " + e.Message); }
        }

        // ---- the name of a compact formation is put around the whole formation, not around its leader
        static FieldInfo _gpLat, _gpLon;
        /// <summary>Distance in nautical miles between the geo anchors (GeoPosition boxed) of two labels.</summary>
        static double GlNm(object a, object b)
        {
            if (_gpLat == null) { var t = a.GetType(); _gpLat = t.GetField("_latitude", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); _gpLon = t.GetField("_longitude", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); }
            if (_gpLat == null || _gpLon == null) return 0.0;
            double la1 = (double)_gpLat.GetValue(a) * Math.PI / 180.0, lo1 = (double)_gpLon.GetValue(a) * Math.PI / 180.0, la2 = (double)_gpLat.GetValue(b) * Math.PI / 180.0, lo2 = (double)_gpLon.GetValue(b) * Math.PI / 180.0;
            double h = Math.Sin((la2 - la1) / 2) * Math.Sin((la2 - la1) / 2) + Math.Cos(la1) * Math.Cos(la2) * Math.Sin((lo2 - lo1) / 2) * Math.Sin((lo2 - lo1) / 2);
            return 2.0 * 3440.065 * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
        }
        static readonly ConditionalWeakTable<object, float[]> GlGroupExt = new ConditionalWeakTable<object, float[]>();   // label -> L,T,R,B distances from the new anchor to the formation's edge

        /// <summary>For every formation name label X: the centre of the active icons of the formation (the units within the spread limit of the leader) and the distances from it to the edges of the drawn icons.
        /// Label -> { cx, cy, L, T, R, B }.</summary>
        static Dictionary<object, float[]> GlGroupBoxes(IList active, IList px)
        {
            Dictionary<object, float[]> res = null, bb = null;
            for (int i = 0; i < active.Count; i++)
            {
                var lab = active[i]; if (!GlIcon(lab)) continue;
                var gr = _gcGroup.GetValue(lab, null); if (gr == null) continue;
                var key = _ggKey.GetValue(gr, null); if (key == null || key.GetType().Name != "UnitFormation") continue;
                if (!GlNear(gr, lab)) continue;                                              // a unit beyond the limit is independent: not in the box of the formation
                var p = px[i]; float x = (float)_f2x.GetValue(p), y = (float)_f2y.GetValue(p);
                if (bb == null) bb = new Dictionary<object, float[]>();
                // the member's drawn extents about its anchor (measured icon, a selected one at least the selection frame); b: anchors box 0-3, edges of the drawn icons 4-7
                var mvm = _gcContent.GetValue(lab, null); GlExt me; float eL = 11.25f, eT = 11.25f, eR = 11.25f, eB = 11.25f;
                if (mvm != null && GlExts.TryGetValue(mvm, out me)) { eL = me.L; eT = me.T; eR = me.R; eB = me.B; }
                if (GlSelected(mvm)) { eL = Math.Max(eL, GlSelFrame); eT = Math.Max(eT, GlSelFrame); eR = Math.Max(eR, GlSelFrame); eB = Math.Max(eB, GlSelFrame); }
                float[] b; if (!bb.TryGetValue(gr, out b)) { b = new[] { x, y, x, y, x - eL, y - eT, x + eR, y + eB }; bb[gr] = b; }
                if (x < b[0]) b[0] = x; if (y < b[1]) b[1] = y; if (x > b[2]) b[2] = x; if (y > b[3]) b[3] = y;
                if (x - eL < b[4]) b[4] = x - eL; if (y - eT < b[5]) b[5] = y - eT; if (x + eR > b[6]) b[6] = x + eR; if (y + eB > b[7]) b[7] = y + eB;
            }
            if (bb == null) return null;
            for (int i = 0; i < active.Count; i++)
            {
                var lab = active[i];
                if (GlIcon(lab) || !_glXs.Contains(lab)) continue;                            // the label with the formation name
                var gr = GlGroupOf(lab); float[] b;
                if (gr == null || !bb.TryGetValue(gr, out b)) continue;
                float cx = (b[0] + b[2]) * 0.5f, cy = (b[1] + b[3]) * 0.5f;
                if (res == null) res = new Dictionary<object, float[]>();
                res[lab] = new[] { cx, cy, cx - b[4], cy - b[5], b[6] - cx, b[7] - cy };
            }
            return res;
        }

        /// <summary>Before ArrangeLabels: the name label of a formation is anchored at the centre of the formation (`_anchorPx`), its side distances kept for RadiusPostfix.</summary>
        static void GlGroupAround(object layer)
        {
            var active = _glActive.GetValue(layer) as IList; var px = _glPx.GetValue(layer) as IList;
            if (active == null || px == null || active.Count != px.Count) return;
            var boxes = GlGroupBoxes(active, px);
            for (int i = 0; i < active.Count; i++)
            {
                var lab = active[i];
                if (GlIcon(lab) || !_glXs.Contains(lab)) continue;
                float[] r;
                if (boxes == null || !boxes.TryGetValue(lab, out r)) { GlGroupExt.Remove(lab); continue; }
                var f2 = Activator.CreateInstance(_gcViewPx.FieldType); _f2x.SetValue(f2, r[0]); _f2y.SetValue(f2, r[1]);
                px[i] = f2;
                GlGroupExt.Remove(lab); GlGroupExt.Add(lab, new[] { r[2], r[3], r[4], r[5] });
            }
        }

        // ---- icon extents: the drawn geometry of the icon (Path "IconGeometry" in the UnitIcon canvas of UnitOverlay.xaml) differs per type and posture
        sealed class GlExt { public float L, T, R, B; public object Type, Posture; }
        static readonly ConditionalWeakTable<object, GlExt> GlExts = new ConditionalWeakTable<object, GlExt>();
        sealed class GlWin { public float Next; }
        static readonly ConditionalWeakTable<object, GlWin> GlWins = new ConditionalWeakTable<object, GlWin>();
        static bool _glNeedExt;
        static readonly Stack<Noesis.DependencyObject> _glStack = new Stack<Noesis.DependencyObject>();
        const float GlSelFrame = 12.5f;                               // half of the selection frame + its stroke
        const float GlGap = 4f;                                       // pixels between the drawn icon and the label box

        /// <summary>Distance from the anchor to the near edge of the label box on a side (0 right, 2 bottom, 4 left, 6 top): the drawn extent of the icon + a gap; without a measure: bottom 7, top 18, sides 13.</summary>
        static float GlBase(object vm, int side)
        {
            GlExt e;
            float d = side == 2 ? 7f : side == 6 ? 18f : 13f;
            if (vm != null && GlExts.TryGetValue(vm, out e)) d = (side == 2 ? e.B : side == 6 ? e.T : side == 0 ? e.R : e.L) + GlGap;
            if (GlSelected(vm)) d = Math.Max(d, GlSelFrame + GlGap);                    // the selection brackets of the game: a 22.5 square about the anchor
            return d;
        }

        static int GlSide()
        {
            var a = Active; string v = a == null || a._glPlace == null ? "Game" : a._glPlace.Value;
            return v == "Bottom" ? 2 : v == "Top" ? 6 : v == "Left" ? 4 : 0;
        }

        /// <summary>AnchorRadiusPx of a text label (read by ArrangeLabels for SlotOffset, which adds 6 and half the box): the radius that puts the box edge GlBase from the anchor.</summary>
        static void RadiusPostfix(object __instance, ref float __result)
        {
            if (!GlFixed()) return;
            try
            {
                if (GlIcon(__instance) || _gcAggOf.GetValue(__instance) != null) return;
                int side = (int)_gcSlot.GetValue(__instance); if (side % 2 != 0) side = 0; float[] ge;
                float sf; _glShift.TryGetValue(__instance, out sf);
                if (GlGroupExt.TryGetValue(__instance, out ge)) { __result = (side == 2 ? ge[3] : side == 6 ? ge[1] : side == 0 ? ge[2] : ge[0]) + GlGap - 6f + sf; return; }   // ge: L, T, R, B; the screen y is down: bottom = B
                __result = GlBase(_gcContent.GetValue(__instance, null), side % 8) - 6f + sf;
            }
            catch { }
        }

        /// <summary>Per map window, once a second while some icon has no (or an outdated) measure: the Path "IconGeometry" of each unit icon gives the extents about the anchor (the centre of the 22.5 canvas).</summary>
        void GlExtStep(object w, float now)
        {
            if (!_glNeedExt || !GlFixed()) return;
            var root = w as Noesis.DependencyObject; if (root == null) return;
            GlWin gw; if (!GlWins.TryGetValue(w, out gw)) { gw = new GlWin(); GlWins.Add(w, gw); }
            if (now < gw.Next) return;
            if (!IsMapWindow(w)) { gw.Next = now + 5f; return; }
            gw.Next = now + 1f; _glNeedExt = false;
            try
            {
                var st = _glStack; st.Clear(); st.Push(root);
                int budget = 80000;
                while (st.Count > 0 && --budget > 0)
                {
                    var e = st.Pop();
                    var c = e as Noesis.Canvas;
                    if (c != null && c.Name == "UnitIcon" && c.DataContext != null && c.DataContext.GetType().Name == "MapUnitViewModel") { GlMeasure(c); continue; }
                    int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                    for (int k = 0; k < n; k++) { var ch = Noesis.VisualTreeHelper.GetChild(e, k); if (ch != null) st.Push(ch); }
                }
                st.Clear();
            }
            catch (Exception e) { Trace("icon extents: " + e.Message); }
        }

        static void GlMeasure(Noesis.Canvas c)
        {
            Noesis.Path path = null;
            var st = new Stack<Noesis.DependencyObject>(); st.Push(c);
            while (st.Count > 0 && path == null)
            {
                var e = st.Pop();
                var p = e as Noesis.Path;
                if (p != null && p.Name == "IconGeometry") { path = p; break; }
                int n = Noesis.VisualTreeHelper.GetChildrenCount(e);
                for (int k = 0; k < n; k++) { var ch = Noesis.VisualTreeHelper.GetChild(e, k); if (ch != null) st.Push(ch); }
            }
            if (path == null || path.Data == null) return;
            var b = path.Data.Bounds;
            if (b.Width <= 0f && b.Height <= 0f) return;
            float th = path.StrokeThickness * 0.5f;
            var vm = c.DataContext;
            GlExt ext; if (!GlExts.TryGetValue(vm, out ext)) { ext = new GlExt(); GlExts.Add(vm, ext); }
            const float m = 11.25f;
            ext.L = Math.Max(2f, m - b.X + th); ext.T = Math.Max(2f, m - b.Y + th); ext.R = Math.Max(2f, b.X + b.Width - m + th); ext.B = Math.Max(2f, b.Y + b.Height - m + th);
            ext.Type = FieldV(vm, "VisualType"); ext.Posture = FieldV(vm, "VisualPosture");
        }

        static bool SolverPartPrefix() { return !GlFixed(); }

        static bool SolvePrefix(object __instance, object registry)
        {
            if (!GlFixed()) return true;
            try { _glLegacy.Invoke(__instance, new[] { registry }); }
            catch (Exception e) { _glFailed = true; Trace("label placement: " + e.Message); return true; }
            return false;
        }

        /// <summary>The label of an aircraft or a helicopter (MapVisualType 1 Air, 2 Helicopter): the labels of such units of an unselected formation stay dropped.</summary>
        static bool GlAirLabel(object lab)
        {
            try { var vt = FieldV(_gcContent.GetValue(lab, null), "VisualType"); int vi = vt == null ? 0 : Convert.ToInt32(vt); return vi == 1 || vi == 2; }
            catch { return false; }
        }

        static void LabelFilter(object layer, bool grp, bool overlap)
        {
            var active = _glActive.GetValue(layer) as IList; var px = _glPx.GetValue(layer) as IList;
            _glCut.Clear();                                                        // label -> the contact lines cut in this pass (applied by ArrangePrefix / ReadSizesPostfix)
            if (active == null || px == null || active.Count != px.Count) return;
            int n = active.Count;
            var drop = new bool[n]; var low = new bool[n];                        // low: the label of a unit of an unselected formation: the last in the order, kept only where it fits
            _glShift.Clear();
            var groupSel = new Dictionary<object, bool>();
            var groupSize = new Dictionary<object, float>();

            // the formations among the active labels: selection (GlGroupSel) and the biggest unit
            var hasName = new HashSet<object>();
            for (int i = 0; i < n; i++)
            {
                var lab = active[i]; if (GlIcon(lab)) continue;
                var gr = GlGroupOf(lab);
                if (gr == null) continue;
                var key = _ggKey.GetValue(gr, null);
                if (key == null || key.GetType().Name != "UnitFormation") continue;
                if (GlUnits(gr) < 2) continue;                                        // one unit: not a formation for the filters
                if (GlIsName(lab)) hasName.Add(gr);
                if (!groupSel.ContainsKey(gr))
                {
                    float big = 0f; var mem = _gmMembers.GetValue(gr) as IList;
                    if (mem != null) for (int k = 0; k < mem.Count; k++) { if (!GlNear(gr, mem[k])) continue; float z = GlUnitSize(_gcContent.GetValue(mem[k], null)); if (z > big) big = z; }
                    groupSel[gr] = GlGroupSel(gr); groupSize[gr] = big;
                }
            }
            // the base cut of the name labels: a name label X over the leader's lines (form 1) keeps line 3 (distance, altitude) only for an unselected formation whose units are not drawn;
            // the leader's own label that carries the name (no X: the game's placement) shows only the name while its formation is not selected
            for (int i = 0; i < n; i++)
            {
                var lab = active[i]; if (GlIcon(lab) || _gcHead.GetValue(lab) == null) continue;
                var gr = GlGroupOf(lab); bool gs;
                bool unsel = grp && gr != null && groupSel.TryGetValue(gr, out gs) && !gs;
                if (_glXs.Contains(lab)) _glCut[lab] = unsel ? 11 : 15;
                else if (unsel) _glCut[lab] = 15;
            }
            if (grp)
                for (int i = 0; i < n; i++)
                {
                    var lab = active[i]; if (GlIcon(lab) || GlIsName(lab)) continue;
                    var gr = GlGroupOf(lab); bool gs;
                    if (gr == null || !groupSel.TryGetValue(gr, out gs) || !GlNear(gr, lab)) continue;
                    bool air = GlAirLabel(lab);
                    if (gs) { if (air && !GlSelected(_gcContent.GetValue(lab, null))) drop[i] = true; continue; }       // a selected formation of aircraft: only the selected aircraft has a label
                    if (!hasName.Contains(gr)) continue;                                 // no name label on screen: the units stay as the game draws them
                    if (overlap && !air) low[i] = true; else drop[i] = true;              // the name stands for the formation; the labels of its ships are added last, where there is room
                }

            if (overlap)
            {
                var idx = new List<int>();
                var size = new float[n]; var sel = new bool[n]; var tier = new int[n];
                var leaders = new HashSet<object>(); foreach (var nm0 in _glNames.Values) if (nm0.Leader != null) leaders.Add(nm0.Leader);
                for (int i = 0; i < n; i++)
                {
                    var lab = active[i];
                    if (drop[i] || GlIcon(lab) || _gcAggOf.GetValue(lab) != null) continue;
                    idx.Add(i);
                    var gr = GlGroupOf(lab); float z; bool nameL = GlIsName(lab), isX = _glXs.Contains(lab);
                    var cvm = _gcContent.GetValue(lab, null); bool gsel;
                    bool unitSel = !isX && GlSelected(cvm);
                    sel[i] = unitSel || (gr != null && nameL && groupSel.TryGetValue(gr, out gsel) && gsel);      // the selected unit / formation
                    // order of adding the texts: a selected unit, the leaders of the formations (and the names of unselected ones), the other units, the names of selected formations, the units of unselected formations
                    tier[i] = low[i] ? 4 : isX ? (grp && !sel[i] ? 1 : 3) : unitSel ? 0 : (leaders.Contains(lab) || nameL) ? 1 : 2;
                    if (sel[i]) size[i] = float.MaxValue;                                                                                          // the same tier: selected first
                    else if (gr != null && nameL && groupSize.TryGetValue(gr, out z)) size[i] = z;
                    else size[i] = GlUnitSize(cvm);
                }
                idx.Sort((p, q) => { int c = tier[p].CompareTo(tier[q]); if (c != 0) return c; c = size[q].CompareTo(size[p]); return c != 0 ? c : p.CompareTo(q); });     // the biggest first; the same size: the order of the list
                bool fixedSide = GlFixed();
                var gboxes = fixedSide ? GlGroupBoxes(active, px) : null;                    // the name label of a formation sits around the whole formation
                bool dbg = Periodic && UnityEngine.Time.unscaledTime - _glDbgAt > 2f; if (dbg) { _glDbgAt = UnityEngine.Time.unscaledTime; Trace("labels: pass, active " + n + ", place " + (_inst._glPlace == null ? "" : _inst._glPlace.Value)); }
                var kept = new List<float[]>();
                var icons = new List<float[]>();                                       // the drawn icons (a selected one with its frame) are places already taken
                bool badgeIn = _glBadgeTplOn;
                for (int i = 0; i < n; i++)
                {
                    var il = active[i]; if (!GlIcon(il) || drop[i]) continue;
                    var ip = px[i]; float ix = (float)_f2x.GetValue(ip), iy = (float)_f2y.GetValue(ip);
                    var ivm = _gcContent.GetValue(il, null); GlExt ie; float eL = 11.25f, eT = 11.25f, eR = 11.25f, eB = 11.25f;
                    if (ivm != null && GlExts.TryGetValue(ivm, out ie)) { eL = ie.L; eT = ie.T; eR = ie.R; eB = ie.B; }
                    if (GlSelected(ivm)) { eL = Math.Max(eL, GlSelFrame); eT = Math.Max(eT, GlSelFrame); eR = Math.Max(eR, GlSelFrame); eB = Math.Max(eB, GlSelFrame); }
                    icons.Add(new[] { ix - eL, iy - eT, eL + eR, eT + eB });
                    if (ivm != null && !badgeIn && GlFriendlyMarker(ivm)) icons.Add(new[] { ix - 14.75f - 30f, iy - 11.25f, 30f, 22.5f });      // the game's 'FRND' badge: left of the icon, centred on it (MarkerTopPrefix)
                }
                float dirX = 0f, dirY = 0f;                                           // the chosen side of the icon (screen y down)
                if (fixedSide) { string pv = _inst._glPlace.Value; dirY = pv == "Bottom" ? 1f : pv == "Top" ? -1f : 0f; dirX = pv == "Right" ? 1f : pv == "Left" ? -1f : 0f; }
                foreach (int i in idx)
                {
                    var lab = active[i];
                    var vp = _gcViewPx.GetValue(lab);
                    float ax = (float)_f2x.GetValue(vp), ay = (float)_f2y.GetValue(vp); float[] gb = null;
                    if (gboxes != null && gboxes.TryGetValue(lab, out gb)) { ax = gb[0]; ay = gb[1]; }
                    var vm = _gcContent.GetValue(lab, null);
                    bool unit = vm != null && vm.GetType().Name == "MapUnitViewModel";
                    bool head = _gcHead.GetValue(lab) != null;
                    int blank; _glCut.TryGetValue(lab, out blank);
                    // the natural sizes of the lines (ReadSizesPostfix); measured now when there are none, the label has another number of lines, or the shape of its text changed
                    GlNat nat = null;
                    try { nat = GlSyncSizes(layer, lab); } catch (Exception e) { if (!_glHostErr) { _glHostErr = true; Trace("label measure: " + e.GetType().Name + ": " + e.Message); } }
                    if (nat == null) { _glCut.Remove(lab); continue; }                 // no measure: the label is left as the game has it
                    float[] lw = nat.W, lh = nat.H;
                    int first = head ? 1 : 0;
                    int best = -1; bool isX = _glXs.Contains(lab); float shiftDone = 0f; bool ours = unit && !head && GlOurs(vm);
                    var ne = new List<int>();                                          // the contact lines that can be drawn: with text, not cut by the base cut
                    for (int q = first; q < lw.Length; q++) if (lw[q] > 0.5f && lh[q] > 0.5f && (blank & (1 << (q - first))) == 0) ne.Add(q);
                    int vn = ne.Count;
                    int bestMask = 0, bc = -1, bsi = 0, bsh = 0;
                    float tnow = UnityEngine.Time.unscaledTime; GlDec pd; GlDecs.TryGetValue(lab, out pd);
                    bool hold = pd != null && !sel[i] && tnow - pd.T < 1f && (pd.Drop || (pd.C <= vn && pd.Si < (fixedSide ? 4 : 1)));      // hysteresis: a decision is tried first for 1 s
                    // pass 0 with a held decision tries only that placement (still checked against the boxes placed before it); when it no longer fits, pass 1 searches as usual. A held drop stays dropped.
                    for (int pass = 0; pass < (hold && !pd.Drop ? 2 : 1) && best < 0; pass++)
                    {
                    bool hd = hold && pass == 0;
                    for (int sh = hd && !pd.Drop ? pd.Sh : 0; sh <= (hd ? (pd.Drop ? -1 : pd.Sh) : (isX ? (sel[i] ? 150 : 60) : 0)) && best < 0; sh += 12)          // the name of a formation moves farther from it when the text of a unit is in its way
                    for (int c = hd ? pd.C : vn; c >= (hd ? pd.C : (head ? 0 : Math.Min(1, vn))) && best < 0; c--)   // all lines first, then fewer and fewer from the bottom; the head line (or the first line) stays
                    for (int si = hd ? pd.Si : 0; si < (hd ? pd.Si + 1 : (fixedSide ? 4 : 1)) && best < 0; si++)       // the chosen side, then the opposite one, then the other two (the offset of a name is the last resort)
                    {
                        float cdx = dirX, cdy = dirY;
                        if (si == 1) { cdx = -dirX; cdy = -dirY; }
                        else if (si >= 2) { bool vert = dirY != 0f; cdx = vert ? (si == 2 ? 1f : -1f) : 0f; cdy = vert ? 0f : (si == 2 ? 1f : -1f); }
                        bool second = c == 1 && ours && vn >= 2;                       // not even 2 lines of an own unit fit: its 2nd line stays, not the 1st
                        float w = 0f, h = 0f;
                        if (head) { w = lw[0]; h = lh[0]; }
                        if (second) { w = Math.Max(w, lw[ne[1]]); h += lh[ne[1]]; }
                        else for (int q = 0; q < c; q++) { w = Math.Max(w, lw[ne[q]]); h += lh[ne[q]]; }
                        float x, y; int chosen = -1;
                        if (fixedSide) { int sd = cdy > 0f ? 2 : cdy < 0f ? 6 : cdx < 0f ? 4 : 0; float bs = gb != null ? (sd == 2 ? gb[5] : sd == 6 ? gb[3] : sd == 0 ? gb[4] : gb[2]) + GlGap : GlBase(vm, sd); bs += sh; float ex = Math.Abs(cdx) * w * 0.5f + Math.Abs(cdy) * h * 0.5f; x = ax + cdx * (bs + ex) - w * 0.5f; y = ay + cdy * (bs + ex) - h * 0.5f; chosen = sd; }
                        else { x = ax + 14.75f; y = ay - 21.25f; }
                        bool hit = false;
                        if (!sel[i] || isX) foreach (var r in kept)                       // a selected unit is never cut or dropped: all lines, the others make room
                            if (x + 1f < r[0] + r[2] && r[0] + 1f < x + w && y + 1f < r[1] + r[3] && r[1] + 1f < y + h) { hit = true; break; }
                        if (!sel[i] || isX) foreach (var r in icons)
                            if (x + 1f < r[0] + r[2] && r[0] + 1f < x + w && y + 1f < r[1] + r[3] && r[1] + 1f < y + h) { hit = true; break; }
                        if (!hit)
                        {
                            bc = c; bsi = si; bsh = sh;
                            best = first + c; shiftDone = sh; if (chosen >= 0) _glSides[lab] = chosen;
                            for (int q = 0; q < vn; q++) if (!(second ? q == 1 : q < c)) bestMask |= 1 << (ne[q] - first);      // the lines that do not fit
                            if (dbg) { GlExt de; string en = vm != null && GlExts.TryGetValue(vm, out de) ? de.L + "/" + de.T + "/" + de.R + "/" + de.B : "-"; Trace("  label " + (unit ? GlLineText(vm, 0) : "?") + " X=" + isX + " sel=" + sel[i] + " anchor " + ax.ToString("0") + "," + ay.ToString("0") + " side " + chosen + " (try " + si + ") lines " + c + "/" + vn + " shift " + sh + " box " + x.ToString("0") + "," + y.ToString("0") + " " + w.ToString("0") + "x" + h.ToString("0") + " ext L/T/R/B " + en + " text [" + (unit ? GlLineText(vm, 0) + "|" + GlLineText(vm, 1) + "|" + GlLineText(vm, 2) + "|" + GlLineText(vm, 3) : "") + "] cut " + (blank | bestMask) + " size " + string.Join(",", GlDbgSizes(lab))); }
                            kept.Add(new[] { x, y, w, h }); break;
                        }
                    }
                    }
                    if (!sel[i])
                    {
                        bool nd = best < 0;
                        if (pd == null) GlDecs.Add(lab, pd = new GlDec { C = bc, Si = bsi, Sh = bsh, Drop = nd, T = tnow });
                        else if (pd.Drop != nd || (!nd && (pd.C != bc || pd.Si != bsi || pd.Sh != bsh))) { pd.C = bc; pd.Si = bsi; pd.Sh = bsh; pd.Drop = nd; pd.T = tnow; }
                    }
                    if (best < 0 && isX && sel[i]) { _glShift[lab] = 0f; continue; }          // the name of a selected formation is never dropped
                    if (isX && best >= 0) _glShift[lab] = shiftDone;
                    if (best < 0) { drop[i] = true; continue; }
                    if ((blank | bestMask) != 0) _glCut[lab] = blank | bestMask;
                }
            }

            // take the dropped ones out of the lists and renumber
            bool any = false;
            for (int i = 0; i < n; i++) if (drop[i]) { any = true; _glCut.Remove(active[i]); }
            if (!any) return;
            for (int i = n - 1; i >= 0; i--)
            {
                if (!drop[i]) continue;
                var lab = active[i];
                _gcEligible.SetValue(lab, false); _gcRealized.SetValue(lab, false); _gcActiveIndex.SetValue(lab, -1);
                _gcRep.SetValue(lab, null); _gcRepScore.SetValue(lab, 0f);
                active.RemoveAt(i); px.RemoveAt(i);
            }
            for (int i = 0; i < active.Count; i++) _gcActiveIndex.SetValue(active[i], i);
        }
    }
}
