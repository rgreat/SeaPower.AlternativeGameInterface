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
Поддерживаются Sea Power 0.8.3 – 0.8.5 (`_info.ini` ApproximateVersion=0.8.3; README и README.BBCode — тоже).
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
[General] WindowPatterns, CascadeOffset, BringToFrontOnClick, AddWindowKey, AllowDragOutside, MinVisiblePixels, RememberOpenWindows,
RemoveSizeLimits, MaximizeOnDoubleClick, ImprovedUnitReferenceUI (бывш. ImprovedUI, миграция `BindRenamed`), SettingsButton,
SnapWindows, SnapDistance (1.3.0), Debug. [UnitReference]: TreeWidth, TreeVisible, Country; RememberSearchPanel, Category,
FilterByDate, SearchByNameOnly, SearchText (1.3.0); OptimizeTree, ScrollToSelected, ShowLoadoutList, ArmedOnly (1.4.0).
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
* Версия: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе; и строку «Version N» в
  начале README.BBCode.txt / README.md (описание Steam). Список изменений к выкладке — одним списком, без разбивки по версиям.
