# Smart Windows — заметки для продолжения работы (для Claude)

Мод для Sea Power: BepInEx 5 + Harmony, загружается через Anchor Chain (Workshop 3380210757).
UI игры — NoesisGUI (XAML), окна — `SeapowerUI.Window`. Доступ к игре — через рефлексию (в csproj нет ссылки на
Seapower-Scripts), Harmony-хуки ставятся по именам методов. Noesis: основной код — через рефлексию, окно настроек и шестерёнка
(1.3.0) — напрямую (ссылка на Noesis.NoesisGUI в csproj, как в Alternative Damage Model).

## Сборка в песочнице (проверено)
1. `apt-get update && apt-get install -y dotnet-sdk-8.0` (без `update` пакеты дают 404).
2. Репозитории проекта Sea Power (все приватные, владелец rgreat; в облачной сессии Claude Code — `add_repo`, клон
   `/home/claude/<имя в нижнем регистре>`, вместе с бинарными файлами; в обычном чате GitHub отдаёт 404):
   SeaPower.SmartWindows (этот мод), SeaPower.AlternativeDamageModel, SeaPower.AlternativeWeaponsControl (вкладка Weapons,
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
5. `dotnet build -c Release -p:ManagedDir=... -p:BepInExCore=... -p:AnchorChainDll=...` → `Release/SmartWindows.dll`.
   Номер билда (UTC-время сборки `yyyyMMdd.HHmm`, свойство `BuildStamp` в csproj → AssemblyMetadata "Build") пишется в лог:
   `Smart Windows <версия> (build ...) loaded` — по нему видно, какая dll реально запущена.
   Сборка сама копирует dll в `Release/` (target StageRelease) — пробные сборки делать на копии репозитория.
   Скрипт `/home/claude/tools/build.sh [repoDir] [zip]`: сборка с путями песочницы + упаковка `SmartWindows.zip`.
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
Каждая сборка — `SmartWindows.zip` БЕЗ корневой папки: `Release/` (SmartWindows.dll, _info.ini, thumbnail.jpg),
`src/`, `SmartWindows.csproj`, `nuget.config`, `PUBLISHING.md`, `DEVNOTES.md`. README пользователь пишет сам.
thumbnail.jpg берётся из клона репозитория.
Общение — по-русски, коротко; в .cfg — один переключатель на функцию. В отчётах не повторять рутинное («собирается без
ошибок», «ничего не коммитил», «не проверено в игре»). Править исходники и коммитить — только по просьбе пользователя
(напоминание stop-hook о незакоммиченном — молча пропускать).

## Совместимость с версиями игры
Поддерживаются Sea Power 0.8.6 (`_info.ini` ApproximateVersion=0.8.6; README и README.BBCode — тоже).
`ModCompatibility.Matches` → `SemanticVersion.ApproximatelyEquals`: тот же Major.Minor и Patch ≥ указанного.
Несовместимому моду игра не скачивает обновления из Workshop (`FileManager.CheckForUpdates`) → нижнюю границу не поднимать
без нужды. **Правило пользователя: если правка ломает работу мода на 0.8.3–0.8.4 (новые методы/типы игры, изменённые
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

## Дерево Unit Reference (как устроено в игре, 0.8.5)
* Данные: `EncyclopedicDataManager.MuseumEntries` (строится корутинами при старте сессии, `UnitsLoaded` / `UnitsLoadedEvent`):
  8 корневых категорий `MuseumEntry` (CategoryKey Vessels / Submarines / Aircrafts / Helicopters / LandUnits / Biologic, плюс
  Weapons {Guns, Launchers} и Sensors) → тип юнита (`_typeFullName`; сначала военные, потом гражданские, по алфавиту) → класс
  (`UnitTreeGrouping.GroupClassEntries`: ключ — EncyclopediaTag или имя до «-class»; один юнит — лист, несколько — групповой
  узел `IsGroupNode` с флагом страны `GroupNation` (если страна одна), годами min/max и детьми с `TreeLabelOverride` =
  уточнение после имени класса или год) → листья `MuseumObjectEntry` (Surface/SubSurface/Aircraft/Land/Biologic) и
  Weapon/Sensor/Gun/Launcher. Папка — ровно тип `MuseumEntry`, лист — подкласс. Строка: `TreeIcon`, `TreeLabel`, `TreeYearText`,
  `CountText` «(N)» (листья под узлом после фильтров).
* `UnitTreeBrowserViewModel(stateKey, isEntryVisible, canRememberEntry)`: КОПИЯ дерева (`CloneForFiltering`, у каждого окна своя,
  IsExpanded/IsSelected свои) без скрытых (`HiddenInEncyclopedia`; опустевшие папки выкидываются). Unit Reference: stateKey
  "Encyclopedia", `UseCategoryTextLabels`, спасательные плоты не запоминаются. Состояние сессии (`_persistedStates[stateKey]`,
  static, до выхода из игры): раскрытие узлов (`ExpansionKey` = «имя|год» или «grp:имя»), страна, FilterByDate, категория,
  последний выбранный (IniRef + VariantIndex + DisplayName). Пока `_isInitializing`, сеттеры не пишут в состояние и
  `UpdateFilteredMuseumEntries` ничего не делает; сбрасывают `RestoreSessionState` / `FinishInitialization`.
* Что видно: `TreeItems` = дети выбранной категории (`SelectedCategory`; полоса категорий `RootCategories` видна при > 1) из
  `FilteredMuseumEntries`; при поиске — все категории сразу. Фильтры (`UpdateFilteredMuseumEntries`, через `FilteredMuseumEntries`
  каждого узла копии; опустевшие папки отпадают): страна (`Nations`: [All] + страны листьев по алфавиту; у оружия/сенсоров — их
  `Nations`); FilterByDate (год миссии: редактор → Environment → CampaignEnvironment → 1985; лист проходит, если
  ServiceEntryDate ≤ год ≤ ServiceExitDate или дат нет); поиск (задержка 250 мс; имя, класс; без SearchByNameOnly — ещё оружие,
  сенсоры, авиагруппа). При поиске всё раскрыто (если строк ≤ 400), без поиска — сохранённое раскрытие; перед сменой фильтра
  раскрытие запоминается.
* Выбор: `SelectedEntry` — у группового узла берётся лучший вариант (в строю в год миссии, иначе самый новый) →
  `SelectedEntryChanged` → `ObjectInformationViewModel.SelectedMuseumEntry`. `RevealEntry` (категория, раскрытие пути,
  IsSelected), `SelectFirstVisibleEntry`; буквы/цифры в дереве — переход по префиксу TreeLabel (сброс через 1 с); двойной щелчок
  (не по стрелке) — `EntryActivated` (Unit Reference на него не подписан).
* Unit Reference (`ObjectInformationViewModel(context, initialIniRef, ..., showTree)`): с деревом и юнитом —
  `RestoreSessionState(restoreSelection: false)` + `RevealEntry(юнит)`; без юнита — `RestoreSessionState()` и, если ничего не
  выбрано, первый лист; без дерева — `FinishInitialization`. «<»/«>» — `ToggleTreeCommand` → `MuseumEntriesVisible`. Окна из
  ссылки на оружие/сенсор — ObjectWeapon/SensorInformationViewModel без дерева.
* Вид `UnitTreeBrowserView` (XAML в ассетах): TreeView; KeyDown → TypeAheadJump, двойной щелчок → ActivateSelected.
* Мод: ширина колонки дерева и сплиттер, скрытие дерева вместе с панелью поиска и сужение окна, страна и панель поиска
  ([UnitReference]) — после методов инициализации браузера (`NationBusyPrefix/Finalizer`).

## Оптимизация дерева Unit Reference (OptimizeTree, 1.4.0, `src/TreeTweaks.cs`)
* «Варианты» в игре — двух видов:
  1) loadout-юниты: у военного корабля/ПЛ (папка vessels, не гражданский) с > 1 loadout каждый loadout, кроме Default/Empty/Ferry,
     — отдельный юнит (`UnitMetadataProvider.AppendLoadoutUnits` → `SimpleObjectParameters.CreateLoadoutUnit`, ключ «ini@loadout»),
     имя «Класс [Loadout.DisplayName или Name]»; без суффикса, если loadout — Early/Late с годом ввода. В дереве это отдельные
     классы, их объединяет групповой узел (`GroupClassEntries`), подпись в группе — уточнение («Late») или год;
  2) корабли класса: секции VariantN ini юнита (`_variants`, `VariantInfo`: ObjectName — имя корабля, годы, страна,
     LoadoutVariant — какой loadout у этого корабля) — дети записи класса (`GetIndividualVesselFromMetadata`, по годам, потом
     по имени), лист `MuseumObjectEntry` с `PredefinedLoadoutVariant` = LoadoutVariant. Его loadout в названии игра НЕ показывает.
* Loadout-юнит в дереве: `IniRef` = ini базового юнита, `IsLoadoutUnit`, `PredefinedLoadoutVariant` = имя loadout; дата — год
  loadout (через `ApplyLoadoutServiceYear` его корабли получают этот год), иначе как у базы; корабли — те же, что у базы
  (это корабли, которые можно модернизировать; так и оставлено).
* Мод (постфикс `UnitTreeBrowserViewModel.set_MuseumEntries` — копия дерева браузера, в конструкторе и после загрузки юнитов;
  только браузер "Encyclopedia", только категории юнитов, при OptimizeTree (от ImprovedUnitReferenceUI не зависит); повторный вызов безопасен):
  - классы внутри типа — по ServiceEntryDate (без даты в конце), затем по имени;
  - групповой узел класса, где есть loadout-юниты: юниты (по IniRef) по дате базы (без базы — min дат вариантов), затем по
    подписи; под каждым его варианты: по дате, при равной — Early, база (= Default), остальные в порядке `_loadouts` ini;
  - подпись (`TreeLabelOverride`): уточнение юнита (`GetClassQualifier` от имени базы, или имени варианта без « [..]»; пусто →
    имя класса) + « [Loadout.DisplayName]» у вариантов; без суффикса у Default и если вариант у юнита один; юниты без
    loadout-вариантов сохраняют подпись игры;
  - юнит без loadout «Default» (но с вариантами) — его собственный лист убирается из копии;
  - пример: Spruance-class: ASROC [Early], ASROC, ABL, ABL [Anti Ship], ABL [Land Attack], VLS; Udaloy-class [Early] 1980,
    Udaloy-class 1985, Udaloy-class [Late] 1988.
  Данные игры (EncyclopedicDataManager) не меняются. Всё это есть и в 0.8.4.

## Прокрутка к выбранному (ScrollToSelected, 1.4.0, `src/TreeScroll.cs`)
* Игра выделяет запись (`RevealEntry`: раскрывает родителей, IsSelected) и ставит `SelectedEntry`, но дерево к ней не
  прокручивает. В TreeView игры на ScrollViewer висит `SuppressBringIntoViewBehavior` (гасит BringIntoView, если цель хоть
  частично видна), поэтому мод прокручивает сам.
* Каждый кадр (Step) для окна Unit Reference с показанным деревом: если сменился `Browser.SelectedEntry`, коллекция
  `TreeItems` (категория, фильтры) или дерево снова показано — путь к записи по `TreeItems` → `FilteredMuseumEntries`,
  контейнеры `ItemContainerGenerator.ContainerFromItem` вниз по пути (до свёрнутого узла — тогда к нему), строка — `PART_Header`
  шаблона (иначе весь TreeViewItem), ScrollViewer — ближайший предок контейнера. При открытии / показе / смене элементов —
  по центру (если не виден целиком), иначе минимальный сдвиг. `CanContentScroll` → `BringIntoView`. До 60 кадров попыток.
* Не зависит от ImprovedUnitReferenceUI. Noesis-типы напрямую (TreeView, TreeViewItem, ScrollViewer, VisualTreeHelper); есть и в 0.8.4.

## Дерево Unit Reference: опции 1.6.0 (`src/TreeSearch.cs`, `src/HoverHelp.cs`, все в [UnitReference] / [UnitStatus])
* Общий механизм: postfix `UpdateFilteredMuseumEntries` (ArmedFilter.cs: `FilteredEntriesPostfix` → `FilterArmed`, затем `AfterFilters`).
  Все патчи 1.6.0 ставятся через `TryPatch` (отказ одного не ломает остальные). Только браузер Encyclopedia (`IsEncyclopediaBrowser`).
* EarliestVariantOnClick: postfix `SelectBestGroupEntry(group)` — если включено, `__result` = ребёнок группы с минимальным
  ServiceEntryDate > 0 (при равных — первый в списке). Игра: в строю в год миссии, иначе новейший.
* KeepOpenedUnit: ObjectInformationViewModel(…, initialIniRef …) — prefix/finalizer ставят `_openingForUnit`; postfix
  `RevealEntry` в этот момент запоминает узел (`Pins[browser]`). `InjectPinned` после фильтров вставляет путь категория→тип→класс→
  юнит в `FilteredMuseumEntries` на нужное место (порядок — по полному списку); вернувшийся узел получает список из одного
  ребёнка (его `FilteredMuseumEntries` после `ResetFilters` — «полный», остался от прошлого).
* HideEmptyCountries: master-список `Nations` запоминается postfix'ом `SetupNations`; после фильтров по листьям полного
  дерева (поиск через игровые `MatchesTextSearch`/`MatchesWeaponTextSearch`/`MatchesSensorTextSearch`, дата, Armed only)
  собирается множество стран (Nation / Nations / civ_ → Civilian), в `Nations` остаются «All», найденные и выбранная.
  Вставка/удаление на месте (ObservableCollection), порядок мастера.
* SearchInDescriptionAndId: postfix `MatchesTextSearch(entry, searchLower, nameOnly)`: при nameOnly=false и отрицательном
  результате — IniRef (id) или Description без тегов содержит запрос.
* LinkHelp (HoverHelp.cs): раз в 0,6 с по видимому дереву окна UR и вкладки Reference ищутся Button с CommandParameter —
  Museum*Entry не-юнит (оружие, сенсор, пушка, пусковая, система); ToolTip = TextBlock (MaxWidth 380) с началом `Description`
  (теги убраны, ≤ 480 знаков). Entry меняется → тултип обновляется.
* FullUnitName ([UnitStatus]): postfix `UnitStatusViewModel.SwitchUnit` ставит `UnitName` = `ObjectBase.DisplayNameFull`
  (игра: DisplayNameShort).

## Меню и файлы (1.6.0, `src/Pickers.cs`)
* HideCustomMission ([General]): postfix статического `ScenarioMenuViewModel.GetMissions` (рекурсивный) убирает `Mission` с
  FileName «_Custom Mission.ini» (`Globals._defaultMissionName`).
* RememberMissionFolder ([MissionEditor] + LastMissionFolder): prefix `MissionEditorViewModel.LoadMission(filename)` пишет папку файла
  (внутри StreamingAssets, не _temp); postfix `FilePickerViewModel.Initialise()` для режима OpenFile и initialPath …/user/missions
  ставит `SelectedFolder` на сохранённую (если есть). Окно «Save as» (SaveFile) не трогается.
* ColorIniFiles ([General]): `PickerStep` (в цикле Update; окно с `FilePickerViewModel` в DataContext, чей `Context` — `MissionEditorView` (только редактор миссий), определяется раз в 0,4 с,
  дальше каждый кадр — ListView окна, строки появляются при прокрутке): TextBlock'и с Text *.ini получают голубую кисть (прежняя запоминается, возвращается при переиспользовании строки).
* DeleteOldLogs ([General], по умолчанию ВЫКЛ — удаление): `CleanLogsOnce` в Awake (до того, как игра начнёт сессионные файлы; повтор в Update безвреден) — `Application.dataPath/Logs`, файлы с
  «_session» в имени (`*_session_N.*`, `*_sessionEventLog_N.html`), кроме `Log.CurrentLogFileName/CurrentEventLogFileName` и
  изменённых позже старта процесса − 1 мин. Player.log / Player-prev.log (Unity) не трогаются.

## Колесо только над деревом ([UnitReference] TreeWheelOnlyOver, 1.5.3, `src/TreeScroll.cs`)
* Жалоба: дерево Unit Reference прокручивалось колесом, когда курсор вне дерева (причина в игре не найдена; вероятно, область
  TreeView/ScrollViewer больше видимой ячейки). Мод (Step, при показанном дереве) один раз на TreeView вешает PreviewMouseWheel:
  курсор (`e.GetPosition`) вне границ TreeView или любого предка до окна → `Handled`, а колесо отдаётся ScrollViewer'у под
  курсором (`VisualTreeHelper.HitTest` от окна, вверх до ScrollViewer с ScrollableHeight > 0, не внутри дерева; шаг Delta·0,4).

## Список loadout'ов для всех юнитов (ShowLoadoutList, 1.4.0, `src/TreeTweaks.cs`)
* `ObjectInformationViewModel.UpdateLoadoutsForEntry` заполняет `AvailableLoadouts` для любого юнита (базовая запись — все
  loadout'ы ini, loadout-юнит — только свой; выбран: начальный → PredefinedLoadoutVariant → Default (не у самолётов) → первый;
  у самолётов при > 1 ещё «None» в начале). Видимость селектора — `HasMultipleLoadouts`: > 1 И `MuseumAircraftEntry`.
* Мод: постфикс `get_HasMultipleLoadouts` → true при `AvailableLoadouts.Count > 1` (не зависит от ImprovedUnitReferenceUI).
  Loadout-юнит (и его корабли) получил бы только свой loadout: prefix `UpdateLoadoutsForEntry` на время вызова снимает у записи
  `IsLoadoutUnit` (finalizer возвращает) — в списке все loadout'ы юнита, выбран свой (PredefinedLoadoutVariant).
* Рядом с селектором — кнопка «Replace» (`TestCommand`: юниту, показанному в сцене, — выбранный loadout; самолёты, корабли,
  ПЛ, наземные). Смысл только в режиме игры «Энциклопедия» (`IsTestMode` = `Globals._inMuseum`: миссия EncyclopediaMission
  из главного меню). Мод (при ShowLoadoutList, каждый кадр, Step) вне энциклопедии её прячет: Button с Command, привязанной
  к …TestCommand, вверх — Panel, где кроме неё только оформление (фигуры, пустые Border/Panel), и Border над ней — подложка;
  `SetCurrentValue(Visibility, Collapsed)` (привязка игры остаётся); в энциклопедии — UpdateTarget / прежнее значение.
* Прозрачная шапка / закрепление кнопки Replace при узком окне (1.5.3) пробовали (HeaderStep: фон шапки, фон кнопки, колонка
  с кнопкой, TranslateTransform) — не сработало, код откачен. Кнопка Replace — Button со стилем темы (Border с
  TemplateBinding Background). Вернуться позже.
* Loadout-юнит (`CreateLoadoutUnit`): у записи `_loadouts` — только свой, таблицы LoadoutSensorsToRemove /
  LoadoutWeaponSystemEntries / LoadoutPodAmmunition — только для него, при смене loadout в списке сенсоры прошлого варианта
  оставались на «Sensors & EW». 1.5.3: в prefix `UpdateLoadoutsForEntry` — `CompleteLoadoutData`: таблицы дополняются из всех
  `MuseumObjectEntries` с тем же IniRef (раз на запись).
  SelectedLoadout обновляет оружие, сенсоры, дальность, стоимость и т. д. Так же в 0.8.4.

## Armed only (при ImprovedUnitReferenceUI, состояние — ArmedOnly, 1.4.0, `src/ArmedFilter.cs`)
* Вооружён = в `MuseumObjectEntry.WeaponEntries` (боеприпасы всех loadout'ов и систем оружия — то, что на вкладке Weapons)
  есть тип `Ammunition.Type` кроме Noisemaker, Chaff, None, Sonobuoy, Fueltank, Paratrooper, MOSS. Не юниты (оружие,
  сенсоры) не трогаются. Сравнение на vessels (255): все 41 «civ_» без оружия; без оружия и не «civ_» — 15 (плоты, капсула,
  мишени QST-35, test, sea_mine, AGI Okean / Sampan, танкеры Чиликин / Казбек, Algol, Mercur decoy) — тоже скрываются.
  Биология в справочнике и так скрыта (`IsHiddenInEncyclopedia`). Самолёты без подвесок (ДРЛО, заправщики, транспорт) скрываются.
* Фильтр: постфикс `UnitTreeBrowserViewModel.UpdateFilteredMuseumEntries` (только браузер "Encyclopedia", не во время
  `_isInitializing`) — после страны / даты / текста, тем же способом: новые коллекции `FilteredMuseumEntries` у папок
  (пустые убираются), у браузера — новый корень (сеттер уведомляет TreeItems). Лист юнита с кораблями решается целиком.
* Галочка: каждый кадр (Step) для окна Unit Reference, пока не найдена (до 120 кадров): CheckBox игры с DataContext = браузер
  и IsChecked, привязанным к FilterByDate / SearchByNameOnly; новая «Armed only» — после последней из них в том же
  родителе (Panel — Insert; Grid — новая Auto-колонка/строка по направлению, в котором идут галочки игры), стиль, поля, шрифт
  — с неё. Click → [UnitReference] ArmedOnly, SaveExpansionState (если не поиск) и UpdateFilteredMuseumEntries.
* Отдельного переключателя нет: галочка и фильтр — при Layout Improvements; строка «Armed only» в «Kept by the mod». Всё есть и в 0.8.4.

## Вкладка Reference в Unit Status (UnitReferenceTab, 1.5.0, `src/StatusReference.cs`)
* Игра: `UnitStatusViewModel.Tabs` — `UnitStatusTabViewModel(owner, UnitStatusTab kind, header)` с Header / IsAvailable /
  IsSelected (сеттер → `OnTabSelected` → `EnsureContent`) / Content / ContentError. `SwitchUnit` (смена выбранного юнита:
  `DisposeOwnedContent`, доступность вкладок, при недоступной выбранной — `SelectFirstAvailableTab`); `Refresh` (каждое
  обновление окна) → `EnsureContent(выбранная)`; контент создаётся лениво, при ошибке — ContentError. Так же в 0.8.4.
* Мод: postfix конструктора — вкладка kind = 5 (за пределами enum), заголовок «Reference», последней; postfix `SwitchUnit` —
  IsAvailable = UnitReferenceTab и у юнита есть `_obp` и он не WeaponBase (иначе, если выбрана, — SelectFirstAvailableTab);
  postfix `EnsureContent` — для нашей вкладки без контента: `new ObjectInformationViewModel(MainGameView.Instance,
  getObjectIniName(), GetReferenceVariantIndex(unit), showTree: false, GetReferenceLoadoutVariant(unit))` (как «Information»
  контекстного меню) в новом `ObjectInformationView` (DataContext) — Content вкладки (UIElement: шаблон не нужен);
  prefix `DisposeOwnedContent` — запомнить `SelectedTabIndex` (внутренняя вкладка переносится на следующий юнит,
  затем `EnsureSelectedTabAvailable`), Content = null, Dispose модели.
* Показ: Content новой вкладки игра НЕ отображает. UnitStatusView (по дампу дерева из trace, 0.8.5): строка вкладок —
  ItemsControl {Tabs} → WrapPanel → RadioButton {Header}/{IsAvailable} на вкладку (наша там появляется сама); ниже Border →
  Grid → Grid {HasUnit}, в нём на каждую вкладку Grid с Visibility {EngagementTab.IsSelected} / {FlightDeckTab.IsSelected} /
  {DamageControlTab.IsSelected} / {SensorsTab.IsSelected} / {SeekerTab.IsSelected} и видом внутри (EngagementBoardView,
  FlightDeckContentView, DamageControlView, SensorInfoContentView, SeekerStatusView), все в одной ячейке.
  Мод (каждый кадр, Step, окно с UnitStatusView): первый элемент с Visibility, привязанной к «…Tab.IsSelected», его Panel —
  туда свой Border (та же ячейка, Margin), Child = наш вид, виден, пока вкладка Reference выбрана. Если вид уже в
  визуальном дереве (игра показала сама) — ничего не добавляется. Запасной путь — ContentControl/ContentPresenter с Content
  вкладки игры; не найдено — дамп дерева UnitStatusView в trace (один раз).
* Replace в этой вкладке — тем же `ReplaceStep` (каждый кадр по живым видам вкладки), вне энциклопедии скрыт.

## Вкладка Weapons — теперь отдельный мод Alternative Weapons Control
Вкладка Weapons (бывшая 1.6.0 в разработке) вынесена в мод Alternative Weapons Control (репозиторий
rgreat/SeaPower.AlternativeWeaponsControl). Его вкладка — kind 6, наша Reference — kind 5; моды ставят хуки на одни и те же
методы UnitStatusViewModel и свои хосты рядом с вкладками игры — друг другу не мешают. Ключи [UnitStatus] WeaponsTab,
ClickVolume, GunOneDirector, GunChannelLimit тестовых сборок удаляются из .cfg (`DropRetiredEntries`).

## Окна Weapon Reference для установок (1.5.1)
* Окно Unit Reference, открытое на установке (без дерева, страница не MuseumObjectEntry — `IsSystemReference`), мод
  считает окном Weapon Reference: `Candidates` → «Weapon Reference» (шаблон, раскладка как у окон боеприпасов),
  `InfoViewModel` → null (никаких Unit Reference-доработок, без вкладки Unit Reference в настройках).

## Фоновые картинки модов ([General] ModBackgroundsOnly, 1.5.3, `src/Backgrounds.cs`)
* Игра: картинки главного меню (`MenuSystemViewModel.Awake`) и экрана загрузки (`LoadScreen.Awake`) —
  `FileManager.GetAllFiles("ui/backgrounds", "*.png")`: файлы из ВСЕХ отмеченных папок (StreamingAssets/original, моды, user)
  одним списком, показывается случайная. В модах папка `ui\backgrounds\`.
* Мод: postfix GetAllFiles (только для пути ui/backgrounds, разделители любые): если в списке есть файлы не из
  `FileManager.specialDirectories[1]` (= original), файлы original убираются. Ставится вместе с хуком экрана загрузки
  (`TryInstallLoadScreenHook`, как только загружена Seapower-Scripts): если меню успело создаться раньше, первая картинка меню
  может быть игровой. По умолчанию включено. Trace: «backgrounds: N from mods, M of the game left out».
* Экран загрузки (Singleton `LoadScreen`) создаётся раньше хука и кэширует список в поле `imagePaths` (Awake): prefix
  `LoadScreen.ChangeBackground` чистит это поле тем же правилом (`DropGamePictures`). Подпись в окне настроек — «Mod backgrounds only».

## Окно настроек: зависимые строки
* `Field.Needs` — строка видна, только пока включён этот bool (по значению в окне, с учётом неприменённого). Смена такого
  переключателя (`Set`) перестраивает вкладку. Сейчас: от «Layout Improvements» (ImprovedUnitReferenceUI) зависят
  «Remember search panel» и группа «Kept by the mod»; Optimize the tree / Scroll to the selected unit / Loadout list — нет.

## Хуки
LoadScreen `<ExecuteLoadScreen>.MoveNext` (конец загрузки), `FileManager.SaveDirectories` (Apply в Mods),
`Window..ctor`, `OnInitialized` (prefix: открыть развёрнутым; postfix: раскладка), `ApplyScreenAnchor`,
`AbsoluteInitialPosition.AssociatedObject_Loaded`, `Kfa_Completed`, `MaximizeChanged`, `Restore_Completed`,
`CommandUnmaximizeWindow`, `HeaderDragDelta`, `ObjectInformationViewModel.set_MuseumEntriesVisible`,
`UnitTreeBrowserViewModel.set_SelectedNation` + RestoreSessionState/OnUnitsLoaded/SetupNations/FinishInitialization,
`DamageControlView..ctor` (+ `Loaded`/`IsVisibleChanged`) — SectionsMinHeight (ниже), UnitStatusViewModel — вкладка Reference.
Событие `Loaded` каждого окна — настройка до первого кадра (без мерцания).

## Минимум панелей секций Damage Control ([UnitStatus] SectionsMinHeight, 1.3.0)
* Вкладка (XAML: `SeaPower.AlternativeDamageModel/ref/xaml/DamageControlView.xaml`): корневой Grid MinHeight=330 (единицы UI),
  строки: 0 карточки-сводки (Auto), 1 полоса DC/Hull/Flooding (Auto, Margin −30 сверху), 2 панели секций (*), 3 «Auto Damage
  Control Status» (Auto). Своего минимума у панелей секций нет: им достаётся 330 − остальные строки (≈150–160 ед.).
* Значение — пиксели экрана, 0 = как в игре. `SetupSections`: корень = ближайший Grid с MinHeight (в ширину, глубина ≤ 5, не в
  списки), элемент строки 2 = панели; замер «игрового» минимума → `SectionsGamePx` («game: ≈ N px» в форме). `ApplySections`:
  > 0 — у корня MinHeight 0, у RowDefinition строки 2 MinHeight = px / масштаб + поля (MinHeight самого элемента в строке «*»
  Grid не учитывает); масштаб — `ScreenScale` (PointToScreen; иначе UIScale × Screen.height / 1080); 0 — минимумы игры обратно.
  Вкладки создаются свёрнутыми и строят содержимое при показе — правка на первом показе.

## Настройки (.cfg)
[General] WindowPatterns, ModBackgroundsOnly (1.5.3), CascadeOffset, BringToFrontOnClick, AllowDragOutside, MinVisiblePixels, RememberOpenWindows,
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

## Рамка окон ([WindowBorder]: Enabled, Thickness 0,5–10, Color #RRGGBB/#AARRGGBB, по умолчанию #A0808080)
Проверена в игре. В `Loaded` у окон с `Draggable = true`
(`AddBorder`): Border из ParseXaml (IsHitTestVisible=False, Panel.ZIndex=100000) последним ребёнком в корневую панель шаблона
(или первую панель ниже через одиночные обёртки), в Grid — на все строки/столбцы. Корневая панель шире видимого окна (зона
ручек ресайза) → `BodyInsets`: крупнейший потомок (глубина ≤ 6) с непрозрачным Background, ≥ 60% ширины и ≥ 30% высоты;
его отступы → Margin рамки. Верхней стороны нет (BorderThickness = t,0,t,t).
Старый формат [OpenWindows] `state,ТипVM` читается (берётся первое поле).

## Рамка подсказок ([WindowBorder] ToolTips, 1.5.2, `src/TooltipFrame.cs`)
* Class handler `EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent)` (есть и в Noesis 0.8.4) — все
  подсказки UI, и модов тоже. Шаблон подсказки игры (`NoesisTheme.Styles.xaml`, Template.ToolTip) — Border с Background и
  Padding (без рамки, LayoutTransform масштаба UI): ему ставятся BorderBrush / BorderThickness (со всех 4 сторон, ширина и цвет
  из [WindowBorder]). Шаблон с собственной рамкой не трогается; корень-Panel — поверх кладётся свой Border. Применяется при
  каждом открытии подсказки (смена настроек — со следующей); свои Border помечены (`TipOwn`), выключение снимает рамку.

## Порядок окон и Weapon Reference (проверено в игре)
* BringToFrontOnClick: PreviewMouseDown / Loaded окна → вверх от окна до первой панели, где в другом ребёнке есть ещё окно;
  только если все дети панели — окна. Состояние (уровень = ZIndex элемента при первом обнаружении, отметка клика) хранится
  ПО ОКНУ: Noesis-обёртки детей панели (ContentPresenter, Grid) при каждом обходе новые — по ним хранить нельзя. ZIndex окон — ОТРИЦАТЕЛЬНЫЙ, вниз от уровня
  игры: переднее окно уровня — уровень × 1000 − 1, за ним − 2, − 3… (как в рабочей сборке 1.2.0 со своим исправлением, потерянным в истории; восстановлено в 1.5.2). Всё, что игра ставит в ту же панель со своим
  ZIndex 0 — брифинг миссии и скриптовые сообщения (`SeapowerUI.Modal` / `RichMissionMessageModal` через WindowHost), только
  что открытые окна, — всегда выше наших окон, без знания их типов. Не-окна панели не трогаются (раньше из-за них мод весь
  порядок оставлял игре: trace «shares … with RichMissionMessageModal»). Панель — первая панель-предок, которая `IsItemsHost`
  (Grid списка окон над WindowHost.Windows; так и с одним окном), иначе первая, где в другом ребёнке есть окно.
  Раньше было «уровень × 1000 + ранг» (≥ 0): окна с рангом > 0 закрывали сообщения игры. Trace при Debug:
  «z-order (почему) in панель: окна сзади вперёд с ZIndex (уровень игры)».
  Развёрнутые Tactical Display / World View — ТОЛЬКО эти два окна (`IsMapWindow`: заголовок целиком TacticalDisplay /
  WorldView, либо собственный — не унаследованный от родителя — DataContext SceneMapViewModel / WorldViewWindowViewModel) всегда ниже всех окон панели (ZIndex = минимальный уровень × 1000 − n), клик по ним порядок не
  меняет; пересчёт и в `MaximizeChanged` (разворот/сворачивание).
* Weapon Reference: шаблон `Weapon Reference*` по заголовку; VM — `ObjectWeaponInformationViewModel` (наследник ObjectInformationViewModel,
  поэтому функции Unit Reference к нему не применяются: `InfoViewModel` сравнивает точное имя типа). Каскад не проверен.

## Окно настроек (1.3.0)
Стиль и устройство — как окно настроек Alternative Damage Model (`SeaPower.AlternativeDamageModel/src/SettingsWindow.cs`):
для него в csproj есть ссылка на Noesis.NoesisGUI, UI строится кодом Noesis (типы игры по-прежнему только через рефлексию).
* `src/SettingsUI.cs` (partial): шестерёнка (`EnsureGear`) — как у ADM (серая 14×14, `GearGrey` 170,176,184, Border-рамка
  Opacity 0.9 → 1 под мышью, Cursor Hand, ToolTip), клик — PreviewMouseLeftButtonDown (Handled) → `SettingsWindow.Open`.
  Кладётся в корневую панель шаблона окна (`TemplateRootPanel`, как рамка), Right/Top, ZIndex 100001, на все строки/столбцы.
  Только у окон из WindowPatterns и при SettingsButton = true; ставится в `Loaded` окна и при опознании в Step. Место: по
  `HeaderThumb` (TranslatePoint к панели) — по вертикали центр заголовка, справа — левее кнопок заголовка (видимые ButtonBase
  на линии заголовка правее 30% его ширины; поиск не заходит в элементы ниже заголовка), +6 px. Trace `gear added to ...` —
  размеры заголовка и найденные кнопки: по нему подгонять. Там же применение изменений (`AfterChange`), `ReloadPatterns`,
  `ReloadOpenPatterns`, `RefreshGears`, `RefreshBorders`, `ResetLayouts`, `SetTyping` (InputHandler.typingActive/typingElement).
* `src/SettingsWindow.cs` (вложенный static class): своё тёмное окно (не SeapowerUI.Window) в самой верхней Grid/Canvas над
  шестерёнкой (не ItemsHost), ZIndex 30000, по центру экрана; размер 900×1140 пикселей экрана (не больше экрана − 40) (k = PointToScreen); размер
  меняется за любой край (полосы 5 px) и угол (16 px; справа внизу — штриховка), 520–1600 × 420–1400, при левом/верхнем крае
  противоположная сторона стоит на месте (`ResizeHandle`, как в ADM); размер держится до выхода из игры; перетаскивание за заголовок (TranslateTransform), ✕ и Close.
  ✕ — нарисованный `Cross(size)` (Path, по центру кнопки, Padding 0): символ «✕» шрифта игры обрезается снизу и не по центру.
  Вкладки General / Remembered Windows / Frame / Unit Status / Unit Reference; Unit Reference — только если открыто шестерёнкой
  окна Unit Reference (`InfoViewModel` ≠ null; Weapon Reference — нет), Unit Status — только из окна Unit Status
  (кнопка «Show all settings» слева в нижней панели, 1.5.0 — показывает и их, повторно — «Show fewer settings»; видна, только
  если что-то скрыто; сбрасывается при закрытии окна).
  (`IsUnitStatusWindow`: строка опознания 'UnitStatus*'); открыто оттуда — сразу на этой вкладке; уже открытое окно при клике
  по шестерёнке другого окна только показывает/прячет эти вкладки. Строка: подпись с жёлтой точкой
  (изменено в окне и ещё не применено), редактор (−/TextBox/+ с шагом и пределами; CheckBox; TextBox для клавиши/цвета (+ образец)/
  текста), «default: …», ↺ (поставить default — тоже до применения). Списки (WindowPatterns, RememberOpenWindows) — `ListRow`:
  строка на окно: имя (TextBox; Enter/уход — принять, пусто — удалить, повтор — откат), справа что хранит мод (сохранённое
  положение x,y,w,h / «last left open|closed» из [OpenWindows]), ↺ — забыть сохранённое положение (на Apply, `_resets`), ✕ —
  убрать; «+ Add window» (пустая строка с фокусом), «Forget all positions»; новые/переименованные/помеченные — с жёлтой точкой.
  Правки строк — по значению имени, не по индексу (строки перестраиваются при потере фокуса). Внизу панель описания
  (Description из .cfg) фиксированной высоты 108. Работа на копии, как в ADM: `_pending` (поле → значение), точки на вкладках с
  правками; Apply — записать (`AfterChange` по каждому полю) и забыть отмеченные положения; OK — то же и закрыть; Cancel / ✕ —
  отбросить; новое открытие — новая копия. Перед Apply/OK текст поля с фокусом принимается (`_commitFocused`).
  Поля: Enter / потеря фокуса — принять, Esc — вернуть; неверное значение откатывается.
* Применение (на Apply / OK): WindowPatterns → повторное опознание окон + шестерёнки + список сохранённых положений; RememberOpenWindows →
  `ReloadOpenPatterns`; [WindowBorder] → рамки окон на экране перерисовываются; SettingsButton → шестерёнки; Debug → сразу.
  SectionsMinHeight → `ReapplyDamageControl`: у настроенных вкладок (`_dcViews`, `DcDone`: вкладка → { корень, его MinHeight, панели секций, MinHeight строки 2,
  масштаб, поля панелей }) — `ApplySections` сразу; видимая ненастроенная — настраивается сразу. RemoveSizeLimits → `RefreshSizeLimits`. Остальное читается при использовании; ImprovedUnitReferenceUI, TreeWidth —
  для окон, открытых после изменения.
* Ради этого с 1.3.0 все Harmony-хуки и события окон ставятся всегда, а тела проверяют свой переключатель
  (HeaderDragDelta, Unit Reference, страна, WindowHost.OpenNewWindow, DamageControlView, PreviewMouseDown, двойной щелчок).
* Совместимость: из игры — только `SeapowerUI.Window.HeaderThumb` и `SeaPower.InputHandler.typingActive` (есть в 0.8.4);
  Noesis 0.8.4 и 0.8.5 одинаков — для 0.8.3–0.8.4 ничего нового не нужно.

## Без ограничений размера (RemoveSizeLimits, «No size limits»; 1.3.0)
* Запоминаемые окна: MinWidth/MinHeight → 100×60, MaxWidth/MaxHeight → ∞, MinWidth/MinHeight 0 у элементов от содержимого
  до окна. Исходные значения запоминаются (`SizeLimits`: окно → { элемент, Min/Max W/H }) и
  возвращаются при выключении — переключение действует на окна на экране сразу (`RefreshSizeLimits`).
* Ресайз за край не упирается в экран: `ResizeCommand.ComputeSize` игры обрезает каждую сторону по хосту; постфикс
  `ResizeBeyondScreen` (Snap.cs, до притягивания) пересчитывает шаг от окна до шага (prefix-state) без этого: верх ≥ 0
  (заголовок доступен), минимум держит сдвинутая сторона. Только для окон из WindowPatterns.

## Подписи формы
* Серые подписи у редакторов — 1–2 слова (единица, «0 = …»); подробности — в Description настройки (панель справки внизу
  формы), там же «для окон, открытых после изменения» и т.п. Колонка редактора ≈ 45 символов, рядом с −/поле/+ меньше.

## Притягивание окон (SnapWindows, 1.3.0, `src/Snap.cs`)
* Перетаскивание за заголовок: `AfterDrag` (постфикс HeaderDragDelta; теперь и при AllowDragOutside = false, если SnapWindows)
  → `SnapMove`. Ресайз за края: `ResizeCommand.Execute(Thickness delta)` игры (prefix запоминает X,Y,ActualWidth/Height; postfix
  `SnapResize`) — притягиваются только сдвинутые стороны (delta.Left/Right/Top/Bottom ≠ 0), с учётом MinWidth/MinHeight.
* Края — видимые (`VisibleInsets`, раз на окно): «тело» рамки (BodyInsets, рамка стоит по нему верно) с поправками: слева и
  справа зона ресайза одинакова — берётся меньшая из двух (у Unit Reference «тело» — только панель информации, его левый отступ
  был шириной дерева); верх — верх HeaderThumb, если он выше тела. Отступ начала заголовка как ширина зоны НЕ годится (в
  заголовке свои поля/значок: края уходят внутрь, окно уезжает за экран). Trace: `snap: body insets`, `snap: visible insets of`, раз в секунду при
  перетаскивании `snap: '<окно>' X,Y … px per unit; screen WxH = X L..R, Y T..B; insets`.
* ВАЖНО (проверено по trace): элемент SeapowerUI.Window всегда стоит в начале своей ячейки — PointToScreen(0,0) окна = 0,0 при
  любом X; X/Y двигают корневую панель шаблона (Grid 'Container' [rows 10,*,10; cols 10,*,10]). Поэтому все замеры — по ней
  (`TemplateRootPanel`): отступы относительно неё, экранное начало окна = её PointToScreen. Пример: экран 2560×1440, 1,333 px/ед.
  → X 0..1920, низ (полоса HUD 1397 px) ≈ 1048 ед.; отступы Tactical Display 10,10,10,10 (тело 30 сверху — под заголовком).
* Замер (`Measure`): сначала `UpdateLayout()` окна, потом X/Y и PointToScreen — за кадр может прийти несколько шагов drag, и
  макет на шаг позади сдвигает все цели на этот шаг (окно дёргается). Другие окна (на экране, не развёрнутые,
  Opacity ≥ 0,05, того же поколения, не предки/потомки, хоть частью на экране) меряются PointToScreen и переводятся в X/Y
  двигаемого окна.
* Края экрана — в пикселях экрана (UnityEngine.Screen), а не GetHostSize (тот давал правый край ~10 px за экраном). Низ —
  верх нижней полосы HUD миссии (`BottomLimit`, кэш 3 с): от кнопки окна HUD (MainGameView.tb_UnitStatusWindow / tb_MapWindow /
  tb_EventLogWindow / ...) вверх до первого элемента шириной ≥ 50% экрана, доходящего до низа экрана (верх > 60% высоты);
  нет такого, но кнопка в нижней пятой экрана — верх кнопки; нет HUD — низ экрана. Trace `snap: bottom edge N of H px`.
* Линии (`EdgeLines` для каждой стороны): края экрана; касание — противоположная сторона окна рядом (мой левый к его правому),
  если перекрываются вдоль стороны (с запасом SnapDistance); уровень — та же сторона окна, к которому окно прилегает ВПЛОТНУЮ
  по другой оси (мой левый к левому окна прямо над/под ним, мой верх к верху окна прямо сбоку; вплотную = видимые края ближе
  1,5 ед.). По просто близким окнам уровень не выравнивается (решение пользователя). Перемещение — два прохода от позиции мыши:
  X, Y, затем X и Y снова с учётом того, к чему окно прилегло. Берётся ближайшая линия в пределах SnapDistance (1–50, по умолчанию 10). Шаги игры идут относительно Thumb, который едет с окном, — притянутый край стоит,
  пока мышь не уйдёт дальше SnapDistance.
* Ресайз (1.5.3): правый/нижний край — X + ширина ПАНЕЛИ шаблона − отступ (раньше Width/Height окна: панель больше на зону
  ресайза → края на ~28 px дальше вправо/вниз).

## Развёрнутое окно в пределах экрана (1.5.3, `src/MaxFit.cs`)
* Развёрнутое (Maximized, после Kfa_Completed: Stretch, Width NaN) окно растягивается на свою ячейку UI, она может быть
  больше экрана. Каждые 0,25 с для развёрнутых окон: видимое тело (панель шаблона − VisibleInsets) через PointToScreen
  сравнивается с UnityEngine.Screen; вылет по сторонам (в ед. UI, ScreenScale) добавляется к Margin окна (не меньше исходного).
  Исходный Margin возвращается при сворачивании. Без отдельного ключа (часть разворота окон). Trace `Maximized window fitted`.

## Unit Reference: панель поиска (RememberSearchPanel, 1.3.0)
* Постфиксы сеттеров `UnitTreeBrowserViewModel` SelectedCategory / FilterByDate / SearchByNameOnly / SearchText
  (`SearchSetPostfix`, имя свойства из `__originalMethod`) → `SaveSearch` (только браузер "Encyclopedia", не при
  `_isInitializing` / `_nationBusy` / нашем восстановлении; SearchText — отложенная запись, остальное сразу).
* Восстановление — `RestoreCountry` = `RestoreNation` + `RestoreSearch` (там же, где страна: после методов инициализации
  браузера): категория по DisplayName из `RootCategories`, затем флажки, затем текст; ставится только отличающееся.
  Работает при ImprovedUnitReferenceUI и RememberSearchPanel. Всё есть и в 0.8.4.

## Окна, открываемые в начале миссии (форма)
* В списке RememberOpenWindows у каждого окна ComboBox open / closed (состояние из [OpenWindows]); правка — в
  `_osStates`, пишется на Apply после самого списка (у нового окна запись появляется при применении списка).

## Не подтверждено в игре / открытые вопросы
* Открытие Unit Reference развёрнутым (последняя правка: `,max` снимается только сворачиванием пользователя).

## Версия
* 13.10.2026: 1.3.0 проверена в игре и зафиксирована как стабильная (коммит «Smart Windows 1.3.0»).
* 13.10.2026: 1.4.0 закоммичена («Smart Windows 1.4.0»): дерево Unit Reference, прокрутка, список loadout'ов, Armed only.
* 13.10.2026: 1.5.0 закоммичена («Smart Windows 1.5.0»): вкладка Reference в Unit Status, «Show all settings».
* 14.10.2026: 1.5.1 закоммичена («Smart Windows 1.5.1»): окна установок как Weapon Reference; вкладка Weapons вынесена в Alternative Weapons Control.
* 28.10.2026: 1.7.0 — WindowedMapAndWorldView (рендер World View в текстуру окна), look-меню и уровни окон, ресайз докированных окон, перетаскиваемая панель времени, лента курса в World View, фото-режим, Track State, апгрейд кораблей в кампании.
* Версия: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе; и строку «Version N» в
  начале README.BBCode.txt / README.md (описание Steam). Список изменений к выкладке — одним списком, без разбивки по версиям.

## UnitMenu.cs, GameFixes.cs, CampaignLoadouts.cs (1.6.0)
- UnitMenu (`[Mission] UnitPanelMenu`): каждые 2.5 с ищем в MainGameView флаг (Image, Source ← `ObjectNation.Value`) и имя (TextBlock, Text ← `ObjectName.Value`) панели юнита; на них вешаем MouseRightButtonDown, который открывает `MainGameView.Instance.ContextMenuCanvas.ContextMenu` с `ViewModel.GetContextMenuItems()` выбранного юнита.
- GameFixes (`[General] FixContactInfo`): `BaseVehicleViewModel.UpdateFrom` штампует обнаружения `Epoch.ToLive()` (missionSessionTime), а старит/удаляет по `missionElapsedTime` — после загрузки сейва часы расходятся, поля сенсора/возраста пустые. Transpiler подменяет вызов getter на `ContactClock()`.
- MaxFit: для окон в Canvas (Task Force Builder кампании) Width/Height = Screen/ScreenScale, Max* = ∞ (stretch в Canvas даёт размер контента).
- CampaignLoadouts (`[Campaign] StrictLoadouts`, по умолчанию off): постфикс `LinearCampaignTaskForceLoadoutsViewModel.GetFamilyVersions` (версии корабля по общему `_encyclopediaTag`) оставляет только типы из `_missionDefaultRestriction` билдера (захват через постфикс `LoadCampaignTaskForceData`) и уже имеющиеся (`ReadCurrentTaskForceUnitKeys`, ключ `Type:Variant`), плюс сам корабль. Если ограничения миссии нет (AllowAll) — список игры. Это опция в настройках, а не в диалоге старта кампании. Не проверено в игре.
- StrictLoadouts, дополнение: постфикс `MergeRelatedClassGroups` (вкладка Builder) убирает варианты, не разрешённые `_missionDefaultRestriction.Allows(type, variant)` и не `IsCurrentTaskForceUnit`; в Unrestricted/debug-режиме (`AllowAllRosterUnits`) не действует. Галка «Strict loadouts» добавляется на экран старта кампании (CampaignMenuView.Instance, рядом с чекбоксом `TaskForceLimitUnitDates`) — зеркало конфига.
- UnitMenu: меню не открывается, если DataContext элемента — Contact*/Weapon* ViewModel (панель показывает контакт, а выделен свой юнит).
- AlertsFix.cs (`[General] AlertsClickThrough`, по умолчанию on): сообщения игры справа внизу (`ItemsControl` над `MainGameViewModel.TopAlerts`) остаются в списке после затухания (`AlertTextOpacity` → 0), а hit-test Noesis игнорирует Opacity → гасшие Alert глотали клики (кнопки Fire в углу окон). Раз в 0,2 с для каждого Alert `IsHitTestVisible = AlertTextOpacity > 0.05`; сам список остаётся hit-testable. ItemsControl ищем по ссылке `ItemsSource == DataContext.TopAlerts` (скан раз в 3 с). Источник: заметка из AWC (SmartWindows_alerts_clickthrough.md); после выхода — убрать код и опцию из AWC.
- Правки 1.6.0 (поздние): StrictLoadouts теперь per-campaign. Значение — ключ `SmartWindows_StrictLoadouts` в `LinearCampaign.PersistentData` (сохраняется с кампанией, по умолчанию False). Галка на экране старта (`_strictStaged`) пишется postfix'ом `LinearCampaignTaskForceDifficultyRules.SeedPersistentDataForNewCampaign`. Строка в окне настроек (вкладка Game Menus, группа Campaign (this game)) — прокси-ConfigEntry в несохраняемом ConfigFile; видна только если окно открыто с шестерёнки Task Force Builder (`Field.TfbOnly`, `SettingsWindow._tfb`), при открытии подгружает значение кампании, Apply пишет его обратно в PersistentData. Опции AlertsClickThrough в cfg больше нет (всегда включено). Группа «Kept by the mod» (Unit Reference) убрана из окна — значения остаются только в cfg. FullUnitName: игра ставит `UnitName` только в SwitchUnit, а Replace (энциклопедия) меняет юнит под тем же окном — postfix `ObjectInformationViewModel.ChangeVariant` (последний шаг Replace) вызывает `SyncFullNames` по списку живых UnitStatusViewModel (без таймеров).

## Локализация (Loc.cs, Release/lang)
- Ядро `Loc.cs` — из руководства (как в AWC): `Loc.T/F/Or/Register`, файлы `lang/<код>.ini` рядом с dll, язык игры или `[General] Language`. Тексты окна настроек не в таблице D, а регистрируются: `SettingsWindow.RegisterTexts` (вызывается в Awake после всех Bind) кладёт `tab.<вкладка>`, `grp.<группа>`, `lbl.<Секция>/<Ключ>`, `unit.…`, `help.…` (описание .cfg) для каждого поля окна. Описания скрытых записей (Category, TreeWidth …) не переводятся — их нет в окне.
- Вкладки, группы и подписи в `Field` остаются английскими идентификаторами (сравнения `Tab == TRef`); показ — через `TabOf/GroupOf/LabelOf/UnitOf/HelpOf`.
- Настройка `[General] Language` — поле вида 'l' (ComboBox: язык игры + `Loc.GameLanguages()`). После Apply со сменой языка окно перестраивается на следующем LayoutUpdated (`RebuildAfterLayout`, не внутри клика).
- Релизные файлы `Release/lang/*.ini` (9 языков) генерируются скриптом из исходников: ключи берутся из `D(...)` в Loc.cs, из `Add(...)` в SettingsWindow.cs и описаний `Bind`; переводы — позиционные файлы. При добавлении текста/настройки: добавить ключ в таблицу или поле, перегенерировать и перевести файлы (иначе игра допишет английскую строку в конец файла под `; --- new ---`). Скрипты лежали вне репозитория (/home/claude/loc).
- Не проверено в игре: шрифты игры могут не содержать CJK/вьетнамские глифы.

## Файл настроек в user (1.6.0)
- `SmartWindowsPlugin.Config` скрыт полем `new ConfigFile Config` (OpenSettingsFile в начале Awake): `StreamingAssets/user/SmartWindows/SmartWindows.cfg`, как AWC. Если файла нет, а BepInEx/config/<guid>.cfg есть — копия один раз (старый не трогаем). Ошибка папки — откат на BepInEx/config. Весь прежний код (`Config.Bind/Save`, `P.Config`) работает без правок, т.к. обращения идут через тип плагина.
- Умолчания взяты из cfg автора: WindowPatterns = `News,Tactical Display,World View,Unit Reference*,Unit Status*,Event Log,Formation Manager,Task Force Builder,Weapon Reference`, SectionsMinHeight = 278. Размеры/раскладки окон ([Layouts], TreeWidth), состояние панели поиска и язык в умолчания не вошли. Колонка «default» у SectionsMinHeight показывает 278 и, второй строкой, высоту игры.

## Производительность (1.6.0, аудит Update и хуков)
* Update() каждый кадр обходит все отслеживаемые окна. Что было дорого: `Prop()` склеивал строку на каждый рефлексивный `Get` (мусор → паузы GC),
  `VtParent/VtChild` — `MethodInfo.Invoke` с новым массивом, `InfoViewModel(w)` (≈8 `Get`) вызывался 4 раза за кадр на каждое окно,
  `LinkHelpStep` обходил всё визуальное дерево КАЖДОГО окна каждые 0.6 с, `StatusRefStep` до 120 кадров подряд искал UnitStatusView полным обходом.
* Сделано: кэш `Prop` по (Type, имя) без конкатенации; `VtParent/VtChild/VtChildrenCount` напрямую через Noesis.VisualTreeHelper;
  `GameAssembly()` кэшируется; помощники Unit Reference — раз в 0.12 с и только для окон с ObjectInformationViewModel (vm ищется один раз и
  передаётся); опрос позиции окна — раз в 0.1 с; LinkHelp — серия из 6 обходов по 0.3 с после смены выбранного юнита/loadout, потом раз в 3 с;
  поиск UnitStatusView — раз в 0.25 с, до 40 попыток; панель сообщений (Alerts) кэшируется; `ContactClock` (вызывается из
  BaseVehicleViewModel.UpdateFrom на каждый контакт) — делегаты (DynamicMethod/CreateDelegate) вместо FieldInfo.GetValue/MethodInfo.Invoke;
  `MatchObjectPostfix` — кэш lower-case id и описания на запись.
* Повторные обходы дерева (аудит 2): поиск TreeView для защиты колеса — раз в 1 с, пока не найден; поиск панели Armed only и кнопки Replace — раз в 0.4 с,
  до 40 попыток; строки файлового пикера — раз в 0.1 с; все обходы ограничены бюджетом узлов и числом попыток. Оставшиеся обходы — по событиям
  (переключение дерева, создание окна, смена выбранного юнита).
* Постоянных таймеров нет; единственные хуки на частые методы — ContactClock (теперь без рефлексии) и MatchesTextSearch (только при вводе поиска).

## Сообщения в правом нижнем углу (1.6.0, `src/AlertsFix.cs`, без опции — всегда включено)
* Погасшие сообщения (Opacity=0 → 0 за 6 с) остаются в hit-test и перехватывают клики (трасса показала попадание в `Run` текста). Фона у общей панели нет.
* Решение: разово при Loaded списка (ViewCapture → `CaptureAlerts`, ItemsControl с `ItemsSource={Binding TopAlerts}`) `IsHitTestVisible=false`. Сообщения только для чтения;
  ни опроса, ни подписок, ни обходов дерева.
* Не помогло (не возвращаться): опрос с коллапсом строк, подписки на PropertyChanged Alert, снятие Background у общей панели.

## Захват элементов вместо обходов (1.6.0, `src/ViewCapture.cs`)
* Классовые обработчики `FrameworkElement.LoadedEvent` (ToggleButton, Button, TreeView, UserControl) и `UIElement.MouseEnterEvent` (Button) регистрируются в `HookCapture()`
  (после первого load screen, рядом с HookToolTips). По ним мод запоминает нужные элементы (слабые ссылки, ключ — view model / окно), без обхода дерева:
  TreeView дерева (ключ UnitTreeBrowserViewModel → `TreeViewOf`), чекбоксы Filter by date / Search by name only и кнопка Replace (ключ ObjectInformationViewModel,
  `PartsByVm`), UnitStatusView и ObjectInformationView (ключ окно), чекбокс «Limit unit dates» стартового экрана кампании (рядом сразу добавляется Strict loadouts),
  MainGameView (правый клик по панели юнита). Подсказки ссылок — при Loaded и MouseEnter кнопки (TipButton). Удалены: FindPanelBoxes, FindReplaceHolder, FindTypeNamed,
  FindByType, LinkHelpStep, CampaignBoxStep, UnitMenuStep, перебор в FindTreeView.
* Остались обходы по событию (разово, не в цикле): расстановка Reference-хоста (MakeRefHost), FindFilterAnchor / ContainsCommand при показе-скрытии дерева, строки файлового
  пикера редактора миссий (раз в 0.1 с, пока пикер открыт), запасной поиск ScrollViewer при прокрутке дерева, EnsureGear.
* Не проверено в игре: что Loaded доходит до классового обработчика у элементов шаблонов Noesis и что к этому моменту уже вычислены привязки (BindingPath).

## Публикации
* 1.6.0 опубликована в Steam (22.10.2026; релизная DLL — сборка 20261021.0256, со старым опросом сообщений). Список изменений 1.6.0 дан пользователем.
* 1.6.1 (после релиза, по разнице строк DLL): события вместо обходов (ViewCapture), IsHitTestVisible=false для списка сообщений, кэш SearchTexts, ContactClock через делегаты.

## Sea Power 0.8.7 (beta 261026) — совместимость с 0.8.6 сохраняется (1.6.2)
* Загрузку модов ломал не мод, а Anchor Chain (нужен AnchorChain.Preloader бета-форк 1.0.2 или новее). `_info.ini` ApproximateVersion остаётся 0.8.6: правило игры — тот же major.minor и патч игры не меньше, т.е. 0.8.7 проходит, а 0.8.6 тоже.
* Проверка: `refcheck` (в scratchpad) сверяет все member-ссылки DLL с Managed обеих версий — 0 расхождений; остаётся только рефлексия по имени.
* Backgrounds.cs: `FileManager.GetAllFiles(path, pattern)` в 0.8.7 получил третий параметр `LayerKind kinds = All` → метод ищется по имени с двумя string в начале (любая перегрузка).
* FixContactInfo: в 0.8.7 игра сама использует missionSessionTime, transpiler заменяет 0 вызовов — безвреден, на 0.8.6 нужен.
* Не проверено в игре: Strict loadouts в кампании с новыми правилами получения юнитов (PointsBased / HistoricalReinforcements), `UnitTreeBrowserViewModel.PreserveNodeSelection` (новое в игре) и «KeepOpenedUnit».


## Strict loadouts в Builder: закрытые лоадауты (1.6.2)
* В Builder у корабля отдельная позиция на КАЖДЫЙ лоадаут (`LoadRosterSection`: variants × `GetLoadoutRosterEntries`, `FixLoadout`). Закрытость лоадаута задаёт файл кампании: `LockedShipLoadoutVariants` (закрытые; `BasicShipLoadoutVariants` по умолчанию `Default|Early` — открыты всегда), открываются миссиями (`TaskForceModeLoadoutsToUnlock`) и персистентными данными — `ModeRules.IsShipLoadoutTypeRewardUnlocked` → `LoadoutOption.IsTypeUnlocked` / `IsLocked`. («Level N» — имена лоадаутов в моде Task Force Grom, в базовой игре Early / Late.) Проблема Strict — случаи, когда несколько разных Vessel объединены в одну логическую группу общим `EncyclopediaTag`: `MergeRelatedClassGroups` склеивает их позиции, и в группе оказываются позиции с лоадаутами, не открытыми кампанией, и юниты, которых миссия не даёт.
* `MergedGroupsPostfix` (патч `MergeRelatedClassGroups`) теперь убирает позиции с `HasFixedLoadout` и `LoadoutOptions[SelectedLoadoutVariant].IsLocked` (`IsTypeUnlocked` = `IsShipLoadoutTypeRewardUnlocked`); фильтр по ростеру миссии (`Allows`) работает как раньше и применяется независимо. Купленные (`IsCurrentTaskForceUnit`) не трогаем. Те же API есть в 0.8.6 и 0.8.7.
* Билдер запоминаем в prefix `LoadCampaignTaskForceData` (иначе первое построение списка шло без фильтра). Трейс: `strict loadouts: builder list N … left out, M … kept in K group(s)` / `not filtered (причина)`.
* Случай «Default»: у кораблей, чьи лоадауты игра не знала при построении каталога (`wp_bpk_kashin_mod`, `wp_skr_krivak2` в Grom), позиция получает fixed-лоадаут `Default`, которого у корабля нет (опции Level 4..6 / Level 2,4..6, все закрыты) — поэтому нет «[Level N]» и списка оружия. `LoadoutLocked`: нет подходящей опции и все опции корабля закрыты → позиция скрывается.
* Не проверено в игре.

## WindowedMapAndWorldView (1.7.0, `src/MapWindows.cs`, `src/PipScene.cs`, `src/CompassTape.cs`; [General] WindowedMapAndWorldView, ранее MapWindowsNotMaximized)
Что делает игра (XAML GameMapView / WorldViewWindowView / MainGameView):
* Окно Tactical Display: `Maximized="{Binding IsFullscreen, TwoWay}"` — окно и `SceneMapViewModel.IsFullscreen` это одно состояние. Окно World View: `ShowPip3D = MapVisible && Map.IsFullscreen && PipOpen && !WorldViewDetached`; кнопка X окна — `ClosePipCommand` (PipOpen=false); пункт «World View» в меню окна карты (`WorldViewOpen` ↔ PipOpen) включён только при `IsFullscreen` (`IsEnabled` привязан к нему). Tab = `ToggleMapFullscreen`. Окно карты открывается/закрывается кнопкой HUD, клавишей (`ToggleMapOpen`) и X (`MapVisible`). `SyncWorldViewWindow` и `UpdateWorldRenderingPause` вызываются из Update каждый кадр.
* Позиция окна: X/Y — `TranslateTransform` на корне шаблона (`VtChild(w,0)`), сам Window стоит в начале хоста. Для Clip / PointToScreen нужен корень шаблона.
Что сделано:
* Патчи: `ShowPip3D` postfix (`!Detached && PipOpen`, без требования карты и IsFullscreen), `ToggleMapFullscreen` (Tab пропускается, вызов из InputHandler), `SetRenderingPaused` prefix (камеры рендерят только пока World View показан или фото-режим), `SetCameraRects` prefix (игра не сжимает камеру прямоугольником PiP; полный прямоугольник пропускается), `Pip3DView.OnRender` prefix (рисует нашу текстуру), `Pip3DView.RemapMouse` prefix, `MouseControlState.OnUpdate` postfix (клик в 3D только внутри картинки, иначе состояние UI), `BlitToPipImage.OnRenderImage` (в режиме — обычный Blit, без копии в `_pipImage`).
* Рендер в текстуру: `LateUpdate` (после обновления Noesis) считает экранный прямоугольник элемента `Pip3DView` (`PointToScreen`), `MapRtStep` держит RenderTexture размером окна в пикселях (смена размера — после 0.15 с покоя). Основная камера (и камера частиц, если есть) получает `targetTexture`. Суперсэмплинг PiP (`CameraManager.PipSupersampleFactor`, private static) — камера рисует в большую текстуру (лимит 16 Мпикс, 8192), `MapRtResolve.OnPostRender` сжимает её вдвое за шаг в показываемую текстуру размером окна. Лимит «не больше экрана» нативного PiP не действует. MSAA — из QualitySettings.
* Резкость: элемент стоит на дробной экранной позиции, поэтому `OnRender` ставит угол текстуры на целый пиксель (`PointToScreen` в момент отрисовки) и рисует её ровно в её пикселях — texel за pixel, без билинейного пересэмплинга. Для окна World View прозрачность принудительно 1 (opacity < 1 даёт offscreen-группу и пересэмплинг), масштаб и обводка в меню look скрыты.
* Мышь: `RemapMouse` переводит экранную позицию в пиксели текстуры (низ-лево), `_wvPx` — неусечённый экранный прямоугольник элемента.
* Фон: камера `SmartWindows_Backdrop` (depth -1000, слой 31, SpriteRenderer с `SmartWindows_Backdrop.jpg/.jpeg/.png` рядом с dll, cover-fit; нет файла — серый). Живёт всю миссию (`MainVm() != null && Map != null`), потому что камера мира рендерит в текстуру.
* Фото-режим (Backspace = `HideHUD` → `SetHudVisible(false)`, также консоль и брифинг): `PhotoMode()` = HUD скрыт; камеры переключаются на `targetTexture = null` (3D на весь экран, без окна), `RemapMouse` и привязка мыши пропускаются; на выходе камеры снова в текстуру. Скриншот без UI (`TakeScreenShot(withUI:false)`) попадает в тот же кадр — `LateUpdate` идёт после `SetHudVisible`.
* Открытие/закрытие по отдельности: `MapModeStep` для каждого экземпляра окна карты: IsFullscreen=false, `NotifyPipChanged`, а у пункта меню «World View» (MenuItem, IsCheckable, привязка IsEnabled с путём IsFullscreen) снимается привязка (`ClearBinding`) и ставится IsEnabled=true (поиск по Items меню, до 20 попыток). Закрытый World View: камеры на паузе, мышь не попадает в мир.
* Лента курса (`CompassTape.cs`): Canvas 480×64 с OpacityMask из MainGameView переносится в `Grid` окна World View (родитель `Pip3DView`), DataContext = MainGameViewModel; цвет текстов/линий задаётся локально (белый/чёрный как в игре) по яркости картинки под лентой: копия полосы текстуры 32×4 → `AsyncGPUReadback` (RGBA32) → среднее, сглаживание 0.5, гистерезис 0.42 / 0.55, шаг 0.1 с. Критерий игры (`DarkTape`) — камера под водой; нам он не годится.
Отвергнуто: дыры в окнах поверх 3D под UI (Clip с полем окна) — внешний Clip попадает в кадр Noesis на кадр позже (порядок `UpdateInputs` → `Update` → render tree), отставал при перетаскивании; экстраполяция это только маскировала. Нативный PiP (`_pipImage`, viewport камеры в углу экрана + даунсэмплинг) давал мыло и лимит размером экрана.

## Рабочие столы (desktops) по клавише «Toggle fullscreen minimap» (по умолчанию Tab) (1.7.0, `src/ProfileSwitch.cs`, `src/SettingsDesktops.cs`; [General] DesktopCount (2..5, по умолчанию 2), [WindowSets] Active / Set1..Set5 / WorldViewOpen; опция TabSwitchesWindowSets удалена — в режиме всегда включено)
* Только в WindowedMapAndWorldView и в миссии: `MapTabPrefix` (патч `ToggleMapFullscreen`, который `InputHandler` вызывает по действию `KeyAction.MiniMapToggleFullscreen`, т.е. по любой назначенной в настройках игры клавише; вместо пропуска) вызывает `SwitchWindowSet`. Живые настройки ([Layouts] через `_saved`, [OpenWindows], WindowLooks, WindowLevels) всегда принадлежат активному набору, всё остальное работает как раньше; неактивный набор лежит в `Set1`/`Set2` текстом (`L|шаблон|x,y,w,h` ; `O|шаблон|open` ; `K|WindowLooks` ; `V|WindowLevels` ; `W|open`, записи через `;;`).
* Переключение: снимок живых настроек в слот активного набора -> разбор слота другого -> `_saved`/`_looks`/`_levels` заменяются, открытые окна получают `ApplyLayout` + `ApplyLook` + `BringToFront` (если уровень изменился); open/closed — значения `_osEntries` заменяются и запускается та же фаза восстановления, что после load screen (`OsPhase`, 1.5 с, запись выключена); World View — `MainGameViewModel.PipOpen`.
* Оба набора начинаются со встроенных раскладок мода (`PresetSet(mapTop)`, доли размера интерфейса); при первой миссии с опцией (оба слота пусты, `WindowSetsStep`) в `Set1`/`Set2` пишутся раскладки и применяется набор 1. Набор 1: World View (3D) сверху на всю ширину (60% высоты), Tactical Display слева внизу; набор 2 — наоборот, Tactical Display (карта) сверху, World View внизу слева. На ультраширокой матрице (соотношение экрана >= 2.1, 21:9 и шире) раскладка другая: три колонки 30/40/30% ширины на всю высоту — основной вид (в наборе 1 World View, во 2 Tactical Display) в центре, второй вид слева сверху (55% высоты), Unit Status и Unit Reference справа, Event Log / Formation Manager слева снизу. Для обычных экранов: Unit Status справа внизу, Unit Reference справа, Event Log / Formation Manager закрыты, вид окон и уровни по умолчанию. Игра не хранит стартовых позиций окон (часть — числа в XAML, часть — привязки к центру экрана), поэтому «настройки игры по умолчанию» восстановить надёжно нельзя. Активный набор хранится в `Active` и действует со следующего запуска (живые настройки уже его).
* `WindowSetsStep` (каждый кадр, без таймеров): раз за миссию, как только настроены окна карты; состояние World View (`PipOpen`) пишется в `WorldViewOpen` постфиксом сеттера `PipOpen` (`MapPipOpenPostfix`) и восстанавливается в начале миссии.
* Настройки: вкладка «Desktop mode» (`WindowedMapAndWorldView` = «Enable desktop mode», `DesktopCount`; ключи в .cfg остались в [General]) и страница «Desktops» во вложенном tab control («Settings» / «Desktops», виден при включённом режиме) вкладки «Desktop mode» (`SettingsDesktops.cs`: список рабочих столов — его состав не редактируется, число задаёт DesktopCount — и сохранённые X/Y/W/H и open/closed окон выбранного стола; редактирование, ↺ по окну и «Reset desktop to default» — к встроенной раскладке (`PresetSet(n)`); работает на копии, Apply/OK пишет в `SetN` и применяет активный стол). Столы 3–5: карта на весь экран / 3D на весь экран / рядом. Не переносится: окна, развёрнутые на весь экран (`ApplyLayout` их пропускает).

## Клавиша World View (1.7.0, `src/WorldViewKey.cs`; [General] WorldViewKey, по умолчанию Insert)
* Открывает / закрывает окно World View (`MainGameViewModel.PipOpen`, как крестик окна); пишет состояние в `WorldViewOpen` активного стола. Работает только в desktop mode и в миссии.
* Не конфликтует с игрой: перед действием проверяются привязки игры (`InputHandler.KeyBindings[].keyCodes` — актуальные, с учётом переназначений в Controls, любая привязка с этой клавишей считается занятой) и клавиши, которые игра читает напрямую (`DirectKeys`: BackQuote, стрелки, E/F/S/W/Y/Z, F11, PageUp/Down, Escape, Return...). Занятая клавиша игнорируется, в лог — предупреждение. Не срабатывает при консоли, меню, rebind, вводе текста (`consoleOpen`/`menuOpen`/`typingActive`/`RebindMode`) и при зажатых Shift/Ctrl/Alt. Поля InputHandler — публичные поля, не свойства: читаются `Fld`.

* Открытие / закрытие при переключении и Apply — без фаз и таймеров: каждое окно ставится один раз через `RestoreOpenStates(key)` с `OsSoft` (без `_osEnforce` и `_osWantOpen`), поэтому закрытое крестиком окно остаётся закрытым.

## Прочее в 1.7.0
* `src/DockReach.cs` (`ResizeDockedWindows`): зона ресайза (`ResizeThumb`, `ResizeHandleWidth`=10) лежит вокруг видимого тела; у края экрана / верха нижней панели HUD она вне экрана. Для стороны, где видимый край на краю, thumb'ы этой стороны получают отрицательный Margin внутрь на 4 единицы и увеличенный размер (thumb'ы в XAML идут после содержимого, значит выше).
* `src/TimePanel.cs` (`DragTimePanel`, `TimePanelPosition`): Grid панели времени (Canvas.Top/Right) в `MainGameView`, перетаскивание за не-кнопочные места, двойной клик — сброс; положение в .cfg.
* `src/WindowLook.cs` (меню look, `WindowLooks`, `WindowLevels`): по умолчанию `WindowLevels` = `Tactical Display|-1;World View|-1` (карта и 3D снизу); прозрачность (фоны Border / панелей), масштаб содержимого, уровень окна — снизу / по умолчанию / сверху (сортировка по Z внутри категорий в `BringToFront`).
* `src/TrackState.cs`, `src/TaskForceUpgrade.cs`, `src/CampaignDismiss.cs`, `src/LoadoutsOrder.cs`, `src/PinCentered.cs`, `src/ExistingWindows.cs`, `src/ResizeCursor.cs`, `src/XamlDump.cs`: описаны в заголовочных комментариях файлов.

* Панель времени (`TimePanel.cs`) — псевдо-окно в рабочих столах: ключ `Time Panel` в `[Layouts]` и в `L|` записях десктопа (X, Y слева / сверху, W/H = NaN, размера нет); живое место — `_saved["Time Panel"]`, применяется `ApplyTimePos` (каждые 0,5 с, пока не тянут). Без сохранённого места в desktop mode — отступ 6,4 справа / 26 сверху (`TimeDefR/T`), иначе место игры. Двойной клик — сброс. `TimePanelPosition` убран (`DropRetiredEntries`). Рамки и заголовка нет, look-меню нет.
