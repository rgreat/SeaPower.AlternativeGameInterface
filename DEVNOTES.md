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
