# Smart Windows — заметки для продолжения работы (для Claude)

Мод для Sea Power: BepInEx 5 + Harmony, загружается через Anchor Chain (Workshop 3380210757).
UI игры — NoesisGUI (XAML), окна — `SeapowerUI.Window`. Доступ к игре — через рефлексию (в csproj нет ссылки на
Seapower-Scripts), Harmony-хуки ставятся по именам методов. Noesis: основной код — через рефлексию, окно настроек и шестерёнка
(1.3.0) — напрямую (ссылка на Noesis.NoesisGUI в csproj, как в Alternative Damage Model).

## Сборка в песочнице (проверено)
1. `apt-get update && apt-get install -y dotnet-sdk-8.0` (без `update` пакеты дают 404).
2. Репозиторий: в облачной сессии Claude Code подключается через `add_repo` (rgreat/SeaPower.SmartWindows) и
   клонируется полностью, вместе с бинарными файлами (dll, thumbnail.jpg). В обычном чате GitHub отдаёт 404.
   GameRefs (UnityEngine*.dll, Seapower-Scripts.dll, Noesis.NoesisGUI.dll): папка `GameRefs/` в репозитории может
   отставать от игры → брать `GameRefs.zip`, приложенный пользователем, распаковать в `/home/claude/game`.
   Версия игры — `SeaPower.Constants.VERSION` в декомпиляции Seapower-Scripts. `GameRefs/` в репозитории — 0.8.5.
3. BepInEx: `https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip`
   (`BepInEx/core/*.dll`); Anchor Chain: `https://github.com/SeaPower-Modders/AnchorChain/releases/download/V1.1.0/dev.zip`.
   GitHub API даёт rate limit — качать прямыми ссылками.
4. `nuget.config` с пустым списком источников обязателен (nuget.org закрыт, restore иначе падает).
5. `dotnet build -c Release -p:ManagedDir=... -p:BepInExCore=... -p:AnchorChainDll=...` → `Release/SmartWindows.dll`.
   Номер билда (UTC-время сборки `yyyyMMdd.HHmm`, свойство `BuildStamp` в csproj → AssemblyMetadata "Build") пишется в лог:
   `Smart Windows 1.3.0 (build ...) loaded` — по нему видно, какая dll реально запущена.
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
Общение — по-русски, коротко; в .cfg — один переключатель на функцию. В отчётах не повторять каждый раз одно и то же
(«собирается без ошибок», «ничего не коммитил» и прочее рутинное) — только новое и важное. Править исходники — только по просьбе
пользователя; коммит в GitHub — только по просьбе пользователя.

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
* Коммит «1» (08.10.2026) по ошибке вернул код 1.1.0 (`01c76a0`) поверх 1.2.0 (`bbeb55b`); восстановлено из `bbeb55b`.
  Прокрутку Unit Reference при смене закладки (`HookTabScroll`, была в 1.1.0) пользователь решил не возвращать.

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
`DamageControlView..ctor` (+ `Loaded`/`IsVisibleChanged`): вкладка Damage Control (XAML: `SeaPower.AlternativeDamageModel/ref/xaml/DamageControlView.xaml`) — корневой Grid MinHeight=330 (единицы UI), строки: 0 карточки-сводки (Auto), 1 полоса DC/Hull/Flooding (Auto, Margin −30 сверху), 2 панели секций (*: 5 колонок Aft…Forward, заголовок, −/+ команд, ListBox повреждений; Border Margin 0,0,0,6), 3 «Auto Damage Control Status» (Auto). Своего минимума у панелей секций в игре нет: им достаётся 330 − остальные строки (≈150–160 ед.). [UnitStatus] SectionsMinHeight (1.3.0; пиксели экрана, 0 = как в игре; бывш. DamageControlMinHeightAdd — удаляется из .cfg): `SetupSections` — корень = ближайший Grid с MinHeight (в ширину, глубина ≤ 5, не в списки), элемент строки 2 = панели секций; замер «игрового» минимума секций (330 − ActualHeight строк 0,1,3 − поля строки 2) → `SectionsGamePx` (в форме «game: ≈ N px»); `ApplySections`: значение > 0 — у корня MinHeight 0, у СТРОКИ 2 (RowDefinition, берётся из корня каждый раз) MinHeight = px / масштаб + поля панелей (MinHeight самого элемента в строке «*» Grid не учитывает — элемент обрезался, нижняя панель стояла на месте) (`ScreenScale`: PointToScreen; иначе UIScale × Screen.height / 1080); 0 — минимумы игры обратно. Раньше (DamageControlMinHeightAdd) прибавлялось к 330, а ещё раньше — ко ВСЕМ MinHeight вкладки, включая строки списка систем (21) и карточки команд ADM (24);
вкладки создаются свёрнутыми и строят содержимое только при показе — правка на первом показе).
Событие `Loaded` каждого окна — настройка до первого кадра (без мерцания).

## Настройки (.cfg)
[General] WindowPatterns, CascadeOffset, BringToFrontOnClick, AddWindowKey, AllowDragOutside, MinVisiblePixels, RememberOpenWindows,
RemoveSizeLimits, MaximizeOnDoubleClick, ImprovedUnitReferenceUI (бывш. ImprovedUI, миграция `BindRenamed`), SettingsButton,
SnapWindows, SnapDistance (1.3.0), Debug. [UnitReference] (1.3.0): RememberSearchPanel, Category, FilterByDate, SearchByNameOnly, SearchText.
[UnitReference] TreeWidth, TreeVisible, Country. [Layouts] `x,y,w,h[,max]` (раскладка всегда «с деревом»).
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
и того же «поколения» (номер экрана загрузки при создании окна: окна прошлой миссии висят, пока строится новая). [UnitStatus] SectionsMinHeight (минимум панелей секций, пиксели экрана; 0 = как в игре; ключи DamageControlExtraShrink и DamageControlMinHeightAdd удаляются).

## Рамка окон ([WindowBorder]: Enabled, Thickness 0,5–10, Color #RRGGBB/#AARRGGBB, по умолчанию #A0808080)
Перенесена из бывшей второй ветки; проверена в игре. В `Loaded` у окон с `Draggable = true`
(`AddBorder`): Border из ParseXaml (IsHitTestVisible=False, Panel.ZIndex=100000) последним ребёнком в корневую панель шаблона
(или первую панель ниже через одиночные обёртки), в Grid — на все строки/столбцы. Корневая панель шире видимого окна (зона
ручек ресайза) → `BodyInsets`: крупнейший потомок (глубина ≤ 6) с непрозрачным Background, ≥ 60% ширины и ≥ 30% высоты;
его отступы → Margin рамки. Верхней стороны нет (BorderThickness = t,0,t,t).
Ветки объединены (одна ветка — эта): прокрутку Unit Reference после смены закладки (HookTabScroll) из второй ветки
сознательно не переносили (переключение закладок оставляет прокрутку как в игре — так и задумано); её формат [OpenWindows] `state,ТипVM` читается (берётся первое поле).

## Порядок окон и Weapon Reference (проверено в игре)
* BringToFrontOnClick: PreviewMouseDown / Loaded окна → вверх от окна до первой панели, где в другом ребёнке есть ещё окно;
  только если все дети панели — окна. Состояние (уровень = ZIndex элемента при первом обнаружении, отметка клика) хранится
  ПО ОКНУ: Noesis-обёртки детей панели (ContentPresenter, Grid) при каждом обходе новые — по ним хранить нельзя (было:
  уровни 1000, 1000000...). ZIndex = уровень × 1000 + ранг.
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
  ✕ — нарисованный `Cross(size)` (Path, по центру кнопки, Padding 0): символ «✕» шрифта игры обрезался снизу и был не по центру
  (то же исправлено в ADM).
  Вкладки General / Remembered Windows / Frame / Unit Status / Unit Reference; Unit Reference — только если открыто шестерёнкой
  окна Unit Reference (`InfoViewModel` ≠ null; Weapon Reference — нет), Unit Status — только из окна Unit Status
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
  до окна (как раньше). С 1.3.0 исходные значения запоминаются (`SizeLimits`: окно → { элемент, Min/Max W/H }) и
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
  заголовке свои поля/значок): в одной сборке так было — все края считались далеко внутри, окно у левого края экрана уезжало
  за экран, остальные края промахивались. Trace: `snap: body insets`, `snap: visible insets of`, раз в секунду при
  перетаскивании `snap: '<окно>' X,Y … px per unit; screen WxH = X L..R, Y T..B; insets`.
* ВАЖНО (проверено по trace): элемент SeapowerUI.Window всегда стоит в начале своей ячейки — PointToScreen(0,0) окна = 0,0 при
  любом X; X/Y двигают корневую панель шаблона (Grid 'Container' [rows 10,*,10; cols 10,*,10]). Поэтому все замеры — по ней
  (`TemplateRootPanel`): отступы относительно неё, экранное начало окна = её PointToScreen. Пример: экран 2560×1440, 1,333 px/ед.
  → X 0..1920, низ (полоса HUD 1397 px) ≈ 1048 ед.; отступы Tactical Display 10,10,10,10 (тело 30 сверху — под заголовком).
* Замер (`Measure`): сначала `UpdateLayout()` окна, потом X/Y и PointToScreen — за кадр может прийти несколько шагов drag, и
  макет на шаг позади сдвигал все цели на этот шаг (окно изредка дёргалось туда-сюда). Другие окна (на экране, не развёрнутые,
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
* 1.3.0: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе.
