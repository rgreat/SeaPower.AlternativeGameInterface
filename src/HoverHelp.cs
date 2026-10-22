using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using Noesis;

namespace SmartWindows
{
    /// <summary>
    /// LinkHelp (1.6.0): the highlighted links of a Unit Reference page (weapons, sensors, guns, launchers, systems: Buttons of the
    /// HyperlinkButton style whose CommandParameter is the entry the link opens) get a tooltip with that entry's short help - the first
    /// part of its Description - when it has one. Set when a Button loads or the mouse enters it (ViewCapture): no search through the tree;
    /// a link gets its tooltip once and again if its entry changes.
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _linkHelp;

        static readonly ConditionalWeakTable<object, object> HelpTipped = new ConditionalWeakTable<object, object>();   // button -> its entry
        static readonly Regex HelpBreaks = new Regex("<\\s*(LineBreak|br)\\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex HelpTags = new Regex("<[^>]*>", RegexOptions.Compiled);

        void BindLinkHelpConfig()
        {
            _linkHelp = Config.Bind("UnitReference", "LinkHelp", true,
                "Unit Reference: a tooltip with the short help (the start of the description) of the weapon, sensor or system on the highlighted links, when it has one.");
        }

        /// <summary>A link (Button) has loaded or the mouse has entered it: gives it the tooltip with the entry's short help, once (again if its entry changes).</summary>
        bool TipButton(Button b)
        {
            var entry = b.CommandParameter;
            if (entry == null) return false;
            object old;
            if (HelpTipped.TryGetValue(b, out old) && ReferenceEquals(old, entry)) return false;
            string tn = entry.GetType().Name;
            if (!tn.StartsWith("Museum", StringComparison.Ordinal) || !tn.EndsWith("Entry", StringComparison.Ordinal)) return false;
            for (var t = entry.GetType(); t != null; t = t.BaseType) if (t.Name == "MuseumObjectEntry") return false;   // units: not a system / weapon
            HelpTipped.Remove(b);
            HelpTipped.Add(b, entry);
            var text = HelpText(Get(entry, "Description") as string);
            if (text == null) { try { ToolTipService.SetToolTip(b, null); } catch { } return false; }
            var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380f };
            ToolTipService.SetToolTip(b, tb);
            return true;
        }

        static string HelpText(string formatted)
        {
            if (string.IsNullOrWhiteSpace(formatted)) return null;
            var s = HelpBreaks.Replace(formatted, "\n");
            s = HelpTags.Replace(s, "");
            s = s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'");
            s = Regex.Replace(s, "[ \\t]+", " ");
            s = Regex.Replace(s, "\\s*\\n\\s*(\\n\\s*)+", "\n\n").Trim();
            if (s.Length == 0) return null;
            const int max = 480;
            if (s.Length > max)
            {
                int cut = s.LastIndexOfAny(new[] { ' ', '\n' }, max);
                s = s.Substring(0, cut > 200 ? cut : max).TrimEnd() + " ...";
            }
            return s;
        }
    }
}
