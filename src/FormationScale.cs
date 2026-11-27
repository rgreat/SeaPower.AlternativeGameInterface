using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// The scale slider of the formation editor (the range rings; in a mission: FormationView, in the campaign's mission preparation:
    /// LinearCampaignTaskForceFormationView) moves in 0.05 nm steps. Here: the step is an option (1 nm by default) and a row of buttons
    /// 1 - 5 nm sits under the slider. The slider is caught by its Loaded event (ViewCapture), found by its view model.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<float> _scaleStep;
        ConfigEntry<bool> _scaleButtons, _scaleRemember, _scaleMin;
        ConfigEntry<float> _scaleMinFloor;
        static readonly ConditionalWeakTable<object, object> ScaleDone = new ConditionalWeakTable<object, object>();    // slider -> its wrapper (once per slider)

        void BindFormationScaleConfig()
        {
            _scaleStep = Config.Bind("FormationEditor", "ScaleStep", 1f,
                new ConfigDescription("Formation editor (in a mission and in the campaign's mission preparation): the step of the scale slider (the range rings), in nm. The game's own is 0.05. " +
                "With a step over 0.05 the slider's lowest value is the step itself.", new AcceptableValueRange<float>(0.05f, 10f)));
            _scaleButtons = Config.Bind("FormationEditor", "ScaleButtons", true,
                "Formation editor: a row of buttons 1, 2, 3, 4, 5 nm under the scale slider, each sets the scale at once.");
            _scaleRemember = Config.Bind("FormationEditor", "RememberScale", true,
                "Formation editor in a mission: the scale of each formation is remembered (the game forgets it when the editor is closed) and kept in the saved game: the section [" + SaveSection + "].");
            _scaleMin = Config.Bind("FormationEditor", "MinimizeScale", true,
                "Formation editor in a mission: a formation whose scale is not remembered yet (never set) is opened at the smallest scale that still holds all its units, not smaller than MinimizeScaleFloor.");
            _scaleMinFloor = Config.Bind("FormationEditor", "MinimizeScaleFloor", 2f,
                new ConfigDescription("MinimizeScale: the smallest scale it may choose, in nm (1 to 10, 2 by default).", new AcceptableValueRange<float>(1f, 10f)));
        }

        static string dc0(Slider sl) { return sl.DataContext == null ? null : sl.DataContext.GetType().Name; }

        static bool IsScaleSlider(Slider sl)
        {
            var dc = sl.DataContext;
            if (dc == null) return false;
            var n = dc.GetType().Name;
            return n == "FormationViewModel" || n == "FormationEditorViewModel" || n == "LinearCampaignTaskForceFormationViewModel";
        }

        /// <summary>Called for every loaded Slider (ViewCapture); does nothing for any but the formation editor's scale slider.</summary>
        static void ScaleSliderLoaded(AlternativeGameInterfacePlugin a, Slider sl)
        {
            if (a._scaleStep == null || !IsScaleSlider(sl)) return;
            try
            {
                object done;
                if (ScaleDone.TryGetValue(sl, out done)) return;
                ScaleDone.Add(sl, sl);

                float step = a._scaleStep.Value;
                if (step > 0.0501f)
                {
                    sl.IsSnapToTickEnabled = true;
                    sl.TickFrequency = step;                // the snap counts from the Minimum: the lowest value is the step itself
                    sl.Minimum = step;
                    Trace("formation editor: scale step " + step + " nm");
                }

                if (dc0(sl) == "FormationViewModel") a.FormationScaleStart(sl);

                if (!a._scaleButtons.Value) return;
                var row = sl.Parent as Panel;               // the Grid holding the slider and its number
                var host = row == null ? null : row.Parent as Panel;
                if (row == null || host == null) return;
                int at = host.Children.IndexOf(row);
                if (at < 0) return;

                var stack = new StackPanel { Orientation = Orientation.Vertical };
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0f, 4f, 0f, 0f), HorizontalAlignment = HorizontalAlignment.Left };
                for (int n = 1; n <= 5; n++)
                {
                    float v = n;
                    var b = new Button { Content = n + " nm", Padding = new Thickness(8f, 1f, 8f, 1f), Margin = new Thickness(0f, 0f, 4f, 0f), MinHeight = 22f, Focusable = false };
                    b.Click += (o, e) => { try { ScaleUserSet = true; sl.Value = Math.Min(Math.Max(v, sl.Minimum), sl.Maximum); } catch (Exception ex) { Trace("formation scale button: " + ex.Message); } finally { ScaleUserSet = false; } };
                    buttons.Children.Add(b);
                }
                // the wrapper takes the row's place in the view's grid (row, span, margin)
                int r = Grid.GetRow(row), rs = Grid.GetRowSpan(row), c = Grid.GetColumn(row), cs = Grid.GetColumnSpan(row);
                var m = row.Margin;
                host.Children.RemoveAt(at);
                row.Margin = new Thickness(0f);
                stack.Children.Add(row);
                stack.Children.Add(buttons);
                stack.Margin = m;
                Grid.SetRow(stack, r); Grid.SetRowSpan(stack, rs); Grid.SetColumn(stack, c); Grid.SetColumnSpan(stack, cs);
                host.Children.Insert(at, stack);
                Trace("formation editor: scale buttons 1 - 5 nm added");
            }
            catch (Exception ex) { Trace("formation scale slider: " + ex.Message); }
        }

        // ---------------------------------------------------------------- per formation: remembered, kept in the save, minimised

        const string SaveSection = "AGI_FormationScale";
        sealed class ScaleBox { public float V; }
        static readonly ConditionalWeakTable<object, ScaleBox> ScaleOf = new ConditionalWeakTable<object, ScaleBox>();    // UnitFormation -> its scale
        static readonly List<WeakReference> ScaleForms = new List<WeakReference>();

        static object Mem(object o, string name)
        {
            if (o == null) return null;
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var fi = t.GetField(name, f | BindingFlags.DeclaredOnly);
                if (fi != null) return fi.GetValue(o);
                var pi = t.GetProperty(name, f | BindingFlags.DeclaredOnly);
                if (pi != null && pi.GetIndexParameters().Length == 0) return pi.GetValue(o, null);
            }
            return null;
        }

        /// <summary>The key of a formation in the save: as the game's own ("[taskforce]_Formation[n]", n counted from 1 in the taskforce's list).</summary>
        static string FormationKey(object formation)
        {
            var tf = Mem(formation, "_taskforce");
            var name = Mem(tf, "_nameInMissionFile") as string;
            var list = Mem(tf, "Formations") as IEnumerable;
            if (string.IsNullOrEmpty(name) || list == null) return null;
            int n = 0;
            foreach (var f in list) { n++; if (ReferenceEquals(f, formation)) return name + "_Formation" + n; }
            return null;
        }

        static object MissionIni(Assembly asm)
        {
            var sc = asm.GetType("SeaPower.SceneCreator");
            var sg = asm.GetType("SeaPower.Singleton`1");
            if (sc == null || sg == null) return null;
            var inst = sg.MakeGenericType(sc).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            var o = inst == null ? null : inst.GetValue(null, null);
            return Mem(o, "MissionIni");
        }

        /// <summary>The saved mission's section of the scales (null when the running mission is not a loaded save or has none).</summary>
        static Dictionary<string, string> SavedScales(Assembly asm)
        {
            try
            {
                var slm = asm.GetType("SeaPower.SaveLoadManager");
                var isSave = slm == null ? null : slm.GetProperty("IsASaveFile", BindingFlags.Static | BindingFlags.Public);
                if (isSave == null || !(bool)isSave.GetValue(null, null)) return null;
                var data = Mem(MissionIni(asm), "Data") as IDictionary;
                if (data == null) return null;
                foreach (DictionaryEntry de in data)
                    if (string.Equals(de.Key as string, SaveSection, StringComparison.OrdinalIgnoreCase))
                    {
                        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var sec = de.Value as IDictionary;
                        if (sec != null) foreach (DictionaryEntry kv in sec) res[(string)kv.Key] = (string)kv.Value;
                        return res;
                    }
            }
            catch (Exception e) { Trace("formation scale (read the save): " + e.Message); }
            return null;
        }

        bool StoredScale(object formation, out float v)
        {
            v = 0f;
            ScaleBox b;
            if (ScaleOf.TryGetValue(formation, out b)) { v = b.V; return v > 0f; }
            return false;          // the saved ones were given to their formations when the save was loaded (ScalesFromSave)
        }

        static void Remember(object formation, float v, bool user = false)
        {
            if (user) Trace("formation editor: '" + (FormationKey(formation) ?? "?") + "' scale " + v.ToString("0.##", CultureInfo.InvariantCulture) + " nm remembered");
            ScaleBox b;
            if (ScaleOf.TryGetValue(formation, out b)) { b.V = v; return; }
            ScaleOf.Add(formation, new ScaleBox { V = v });
            ScaleForms.Add(new WeakReference(formation));
        }

        /// <summary>The smallest scale (nm) at which every station of the formation is inside the rings, with a little room; 0 when it cannot be told.</summary>
        static float FitScale(object formation)
        {
            var stations = Mem(formation, "Stations") as IEnumerable;
            if (stations == null) return 0f;
            float far = 0f;
            foreach (var st in stations)
            {
                if (!(Mem(st, "StationPosition") is UnityEngine.Vector3)) continue;
                var v = (UnityEngine.Vector3)Mem(st, "StationPosition");
                far = Math.Max(far, (float)Math.Sqrt(v.x * v.x + v.z * v.z) * 0.036285132f);
            }
            return far * 1.05f;
        }

        /// <summary>The scale slider of a formation of the running mission has been loaded: put the remembered scale (or the minimal one) in, watch the user's changes.</summary>
        void FormationScaleStart(Slider sl)
        {
            if (Mem(sl.DataContext, "Formation") == null) return;
            FormationScaleApply(sl);
            if (!_scaleRemember.Value) return;
            // the same view given another formation (the window reused): its scale is put in as for a new one
            var shown = new WeakReference(Mem(sl.DataContext, "Formation"));
            sl.DataContextChanged += (o, e) =>
            {
                try
                {
                    var f = dc0(sl) == "FormationViewModel" ? Mem(sl.DataContext, "Formation") : null;
                    if (f == null || ReferenceEquals(f, shown.Target)) return;     // the same formation again (the event also comes when the view is built)
                    shown = new WeakReference(f);
                    FormationScaleApply(sl);
                }
                catch (Exception ex) { Trace("formation scale (other formation): " + ex.Message); }
            };
            float last = sl.Value;
            sl.ValueChanged += (o, e) =>
            {
                try
                {
                    float nv = sl.Value;
                    if (Math.Abs(nv - last) < 1e-4f) return;
                    last = nv;
                    var f = Mem(sl.DataContext, "Formation");       // the slider's own formation now (not the one it was loaded for)
                    if (f != null && (sl.IsMouseOver || sl.IsKeyboardFocusWithin || ScaleUserSet)) Remember(f, nv, true);
                }
                catch (Exception ex) { Trace("formation scale (remember): " + ex.Message); }
            };
        }

        /// <summary>The remembered (or saved) scale of the slider's formation, else the smallest that holds its units (MinimizeScale).</summary>
        void FormationScaleApply(Slider sl)
        {
            var formation = Mem(sl.DataContext, "Formation");
            if (formation == null || !(_scaleRemember.Value || _scaleMin.Value)) return;
            float v;
            string why = "remembered";
            if (!(_scaleRemember.Value && StoredScale(formation, out v)))
            {
                v = 0f;
                why = "nothing remembered, MinimizeScale off";
                if (_scaleMin.Value)
                {
                    float fit = FitScale(formation);
                    if (fit > 0f)
                    {
                        v = Math.Max(fit, _scaleMinFloor.Value);
                        why = "minimized: farthest unit " + (fit / 1.05f).ToString("0.##", CultureInfo.InvariantCulture) + " nm, floor " + _scaleMinFloor.Value.ToString("0.##", CultureInfo.InvariantCulture) + " nm";
                        float step = _scaleStep.Value;
                        if (step > 0.0501f) v = (float)Math.Ceiling(v / step - 0.001f) * step;      // a value of the slider's grid
                    }
                }
            }
            if (v > 0f)
            {
                v = Math.Min(Math.Max(v, sl.Minimum), sl.Maximum);
                sl.Value = v;
                Trace("formation editor: '" + (FormationKey(formation) ?? "?") + "' scale " + v.ToString("0.##", CultureInfo.InvariantCulture) + " nm (" + why + ")");
            }
            else Trace("formation editor: '" + (FormationKey(formation) ?? "?") + "' keeps the game's scale " + sl.Value.ToString("0.##", CultureInfo.InvariantCulture) + " nm (" + why + ")");
        }

        /// <summary>Set by the 1 - 5 nm buttons while they put their value in (the mouse is over the button, not the slider).</summary>
        static bool ScaleUserSet;

        // the game writes the saved mission: the scales go into it (those of the loaded save that were not touched stay)
        void HookFormationScale(Harmony harmony, Assembly asm)
        {
            var slm = asm.GetType("SeaPower.SaveLoadManager");
            var m = slm == null ? null : slm.GetMethod("WriteFormationManagerData", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) { Trace("SaveLoadManager.WriteFormationManagerData not found: the formation scales are not kept in the save"); return; }
            TryPatch(harmony, m, null, "ScaleSavePostfix", null);
            Trace("patched SaveLoadManager.WriteFormationManagerData (formation scales in the save)");
            var uf = asm.GetType("SeaPower.UnitFormation");
            var dis = uf == null ? null : uf.GetMethod("Disband", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (dis != null) { TryPatch(harmony, dis, null, "ScaleDisbandPostfix", null); Trace("patched UnitFormation.Disband (its remembered scale is dropped)"); }
            else Trace("UnitFormation.Disband not found: a disbanded formation's scale is only left out of the save");
        }

        static void ScaleDisbandPostfix(object __instance)
        {
            try
            {
                ScaleBox b;
                if (__instance == null || !ScaleOf.TryGetValue(__instance, out b)) return;
                ScaleOf.Remove(__instance);
                Trace("formation editor: a disbanded formation's scale " + b.V.ToString("0.##", CultureInfo.InvariantCulture) + " nm dropped");
            }
            catch (Exception e) { Trace("formation scale (disband): " + e.Message); }
        }

        static bool _scalesPending;           // set by SaveLoadManager.LoadMetadata (a mission is being loaded); the first StartTimer after it is the scene's own

        /// <summary>A save was loaded (SaveLoadManager.StartTimer, once per scene): each saved scale goes to its formation object now, while the keys still
        /// match the save's numbering; from then on a scale follows its formation, whatever happens to the numbers.</summary>
        static void ScalesFromSave()
        {
            var a = Active;
            if (!_scalesPending) return;          // StartTimer is also called by the autosave (SaveLoadManager.OnUpdate) of a running mission
            _scalesPending = false;
            if ((object)a == null || a._scaleRemember == null || !a._scaleRemember.Value) return;
            try
            {
                var asm = GameAssembly();
                var saved = SavedScales(asm);
                if (saved == null || saved.Count == 0) { Trace("formation scales read from the save: none (" + (saved == null ? "not a save, or no [" + SaveSection + "] section" : "empty section") + ")"); return; }
                var tm = asm.GetType("SeaPower.TaskforceManager");
                var sg = asm.GetType("SeaPower.Singleton`1");
                var inst = tm == null || sg == null ? null : sg.MakeGenericType(tm).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var tfs = Mem(inst, "_taskForces") as IEnumerable;
                int n = 0;
                if (tfs != null)
                    foreach (var tf in tfs)
                    {
                        var list = Mem(tf, "Formations") as IEnumerable;
                        if (list == null) continue;
                        foreach (var f in list)
                        {
                            string txt; float v; var key = FormationKey(f);
                            if (key != null && saved.TryGetValue(key, out txt) && float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0f) { Remember(f, v); n++; }
                        }
                    }
                Trace("formation scales read from the save: " + n + " of " + saved.Count);
            }
            catch (Exception e) { Trace("formation scale (read the save): " + (e.InnerException ?? e).Message); }
        }

        static void ScaleSavePostfix(object ini)
        {
            var a = Active;
            if ((object)a == null || a._scaleRemember == null || !a._scaleRemember.Value || ini == null) return;
            try
            {
                var write = ini.GetType().GetMethod("writeValue", new[] { typeof(string), typeof(string), typeof(string) });
                if (write == null) return;
                int n = 0;
                for (int i = ScaleForms.Count - 1; i >= 0; i--)
                {
                    var f = ScaleForms[i].Target;
                    if (f == null) { ScaleForms.RemoveAt(i); continue; }
                    ScaleBox b;
                    var key = FormationKey(f);
                    if (key == null || Mem(f, "_isDisbanded") is bool && (bool)Mem(f, "_isDisbanded") || !ScaleOf.TryGetValue(f, out b)) continue;
                    write.Invoke(ini, new object[] { SaveSection, key, b.V.ToString("0.####", CultureInfo.InvariantCulture) });
                    n++;
                }
                Trace("formation scales written to the save: " + n + " in [" + SaveSection + "]");
            }
            catch (Exception e) { Trace("formation scale (write the save): " + (e.InnerException ?? e).Message); }
        }
    }
}
