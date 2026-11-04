using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Unit Reference, ships and submarines: a "Propulsion" cell in the row of the leading particulars (Length, Beam, Displacement, Max Speed), right after
    /// Max Speed: the type and power of the power plant(s), "Gas turbine 80,000 hp"; a second line for the auxiliary plant (submarines: "Electric 2,990 hp"); with several plants the caption line is left out (a hint on the cell says what it is).
    /// The data is the vessel's ini ([MainPowerSystem] / [AuxPowerSystem] Type and HorsePower), found through UnitMetadataProvider.UnitData.
    /// Event driven: the game's own UnitTabs_SelectionChanged handler runs when the unit page is built (the tab control is created and selects its tab);
    /// a postfix there puts the cell into the page's WrapPanel once; the cell updates itself on DataContextChanged (another unit shown in the same page).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _propulsion;
        const string PropulsionTag = "agiPropulsion";

        void BindPropulsionConfig()
        {
            _propulsion = Config.Bind("UnitReference", "ShowPropulsion", true,
                "Unit Reference: a Propulsion cell in the main parameters of ships and submarines (type and power of the engines), after Max Speed.");
        }

        void HookPropulsion(Harmony harmony, Assembly asm)
        {
            var view = asm.GetType("SeapowerUI.ObjectInformationView");
            var m = view == null ? null : view.GetMethod("UnitTabs_SelectionChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Logger.LogWarning("ObjectInformationView.UnitTabs_SelectionChanged not found (game updated?); the Propulsion cell is not available."); return; }
            TryPatch(harmony, m, null, "PropulsionPostfix", null);
            Trace("patched ObjectInformationView.UnitTabs_SelectionChanged (Propulsion cell)");
        }

        static void PropulsionPostfix(object sender)
        {
            var a = Active;
            if ((object)a == null || a._propulsion == null || !a._propulsion.Value) return;
            try { a.AddPropulsionCell(sender as TabControl); }
            catch (Exception e) { Trace("Propulsion cell: " + e); }
        }

        void AddPropulsionCell(TabControl tabs)
        {
            var root = tabs == null ? null : VtParent(tabs) as Grid;
            if (root == null) return;
            WrapPanel row = null;
            for (int i = 0, n = root.Children.Count; i < n; i++)
            {
                var w = root.Children[i] as WrapPanel;
                if (w != null && Grid.GetRow(w) == 1) { row = w; break; }
            }
            if (row == null || row.Children.Count < 4) return;
            foreach (var c in row.Children) { var fe = c as FrameworkElement; if (fe != null && PropulsionTag.Equals(fe.Tag)) return; }   // already there

            // the same look as the game's cells: its Border style and its label style
            var first = row.Children[0] as Border;
            var stack = first == null ? null : first.Child as StackPanel;
            var label0 = stack == null || stack.Children.Count == 0 ? null : stack.Children[0] as TextBlock;
            if (first == null || label0 == null) return;
            var label = new TextBlock { Style = label0.Style, Text = Loc.T("ref.propulsion") };
            var value = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var cell = new Border { Style = first.Style, Tag = PropulsionTag, Child = new StackPanel() };
            var sp = (StackPanel)cell.Child;
            sp.Children.Add(label); sp.Children.Add(value);
            Action refresh = () =>
            {
                string text = null;
                try { if (_propulsion != null && _propulsion.Value) text = PropulsionText(cell.DataContext); }
                catch (Exception e) { Trace("PropulsionText: " + e.Message); }
                value.Text = text ?? "";
                label.Visibility = text != null && text.IndexOf('\n') >= 0 ? Visibility.Collapsed : Visibility.Visible;   // several engines: no caption line (the hint says what it is)
                cell.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            };
            try { ToolTipService.SetToolTip(cell, Loc.T("ref.propulsion.tip")); } catch { }
            cell.DataContextChanged += (s, e) => refresh();
            row.Children.Insert(Math.Min(4, row.Children.Count), cell);   // after Max Speed
            refresh();
        }

        // ------------------------------------------------------------------ the data

        static Dictionary<string, object> _vesselInis;
        static MethodInfo _iniRead;

        /// <summary>The ini of every vessel (surface ship or submarine) by its ini name, read from UnitMetadataProvider.UnitData once.</summary>
        static Dictionary<string, object> VesselInis()
        {
            if (_vesselInis != null) return _vesselInis;
            var res = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var asm = GameAssembly();
            var t = asm == null ? null : asm.GetType("SeaPower.UnitMetadataProvider");
            var sg = asm == null ? null : asm.GetType("SeaPower.Singleton`1");
            if (t == null || sg == null) return null;
            var inst = sg.MakeGenericType(t).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
            var data = inst == null ? null : t.GetProperty("UnitData").GetValue(inst, null) as IDictionary;
            if (data == null) return null;                       // not loaded yet: asked again next time
            foreach (DictionaryEntry de in data)
            {
                var key = de.Key; var val = de.Value;
                var folder = key.GetType().GetField("Item1").GetValue(key) as string;
                if (folder != "vessels") continue;
                var name = key.GetType().GetField("Item2").GetValue(key) as string;
                var prm = val.GetType().GetField("Item2").GetValue(val);
                var ini = prm == null ? null : prm.GetType().GetField("_ini", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(prm);
                if (name != null && ini != null) res[name] = ini;
            }
            if (_iniRead == null && res.Count > 0)
                foreach (var v in res.Values)
                {
                    _iniRead = v.GetType().GetMethod("readValue", new[] { typeof(string), typeof(string), typeof(string), typeof(bool) });
                    break;
                }
            _vesselInis = res;
            return res;
        }

        string PropulsionText(object entry)
        {
            if (entry == null) return null;
            var iniRef = Get(entry, "IniRef") as string;
            if (string.IsNullOrEmpty(iniRef)) return null;
            var inis = VesselInis();
            object ini;
            if (inis == null || _iniRead == null || !inis.TryGetValue(iniRef, out ini)) return null;
            var lines = new List<string>();
            foreach (var section in new[] { "MainPowerSystem", "AuxPowerSystem" })
            {
                var type = _iniRead.Invoke(ini, new object[] { section, "Type", "", false }) as string;
                if (string.IsNullOrEmpty(type) || type.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                var hpText = _iniRead.Invoke(ini, new object[] { section, "HorsePower", "", false }) as string;
                float hp;
                float.TryParse(hpText, NumberStyles.Float, CultureInfo.InvariantCulture, out hp);
                var name = Loc.T("ref.pp." + type.Trim().ToLowerInvariant());
                lines.Add(hp > 0f ? name + " " + Power(hp) + " " + Loc.T("ref.hp") : name);
            }
            return lines.Count == 0 ? null : string.Join("\n", lines.ToArray());
        }

        /// <summary>80000 -> "80,000" (ru: "80 000" with a no-break space).</summary>
        static string Power(float hp)
        {
            var s = Math.Round(hp).ToString("N0", CultureInfo.InvariantCulture);
            return Loc.Language == "ru" ? s.Replace(",", " ") : s;
        }
    }
}
