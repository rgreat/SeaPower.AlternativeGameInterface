using System;
using System.Collections.Generic;
using System.Reflection;
using Noesis;

namespace AlternativeGameInterface
{
    /// <summary>
    /// The gear of the World View window (the 3D picture) also reaches the settings of the other rgreat mods that are installed and ticked
    /// (only Alternative Special Effects, the 3D effects, is offered): with it around, the gear offers the list "Game Interface / Special Effects";
    /// alone, it opens our settings at once. The mods are read from the settings hub registry (protocol v1, AppDomain data; BCL types only), the list
    /// is the hub's own (SettingsHub.ShowList), so SettingsHub.cs stays the unchanged shared copy.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        const string HubRegistryKey = "rgreat.settings.v1", OwnHubId = "alternative-game-interface";

        /// <summary>The other active mods of the registry plus our own entry (the gear's own opening); false when there is no other mod or the list cannot be shown.</summary>
        static bool ChooseSettings(UIElement anchor, Action openOwn)
        {
            try
            {
                var reg = AppDomain.CurrentDomain.GetData(HubRegistryKey) as List<object[]>;
                if (reg == null) { Trace("settings gear: no hub registry"); return false; }
                var list = new List<object[]>();
                lock (reg)
                    foreach (var e in reg)
                    {
                        var id = e.Length < 6 ? null : e[0] as string;
                        if (id == null || id.IndexOf("special-effects", StringComparison.Ordinal) < 0) continue;       // the 3D window: ours and the 3D effects only
                        var act = e[5] as Func<bool>;
                        if (act == null || act()) list.Add(e);
                    }
                Trace("settings gear: other mods in the hub: " + list.Count);
                if (list.Count == 0) return false;
                Func<string> title = () => Loc.T("menu.button");
                Action<object> open = a => openOwn();
                list.Add(new object[] { OwnHubId, title, PluginVersion, 10, open, (Func<bool>)(() => true) });
                list.Sort((a, b) => { int c = Convert.ToInt32(a[3]).CompareTo(Convert.ToInt32(b[3])); return c != 0 ? c : string.CompareOrdinal(a[0] as string, b[0] as string); });
                var show = typeof(Rgreat.Hub.SettingsHub).GetMethod("ShowList", BindingFlags.Static | BindingFlags.NonPublic);
                if (show == null) return false;
                show.Invoke(null, new object[] { anchor, list });
                return true;
            }
            catch (Exception ex) { Trace("ChooseSettings: " + ex.Message); return false; }
        }
    }
}
