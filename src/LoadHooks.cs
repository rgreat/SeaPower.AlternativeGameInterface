using System;
using System.Reflection;
using HarmonyLib;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Game hooks that other features need around a mission load (no option of their own; the features check theirs):
    ///  - SaveLoadManager.LoadMetadata (inside GameInitializer.init: LoadMission has no yield before it) and the first SaveLoadManager.StartTimer after it
    ///    (the end of LoadMission; StartTimer is also called by the autosave): the formation scales of a loaded save (FormationScale);
    ///  - the same StartTimer arms the options "Pause after loading a save" / "Hide briefing when loading a save" (LoadStart.cs);
    ///  - CancelAction.UnPause (the briefing's Play, also the pause menu's Resume): the HUD windows remembered open (RememberOpenWindows).
    /// The same methods in the beta and the release game (both decompiled sources read).
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        void HookLoadHooks(Harmony harmony, Assembly asm)
        {
            const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var slm = asm.GetType("SeaPower.SaveLoadManager");
            var meta = slm == null ? null : slm.GetMethod("LoadMetadata", sf, null, Type.EmptyTypes, null);
            var timer = slm == null ? null : slm.GetMethod("StartTimer", sf, null, Type.EmptyTypes, null);
            if (meta != null && timer != null)
            {
                TryPatch(harmony, meta, null, "LoadMetadataPostfix", null);
                TryPatch(harmony, timer, null, "StartTimerPostfix", null);
                Trace("patched SaveLoadManager.LoadMetadata / StartTimer (formation scales and load options of a loaded save)");
            }
            else Trace("SaveLoadManager.LoadMetadata / StartTimer not found: the formation scales are not read from a save");
            var cancel = asm.GetType("SeaPower.CancelAction");
            var unpause = cancel == null ? null : cancel.GetMethod("UnPause", sf, null, Type.EmptyTypes, null);
            if (unpause != null) { TryPatch(harmony, unpause, null, "MissionStartedPostfix", null); Trace("patched CancelAction.UnPause (HUD windows opened when the mission starts)"); }
            else Trace("CancelAction.UnPause not found: HUD windows pressed during the briefing may not open");
        }

        /// <summary>A save is being loaded (inside GameInitializer.init): a new load, nothing left over from the one before.</summary>
        static void LoadMetadataPostfix()
        {
            _scalesPending = true;
            _startArmed = false;
            _holdAfterPlay = false;
        }

        /// <summary>The end of LoadMission (also called by the autosave: only the first call after LoadMetadata counts).</summary>
        static void StartTimerPostfix()
        {
            bool fresh = _scalesPending;
            ScalesFromSave();
            if (fresh) LoadStartArm();
        }
    }
}
