using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;
using Path = System.IO.Path;

namespace SmartWindows
{
    /// <summary>
    /// Menus and files (1.6.0):
    ///  - HideCustomMission: "_Custom Mission" (the mission editor's default file) is left out of the SCENARIOS list (postfix of
    ///    ScenarioMenuViewModel.GetMissions);
    ///  - RememberMissionFolder: the folder of the last mission loaded in the mission editor is kept in the .cfg and opened by the editor's
    ///    "open mission" file picker (postfix of FilePickerViewModel.Initialise(), prefix of MissionEditorViewModel.LoadMission);
    ///  - ColorIniFiles: *.ini names in the mission editor's file pickers are light blue;
    ///  - DeleteOldLogs: at start the game's logs of earlier sessions (Logs\*_session_*, *_sessionEventLog_*) are deleted, the current ones stay.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _hideCustom, _rememberFolder, _blueIni, _deleteLogs;
        ConfigEntry<string> _lastMissionDir;
        PropertyInfo _pickerMode, _pickerCtx;
        FieldInfo _pickerInitial, _pickerHome;
        bool _logsDone;

        void BindPickersConfig()
        {
            _hideCustom = Config.Bind("General", "HideCustomMission", true,
                "Main menu: the editor's default file \"_Custom Mission\" is not shown in the SCENARIOS list.");
            _rememberFolder = Config.Bind("MissionEditor", "RememberMissionFolder", true,
                "Mission editor: the file picker for opening a mission starts in the folder of the mission opened last time.");
            _lastMissionDir = Config.Bind("MissionEditor", "LastMissionFolder", "", "The folder of the mission opened last in the mission editor (kept by the mod).");
            _blueIni = Config.Bind("General", "ColorIniFiles", true, "Mission editor file pickers: names of *.ini files are light blue.");
            _deleteLogs = Config.Bind("General", "DeleteOldLogs", false,
                "At the start of the game the log files of earlier sessions (the game's Logs folder: *_session_*, *_sessionEventLog_*) are deleted; this session's stay.");
        }

        void HookPickers(Harmony harmony, Assembly asm)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var smvm = asm.GetType("SeapowerUI.ViewModels.ScenarioMenuViewModel");
            var gm = smvm == null ? null : smvm.GetMethod("GetMissions", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (gm != null) { TryPatch(harmony, gm, null, "MissionsPostfix", null); Trace("patched ScenarioMenuViewModel.GetMissions"); }
            else Trace("ScenarioMenuViewModel.GetMissions not found: HideCustomMission not available");

            var fp = asm.GetType("SeapowerUI.ViewModels.FilePickerViewModel");
            _pickerMode = fp == null ? null : fp.GetProperty("FilePickerModeSetting", inst);
            _pickerCtx = fp == null ? null : fp.GetProperty("Context", inst);
            _pickerInitial = fp == null ? null : fp.GetField("initialPath", inst);
            _pickerHome = fp == null ? null : fp.GetField("homePath", inst);
            var init = fp == null ? null : fp.GetMethod("Initialise", inst, null, Type.EmptyTypes, null);
            if (init != null && _pickerMode != null && _pickerInitial != null) { TryPatch(harmony, init, null, "PickerInitPostfix", null); Trace("patched FilePickerViewModel.Initialise"); }
            else Trace("FilePickerViewModel.Initialise not found: RememberMissionFolder not available");
            var mevm = asm.GetType("SeapowerUI.ViewModels.MissionEditorViewModel");
            var load = mevm == null ? null : mevm.GetMethod("LoadMission", inst);
            if (load != null) TryPatch(harmony, load, "LoadMissionPrefix", null, null);
            else Trace("MissionEditorViewModel.LoadMission not found: RememberMissionFolder not available");
        }

        // ---------------------------------------------------------------- _Custom Mission

        static void MissionsPostfix(object __result)
        {
            var a = Active;
            if ((object)a == null || !a._hideCustom.Value) return;
            var list = __result as IList;
            if (list == null) return;
            try
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var f = a.Get(list[i], "FileName") as string;
                    if (f != null && string.Equals(Path.GetFileName(f), "_Custom Mission.ini", StringComparison.OrdinalIgnoreCase)) list.RemoveAt(i);
                }
            }
            catch (Exception e) { Trace("HideCustomMission: " + e.Message); }
        }

        // ---------------------------------------------------------------- the editor's open-mission folder

        static void LoadMissionPrefix(string filename)
        {
            var a = Active;
            if ((object)a == null || !a._rememberFolder.Value || string.IsNullOrEmpty(filename)) return;
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(filename));
                if (dir == null || dir.IndexOf("_temp", StringComparison.OrdinalIgnoreCase) >= 0) return;
                var home = Path.GetFullPath(UnityEngine.Application.streamingAssetsPath);
                if (!dir.StartsWith(home, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(dir)) return;
                if (a._lastMissionDir.Value != dir) { a._lastMissionDir.Value = dir; a.Config.Save(); }
            }
            catch (Exception e) { Trace("RememberMissionFolder: " + e.Message); }
        }

        static void PickerInitPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null || !a._rememberFolder.Value) return;
            try
            {
                var mode = a._pickerMode.GetValue(__instance, null);
                if (mode == null || mode.ToString() != "OpenFile") return;
                var initial = a._pickerInitial.GetValue(__instance) as DirectoryInfo;
                if (initial == null) return;
                var n = initial.FullName.Replace('\\', '/').TrimEnd('/');
                if (!n.EndsWith("user/missions", StringComparison.OrdinalIgnoreCase)) return;   // the editor's open-mission picker only
                var last = a._lastMissionDir.Value;
                if (string.IsNullOrEmpty(last) || !Directory.Exists(last)) return;
                var home = a._pickerHome == null ? null : a._pickerHome.GetValue(__instance) as DirectoryInfo;
                if (home != null && !last.StartsWith(home.FullName, StringComparison.OrdinalIgnoreCase)) return;
                a.SetV(__instance, "SelectedFolder", new DirectoryInfo(last));
                a.Verbose("mission picker opened in " + last);
            }
            catch (Exception e) { Trace("RememberMissionFolder: " + e.Message); }
        }

        // ---------------------------------------------------------------- blue ini files

        sealed class PickerState { public float Next, WalkNext; public bool Picker, Known; public WeakReference List; }
        static readonly ConditionalWeakTable<object, PickerState> PickerStates = new ConditionalWeakTable<object, PickerState>();
        Brush _iniBrush;

        /// <summary>Each frame for each window: a file picker's list is checked every frame (rows are made while scrolling).</summary>
        void PickerStep(object w, float now)
        {
            if (!_blueIni.Value) return;
            var st = PickerStates.GetOrCreateValue(w);
            var fe = w as FrameworkElement;
            if (fe == null) return;
            if (now >= st.Next)
            {
                st.Next = now + 0.4f;
                if (!st.Known)
                {
                    var c = Get(w, "Content");
                    foreach (var o in new[] { Get(w, "DataContext"), c, c == null ? null : Get(c, "DataContext") })
                        if (o != null && TypeName(o) == "FilePickerViewModel")
                        {
                            var ctx = _pickerCtx == null ? null : _pickerCtx.GetValue(o, null);
                            st.Picker = ctx != null && TypeName(ctx) == "MissionEditorView";   // the mission editor's pickers only
                            st.Known = true;
                            break;
                        }
                }
            }
            if (!st.Picker || !fe.IsVisible) return;
            var list = st.List == null ? null : st.List.Target as ListView;
            if (list == null || VisualTreeHelper.GetParent(list) == null)
            {
                list = FindDescendant<ListView>(fe);
                st.List = list == null ? null : new WeakReference(list);
                if (list == null) return;
            }
            if (_iniBrush == null) _iniBrush = new SolidColorBrush(Color.FromRgb(110, 184, 255));
            if (now < st.WalkNext) return;   // the rows are walked ten times a second (they are made while scrolling)
            st.WalkNext = now + 0.1f;
            var stack = new Stack<DependencyObject>();
            stack.Push(list);
            int budget = 6000;
            while (stack.Count > 0 && --budget > 0)
            {
                var e = stack.Pop();
                var tb = e as TextBlock;
                if (tb != null)
                {
                    // stateless: rows are reused while scrolling and Noesis may hand out a new wrapper for the same element
                    bool ini = tb.Text != null && tb.Text.EndsWith(".ini", StringComparison.OrdinalIgnoreCase);
                    var sb = tb.Foreground as SolidColorBrush;
                    bool blue = sb != null && (ReferenceEquals(sb, _iniBrush) || (sb.Color.R == 110 && sb.Color.G == 184 && sb.Color.B == 255));
                    if (ini && !blue) tb.Foreground = _iniBrush;
                    else if (!ini && blue) tb.ClearValue(TextBlock.ForegroundProperty);
                    continue;
                }
                int n = VisualTreeHelper.GetChildrenCount(e);
                for (int k = n - 1; k >= 0; k--) { var ch = VisualTreeHelper.GetChild(e, k); if (ch != null) stack.Push(ch); }
            }
        }

        // ---------------------------------------------------------------- old logs

        void CleanLogsOnce()
        {
            if (_logsDone) return;
            _logsDone = true;
            if (!_deleteLogs.Value) return;
            try
            {
                var dir = Path.Combine(UnityEngine.Application.dataPath, "Logs");
                if (!Directory.Exists(dir)) return;
                var start = System.Diagnostics.Process.GetCurrentProcess().StartTime.AddMinutes(-1);
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var log = GameAssembly() == null ? null : GameAssembly().GetType("SeaPower.Log");
                foreach (var n in new[] { "CurrentLogFileName", "CurrentEventLogFileName" })
                {
                    var p = log == null ? null : log.GetProperty(n, BindingFlags.Static | BindingFlags.Public);
                    var v = p == null ? null : p.GetValue(null, null) as string;
                    if (!string.IsNullOrEmpty(v)) keep.Add(Path.GetFullPath(v));
                }
                int n1 = 0;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var name = Path.GetFileName(f);
                    if (name.IndexOf("_session", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (keep.Contains(Path.GetFullPath(f)) || File.GetLastWriteTime(f) >= start) continue;
                    try { File.Delete(f); n1++; } catch { }
                }
                Trace("old logs: " + n1 + " file(s) deleted from " + dir);
            }
            catch (Exception e) { Trace("DeleteOldLogs: " + e.Message); }
        }
    }
}
