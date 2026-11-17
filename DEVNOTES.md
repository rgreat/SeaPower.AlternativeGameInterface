# Alternative Game Interface — заметки для продолжения работы (для Claude)

Мод для Sea Power: BepInEx 5 + Harmony, загружается через R.A.T.S. Keel (рекомендуется, Workshop 3835527459) или Anchor Chain (Workshop 3380210757).
UI игры — NoesisGUI (XAML), окна — `SeapowerUI.Window`. Доступ к игре — через рефлексию (в csproj нет ссылки на
Seapower-Scripts), Harmony-хуки ставятся по именам методов. Noesis: основной код — через рефлексию, окно настроек и шестерёнка
(1.3.0) — напрямую (ссылка на Noesis.NoesisGUI в csproj, как в Alternative Damage Model).

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

## Фильтр по источникам — выпадающий список «Source» (при ImprovedUnitReferenceUI, 1.9.0, `src/SourceFilter.cs`)
* Источник юнита — `MuseumEntry.ModSource` (игра ставит из папки ini юнита, `FileManager.GetModSourceForFile`; у базовой игры пусто; есть и в 0.8.6, и в 0.8.7; `CloneForFiltering` = MemberwiseClone, поле сохраняется). Только `MuseumObjectEntry`; оружие/сенсоры — не трогаются.
* Список источников — один обход `MuseumEntries` браузера (кэш на браузер, не кэшируется пока дерево пусто): база первой, потом моды по имени. Меньше двух — кнопка скрыта.
* Состояние — `[UnitReference] SourcesOff`: ВЫКЛЮЧЕННЫЕ источники через `|` (`<base>` = базовая игра), чтобы новый мод появлялся включённым. Пусто = все. Если выключены все присутствующие — фильтра нет (рассинхрон с установленными модами); в меню последний включённый снять нельзя.
* Фильтр — тот же постфикс `UpdateFilteredMuseumEntries`, что у Armed only, после него: `FilterBy(browser, SourcePredicate, …)` (Armed only переписан на общий `FilterBy`/`KeepBy`). `SyncNations` учитывает фильтр (`_collectSrc`): страны без оставшихся юнитов уходят из списка.
* Кнопка — `SourceBoxStep` рядом с `ArmedBoxStep` (раз в 0.12 с на окно): вставляется в тот же StackPanel сразу после Grid с TextBox поиска; подпись «Source: all ▾» / «Source: 2/5 ▾» обновляется по изменению. Меню — Popup как у меню вида окна (☑/☐, StaysOpen, закрывается при уходе мыши или повторном клике), строка «all» сбрасывает фильтр; клик по строке → SourcesOff, `SaveExpansionState` (если не поиск), `UpdateFilteredMuseumEntries`.
* Тексты: `ui.sources`, `ui.sourcesAll`, `ui.baseGame`, `tip.sources` во всех lang/*.ini.

## Вкладка Reference в Unit Status (UnitReferenceTab, 1.5.0, `src/StatusReference.cs`)
* Игра: `UnitStatusViewModel.Tabs` — `UnitStatusTabViewModel(owner, UnitStatusTab kind, header)` с Header / IsAvailable /
  IsSelected (сеттер → `OnTabSelected` → `EnsureContent`) / Content / ContentError. `SwitchUnit` (смена выбранного юнита:
  `DisposeOwnedContent`, доступность вкладок, при недоступной выбранной — `SelectFirstAvailableTab`); `Refresh` (каждое
  обновление окна) → `EnsureContent(выбранная)`; контент создаётся лениво, при ошибке — ContentError. Так же в 0.8.4.
* Мод: postfix конструктора — вкладка kind = 5 (за пределами enum), заголовок «Reference», последней; postfix `SwitchUnit` —
  IsAvailable = UnitReferenceTab и у юнита есть `_obp` (ракеты и торпеды тоже: «Information» игры открывается для любого ObjectBase) (иначе, если выбрана, — SelectFirstAvailableTab);
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
* Оружие (ракета, торпеда): `ResolveUnitEntry` ищет только `MuseumObjectEntry`, для ini оружия вид был пустым; для него — запись из `EncyclopedicDataManager.WeaponMuseumEntries` (IniRef) и `ObjectWeaponInformationViewModel(context, entry)` (`WeaponEntryOf`). У контакта-оружия `Vehicle.Class` = `getObjectIniName()` сразу.
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

## Фоновые картинки ([General] Backgrounds = Mods|Mixed|Game, was bool ModBackgroundsOnly since 1.5.3, `src/Backgrounds.cs`)
* Игра: картинки главного меню (`MenuSystemViewModel.Awake`) и экрана загрузки (`LoadScreen.Awake`) —
  `FileManager.GetAllFiles("ui/backgrounds", "*.png")`: файлы из ВСЕХ отмеченных папок (StreamingAssets/original, моды, user)
  одним списком, показывается случайная. В модах папка `ui\backgrounds\`.
* Мод: postfix GetAllFiles (только для пути ui/backgrounds, разделители любые): если в списке есть файлы не из
  `FileManager.specialDirectories[1]` (= original), файлы original убираются. Ставится вместе с хуком экрана загрузки
  (`TryInstallLoadScreenHook`, как только загружена Seapower-Scripts): если меню успело создаться раньше, первая картинка меню
  может быть игровой. По умолчанию включено. Trace: «backgrounds: N from mods, M of the game left out».
* Экран загрузки (Singleton `LoadScreen`) создаётся раньше хука и кэширует список в поле `imagePaths` (Awake): prefix
  `LoadScreen.ChangeBackground` чистит это поле тем же правилом (`DropGamePictures`). Подпись в окне настроек — «Mod backgrounds only».

* 1.8.2: хуки (`TryInstallLoadScreenHook` → `HookBackgrounds`) ставятся в конце `Awake` (после `RefreshEnabled()`, чтобы `Active` работал); раньше —
  в первом `Update`, ~2 с после Awake, и 1-2 с были видны штатные фоны. Запасной вариант из `Update` остался (сборка игры может быть ещё не загружена).
* Опция `[General] RealPhotos` (bool, true): «свои» картинки мода = файлы под папкой самого мода (`OwnDir()`: первая папка после StreamingAssets/1286220).
  `FilterBackgrounds` делит список на игровые / свои / чужие: Mods = чужие (+свои), пусто → игровые; Mixed = все (без своих при выкл.); Game = игровые.
  Пустой результат = список не меняется. `BackgroundsOn` истинно и для Mixed при выключенных фото.

## Settings window left open across a screen change (2.3.3)
* If the settings window was open when a mission / menu was loaded (also the game reloading after a change in its Mods menu), `_root`
  stays parented in the old screen's Grid and `IsOpen` stays true, so every later `Open` only switched tabs in the invisible copy: the
  window "did not open". `Open` now finds the gear's panel first; when `IsOpen` but that panel is not `_host`, the old copy is closed
  and the window built anew (trace "the open one belongs to another screen").
* The window also closes itself (as Cancel) when a new load screen starts (`LoadScreenPrefix` → `SettingsWindow.Discard`, checked by
  `Exists`, not `IsOpen`: the game hides the windows layer in its pause menu). Closing is traced now: "closed (OK)" / "closed (Cancel / X / discarded)".

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
* 13.10.2026: 1.3.0 проверена в игре и зафиксирована как стабильная (коммит «Alternative Game Interface 1.3.0»).
* 13.10.2026: 1.4.0 закоммичена («Alternative Game Interface 1.4.0»): дерево Unit Reference, прокрутка, список loadout'ов, Armed only.
* 13.10.2026: 1.5.0 закоммичена («Alternative Game Interface 1.5.0»): вкладка Reference в Unit Status, «Show all settings».
* 14.10.2026: 1.5.1 закоммичена («Alternative Game Interface 1.5.1»): окна установок как Weapon Reference; вкладка Weapons вынесена в Alternative Weapons Control.
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
- AlertsFix.cs (`[General] AlertsClickThrough`, по умолчанию on): сообщения игры справа внизу (`ItemsControl` над `MainGameViewModel.TopAlerts`) остаются в списке после затухания (`AlertTextOpacity` → 0), а hit-test Noesis игнорирует Opacity → гасшие Alert глотали клики (кнопки Fire в углу окон). Раз в 0,2 с для каждого Alert `IsHitTestVisible = AlertTextOpacity > 0.05`; сам список остаётся hit-testable. ItemsControl ищем по ссылке `ItemsSource == DataContext.TopAlerts` (скан раз в 3 с). Источник: заметка из AWC (AlternativeGameInterface_alerts_clickthrough.md); после выхода — убрать код и опцию из AWC.
- Правки 1.6.0 (поздние): StrictLoadouts теперь per-campaign. Значение — ключ `AlternativeGameInterface_StrictLoadouts` в `LinearCampaign.PersistentData` (сохраняется с кампанией, по умолчанию False). Галка на экране старта (`_strictStaged`) пишется postfix'ом `LinearCampaignTaskForceDifficultyRules.SeedPersistentDataForNewCampaign`. Строка в окне настроек (вкладка Game Menus, группа Campaign (this game)) — прокси-ConfigEntry в несохраняемом ConfigFile; видна только если окно открыто с шестерёнки Task Force Builder (`Field.TfbOnly`, `SettingsWindow._tfb`), при открытии подгружает значение кампании, Apply пишет его обратно в PersistentData. Опции AlertsClickThrough в cfg больше нет (всегда включено). Группа «Kept by the mod» (Unit Reference) убрана из окна — значения остаются только в cfg. FullUnitName: игра ставит `UnitName` только в SwitchUnit, а Replace (энциклопедия) меняет юнит под тем же окном — postfix `ObjectInformationViewModel.ChangeVariant` (последний шаг Replace) вызывает `SyncFullNames` по списку живых UnitStatusViewModel (без таймеров).

## Локализация (Loc.cs, Release/lang)
- Ядро `Loc.cs` — из руководства (как в AWC): `Loc.T/F/Or/Register`, файлы `lang/<код>.ini` рядом с dll, язык игры или `[General] Language`. Тексты окна настроек не в таблице D, а регистрируются: `SettingsWindow.RegisterTexts` (вызывается в Awake после всех Bind) кладёт `tab.<вкладка>`, `grp.<группа>`, `lbl.<Секция>/<Ключ>`, `unit.…`, `help.…` (описание .cfg) для каждого поля окна. Описания скрытых записей (Category, TreeWidth …) не переводятся — их нет в окне.
- Вкладки, группы и подписи в `Field` остаются английскими идентификаторами (сравнения `Tab == TRef`); показ — через `TabOf/GroupOf/LabelOf/UnitOf/HelpOf`.
- Настройка `[General] Language` — поле вида 'l' (ComboBox: язык игры + `Loc.GameLanguages()`). После Apply со сменой языка окно перестраивается на следующем LayoutUpdated (`RebuildAfterLayout`, не внутри клика).
- Релизные файлы `Release/lang/*.ini` (9 языков) генерируются скриптом из исходников: ключи берутся из `D(...)` в Loc.cs, из `Add(...)` в SettingsWindow.cs и описаний `Bind`; переводы — позиционные файлы. При добавлении текста/настройки: добавить ключ в таблицу или поле, перегенерировать и перевести файлы (иначе игра допишет английскую строку в конец файла под `; --- new ---`). Скрипты лежали вне репозитория (/home/claude/loc).
- Не проверено в игре: шрифты игры могут не содержать CJK/вьетнамские глифы.

## Файл настроек в user (1.6.0)
- `AlternativeGameInterfacePlugin.Config` скрыт полем `new ConfigFile Config` (OpenSettingsFile в начале Awake): `StreamingAssets/user/AlternativeGameInterface/AlternativeGameInterface.cfg`, как AWC. Если файла нет, а BepInEx/config/<guid>.cfg есть — копия один раз (старый не трогаем). Ошибка папки — откат на BepInEx/config. Весь прежний код (`Config.Bind/Save`, `P.Config`) работает без правок, т.к. обращения идут через тип плагина.
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
* Фон: камера `AlternativeGameInterface_Backdrop` (depth -1000, слой 31, SpriteRenderer с `AlternativeGameInterface_Backdrop.jpg/.jpeg/.png` рядом с dll, cover-fit; нет файла — серый). Живёт всю миссию (`MainVm() != null && Map != null`), потому что камера мира рендерит в текстуру.
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

* `src/EventLogMap.cs` (`CenterMapOnClick`, [EventLog]): a handler of `Hyperlink.ClickEvent` on the Event Log window (DataContext `EventLogViewModel`); the Hyperlink's DataContext is the `Alert`, its Command is `SelectRaisingObject` / `SelectAboutObject` (the game switches the 3D camera) - the object is `RaisingObject` / `AboutObject`. The place is `ObjectBase.GetMapPositionForTaskforce(Globals._playerTaskforce)`, only when the player's side has a map vehicle for the object or it is the player's own (an unseen enemy's real place is not given away); `MapBase.TargetCenter` of the Tactical Display window is set (animated). The settings tab "Event Log" is shown when the settings are opened from the Event Log window.

* `src/EventLogMap.cs` (`AllTracksAreLinks`): the game makes a Hyperlink for a track only in `RaiseAlert(raising, about, text)` (`${TrackId}`, `${OwnTrackId}`); "New contact ... track N" (`Taskforce`: the id is put into the text by the caller), "Track N classified" and "Track N faded" are plain. Prefix / postfix on every `MainGameViewModel.RaiseAlert`: the Alert added by the call (`EventLog.Alerts[0]`, a new object) gets every number of 4 - 6 digits that is not inside a `<Hyperlink>` and is a track in `Globals._playerTaskforce.PlottingTable.Vehicles` (own tracks from 1001, other sides' from 7001) wrapped in a Hyperlink bound to `SelectRaisingObject` / `SelectAboutObject`; an Alert that has no object in that place gets it (the backing fields of the get-only properties, the command taken from an Alert the game builds with that object). `AlertContent.FormattedContent` is rewritten (it is parsed again).

- Damage Control: ancestors of the tab (up to the window) get ClipToBounds while SectionsMinHeight > 0, so the section lists no longer draw below the window edge (ClipTab; restored at 0).
- Damage Control: row-2 MinHeight is capped to the room left in the window (DcRoom: window height - tab offset - frame bottom inset - 8 px - other rows), refitted on window size change (DcFitStep), so with a small window the section lists shrink instead of being cut off; SectionsMinHeight is the minimum only when the window has room.

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
`Enabled` = the Mods-menu tick. `menu.button` = "Game Interface" (ru "Игровой интерфейс", others English) in `Loc.cs` and every `Release/lang/*.ini`. `OpenSettingsFromMenu(anchor)` (the main menu / pause menu view): starts from the view's Content (a UserControl has no panel of its own) and calls `SettingsWindow.Open(..., all flags false, showAll: true)`: every tab, as after "Show all settings" (that button toggles back). The window then goes into the topmost Grid above the view's content, like for the News window's gear in the main menu. Nothing in the settings code needs a game window: the Strict loadouts row only writes into campaign data when there is a campaign (`CampaignData()` null otherwise), the window lists work on the tracked windows that exist. The gears of the windows are not touched.

**Gear of the World View window (`src/ModChooser.cs`).** The gear of the World View window (`IsWorldViewWindow`: header "World View" or its own `WorldViewWindowViewModel`; the window's DataContext itself is usually inherited, so it is not tested alone) calls `ChooseSettings`: it reads the hub registry (`AppDomain` data `rgreat.settings.v1`), takes only the active entry whose id contains `special-effects` (the 3D effects; the other mods have nothing to do with the 3D window), adds our own entry (its open = the gear's usual `SettingsWindow.Open` with the window's flags) and shows the hub's own list through reflection on our copy's private `SettingsHub.ShowList` (so the shared `SettingsHub.cs` stays unchanged). No other active mod / reflection failure -> our settings open at once. Other windows' gears are unchanged.

## Propulsion cell in Unit Reference (`src/Propulsion.cs`, `[UnitReference] ShowPropulsion`)
The unit page is one DataTemplate (`UnitDetailTemplate` in ObjectInformationView.xaml): Grid {identity header Border, WrapPanel of stat cells (Grid.Row 1), TabControl (row 2)}; one template per entry type, re-used while the type stays (the DataContext changes). The game's `ObjectInformationView.UnitTabs_SelectionChanged` runs when the TabControl is built, so a postfix there (`PropulsionPostfix`) finds the WrapPanel through the TabControl's parent Grid and inserts a cell (Tag `agiPropulsion`, the game's own Border / label styles copied from the first cell) at index 4, after Max Speed; `DataContextChanged` of the cell refreshes its text (no polling). Data: `UnitMetadataProvider.Instance.UnitData` (key (folder, ini name)) -> folder `vessels` -> `SimpleObjectParameters._ini` -> `IniHandler.readValue(section, key, string, bool)` of `[MainPowerSystem]` / `[AuxPowerSystem]` `Type` (Steam / GasTurbine / Diesel / Nuclear / Electric / AIP / None) and `HorsePower` (the total of the vessel). The dictionary of inis is built once. With several plants (main + auxiliary) the caption line is collapsed (fewer lines); the cell has a tooltip `ref.propulsion.tip`. Aircraft and land units have no cell. Texts: `ref.propulsion`, `ref.pp.<type>`, `ref.hp` in `Loc.cs` and every lang file.

## Window opacity and state-driven backgrounds (`WindowLook.StateDriven`, `HookState`)
The opacity puts a clone of a Border's / Panel's brush on the element. Set as a LOCAL value it wins over the style's triggers: a Border in the template of a `ListBoxItem` / `TreeViewItem` / `TabItem` / `ComboBoxItem` / `MenuItem` / `ButtonBase` (TemplatedParent of those types) has its Background changed by IsSelected / IsMouseOver / IsPressed / IsChecked / IsEnabled triggers, so the clone stuck: the unit type tab (ListBox in `UnitTreeBrowserView`) that was red when the look was applied stayed red (found 2026-11-04). Now such Borders get the clone through `SetCurrentValue` (the style stays the value source, so the next trigger puts the game's brush back; `InvalidateProperty` + set when the factor returns to 1), and their TemplatedParent is hooked once (`ConditionalWeakTable`, factor kept there): MouseEnter/Leave, IsEnabledChanged, keyboard focus, Selected/Unselected (ListBoxItem, TreeViewItem; TabItem through `Selector.SelectedEvent`), Checked/Unchecked/Indeterminate (ToggleButton), PreviewMouseLeftButtonDown/Up and LostMouseCapture (buttons, menu items). Each event dims the template's backgrounds at once (`Redim`, not into nested items) and queues the item for the next window step (`FlushRedim` in `LookStep`) for triggers that follow the event. `DimWalk` hooks state-driven Borders even while their brush is empty (an item selected later). The once-a-second `LookStep` re-apply stays the backstop.

## IMGUI windows of other mods over Noesis (`src/ImguiOrder.cs`)
CameraOffset.dll: `OffsetMenuHandler.OnGUI` (depth 0) draws `GUI.Window`, dragged by `GUI.DragWindow` on the 20-unit title (`GUI.matrix` scale = UIScale or H/1080). `NoesisView.OnGUI` -> `ProcessEvent`: MouseDown / MouseUp -> `ev.Use()` when `HitTest` hits a Noesis element, then `_uiView.MouseButtonDown/Up` (mouse moves go in Update, not OnGUI). There are several NoesisViews (each event reaches 2-3 of them). Trace (2026-11-04) showed them first whatever `GUI.depth` (NoesisView has `useGUILayout = false`): setting depth 10000 did not help, it is kept (harmless). Fix: prefix `NoesisGuiPrefix` keeps a copy of each MouseDown (per view, in order) and skips the original; at the next event (Repaint follows in the same frame) `ResolveDown`: `GUIUtility.hotControl != 0` (an IMGUI control / window title took the press) -> dropped; else each view gets the copy through its own `ProcessEvent` in the original order, stopping after the view that uses it (as the game did). MouseUp / MouseDrag are skipped while `hotControl != 0`. A press on an IMGUI window body (no control) still reaches the interface. Everything by reflection (`Event`, `GUIUtility` are in IMGUIModule, not referenced).

## SettingsHub timing (1.8.0)
`SettingsHub.cs` (the new canonical copy) patches the menu classes only after the load screen has finished: patching them at plugin start ran their static constructors (Noesis properties) before Noesis was ready and crashed the Release build of the game. Because the hub now hooks the load screen iterator itself, `Register`/`Install` are called in `TryInstallLoadScreenHook` (right after our own load screen patch), not in `TryHook`, which runs after the load screen is done.

## Two game builds (checked 2026-10-30)
The newer Release build (0.8.7-261021, decompiled from the user's GameFiles) was compared member by member with the older build: every game type and member the mod reflects exists in both, except `BlitToPipImage.Downsample` and `CameraManager.PipSupersampleFactor` (newer only). `PipSupersampleFactor` was already optional (factor 1 without it). `HidePipScene` used to switch itself off without `Downsample`; now it falls back to a plain `Graphics.Blit(source, pipImage)`, which is what the older build's own `OnRenderImage` does. Other differences between the builds (MainGameViewModel takes the window host from `RootControl.FindName("WindowHost")` instead of `WindowHostManager.CurrentHost`, `PipShown = ShowPip3D && IsHudVisible`, `Singleton<T>.OnDestroy` clears the instance) do not touch anything the mod reflects; `WindowHostManager.CurrentHost` exists in both.

## Optical view (binoculars) in WindowedMapAndWorldView (1.8.0, `src/BinocularFrame.cs`, `src/MapWindows.cs`)
Current design only; every point below was found in the game code or in a trace.

**Mouse / camera.** The optical overlay lies over the World View picture but is not inside `Pip3DView`, so `MouseOverManager.UpdateCursor` (hit element != Pip3DView -> over Noesis) puts `MouseControlState` into UI and blocks the camera. `MouseOverManager` must NOT be patched (its static constructor runs before Noesis knows the type: "Native type is not registered", crash on mission load). `MapMouseStatePostfix` (on `MouseControlState.OnUpdate`): mouse inside the picture (or a drag that began there) and state UI -> `MapOverlayIsWorld` repeats the hit test from the window's root; a hit inside the World View window -> `RepeatWorld`: reset `_currentState` to All (the early-returning first pass leaves UI, which the repeated pass would take as the state the press began in and the whole drag would go to the UI), `setMouseOverNoesis(false)`, `OnUpdate` again (re-entrancy guard). Title bar and buttons are outside the picture rectangle and stay UI.

**Frame fit.** Game XAML (WorldViewWindowView.xaml): window Grid(ClipToBounds){Pip3DView, Viewbox UniformToFill (ShowPipOpticalOverlay){OpticalOverlayView Width=UiRootWidth Height=UiRootHeight}}; the overlay is laid out for the whole screen (fixed margins for 1920x1080) and cropped, so in another window shape the eyepieces are cut off. `BinoFit`: Viewbox -> `Stretch.Uniform` (whole frame, centred), then a centred `ScaleTransform` k = min(1.30, fill/fit scale) on the overlay (frame and interface; `BinoMaxEnlargeWide/Tall`), four black `Rectangle` bars in the window grid for what is still uncovered, and the sensor bearing block (the 140x140 compass, bottom right; `BinoCompass`) moved in by what the window cuts off. Sizes come from the Viewbox's parent grid (the Viewbox itself reports 0x0). Option off: `UniformToFill`, no transforms, bars hidden. Event driven: a postfix on the `OpticalOverlayView` constructor (no static constructor: safe to patch); the one with a Viewbox ancestor is the window's (the full-screen one in MainGameView stays as the game lays it out); SizeChanged/IsVisibleChanged/IsOpticalViewActive events refit.
**Never keep native Noesis elements (Viewbox, Grid) in a `WeakReference`**: the managed wrapper is created on demand and the target is gone after a while (the first two designs silently never ran). Only types the game or the mod extends (OpticalOverlayView, Window) are safe; otherwise walk from them with `VisualTreeHelper.GetParent` (`BinoViewbox`).

**Backspace (photo mode) + optics.** `RefreshHudCameraVisibility`: `UiCamera.enabled = IsHudVisible || IsOpticalViewActive`; the windows layer `OverlayCanvasNew` is not under `DisplayAreaGrid`, so the windows showed over the picture, and the forced `ShowPip3D = true` put the overlay into the World View window whose picture is not updated (`PipShown = ShowPip3D && IsHudVisible`). Now `MapShowPipPostfix` keeps the game's value while `PhotoMode()` (the game's own full-screen optics) and `PhotoLayers` (on `IsHudVisible` and on the option change) hides `OverlayCanvasNew` and re-raises the `ShowPip3D`-dependent flags (the game does not raise them when the HUD toggles).

**Normal view in binoculars** (`BinocularView.cs`, moved from Alternative Special Effects where it was `[Optics] BinocularsNormalView / NormalViewDayOnly`; here `[Camera] BinocularsNormalView / BinocularsNormalViewDayOnly`, "In Mission" tab, both on by default). Postfix of `CameraManager.ApplyOpticalPostProcessProfile`: for the Binoculars mode and the last (maximum) optical profile the post-process filter (`_opticalPostProcess.effectEnabled`) starts off, `toggleKey` = `originalToggleKey` so the game's filter key turns it on; DayOnly: only while `DynamicSky.getSunlightIntensity() >= 0.5`. Old ASE values are not migrated.

**Ship binoculars below the horizon.** `CameraManager.ApplyOpticalRotationConstraint` clamps the ship binoculars' pitch to -90..0 (positive pitch looks down). `BinoPitchPre/Post` keep the pitch before the clamp and, for Binoculars on a `Vessel` only, clamp it to -90..`BinocularsBelowHorizon` (config section Camera, "In Mission" tab, default 10, 0 = the game). Aircraft binoculars and the periscope are untouched.

**Screen vs Noesis coordinates (found 2026-10-30).** `MouseOverManager.UpdateCursor` feeds `PointFromScreen` with `mouse * UIScale * Screen.height / 1080`; `PointFromScreen`/`PointToScreen` are inverses in screen pixels (top left origin), so that is right only at 1080 lines: at 1440 the game's hit lies a third too far from the corner (the "overNoesis" flag flipped at the picture's lower part, and resizing a window over the picture also rotated the camera). AGI's own hit test (`HitIsWorld`) uses plain pixels. `OverWorld` (on mouse move / button down, not every frame) says whether the picture really is under the mouse (inside the World View window, not under a `Thumb`/`ButtonBase`; no hit counts as the picture); the press decides the owner of the whole drag (`_mapDownInside`); not the picture -> the postfix forces state UI regardless of the game's flag. The rectangle of the picture alone is only a pre-filter. Ordinary mode (not Desktop) uses the same fix: `GameMouseFix` in the same `MouseControlState.OnUpdate` postfix hit-tests the whole main view root in pixels (hit and not under `Pip3DView` = UI), the press decides the owner of a drag (`_fixDownNoes`), skipped when the HUD is hidden (`HudHidden`).

## Damage Control lists: mouse wheel ([UnitStatus] DamageListsWheel, `src/DamageScroll.cs`)
- The five section lists of `DamageControlView` are plain ListBoxes without a Background: the empty parts of a row are not hit-testable, so the wheel reached nothing there.
- `SetupDamageWheel(view)` runs once per tab from `OnDamageControlLoaded` / `OnDamageControlShown` (events, no polling): the ListBoxes found in the visual tree get `Background = Transparent` and a bubbling `MouseWheel` handler: if the list's own ScrollViewer did not handle the event and `ScrollableHeight > 0`, it calls `LineUp` / `LineDown` (|Delta| / 40 times, the unit of the ScrollViewer is not assumed) and marks it handled.
- Do NOT set `CanContentScroll = false` on these lists: with the game's VirtualizingStackPanel (Recycling) new items - newly damaged systems - were no longer shown.
- Applies to the next tab shown after the option is changed. Trace: "damage control wheel: N lists set up".

## Tactical Display closed by a desktop switch (`RestoreOpenStates`)
- The map window is closed the way its X button closes it: `Window.CloseWindowNow()` (OnClosed, the window manager, the host's close command); MapVisible then follows through the HUD button's ToggleWindowBroker. Setting `MainGameViewModel.MapVisible` directly is the fallback.
- KeepMapView state is kept per `SceneMapViewModel`, not per window: the zoom belongs to the view model and outlives the window, so a window opened again at another size gets the zoom corrected from the size the zoom was last right for. Closed windows (not visible) are not measured.

## Game bug: drawing tool objects doubled after the map window is closed and opened (not the mod)
- Reproduced without the mod (Sea Power 0.8.7): draw a ring / ruler, close the Tactical Display (X), open it again - a second picture of the ring / ruler lines stays fixed on the screen (does not follow zoom or pan). Markers are not doubled.
- Diagnosed with a temporary trace (removed): `DrawingLayer.Graphics` holds each drawing once; the visual tree (all NoesisView roots, ~850 elements) holds one MapEllipse / MapPolyline per drawing, in the new window's GizmoLayer, ParentMap = its own map. The eraser removes a drawing and both pictures go - the extra picture belongs to the same drawing but is not in any visual tree (a stale render of the closed window's view, still bound to the drawing).
- Related defect in `GizmoHost`: `Cleanup` (Unloaded) disposes `_handle` but does not null it, so a second Loaded of the same host calls `ReplaceGizmo`, which does not put the gizmo back into the layer.

## Reference tab: the tab Kind (`src/StatusReference.cs`)
* The game's `UnitStatusViewModel.EnsureContent` builds a tab's content by its `Kind`. A newer game build added a Supplies tab (`UnitStatusTab` 5, `SuppliesStatusViewModel`): our tab, made with Kind 5, got the Supplies view model as its Content and the Reference stayed empty (trace: `Reference tab: not made: ... content SuppliesStatusViewModel`).
* The Reference tab now uses Kind 105 (far past the game's values; the game also looks tabs up by Kind in `ShowUnitStatus`). Alternative Weapons Control's Weapons tab uses 6, which collides when the game takes 6.
* `RefWhy` traces once why a selected Reference tab got no content.
* Reference for a contact (`RefTarget`, `WatchContact`): while Track State holds a contact (`TrackTab.Vehicle`), the Reference tab is available when `Vehicle.Class` has a value (the game sets it when one candidate class is left or the identification is forced; the value is the unit's ini name). The variant and loadout of the real object are not used (variant 0). `Vehicle.Updated` is subscribed while the contact is held (no polling): when `Class` changes, `UpdateRefTab` runs; content made for another ini is dropped and made again. Weapons are left out. `TrackSwitch` selects the Track State tab with `SelectOnly` (the Reference tab may have been the selected one).
* Track State tab Kind is 107 (was 7), for the same reason as the Reference tab's 105.


## Other monitors for windows (desktop mode, `[Monitors] Enabled`, `src/Monitors.cs`)
Game facts (decompile): windows are items of `WindowHost.Windows` (ObservableCollection of view models); the data templates (VM -> `Window`) are in `MainGameView`'s `UserControl.Resources`; an `ItemsControl` in `OverlayCanvasNew` shows them; `WindowHost` is a Behavior on the view root that handles `WindowHostCommands.OpenWindowCommand / CloseWindowCommand` for the elements below that root (`MainGameView.FindName("WindowHost")` in the newer build, `WindowHostManager.CurrentHost` before). `NoesisView.MousePosition` uses `Display.RelativeMouseAt` and `OnGUI` / `UpdateMouse` act only for the display of the view's camera (`_activeDisplay == _camera.targetDisplay`), so a view with its own camera on a display works there. The game's own use of other displays: `SecondDisplayMapHandler.EnableSecondMonitor` (second map; `SecondaryDisplayBlanker` black cameras), `CameraManager.SetWorldDisplay` (World View on a display, `WorldViewDetached` -> `ShowPip3D` false), `CameraManager.MouseOnWorldDisplay`.
* Off for the game while the feature is on (`MonitorsOn` = option + desktop mode + more than one display): prefixes force `EnableSecondMonitor(enabled=false)` and `SetWorldDisplay(0)`, `WorldViewDetached` answers false.
* A `MonitorView` per display i >= 1 (`MonitorsStep`, in a mission when the main view and its host exist): GameObject + Camera (targetDisplay i, grey clear) + `NoesisView` (added by reflection after the camera; render settings copied from the main game NoesisView; `UseRealTimeClock`); content = XAML parsed in code (Grid with ScaleTransform = UIScale x display height / 1080, ItemsControl over a second `WindowHost`'s `Windows`), set with the private `NoesisView.CreateView`. The second `WindowHost` is made with `Activator` (its constructor makes itself the manager's `CurrentHost`: put back), attached with `Interaction.GetBehaviors(root).Add`. The main view's `Resources` are merged into the Grid; DataContext = the main view model. Not tested by me at runtime: failures go to the trace ("monitors: ..."), `_monBroken` switches the feature off.
* Moving a window = its view model out of one host's `Windows` into the other's (`MonitorMovesStep`, queued from the menu / Loaded: never inside layout). A view model is on one host only: `WindowHost.OpenNewWindow` is skipped when another host has it, `CanCloseWindow` answers true and `CloseWindow` is forwarded to the owner (the game's toggles ask the main host).
* The monitor of a window pattern: `[Monitors] Windows` = `pattern|n;...` (n = display number, 1 = main, not stored), per desktop in the record `N|` of the desktop text (`DeskData.N`); the desktop switch moves windows whose monitor differs. `Loaded` of a window (`MonitorCheck`) moves it to its remembered monitor. The look menu has a `Monitor 1 2 3` row (`MonitorRow`) for remembered windows other than the Tactical Display / World View (the 3D picture stays on the main display; later).
* Mouse input of the monitor views: `NoesisView.ProjectPointer` flips y by `Screen.height` (the main window's) and the OnGUI mouse events carry the main window's coordinates, so `MonProjectPrefix` flips by the view's own display height and `MonProcessEventPrefix` turns `enableMouse` off for these views (keys still come through OnGUI: a view acts only while the mouse is on its display, `_activeDisplay`). `MonitorInputStep` sends buttons (down / up / double click by time and distance) and the wheel (`mouseScrollDelta` x 120) with `Display.RelativeMouseAt` positions to the view of the display under the mouse; a release goes to the view of the press. Moves are NoesisView's own `UpdateMouse`. `[Monitors] Enabled` is on by default (it still needs desktop mode and more than one display).
* Geometry per monitor: `DisplaySize(element)` (display of the element's view: `DisplayOf` compares the visual root with the monitors' roots) replaces `Screen.width/height` in MaxFit, DockReach and Snap; the HUD's bottom limit applies to the main display only.
* Mouse: `MapMouseStatePostfix` (desktop mode) forces UI state while `Display.RelativeMouseAt(Input.mousePosition).z > 0` (only windows are on the other monitors); `GameMouseFix` / the postfix / `MapRemapPrefix` leave the game's decision when `CameraManager.MouseOnWorldDisplay` (the game's own World View monitor); `MapPausePrefix` does not pause while `WorldViewDetached`; the World View texture is released when the game detaches the view.

### Monitors: backdrop, Tactical Display, World View (1.8.2)
- Each monitor camera gets its own sprite object (layer 31, the main backdrop's sprite, cover-fitted per display) - `MonitorBackdrop`.
- `DisplayOf` walks `GetParent` up to `m.Root` (GetRoot returns an internal root, not ours).
- The Tactical Display / World View are choosable for a monitor. `MainGameViewModel.SyncWorldViewWindow` is skipped (`MonSyncWorldPrefix`) when the World View VM sits on a monitor host (else it would be duplicated into the main host).
- `_wvDisp` = display of the World View picture; `_wvPx` is in that display's pixels. `PointerOn(display, out x, out y)` gives the mouse in that display's top-left pixels (RelativeMouseAt); used by HitIsWorld, MapMouseStatePostfix; MapRemapPrefix does the same for the texture mapping. Photo mode (HUD hidden) is off when the World View is on another display.
- Untested at runtime: needs a trace from a 2-monitor setup.

### Do not patch SeapowerUI.SceneEdit.Behaviours.MouseOverManager
Harmony's patch runs the type's static constructor, which registers a routed event for a type Noesis does not know yet: MainGameView fails to load ("Native type is not registered for 'MouseOverManager'"). Its cursor callback (one global callback for all views) cannot be filtered there; the stale hover of a view on another display is avoided by parking that view's mouse far outside (monitor view at creation, a view the mouse left).

## Фон главного меню: видео или слайдшоу (`[General] MenuBackground` Video/Slideshow, `SlideshowSeconds` 15, по умолчанию Video, `src/Backgrounds.cs`)
* `MainMenuView.xaml`: `MediaElement MainMenuBackgroundVideo_MediaElement` (main_menu.mp4, зациклен) поверх картинки `MenuSystemView` (`BackgroundImage`, выбирается `MenuSystemViewModel.ChangeBackground` при смене `CurrentWindow`, если `randomizeImage`).
* Slideshow: prefix сеттера `CurrentWindow` — у новой `MainMenuView` до показа `Source = null`, Stop, Collapsed (видео не загружается); `MenuBackgroundStep` (каждый кадр): пока текущая страница — главное меню, раз в `SlideshowSeconds` вызывает `ChangeBackground` (до 4 раз, пока картинка та же). Плавная смена: второй `Image` (в Grid `MenuSystemView` на индексе 1, над картинкой игры, под страницей); следующая картинка (случайная из `imagePaths`, ≠ текущей) грузится через `ResourceLoader.getNoesisTextureSourceResource`, проявляется 1,5 с (smoothstep по Opacity), затем становится `BackgroundImage`/`backgroundImageResourceKey`, прежняя выгружается `PurgeCachedTextures`. Возврат к Video «на ходу»: `RestoreMenuVideo` (сохранённый `Source`, Visible, Play).

## Быстродействие: что считается каждый кадр (проверка 1.8.2)
* Каждый кадр: `MenuBackgroundStep` (в меню; в миссии выходит сразу), `BackdropStep`, `MonitorsStep` (+ `MonitorMovesStep/InputStep/MenuStep` только при вторичных мониторах), по окнам — `Step` и перечисленные в Update шаги.
* Кэшируются: `MainGameView.Instance` (FieldInfo), `MainHost()` (по экземпляру вида; `FindName` раз за вид), поле `ContextMenuCanvas`; причина «мониторы вкл/выкл» формируется строкой только при изменении входных значений; `MonitorBackdrop` не трогает спрайт, пока размер экрана не менялся; Event Log: окно с известным не-логовским DataContext больше не проверяется.
* Совместимость 0.8.6 (release) и 0.8.7: сверено по декомпилу — `MenuSystemViewModel`, `MainMenuView.xaml` (видео-элемент), `ObjectWeaponInformationViewModel`, `EncyclopedicDataManager.WeaponMuseumEntries`, `Vehicle.Class`, `NoesisView` одинаковы; `MainGameViewModel.SyncWorldViewWindow` в 0.8.6 берёт хост из `FindName("WindowHost")`, в 0.8.7 — из `WindowHostManager.CurrentHost` (патч и `MainHost()` учитывают оба).

## Unit Status: закладка на юнит (`[UnitStatus] RememberTab`, true, `src/StatusTabMemory.cs`)
* Prefix `UnitStatusViewModel.SwitchUnit`: закладка окна (выбранная и доступная; Kind, в т.ч. 105 Reference и закладки других модов) записывается для покидаемого юнита (`_unit`, у контакта — `TrackTab.Vehicle`) в `ConditionalWeakTable`. Postfix (после хуков Track State / Reference): у нового юнита с записью — `SelectOnly` на закладку, если она доступна. Закладку, которая ещё недоступна (её делает доступной хук другого мода, напр. AWC Weapons), подхватывает postfix `Refresh` окна (один раз, дальше выбор игры). Ключ юнита — ссылка на объект: память живёт, пока жив юнит / контакт.
* 2.3.0: the option `RememberTab` is removed (always on; the key is in `DropRetiredEntries`). `[UnitStatus] LastTab` (int Kind, -1 none, written by the mod): `TabTrackStep` (per frame, window with UnitStatusView, skipped while a restore is pending) writes the open tab to `TabMemory[unit]` and `LastTab`; `TabApply` takes `TabMemory` or, for a unit without a record, `LastTab` (the previous unit's tab, also the first unit after a restart). The cut is recomputed per contact 5 times a second at most (`ClipState.Tick`; `UpdateEllipse` is called every frame); `FieldV` caches the members. Not verified in game.

* 2.4.0: what counts as the user's tab. The game's `SwitchUnit` keeps the open tab when the new unit has it, else `SelectFirstAvailableTab` (Engagement). The old code noted every shown tab (also in the SwitchUnit prefix) as the unit's own and as `LastTab`, so a unit that lacks the tab (a weapon: no Sensors) put its fallback into `LastTab` and into its own record, and the next unit opened on Engagement. Now `TabNote` writes nothing; `TabTrackStep` (20 Hz) writes `TabMemory[unit]` and `LastTab` only when the tab changed while the same unit stayed shown (`TabSeenBy`: status -> unit + tab seen last; a tab seen right after the unit changed is only remembered as "seen"). Trace lines "remember tab: ..." show the user's choice, a put-back and an unavailable tab. Not verified in game.

## Enabling in the Mods menu needs a restart (2026-11-11)
Anchor Chain (with its Beta Compatibility preloader) loads the DLLs of **ticked** mod folders only ("original mod code will be loaded only from checked directories"). A mod that is unticked at game start is not loaded at all (LogOutput.log lists no "Loaded assembly AlternativeGameInterface"), so nothing in the plugin runs and ticking it later (Apply only reloads scene 0) cannot start it: the game must be restarted. Turning it OFF at run time works (the plugin follows the tick and goes idle). The comment in AnchorChainEntry ("started even when unticked") is only true for older Anchor Chain versions. Not fixable in the mod.

## Sea Power 0.9.0 - compatibility review (2026-11-12, mod 1.9.0, nothing changed in the source yet)
Compared: decompiled 0.9.0 (`Seapower-Scripts.dll`, `Noesis.NoesisGUI.dll` from GameFiles) against the 0.8.7 builds, and the 0.9.0 XAML against the 0.8.7 XAML. Every game type and member name the mod reflects still exists; no patched method changed its parameters in a way that breaks a patch.

Fixed on 2026-11-12 (1.9.0): `_info.ini` -> `GreaterThanEqualToVersion=0.8.6`; `IsFlagship` check in `CanDismissPostfix` (CampaignDismiss.cs); `MoveTimeFollowers` in TimePanel.cs (the other children of the time Canvas get the panel's Canvas.Right and its Canvas.Top added to their own top margin, their Top binding stays). The findings:
* `Release/_info.ini` has `ApproximateVersion=0.8.6` (rule: same major.minor, patch not lower). On 0.9.0 it fails: `FileManager.CheckCompatibility` shows the "incompatible mods" warning at start (Disable mods / Ignore), and `CheckForUpdates` does not download Workshop updates of a mod whose metadata fails the check. Use `GreaterThanEqualToVersion=0.8.6` (the key exists in 0.8.6 / 0.8.7 / 0.9.0). The same line is in ADM, AWC, ASE.
* DismissDamaged: 0.9.0 `CanDismiss` also refuses the flagship (`LinearCampaignForceFlagship.IsFlagship(item)`); `CanDismissPostfix` repeats the airbase / historical checks but not this one, so a damaged flagship becomes dismissable. Add the flagship check (type is absent before 0.9.0).
* Time panel (TimePanel.cs): `TimeCompressionView.xaml` got a second Canvas child - the mission timer Border (Canvas.Right=0, Top = height of the controls, RenderTransform bound to the controls). The mod moves the controls Grid by Canvas.Top / Canvas.Right, so the timer stays in the corner when the panel is dragged.

Works, behaviour to know:
* Tactical map rewritten (`MapInstance`, `MapRegistry`, `MapTerrainBackground`, geometry store instead of XAML shapes, orthographic projection, shared `MapDrawingLayer`). The map world is built when a `MapBase` is loaded and released when it is unloaded: closing / reopening the Tactical Display or moving it to another monitor rebuilds it. Still a RenderTexture, so it shows in any NoesisView. Main map renders only while `UiCamera.enabled` (HostVisible). `MapBase.ZoomLevel / TargetZoomLevel`, `MapViewModel.Center` are there (KeepMapView, EventLogMap). The pending "GizmoHost drawing duplicates" fix is probably obsolete (drawings are no longer per-view XAML) - check in game.
* `MainGameViewModel` takes the host from `RootControl.FindName("WindowHost")` (as 0.8.6); `WindowHostManager.CurrentHost` still exists. `MainHost()` covers both.
* NoesisGUI updated: `NoesisView.ProjectPointer` uses the camera's pixelRect, `ProcessEvent` uses `_viewSizeY` instead of `Screen.height`. The monitor patches replace / gate these and stay valid. Public managed API: additions only.
* Unit Status: new game tab `UnitStatusTab.Supplies = 5`. Mod kinds 105 / 107 are clear; AWC's Weapons tab is 6 - the next game tab will collide.
* Unit Reference: new "Performance" tab (index 3, aircraft / helicopters only; `HasPerformanceTab`, `EnsureSelectedTabAvailable` handles a remembered index). New weapon / sensor rows. Tree, search panel, loadouts unchanged.
* Event Log: new `RaiseContactAlert` (New contact ...) makes the track number a link itself (`Alert.SelectContact` -> `NavigateToContact`: opens the map and centres it on the first click). It is not a `RaiseAlert` overload, so the mod leaves these messages alone; its own links still centre on the repeated click.
* File layers (`FileLayer`, `LayerKind`, runtime mounts): `GetAllFiles(path, pattern, kinds)`, `SaveDirectories`, `specialDirectories`, [LoadOrder] format unchanged for the mod. `GetModSourceForFile` returns the layer name for every non-base layer (the user folder too) - the Source filter lists it as a source.
* New: rolling replay (Alt+F8; closes Unit Status / Formation Manager / Tactical Doctrine while active, hides the bottom bar), radio subtitles, rudder / wire / fuel in the bottom bar, game option for World View supersampling (already read by the mod). Not seen in game yet with Desktop mode.

## Cleanup after dropping 0.8.x (2026-11-12)
* FixContactInfo removed (GameFixes.cs, the setting, the lang keys): 0.9.0 `BaseVehicleViewModel` reads `missionSessionTime` everywhere, there is no `missionElapsedTime` call left to redirect. Old saves keep an unused `FixContactInfo` line in the .cfg - harmless.
* GizmoHost (drawings doubled after the map window is closed and opened): the mod never patched it. In 0.9.0 `GizmoHost.Cleanup` nulls `_handle` (the defect noted above is fixed) and the map is rewritten (geometry store, no XAML shapes), so the 0.8.7 note is history; not re-tested in the game.
* AnchorChainEntry comment corrected: a mod unticked at game start is not started by the newer Anchor Chain.

## Mission timer draggable by itself (1.9.0, TimePanel.cs)
* The Border under the time controls ("Time Remaining", 0.9.0) is made hit-testable (`IsHitTestVisible=true`) and dragged like the panel. Place: `[Layouts] "Time Remaining"` (X from the left, Y from the top, width = the panel's). Not saved = it follows the panel (Canvas.Right = panel's, Canvas.Top = panel's top + height); the Canvas.Top binding is replaced by a local value. Double-click = back under the panel. Not in the Desktops list yet (only the plain layout entry).

## Text by background and the frame of transparent windows (1.9.0)
* `look[2]`: 0 off, 1 text contrast (saved as '-', '+'); the contrast includes the text-by-background shift (a separate 'Text by background' checkbox existed in a test build, '*' is read as '+'). The backdrop is the finished screen, not the 3D view (the 3D view is one window among others; works in menus too): SceneLum.cs - a coroutine at the end of the frame, every 0.5 s and only while a window asked in the last 3 s (`SceneLumAsk` from `ApplyLook`), calls (up to 10 times a second with the small capture, 4 with the full one - one at a time, the next starts when the readback is in) `ScreenCapture.CaptureScreenshotIntoRenderTexture` (reflection, UnityEngine.ScreenCaptureModule; the texture must be screen sized) into a texture, shrinks it to 64x36, AsyncGPUReadback (9 KB). The texture is 1/10 of the screen when Unity scales into it: the first captures take both a full-size and a 1/10 one and compare their 64x36 maps (mean difference < 0.06 and a picture that is not even) - then the full texture is freed (`_slMode` 1); otherwise the full one stays (2); see the Trace 'small capture'. When a map arrives, `AutoLookRefresh(display)` recolours at once the texts that the last window step collected (`LookState.AutoTexts`) for the windows on that display if their t flipped (`AutoStep`) - no tree walk, nothing when the picture is steady. A window averages the points under it (the capture includes the window itself and the windows above it; opaque dark window -> dark). Every display with a window that uses the option: display 0 in the end-of-frame coroutine, a monitor (Monitors.cs, `MonitorView.Cam`) in `Camera.onPostRender` of its camera (`SlPostRender`); the state is per display (`SlDisplay`, its own small/full mode check); a monitor without a picture yet counts as its flat clear colour. Row order: Unity copies the screen as it is in memory, so on Direct3D/Metal/Vulkan (`SystemInfo.graphicsUVStartsAtTop`) the first row is the top; `[General] BackdropRows` = auto / topFirst / bottomFirst overrides it. **Not tested in the game** - if texts of a window follow the wrong place, flip BackdropRows.
* `OutlineWalk(.., auto)`: with auto >= 0 the text colour = `ShiftText(original, t)`: grey texts start from the pure white / black of the contrast, then HSL lightness of a text above 0.3 moves toward 15 % of itself by t (hue, saturation kept; coloured texts too); darker texts stay. t = 0 or 1 only (no gradations: `AutoStep`, up at backdrop brightness 0.66, down at 0.58; was a steep smoothstep over 0.58..0.68 (white text with a black halo reads better than black text with a white one on a mid backdrop - e.g. an Event Log over the sea, ~0.4 - and the mid-grey texts of a slow transition read badly on either halo; the band 0.34..0.48 gave exactly that; 0.42..0.58 and a quarter of the lightness left the mid and bright backdrops with little contrast), applied at once (no easing in time). The halo stays one of two colours (white for dark text, black for light). Originals are kept in `_origFg`, restored when the option goes off. A new brush is made only when the colour moves by more than 3/255.
* Frame (`AddBorder`) of a window with opacity < 100 %: `FrameOpacity` = max(0.2, opacity / 2) on the frame element; set from `ApplyLook` and when the frame is added. Not tested in the game.

## Elements rewritten by other code (flicker, 2026-11-12)
* A mod that builds its UI in code and sets Foreground / Background on every refresh (AWC's Weapons tab) fought with the look: its colour came back after ours, so the text / background flipped between the two (the window step is once a second). Now a colour we set (`SetFg`, `_ourFg`) or a dimmed background (the Tag) that is found replaced by other code is counted (`_fgHits`, `_bgHits`); the second time the element is left alone for good (not state-driven ones: the game's triggers legitimately put the brush back). Such elements show their own colour / background undimmed. Not tested in the game.

## Release 2.0.0 (2026-11-12)
Version bumped in `AlternativeGameInterface.csproj`, `PluginVersion`, README.md, README.BBCode.txt. ChangeList.txt: new 2.0.0 section on top, the released 1.9.0 section restored as published (post-release items moved to 2.0.0); ChangeList-1000.txt rewritten for 2.0.0. Min game version 0.9.0 (`GreaterThanEqualToVersion`). Not committed until the user says so.

### Text contrast: state per TextBlock (2.0.0)
A wide window over a mixed backdrop (light sky + dark ship) had a mean near the thresholds and kept white text. Now each TextBlock samples the brightness under its own rect (`TextT`, `_txtT`, same 0.58/0.66 hysteresis); `SceneLumUnder` takes any FrameworkElement; the window-level `AutoT` is only the start value / fallback.

Halo of Text contrast: blur 2 -> 1 (denser, a blurred thin stroke was too faint); config `[General] TextHaloBlur` (0.5-4).

Halo of Text contrast, final: no blur - a hard pure white / black DropShadow (BlurRadius 0, ShadowDepth `TextHaloDepth`=1, dir 315, one-sided); recognised by blur 0 + that depth. A full all-round outline would need duplicate texts (Noesis has no stroke / second effect).

## Mod info on the loading screen (2.0.0, `src/LoadInfo.cs`)
`[General] LoadScreenInfo` (default on). A Harmony postfix on `LoadScreen.LoadText` (setter, called on every load step) adds one TextBlock to the root Grid of `SeapowerUI.LoadScreenView.Instance` (row 2, column 1 = under the progress bar; no hit test) with "Alternative Game Interface <PluginVersion>". Only this mod (the settings hub registry with the other mods was dropped on request). The cut is recomputed per contact 5 times a second at most (`ClipState.Tick`; `UpdateEllipse` is called every frame); `FieldV` caches the members. Not verified in game.

First loading screen (2026-11-12 trace): the plugin's Awake (Anchor Chain loads mods inside a long blocking step of the first load screen) runs ~3 s before the screen ends and the frame loop (Update) starts only ~0.3 s before the end (trace: Awake 17:09:10.07, first Update 12.94, load screen done 13.24); LoadText is not set in between. So the first screen is almost entirely the game's own (base picture, no line); nothing the mod does can change it. `HookLoadInfo` now calls the line / first-picture replacement once right at hook time (it shows for the last fraction of a second at best); later load screens (missions) get the line and the mods' pictures normally.
Anchor Chain Preloader (the latest, 9 KB, checked): `WaitAndLoad` waits for `StartupDone` = UnitMetadataProvider ready and `LoadScreen.LoadText == "Done"`, and only then loads the mods (`AnchorChainLoader.LoadPlugins`). So by design no mod exists while the first load screen is working; the screen's picture / text are the game's own. Not fixable from a mod; only the Anchor Chain author could load earlier.

## R.A.T.S. Keel (loader, 2026-11-12; SeaPowerKeel.dll 193 KB checked)
Keel replaces the Anchor Chain loader (stub in BepInEx/plugins) and starts both kinds of plugins of a dll: a `BaseUnityPlugin` (it does `host.AddComponent(type)` and puts the info into `Chainloader.PluginInfos`) and an `ACPlugin`+`IAnchorChainMod` (calls `TriggerEntryPoint`). Our dll has both (`AlternativeGameInterfacePlugin` and `AnchorChainEntry`): `AnchorChainEntry` already skips when the plugin exists; for the opposite order `Awake` now destroys a second instance (`_inst` guard). Keel also waits for `LoadScreen.LoadText == "Done"` before starting mods (globe build), so it does not help the first loading screen either. Keel starts only mods ticked in Mods (so `ModState.Controlled` false there is fine). Not tested with Keel in game.
**Keel test (2026-11-12, trace + Keel logs): bug found.** Keel starts the mods ~1.5 s after the first load screen has finished ("Start-up load screen done (waited 10.7 s)"), so the `ExecuteLoadScreen` postfix never saw its end: `LoadScreenDone` stayed false and the window hooks (`TryHook`) were never installed ("finished so far 0" forever). Fix: `StartupLoadScreenStep` (LoadInfo.cs, called from `Update`): `LoadScreen.LoadText == "Done"` for more than 1 s while no finished screen is counted = the first load screen is finished. Under Anchor Chain this does not fire early (the screen ends within ~0.3 s of the first Update). Keel report: AGI RUNNING via its Anchor Chain entry; the BepInEx plugin class of the same dll is SKIPPED by Keel itself ("the same DLL's Anchor Chain entry is the one that runs"), so the double-start guard in `Awake` is only a safety net. Keel's warning "looks up SeapowerUI.ViewModels by name" = a namespace string prefix, harmless.

### Keel: меню Alternative Mods
Под Keel корутина первого экрана загрузки уже завершена к старту модов, поэтому постфикс хаба `LoadScreenPostfix` не срабатывает и кнопка меню не ставится. `StartupLoadScreenStep` вызывает `SettingsHub.LoadScreenPostfix(false)` через reflection (SettingsHub.cs не правим). Если у ASE/ADM/AWC своя копия хаба и они стартуют позже — у них то же; каноническое исправление в ASE: в `Install()` проверять `LoadScreen.LoadText == "Done"` и сразу вызывать `DoInstall`.

### Slideshow под Keel
Хук `CurrentWindow` ставится после показа меню (моды стартуют позже), `_menuVm` оставался null и слайдшоу не работало. `MenuBackgroundStep` теперь находит модель по странице `MainMenuView.Instance` (вверх по визуальному дереву до DataContext типа `MenuSystemViewModel`); видео к этому моменту уже запущено — `DisableMenuVideo` его выключает при первом шаге.

### Keel: Backgrounds = Mods
Список картинок меню (`MenuSystemViewModel.imagePaths`) читается в `Awake`, до старта модов под Keel, поэтому постфикс `GetAllFiles` его не фильтровал. `FixMenuPictures` (из `MenuBackgroundStep`, один раз на модель) применяет тот же фильтр к уже готовому списку и при необходимости вызывает `ChangeBackground`. Модель находится по странице меню (см. Slideshow под Keel). Общее правило: всё, что хук ловит «при создании» (Awake/ctor) синглтонов и страниц главного меню, под Keel надо доделывать по уже существующим экземплярам.

### Keel: меню не кликается после Close в окне Keel
Окно Keel — IMGUI, закрывается по нажатию (Input System, в Update) → hotControl на отпускании уже мог быть ≠0 (или наоборот), и `NoesisGuiPrefix` отдавал Noesis нажатие, но не отпускание: захват мыши оставался. Теперь пара согласована: `_noesisDown` (нажатие ушло в интерфейс → отпускание/drag тоже всегда), `_withheld` (нажатие забрал IMGUI → отпускание не отдаётся). Гипотеза, без доступа к игре не проверена; без AGI залипания нет (проверка пользователя), со слайдшоу выключенным — есть.

### Keel: меню не кликается после Close — настоящая причина (trace build 1538)
`GUIUtility.hotControl` оставался 31 навсегда: окно Keel закрывается по нажатию (Input System, Update), кнопка IMGUI не получает MouseUp и не сбрасывает hot. `ResolveDown` считал `hot != 0` захватом IMGUI и не отдавал Noesis ни одного нажатия. Теперь запоминается `_hotBefore` (hot до обработки нажатия IMGUI); захват = hot изменился этим нажатием. Отпускание/drag не отдаются только если нажатие было забрано (`_withheld`), текущий hot для них не смотрим.

### Keel: StartEarly
Keel (новая версия, по тексту её изменений) умеет запускать мод до конца стартового экрана загрузки: `[Keel] StartEarly=true` в `_info.ini` (добавлено в `Release/_info.ini`; в приложенном архиве 0.5.0 ключа ещё нет, формат взят из описания обновления, не проверен по коду). Anchor Chain секцию игнорирует и ждёт «Done», как раньше. Обходы под поздний старт (`StartupLoadScreenStep`, `FixMenuPictures`, поиск модели меню, вызов `SettingsHub.LoadScreenPostfix`) оставлены: они срабатывают только если экран уже закончился. Проверить по trace под новым Keel: «patched LoadScreen.LoadText» и «loading screen info: line added» должны идти ДО «load screens 1».

### Хаб: подпись кнопки главного меню при раннем старте
`StartEarly` → AGI регистрируется в хабе один, хаб ставит кнопку с именем мода («Game Interface»), ASE/ADM/AWC регистрируются позже, подпись не обновляется. Обход: `HubLabelStep` (src/HubLabel.cs, раз в секунду, пока есть страница главного меню (остальные моды регистрируются позже)) ставит `SettingsHub.Label()` в кнопку `rgreatSettings`. Правильно — в каноническом хабе (ASE): `Register` обновляет подписи уже созданных кнопок (см. файл инструкций hub-label-refresh.md); тогда HubLabel.cs удалить.

### Keel: ложное предупреждение «SeapowerUI.ViewModels.»
Keel сканирует строковые литералы и считает префикс `"SeapowerUI.ViewModels."` (TaskForceUpgrade.UpStatic) именем типа, которого нет. Имя теперь собирается `string.Join`, литерала нет. Остальные предупреждения Keel по нашим модам: ADM `SeaPower.BaseObject` (в 0.9.x тип `ObjectBase`) — в чате ADM.

### Хаб: каноническая копия из ASE (eb6609e) — обход HubLabel удалён
`src/SettingsHub.cs` снова копия ASE без изменений: в ней `Install` сам ставит меню, если стартовый экран уже закончился (Keel), и `Register` обновляет подписи кнопок уже построенных меню (`RefreshLabels`). `HubLabel.cs` / `HubLabelStep` убраны. Вызов `SettingsHub.LoadScreenPostfix(false)` из `StartupLoadScreenStep` оставлен (защищён флагом `_installed`).

### Хаб: RefreshLabels и в Install
У каждой dll своя копия класса хаба (свои static). В `Register` у позднего мода `_game` ещё null (порядок в плагинах: Register, затем Install), и `RefreshLabels` выходил сразу: подпись кнопки оставалась «Game Interface». Теперь `RefreshLabels()` вызывается и после `_game = game` в `Install`. Файл — для канонической копии в ASE; в AGI лежит тот же.

### Мерцание текста на фоне (даже на паузе)
Картинка под окном живёт и на паузе (дым, вода, частицы), яркость под текстом колеблется у порога 0,58–0,66 и состояние текста переключалось каждый кадр. `TextT`: яркость сглаживается (EMA ≈ 0,3 с), новое состояние должно продержаться 0,4 с, первое измерение применяется сразу. Состояние окна (`AutoStep`) без изменений. Если мерцание осталось — это не порог, а второй источник (захват «small/full», перезапись цвета самим окном); смотреть trace `text by background`.

### Мерцание текста на паузе — настоящая причина: обратная связь
Захват — готовый экран, вместе с окнами. Яркость под текстом включает сам текст (глифы + гало ≈ 25 % его рамки): белый текст поднимал яркость выше порога → текст темнел → яркость падала → текст белел… с периодом захвата (полный захват 0,25 с = 4 Гц) даже на статичной картинке. Теперь `TextT` вычитает вклад текста: bg = (m − c·L_текста)/(1 − c), c = `TextCover` = 0,25, L — яркость текущего цвета. Сглаживание и задержка 0,4 с оставлены. Если мерцает ещё на краю — подбирать `TextCover` (больше при крупных глифах). Для окна целиком (`AutoStep`, фон окна) поправки нет.

## Encyclopedia Save / Load (src/MuseumSave.cs)
The Encyclopedia is the mission `missions/Demo/EncyclopediaMission.ini` (`Globals._inMuseum`); its pause menu is a short hand-made list (Resume, Options, Send debug, Exit) without Save/Load. Patch: prefix on `PauseMenuViewModel.Initialise(List<PauseMenuButton>)`; when `_inMuseum`, a throw-away PauseMenuViewModel runs `InitialiseMissionPause()` and its "SaveAs" / "Load" buttons (the game's own file pickers, `SaveLoadManager.SaveGame`, `Globals.OpenMission` + `UnloadMissionAndReload`) are inserted before Options. Folder: saves/missions (no LinearCampaign) = the main menu Scenarios load dialog. Guard `_museumBusy` stops the recursion (InitialiseMissionPause calls Initialise itself). Not verified in game yet.

## Encyclopedia / Encyclopedia 3D (src/EncyclopediaSplit.cs)
Main menu: `EncyclopediaSplitApply` (no polling: MainMenuView ctor postfix, the option's SettingChanged, LanguageResourceHandler.LanguageChanged, and once at hook time for a menu that is already up) finds the game's Encyclopedia button (first Button after the 2nd spacer TextBlock of the `scrollViewer` StackPanel), renames it "<game text> 3D" (loads EncyclopediaMission as before) and inserts our button before it with the game text (`mainmenuencyclopedia`) that opens `ObjectInformationViewModel(MainMenuView)` via `WindowHostCommands.OpenWindowCommand` (the main menu XAML has its own WindowHost, like the mission editor). Texts are set as strings (the binding is replaced), so they are set again on a language change. Not verified in game yet.

## Translations (rule)
Every new option / group / text gets entries in ALL Release/lang/*.ini (cn de en es fr ja ko ru vn), real translations, not English copies: `grp.<group>`, `lbl.<Section>/<Key>`, `help.<Section>/<Key>` (+ `unit.` when the option has a unit). Check with a key diff against en.ini before building.

## Errors when loading a save (FixSoundOnReload, src/MuseumSave.cs)
Loading a save from a mission (pause menu): while the mission is unloaded `PlaySoundDistanceBased.Update` (objects still alive) calls `CameraManager.getCameraUnityPosition` with no camera and throws NullReferenceException every frame; the game shows an error box for each (100+ boxes in the log, the load itself succeeds). Not caused by AGI (no AGI code touches game cameras; the backdrop camera is our own). Harmony finalizer on `PlaySoundDistanceBased.Update` swallows only that NRE (stack contains getCameraUnityPosition). Option "No errors when loading a save" (In Mission), default on.

## Release 2.1.0 (2026-11-13)
Version bumped in `AlternativeGameInterface.csproj`, `PluginVersion`, README.md, README.BBCode.txt (7756 bytes, 82 lines), ChangeList.txt, ChangeList-1000.txt. ChangeList 2.0.0 section restored to the published text; the Keel and loading-screen-line items moved to 2.1.0. New GUI options: Game Menus / Main menu / "Encyclopedia and Encyclopedia 3D" (default on); In Mission / "No errors when loading a save" (default on). Encyclopedia Save/Load has no option.

## Ghost window (0 % opacity, src/WindowLook.cs)
Opacity step 0 %: `ApplyLook` computes `ghost` (opacity 0, mouse not over the window, look menu closed, not a bottom window): title strip factor 0, frame (`Borders`) opacity 0, and the header grid (parent of HeaderThumb, only if <= 48 units high) Opacity 0. Hover is event-driven: MouseEnter / MouseLeave of the window element (hooked once, `LookState.HoverHooked`) re-run ApplyLook. The hidden header still takes the mouse (opacity, not visibility).

## Release 2.2.0 (2026-11-15; 2.1.1 (Unit Reference tree button) was published earlier)
Version bumped in `AlternativeGameInterface.csproj`, `PluginVersion`, README.md, README.BBCode.txt (7674 bytes; the text of the Steam page is the base: the page and its change notes are read with the Steam API `ISteamRemoteStorage/GetPublishedFileDetails` (item 3813683442) and `.../changelog/3813683442?p=1,2` from the shell), ChangeList.txt, ChangeList-1000.txt (2.1.1 only).
* Loader: R.A.T.S. Keel was removed from Steam (it may be restored); README / `_info.ini` stay as in 2.1.0 (Keel recommended, Anchor Chain alternative; AC install is described in the AC documentation). Anchor Chain 1.2.0 needs the separate `AnchorChain.Preloader.dll` (inside `ACPreloader.zip` of release V1.2.0, goes to `BepInEx\plugins`); `AnchorChain.dll` must NOT be in `BepInEx\plugins` too: the preloader loads the Workshop copy with `Assembly.LoadFile`, a second copy in plugins has other type identity, and no mod entry point (`IAnchorChainMod`) is found (log: "Loaded assembly ..." but no "Loaded plugin ..."). `AnchorChain.dll` of V1.2.0 = the one of `dev.zip`.
* Unit Reference "<" / ">" (`FindTree`): a window opened with the tree hidden never loads the TreeView (collapsed), so `TreeViewOf` finds nothing and the toggle ended with "window not found" (no resize). Now: tracked window whose `InfoViewModel` is the view model + `UnitTreeBrowserView` looked up in it once; the pair (window, tree view) is remembered per view model (`TreeOfVm`) for later toggles. Trace on failure: "tree toggle: no window (tracked N, tree view found/not found)". Verified in the trace of build 20261114.1207 (window from the main menu and from a mission).
* Game 0.9.2 (0.9.2-261113b): no incompatibilities found (crew skill rule, `Waves` enum rename, XAML of Port Status / Task Force Builder / Debrief / Options do not touch the mod).
* `build.sh`: `zip -x` goes after all paths (before, only `Release` was packed).

### Save/Load folder memory (2.2.0)
`src/Pickers.cs`: option `General/RememberSaveFolder`. `FilePickerViewModel.Initialise` prefix clears a "ready" flag, postfix restores the stored folder (pickers with extension `sav`, home folder named `saves`; initial folder `campaigns` -> `LastSaveFolderCampaigns`, otherwise `LastSaveFolderMissions`) and sets the flag. Postfix on the `SelectedFolder` setter stores the folder only while the flag is set, so the initial value set by the game is not recorded. Folder must exist and lie under the picker's home.

### World View picture opacity (2.2.0)
Cause of the difference from the no-GUI render: the camera texture has an alpha channel; blended effects (sun glow, exhaust) leave alpha < 1 there, the screen has none, Noesis blended those pixels with the backdrop. `MapRtResolve.OnPostRender` (after the last camera) now draws a full-target quad with `Hidden/Internal-Colored` (Blend One One, colour 0,0,0,1) so alpha becomes 1; it runs in the non-supersampled case too.

### Snapshot (2.2.0, `src/Snapshot.cs`)
[Debug] Snapshot (button in the Debug tab, switches itself off) appends frame time, GC/Unity memory, Unity objects and MonoBehaviours by type, particles, log error counters and the mod's own collections (static + plugin fields that are `ICollection`) to `BepInEx/AlternativeGameInterface_snapshots.txt`; every figure carries its change since the previous snapshot. Use: one before saving and one after loading (what a load resets accumulated in the mission; what stays is process-level or in the save). The file is deleted at every game start.

Snapshot also lists, for ParticleSystem / Light / AudioSource / SphereCollider / TrailRenderer, the active / playing counts and the top names and root names, and the live managed heap after `GC.Collect()`. First real result (2026-11-15, 40 min mission): a save/load brought objects back from 71k to 56k (ParticleSystem 3.9k -> 1.2k, Light 356 -> 162, AudioSource 1.27k -> 672) and the frame time from 23.7 to 15.9 ms, while the managed heap stayed at 1.15 GB (834 MB at the first load) and the tracked Noesis windows did not shrink (482 -> 570): scene objects pile up in the game, the managed growth survives the load.
Settings window: field kind 'a' = a one-shot action button (ActionEditor sets the self-clearing config entry at once; no default / reset column). Used by Snapshot and DumpXaml.

### Create Mod window fill (`src/ModCreate.cs`, [General] ModCreateFill)
Postfix on `CreateModMenuViewModel.SelectedUserCreatedMod` setter (SeapowerUI.ViewModels, internal): with `UpdateExisting` and an item chosen, the mod's local folder is looked up in the top-level folders of StreamingAssets (`_info.ini` present; folder name equals the item title without spaces, or a `Name=` of a `[Language_*]` section equals the title) and set as `FolderPath`; the picture is the first of thumbnail.jpg / .jpeg / .png, preview.jpg / .png up to the game's 1,000,000 byte limit, loaded through `ImageConversion.LoadImage` into `PreviewImage` (a `Noesis.TextureSource`) and its path into the private `previewImagePath`. Not in a changelog yet: version to be decided.
Picture fallback: any .jpg/.jpeg/.png of the folder that is 512x512 and <= 1 MB.

## Campaign: ship state on briefing / newspaper events (Campaign/EventShipState, src/CampaignEvents.cs)
Symptom: with an event (FreeEvent: newspaper, briefing, e.g. Mission6 "after AR02" in the Argentina campaign) selected on the campaign map, the Task Force shows ships with full ammunition and no damage, as before the first mission. The save file itself is correct (PostState of the last mission is there).
Cause (decompile 0.9.2): the view model passes `(_selectedMission as LinearCampaignMission)?._index ?? 0` (an event is not a LinearCampaignMission) to `LinearCampaignTaskForceSaveManager.FindLatestPriorUnitStateSection`; its loop starts at `Math.Max(1, target - 1)`, so for 0 only Mission 1 is checked. `LinearCampaignTaskForceStatusViewModel.LoadFromSnapshot` keeps the previous `_targetMissionIndex` when 0 is passed (stale index: state from before the previously selected mission).
Fix: Harmony prefixes on FindLatestPriorUnitStateSection and LoadFromSnapshot: an index <= 0 becomes (highest N of TaskForceMode_Mission{N}_PostState_/_LaunchState_) + 1. Not verified in game yet.

## Unit Status header: class (first 2.3.0 build; superseded by the section "window title and label" below)
The red label at the right of the tab strip is `UnitStatusViewModel.UnitName` (the game sets it to `DisplayNameShort` in SwitchUnit). FullUnitName already replaced it with DisplayNameFull (FullUnitName in StatusReference.cs, also after Replace / ChangeVariant); The two switches were merged into one list UnitStatus/HeaderName = Class (default) | Full | Short (the old FullUnitName / ClassInHeader entries are dropped from the config). Class uses `ObjectBase.ClassNameShort` (`_obp._className`, the part after the comma of the "Default" name in vessel_names), then ClassNameFull, then the full / short name. The cut is recomputed per contact 5 times a second at most (`ClipState.Tick`; `UpdateEllipse` is called every frame); `FieldV` caches the members. Not verified in game.

## Release 2.3.1 (minor)
2.3.0 is published. 2.3.1 = only the option `General/DoubleClickOpen` (default off): Pickers.cs `PickerDoubleClickPostfix` (postfix of the FilePickerViewModel select command lambda, OpenFile pickers: runs ButtonCommand) and ViewCapture.cs `OnTreeItemDoubleClick` (scenario TreeView of the main menu: runs ScenarioMenuViewModel.PlayCommand). Version bumped in csproj, PluginVersion, READMEs, ChangeList.txt (new 2.3.1 section on top), ChangeList-1000.txt (2.3.1 only).

## Release 2.3.0 (published)
Release docs prepared 2026-11-16: 2.2.0 (and 2.2.0 b) is PUBLISHED on Steam (World View transparency, Save/Load folder, Create Mod fill, campaign event ship state), so ChangeList-1000.txt lists only 2.3.0 changes. README.md was 8219 bytes (limit 8192, trim), README.BBCode.txt 7862 bytes / 79 lines. Keel is kept in the docs second, marked "temporarily unavailable".
Version bumped in `AlternativeGameInterface.csproj`, `PluginVersion`, README.md, README.BBCode.txt, ChangeList.txt, ChangeList-1000.txt (2.3.0 only). 2.2.0 (World View opacity, Save/Load folder memory, Create Mod fill, campaign event ship state) is built but not published yet; 2.3.0 adds the Unit Status window title / label name lists (UnitStatus/WindowTitleName, UnitStatus/UnitLabelName).

## Unit Status: window title and label (2.3.0, StatusReference.cs `UnitText` / `FullUnitName` / `StatusTitleStep`)
Both texts are bound to `UnitStatusViewModel.UnitName` (MainGameView.xaml: the window Header is `<Run WindowHeader/> <Run UnitName/>`; UnitStatusView.xaml: the label at the right of the tabs is `Text="{Binding UnitName}"`), so they are decoupled: the label keeps the VM property (set after SwitchUnit, `UnitLabelName`, default TypeClass), the second Run of the window Header gets its own local text every frame (`StatusTitleStep`, `WindowTitleName`, default FullName; a local value replaces the binding). Five forms: ShortName (DisplayNameShort, the game's default for both), FullName (DisplayNameFull), ShortClass (ClassNameShort), FullClass (ClassNameFull = `_obp._defaultName`), TypeClass (`_obp._typeFullName` + FullClass); an empty form falls back to the short name. The HeaderName entry of the first 2.3.0 build is dropped. The cut is recomputed per contact 5 times a second at most (`ClipState.Tick`; `UpdateEllipse` is called every frame); `FieldV` caches the members. Not verified in game.
The Header TextBlock has three inlines: Run(WindowHeader), a Run of the space between the two Runs in the XAML, Run(UnitName): StatusTitleStep sets the LAST Run (the first version took the 2nd, the space, and the title showed both texts glued together). In the lists the ShortName value is marked "(game default)" (the game shows DisplayNameShort in both places).

## Track State layout (2.3.0, TrackState.cs `TrackXaml2`)

Option `UnitStatus/TrackLayout` (default on; settings tab "Track State" together with `TrackState`): off = the old `TrackXaml` (the game's templates) is used. Read when the tab view is built, so it applies to the next selected contact.
Option `UnitStatus/TrackIdentProgress` (default on; new layout only): row LblProg/TxtProg under Identification, hidden when nothing to show. `TrackIdentProgress`: Vehicle.Class set -> 100%; else the max of `_observationTime / _identificationTime` over `_esmObservationData` of all `_obp._esmSystems` (`_esm`) of the player taskforce units (`Globals._playerTaskforce._taskforce*` lists) whose key `_baseObject` is `Vehicle.Object`; read twice a second, rounded to tens; Class set -> 2 -> row hidden; ESM ratio >= 0.95 -> text "not enough data" (`track.identNoData`; the ESM only gives the signature bit, the contact can stay "Detected" with several candidates left). Not verified in game (the game itself has no such field in VehicleInfo; same numbers as its debug ESM panel).
The game's VehicleInfo body (DockPanel of the StateComponents: BaseVehicleViewModel in 4 equal columns / 3 equal rows, SignaturesViewModel below) wastes space and lets "Detected By" push the table away. `TrackXaml2`: row 0 facts (State / Identification, Last / First detection age) in two compact label+value pairs per row; row 1 "Detected By" caption + ScrollViewer `DetBox` (cards copied from VehicleInfo.xaml, WrapPanel) whose MaxHeight is set on SizeChanged to 36 % of the body (min 64); row 2 `ContentControl Content={Binding SignatureInfo}` (the game's own template with its generated columns and its own scroll) takes the rest; row 3 Reset TMA. Labels come from code (`TrackLabel`, the game's language keys). `VehicleInfoViewModel.BaseInfo` / `SignatureInfo` are computed properties over StateComponents, which is filled once in the constructor. If ParseXaml of TrackXaml2 fails (a missing StaticResource), the trace says so and the old `TrackXaml` is used. The cut is recomputed per contact 5 times a second at most (`ClipState.Tick`; `UpdateEllipse` is called every frame); `FieldV` caches the members. Not verified in game.

## Settings: Remembered Windows (2.3.0)

No longer a top-level tab: `TRemembered` fields are shown on page 1 of the "Desktop mode" tab (`MapSubTabs()` returns 0 Settings / 1 Remembered Windows / 2 Desktops; Desktops only while desktop mode is on). The page works without desktop mode too. The tab dot of "Desktop mode" covers changes of all three pages.

## Contact estimate squeeze (2.3.0, `[Map] ClipContactEllipse`, `src/ContactEllipse.cs`)
Problem: the TMA error ellipse of a bearing-only contact may reach behind the sensor. How the game works: `Vehicle.DetectedPosition` / `ExtrapolatedPosition` = `(GeoPosition, PositionEigpairs, Entity)?` are copied from the ECS (`DetectedPosition` / `EstimatedPosition` components or `VehicleSnapshot`) in `TransferFromSnapshot` / `TransferFromComponents` on each update; `PositionEstimate` picks one of them; `Position.Value = PositionEstimate.Item1` is the icon (`MapUnitViewModel.Location`, updated at random with p = 1/15 for contacts - the jumps) and the base of the range in the label (`GetDistance(selected, Location)`); `ErrorEllipseViewModel.UpdateEllipse` draws the ellipse (K^0.5 sigma, `TMAContext.KField`, SharedStatic<float>) from the same estimate. Tried: clipped polyline (heavy), changing the ellipse geometry only (the icon and range stayed). Now: postfix of both Transfer methods changes the copy in the Vehicle (not the ECS data: nothing accumulates): the sensor / bearing from `TryGetLatestBearingTrack` (private; looked up 5 times a second per contact; `BearingDeg` is the game's grid yaw, same frame as `MajorAxis` x,y), the covariance S Sigma S' with S = I + (k-1)uu' (k = (d0+h)/(2h): the extent along the bearing becomes [0, d0+h]), new eigenpair (axes by atan2, std devs), the centre moved along the true heading by (h-d0)/2 m. Not for a fresh precise track, not when nothing is behind, not when everything is (stale bearing), only `IsUiTable` (the player's table; the AI tables keep their estimates). `UnsafeDataPointer` of the SharedStatic is read for K (csproj: AllowUnsafeBlocks), 5.99 if it fails (trace line "contact estimate: K = ..."). Not verified in game; note it changes what weapons / the range readout use for the contact (KnownUnityPosition) - by the user's decision.

## Compact sonobuoys (2.3.0, `[Map] CompactSonobuoys`, `SonobuoysThreshold` 20, `src/SonobuoyDeclutter.cs`)
The map icon of a unit is the Canvas `UnitIcon` of `UnitOverlay.xaml` (DataContext `MapUnitViewModel`, `VisualType` PassiveSonobouy / ActiveSonobouy; RenderTransform = Translate(-11.25,-11.25)); the text is drawn by the label solver (`MapLabelLayer`) from `ContactInfoLine1..4` of the same view model. `SonobuoyStep` (per tracked map window, 2 times a second): walks the visual tree, collects the sonobuoy icons and the `MapBase`, counts the visible ones (IsVisible, `PointToScreen` inside the map rectangle); crowded when > threshold (off again below 90%); then per icon: RenderTransform = Group(Scale 0.5 about 11.25,11.25 + the original), the VM is flagged and the getters of `ContactInfoLine1..4` (postfix) return "" while flagged; `OnPropertyChanged` is raised for the four names when the flag changes and `Label.BumpContentVersion()` is called (the label solver re-measures a label only when its ContentVersion changes; the game setters do the same) — without it the text came back late and not for all. The original transform is kept in `SonoIconState` and put back. Not verified in game: the label solver may keep the space of the empty lines; the Canvas name `UnitIcon` / VisualType names are from the 0.9.2 XAML.

## Анализ быстродействия (2.3.0, по коду, без замеров в игре; код не менялся)
Профайлера в игре не было: оценки по числу вызовов. Замерить можно существующим снимком (опция Snapshot: frame ms) с включённой и выключенной опцией.
Покадровое (Update, каждый отслеживаемый `Tracked`, в т.ч. закрытые окна — список `_tracked` не чистится, пока окно живо):
1. `StatusTitleStep`, `TabTrackStep`, `StatusRefStep`, `TrackStateStep` — каждый кадр на каждое окно Unit Status, без троттлинга. Рефлексия `Get/GetB` (боксинг), `foreach` по `tb.Inlines`/`Tabs` (аллокация энумератора), `UnitText` (строка каждый кадр + 1–3 рефлексии), `TabKey/SelectedKind` (Kind боксится). Решение: проверять только при смене юнита/вкладки (SwitchUnit/Refresh постфиксы уже есть) либо раз в 0.1–0.25 с; сравнивать Header-текст, не пересобирая строку.
2. Покадровые шаги вне `t.WasActive`: `TrackOpenState`, `FitMaximized` (GetB "Maximized" + CWT), `PinStep`, `LookStep`, `MapModeStep`, `EventLogStep`, `PickerStep` — на закрытых окнах впустую. `FitMaximized`/`LookStep`/`PickerStep` можно пропускать при `!t.WasActive && !fs.On`.
3. `Step` каждый кадр: `IsOnScreen` = `GetB("IsVisible")` + `VtParent` (рефлексия + try/catch на каждое окно). Кэшировать делегаты (`Func<object,bool>` через `CreateDelegate`) вместо `PropertyInfo.GetValue`.
4. `IsWindowOf` (IsMapWindow/IsWorldViewWindow): на каждый вызов `new[]{h,ch}`, `new[]{"WorldView"}`, лямбда, `Squash(t)` (строки). Вызывается из SonobuoyStep (раз в 5 с у не-карт), MapModeStep, KeepMapViewStep; кэшировать результат на окно (CWT<bool?>) после того, как Header/DataContext заданы.
Периодическое:
5. `SonobuoyStep` (мой, 2.3.0): полный обход визуального дерева окна карты (бюджет 80 000 узлов) 2 раза в секунду + `new List/Stack`. Это самый дорогой периодический шаг мода при включённой опции (даже без единого сонобуя). Решение: ходить по дереву раз в 2 с (или по смене числа юнитов), в промежутках считать по кэшу `WeakReference<Canvas>`; либо найти контейнер иконок (общий предок) и ходить только по нему; пропускать вообще, если в миссии нет сонобуёв (счётчик по `VisualType` в постфиксе).
6. `SonoLinePostfix`: на каждый вызов геттера ContactInfoLine1–4 (карта, MapTrack.Sample, label solver — для каждого юнита) один поиск в `ConditionalWeakTable`. Быстрый путь: статический счётчик «сколько VM сейчас с Hide»; 0 → return без поиска.
Патчи на частые методы игры:
7. `NoesisGuiPrefix` (IMGUI-события, несколько раз за кадр): `Convert.ToInt32(_evType.GetValue(ev))` — рефлексия+боксинг на каждое событие; заменить скомпилированным делегатом.
8. `VehicleTransferPostfix` (ContactEllipse): на каждый перенос состояния контакта 2× `FieldInfo.GetValue` + `PropertyInfo.GetValue(IsUi)` ещё до троттлинга; порядок: сначала дешёвый `EstSensors`/Tick (200 мс), потом рефлексия; `Squeeze` аллоцирует `object[]`/`double2` — делегаты/кэш полей.
9. `MapMouseStatePostfix`, `MapRemapPrefix`, `MapSetRectsPrefix` — каждый кадр, но только при `MapOn()`; дёшево, `MouseDisplay()` вызывается дважды в одной строке — можно один раз.
Не трогать (уже ок): `SourceFilter/ArmedFilter` (CWT, Done-флаги), `MatchObjectPostfix` (кэш lower-case текста), `CompassTapeStep` (0.4 с), `DockReachStep` (0.12 с), `MonitorDupesStep` (0.5 с), `MapPipStep` (ранний выход без опции), `Trace` (только при `_debug`).
Ожидаемый выигрыш: заметен только при многих открытых окнах Unit Status и большой карте; п.5 и п.1 — первые кандидаты.

Сделано по этому анализу (2.3.0, не проверено в игре):
- `src/FastGet.cs`: `FastGet.Property/StaticProperty/Field` — делегаты вместо `PropertyInfo.GetValue` (generic-обёртка `Func<T,R>`; поле — DynamicMethod). `Get()` теперь кэширует `Func<object,object>` на (тип, имя); при неудаче — прежняя рефлексия. Это ускоряет все `Get/GetB/GetF` разом (п.1, 3). `IsOnScreen` — напрямую через `Noesis.UIElement`.
- п.1: `StatusTitleStep/TabTrackStep/StatusRefStep/TrackStateStep` — раз в 0.05 с на окно (`Tracked.UsNext`), не каждый кадр. п.2: `PickerStep` только для активных окон, `FitMaximized` — для активных/настроенных (закрытое окно подгонится при открытии).
- п.4: `IsWindowOf` без аллокаций (кэш `Squash`, статические массивы), положительный результат запоминается на окно отдельно для карты и World View (`WinOfMapYes/WinOfWvYes`).
- п.5: `SonobuoyStep` ходит по дереву раз в 2 с (`SonoWalk`, значки хранятся как `WeakReference`), счёт видимых — каждые 0.5 с по кэшу; новые сонобуи получают компактный вид с задержкой до 2 с. п.6: `_sonoHidden` — пока ни один юнит не скрыт, геттеры подписи не ищут флаг.
- п.7: `Event.type` / `Event.current` в `NoesisGuiPrefix` — делегаты. п.8: `VehicleTransferPostfix` читает `DetectedPosition/ExtrapolatedPosition/IsUiTable` делегатами. п.9: `MouseDisplay()` в `MapMouseStatePostfix` один раз.
- Если рефлексия-делегаты вдруг не создаются (другой рантайм) — молча остаётся старый путь; замерить можно снимком (frame ms).

## Аудит утечек (2.3.0, по коду, не проверено в игре)
Найдено и исправлено: (1) `_backdrop` (MapWindows): `Texture2D` + `Sprite` фоновой карты не освобождались при удалении GameObject (ассеты) — теперь `FreeBackdropAssets()` при снятии и перед пересозданием; (2) `TreeTweaks.LoadoutDataDone` был `HashSet<object>` со словарями загрузок всех юнитов (держал данные энциклопедии навсегда) — теперь ConditionalWeakTable; (3) ModCreate: текстура картинки Workshop не уничтожалась при смене выбора (`_mcTex`); (4) `Created` (WeakReference на каждое окно) только рос — раз в 30 с мёртвые убираются, когда все уже в `_tracked`; (5) `TraceOnce` — предел 300 ключей.
Проверено, утечки нет: статические списки WeakReference (`_probes`, `_dcViews`, `_borderOwners`, `Statuses`, `_redimQueue`, `_pauseButtons` — чистятся/ограничены), `_pendingDown` (очищается), `SettingsWindow` статические списки (пересоздаются, `Fields` один раз), обработчики событий на объектах, которые сам же и создаём (замыкание ссылается на тот же объект — цикл, сборщик собирает; `LayoutUpdated` отписывается), RenderTexture (`FreeRt`, `SlDrop`), камеры мониторов (`Destroy`), CWT-таблицы (ключ — окно/юнит/контакт, слабые). Остаётся по мелочи: `_getters` / `SquashCache` / `FieldVCache` — ограничены числом типов/имён; `_sonoHidden` может остаться >0 после уничтожения скрытых юнитов (только замедляет геттеры, не течёт).
`_sonoHidden` теперь обнуляется, когда ни одно окно карты не в компактном состоянии (`_sonoCrowdWins`): флаги ушедших юнитов больше не держат медленный путь геттеров. Если окно закрыли в компактном состоянии, счёт окон не падает до нуля (только замедление, как раньше).

## Профайлер мода (2.3.0, `src/Profile.cs`)
Работает только при `Debug = true`: `Prof.T()` / `Prof.Lap(id, t0)` (Stopwatch) вокруг шагов Update (по окнам: Step, TrackOpenState, Fit, Pin, Look, MapMode, KeepMap, EventLog, DcFit, Picker, Sonobuoy, Unit Status, ResizeCursor, DockReach, Upgrade, LoadoutOrder), MonitorsStep, остальных шагов, LateUpdate (MapPipStep) и патчей на методы игры (MouseControlState, NoesisView.OnGUI, перенос состояния контакта, PIP render, remap мыши, SetRects, PIP blit). Раз в 10 с в trace строка `profile:` — средний кадр (мс, fps, худший) и мс на кадр по разделам. Это только CPU мода; всё, что мод заставляет делать GPU (текстура World View с сверхсэмплингом, камера подложки, текст-эффекты), в цифрах не видно: если кадр длинный, а разделы дают мало — цена в рендере.
Там же (только Debug): строки `hitch:` на кадры > 25 мс с разбивкой по фазам кадра (метки `Prof.Ring`: первый Update → Update мода → последний LateUpdate → конец рендера → начало следующего кадра; `FrameStamp.cs`, `PhaseLoop`), трейс пишется фоновым потоком. Вывод разбора 2.3.0: секундные просадки в миссии дают игра (шаг симуляции раз в секунду; без модов то же) и, частично, захват экрана «текста по фону» на паузе; Keel-окна в меню давали отдельный 1 с хитч. При `Debug = false` (по умолчанию) всё это не работает: ни меток, ни компонентов, ни потока трейса.
Найдено по trace 03:17 (2.3.0 build 20261115.2344): (1) при загрузке миссии `MapPipStep` включал «фото-режим» (камеры на экран), а `BackdropStep` на следующем кадре откатывал (`!want`) — по кадру туда-сюда, 226 раз (+ запись в trace и смена `targetTexture` всех камер); исправлено: `MapPipStep` ничего не делает, пока нет миссии (`MapMissionOn()`). (2) в 03:17:29–32 окна Event Log / Tactical Display / Unit Status «загружаются» ~40 раз в секунду (123 раза Event Log, 126 раз Tactical Display) — драка восстановления открытых окон (`RestoreOpenStates` каждый кадр фазы, `OsEnforce`) с игрой; не исправлено, нужен trace с подробностями (какое окно кто закрывает).

Просадка раз в секунду (2.3.0): `LookStep` вызывал `ApplyLook` раз в секунду (`st.Next = now + 1f`) для каждого окна с нестандартным look (у пользователя Event Log и News: opacity 0, scale 1.2, контраст `+`) — 4 обхода дерева (HasRender, SolveFactor, DimWalk, OutlineWalk, до 6000 узлов каждый) + `SceneLumAsk` (держит запущенным захват экрана 10 раз/с для «текст по фону»), в том числе для закрытых окон. Теперь: `LookStep` только для активных окон; полный `ApplyLook` — если `LayoutUpdated` панели сработал с прошлого обхода (`LookState.Dirty`) или раз в 10 с (`Full`); раз в секунду без обхода только `SceneLumAsk(st.AutoDisp)`, чтобы захват не затухал (3 с). Цвет уже найденных текстов обновляет `AutoLookRefresh` без обхода. Захват экрана для «текст по фону» (ScreenCapture.CaptureScreenshotIntoRenderTexture + 2 blit + AsyncGPUReadback, до 10 раз/с) остаётся самой дорогой частью по GPU при любом окне с контрастом `+`: если нужен FPS — снять `+` (`-`) у Event Log / News.

Trace пользователя 03:29 (build 20261116.0027, главное меню, окно News с контрастом `+`): профайл — код мода 0.035–0.12 мс/кадр из 9.3 (107 fps), то есть шаги мода не причина; «worst» кадры ~100 мс раз в 10 с совпадали с записью строки profile (File.AppendAllText на потоке игры, антивирус) — теперь Trace пишет отдельный поток (очередь, сброс раз в 0.3 с и в OnDestroy; последние 0.3 с при вылете могут не дойти). `text by background: small capture differs - the full capture is used` — полный захват 2560x1440 (режим 2): шаг теперь 0.5 с (режим 1 — 0.2 с). `ResizeCursorStep` в меню каждую секунду делал `Noesis.GUI.SetCursorCallback` — теперь при смене экрана и раз в 10 с. Профайл теперь показывает worst-вызов каждого раздела и число сборок мусора (gen0/1/2) за интервал: раз в секунду gen0 = подозрение на GC от аллокаций.

## Лимит FPS (FrameLimit.cs, OnSliderLoaded в ViewCapture.cs)
Лимит — штатная опция игры (VideoSettings/TargetFrameRate в options.ini, слайдер `sliderTargetFrameRate` в OptionsMenuView.xaml: 15..120, шаг 5). Мод своего значения не хранит: `General/FrameRateOptionMax` (250, только в cfg, в окне настроек нет) поднимает `Maximum` слайдера при его Loaded (при ≥ 1000 расширяет рамку с числом). Слайдер при привязке уже обрезал сохранённое значение до 120 (TwoWay) — после смены Maximum значение возвращается из `GameFps()` (то, что игра задала в `Application.targetFrameRate` или мод забрал в `_gameFps`). Точный лимитер (настройки нет, всегда при `vSyncCount == 0`): `FpsLimitStep` раз в секунду забирает `Application.targetFrameRate > 0` в `_gameFps`, ставит -1 и держит темп корутиной `Pacer()` (Stopwatch, Sleep(1) до 2 мс, затем ожидание вхолостую; `timeBeginPeriod(1)`); при vSync != 0 не работает. «Без лимита» — не значение игры (слайдер коэрсит -1 до 15), а `General/NoFrameLimit`: под слайдером добавляется CheckBox (`FpsBoxes`, вставка после строки слайдера), он отключает слайдер (`IsEnabled=false`); `FpsLimitStep` и `Pacer()` при флаге ставят движку -1 и не ждут, значение игры остаётся в `_gameFps`.

### Adaptive V-Sync (FrameLimit.cs `AdaptiveVSyncStep`, ViewCapture.cs `OnComboLoaded`)
В список V-Sync видео-настроек (ComboBox с ItemsSource `VsyncModes`, `List<Tuple<int,string>>`) при Loaded добавляется пункт `Tuple(1, "Adaptive V-Sync")` — значение 1, как у «Every»: игра сохраняет в options.ini обычный V-Sync, без мода или при его отключении получается штатный режим (значение 3 писать нельзя: игра выставила бы `vSyncCount = 3`). Пункт добавляется при `General/AddAdaptiveVSync` (вкл, есть в окне настроек); что выбран именно он — `General/AdaptiveVSync` (по SelectionChanged; список не INotifyCollectionChanged — ItemsSource переприсваивается, выбор восстанавливается). Режим активен при флаге и сохранённом `QualitySettings/VSync == 1` (читается через рефлексию из OptionsManager.OptionsIni раз в секунду). Каждый кадр: `dt > 1.5 × период экрана` — «поздний»; окно 0.5 с, ≥ 25% поздних → `vSyncCount = 0` (`_avOff`; точный лимитер при этом без лимита: кадры измеряют работу); `_avOff` и ни одного кадра длиннее 0.85 периода 3 с → снова 1. Если лимит игры ниже частоты экрана — режим ничего не меняет. `ReleaseFrameControl()` при отключении мода в меню Mods: возвращает лимит игры и `vSyncCount` из ini.

### Anchor Chain: StartEarly
`Release/_info.ini` has both `[Keel] StartEarly=true` and `[AnchorChain] StartEarly=true`. The Anchor Chain side needs the change of issue #32 (fork rgreat/AnchorChain, branch early-start) and the stub preloader 2.0.0 of PR #24; with the stock AnchorChain 1.2.0 the key is ignored and the mod starts as before.

### Debug: Screenshot all tabs (SettingsShots.cs)
[Debug] ScreenshotTabs (button in the Debug tab, switches itself off): with the settings window open, shows every tab (as with "Show all settings", all 3 pages of the Desktop mode tab) and saves the options window alone (rect of `_root` via PointToScreen, `ScreenCapture.CaptureScreenshotAsTexture` at end of frame, cropped) as PNG to `BepInEx/AlternativeGameInterface_screens/NN_Tab.png`; the folder PNGs are cleared at each run; the window state is restored after.

### Campaign: KeepReserveSubmarines (ReserveSubmarines.cs)
Game bug: a submarine waiting for service (Rearm/Repair order) sits in the `Reserve` force. `CreateManagementSnapshot` (what Status/Builder show) leaves out `IsCampaignSupportUnit` items (aircraft, helicopters, SUBMARINES) of the selected force and takes submarines only from `Submarine-...` forces, so the Reserve submarine is invisible in the windows, the return dialog never lists it, and when the Reserve is the selected force any roster change (`UpdateSelectedStandardTaskForceSnapshot` -> `ReplaceSnapshot(items without support units)` -> persist) drops it from the save; only the service order stays. Seen in the 18-save series (M-03-1 -> M-03-2, and M-03-1 -> buying a ship). Not caused by UpgradeShip (the upgrade itself is fine: trace `roster before/after sale/after confirm`).
Fix: Harmony prefix on `LinearCampaignStandardTaskForceEntry.ReplaceSnapshot` - for the `Reserve` entry the submarines of the old snapshot that the new one lacks are appended (new snapshot built by reflection). Config `[Campaign] KeepReserveSubmarines` (true). Trace: "KeepReserveSubmarines: the Reserve keeps ...". Not done: showing the Reserve submarine in the windows / the return dialog (game behaviour).
ShowReserveSubmarines (same file): the Reserve card lists only `row.IsShip` rows (`RefreshManagerRoster`), a submarine is a row only through its own `Submarine-` force; the Submarine tab (`HasStandardSubmarineCategory`) needs a submarine in the management snapshot. Postfix on `LinearCampaignStandardTaskForceEntry.RefreshManagerRoster`: for the Reserve entry the submarine rows of its snapshot are built with `status.CreateManagerRosterPreview` and added to `ManagerUnits` (no new force, so no duplicate). Config `[Campaign] ShowReserveSubmarines` (true). Unchecked in game: how the card template draws a submarine row. Trace: "ShowReserveSubmarines: N submarine(s) listed...".
KeepReserveSubmarines scope (2nd build): the ReplaceSnapshot prefix acts only inside `LinearCampaignMapViewModel.UpdateSelectedStandardTaskForceSnapshot` (prefix/finalizer depth counter `_rsDepth`) - that is the only lossy path (Status snapshot minus IsCampaignSupportUnit replaces the selected force; lossy when the Reserve is selected). Transfers (`TransferStandardTaskForceUnit`) and the return from service (`AcknowledgeReturns` -> `ReleaseRepairedSubmarines` -> `RefreshStandardTaskForcesFromStore`, new entries) are left alone. Side effect of the loss in the game: `PendingReturns` lists only orders whose unit is still in a force, so the order of a lost submarine is never acknowledged. Save repair recipe (fix.py, done for M-06A-2): add `TaskForceMode_ForceN(+_Unit1)` with `ForceId=Submarine-<id>`, bump `TaskForceMode_Forces/Count`, add the unit to `CurrentTaskForce` too (else the Builder's next commit drops the Submarine- force), set the order `Acknowledged=True`.
RestoreLostSubmarines (same file, replaces hand repair of saves): postfix on `LinearCampaignMapViewModel.RefreshStandardTaskForcesFromStore` (guard `_rsRestoring`). Finds `TaskForceMode_RepairOrder_*` with Acknowledged != True, Source state not IsDestroyed, whose InstanceId is in no force of `ReadCurrentTaskForces()`; takes the unit from the latest `TaskForceMode_Mission<N>_Post_Unit*` with that id and Category=Submarine (via private `ReadSnapshot(state, "..._Post", "..._Post_Unit")`), appends it to the Reserve group, `WriteCurrentTaskForces(coll, null, null, null)`, then calls RefreshStandardTaskForcesFromStore again. The game's return dialog (`PendingReturns` on mission open) then acknowledges the order and `ReleaseRepairedSubmarines` gives the Submarine- force. Ships are not restored (an orphan order of a ship = it was sold). Config `[Campaign] RestoreLostSubmarines` (true). Trace: "RestoreLostSubmarines: put back into the Reserve: ...". Untested in game.
Save series check: only two submarine instance ids ever appear in the 18 saves (Santa Fe 9bdf.., San Luis 550b..) - no second purchased submarine left a trace.
Options merged: Keep / Show / Restore are one option `[Campaign] FixReserveSubmarines` (true), field `_fixSubs`, label "Fix submarines sent for service" (lang keys lbl/help.Campaign/FixReserveSubmarines). The earlier three keys never shipped.

### UpgradeShip: the sold hull is free to buy again
The game's Dismiss returns a ship to the roster ("Dismissed units return to the roster and can be purchased again"). The Builder marks owned hulls (IsCurrentTaskForceUnit, IsAffordable=false) when it builds its roster (`AddFilteredRosterGroups`) and the upgrade sells the old ship while the Builder stays open, so after `DoUpgrade` the Builder is refreshed: `RefreshCurrentTaskForceOwnershipState`, `LoadCampaignTaskForceData(true)`, `NotifyPurchaseStateChanged` (`FreeOldHull`). Trace "UpgradeShip: old hull <type:variant>: owned in the save X; in the roster, owned X, already in task force X, affordable X". Static analysis did not find a blocker in the save itself (the old hull is in no force and not in CurrentTaskForce after the upgrade) - the trace line is there to show what still blocks it, if anything.
Builder diagnostics (TaskForceUpgrade.cs, `TraceRosterItem`): on every new SelectedRosterItem the trace gets "Builder: selected <type:variant>, cost, surcharge, available, pending, fleet value, cap, its cap value, owned flag, owned in the save, destroyed, already in task force, can afford, affordable flag" - the game greys "owned" and "too expensive / over the fleet cap" alike (IsAffordable=false). Case 2026-11-17: Santa Fe not buyable in M-06A-2 - the save has 50 points (PersistentData TaskForceAvailablePoints, PointJournal Balance, all forces), Santa Fe is not owned, not in Losses, allowed for mission 20, in service 1971-1982.
FixReserveSubmarines rework (2026-11-17, after M-07-1/M-07-2): the sub was lost again right after "Send for rearm" from the Mission Debrief although the old ReplaceSnapshot keep traced "the Reserve keeps ..." - likely Harmony(X) did not write `__args[0]` back. Now:
- keep (store): prefix on private static `LinearCampaignTaskForceSaveManager.WriteCurrentTaskForces(IniHandler, collection)` - the one funnel of every forces write. Pending-service submarines (order not Acknowledged, source state not destroyed) missing from the collection are added to the Reserve; record from the saved forces (`ReadSnapshot(state, "TaskForceMode_ForceN", "..._Unit")`) else the latest `Mission<N>_Post`. The argument object is changed in place (`<Forces>k__BackingField`) as well as `__args[1]`. Trace "FixReserveSubmarines: kept in the Reserve X (the write came from A <- B ...)" - the caller chain is for the game bug report.
- keep (memory): ReplaceSnapshot prefix inside `UpdateSelectedStandardTaskForceSnapshot` sets the new snapshot's `<Items>k__BackingField` (no `__args` write-back needed).
- restore: `RefreshStandardTaskForcesFromStore` postfix writes the forces once more when a pending sub is missing (the keep logic adds it), then refreshes.
- show: unchanged (`RefreshManagerRoster` postfix).
UpgradeShip: the button is disabled (tooltip `up.owned`, "this version is already in your fleet") when the selected roster variant is already owned - the Builder's own `IsVariantAlreadyInTaskForce(sel)` (owned flag, owned hull keys in the save, pending/purchased lists). `DoUpgrade` checks it again before anything is sold (trace "UpgradeShip: X is already in the fleet - nothing sold"): before, the old ship was sold and the Builder then refused the purchase.
UpgradeShip: ships of the other surface forces (Reserve, other task forces) are candidates too (2026-11-17). The Status window holds only the selected force, so `UpOtherForceShips` builds each other force's rows with `status.CreateManagerRosterPreview(force.Snapshot, missionIndex)` (as the Manager cards do) and uses the row's preview view model (`_owner`) for CanDismiss / refunds; ships away for service (`LinearCampaignTaskForceRepairs.IsUnavailable`) are left out; cached by the forces' snapshot objects + selection + mission. The menu shows "<ship> (<force>)". `DoUpgrade` selects that force first with the map's own `SelectStandardTaskForce(entry, true)` (`LinearCampaignMapViewModel.Instance`; the Builder's status VM is the map's `TaskForceStatusWindow`), finds the ship in the Status window again, sells/buys there (the new ship joins that force) and selects the previous force back.
UpgradeShip window header (2026-11-17): as the game's Window.xaml - 20 px row, `Brush.Window.Header`, the title TextBlock at margin 2.5 with the theme's default text style, close = Button Width 20, Padding 0, bold "X". The old "X" was a TextBlock inside the draggable header: the header's MouseLeftButtonDown captured the mouse, so the X never got its MouseLeftButtonUp (did not close). Now the header ignores presses on the button (`x.IsMouseOver` / `IsInside(e.Source, x)`).
Show moved (2026-11-17): the row of a submarine in service is no longer added to the Reserve card of the Surface tab (`RefreshManagerRoster` postfix removed) but to the Submarines tab: postfix on `LinearCampaignMapViewModel.RefreshStandardManagerVisibleUnits` - when `IsStandardSubmarineCategorySelected`, submarines of the Reserve entry's snapshot that `StandardManagerVisibleUnits` lacks get a row from `TaskForceStatusWindow.CreateManagerRosterPreview(one-item snapshot, mission index)` (IsReserveUnit=true; rows cached per item object) and are appended; NotifyPropertyChanged HasStandardManagerVisibleUnits / HasStandardManagerEntries. The tab itself is already offered (`HasStandardSubmarineCategory` looks at all StandardTaskForces items).
Text by background, coloured texts on a dark backdrop (2026-11-17): a coloured (non-grey) text is lightened to at least 80 % HSL lightness (`TextColourLight`, hue and saturation kept) while the backdrop is dark (t = 0); on a light backdrop it is darkened as before. Before, on a dark backdrop it kept the game's colour (only grey texts went to pure white), which he had taken for "lightened the wrong way".
The halo follows the text's own lightness: light text (dark backdrop) - black halo, dark text (light backdrop) - white halo.
Text by background, inlines (2026-11-17): the Event Log's unit links are Hyperlink inlines with a colour of their own (red); only the TextBlock's Foreground was shifted, so the link kept its colour while the halo flipped (white halo on a light backdrop made the red look lighter, black on a dark one darker - "coloured text works the wrong way"). `ShiftInlines` gives every inline whose Foreground is not inherited (not the parent's brush) the same `ShiftText` as the text (originals in `_inlOrig`, ours in `_inlOurs`); off / text by background off: our colours are put back to the originals (a colour the game set meanwhile stays).
Coloured texts' band (2026-11-17): HSL lightness at least 0.7 on a dark backdrop (`TextColourLight`) and at most 0.35 on a light one (`TextColourDark`), otherwise unchanged - the hue stays recognisable (the Event Log's red link was nearly black at 15 %). Grey texts as before (white / down to 15 %).
The halo of a coloured text follows the backdrop (`HaloWhite`: white on a light one, black on a dark one), not its own lightness - within the band a blue at 0.7 still counts as "dark" by luminance. Also used for the halo share in `TextT`.
Coloured texts on a dark backdrop: tried full HSV brightness (vivid red instead of pale pink) - he wanted it back, so it is HSL lightness 0.7 again.
Link hover: our local Foreground on a Hyperlink hid the hover colour of its style (a trigger loses to a local value). `HookInlineHover`: on MouseEnter our colour is cleared (the style's normal / hover colour shows), on MouseLeave it is set again; `ShiftInline` does not touch a recoloured inline under the mouse (it would take the hover colour for the original).
Link hover outline (TextElement.Stroke on the inline) was tried and removed: the stroke and the row halo together smeared the letters.
Inline colour restore (text by background off / look off): a colour that came from the style is given back with ClearValue (so the style's hover trigger works again; setting it back as a local value hid the hover colour), a local original (`ReadLocalValue` is a Brush, `_inlLocal`) is set again.

### Event Log: Compact layout (EventLogCompact.cs)
`[EventLog] Compact` (off). Event Log tab, group "Layout", "Compact layout" / "less spacing". On the window's own elements, no tree search: window Content = the XAML's Grid (Margin 5), its first child the TabControl (theme Padding 12), its TabItems (theme Padding 12,7.5; header font 16 stays), TabItem.Content = the Borders of the XAML: tabs 0/1 Border.Child = the ListBoxes (their ItemTemplate gets a copy of the game's row template with smaller Border Padding / column gap, parsed with GUI.ParseXaml), tabs 2/3 Border Padding 5. Sizes are chosen in SCREEN PIXELS (margin 3, content 4, tab header 10 x 3, row 6 x 2, time-text gap 8) and divided by `ScreenScale(window)` (px per unit, incl. the window's look scale) - re-applied on the window's Loaded when the scale changed and on the option switch (windows kept in `_logWindows`). Off: the remembered originals go back.

### Main menu: News window fills the free area (NewsFit.cs)
`[General] FitNewsWindow` (on). Game Menus tab, "Main menu", "News window fills the free area". `NewsFitStep` (after PinStep, once per window and per canvas size): target = from the right edge of MainMenuView's ScrollViewer "scrollViewer" (menu column) to the canvas' right edge, from the canvas top to 6 screen px above the loading tip bar (the 800-wide Border on MainMenuView's Canvas; without tips 30 units above the bottom); measured with PointToScreen, turned into canvas units with PointFromScreen, plus the window's VisibleInsets; MaxWidth / MaxHeight bindings replaced. First time only (`[General] NewsLookApplied`, written by the plugin) and only when no look is remembered for the News pattern: look 0 % opacity / 120 % scale / text contrast (his own setup). The two FindFirst searches run once per fit, not per frame.
News fit, second build: the VisibleInsets estimate put the window ~13 px too far left / up and a few px too low (2560x1440); now after setting X/Y/Width/Height the window is checked 0.2 s later against its frame (the mod's border = what the user sees, `Borders`) on the screen and corrected once by the differences (`NewsCorrect`). The first look is applied at once (`ApplyLook` with the look state's panel); before, only the stored look changed (the menu showed 0 % / 120 % but the window stayed as it was).
News fit, third build (trace 2026-11-17): the frame check moved the window 26.67 px DOWN by the frame's top - the frame sits on the body below the title bar, so the title bar went above the screen (invisible, hover could not show it). The top is now checked against the HeaderThumb's top; left / right / bottom against the frame (those came out 0 / 0 / -5 px).
Main menu background: default changed to Slideshow (was Video), 2026-11-17 (his request); existing cfg files keep their value.
News fit without the jump (2.3.3): the window is hidden (Opacity 0) and fitted in OnWindowLoaded (after its first layout, before its first frame); the visible edges are measured on the window as it is (HeaderThumb top, frame sides / bottom, in canvas units against X / Y / Width / Height) so the offsets carry over exactly; when the frame is not laid out yet (it is added in the same Loaded) the inset estimate + the frame check (`NewsCorrect`, 0.2 s) are used while it stays hidden. Shown by `NewsFitStep` once fitted, or after 2 s whatever happened. No longer waits for PinStep (our X / Y replace the game's centring bindings anyway).
News fit vs. the user (2.3.3): the user's own place / size / look win. `NewsUserChanged` (from DragPostfix, ResizePostfix, the look menu pick and SetLevel; our own SetF / LookMap writes do not go through them) switches `FitNewsWindow` off. First time (`NewsLookApplied` false): a News window with a remembered layout (`_saved`) or look switches the option off instead of being fitted: checked at start right after `ReloadPatterns` (`NewsOwnedAtStart`, by the pattern matching "News"), so the check box is off before any window, and again in `NewsFitLoaded` / `NewsFitStep` (`NewsUserOwned`). Switching the option on (SettingChanged): `NewsLookApplied` = false, `ResetLayouts(key)` + the look removed (`NewsReset`), the open News window refitted and given the mode's look.

### Camera offset (CameraShift.cs)
- Re-implementation of the Camera Offset mod (bananaduck.cameraoffset 0.4.0): postfix on `CameraManager.OnLateUpdate`; `ResetProjectionMatrix`, then `m[0,2] = -m[0,0]*X`, `m[1,2] = -m[1,1]*Y` on `_camera` and `_particleCamera` (lens shift: the camera does not move). One X/Y pair (`[WorldView] CameraOffsetFollowX/Y`, ±25 % of the view, /50 for the matrix), used in `CurrentMode` Follow and FollowBehind only, while `RenderPosition.SelectedObject` exists and not when `IsOpticalViewActive`. SmoothDamp, 0.1 s fixed. (The mod's per-mode values, glide time and optics switch were dropped on request.) Once back at zero the matrix is reset once and left alone.
- Our postfix is always installed and returns while the mod works. "Works" = its own postfix (owner `bananaduck.cameraoffset.patch`) is on `CameraManager.OnLateUpdate` (`Harmony.GetPatchInfo`), not "its dll is loaded": unticked in the Mods menu the mod stops also without a restart (Anchor Chain), while its dll stays loaded. `RefreshCamMod` reads it at start, after our patch, in `RefreshEnabled` (Mods menu saved / load screen), when a settings page or the look menu is built (`CamModWorking`), and once a second from our postfix. We never patch / unpatch the mod ourselves. While it works the rows are greyed out (`Field.Conflict` → disabled, opacity 0.45, a `cam.conflict` note under the group title and in the help line; also in the look menu).
- Editor: kind 'p' = `SettingsWindow.PadRow`: a 2D pad (sight with ticks every 5 %, the camera dot) for `f` (X) and `f.Pair` (Y; that field is `Hidden`, no row of its own, but its pending value / dot / Apply work as usual). Drag or click on the pad, RepeatButton arrows at the edges step by `Step`; pad edges = Min/Max; ↺ resets both.
- Look menu (▾) of the World View window: `CameraOffsetMenuRow` = the switch + the same `SettingsWindow.Pad`, writing the entries directly (saved, live); moving the dot also switches the offset on; `SettingsWindow.Forget(entry)` drops an unapplied value of the open settings window and rebuilds its tab; the menu does not close on MouseLeave while `PadDragging`. Greyed out with `cam.conflict` when the mod is loaded.
- Settings: tab "World View", shown when the settings are opened from the World View window's gear (`Open(..., worldView)`, `IsWorldViewWindow`). Live before Apply: the postfix reads `SettingsWindow.Shown(entry)` = the pending value while the window is open, else the entry's value; closing without Apply returns to the saved values.

### Game version watermark (VersionMark.cs)
`[General] ShowGameVersion` = Game (default) / Show / Hide; Debug tab, group "Game", "Game version watermark". The game decides once in `SeaPower.VersionWatermark.OnStart` (GameInitializer): shown on a Steam beta branch, or with [Debug] ShowVersionWatermark in its options ini AND config.ini; otherwise the object is deactivated. Postfix on OnStart puts Show / Hide over it (activate + the game's text `productName + " v" + version` on its TextMeshProUGUI, or deactivate); Game leaves the game's result. The option switch acts on the singleton's cached `_instance` (read by reflection, its Instance getter would create one); Show/Hide -> Game re-runs the game's OnStart. Nothing written to files.
