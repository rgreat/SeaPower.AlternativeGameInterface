# Alternative Game Interface — заметки для продолжения работы (для Claude)

Мод для Sea Power: BepInEx 5 + Harmony, загружается через R.A.T.S. Keel (рекомендуется, Workshop 3835527459) или Anchor Chain (Workshop 3380210757).
UI игры — NoesisGUI (XAML), окна — `SeapowerUI.Window`. Доступ к игре — через рефлексию (в csproj нет ссылки на
Seapower-Scripts), Harmony-хуки ставятся по именам методов. Noesis: основной код — через рефлексию, окно настроек и шестерёнка
(1.3.0) — напрямую (ссылка на Noesis.NoesisGUI в csproj, как в Alternative Damage Model).


## Как работать с этими заметками (для Claude: не раздувать контекст)
- Этот файл — только основное (сборка, правила, архитектура, подводные камни). **Не читать целиком чужие разделы**: подробная история функций — в `DEVNOTES-ARCHIVE.md`.
- Нужная тема: `grep -n "ключевое слово" DEVNOTES-ARCHIVE.md`, затем `sed -n 'N,Mp'` по номерам строк из индекса ниже (читать только этот раздел).
- Общий код окон настроек (SettingsKit/Hub, вкладки, поиск, Ctrl+C, язык) описан в `seapower.alternativebase/README.md`, не здесь.
- В чате: большие файлы только через `grep`/`sed -n`/`Read` с offset+limit; логи — `grep`/`tail`, не целиком; вывод команд ограничивать (`| cut -c1-200 | head`).
- Новые записи: основное — сюда (коротко); подробности и история отладки — в архив, в конец, отдельным разделом.


## Состояние релиза
- AGI 2.8.0 is published on Steam (Nov 24 7:44am; change log checked with curl no-cache: the pasted text is the ChangeList.txt block of that time). It was published from the zip and never committed to git: the next commit holds it together with 2.8.1. Before it: 2.7.1 (Nov 21 7:41pm), 2.7.0 (Nov 21 5:59pm).
- Steam (checked no-cache, 2026-11-25): the latest entry is "Back to Version 2.8.1 - Regression on Release version of the Game" (Nov 25 10:57am; rgreat rolled 2.8.2 back), so the published code is 2.8.1. AGI 2.9.0 (his "делай новый релиз 2.9.0", 2026-11-25) is PREPARED, NOT committed until "коммить": it holds everything since 2.8.1 (the 2.8.2 content + the 2.8.3 fixes: SafeHook per hook, Event Log made once after a load, formation editor scale step / buttons / remembered scale in the save, the two load options done the new way). git HEAD is still 2.7.1 (2.8.0 - 2.9.0 go in one commit). The ChangeList.txt block "Version 2.9.0" replaces the 2.8.2 / 2.8.3 blocks. Review before 2.9.0: new files read for leaks (static collections are ConditionalWeakTables or pruned weak lists; no handler left on a long-lived object); LoadStart state is reset by every LoadMetadata; CampaignLoadedPostfix removed (beta campaign saves are incompatible with the release game, not an AGI matter).
- 2026-11-24: game beta 0.9.3 Build 261123 analysed (see "Game builds: release and the latest beta" below). No AGI code change needed so far.

## Game builds: release and the latest beta (2026-11-24)
- Supported game versions: exactly two, the game **release** and the **latest beta** (now release 0.9.3 Build 261116 and beta 0.9.3 Build 261123). Older builds need no support. A separate code branch per build is allowed when behaviour differs (detect the build at run time: beta has `TacticalDoctrine.AutoEngageRuleSet`, release has `AutoAttackSurface`).
- Do not remove "old" code (even if marked "for an older game") before checking in the decompiled code of BOTH targets that it is not needed.
- `seapower.gamefiles`: branch `StableBeta` (until 2026-11-24 `main`, same content) = beta line (0.9.2 `4190ba7`, beta 261116b `e1571a8`, beta 261123 `87cc0f3`), branch `Release` (`9990702`) = release 261116. `git diff origin/Release origin/StableBeta` = release -> beta.
- Checked 2026-11-24: all four mods build with 0 errors against both targets; no reflected type or member of AGI is missing in either. Release vs beta for AGI: `PauseMenuViewModel` keeps `MissionBriefView` only in the release; the beta has the Supplies tab in Unit Status (AGI finds tabs by their `*Tab.IsSelected` binding, so no change needed); `MapLabelLayer` has longitude wrap (`WrapPx`) only in the release; `Window.xaml` has the same text at `Campaign/` (release) and `Noesis/View/ControlTemplates/` (beta). Full lists: project doc `claude/game-update-howto.md` (procedure + results) and the report `game-changes-0.9.3-261123.md` (chat outputs).
- AGI checked on both builds by code (2026-11-24, nothing run in the game): every game member AGI hooks or reads has the same name and signature in the release and in the beta: the label filter (`MapLabelLayer.Gate1`, `PlaceLegacy`, `Cluster`, `Attribute`, `Solve`, `ReadSizes`, `ArrangeLabels`, `MapLabelCandidate` fields, `MapUnitViewModel.RelationshipMarkerTop`, `MapLabelPresenter.SetLineAlignment`), `FilePickerViewModel.Initialise`, `MissionEditorViewModel.LoadMission`, `UnitStatusViewModel.SwitchUnit/DisposeOwnedContent`, `Utils.openIniFile`, `Singleton<T>.InstanceExists`, `PauseMenuViewModel.Initialise/InitialiseMissionPause`, the XAML element names AGI looks up. Window, WindowHost, CameraManager and BlitToPipImage code is identical in both. Behaviour differences to test in the game on BOTH builds: labels (release solver wraps by longitude, beta does not; the 2.7.1 fix was seen on the release only), Unit Status with the new Supplies tab (beta), Encyclopedia Save/Load buttons (`HasMissionBriefing` differs).
- Open: SAM load/reload glitches seen by the user in the beta (zero-loadout semantics changed in `WeaponSystemLauncher`: the release skips `<= 0`); AWC chat, needs a save + trace + whether it happens on the release.

## Recent topics (details in the archive, see the index below)
- FitNewsWindow (2026-11-24, in 2.8.1, not committed): a tick always applies the fit and the look (0 % / 120 % / contrast); `NewsOwnedAtStart`, `NewsUserOwned` and the `[General] NewsLookApplied` entry are removed (the entry is dropped in `DropRetiredEntries`); `NewsFitNow` forgets the window's saved place (`ResetLayouts`) and sets the look when it differs; only a move / resize / restyle after the tick switches it off (`NewsUserChanged`). Trace 52332dbd: the option was off in the .cfg (switched off at an earlier session), the tick was set in the settings window and dropped by Cancel / X.
- AutoLoadCloseOnCampaign (2026-11-24, in 2.8.0 (published), not in git yet): `[General] AutoLoadCloseOnCampaign` (default true); `MenuLoad.cs`: the Load game window opened by AutoOpenLoad on the Campaign screen is closed when `CampaignMenuViewModel.SelectedMission` is set to a campaign (`CampaignChosenPostfix`); only that window (its VM is noted in `PickerInitPostfix` while the auto command runs, its Window in `PickerLoaded`), closed with `CloseWindowLikeGame`. Not run in the game yet.
- RefTabs (2026-11-24, in 2.8.2, not in git yet; src/RefTabs.cs): `[UnitReference] ReferenceTabs` (default true; Unit Reference / "Tabs for opened units"). A tab = a MuseumEntry (no second VM/page); the strip is a Border added to the root Grid of the ObjectInformationView (Grid col 2 / row 1, top, 28 high) and the unit page ScrollViewer (col 2 / row 1) gets +28 top margin while tabs exist; built in `RefTabsStep` (the per-round Unit Reference step, view from `InfoViewByWindow`). Per-VM state `RefTabsByVm`; host window = `_refHostWin` (the last window used, else the first on screen). Double click: prefix on `UnitTreeBrowserView.Tree_PreviewMouseLeftButtonDown` (ClickCount>=2; the item under the mouse: a unit, or a class row (IsGroupNode) -> the unit the game picked on the first click; folders and the expander stay the game's). New windows: prefix on `CustomRoutedCommand.Execute(parameter, target)` for `WindowHostCommands.OpenWindowCommand` with an `ObjectInformationViewModel` (or its Weapon / Sensor subclass: `IsRefVm`) that was made for an entry (ctor postfixes mark `ForUnitVms`: the iniRef ctor with initialIniRef != null, the (context, MuseumEntry) ctor), an entry page (any MuseumEntry subclass: `IsPageEntry` - units, weapons, launchers, guns, sensors; not folders / class rows): with a host -> AddRefTab + Dispose of the new VM + skip the open; without -> opens as always and its unit is the first tab. The toggle-broker windows (no unit) are never touched. Shift (`ShiftHeld`: UnityEngine.Input LeftShift/RightShift): a double click -> `OpenRefWindow` (new VM as the game: OIVM(ctx, iniRef, variantIndex, true, null) for units, Weapon/Sensor/OIVM(ctx, entry) for the rest; ctx = MainGameView.Instance; `_refBypass` while the mod runs OpenWindowCommand), a game-opened window -> `RefOpenPrefix` lets it open. Units get `HideTreeVms`: the splitter setup (`SetupUnitReference`, same place as "tree hidden as last time") hides the tree via `_treeSetter`, `[UnitReference] TreeVisible` is put back (not remembered); with Layout Improvements off RefTabsStep does the plain setter. Closing down to one tab hides the strip (`RefTabState.Hidden`, cleared when a new unit is added). Tab name: DisplayName; when the page had `HasMultipleLoadouts` while the unit was shown (`RefTabState.Multi`, read each step for the shown tab) the trailing "[...]" (variant) is cut. Showing a tab: `Browser.RevealEntry(pred by IniRef+VariantIndex)` then `Browser.SelectedEntry = found`, fallback `SelectedMuseumEntry`. Not tested in the game: check the strip position (the red box on the user's screenshot), the tab highlight, the close cross, the wheel over the strip, that the Unit Status "<" and the weapon/sensor reference windows are untouched.
- RememberOpenWindows / HUD toggle windows (2026-11-25, 2.8.3): the "pressed but no window" re-press waited 1.5 s (OsHostWait) and pressed again; in the release game after a campaign-save load the Event Log was made 4-5 times (window loaded every ~1.5 s). Then (trace 49b9802a) the Event Log never came: in the release game a save loaded without the briefing has the HUD hidden until our Play-start, and a press then makes no window (before, the 1.5 s re-press hid that). Trace 2fdac42d: the HUD was already visible, still no window - the press works only after the mission start (CancelAction.UnPause; the old 1.5 s re-press got a window only with the first press after our Play-start). Now the press waits for `MissionStartPending` (= `_hideApplied`, until our Play-start; `_osPressedHost` cleared there) and for `MainGameViewModel.GetHudVisible()` (the open-state phase runs every frame until OsSettle after the load screen, so it is pressed when the HUD shows). No clock: the host current at the mod's press is kept (`_osPressedHost`); it re-presses only when the press went to another host. `HostedOpen` also counts a shown window of that view model loaded in this scene (trace "its window is up but not in the current host's list"), and traces the host list when the window is missing.
- FormationScale save logic (2026-11-25): the saved scales are given to their UnitFormation objects when a save is loaded (`ScalesFromSave`: the first StartTimer after LoadMetadata - StartTimer is also called by the autosave in SaveLoadManager.OnUpdate, which read the OLD mission's section in trace 49b9802a), so renumbering later cannot move them; UnitFormation.Disband postfix drops the formation's scale; the save writes only formations still in a taskforce and not disbanded (nothing copied from the loaded save any more).
- RETURNED (2026-11-25, 2.8.3, src/LoadStart.cs) the other way, his order: the game shows the launch briefing as always; the load screen's end after SaveLoadManager.StartTimer (armed only when SaveLoadManager.IsASaveFile) fires LoadStartStep: briefing up + Hide -> the briefing view model's own PlayCommand.Execute (closes it, HUD on, unpauses) then GameTime.Pause(true) if PauseAfterLoad (CancelAction.UnPause postfix LoadStartAfterPlay does the pause after Play, also the player's); briefing up + only Pause -> pause after the player's Play; no briefing + Pause -> GameTime.Pause at once. Nothing of the game's start (ctor, ShowLaunchBriefing, Globals path) is changed. Trace lines "save load: ...". The empty-LinearCampaignEventName fix (CampaignLoadedPostfix) is what removed the campaign map over the mission. History of the first removal: [Mission] PauseAfterLoad and HideBriefingOnLoad, his order "удали весь код обработки этих 2х опций", to be re-added from scratch): [Mission] PauseAfterLoad and HideBriefingOnLoad - src/SaveLoadStart.cs deleted, settings rows and lang keys removed, the two config keys in DropRetiredEntries. Reason: beta trace 6b635fc6 (both options OFF) still showed the campaign map (its newspaper event) over the mission, cause unknown (not proven to be the game: needs a run without AGI). What stayed, in src/LoadHooks.cs: LoadMetadata/StartTimer postfixes for FormationScale (ScalesFromSave) and CancelAction.UnPause postfix for the HUD windows (MissionStartedPostfix). The notes below on SaveLoadStart are history.
- FINAL WORD (2026-11-25, from the game developers via rgreat): beta saves of a campaign are NOT compatible with the release game (the campaign was fully redone). The empty campaign map after leaving a mission loaded from such a save (also without mods) and the odd saves (empty LinearCampaignEventName, a briefing path of another mission) came from that, not from AGI or the two load options. CampaignLoadedPostfix was removed again in 2.9.0 (a guard for an empty event name; the game does not handle one, but only an incompatible save has it). Do not chase these symptoms with a save made on the other branch.
- ROOT CAUSE of the campaign map/newspaper over a loaded campaign save (2026-11-25, verified without mods + the save rg.sav): the save has `[File] LinearCampaignEventName=` EMPTY (SaveLoadManager writes playedMission.EventName, else the last unlocked mission, else empty). SceneCreator.SceneManager_LinearCampaignLoaded -> LinearCampaign.Initialise(""): SetCamera(false) + InputActions.LinearCampaign.Disable() only run for a non-empty name (both branches) -> the Campaign Camera/view stays over the mission. Fix in LoadHooks.cs: CampaignLoadedPostfix does those two calls for an empty name. Why the name was saved empty is NOT known (Missions empty or playedMission null at save time). The two removed options were not the cause; re-adding them is possible again.
- Diagnostics (2.8.3): every written save traces "save written: BaseFile, LinearCampaignSavePath, LinearCampaignEventName (<-- EMPTY for a campaign save), game's current mission path" (LoadHooks.SaveDiagPostfix on WriteFormationManagerData). Open question: could the old HideBriefingOnLoad (Globals.currentMissionFilePath set to "" during the MissionBriefViewModel ctor) have made the empty-event-name save? No mechanism found: the path is a plain auto-property, only that ctor read it in the window.
- Trace 059dafc0 (2026-11-25, after the two options were removed): the campaign newspaper/map over the loaded mission STILL appeared, so it was not those options. The trace shows the mod pressed the Event Log HUD button at 28.8 s and again at 32.8 s while the load screen was up (LoadScreenRunning true from ~30 s), before the mission HUD existed; no UnPause, timeScale 0, Campaign Camera rendering to the end. Fix: HUD toggle presses wait while LoadScreenRunning ("the load screen is up" in the trace). Not proven to be the cause: test with RememberOpenWindows off / AGI disabled.
- SaveLoadStart rewritten (2026-11-25, 2.8.3) as two branches (only one patched): beta = ShowLaunchBriefing prefix (skip) + postfix (GameInitializer._launchBriefing set -> briefing up); release = MissionBriefViewModel() ctor prefix/postfix only inside GameInitializer.init. Common hooks: init prefix/finalizer (per-load flags reset; NOT in LoadMetadata, asset-bundle builds run LoadMission after init), LoadMetadata (pause pending if a save; scales pending), StartTimer (hidden -> CancelAction.UnPause + CampaignCameraOff; then pause unless the briefing is up), CancelAction.UnPause postfix (HUD phase; pause after Play when the briefing of a save was up - before, the beta lost PauseAfterLoad with a briefing). Traced in both decompiled sources: LoadMission has no yield before LoadMetadata (both inside init, before the briefing); StartTimer is at the end of LoadMission after yields (after the briefing); Play = CancelAction.UnPause in both; GameTime.Pause/UnPause identical (Pause sets IsInPauseMode); LinearCampaign.SetCamera / InputActions.LinearCampaign in both.
- HUD windows vs the briefing (trace e82aafe2, beta, briefing SHOWN): the Event Log was pressed twice during the load/briefing, made no window, and the start-up phase (0.5 s after the load screen) was over before Play. Now a press waits while `LaunchBriefingUp()` (MenuSystemViewModel.CurrentWindow is MissionBriefView) or `MissionStartPending`; a postfix on CancelAction.UnPause (both branches) runs the phase again once per scene when a window waited (`_osStartWait`), so the windows open right after Play. Not tested in game.
- SaveLoadStart beta (trace 0e79337e, 2026-11-25): the same campaign-map-over-the-mission with the beta's ShowLaunchBriefing skipped (Campaign Camera rendering, a campaign newspaper on the screen, mission running behind). LaunchBriefingPrefix now sets `_hideApplied` too and `_slUnpause` is CancelAction.UnPause in both branches, so StartTimerPostfix starts the mission like Play and CampaignCameraOff runs on the beta as well.
- SaveLoadStart release, 2nd lesson (2026-11-25): the parameterless MissionBriefViewModel ctor is ALSO run by PauseMenuViewModel (its briefing tab, on Escape) and ScenarioMenuViewModel.TryLoadBriefing; and SaveLoadManager.StartTimer is ALSO called by SaveLoadManager.OnUpdate each time autosave is re-enabled (a menu closed, unpause). Trace showed Escape in a loaded save -> pause-menu briefing blanked + on closing the menu CancelAction.UnPause/SetCamera ran again. Fix: prefix+finalizer on GameInitializer.init sets `_inLaunchInit`; the ctor hooks act only inside it.
- SaveLoadStart release-game lesson (2026-11-25, 2.8.3): with the briefing left out (empty path in the ctor) NOTHING closes the menu the mission was started from - trace showed `Campaign Camera` + `Menu Camera` still rendering over the mission, "no windows at all". The Play button's `CancelAction.UnPause()` (GameTime.UnPause, blank menu window, menuOpen=false, cursor, SetHudVisible(true)) does it; `StartTimerPostfix` now calls it when `_hideApplied` (set in BriefCtorPrefix, cleared in LoadMetadata), then `CampaignCameraOff` (LinearCampaign.SetCamera(false) + InputActions.LinearCampaign.Disable(): without the first the campaign world map stays over the mission, without the second Escape also runs LinearCampaign.PauseMenu_performed and the pause menu opens only now and then), then PauseAfterLoad pauses again. Check in the trace: "the mission is started as the Play button of the briefing starts it" and no `Campaign Camera` in the render lines after the load.
- SaveLoadStart (2026-11-24, in 2.8.2, not in git yet; src/SaveLoadStart.cs): `[Mission] PauseAfterLoad` (default true, "Pause after loading a save") and `[Mission] HideBriefingOnLoad` (default false, "Hide briefing when loading a save"), both In Mission tab. A save = `[File] SaveFile = true` in the mission file (`Globals.currentMissionFilePath`, read by a light line scan in IsSaveFile; the game reads it in SaveLoadManager.LoadMetadata -> IsASaveFile). HideBriefing: prefix on `GameInitializer.ShowLaunchBriefing` returns false for a save (that method also pauses the game and hides the HUD, so with it skipped the loaded mission runs at once). PauseAfterLoad: postfix of `SaveLoadManager.LoadMetadata` sets a pending flag for a save, postfix of `SaveLoadManager.StartTimer` (the end of SceneCreator.LoadMission) calls `GameTime.Pause()` (sets TimeCompression 0 and InputHandler.IsInPauseMode). Not tested in the game: check that Play/pause key unpauses and that the HUD is visible with the briefing hidden. RELEASE game (2026-11-25, trace from rgreat): there is no `GameInitializer.ShowLaunchBriefing` (the options did nothing): `GameInitializer.init` makes `new MissionBriefView()`, whose parameterless `MissionBriefViewModel` ctor reads `Globals.currentMissionFilePath`, sets `hasContent`, pauses and hides the HUD when there is content, and the window is shown only with `hasContent`. HideBriefingOnLoad: prefix on that ctor sets the path to "" (private setter, `_slPathSet`) so the ctor returns at once, postfix restores it. PauseAfterLoad with a briefing: the Play button runs `CancelAction.UnPause`, so a postfix on it pauses again (`_holdAfterBrief`, set by the ctor postfix when `hasContent` for a save, cleared by LoadMetadata). Lesson: `GetConstructor(Static|Instance|...)` matches the static constructor too (AmbiguousMatchException) - and an exception in one `Hook*` ended `TryHook`, so every hook after it (gear, News fit...) was missing: every `Hook*` call in `TryHook` now goes through `SafeHook` (catch + trace), so one failing feature cannot set `_hookFailed` (which switches the window features off: gear, News fit, layouts).
- RememberSaveSort (2026-11-24, in 2.8.0 (published), not in git yet): `[General] RememberSaveSort` (default true; Game Menus / Save and load / "Remember sort order"); the sort order of the Save / Load game pickers (`Pickers.cs`), kept per list (`LastSaveSortMissions` / `LastSaveSortCampaigns`: NameAsc, NameDesc, DateAsc, DateDesc) next to the folder; details in the archive ("Save / Load game: sort order").
- FormationScale (2026-11-25, in 2.8.3; src/FormationScale.cs): `[FormationEditor] ScaleStep` (float 0.05-10, default 1; "Scale slider step", nm) and `ScaleButtons` (default true), rows in own settings tab `Formation Editor` (`TFormEd`, hidden like the other window tabs: `_formEd`, `Open(..., formEditor)`, gear of a window whose candidates match `FormationEditor*` / `LinearCampaignTaskForceFormation*` - not verified that such a window has a gear; else Show all settings), group "Range rings". Per formation (only FormationViewModel = in-game editor; the mission editor's FormationEditorViewModel and the campaign VM get only step + buttons): `RememberScale` (on), `MinimizeScale` (on, applies only when nothing is remembered/saved for the formation), `MinimizeScaleFloor` (nm, 1-10, default 2). Memory: ConditionalWeakTable UnitFormation->scale; the save: postfix of private `SaveLoadManager.WriteFormationManagerData(IniHandler ini)` writes section `[AGI_FormationScale]` key `<taskforce._nameInMissionFile>_Formation<n>` (the game's own key of WriteFormations) = scale, copying the loaded save's section first (IsASaveFile; MissionIni.Data) so untouched formations survive a second save; read lazily when the slider loads. Fit = max station distance (StationPosition * 0.036285132 nm) * 1.05. Not tested in game. Everything about the formation editor goes into this tab. The game's scale slider (FormationView in a mission, LinearCampaignTaskForceFormationView in campaign prep; Minimum 0.05, TickFrequency 0.05, snap from Minimum) is caught in `OnSliderLoaded` (ViewCapture) by its DataContext type name; with step > 0.05: TickFrequency = Minimum = step. The slider's Grid is wrapped in a StackPanel (same Grid.Row/Span/margin) with buttons 1-5 nm that set `Slider.Value` (two-way bound to BullseyeScale). Not tested in game yet.
- FormationManagerClick / DoubleClickCentersMap (2026-11-24, in 2.8.0 published; 2026-11-25 own tab, in 2.8.2): own settings tab `Formation Manager` (`TForm`), hidden like the other window tabs - shown when the gear of the Formation Manager window opened the settings (`SettingsWindow.Open(..., formation)`, `IsFormationManagerWindow`: candidates glob `FormationManager*`) or with "Show all settings". `[FormationManager] ClickGoesToUnit` (moved from `[Mission] FormationManagerClick` by `BindMoved`, value kept) and `DoubleClickCentersMap` (new, off): the double click (ClickCount 2 of the same tunnelling handler) calls `CenterMapAsKeyC(unit)` = what key C does (`_mainGameViewModel.Map.DisplayMap.Center = unit.Position.Value`); `CenterMapOn` (TargetCenter, as the Event Log) was tried first: TargetCenter starts a PointAnimation that does not run while the game is paused (PauseAfterLoad) - the trace showed the call but the map stayed; the game's own double click (camera) runs as before. The game has no code that centres the map on a click (checked in the decompiled game: only the C key / MiniMapCenterToTarget etc.), so the single click never centred it; the `MapViewModel.Center` block (`FormMapCentrePrefix`) and the timer hack were removed. One click: `MainGameViewModel.SelectUnitCommand` like the game's double click - `src/FormationClick.cs`, hooked in `HookMainView` (archive: end of file).
- Map labels, 2026-11-24 (not committed): box checked against the drawn lines (`GlWiden`) and against the text itself (`GlProbeFix`), places taken by other mods (`rgreat.map.occupied.v1`, ADM prompt), the empty first line (a stale measure inside the line, `GlInvalidateTree`; confirmed by trace 9ef6f0b4, build 1434) - archive 1246 and the end of the file. Before: cut at the right edge, 2.7.1 (archive 1056). Label optimisation (`FastAcc`, `RectGrid`, profiler sections) and the Event Log loop fix are in 2.6.0+ (archive 1062, 1070).
- Monitors: second-monitor click report, double-click on a title bar, drag across displays, hang on load with PerMonitorVSync, "no present hook" decision (archive 1074, 1128, 1178, 1188, 1204, 1225). 3D stutter with a second display: open (archive 1103).
- Settings hub (1.8.0) and its timing, Rename, backgrounds setting, title-bar buttons (archive 1090, 1100, 1082).

## Keel: BlockingStart (2026-11-22, committed)
`Release/_info.ini` has `[Keel] BlockingStart=true` next to `StartEarly=true`. Only a mod with it holds the game up, and only at one point: right before the game's first `LoadScreen.ExecuteLoadScreen` call (needs the Keel build that knows the key; an older Keel ignores it and the mod starts as a plain early mod, one frame step at a time, after the first picture). Plain `StartEarly` mods (ASE, ADM, AWC) start in the game's first frames next to the game. Reason: the game has no frames from the start of its own loading until its first scene is up (the Keel coroutine got its second step about 6 s after Awake), so a frame-based start could not beat the first picture.

## Последний активный мод (шестерёнка World View и меню)
- Окно настроек любого мода при построении (`ModRow`) помечает свой мод последним (`SettingsHub.MarkLast`). Меню открывает его окно; шестерёнка окна World View — `SettingsHub.OpenLastOther` (другой активный мод, иначе своё). `ModChooser.cs` (список «Game Interface / Special Effects») убран: `ShowList` в хабе давно нет. Не проверено в игре.

## Индекс архива (строка в DEVNOTES-ARCHIVE.md — раздел; размер в символах)
- 3 — Дерево Unit Reference (как устроено в игре, 0.8.5) (3682)
- 37 — Оптимизация дерева Unit Reference (OptimizeTree, 1.4.0, `src/TreeTweaks.cs`) (2529)
- 62 — Прокрутка к выбранному (ScrollToSelected, 1.4.0, `src/TreeScroll.cs`) (1108)
- 73 — Дерево Unit Reference: опции 1.6.0 (`src/TreeSearch.cs`, `src/HoverHelp.cs`, все в [UnitReferen (2163)
- 94 — Меню и файлы (1.6.0, `src/Pickers.cs`) (1383)
- 106 — Колесо только над деревом ([UnitReference] TreeWheelOnlyOver, 1.5.3, `src/TreeScroll.cs`) (599)
- 112 — Список loadout'ов для всех юнитов (ShowLoadoutList, 1.4.0, `src/TreeTweaks.cs`) (2219)
- 133 — Armed only (при ImprovedUnitReferenceUI, состояние — ArmedOnly, 1.4.0, `src/ArmedFilter.cs`) (1681)
- 148 — Фильтр по источникам — выпадающий список «Source» (при ImprovedUnitReferenceUI, 1.9.0, `src/Sou (1649)
- 156 — Вкладка Reference в Unit Status (UnitReferenceTab, 1.5.0, `src/StatusReference.cs`) (2880)
- 180 — Вкладка Weapons — теперь отдельный мод Alternative Weapons Control (515)
- 186 — Окна Weapon Reference для установок (1.5.1) (377)
- 191 — Фоновые картинки ([General] Backgrounds = Mods|Mixed|Game, was bool ModBackgroundsOnly since 1. (1849)
- 208 — Settings window left open across a screen change (2.3.3) (827)
- 216 — Окно настроек: зависимые строки (395)
- 221 — Минимум панелей секций Damage Control ([UnitStatus] SectionsMinHeight, 1.3.0) (1045)
- 231 — Рамка окон ([WindowBorder]: Enabled, Thickness 0,5–10, Color #RRGGBB/#AARRGGBB, по умолчанию #A (684)
- 239 — Рамка подсказок ([WindowBorder] ToolTips, 1.5.2, `src/TooltipFrame.cs`) (689)
- 246 — Порядок окон и Weapon Reference (проверено в игре) (1997)
- 263 — Окно настроек (1.3.0) (4901)
- 305 — Без ограничений размера (RemoveSizeLimits, «No size limits»; 1.3.0) (714)
- 313 — Подписи формы (257)
- 317 — Притягивание окон (SnapWindows, 1.3.0, `src/Snap.cs`) (3354)
- 347 — Развёрнутое окно в пределах экрана (1.5.3, `src/MaxFit.cs`) (557)
- 353 — Unit Reference: панель поиска (RememberSearchPanel, 1.3.0) (716)
- 361 — Окна, открываемые в начале миссии (форма) (264)
- 365 — UnitMenu.cs, GameFixes.cs, CampaignLoadouts.cs (1.6.0) (3591)
- 375 — Файл настроек в user (1.6.0) (858)
- 379 — Производительность (1.6.0, аудит Update и хуков) (1727)
- 394 — Сообщения в правом нижнем углу (1.6.0, `src/AlertsFix.cs`, без опции — всегда включено) (590)
- 400 — Захват элементов вместо обходов (1.6.0, `src/ViewCapture.cs`) (1421)
- 411 — Sea Power 0.8.7 (beta 261026) — совместимость с 0.8.6 сохраняется (1.6.2) (985)
- 419 — Strict loadouts в Builder: закрытые лоадауты (1.6.2) (1847)
- 426 — WindowedMapAndWorldView (1.7.0, `src/MapWindows.cs`, `src/PipScene.cs`, `src/CompassTape.cs`; [ (4586)
- 441 — Рабочие столы (desktops) по клавише «Toggle fullscreen minimap» (по умолчанию Tab) (1.7.0, `src (3383)
- 448 — Клавиша World View (1.7.0, `src/WorldViewKey.cs`; [General] WorldViewKey, по умолчанию Insert) (1102)
- 454 — Прочее в 1.7.0 (3769)
- 469 — Propulsion cell in Unit Reference (`src/Propulsion.cs`, `[UnitReference] ShowPropulsion`) (1377)
- 472 — Window opacity and state-driven backgrounds (`WindowLook.StateDriven`, `HookState`) (1505)
- 475 — IMGUI windows of other mods over Noesis (`src/ImguiOrder.cs`) (1253)
- 478 — Optical view (binoculars) in WindowedMapAndWorldView (1.8.0, `src/BinocularFrame.cs`, `src/MapW (5574)
- 494 — Damage Control lists: mouse wheel ([UnitStatus] DamageListsWheel, `src/DamageScroll.cs`) (969)
- 500 — Tactical Display closed by a desktop switch (`RestoreOpenStates`) (630)
- 504 — Game bug: drawing tool objects doubled after the map window is closed and opened (not the mod) (999)
- 509 — Reference tab: the tab Kind (`src/StatusReference.cs`) (1446)
- 517 — Other monitors for windows (desktop mode, `[Monitors] Enabled`, `src/Monitors.cs`) (5956)
- 537 — Фон главного меню: видео или слайдшоу (`[General] MenuBackground` Video/Slideshow, `SlideshowSe (1165)
- 541 — Быстродействие: что считается каждый кадр (проверка 1.8.2) (1070)
- 546 — Unit Status: закладка на юнит (`[UnitStatus] RememberTab`, true, `src/StatusTabMemory.cs`) (2077)
- 552 — Enabling in the Mods menu needs a restart (2026-11-11) (688)
- 555 — Sea Power 0.9.0 - compatibility review (2026-11-12, mod 1.9.0, nothing changed in the source ye (4163)
- 573 — Cleanup after dropping 0.8.x (2026-11-12) (713)
- 578 — Mission timer draggable by itself (1.9.0, TimePanel.cs) (533)
- 581 — Text by background and the frame of transparent windows (1.9.0) (3376)
- 586 — Elements rewritten by other code (flicker, 2026-11-12) (660)
- 589 — Release 2.0.0 (2026-11-12) (1160)
- 599 — Mod info on the loading screen (2.0.0, `src/LoadInfo.cs`) (1697)
- 605 — R.A.T.S. Keel (loader, 2026-11-12; SeaPowerKeel.dll 193 KB checked) (7848)
- 645 — Encyclopedia Save / Load (src/MuseumSave.cs) (763)
- 648 — Encyclopedia / Encyclopedia 3D (src/EncyclopediaSplit.cs) (809)
- 651 — Errors when loading a save (FixSoundOnReload, src/MuseumSave.cs) (659)
- 654 — Release 2.1.0 (2026-11-13) (490)
- 657 — Ghost window (0 % opacity, src/WindowLook.cs) (500)
- 660 — Release 2.2.0 (2026-11-15; 2.1.1 (Unit Reference tree button) was published earlier) (5206)
- 683 — Campaign: ship state on briefing / newspaper events (Campaign/EventShipState, src/CampaignEvent (1092)
- 688 — Unit Status header: class (first 2.3.0 build; superseded by the section "window title and label (884)
- 691 — Release 2.3.1 (minor) (493)
- 694 — Release 2.3.0 (published) (789)
- 698 — Unit Status: window title and label (2.3.0, StatusReference.cs `UnitText` / `FullUnitName` / `S (1478)
- 702 — Track State layout (2.3.0, TrackState.cs `TrackXaml2`) (2205)
- 708 — Settings: Remembered Windows (2.3.0) (354)
- 712 — Contact estimate squeeze (2.3.0, `[Map] ClipContactEllipse`, `src/ContactEllipse.cs`) (1897)
- 715 — Compact sonobuoys (2.3.0, `[Map] CompactSonobuoys`, `SonobuoysThreshold` 20, `src/SonobuoyDeclu (1331)
- 718 — Анализ быстродействия (2.3.0, по коду, без замеров в игре; код не менялся) (4883)
- 743 — Аудит утечек (2.3.0, по коду, не проверено в игре) (1656)
- 748 — Профайлер мода (2.3.0, `src/Profile.cs`) (3835)
- 757 — Лимит FPS (FrameLimit.cs, OnSliderLoaded в ViewCapture.cs) (23090)
- 821 — Formation Manager: right click on another monitor (after 2.4.0) (2931)
- 828 — Map label filters (after 2.4.0, `[Map] GroupNameOnly`, `LabelOverlapFilter`, both default false (5763)
- 839 — Load screen line without Effect (after 2.4.0, `LoadInfo.cs`) (716)
- 842 — Formation name around the whole formation (after 2.4.0, `GlGroupAround` in `MapLabelFilter.cs`, (4741)
- 853 — Formation name as a label of its own (after 2.4.0, `GlNamesSync` in `MapLabelFilter.cs`, only w (4920)
- 863 — Formation lines (after 2.4.0, `[Map] FormationLines` default true, `src/FormationLines.cs`) (1250)
- 866 — Icons as taken places (overlap filter) (443)
- 869 — Master switch `[Map] DrawingImprovements` (default true) (609)
- 872 — Own units: the 2nd line when 2 lines do not fit (322)
- 875 — Selected formation name: farther shift (357)
- 878 — Order of the formation name (corrected) (420)
- 881 — Empty lines in the label text (718)
- 884 — Own auto placement of the labels (part of the overlap filter) (1027)
- 887 — Stale label box after a line is blanked (zoom out) (820)
- 890 — Line alignment of the label text (461)
- 893 — Box taller than the text (trace of 2026-11-18) (791)
- 896 — Empty lines (live) (788)
- 899 — Empty line in the middle of a text (1050) (404)
- 902 — Empty line inside the text of one label line (trace 2026-11-18) (611)
- 905 — Natural line sizes of a cut label (7006) (585)
- 908 — The game measures every label with all its lines first (666)
- 911 — Own full measure before the filter (848)
- 914 — Natural sizes are re-measured when the text changes (595)
- 917 — Hysteresis of the placement (1 s) (753)
- 920 — Load on entering Scenarios / Campaign, windows beside the menu (`src/MenuLoad.cs`) (1212)
- 923 — File picker without a height limit ([General] FilePickerTall, default true) (470)
- 926 — The 'FRND' badge (friendly unit of another side) (904)
- 929 — 'FRND' merged into the first line ([Map] FriendlyBadgeInLabel, default true, in the master swit (1127)
- 934 — The badge is a line of the label with the usual cutting rules (replaces "goes with the first li (755)
- 937 — FRND badge glued to line 1 (no virtual line) (408)
- 940 — Text alignment inside a line (429)
- 943 — The game's own FRND badge (295)
- 946 — Labels of the units of an unselected formation: last in the order (693)
- 951 — Formation update by the leader (`[Map] FormationUpdateByLeader`, on by default since 2.5.0, src (1942)
- 958 — Aircraft formation: distance and altitude under the name (`[Map] AirGroupInfo`, on by default) (1739)
- 963 — The game's FRND badge stayed on some units until they were selected (634)
- 966 — Map labels reworked after the review (2026-11-18) (3997)
- 980 — Fine zoom with Shift (`[General] MapFineZoomShift`, on by default, src/MapFineZoom.cs) (712)
- 983 — Version 2.5.0 (2026-11-18) (481)
- 986 — Optical View (Z) without a World View picture: the camera did not follow the mouse (bug report, (1022)
- 992 — Version 2.5.1 (209)
- 995 — Trace volume (540)
- 998 — Game beta 0.9.3-261116b (gamefiles e1571a8) - what changed for the mod (1870)
- 1004 — Camera offset per desktop (468)
- 1007 — Settings window: one-line tab strip (after 2.5.1, not committed) (1388)
- 1010 — Settings screenshots: window fitted to each tab (after 2.5.1, not committed) (750)
- 1013 — Text-by-background thresholds as options (after 2.5.1, not committed) (505)
- 1016 — Settings kit: the tab strip and the screenshots moved to shared files (after 2.5.1, not committ (618)
- 1019 — Settings window frame and row editors (settings kit: SettingsKit.cs, after the last release) (2467)
- 1027 — Switch between the mods' settings windows (after the last release) (1433)
- 1033 — Tabs, search, revert (kit) (432)
- 1037 — Camera offset in the settings window (814)
- 1040 — "Show all settings": the mode is kept for the game session (533)
- 1043 — Pages inside a tab (SettingsPages.cs) (1246)
- 1050 — Ctrl+C copies the text under the mouse (kit) (764)
- 1053 — One shared Language (hub + kit) (1056)
- 1056 — Map labels cut at the right edge (2026-11-22) (1000)
- 1062 — Оптимизация подписей на карте (2026-11-19, не закоммичено) (1027)
- 1070 — Цикл «Event Log» при старте миссии (2026-11-19, не закоммичено) (678)
- 1074 — Мониторы: окно на втором мониторе видно, но не кликается (отчёт игрока, не воспроизведено) (2586)
- 1082 — Rename: Smart Windows -> Alternative Game Interface; Backgrounds setting; title-bar buttons (2045)
- 1090 — Settings hub (rgreat mods, 1.8.0, `src/SettingsHub.cs`) (2503)
- 1100 — SettingsHub timing (1.8.0) (507)
- 1103 — 3D stutter with a second display, desktop mode off (open) (2754)
- 1128 — Monitors: the game's second-monitor view stayed on over the monitor's view (two Tactical Displays) (5779)
- 1178 — Hang on mission load with SyncTest PerMonitorVSync; other monitors drawn under a load screen (965)
- 1188 — Drag across displays: the window follows the mouse until the button is released (1674)
- 1204 — Decision: no present hook; Adaptive V-Sync with several displays syncs to one monitor (MultiVSync.cs (2427)
- 1225 — Monitors: double-click on a title bar missed (trace a2ce4b88) (6249)
- 1246 — Map labels: sizes checked against the drawn lines and the text itself; places taken by other mods (after 2.7.1, not committed) (4900)

## Сборка в песочнице (проверено)
1. `apt-get update && apt-get install -y dotnet-sdk-8.0` (без `update` пакеты дают 404).
2. Репозитории проекта Sea Power (все приватные, владелец rgreat; в облачной сессии Claude Code — `add_repo`, клон
   `/home/claude/<имя в нижнем регистре>`, вместе с бинарными файлами; в обычном чате GitHub отдаёт 404):
   SeaPower.AlternativeGameInterface (этот мод), SeaPower.AlternativeDamageModel, SeaPower.AlternativeWeaponsControl (вкладка Weapons,
   выделена отсюда), SeaPower.GameFiles (только чтение: файлы игры).
   Ссылки для сборки (UnityEngine*.dll, Seapower-Scripts.dll, Noesis.NoesisGUI.dll): `SeaPower.GameFiles/Managed/`
   (актуальная сборка игры; на 14.10.2026 — та же, что 0.8.5 в `/home/claude/game`), кроме UnityEngine.InputLegacyModule.dll —
   его нет в GameFiles (папки `GameRefs/` в репозитории больше нет): брать из `GameRefs.zip` пользователя;
   всё вместе — в `/home/claude/game`. Данные игры (vessels, ammunition, systems, aircraft, land_units, language_en,
   xaml-assets...) — там же, для анализа. Версия игры — `SeaPower.Constants.VERSION` в декомпиляции Seapower-Scripts.
3. BepInEx: `https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip`
   (`BepInEx/core/*.dll`); Anchor Chain: `https://github.com/SeaPower-Modders/AnchorChain/releases/download/V1.1.0/dev.zip`.
   GitHub API даёт rate limit — качать прямыми ссылками.
4. `nuget.config` с пустым списком источников обязателен (nuget.org закрыт, restore иначе падает).
5. `dotnet build -c Release -p:ManagedDir=... -p:BepInExCore=... -p:AnchorChainDll=...` → `Release/AlternativeGameInterface.dll`.
   Номер билда (UTC-время сборки `yyyyMMdd.HHmm`, свойство `BuildStamp` в csproj → AssemblyMetadata "Build") пишется в лог:
   `Alternative Game Interface <версия> (build ...) loaded` — по нему видно, какая dll реально запущена.
   Сборка сама копирует dll в `Release/` (target StageRelease) — пробные сборки делать на копии репозитория.
   Скрипт `/home/claude/tools/build.sh [repoDir] [zip]`: сборка с путями песочницы + упаковка `AlternativeGameInterface.zip`.
   Предупреждение MSB3277 про System.IO.Compression безвредно.
   Order matters: build → zip → only then `git checkout Release/AlternativeGameInterface.dll` (the tracked dll). Restoring it before zipping ships the OLD dll with new sources (happened 2026-11-24; the trace showed the old build stamp).
6. Анализ игры: `monodis` на сборке игры 0.8.x падает (ссылка на System.Threading.Channels 10.0). Работает ILSpy:
   `https://github.com/icsharpcode/ILSpy/releases/download/v9.1/ILSpy_binaries_9.1.0.7988-x64.zip` → консольная
   программа net8.0 со ссылкой на `ICSharpCode.Decompiler.dll` (CSharpDecompiler.DecompileTypeAsString по всем типам,
   имя файла — `ReflectionName`, иначе generic-типы перезаписывают друг друга). XAML в dll нет (он в ассетах Unity).
7. После обновления игры: декомпилировать старую и новую Seapower-Scripts, `diff -rq`; все строковые имена из src
   (`"..."`, которые были в старой декомпиляции) должны найтись в новой.
   0.8.4 → 0.8.5: окна/UI не менялись, Noesis API тот же, все имена на месте; `Singleton<T>.Instance` при выгрузке
   сцены (`Singleton.TearingDown`) теперь возвращает null с предупреждением вместо создания объекта.

## Выдача результата (предпочтения пользователя)
Каждая сборка — `AlternativeGameInterface.zip` БЕЗ корневой папки: `Release/` (AlternativeGameInterface.dll, _info.ini, thumbnail.jpg),
`src/`, `AlternativeGameInterface.csproj`, `nuget.config`, `PUBLISHING.md`, `DEVNOTES.md`. README пользователь пишет сам.
thumbnail.jpg берётся из клона репозитория.
Общение — по-русски, коротко; в .cfg — один переключатель на функцию. В отчётах не повторять рутинное («собирается без
ошибок», «ничего не коммитил», «не проверено в игре»). Править исходники и коммитить — только по просьбе пользователя
(напоминание stop-hook о незакоммиченном — молча пропускать).

## Совместимость с версиями игры
Целевые версии (с 2026-11-24): релиз игры и последняя бета, см. «Game builds». Нижняя граница `_info.ini` — 0.9.0. Поддерживается Sea Power 0.9.0 и новее (`_info.ini` GreaterThanEqualToVersion=0.9.0; до 1.9.0 - ApproximateVersion=0.8.6; README и README.BBCode — тоже). Старые версии игры (0.8.x) больше не поддерживаются: правки под них не нужны, предупреждать не нужно; прежние запасные пути в коде безвредны и остаются, пока не мешают.
`ModCompatibility.Matches` → `SemanticVersion.ApproximatelyEquals`: тот же Major.Minor и Patch ≥ указанного.
Несовместимому моду игра не скачивает обновления из Workshop (`FileManager.CheckForUpdates`) → нижнюю границу не поднимать
без нужды. **Старое правило (отменено 2026-11-12, 0.8.x больше не поддерживается): если правка ломает работу мода на 0.8.3–0.8.4 (новые методы/типы игры, изменённые
сигнатуры), предупредить заранее, до правки.** Код мода — рефлексия по именам: отсутствующее имя на старой версии
должно давать мягкий отказ функции (предупреждение в логе), а не падение мода.

## Подводные камни (уже наступали)
* Noesis `ColumnDefinition/RowDefinition` наследуют FrameworkElement: два свойства `Width`/`Height` →
  `GetProperty` бросает AmbiguousMatchException. Хелпер `Prop()` идёт от реального типа вверх (DeclaredOnly).
* Noesis-коллекции: элементы брать типизированным `get_Item(int)` (`Item()`), не через IList/IEnumerable.
* `Visibility` панели фильтра Unit Reference держит игра (не перебить даже снятием привязки) → скрываем через
  `Opacity=0` + перенос соседей строкой Grid (`SpanOverPanel`).
* Unit Reference создаётся кодом через `WindowHost`; при `OnInitialized` его ещё не опознать → повтор в `Loaded`.
* `UnitTreeBrowserViewModel.UpdateFilteredMuseumEntries` ничего не делает при `_isInitializing` → страну
  выставлять после `FinishInitialization`.
* Делегаты Noesis-событий: `Delegate.CreateDelegate` на метод `(object, object)` (контравариантность).
* Anchor Chain грузит все папки модов; включённость берём из `usersettings.ini` [LoadOrder] через
  `SeaPower.Utils.openPersistentIniFile` (id = имя папки в StreamingAssets или Workshop id).
* `Singleton<T>.Instance` создаёт объект, если его нет → сначала `InstanceExists(false)`. Геттеры MainGameViewModel
  перебирать только нужного типа (некоторые трогают синглтоны).
* `ExecutedCustomCommandArgs` — struct: в Harmony-постфиксе брать через `object[] __args`.
* Прокрутку Unit Reference при смене закладки (`HookTabScroll`, была в 1.1.0) пользователь решил не возвращать.

## Окна миссии (как устроено в игре)
* HUD-окна «хостятся»: их view-model лежат в `WindowHostManager.Instance.CurrentHost.Windows`
  (`ObservableCollection<object>`); открытие/закрытие — `WindowHostCommands.OpenWindowCommand/CloseWindowCommand`
  (`CustomRoutedCommand.Execute(param, UIElement target)`, target — `MainGameView.Instance`),
  обработчики — `WindowHost.OpenNewWindow/CloseWindow`. Элемент `Window` при каждом открытии создаётся заново.
* Кнопки HUD (`MainGameView`: ToggleButton `FormationManagerWindow`, `EventLogWindow`, `UnitStatusWindow`,
  `CameraControlsWindow`, `MapWindow`) — `ToggleWindowBroker` (WindowContent = VM).
* Закрытие крестиком окна: `Window.Close` → `CloseAction` или `CloseWindowNow` → Close-команда хоста.

## Хуки
LoadScreen `<ExecuteLoadScreen>.MoveNext` (конец загрузки), `FileManager.SaveDirectories` (Apply в Mods),
`Window..ctor`, `OnInitialized` (prefix: открыть развёрнутым; postfix: раскладка), `ApplyScreenAnchor`,
`AbsoluteInitialPosition.AssociatedObject_Loaded`, `Kfa_Completed`, `MaximizeChanged`, `Restore_Completed`,
`CommandUnmaximizeWindow`, `HeaderDragDelta`, `ObjectInformationViewModel.set_MuseumEntriesVisible`,
`UnitTreeBrowserViewModel.set_SelectedNation` + RestoreSessionState/OnUnitsLoaded/SetupNations/FinishInitialization,
`DamageControlView..ctor` (+ `Loaded`/`IsVisibleChanged`) — SectionsMinHeight (ниже), UnitStatusViewModel — вкладка Reference.
Событие `Loaded` каждого окна — настройка до первого кадра (без мерцания).

## Настройки (.cfg)
[General] WindowPatterns, Backgrounds (1.5.3, as ModBackgroundsOnly), CascadeOffset, BringToFrontOnClick, AllowDragOutside, MinVisiblePixels, RememberOpenWindows,
RemoveSizeLimits, MaximizeOnDoubleClick, ImprovedUnitReferenceUI (бывш. ImprovedUI, миграция `BindRenamed`), SettingsButton,
SnapWindows, SnapDistance (1.3.0), Debug. [UnitReference]: TreeWidth, TreeVisible, Country; RememberSearchPanel, Category,
FilterByDate, SearchByNameOnly, SearchText (1.3.0); OptimizeTree, ScrollToSelected, ShowLoadoutList, ArmedOnly (1.4.0);
TreeWheelOnlyOver (1.5.3); EarliestVariantOnClick, KeepOpenedUnit, HideEmptyCountries, SearchInDescriptionAndId, LinkHelp (1.6.0); [UnitStatus] FullUnitName (1.6.0); [General] HideCustomMission, ColorIniFiles, DeleteOldLogs; [MissionEditor] RememberMissionFolder, LastMissionFolder (1.6.0).
[UnitStatus]: SectionsMinHeight (1.3.0), UnitReferenceTab (1.5.0). [WindowBorder]: Enabled, Thickness, Color, ToolTips (1.5.2). Удаляемые ключи прошлых версий — `DropRetiredEntries`.
[Layouts] `x,y,w,h[,max]` (раскладка всегда «с деревом»). [WindowBorder] — ниже.
[OpenWindows] open/closed (проверено в игре, включая мгновенный старт и блокировку открытия игрой). Окна HUD — через «рычаги» игры:
MainGameViewModel.MapVisible / UnitStatusVisible (VM: MainGameView.DataContext или SeaPower.Globals._mainGameViewModel),
кнопки MainGameView.tb_EventLogWindow / tb_FormationManagerWindow. Запись — опросом этих рычагов, изменение пишется,
если держится 1 с при видимом MainGameView. Фаза старта (из второй ветки): от начала экрана загрузки до 0,5 с после
его конца запись выключена, а `RestoreOpenStates` каждый кадр приводит окна к сохранённому состоянию, как только появился
новый MainGameView (старый, прошлой миссии, не трогается); ещё 3 с после — удержание. В фазе префикс
`WindowHost.OpenNewWindow` не даёт игре самой открыть окно, сохранённое закрытым (кроме Unit Status), и вызывает
`CloseWindowCommand.UpdateCanExecute`, чтобы кнопка HUD не осталась нажатой.
Окна с кнопкой HUD (Event Log, Formation Manager): «открыто» = VM есть в `WindowHostManager.Instance.CurrentHost.Windows`
(синглтон читать только после `InstanceExists(false)`), а не `IsChecked`: кнопка, нажатая до готовности хоста, остаётся
нажатой без окна. Пока хоста нет — ждём; нажата без окна → отжать и нажать снова (или просто отжать, если нужно закрыто). Unit Status: «закрыт без выбранного юнита» не пишется; если игра отказала
(нет юнита) — открываем, когда разрешит (до 2 мин). Прочие окна — по IsVisible окна.
Каскад: несколько окон одного шаблона (Weapon Reference, «Unit Reference - X») — сохраняет только первое (EarlyState.Cascade == 0),
остальные открываются со сдвигом CascadeOffset × число уже открытых (вправо — не дальше правого края экрана).
Считаются только окна в UI (IsVisible + есть визуальный родитель: Unit Status оставляет старые окна с IsVisible=true)
и того же «поколения» (номер экрана загрузки при создании окна: окна прошлой миссии висят, пока строится новая).

## Не подтверждено в игре / открытые вопросы
* 3D stutter with a second display, desktop mode off (archive 1103, open). SAM glitches of the beta 261123: AWC chat.
* Открытие Unit Reference развёрнутым (последняя правка: `,max` снимается только сворачиванием пользователя).

## Версия
* 13.10.2026: 1.3.0 проверена в игре и зафиксирована как стабильная (коммит «Alternative Game Interface 1.3.0»).
* 13.10.2026: 1.4.0 закоммичена («Alternative Game Interface 1.4.0»): дерево Unit Reference, прокрутка, список loadout'ов, Armed only.
* 13.10.2026: 1.5.0 закоммичена («Alternative Game Interface 1.5.0»): вкладка Reference в Unit Status, «Show all settings».
* 14.10.2026: 1.5.1 закоммичена («Alternative Game Interface 1.5.1»): окна установок как Weapon Reference; вкладка Weapons вынесена в Alternative Weapons Control.
* 28.10.2026: 1.7.0 — WindowedMapAndWorldView (рендер World View в текстуру окна), look-меню и уровни окон, ресайз докированных окон, перетаскиваемая панель времени, лента курса в World View, фото-режим, Track State, апгрейд кораблей в кампании.
* Версия: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе; и строку «Version N» в
  начале README.BBCode.txt / README.md (описание Steam). Список изменений к выкладке — одним списком, без разбивки по версиям.

## Локализация (Loc.cs, Release/lang)
- Ядро `Loc.cs` — из руководства (как в AWC): `Loc.T/F/Or/Register`, файлы `lang/<код>.ini` рядом с dll, язык игры или `[General] Language`. Тексты окна настроек не в таблице D, а регистрируются: `SettingsWindow.RegisterTexts` (вызывается в Awake после всех Bind) кладёт `tab.<вкладка>`, `grp.<группа>`, `lbl.<Секция>/<Ключ>`, `unit.…`, `help.…` (описание .cfg) для каждого поля окна. Описания скрытых записей (Category, TreeWidth …) не переводятся — их нет в окне.
- Вкладки, группы и подписи в `Field` остаются английскими идентификаторами (сравнения `Tab == TRef`); показ — через `TabOf/GroupOf/LabelOf/UnitOf/HelpOf`.
- Настройка `[General] Language` — поле вида 'l' (ComboBox: язык игры + `Loc.GameLanguages()`). После Apply со сменой языка окно перестраивается на следующем LayoutUpdated (`RebuildAfterLayout`, не внутри клика).
- Релизные файлы `Release/lang/*.ini` (9 языков) генерируются скриптом из исходников: ключи берутся из `D(...)` в Loc.cs, из `Add(...)` в SettingsWindow.cs и описаний `Bind`; переводы — позиционные файлы. При добавлении текста/настройки: добавить ключ в таблицу или поле, перегенерировать и перевести файлы (иначе игра допишет английскую строку в конец файла под `; --- new ---`). Скрипты лежали вне репозитория (/home/claude/loc).
- Не проверено в игре: шрифты игры могут не содержать CJK/вьетнамские глифы.

## Публикации
* 1.6.0 опубликована в Steam (22.10.2026; релизная DLL — сборка 20261021.0256, со старым опросом сообщений). Список изменений 1.6.0 дан пользователем.
* 1.6.1 (после релиза, по разнице строк DLL): события вместо обходов (ViewCapture), IsHitTestVisible=false для списка сообщений, кэш SearchTexts, ContactClock через делегаты.

## Two game builds (checked 2026-10-30)
The newer Release build (0.8.7-261021, decompiled from the user's GameFiles) was compared member by member with the older build: every game type and member the mod reflects exists in both, except `BlitToPipImage.Downsample` and `CameraManager.PipSupersampleFactor` (newer only). `PipSupersampleFactor` was already optional (factor 1 without it). `HidePipScene` used to switch itself off without `Downsample`; now it falls back to a plain `Graphics.Blit(source, pipImage)`, which is what the older build's own `OnRenderImage` does. Other differences between the builds (MainGameViewModel takes the window host from `RootControl.FindName("WindowHost")` instead of `WindowHostManager.CurrentHost`, `PipShown = ShowPip3D && IsHudVisible`, `Singleton<T>.OnDestroy` clears the instance) do not touch anything the mod reflects; `WindowHostManager.CurrentHost` exists in both.

## Translations (rule)
Every new option / group / text gets entries in ALL Release/lang/*.ini (cn de en es fr ja ko ru vn), real translations, not English copies: `grp.<group>`, `lbl.<Section>/<Key>`, `help.<Section>/<Key>` (+ `unit.` when the option has a unit). Check with a key diff against en.ini before building.

- Release test of 2.9.0 (2026-11-26, a user report): (1) the briefing was shown and the game not paused after a load: `LoadMetadataPostfix` / `StartTimerPostfix` were NOT in the source (lost when CampaignLoadedPostfix was removed; `TryPatch` takes the names as strings, so it built; the trace had "patch of LoadMetadata failed: Value cannot be null. Parameter name: method") - restored in LoadHooks.cs. LESSON: after removing code, grep that every `TryPatch(..., "Name", ...)` string has a method; the trace of a start must show no "patch of ... failed". (2) The Formation Editor was not remembered: the saved WindowPatterns was an earlier default - now such a list (exactly the old default, with or without Sensor Reference) is replaced by the new default once (trace "WindowPatterns: the saved list was an earlier default"). (3) The text of sonobuoys stays when the layer "Weapon Sonobuoys" (layer 23) is off: the game hides the icon (gizmo Visibility bound to the layer) but its label (`MapLabelLayer`, `MapLabelCandidate`, CeilingLayer only for info lines) is not tied to the layer; now the text lines of the sonobuoys are blanked while `MapBase.DisplayState.IsLayerVisible(23)` is false (part of CompactSonobuoys, SonoApply(c, small, hide)). (4) "not half size": not reproduced by code (same XAML / view model on both branches); the compact look starts only above SonobuoysThreshold (20) visible sonobuoys; a trace line now shows how many icons the walk found.

- 2.9.1 PREPARED (2026-11-26), not committed / not published: the fixes of the release test above (hooks of the load restored, WindowPatterns migration, sonobuoy text with the layer off). Test trace d7d6c014 (build 2210): hooks patched, "save load: the game is paused (no briefing)", sonobuoys 22 -> compact. Not yet seen in a trace: the layer-off line, a briefing shown + Play + pause.
- 2.9.1 also: Event Log "Wrap long lines" (EventLog/WrapLines, on): the game makes each `Alert.AlertContent` a `FormatBindableTextBlock` with the default NoWrap, so a long line is cut at the row edge; now `TextWrapping = Wrap` is set on the alerts shown when the window is found (LogWrapApply) and on each new alert in LogRaisePostfix (the same RaiseAlert hooks as the links; the watch is on when links or wrap is on). Switching it off sets NoWrap on the shown rows. Not yet seen in a trace: "event log: lines wrap".
- 2.9.1 also: Unit Reference "Countries by count" ([UnitReference] CountriesByCount, off): a small Button ("A-Z" / "9-1") inserted into the StackPanel right after the nation ComboBox (found by ItemsSource == Browser.Nations; ArmedBoxStep); SyncNations now builds the wanted list (All first, then the game's alphabetical order or by units found, ties by the game order) and ApplyNationOrder puts it in without removing the selected nation (a removed selected item would clear the combo box and the game's SelectedNation); counts are per nation id over the leaf entries that pass the search / date / armed / source filters (Collect, Tally); `_natSorted` makes one more pass when the option is switched off. Not yet seen in a game.
- 2.9.1 also: the country names get " (N)" (N = units found under the search / date / armed / source filters; "All" = the total): SyncNations writes `NationName` (a plain property of EncyclopediaNation, no change notification) and RefreshNationCombo re-assigns the drop-down's ItemTemplate so items and the selected one are drawn again; the game's own name is kept in `NationBase` (NationPlainName) and is what [UnitReference] Country saves / restores. Without any filter the hide-empty pass is a no-op as before (the counts themselves are always collected with ImprovedUnitReferenceUI). Risk to look at in a game: the template re-assignment while the selection is changing (the selection must stay).
- 2.9.2 fix (2026-11-27): the country counts / order / hide-empty are per open tab (user report): Collect starts from CurrentTab (the children of the SelectedCategory in MuseumEntries); the game does not re-filter on a tab change, so set_SelectedCategory has its own postfix TabSetPostfix -> SyncNations (Encyclopedia browser only, not while the game sets up). Not yet seen in a game.
- 2.9.2 change (2026-11-27): the sort switch is no longer a Button beside the combo but the LAST ITEM of the country list: an extra EncyclopediaNation (SortItemOf, NationId "All" so the game never sees a foreign id, name "[A-Z]   9-1" / "A-Z   [9-1]") added by SyncNations after the sorted nations (only with ImprovedUnitReferenceUI). NationSetPrefix (new prefix on set_SelectedNation) swallows it (returns false), toggles CountriesByCount, puts combo.SelectedItem back to the VM's nation (under _nationRestoring) -> the binding re-runs the game's filters -> SyncNations re-sorts and re-labels. The drop-down closes after the choice. Not yet seen in a game; risks: the combo box cannot be set from inside its own selection change; a selection that stays on the item. SetupNations clears the list: the item is put back on the next pass.
- 2.9.2 REDESIGN (2026-11-27, supersedes the "last item" idea above, user: ugly and did not work): CountryMenu.cs. The game's country ComboBox stays (Collapsed, its binding lives) and a Button + Popup of the mod (the look of the Source menu) replaces it in the same StackPanel: header = flag + name (N) + arrow; menu = a two-segment switch "A-Z" | "9-1" on top (the active one blue, SetNatCount) and the countries below (flag, name, count right; the selected one blue; click -> SetV SelectedNation, the menu closes; the menu closes when the mouse leaves it). The nations keep their plain names (no labels any more); SyncNations puts the counts in NatInfo (per browser) and calls NatRefreshFor (also from NationSetPostfix) to redraw the header / open menu. No sort item, no NationSetPrefix, no ItemTemplate trick. Not yet seen in a game: the Image flag source (TextureSource as ImageSource), the Button style of the game, popup placement in the StackPanel.
- 2.9.2 fix (2026-11-27): 9-1 -> A-Z did not go back: SetupNations ends with SelectedNation = All, which runs the filter pass (and SyncNations) BEFORE SetupNationsPostfix copies the list, so the copy (NationMaster.All) could be in count order. Now the copy is sorted by plain name (the game's order) in the postfix, and SyncNations does nothing while All is not in the list (stale copy). "A-Z" label localised (ui.natAlpha, tip.natAlpha in 9 lang files; cn 字母, ja 五十音, ko 가나다, ru А-Я, others A-Z).
- 2.9.2 change (2026-11-27): the country counts are of unit TYPES (user): a unit entry counts once per nation per IniRef (variants, squadrons, loadout variants of one ini are one; `_seenTypes`), "All" = distinct inis; weapons / sensors (no unit entry) still count each leaf. Not yet seen in a game.
- 2.9.2 change (2026-11-27): [UnitReference] CountriesByCount default is now true (by count); the last choice is saved on each switch (SetNatCount -> Config.Save). A cfg written earlier keeps its old false.
- 2.9.2 PREPARED (2026-11-27), not committed / not published: Steam has 2.9.1 (Nov 25, "Hotifx ... two hooks of the load"; verified with curl). 2.9.2 = everything after it: WindowPatterns migration, sonobuoy text with the layer off, Event Log wrap, Unit Reference country counts + sort button, coloured Show all / fewer settings button, trace line. The 2.9.1 notes above describe the work tree of the day; the git commit "2.9.2" carries 2.8.0 - 2.9.2.
