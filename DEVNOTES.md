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
- 2026-11-19: версия 2.6.0 подготовлена (csproj, PluginVersion, ChangeList.txt, ChangeList-1000.txt = текст Change Log для Steam, README). Не выложена в Steam, пока пользователь не скажет; не закоммичена.
- 2026-11-22: 2.7.0 is published on Steam (his word; the fetched change log still showed 2.6.0). Version 2.7.1 prepared: map labels no longer cut (GlZeroWithText in MapLabelFilter.cs). csproj, PluginVersion, ChangeList.txt, ChangeList-1000.txt, README.md, README.BBCode.txt updated. Prepared, not published, not committed.

## Keel: BlockingStart (2026-11-22, not committed)
`Release/_info.ini` has `[Keel] BlockingStart=true` next to `StartEarly=true`. Only a mod with it holds the game up, and only at one point: right before the game's first `LoadScreen.ExecuteLoadScreen` call (needs the Keel build that knows the key; an older Keel ignores it and the mod starts as a plain early mod, one frame step at a time, after the first picture). Plain `StartEarly` mods (ASE, ADM, AWC) start in the game's first frames next to the game. Reason: the game has no frames from the start of its own loading until its first scene is up (the Keel coroutine got its second step about 6 s after Awake), so a frame-based start could not beat the first picture.

## Map labels cut at the right edge (2026-11-22)

- Symptom (release 0.9.3): the name and distance lines of some labels (7001 Maritime Patrol, SAM sites, airbases) are cut at the width of the other lines. The trace line `label ... size 0x0,24x12,52x24,0x0` with `cut 0` and `lines 2/2`: the natural size of the name line is 0, so the box is as wide as the other lines only. It started after the scene changed (06:29:21), earlier labels had the name measured.
- Likely cause (not proven): GlHostMeasure measured the label before the text reached it, while the shape (`GlShape`) is taken from the view model, so the shape looked unchanged and the label was never measured again.
- Fix: in GlSyncSizes (the 0.15 s check) a contact line with text in the view model and a natural size of 0 makes the label measured again (`GlZeroWithText`). Checked in the game on the release 0.9.3: the labels are whole (a label can stay cut for a few seconds while its unit is just detected, then it is measured again).

## Оптимизация подписей на карте (2026-11-19, не закоммичено)
- `FastAcc.cs`: `Acc` — обёртка над FieldInfo/PropertyInfo с делегатами (FastGet) вместо рефлексии; все дескрипторы в `MapLabelFilter.cs` переведены на неё, `GetFloat` для Vector2 без боксинга. `TrackState.FieldV` — кэш по (Type, имя) без аллокации строк.
- `RectGrid.cs`: сетка 96 px для проверки перекрытий (вместо перебора всех `kept`/`icons` на каждый вариант положения); проверка та же (строго, поле 1 px).
- `LabelFilter`: переиспользуемые коллекции (`_glGroupSel`, `_glHasName`, `_glIdx`, `_glLeaders`, `_glNe`) вместо new на каждый проход.
- `GlSyncSizes`: форма текста сверяется не чаще 7 раз/с (`GlNat.ShapeAt`) и без аллокации (`GlShapeIs`); геттеры строк — через делегаты.
- Проверка известности габаритов иконок (`GlExtAt`) — раз в 0,25 с. `GetType().Name` заменён сравнением Type по ссылке (`GlIsUnitVm`/`GlIsFormation`).
- Профайлер: секции `LblFilter`, `LblArrange`, `LblSizes` (Prof.Count = 36). Замеров в игре пока нет — после трассы смотреть их.

## Цикл «Event Log» при старте миссии (2026-11-19, не закоммичено)
- В трассах 0512bd9d и 703c90e1: окно Event Log создавалось и закрывалось 200–300 раз за 5–6 с (~40 раз/с), fps падал до 29. Причина по коду: `RestoreOpenStates` каждый кадр фазы видел «кнопка нажата, а окна в хосте нет» (хост принимает новое окно через несколько кадров) и отпускал кнопку → игра закрывала окно → нажималась снова. Исправление: `_osPressedAt` — после нажатия 1,5 с (`OsHostWait`) кнопку не отпускаем. Проверить по трассе: «window loaded: 'Event Log'» должно быть 1–2 раза.
- Профайл боя (58 fps, 17 мс/кадр): мод ≈ 1,1 мс (подписи 0,78, формации по лидеру 0,02) — дальше оптимизировать нечего.

## Мониторы: окно на втором мониторе видно, но не кликается (отчёт игрока, не воспроизведено)
- Симптомы: окно на втором мониторе подсвечивается под мышью, клик не работает; перетаскивание окна двигает Tactical Display на первом мониторе. Это то, что бывает, когда мод/игра считают мышь находящейся на основном дисплее: всё держится на `Display.RelativeMouseAt` (`MouseDisplay`, `MonitorInputStep`, `MapMouseStatePostfix`, `PointerOn`); при нуле (мышь вне окна игры в системе координат основного дисплея) `cd = 0` → ввод идёт на основной вид, а `ForceUi` не включается.
- Добавлена трасса `MouseProbe` (Monitors.cs): строка «monitors: displays [...]» (размеры, активность, режим окна) и строки «monitors: mouse x,y -> RelativeMouseAt ...» при смене дисплея; «<- outside the game window but taken for the main display» = подозреваемый случай. До 60 строк за запуск. Нужна трасса от игрока (Debug), чтобы увидеть, что отдаёт `RelativeMouseAt`.
- `PressProbe` (Monitors.cs): строка «monitors: press on monitor N / the MAIN view at x,y (...): цепочка элементов» на каждый левый клик, дошедший до вида монитора, и на клик по ГЛАВНОМУ виду, пока мышь на другом дисплее (до 40 строк). Нет строки «press on monitor N» при клике = клик не дошёл до вида (путь `MonitorInputStep`); есть строка с цепочкой = дошёл, дело в элементе/команде; есть «press on the MAIN view» при мыши на дисплее N = главный вид берёт клики второго монитора (симптом «двигает Tactical Display»).
- Декомпиляция (получилось): `ilspycmd` уже стоит в `/tmp/ilspy` (если нет — ставить из репозитория NuGet нельзя без разрешения); запуск: `export PATH=$PATH:/opt/dotnet DOTNET_ROLL_FORWARD=Major; /tmp/ilspy/ilspycmd -p -o <dir> -r /home/claude/seapower.gamefiles/Managed <dll>` (~40 с на Seapower-Scripts.dll); отдельный тип: `-t NoesisView`. Старая сборка: `git -C seapower.gamefiles show <коммит>:Managed/<dll>`.
- Сравнение 0.8.7 (b405cb8) и 0.9.3-beta (e1571a8): код игры про дисплеи и мышь (`CameraManager`, `SecondDisplayMapHandler`, `MouseControlState`, `Display.*`, `Cursor.*`, `Screen.*`) не менялся; `NoesisView` (Noesis.NoesisGUI.dll) в исходнике идентичен, но сами Noesis.NoesisGUI.dll, UnityEngine.CoreModule.dll, InputLegacyModule, Unity.InputSystem перезаписаны в 0.9.0 (другой движок / нативный Noesis: это декомпиляцией не проверить).
- В моде `Monitors.cs` менялся в 2.5.0 (11-18): ItemsControl монитора обёрнут в `UserControl` (правый клик Formation Manager) и `MapMouseStatePostfix` вызывает `MouseDisplay()` один раз. Если у игрока проблемы начались с 2.5.0 — подозревать обёртку; если с 0.9.0 — движок.

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
Поддерживается Sea Power 0.9.0 и новее (`_info.ini` GreaterThanEqualToVersion=0.9.0; до 1.9.0 - ApproximateVersion=0.8.6; README и README.BBCode — тоже). Старые версии игры (0.8.x) больше не поддерживаются: правки под них не нужны, предупреждать не нужно; прежние запасные пути в коде безвредны и остаются, пока не мешают.
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

## Rename: Smart Windows -> Alternative Game Interface
Everything is renamed: namespace/assembly/dll/zip/csproj `AlternativeGameInterface`, plugin class `AlternativeGameInterfacePlugin`, GUID `com.rgreat.seapower.alternativegameinterface`, Anchor Chain id `io.github.rgreat.seapower-alternative-game-interface`, settings `StreamingAssets/user/AlternativeGameInterface/AlternativeGameInterface.cfg`, trace `BepInEx/AlternativeGameInterface.trace.txt`. Migration (once, when the new files do not exist): the old BepInEx cfg (`...smartwindows.cfg`, then `...windowmemory.cfg`) and `user/SmartWindows/SmartWindows.cfg` are copied over. The campaign data key `SmartWindows_StrictLoadouts` is kept as is (saved campaigns). Repository: rgreat/SeaPower.AlternativeGameInterface.

- Backgrounds: `[General] Backgrounds` = `Mods` (default, the old ModBackgroundsOnly=true) / `Mixed` (old false) / `Game` (only StreamingAssets/original pictures, when mods bring some). The old bool is read once from the orphaned entries. Settings kind `'m'` = drop-down over the entry's AcceptableValueList; value names are `val.<Section>/<Key>.<Value>` in lang files.
- Release/ui/backgrounds/ ships empty (not tracked by git): the mod's own background pictures (*.png) go there; the folder is in the zip.

- Title-bar buttons (SettingsUI.cs `ButtonsHost`): the game's Window template header is a Grid (title + HeaderThumb in column 0 '*', a ContentPresenter with the window's ButtonsTemplate in column 1 'Auto'); the template is usually a horizontal StackPanel (close button; the map: menu, maximize, close). Other mods insert their buttons there (SeaPowerTopo puts its bar before the Menu). The gear and the arrow are inserted as the first children of that StackPanel (layout, no overlap, the title shrinks); the overlay at the right of the Thumb is only the fallback when the buttons are built differently. The gear counts as present while it is inside the window (a rebuilt button panel takes it along, then it is made again).

## Settings hub (rgreat mods, 1.8.0, `src/SettingsHub.cs`)
One entry for the settings of all the author's mods in the main menu (under Mod Manager) and the pause menu (under Options); protocol v1, a registry in `AppDomain` data `rgreat.settings.v1` (BCL types only), so mods do not depend on each other. `src/SettingsHub.cs` is copied unchanged from the Alternative Special Effects repo (canonical copy there; never edit it here). Registration in `TryHook` (own try/catch), after the other hooks:
```csharp
SettingsHub.Register("alternative-game-interface", () => Loc.T("menu.button"), PluginVersion, 10, anchor => OpenSettingsFromMenu(anchor as Noesis.UIElement), () => Enabled);
SettingsHub.Install(harmony, asm, Trace, () => Loc.Language);
```
`Enabled` = the Mods-menu tick. `menu.button` = "Game Interface" (ru "Игровой интерфейс", others English) in `Loc.cs` and every `Release/lang/*.ini`. `OpenSettingsFromMenu(anchor)` (the main menu / pause menu view): starts from the view's Content (a UserControl has no panel of its own) and calls `SettingsWindow.Open(..., all flags false, showAll: true)`; the `showAll` argument is no longer used: the window opens in the "fewer settings" mode like from any gear (only the tabs without a window context; the "Show all settings" button is there). The window then goes into the topmost Grid above the view's content, like for the News window's gear in the main menu. Nothing in the settings code needs a game window: the Strict loadouts row only writes into campaign data when there is a campaign (`CampaignData()` null otherwise), the window lists work on the tracked windows that exist. The gears of the windows are not touched.

**Gear of the World View window (`src/ModChooser.cs`).** The gear of the World View window (`IsWorldViewWindow`: header "World View" or its own `WorldViewWindowViewModel`; the window's DataContext itself is usually inherited, so it is not tested alone) calls `ChooseSettings`: it reads the hub registry (`AppDomain` data `rgreat.settings.v1`), takes only the active entry whose id contains `special-effects` (the 3D effects; the other mods have nothing to do with the 3D window), adds our own entry (its open = the gear's usual `SettingsWindow.Open` with the window's flags) and shows the hub's own list through reflection on our copy's private `SettingsHub.ShowList` (so the shared `SettingsHub.cs` stays unchanged). No other active mod / reflection failure -> our settings open at once. Other windows' gears are unchanged.

## SettingsHub timing (1.8.0)
`SettingsHub.cs` (the new canonical copy) patches the menu classes only after the load screen has finished: patching them at plugin start ran their static constructors (Noesis properties) before Noesis was ready and crashed the Release build of the game. Because the hub now hooks the load screen iterator itself, `Register`/`Install` are called in `TryInstallLoadScreenHook` (right after our own load screen patch), not in `TryHook`, which runs after the load screen is done.

## Two game builds (checked 2026-10-30)
The newer Release build (0.8.7-261021, decompiled from the user's GameFiles) was compared member by member with the older build: every game type and member the mod reflects exists in both, except `BlitToPipImage.Downsample` and `CameraManager.PipSupersampleFactor` (newer only). `PipSupersampleFactor` was already optional (factor 1 without it). `HidePipScene` used to switch itself off without `Downsample`; now it falls back to a plain `Graphics.Blit(source, pipImage)`, which is what the older build's own `OnRenderImage` does. Other differences between the builds (MainGameViewModel takes the window host from `RootControl.FindName("WindowHost")` instead of `WindowHostManager.CurrentHost`, `PipShown = ShowPip3D && IsHudVisible`, `Singleton<T>.OnDestroy` clears the instance) do not touch anything the mod reflects; `WindowHostManager.CurrentHost` exists in both.

## Translations (rule)
Every new option / group / text gets entries in ALL Release/lang/*.ini (cn de en es fr ja ko ru vn), real translations, not English copies: `grp.<group>`, `lbl.<Section>/<Key>`, `help.<Section>/<Key>` (+ `unit.` when the option has a unit). Check with a key diff against en.ini before building.


## 3D stutter with a second display, desktop mode off (open)

Report: desktop mode off, World View fullscreen on the main display, the game's World View monitor off: the 3D picture updates
noticeably less often than the FPS says; turning the game's World View monitor on makes it smooth.
Code check: with desktop mode off the mod does not touch the cameras, the displays or the picture (MapShowPipPostfix, MapPausePrefix,
MapSetRectsPrefix, monitors - all gated by MapOn / MonitorsOn); the World View Render Rate (`CameraManager.UpdatePipRenderRate`) works
only while `PipShown` (the small picture over the fullscreen map) - left as the game has it.
Trace c0d46159 (desktop mode, two displays active, V-Sync on, no frame limit): every frame length is a multiple of 13.33 ms (75 Hz) and
in the mission the frames sit at 27 ms (every 2nd refresh). A second active display with V-Sync is the suspect (Unity presents to every
active display).
`RenderProbe.cs` (Debug + TracePeriodic): "render: setup" (V-Sync, frame limit, displays with refresh rates, game options WorldViewMonitor /
SecondMonitorEnabled / PipRenderRate, CameraManager PipShown / paused / skip frame / world display, enabled cameras with display, rect,
texture) on change, and every 5 s "render: pacing" (frames by refresh intervals, renders per camera via Camera.onPreRender).
Trace 1af7ecaa: main DM-MONB3205 74.96 Hz, second ASUS VS229 60 Hz. Display 2 is off in the menu (frames at 75 Hz); the mod activates it
(desktop mode + Monitors) and it stays active for the session (Unity cannot deactivate a display; `AlternativeGameInterface_Clear2`
clears it after desktop mode is switched off). From then on, with V-Sync, the game runs at exactly 60 fps: per 5 s 225 frames of one
75 Hz refresh and 75 of two - every 4th frame shown twice on the main monitor. All cameras render every frame (no skipping).
`SyncTest.cs`, [Debug] SyncTest (Off / NoVSync / MainVBlank / MainVBlankGated): Unity's V-Sync off while on (the frame limit and Adaptive
V-Sync stand aside), the mod paces in WaitForEndOfFrame: by time at the main refresh, or by the main display's vblank
(`D3DKMTWaitForVerticalBlankEvent`, a thread per active display, display -> \\.\DISPLAYn by the game window's monitor and pixel size);
Gated: cameras of another display (no target texture) are disabled for a frame unless that display refreshed since they last drew
(NoesisView keeps updating in LateUpdate; it renders in the camera's PreRender / OnPostRender, so only the drawing is skipped).
Trace "sync test ..." every 5 s (TracePeriodic): fps, waits, missed refreshes, refreshes per display, frames drawn per display.
Open question it answers: does Unity present to a display no camera drew to (if yes, Gated changes nothing).

## Monitors: the game's second-monitor view stayed on over the monitor's view (two Tactical Displays)

Trace c46f8ea4: game option WorldViewMonitor = 1 (World View on display 2); desktop mode switched on in the mission. The game's
`SecondDisplayMapHandler` (camera "UI Camera 2", depth 75, its NoesisView with the second-monitor map) is enabled only in its `Refresh`
(start / options change) - `MonSecondMapPrefix` on `EnableSecondMonitor` never ran, so the view stayed on display 2 over
`AlternativeGameInterface_Monitor2` (depth 0): `MonDetachedPostfix` made `WorldViewDetached` false -> `ShowSecondMonitorMap` true ->
a second Tactical Display there, without the monitor input routing (glitchy).
Fix: `SecondMapHandler(false)` after the monitors' views are built calls `EnableSecondMonitor(false, display)` when its camera is on;
`SecondMapHandler(true)` when the views are removed (desktop mode / option off) calls the game's `Refresh` (its options decide again).
Not related to [Debug] SyncTest. SyncTest MainVBlank in that trace: 75 fps with display 2 active (60 Hz display no longer holds the
frame), misses where the frame took longer than a refresh.
Trace fb42e025 (MainVBlankGated): 75 fps, display 2 drawn ~60/s - the rates hold, but frames visibly mix (tearing: with Unity's V-Sync
off the frames are presented at any point of the scan; on display 2 at a random phase, on the main display a few ms after its vblank,
after the render thread). Added SyncTest mode VSyncGated: Unity's V-Sync on (no tearing), no pacing by the mod, display 2's cameras
gated by its vblanks - answers whether Unity presents to a display no camera drew to (75 fps = it does not, 60 = it does).
Tearing on both monitors in the V-Sync-off modes (user). SyncTest mode PerMonitorVSync (`PresentHook.cs`): vertical sync per monitor
past Unity. The DXGI swap chain method table (slots Present 8, GetDesc 12, Present1 22; found on a throwaway D3D11 swap chain of a hidden
STATIC window - the table is shared by the process's swap chains) gets managed hooks (kept alive for the process; switching off only sets
`_phOn` false - the hook then passes through). Unity's V-Sync is off; in the hook the main monitor's swap chain presents with sync
interval 1 (ALLOW_TEARING dropped), another monitor's only when its vblank watcher counted a refresh since its last present (also
interval 1, so its queue never fills); otherwise the present is skipped and S_OK returned. Swap chain -> monitor by GetDesc
OutputWindow -> MonitorFromWindow -> \\.\DISPLAYn. Trace: "presents: <device> shown N skipped M". Risk: D3D12 (Unity may track the back
buffer index itself - a skipped present may break it); the trace names the graphics API.
Trace 04c525d3 (PerMonitorVSync, Direct3D11): 37.5 fps, every frame two main refreshes, display 2 presents never skipped (37 < 60).
Each Present with sync interval 1 waited for its own monitor (one frame queued per swap chain): main refresh + display 2 refresh, in
sequence -> two main intervals. Fix: another monitor presents with sync interval 0 without ALLOW_TEARING (flip model: shown at that
monitor's next refresh, no tearing, no wait), still only after its refresh.
Drag across displays sometimes lost (user): `TryStartTransfer` failed silently; now traces "drag to display N not started: <why>"
(when a title bar drag was seen in the last 2 s).
Trace 2ff04333 (PerMonitorVSync, interval 0 on display 2): fps 52-59 with 1x/2x refresh mix, display 2 presents never skipped -
interval 0 without ALLOW_TEARING still waited for display 2's queue. Now: display 2 presents with interval 1 | DO_NOT_WAIT, at most once
per its refresh; WAS_STILL_DRAWING -> that present dropped (S_OK to Unity). Trace: shown / skipped (no refresh yet) / dropped (queue full).
The game froze at the end of that session (right after dragging World View to display 2); Player.log has nothing. The hook had locks on
the render thread (a dictionary + the watchers' lock) - removed: the render thread keeps its own array of swap chains, the watchers come
as an array swapped in by the main thread (`_phWatchers`, `_phGen` to re-match).
[General] MonitorFps (`MonitorFps.cs`): frame counter per monitor - a small Border box in each view (main: the first panel of the mission HUD
view, game fps + presents to monitor 1; other monitors: the monitor view's root, presents to that monitor; without the hook = the frame
rate). Dragged by the mouse (capture), kept wholly inside its view (also when the view shrinks or the text grows), place per display in
[Debug] MonitorFpsPlaces "d:x,y;...".
Trace a5259b3b (encyclopedia 3D, one unit, no CPU load): PerMonitorVSync with display 2 at interval 1 | DO_NOT_WAIT - exactly 37.5 fps,
every frame two main refreshes, display 2 never reported a full queue. A vsynced presentation on the other monitor holds the frame anyway.
Now display 2 presents with interval 0 and Unity's own flags (ALLOW_TEARING kept if given) | DO_NOT_WAIT, once per its refresh; the main
monitor keeps interval 1 without tearing. Display 2 may tear.
Drag across displays lost the window: the transfer kept the grabbed point in pixels of the old display (2560x1440, 1.333 px per unit)
and placed it with the new display's scale (1.0): the continuing press landed below the title bar (Pip3DView instead of HeaderThumb in
the trace). Now Xfer.Ox / Oy are UI units; PlaceUnderMouse: X = mouse / scale - Ox + Ix.
Goal (user): V-Sync on every monitor. SyncTest PerMonitorVSync now: the other monitor presents interval 0 without ALLOW_TEARING |
DO_NOT_WAIT, once per its refresh (flip model: shown at that monitor's next refresh, no tearing; a full queue -> dropped, not waited).
PerMonitorVSyncTear (mode 6): the same with Unity's own flags (may tear) - to see which of the two holds the frame.

## Hang on mission load with SyncTest PerMonitorVSync; other monitors drawn under a load screen

Trace 1f70ae17 + logs: the game hung while loading a mission (the trace stops, nothing in the logs); both hangs so far were with the
present hook on (Keel also reported the previous game as not ended normally). Suspect: the managed hook on Unity's render thread - a
Mono stop-the-world can suspend that thread inside DXGI with its lock held. Not fixable from C#; a native (C++) hook would be needed.
User told not to test PerMonitorVSync / PerMonitorVSyncTear for now.
The other display showed windows (World View with live 3D) while the load screen still covered the main one. Now `LoadScreenRunning`
(set by the load-screen coroutine's first step, cleared at its end) hides the monitor views' roots (Visibility.Hidden) while it is on;
MonitorsStep checks it every frame. Risk: a load-screen coroutine that never ends with MoveNext() == false would keep them hidden.

## Drag across displays: the window follows the mouse until the button is released

The transfer used to end with a synthetic press on the new display's view at the mouse; it had to land on the title bar - a fast mouse
that had already left the title bar (or a placement off by a few units) lost the drag. Now after the window appears on the new host the
mod moves it itself every frame (`PlaceUnderMouse`) while the button is held; the mouse going on to another display (back to the first
too) moves the window there and it keeps following; release ends it ("drag across displays ended on display N"). No snapping while
carried this way. Only the wait for the window on the new host is limited (4 s).
Trace 46e6b77a: timeScale 1 already while the load screen was still on (from 23:45:55, load screen ended 23:45:58) - the game unpauses
before its load screen is gone, hence the moving waves / rocking ship seen on the other display before. PerMonitorVSync (tested again)
hung again; fps 43-57, display 2 never skipped/dropped (interval 0 without tearing still holds the frame).
Hypothesis (with the user): the frame latency limit (`QualitySettings.maxQueuedFrames` -> IDXGIDevice1::SetMaximumFrameLatency) is one
for the device; a present to the 60 Hz monitor stays queued until its refresh and takes a place the main monitor needs (a per-swap-chain
limit needs FRAME_LATENCY_WAITABLE_OBJECT at creation - Unity's choice). SyncTest PerMonitorVSync / Tear now keep maxQueuedFrames at
max(3, active monitors + 1) (restored when the mode is left) on top of the per-monitor present gating. `render: setup` shows
maxQueuedFrames. Test only (the managed hook can hang the game).

## Decision: no present hook; Adaptive V-Sync with several displays syncs to one monitor (MultiVSync.cs)

The DXGI Present hook (PresentHook.cs) and [Debug] SyncTest are removed: the managed hook on Unity's render thread hung the game three
times and never gave the full rate (also with maxQueuedFrames 3: ~45 fps, the time went between the mod's Update and the last
LateUpdate - the main thread waiting for the render thread).
Now (user's design): Adaptive V-Sync (game V-Sync option 1 + [General] AdaptiveVSync) with more than one active Unity display ->
`MultiVSyncStep` (from AdaptiveVSyncStep): Unity's vSyncCount 0, the frames wait at WaitForEndOfFrame for the next refresh of one
monitor (vblank watcher threads, D3DKMTWaitForVerticalBlankEvent - background threads, not the render thread); the other monitors
get the frames without vertical sync (may tear). The monitor: [General] AdaptiveVSyncDisplay (Auto = the fastest measured, "1".. = that
display when active); the settings window shows it as a drop-down (kind 'd', DisplayEditor) of the displays with name, size and rate
(DisplayLabel: Unity display -> system DisplayInfo of the same size, the game window's display for the first). Adaptive: average frame of 0.5 s over 1.2 refreshes -> not synced; no frame over 0.85 refresh for
3 s -> synced again. The game's frame limit below the rate: no waiting (the mod's precise limiter paces). One display again ->
`MultiVSyncOff`, the usual Adaptive V-Sync (Unity's vertical sync) restarts. Known from the MainVBlank test: the synced monitor itself
may tear a little (the render thread presents a few ms after the wait).
[General] MonitorFps (Windows > Performance): "FPS: N" on every monitor, same format everywhere. [Debug] MonitorFpsInfo (off by default) adds the
monitor number + measured refresh rate and, on the main one, the monitor V-Sync is synced to.
Adaptive V-Sync never switched on (trace 635807d9: game V-Sync = Adaptive V-Sync, yet no "adaptive V-Sync on" in any trace):
`GameVsyncOption` looked for IniHandler.readValue(string, string, int) with exactly 3 parameters; the game's has a 4th
(bool performLogging = true) - already in 0.8.7 (the oldest build in SeaPower.GameFiles), so the mod's Adaptive V-Sync never worked -> not found -> the option read as 0 -> Adaptive V-Sync off, Unity's plain vertical sync stayed.
Fixed: 3 or 4 parameters (performLogging false passed); a trace line when not found.

## Monitors: double-click on a title bar missed (trace a2ce4b88)
The mod makes the double click for the monitor views itself (`MonitorInputStep`: Noesis MouseDoubleClick instead of MouseButtonDown). The missed ones
were clicks 240 ms apart with the mouse 5-7 px off (limit was < 5 px and 0.35 s). Now: the system double-click time (GetDoubleClickTime, 0.5 s
default) and <= 10 px.
MonitorFps outside a mission: `MfMainPanel` takes the first loaded screen of MainGameView, MainMenuView, PauseMenuView, LinearCampaignMapView (static Instance); the box moves between screens by itself (MfBox re-parents). Only display 1 has a counter there (monitor views exist in a mission only).
Trace 3438b1bf: on display 2 the double click arrives (pair 120-160 ms, "z-order (maximize)" twice), but no layout change follows (no texture / dock reach lines) for a World View built on display 2 from the saved state; after dragging it across displays (window rebuilt) the same double click maximized. Cause not found yet: ToggleMaximized now traces "double-click on 'X': Maximized a -> b, size, loaded" and 0.6 s later "double-click result: ..." (DoubleClickCheckStep).
Probable cause (user: after dragging across displays the double click works again): the window built on display 2 had "no title bar yet (HeaderThumb not visible)" when HookHeader ran; its template was applied later, so the hooked Thumb was not the one on screen. HookHeader now also runs where the gear finds the visible title bar (SettingsUI). Diagnostics from ToggleMaximized kept.
Trace 9a9a12fd: a window moved to display 2 from the saved state got ToggleMaximized twice per double click (same ms: Maximized False->True, True->False = nothing); after a drag across displays only once.
Cause: Noesis keeps managed proxies weakly (`Extend._proxies`), so after a GC the same native header Thumb is another proxy object; `HeaderHooks` (a ConditionalWeakTable by proxy) missed it and HookHeader (Loaded after the move, activation, gear) added a second handler. Now the Thumb is marked natively (Tag = HeaderMark) and the handler finds its window from the sender (WindowAbove) instead of a weak reference to a proxy.
Double click on the left part of a title bar of a World View dragged to display 1 does nothing (right part works; user). Trace 11004aec has no press lines for display 1 (PressProbe skipped main-view presses on the main display; limit 40) - now logged too (limit 80): the chain of elements under the press shows what takes the click (candidates: Event Log / Unit Reference windows above the World View in z-order at the left, the FPS counter box, a resize thumb).
Empty-place double click on a fully transparent (0 %) window: its frame blinks red twice (0.34 s). WindowLook.cs: `EmptyDoubleClick` hooked to the window's MouseDoubleClick in HookGhost (idempotent: a second handler through a weak proxy only restarts the blink); empty = no Thumb / button / list / text / image / tab item between OriginalSource and the window; `RedBlinkStep` (called from UpdateCore only while blinks exist) swaps the frame Border's brush/thickness and restores them. Trace line (Debug): "empty place double click on ...".
MonitorFps places per screen: the main counter's place is kept per view it sits in (mission = key "0" as before; "0.MainMenuView", "0.PauseMenuView", "0.LinearCampaignMapView") in [Debug] MonitorFpsPlaces. MfMainPanel returns the mode; the box is recreated (new place) when the view changes. Monitor counters (display 2+) have one place.
Cleanup of the double-click investigation: removed the 0.6 s "double-click result" check, the MAIN-view press probe (PressProbe is for monitor views only again, 40 lines) and the extra HookHeader call in the gear code (the HeaderMark Tag + WindowAbove fix is the real one).
Adaptive V-Sync stuck off in a paused game (trace 0201695c): paused, the frames run at 100+ fps with the sync off, but a frame over 85 % of a refresh (GC, a label pass: about once a second) came in nearly every 0.5 s window, so the rule "no such frame for 3 s" never let the sync come back (26 s off in the trace; with the sync on the frames in that scene missed refreshes: 8 of 30 late, then off again after 4 s). Now (FrameLimit.cs `AdaptiveFits`, the same in MultiVSync.cs): back on when the average frame of the 0.5 s window is under 80 % of a refresh (average only, single long frames do not decide), 3 s after it was switched off (no back-off: a one-off load hitch must not delay the return). Open: why the frames in the paused scene miss refreshes with the sync on although they run at 9 ms without it (frame times 20-26 ms that are not multiples of 13.3 ms; borderless window under DWM / GPU clocks down under sync?) - then the sync stays off most of the time and the frames are not capped (by design: adaptive-off = no limit).

2026-11-22: Adaptive V-Sync (FrameLimit.cs, MultiVSync.cs) is average-only now: off when the 0.5 s average frame > 1.2 refreshes, back on after 3 s when it is < 0.8 refresh; the late/over frame counters and the back-off are gone. Known: a one-off load hitch at the menu start (trace 0bc85866: 9 of 12 frames late at 1.3 s) still switches it off for 3 s.
2026-11-22: the Adaptive V-Sync window is 2 s now (was 0.5 s): the game has a ~90 ms stall every second (also without the mod, trace 7d186eba), in a 0.5 s window it lifted the average to 16-18 ms (> 1.2 refreshes) and the sync flipped off/on every few seconds; over 2 s the average stays ~14 ms.
2026-11-22: Adaptive V-Sync: off by the 2 s average (see above); back on at once by a 0.5 s average < 0.8 refresh (no waiting pause: the unsynced frames are short, so a high fps is enough).
2026-11-22: FPS counter window (MonitorFps.cs `MfInside`): kept wholly inside the screen of its view (top visual element, screen rectangles, scale allowed for); the size is re-measured after each text change (more digits in the FPS), so it is moved at once; `_mfWant` keeps the place the user chose, the box goes back to it when the text is shorter again.
2026-11-22: MfInside bug: `DesiredSize` includes the margin, so a box standing at the right (big left margin) was taken as wider by that margin and was thrown to the left, then back (jumping every 0.5 s); the margin is subtracted now.
