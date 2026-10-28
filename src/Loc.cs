using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SmartWindows
{
    /// <summary>
    /// Localization. All the mod's texts are in the table below (key, English text); the translations are plain text files
    /// "lang/&lt;code&gt;.ini" in the mod folder, one for each language of the game (the list is the game's own: config.ini [Languages]):
    ///     key = text        (";" starts a comment line; \n in a text is a line break; {0}, {1} are filled in by the mod)
    /// The language is the game's (Options → Language), or [General] Language in the .cfg when it is set. A text missing from the
    /// file is English. The mod creates the files that are missing (the English texts to translate); in an existing file it
    /// appends the keys added later (after "; --- new ---"); it never changes the lines the player has edited.
    /// The settings window's help texts (the .cfg descriptions) are in the files as "help.Section/Key".
    /// </summary>
    static class Loc
    {
        public static Action<string> Trace = s => { };
        /// <summary>The mod's display name, written in the head of each lang file.</summary>
        public const string ModName = "Smart Windows";
        /// <summary>Set from the plugin: the game's assembly (Seapower-Scripts).</summary>
        public static Func<Assembly> GameAsm = () => null;

        static readonly List<string> Keys = new List<string>();
        static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal);
        static Dictionary<string, string> _tr = new Dictionary<string, string>(StringComparer.Ordinal);
        static string _lang = "en", _forced = "";
        static int _checkedAt = -1;
        static bool _filesDone;

        /// <summary>Language from the .cfg ("" = the game's).</summary>
        public static Func<string> Forced = () => "";

        static void D(string key, string en) { Keys.Add(key); En[key] = en; }

        static Loc()
        {
            // The mod's texts: D(<key>, <English text>); \n is a line break, {0} {1} are filled in by Loc.F.
            // The labels, units, group / tab names of the settings window ("lbl." "unit." "grp." "tab.") and the .cfg descriptions ("help.")
            // are added by Loc.Register from the settings table / the config entries.
            // Settings window
            D("ui.title", "Smart Windows - Settings");
            D("ui.help", "Point at a setting to see what it does.");
            D("btn.showAll", "Show all settings");
            D("btn.showFewer", "Show fewer settings");
            D("btn.cancel", "Cancel");
            D("btn.apply", "Apply");
            D("btn.ok", "OK");
            D("tip.resize", "Drag to resize");
            D("val.on", "on");
            D("val.off", "off");
            D("val.empty", "empty");
            D("val.open", "open");
            D("val.closed", "closed");
            D("val.gameLang", "(the game's language)");
            D("val.default", "default: {0}");
            D("val.defaultCap", "Default: {0}");
            D("val.gamePx", "game: \u2248 {0} px");
            D("val.gameOpenTab", "game: open the tab");
            D("st.dirty", "\u25CF = changed, not applied yet   \u21BA = the default");
            D("st.clean", "\u21BA = the default");
            D("tip.default", "The default");
            D("tip.defaultList", "The default list");
            D("btn.addWindow", "+ Add window");
            D("btn.forgetAll", "Forget all positions");
            D("tip.forgetAll", "On Apply the saved position and size of every remembered window is forgotten");
            D("hint.emptyName", "empty name = removed");
            D("info.forgotten", "position forgotten on Apply");
            D("info.at", "{0} x {1} at {2}, {3}");
            D("info.max", "max");
            D("info.notSaved", "position not saved yet");
            D("tip.notSeen", "Not seen yet: the mod writes it when the window is opened or closed in a mission");
            D("tip.asLeft", "As it was left last time; change it here");
            D("tip.keepPos", "Keep the saved position");
            D("tip.forgetPos", "Forget the saved position and size (on Apply): the window opens where the game puts it");
            D("tip.remove", "Remove from the list");
            D("hov.newWindow", "New window");
            D("hov.layoutHelp", "A window to remember. The name is compared with the window's title, name, content type and view-model type, case and spaces ignored, from its beginning: 'Unit Reference*' also matches 'Unit Reference - XYZ'; '*' = any text. Hover a window and press the 'add' key to put it here by itself.");
            D("hov.openHelp", "A window whose open / closed state is restored at the next mission (same naming rules as the remembered windows).");
            D("tip.gear", "Smart Windows settings");
            D("tip.look", "Window opacity and scale");
            D("tab.track", "Track State");
            D("up.button", "Upgrade ship...");
            D("up.title", "Upgrade ship");
            D("up.sell", "Sell");
            D("up.buy", "Buy");
            D("up.crew", "Crew");
            D("up.min", "lowest level");
            D("up.do", "Upgrade");
            D("up.cancel", "Cancel");
            D("up.saleDismiss", "Dismissed for {0} pts");
            D("up.saleDecom", "Decommissioned for {0} pts");
            D("up.aircraft", "{0} assigned aircraft: up to {1} move to the new ship (hangar permitting), the rest return to the unassigned pool.");
            D("up.price", "Price: {0} pts");
            D("up.priceSur", "Price: {0} pts (+{1} scarcity surcharge)");
            D("up.damaged", "The ship is damaged: the repair cost is already deducted from the refund ({0} pts).");
            D("up.points2", "Points: {0} > {1}");
            D("up.fleet", "Fleet value: {0} > {1} / {2}");
            D("up.tip", "Replace a ship of the task force by the selected one: the old ship is sold at the Dismiss price (Decommission when only that is possible), the new one is bought, the crew loses one skill level (Smart Windows).");
            D("up.noSel", "Select a ship in the roster.");
            D("up.noStatus", "The task force is not available here.");
            D("up.hist", "Not available with historical reinforcements.");
            D("up.pending", "Confirm or remove the pending purchases first.");
            D("up.none", "No ship of the task force can be replaced by this one.");
            D("up.points", "not enough points");
            D("up.cap", "over the fleet value cap");
            D("up.item", "sold for {0} pts, crew {1} > {2}");
            D("up.itemDecom", "decommissioned for {0} pts, crew {1} > {2}");
            D("look.opacity", "Opacity");
            D("look.scale", "Scale");
            D("look.outline", "Text outline");
            D("look.level", "Level");
            D("look.level.bottom", "Bottom");
            D("look.level.default", "Default");
            D("look.level.top", "Top");
            D("look.outline.tip", "Shadow around the texts (white for dark text, black for light text) when the window opacity is below 100%");
            // Check boxes the mod adds to the game's windows
            D("ui.armedOnly", "Armed only");
            D("tip.armedOnly", "Hide units without weapons");
            D("ui.strict", "Strict loadouts");
            D("tip.strict", "Task Force Builder offers ship versions and purchases only for ships the mission lists as available or that you already own (Smart Windows).");
        }

        // ------------------------------------------------------------ access

        /// <summary>The text of a key in the current language (English when it is not translated).</summary>
        public static string T(string key)
        {
            Check();
            string v;
            if (_tr.TryGetValue(key, out v) && v.Length > 0) return v;
            return En.TryGetValue(key, out v) ? v : key;
        }

        /// <summary>The text with {0}, {1} ... filled in.</summary>
        public static string F(string key, params object[] a)
        {
            var s = T(key);
            try { return string.Format(CultureInfo.InvariantCulture, s, a); }
            catch { try { return string.Format(CultureInfo.InvariantCulture, En.ContainsKey(key) ? En[key] : key, a); } catch { return s; } }
        }

        /// <summary>The translation of a key if there is one, else the given (English) text; for texts that are not in the table (.cfg descriptions).</summary>
        public static string Or(string key, string fallback)
        {
            Check();
            string v;
            return _tr.TryGetValue(key, out v) && v.Length > 0 ? v : fallback;
        }

        /// <summary>Texts known only at run time (the .cfg descriptions): put in the table so that the files get them.</summary>
        public static void Register(string key, string en)
        {
            if (string.IsNullOrEmpty(key) || En.ContainsKey(key)) return;
            Keys.Add(key); En[key] = en ?? "";
            _filesDone = false;
        }

        public static string Language { get { Check(); return _lang; } }

        /// <summary>The language setting changed: the next text is read in the new language.</summary>
        public static void Refresh() { _checkedAt = -1; }

        static bool _checking;

        static void Check()
        {
            if (_checking) return;
            int now = Environment.TickCount;
            if (_checkedAt >= 0 && now - _checkedAt < 1500 && _filesDone) return;
            _checking = true;
            try
            {
                _checkedAt = now;
                string forced = "";
                try { forced = (Forced() ?? "").Trim(); } catch { }
                string lang = forced.Length > 0 ? forced : GameLanguage();
                if (lang != _lang || _tr.Count == 0 && !_filesDone || forced != _forced)
                {
                    _lang = lang; _forced = forced;
                    _tr = Load(lang);
                }
                if (!_filesDone) { _filesDone = true; try { MakeFiles(); } catch (Exception e) { Trace("lang files: " + e.Message); } _tr = Load(_lang); }
            }
            catch (Exception e) { Trace("Loc: " + e.Message); }
            finally { _checking = false; }
        }

        // ------------------------------------------------------------ the game's language

        static Type _tHandler, _tSingleton;

        static string GameLanguage()
        {
            try
            {
                var asm = GameAsm();
                if (asm == null) return _lang;
                if (_tHandler == null) { _tHandler = asm.GetType("SeaPower.LanguageResourceHandler"); _tSingleton = asm.GetType("SeaPower.Singleton`1"); }
                var inst = _tSingleton.MakeGenericType(_tHandler).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                if (inst == null) return _lang;
                var l = _tHandler.GetProperty("Language").GetValue(inst, null) as string;
                return string.IsNullOrEmpty(l) ? "en" : l;
            }
            catch { return _lang; }
        }

        /// <summary>The languages of the game: its config.ini [Languages] (code → name); English alone when it cannot be read.</summary>
        public static List<KeyValuePair<string, string>> GameLanguages()
        {
            var res = new List<KeyValuePair<string, string>>();
            try
            {
                var asm = GameAsm();
                var utils = asm == null ? null : asm.GetType("SeaPower.Utils");
                MethodInfo open = null;
                if (utils != null)
                    foreach (var m in utils.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        var p = m.GetParameters();
                        if (m.Name == "openIniFile" && p.Length == 4 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string)) { open = m; break; }
                    }
                if (open != null)
                {
                    var ini = open.Invoke(null, new object[] { "", "/config.ini", false, false });
                    var d = ini == null ? null : ini.GetType().GetMethod("GetSectionKeyValues", new[] { typeof(string) }).Invoke(ini, new object[] { "Languages" }) as Dictionary<string, string>;
                    if (d != null)
                        foreach (var kv in d)
                            if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value)) res.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
                }
            }
            catch (Exception e) { Trace("game languages: " + e.Message); }
            if (res.Count == 0) res.Add(new KeyValuePair<string, string>("en", "English"));
            return res;
        }

        // ------------------------------------------------------------ files

        static string _dir;

        public static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    string p = "";
                    try { p = Path.GetDirectoryName(typeof(Loc).Assembly.Location) ?? ""; } catch { }
                    _dir = Path.Combine(p, "lang");
                }
                return _dir;
            }
        }

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }

        static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length) { char n = s[++i]; sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static Dictionary<string, string> Load(string lang)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(lang) || lang.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return d;
            var path = Path.Combine(Dir, lang + ".ini");
            try
            {
                if (!File.Exists(path)) return d;
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    d[line.Substring(0, eq).Trim()] = Unesc(line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception e) { Trace("lang " + lang + ": " + e.Message); }
            return d;
        }

        static void MakeFiles()
        {
            Directory.CreateDirectory(Dir);
            foreach (var l in GameLanguages()) Sync(l.Key, l.Value);
            Sync("en", "English");
            if (_lang != "en" && !File.Exists(Path.Combine(Dir, _lang + ".ini"))) Sync(_lang, _lang);
        }

        /// <summary>Creates the language file, or appends the keys it lacks.</summary>
        static void Sync(string code, string name)
        {
            if (code.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return;
            var path = Path.Combine(Dir, code + ".ini");
            bool english = code == "en";
            if (!File.Exists(path))
            {
                var sb = new StringBuilder();
                sb.AppendLine("; " + ModName + " - texts, " + name + " (" + code + ")");
                sb.AppendLine("; key = text. Edit the text after '='. A line with no translation can stay English or be deleted.");
                sb.AppendLine("; \\n = line break; {0}, {1} ... are filled in by the mod - keep them.");
                sb.AppendLine("; The file is used when the game's language is '" + code + "' (or [General] Language in the .cfg).");
                sb.AppendLine();
                foreach (var k in Keys) sb.AppendLine(k + " = " + Esc(En[k]));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return;
            }
            var have = Load(code);
            var add = new StringBuilder();
            foreach (var k in Keys)
                if (!have.ContainsKey(k) && !HasKeyLine(path, k)) add.AppendLine(k + " = " + Esc(En[k]));
            if (add.Length > 0)
                File.AppendAllText(path, Environment.NewLine + "; --- new ---" + Environment.NewLine + add, new UTF8Encoding(false));
        }

        static HashSet<string> _lineCache; static string _lineCachePath;

        static bool HasKeyLine(string path, string key)
        {
            if (_lineCachePath != path)
            {
                _lineCachePath = path; _lineCache = new HashSet<string>(StringComparer.Ordinal);
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var t = raw.TrimStart(';', '#', ' ');
                    int eq = t.IndexOf('=');
                    if (eq > 0) _lineCache.Add(t.Substring(0, eq).Trim());
                }
            }
            return _lineCache.Contains(key);
        }
    }
}
