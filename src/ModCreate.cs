using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Create Mod window (Mods menu): with "Update Existing" and a Workshop item chosen, Pick Folder and Pick Image are filled in at once - the folder
    /// of the mod in StreamingAssets (a top-level folder with an _info.ini whose name is the item's title, or whose folder name is the title without spaces)
    /// and its picture (thumbnail.jpg / .png, up to the game's 1 MB limit).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _modCreateFill;
        static FieldInfo _mcPreviewPath;

        void BindModCreateConfig()
        {
            _modCreateFill = Config.Bind("General", "ModCreateFill", true,
                "Create Mod window: with Update Existing, choosing a Workshop item fills in Pick Folder (the mod's folder in StreamingAssets) and Pick Image (its thumbnail.jpg).");
        }

        void HookModCreate(Harmony harmony, Assembly asm)
        {
            var t = asm.GetType("SeapowerUI.ViewModels.CreateModMenuViewModel");
            var p = t == null ? null : t.GetProperty("SelectedUserCreatedMod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var set = p == null ? null : p.GetSetMethod(true);
            _mcPreviewPath = t == null ? null : t.GetField("previewImagePath", BindingFlags.Instance | BindingFlags.NonPublic);
            if (set != null && _mcPreviewPath != null) { TryPatch(harmony, set, null, "ModCreateSelectedPostfix", null); Trace("patched CreateModMenuViewModel.SelectedUserCreatedMod"); }
            else Trace("CreateModMenuViewModel not found: ModCreateFill not available");
        }

        static string Plain(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in s ?? "") if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        /// <summary>Names written in the _info.ini of a mod (Name= of every Language_ section).</summary>
        static List<string> InfoNames(string ini)
        {
            var names = new List<string>();
            try
            {
                bool lang = false;
                foreach (var raw in File.ReadAllLines(ini))
                {
                    var l = raw.Trim();
                    if (l.StartsWith("[")) { lang = l.StartsWith("[Language_", StringComparison.OrdinalIgnoreCase); continue; }
                    if (lang && l.StartsWith("Name=", StringComparison.OrdinalIgnoreCase)) names.Add(l.Substring(5).Trim());
                }
            }
            catch { }
            return names;
        }

        static string FindModFolder(string title)
        {
            string want = Plain(title);
            if (want.Length == 0) return null;
            string byName = null;
            foreach (var dir in Directory.GetDirectories(Application.streamingAssetsPath))
            {
                string leaf = Path.GetFileName(dir);
                if (string.Equals(leaf, "original", StringComparison.OrdinalIgnoreCase)) continue;
                string ini = Path.Combine(dir, "_info.ini");
                if (!File.Exists(ini)) continue;
                if (Plain(leaf) == want) return dir;
                if (byName == null) foreach (var n in InfoNames(ini)) if (Plain(n) == want) { byName = dir; break; }
            }
            return byName;
        }

        static Texture2D _mcTex;

        static Texture2D LoadPicture(string path)
        {
            try
            {
                var tex = new Texture2D(2, 2);
                Type ic = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { ic = asm.GetType("UnityEngine.ImageConversion"); if (ic != null) break; }
                var load = ic == null ? null : ic.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (load != null && (bool)load.Invoke(null, new object[] { tex, File.ReadAllBytes(path) })) return tex;
                UnityEngine.Object.Destroy(tex);
            }
            catch { }
            return null;
        }

        static void ModCreateSelectedPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null || a._modCreateFill == null || !a._modCreateFill.Value) return;
            try
            {
                var sel = a.Get(__instance, "SelectedUserCreatedMod");
                if (sel == null || !(a.Get(__instance, "UpdateExisting") is bool) || !(bool)a.Get(__instance, "UpdateExisting")) return;
                var title = sel.GetType().GetProperty("Item2").GetValue(sel, null) as string;
                string folder = FindModFolder(title);
                if (folder == null) { Trace("Create Mod: no local folder for '" + title + "'"); return; }
                a.SetV(__instance, "FolderPath", folder);
                Trace("Create Mod: folder " + folder);
                // the named picture first; else any picture of the folder that is 512 x 512 (the Workshop's size) and within the game's 1 MB limit
                var order = new List<string>();
                foreach (var n in new[] { "thumbnail.jpg", "thumbnail.jpeg", "thumbnail.png", "preview.jpg", "preview.png" }) order.Add(Path.Combine(folder, n));
                var rest = new List<string>();
                foreach (var f in Directory.GetFiles(folder))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext == ".jpg" || ext == ".jpeg" || ext == ".png") && !order.Exists(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase))) rest.Add(f);
                }
                rest.Sort(StringComparer.OrdinalIgnoreCase);
                int named = order.Count; order.AddRange(rest);
                for (int k = 0; k < order.Count; k++)
                {
                    var f = order[k];
                    if (!File.Exists(f) || new FileInfo(f).Length > 1000000) continue;
                    var tex = LoadPicture(f);
                    if (tex == null) continue;
                    if (k >= named && (tex.width != 512 || tex.height != 512)) { UnityEngine.Object.Destroy(tex); continue; }
                    a.SetV(__instance, "PreviewImage", new Noesis.TextureSource(tex));
                    var old = _mcTex; _mcTex = tex;
                    if (old != null) { try { UnityEngine.Object.Destroy(old); } catch { } }      // the picture of the previous choice
                    _mcPreviewPath.SetValue(__instance, f);
                    Trace("Create Mod: image " + f + " " + tex.width + "x" + tex.height);
                    return;
                }
                Trace("Create Mod: no picture in " + folder);
            }
            catch (Exception e) { Trace("Create Mod fill: " + e.Message); }
        }
    }
}
