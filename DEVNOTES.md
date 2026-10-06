# Smart Windows — заметки для продолжения работы (для Claude)

Мод для Sea Power: BepInEx 5 + Harmony, загружается через Anchor Chain (Workshop 3380210757).
UI игры — NoesisGUI (XAML), окна — `SeapowerUI.Window`. Весь доступ к игре и Noesis — через рефлексию
(в csproj нет ссылок на Seapower-Scripts/Noesis), Harmony-хуки ставятся по именам методов.

## Сборка в песочнице (проверено)
1. `apt-get update && apt-get install -y dotnet-sdk-8.0` (без `update` пакеты дают 404).
2. Пользователь прикладывает `GameRefs.zip` (UnityEngine*.dll, Seapower-Scripts.dll, Noesis.NoesisGUI.dll) →
   распаковать, например, в `/home/claude/game/Managed`.
3. BepInEx: `https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip`
   (`BepInEx/core/*.dll`); Anchor Chain: `https://github.com/SeaPower-Modders/AnchorChain/releases/download/V1.1.0/dev.zip`.
   GitHub API даёт rate limit — качать прямыми ссылками.
4. `nuget.config` с пустым списком источников обязателен (nuget.org закрыт, restore иначе падает).
5. `dotnet build -c Release -p:ManagedDir=... -p:BepInExCore=... -p:AnchorChainDll=...` → `Release/SmartWindows.dll`.
   Номер билда (UTC-время сборки `yyyyMMdd.HHmm`, свойство `BuildStamp` в csproj → AssemblyMetadata "Build") пишется в лог:
   `Smart Windows 1.2.0 (build ...) loaded` — по нему видно, какая dll реально запущена.
6. Анализ игры: `apt-get install -y mono-utils`, `monodis Seapower-Scripts.dll > sps.il` (ilspy недоступен).

## Выдача результата (предпочтения пользователя)
Каждая сборка — `SmartWindows.zip` БЕЗ корневой папки: `Release/` (SmartWindows.dll, _info.ini, thumbnail.jpg),
`src/`, `SmartWindows.csproj`, `nuget.config`, `PUBLISHING.md`, `DEVNOTES.md`. README пользователь пишет сам.
Общение — по-русски, коротко; в .cfg — один переключатель на функцию.

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

## Хуки
LoadScreen `<ExecuteLoadScreen>.MoveNext` (конец загрузки), `FileManager.SaveDirectories` (Apply в Mods),
`Window..ctor`, `OnInitialized` (prefix: открыть развёрнутым; postfix: раскладка), `ApplyScreenAnchor`,
`AbsoluteInitialPosition.AssociatedObject_Loaded`, `Kfa_Completed`, `MaximizeChanged`, `Restore_Completed`,
`CommandUnmaximizeWindow`, `HeaderDragDelta`, `ObjectInformationViewModel.set_MuseumEntriesVisible`,
`UnitTreeBrowserViewModel.set_SelectedNation` + RestoreSessionState/OnUnitsLoaded/SetupNations/FinishInitialization,
`DamageControlView..ctor` (+ `Loaded`/`IsVisibleChanged`: корневой Grid вкладки [rows Auto,Auto,*,Auto] имеет MinHeight=330 → +DamageControlMinHeightAdd (панель начинает уезжать при большей высоте окна);
вкладки создаются свёрнутыми и строят содержимое только при показе — правка на первом показе).
Событие `Loaded` каждого окна — настройка до первого кадра (без мерцания).

## Настройки (.cfg)
[General] WindowPatterns, CascadeOffset, BringToFrontOnClick, AddWindowKey, AllowDragOutside, MinVisiblePixels, RememberOpenWindows,
RemoveSizeLimits, MaximizeOnDoubleClick, ImprovedUnitReferenceUI (бывш. ImprovedUI, миграция `BindRenamed`), Debug.
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
и того же «поколения» (номер экрана загрузки при создании окна: окна прошлой миссии висят, пока строится новая). [UnitStatus] DamageControlMinHeightAdd (+200 px к MinHeight=330; 0 = как в игре; старый ключ DamageControlExtraShrink удаляется).

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

## Не подтверждено в игре / открытые вопросы
* Открытие Unit Reference развёрнутым (последняя правка: `,max` снимается только сворачиванием пользователя).

## Версия
* 1.2.0: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе.
