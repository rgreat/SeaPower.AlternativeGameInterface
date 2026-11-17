using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AlternativeGameInterface
{
    /// <summary>
    /// ShowGameVersion (2.3.3): the game's own version watermark ("Sea Power v0.9.3-...", SeaPower.VersionWatermark, a TextMeshPro text): as the game
    /// decides (default), or always shown / hidden, in memory: the game decides it once in VersionWatermark.OnStart (beta branch, or [Debug] ShowVersionWatermark of the
    /// options ini and of config.ini) and deactivates the object when it is not wanted. A postfix on OnStart puts the option's choice over it;
    /// the option switch and the hook time apply it to the instance there is (Singleton&lt;VersionWatermark&gt;._instance, read without creating one).
    /// No file is written.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<string> _showVersion;   // Game / Show / Hide
        static Type _vwType;

        void BindVersionMarkConfig()
        {
            _showVersion = Config.Bind("General", "ShowGameVersion", "Game",
                new ConfigDescription("The game's version watermark: Game = as the game decides (beta branch or [Debug] ShowVersionWatermark), Show / Hide = always shown / hidden (in memory only; config.ini is not changed).",
                new AcceptableValueList<string>("Game", "Show", "Hide")));
            _showVersion.SettingChanged += (s, e) => VersionMarkApply(VersionMarkInstance());
        }

        void PatchVersionMark(Harmony harmony, Assembly asm)
        {
            try
            {
                _vwType = asm.GetType("SeaPower.VersionWatermark");
                var m = _vwType == null ? null : _vwType.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) { Trace("ShowGameVersion: VersionWatermark.OnStart not found"); return; }
                harmony.Patch(m, postfix: Hm("VersionMarkPostfix"));
                Trace("patched VersionWatermark.OnStart (ShowGameVersion)");
                VersionMarkApply(VersionMarkInstance());   // the game's start may have run before the hooks
            }
            catch (Exception e) { Trace("patch ShowGameVersion: " + e.Message); }
        }

        /// <summary>The watermark there is, or null (the singleton's cached field: its Instance getter would make a new one).</summary>
        static object VersionMarkInstance()
        {
            try
            {
                if (_vwType == null) return null;
                var st = _vwType.BaseType;   // Singleton<VersionWatermark>
                var f = st == null ? null : st.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
                var o = f == null ? null : f.GetValue(null) as UnityEngine.Object;
                return o != null ? (object)o : null;   // a destroyed Unity object compares equal to null
            }
            catch { return null; }
        }

        static void VersionMarkPostfix(object __instance)
        {
            var a = Active;
            if ((object)a == null || _vmInGame) return;
            _vmInGame = true;   // the game has just decided: "Game" leaves it so
            try { a.VersionMarkApply(__instance); } finally { _vmInGame = false; }
        }

        /// <summary>"Game": the game's own decision again (its OnStart is run once more: it reads the settings and shows or hides the text itself).</summary>
        void VersionMarkGame(Component comp)
        {
            if (_vmInGame) return;   // inside the game's own OnStart (the postfix): nothing to redo
            _vmInGame = true;
            try { if (!comp.gameObject.activeSelf) comp.gameObject.SetActive(true); _vwType.GetMethod("OnStart").Invoke(comp, null); }
            finally { _vmInGame = false; }
            Trace("ShowGameVersion: as the game decides (" + (comp.gameObject.activeSelf ? "shown" : "hidden") + ")");
        }
        static bool _vmInGame;

        void VersionMarkApply(object inst)
        {
            try
            {
                var comp = inst as Component;
                if (comp == null || _showVersion == null) return;
                string mode = _showVersion.Value;
                if (mode != "Show" && mode != "Hide") { VersionMarkGame(comp); return; }
                bool on = mode == "Show";
                if (on)
                {
                    var tmpType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                    var tmp = tmpType == null ? null : comp.GetComponent(tmpType);
                    var p = tmp == null ? null : tmp.GetType().GetProperty("text");
                    if (p != null) p.SetValue(tmp, Application.productName + " v" + Application.version, null);   // the game's own text (OnStart sets it only when it shows it)
                }
                if (comp.gameObject.activeSelf != on) comp.gameObject.SetActive(on);
                Trace("ShowGameVersion: the version watermark " + (on ? "shown" : "hidden"));
            }
            catch (Exception e) { Trace("ShowGameVersion: " + e.Message); }
        }
    }
}
