using System;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Main menu: the Encyclopedia item is split in two. "Encyclopedia" opens the Unit Reference window right in the menu (the main menu
    /// has its own window host, as the mission editor has; no mission is loaded); "Encyclopedia 3D" is the game's own item
    /// (loads EncyclopediaMission). Texts are the game's ("mainmenuencyclopedia"), "3D" added.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        const string EncName = "agiEncyclopedia";
        ConfigEntry<bool> _encSplit;

        void BindEncyclopediaConfig()
        {
            _encSplit = Config.Bind("General", "EncyclopediaSplit", true,
                "Main menu: ENCYCLOPEDIA opens the Unit Reference window (no mission is loaded); ENCYCLOPEDIA 3D is the game's own item (loads the Encyclopedia mission).");
        }

        /// <summary>Hooks (no polling): the main menu constructor, the switch, the game's language change; the menu already up (late start) is done at once.</summary>
        void HookEncyclopediaSplit(HarmonyLib.Harmony harmony, Assembly asm)
        {
            var mm = asm.GetType("SeapowerUI.MainMenuView");
            var ctor = mm == null ? null : mm.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (ctor == null) { Trace("MainMenuView constructor not found: EncyclopediaSplit not available"); return; }
            harmony.Patch(ctor, postfix: Hm("EncMenuCtorPostfix"));
            _encSplit.SettingChanged += (s, e) => EncyclopediaSplitApply(CurrentMainMenu());
            try
            {
                var lh = asm.GetType("SeaPower.LanguageResourceHandler");
                var sg = asm.GetType("SeaPower.Singleton`1");
                var inst = lh == null || sg == null ? null : sg.MakeGenericType(lh).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                var ev = lh == null ? null : lh.GetEvent("LanguageChanged");
                if (inst != null && ev != null)
                    ev.AddEventHandler(inst, Delegate.CreateDelegate(ev.EventHandlerType, typeof(AlternativeGameInterfacePlugin).GetMethod("EncLanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception e) { Trace("EncyclopediaSplit language event: " + e.Message); }
            EncyclopediaSplitApply(CurrentMainMenu());
            Trace("patched MainMenuView..ctor (Encyclopedia / Encyclopedia 3D)");
        }

        Noesis.FrameworkElement CurrentMainMenu() { return _mainMenuInst == null ? null : _mainMenuInst.GetValue(null) as Noesis.FrameworkElement; }
        static void EncMenuCtorPostfix(object __instance) { var a = Active; if ((object)a != null) a.EncyclopediaSplitApply(__instance as Noesis.FrameworkElement); }
        static void EncLanguageChanged(object sender, EventArgs e) { var a = Active; if ((object)a != null) a.EncyclopediaSplitApply(a.CurrentMainMenu()); }

        void EncyclopediaSplitApply(Noesis.FrameworkElement view)
        {
            if (view == null || _encSplit == null) return;
            try
            {
                var scroll = view == null ? null : view.FindName("scrollViewer") as Noesis.ScrollViewer;
                var list = scroll == null ? null : scroll.Content as Noesis.StackPanel;
                if (list == null) return;
                var kids = list.Children;
                Noesis.Button mine = null, orig = null;
                int seps = 0;
                for (int i = 0; i < kids.Count; i++)
                {
                    var b = kids[i] as Noesis.Button;
                    if (b != null && b.Name == EncName) mine = b;
                    if (kids[i] is Noesis.TextBlock) { seps++; continue; }
                    // the game's item: the first button after the second spacer (Instant ... Continue, spacer, Encyclopedia)
                    if (b != null && b.Name != EncName && orig == null && seps == 2) orig = b;
                }
                if (orig == null) return;
                if (!_encSplit.Value)
                {
                    // off: the game's item as it was
                    if (mine != null) kids.Remove(mine);
                    string back = GameText("mainmenuencyclopedia", "Encyclopedia").ToUpperInvariant();
                    if (!Equals(orig.Content as string, back) && ((orig.Content as string) ?? "").EndsWith(" 3D")) orig.Content = back;
                    return;
                }
                string t = GameText("mainmenuencyclopedia", "Encyclopedia").ToUpperInvariant();
                if (mine == null)
                {
                    mine = new Noesis.Button { Name = EncName };
                    var style = view.TryFindResource("MenuButton") as Noesis.Style;
                    if (style != null) mine.Style = style;
                    mine.FontSize = 25f;
                    var v = view;
                    mine.Click += (s, e) => OpenMenuUnitReference(v);
                    int at = 0;
                    for (int i = 0; i < kids.Count; i++) if (ReferenceEquals(kids[i], orig)) { at = i; break; }
                    kids.Insert(at, mine);
                    Trace("main menu: Encyclopedia (Unit Reference) and Encyclopedia 3D");
                }
                if (!Equals(mine.Content as string, t)) mine.Content = t;
                string t3 = t + " 3D";
                if (!Equals(orig.Content as string, t3)) orig.Content = t3;
            }
            catch (Exception e) { if (_traceOnce.Add("enc-split")) Trace("Encyclopedia split: " + e.Message); }
        }

        static WeakReference _encTemplatesFor;

        /// <summary>The main menu XAML has no DataTemplate for ObjectInformationViewModel (the mission, the editor and the pause menu merge this dictionary): without it the window is added to the host but nothing is drawn.</summary>
        static void EncyclopediaTemplates(Noesis.FrameworkElement view)
        {
            if (_encTemplatesFor != null && ReferenceEquals(_encTemplatesFor.Target, view)) return;
            var rd = Noesis.GUI.LoadXaml("Assets/Scripts/UI/Game/Views/ObjectInformationViewTemplates.xaml") as Noesis.ResourceDictionary;
            if (rd == null) throw new InvalidOperationException("ObjectInformationViewTemplates.xaml not loaded");
            view.Resources.MergedDictionaries.Add(rd);
            _encTemplatesFor = new WeakReference(view);
            Trace("Encyclopedia: the Unit Reference template merged into the main menu resources");
        }

        void OpenMenuUnitReference(Noesis.FrameworkElement view)
        {
            try
            {
                var asm = GameAssembly();
                EncyclopediaTemplates(view);
                var vmT = asm.GetType("SeapowerUI.ViewModels.ObjectInformationViewModel");
                var cmds = asm.GetType("SeapowerUI.WindowHostCommands");
                var open = cmds.GetProperty("OpenWindowCommand", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
                var vm = Activator.CreateInstance(vmT, new object[] { view, null, 0, true, null });
                MethodInfo ex = null;
                foreach (var m in open.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                    if (m.Name == "Execute" && m.GetParameters().Length == 2) { ex = m; break; }
                if (ex == null) { Trace("Encyclopedia: Execute(parameter, target) not found"); return; }
                ex.Invoke(open, new object[] { vm, view });
                Trace("Encyclopedia: Unit Reference opened from the main menu");
            }
            catch (Exception e) { Logger.LogWarning("Encyclopedia (Unit Reference): " + e.Message); Trace("Encyclopedia (Unit Reference): " + e); }
        }
    }
}
