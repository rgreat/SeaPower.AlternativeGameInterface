using System;
using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace AlternativeGameInterface
{
    /// <summary>
    /// World View key (1.7.1), desktop mode: one key opens and closes the World View window (the same state as its X button and the "World View" item of
    /// the Tactical Display's menu). It never overrides the game's keys: the game's own bindings (InputHandler.KeyBindings, as set in Controls - also after
    /// the player has changed them) and the keys the game reads directly are checked, and a key that is already used is ignored (with a warning in the log).
    /// The key does nothing while the console, a menu, a text box or a rebinding has the keyboard, or while Shift / Ctrl / Alt is held.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<KeyCode> _wvKey;
        string _wvKeyBad;      // the conflict last reported (not repeated)

        // keys the game reads directly (Input.GetKeyDown), not through its binding list
        static readonly KeyCode[] DirectKeys =
        {
            KeyCode.BackQuote, KeyCode.Delete, KeyCode.DownArrow, KeyCode.UpArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.E, KeyCode.F, KeyCode.S,
            KeyCode.W, KeyCode.Y, KeyCode.Z, KeyCode.F11, KeyCode.PageDown, KeyCode.PageUp, KeyCode.Escape, KeyCode.Return, KeyCode.KeypadEnter
        };

        void BindWorldViewKeyConfig()
        {
            _wvKey = Config.Bind("General", "WorldViewKey", KeyCode.Insert,
                "Desktop mode: the key that opens and closes the World View window. A key the game already uses (Controls - its bindings, also the ones you changed) is never taken: " +
                "it is ignored and the log says so. None = no key. It does nothing while the console, a menu or a text box has the keyboard, or while Shift / Ctrl / Alt is held.");
        }

        static object GameInput()
        {
            try
            {
                var asm = GameAssembly();
                var ih = asm == null ? null : asm.GetType("SeaPower.InputHandler");
                if (ih == null) return null;
                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
                var exists = ih.GetMethod("InstanceExists", sf, null, new[] { typeof(bool) }, null);
                var instP = ih.GetProperty("Instance", sf);
                if (exists == null || instP == null || !(bool)exists.Invoke(null, new object[] { false })) return null;
                return instP.GetValue(null, null);
            }
            catch { return null; }
        }

        /// <summary>A field or a property of the game's object (the input handler keeps its state in public fields).</summary>
        static object Fld(object o, string name)
        {
            try
            {
                if (o == null) return null;
                const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var t = o.GetType();
                var fi = t.GetField(name, f);
                if (fi != null) return fi.GetValue(o);
                var pi = t.GetProperty(name, f);
                return pi == null ? null : pi.GetValue(o, null);
            }
            catch { return null; }
        }

        static bool FldB(object o, string name) { var v = Fld(o, name); return v is bool && (bool)v; }

        /// <summary>The console, a menu, a rebinding or a text box has the keyboard: the game's hot keys are off, so are ours.</summary>
        bool GameKeysOff(object ih)
        {
            try
            {
                if (ih == null) return true;
                if (FldB(ih, "consoleOpen") || FldB(ih, "menuOpen") || FldB(ih, "typingActive")) return true;
                var rb = Fld(ih, "RebindMode");
                var v = rb == null ? null : Fld(rb, "Value");
                return v is bool && (bool)v;
            }
            catch { return true; }
        }

        /// <summary>What the key is used for in the game (an action name), or null.</summary>
        string GameKeyConflict(object ih, KeyCode key)
        {
            foreach (var k in DirectKeys) if (k == key) return "a key the game reads directly";
            var list = ih == null ? null : Fld(ih, "KeyBindings") as IEnumerable;
            if (list == null) return null;
            foreach (var kb in list)
            {
                var codes = kb == null ? null : Fld(kb, "keyCodes") as IEnumerable;
                if (codes == null) continue;
                foreach (var c in codes)
                    if (c is KeyCode && (KeyCode)c == key) { var a = Fld(kb, "KeyAction"); return a == null ? "a game action" : a.ToString(); }
            }
            return null;
        }

        void WorldViewKeyStep()
        {
            if (_wvKey == null || _wvKey.Value == KeyCode.None || _windowedMap == null || !_windowedMap.Value) return;
            if (!Input.GetKeyDown(_wvKey.Value)) return;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;
            if (!InMission()) return;
            var ih = GameInput();
            if (GameKeysOff(ih)) return;
            var conflict = GameKeyConflict(ih, _wvKey.Value);
            if (conflict != null)
            {
                string msg = "World View key " + _wvKey.Value + " is used by the game (" + conflict + "): ignored. Choose another key in the settings (Desktop mode).";
                if (_wvKeyBad != msg) { _wvKeyBad = msg; Logger.LogWarning("Alternative Game Interface: " + msg); }
                return;
            }
            var cur = WorldViewOpenNow();
            if (!cur.HasValue) return;
            SetWorldViewOpen(!cur.Value);
            _wvOpenLive.Value = cur.Value ? "closed" : "open";      // the state of the active desktop
            Config.Save();
            Trace("World View " + (cur.Value ? "closed" : "opened") + " by the key " + _wvKey.Value);
        }
    }
}
