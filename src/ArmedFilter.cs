using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Armed only (with ImprovedUnitReferenceUI): an "Armed only" check box on the search panel of the Unit Reference tree (next to the game's
    /// "Filter by date" / "Search by name only"). On: units without weapons (decoys, sonobuoys, fuel tanks do not count) are
    /// left out of the tree. Done after the game's filters (postfix of UnitTreeBrowserViewModel.UpdateFilteredMuseumEntries,
    /// encyclopedia browser only) the same way they work: new FilteredMuseumEntries collections, the units' data untouched.
    /// The box's state is kept in [UnitReference] ArmedOnly.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _armOnly, _natCount;
        MethodInfo _updateFiltered, _saveExpansion;

        sealed class ArmState { public bool Done; public int Tries; public CheckBox Box; public object CountBox; }
        static readonly ConditionalWeakTable<object, ArmState> ArmStates = new ConditionalWeakTable<object, ArmState>();

        void HookArmedFilter(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var utb = asm.GetType("SeapowerUI.ViewModels.UnitTreeBrowserViewModel");
            _updateFiltered = utb == null ? null : utb.GetMethod("UpdateFilteredMuseumEntries", inst, null, Type.EmptyTypes, null);
            _saveExpansion = utb == null ? null : utb.GetMethod("SaveExpansionState", inst);
            if (_updateFiltered == null) { Logger.LogWarning("UnitTreeBrowserViewModel.UpdateFilteredMuseumEntries not found (game updated?); the Armed only filter is not available."); return; }
            harmony.Patch(_updateFiltered, postfix: Hm("FilteredEntriesPostfix"));
            Trace("patched UnitTreeBrowserViewModel.UpdateFilteredMuseumEntries");
        }

        static void FilteredEntriesPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null) return;
            if (a._improvedUI.Value && a._armOnly.Value)
            {
                try { a.FilterArmed(__instance); }
                catch (Exception e) { Trace("FilterArmed: " + e); }
            }
            if (a._improvedUI.Value)
            {
                try { var keep = a.SourcePredicate(__instance); if (keep != null) a.FilterBy(__instance, keep, "sources"); }
                catch (Exception e) { Trace("FilterSources: " + e); }
            }
            try { a.AfterFilters(__instance); }
            catch (Exception e) { Trace("AfterFilters: " + e); }
        }

        void FilterArmed(object browser) { FilterBy(browser, IsArmed, "armed only"); }

        /// <summary>Leaves out the units that fail <paramref name="ok"/> (folders that end up empty go too): new FilteredMuseumEntries collections.</summary>
        void FilterBy(object browser, Func<object, bool> ok, string what)
        {
            if (_browserInitializing != null && _browserInitializing.GetValue(browser) is bool && (bool)_browserInitializing.GetValue(browser)) return;
            if (!IsEncyclopediaBrowser(browser)) return;
            var root = Get(browser, "FilteredMuseumEntries") as IList;
            if (root == null) return;
            var kept = Activator.CreateInstance(root.GetType()) as IList;   // ObservableCollection<MuseumEntry>
            int dropped = 0;
            foreach (var cat in root) if (KeepBy(cat, ok, ref dropped)) kept.Add(cat);
            SetV(browser, "FilteredMuseumEntries", kept);
            Verbose(what + ": " + dropped + " unit(s) left out");
        }

        /// <summary>A folder keeps its armed entries (FilteredMuseumEntries replaced) and stays if any are left; a unit stays if armed.</summary>
        bool KeepBy(object e, Func<object, bool> ok, ref int dropped)
        {
            if (e == null) return false;
            if (e.GetType().Name != "MuseumEntry") { if (ok(e)) return true; dropped++; return false; }
            var kids = Get(e, "FilteredMuseumEntries") as IList;
            if (kids == null || kids.Count == 0) return true;   // an empty folder: as the game has it
            var kept = Activator.CreateInstance(kids.GetType()) as IList;
            foreach (var k in kids) if (KeepBy(k, ok, ref dropped)) kept.Add(k);
            if (kept.Count == kids.Count) return true;
            SetV(e, "FilteredMuseumEntries", kept);
            return kept.Count > 0;
        }

        static bool IsUnitEntry(object e)
        {
            for (var b = e.GetType(); b != null; b = b.BaseType) if (b.Name == "MuseumObjectEntry") return true;
            return false;
        }

        static readonly string[] NotArms = { "Noisemaker", "Chaff", "None", "Sonobuoy", "Fueltank", "Paratrooper", "MOSS" };

        /// <summary>A unit is armed when its weapons (MuseumObjectEntry.WeaponEntries: the ammunition of all its loadouts and weapon
        /// systems, as the Weapons tab lists them) hold anything but decoys, sonobuoys, fuel tanks, paratroopers. Not a unit: kept.</summary>
        bool IsArmed(object e)
        {
            if (!IsUnitEntry(e)) return true;   // weapons, sensors
            var list = Get(e, "WeaponEntries") as IEnumerable;
            if (list == null) return false;
            foreach (var w in list)
            {
                var ty = w == null ? null : TextOf(Get(w, "Type"));
                if (ty != null && Array.IndexOf(NotArms, ty) < 0) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- the check box

        /// <summary>Each frame for a Unit Reference window on screen: adds the check box once its search panel is there.</summary>
        void ArmedBoxStep(object w, object vm)
        {
            var st = ArmStates.GetOrCreateValue(w);
            if (st.Done) return;
            var browser = vm == null ? null : Get(vm, "Browser");
            if (browser == null) return;
            // the game's check boxes were caught when they loaded (ViewCapture): no search
            VmParts parts;
            if (!PartsByVm.TryGetValue(vm, out parts)) return;
            var date = PartOf(parts.Date) as ToggleButton;
            var name = PartOf(parts.Name) as ToggleButton;
            if (date == null) return;
            if (name == null && ++st.Tries < 10) return;   // the name box loads next to the date box: a moment's wait for it
            st.Done = true;
            Trace("armed only: date box " + date.GetType().Name + " in " + Describe(VisualTreeHelper.GetParent(date)) + (name != null ? ", name box in " + Describe(VisualTreeHelper.GetParent(name)) : ", no name box"));
            var last = name ?? date;
            var parent = VisualTreeHelper.GetParent(last) as Panel;
            if (parent == null) { Trace("armed only: the date box is in " + (VisualTreeHelper.GetParent(last) == null ? "nothing" : VisualTreeHelper.GetParent(last).GetType().Name) + ", not a panel"); return; }

            var box = new CheckBox { Content = Loc.T("ui.armedOnly"), IsChecked = _armOnly.Value };
            if (last is CheckBox) box.Style = last.Style;
            box.Margin = last.Margin;
            box.Padding = last.Padding;
            box.FontSize = last.FontSize;
            box.Foreground = last.Foreground;
            box.VerticalAlignment = last.VerticalAlignment;
            box.HorizontalAlignment = last.HorizontalAlignment;
            try { ToolTipService.SetToolTip(box, Loc.T("tip.armedOnly")); } catch { }
            box.Click += (o, e) => { try { SetArmedOnly(browser, box.IsChecked == true); } catch (Exception ex) { Trace("armed only: " + ex.Message); } };

            PlaceBox(parent, date, last, box);
            st.Box = box;
            Trace("armed only: check box added to " + parent.GetType().Name);

            // the country drop-down is remembered: its items are drawn again when the counts change; its last item switches the sort (SyncNations)
            try
            {
                UIElement root = date;
                for (var up = VisualTreeHelper.GetParent(date); up != null; up = VisualTreeHelper.GetParent(up)) root = up as UIElement ?? root;
                var nations = Get(browser, "Nations");
                var combo = FindFirst(root, d => { var c = d as ComboBox; return c != null && nations != null && ReferenceEquals(c.ItemsSource, nations); }) as ComboBox;
                if (combo == null) { Trace("countries by count: the country drop-down was not found"); return; }
                var cp = VisualTreeHelper.GetParent(combo) as StackPanel;
                if (cp == null) { Trace("countries by count: the country drop-down is not in a stack panel"); return; }
                st.CountBox = null;
                BuildCountryMenu(browser, combo, cp);
                Trace("countries: the drop-down of the mod replaces the game's combo box");
                if (_updateFiltered != null) Invoke(_updateFiltered, browser);   // the counts show at once
            }
            catch (Exception ex) { Trace("countries by count: " + ex.Message); }
        }

        /// <summary>Puts a new check box after "last" in the panel of the game's boxes (the next cell of the grid in the direction the boxes run, or the next child).</summary>
        static void PlaceBox(Panel parent, UIElement date, UIElement last, CheckBox box)
        {
            var grid = parent as Grid;
            if (grid != null && (date != last))
            {
                // two boxes in a grid: the next cell in the direction they run (a new Auto row / column)
                int r1 = Grid.GetRow(date), c1 = Grid.GetColumn(date), r2 = Grid.GetRow(last), c2 = Grid.GetColumn(last);
                if (r1 == r2 && c2 > c1)
                {
                    grid.ColumnDefinitions.Insert(c2 + 1, new ColumnDefinition { Width = GridLength.Auto });
                    foreach (var ch in grid.Children) { var u = ch as UIElement; if (u != null && Grid.GetColumn(u) > c2) Grid.SetColumn(u, Grid.GetColumn(u) + 1); }
                    Grid.SetRow(box, r2); Grid.SetColumn(box, c2 + 1);
                }
                else
                {
                    grid.RowDefinitions.Insert(r2 + 1, new RowDefinition { Height = GridLength.Auto });
                    foreach (var ch in grid.Children) { var u = ch as UIElement; if (u != null && Grid.GetRow(u) > r2) Grid.SetRow(u, Grid.GetRow(u) + 1); }
                    Grid.SetRow(box, r2 + 1); Grid.SetColumn(box, c2);
                }
                grid.Children.Add(box);
            }
            else if (grid != null)
            {
                grid.RowDefinitions.Insert(Grid.GetRow(last) + 1, new RowDefinition { Height = GridLength.Auto });
                foreach (var ch in grid.Children) { var u = ch as UIElement; if (u != null && Grid.GetRow(u) > Grid.GetRow(last)) Grid.SetRow(u, Grid.GetRow(u) + 1); }
                Grid.SetRow(box, Grid.GetRow(last) + 1); Grid.SetColumn(box, Grid.GetColumn(last));
                grid.Children.Add(box);
            }
            else parent.Children.Insert(parent.Children.IndexOf(last) + 1, box);
        }

        void SetNatCount(object browser, bool on)
        {
            if (_natCount.Value != on) { _natCount.Value = on; Config.Save(); }
            if (_updateFiltered != null) Invoke(_updateFiltered, browser);   // the game's filters run again, the country list follows (AfterFilters)
        }

        void SetArmedOnly(object browser, bool on)
        {
            if (_armOnly.Value != on) { _armOnly.Value = on; Config.Save(); }
            // as the game's own filter switches do: keep the expansion, then filter again
            if (_saveExpansion != null && !GetB(browser, "IsSearching")) Invoke(_saveExpansion, browser, Get(browser, "FilteredMuseumEntries"));
            if (_updateFiltered != null) Invoke(_updateFiltered, browser);
        }
    }
}
