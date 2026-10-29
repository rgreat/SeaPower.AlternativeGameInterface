using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;

namespace AlternativeGameInterface
{
    /// <summary>
    /// DumpXaml (1.7.0, for development): switching [Debug] DumpXaml on writes the XAML of every Noesis asset the game has loaded so far under
    /// BepInEx/AlternativeGameInterface_xaml (the asset's own path; new files only) and switches it off again. Assets load when their UI is first shown,
    /// so open the windows of interest (Task Force Builder, Status, ...) first, then switch it on (in the settings window or in the .cfg).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _dumpXaml;
        static readonly HashSet<string> XamlExported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The mod runs from the Steam Workshop folder (steamapps/workshop/content/1286220), not from a local install.</summary>
        static bool IsWorkshopInstall()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(AlternativeGameInterfacePlugin).Assembly.Location) ?? "";
                string path = dir.Replace('/', '\\').TrimEnd('\\') + "\\";
                return path.IndexOf("\\workshop\\content\\", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("\\1286220\\", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        void BindXamlDumpConfig()
        {
            _dumpXaml = Config.Bind("Debug", "DumpXaml", false,
                "For development: switching it on writes the XAML of the game's UI assets loaded so far to BepInEx/AlternativeGameInterface_xaml, then it switches itself off (open the windows you need first).");
        }

        void XamlDumpStep(float now)
        {
            if (_dumpXaml == null || !_dumpXaml.Value) return;
            // one dump per switching on: the switch goes back to false at once (what is loaded at that moment is written)
            _dumpXaml.Value = false;
            try
            {
                string dir = System.IO.Path.Combine(Paths.BepInExRootPath, "AlternativeGameInterface_xaml");
                int n = 0;
                foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<NoesisXaml>())
                {
                    if (x == null || x.content == null || x.content.Length == 0) continue;
                    string uri = string.IsNullOrEmpty(x.uri) ? x.name + ".xaml" : x.uri;
                    if (!XamlExported.Add(uri)) continue;
                    string rel = uri.Replace('\\', '/');
                    int colon = rel.IndexOf(":/", StringComparison.Ordinal);
                    if (colon >= 0) rel = rel.Substring(colon + 2);
                    rel = rel.TrimStart('/');
                    foreach (var bad in System.IO.Path.GetInvalidPathChars()) rel = rel.Replace(bad, '_');
                    string path = System.IO.Path.Combine(dir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    try
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                        System.IO.File.WriteAllBytes(path, x.content);
                        n++;
                    }
                    catch (Exception e) { Trace("DumpXaml " + uri + ": " + e.Message); }
                }
                Trace("DumpXaml: " + n + " new file(s) written to " + dir + " (" + XamlExported.Count + " in all)");
            }
            catch (Exception e) { Trace("DumpXaml: " + e.Message); }
        }
    }
}
