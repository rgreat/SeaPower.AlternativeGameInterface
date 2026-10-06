# Smart Windows — заметки для продолжения работы (для Claude)

Мод для Sea Power: BepInEx 5 + Harmony, загружается через Anchor Chain (Workshop 3380210757).
UI игры — NoesisGUI (XAML), окна — `SeapowerUI.Window`. Весь доступ к игре и Noesis — через рефлексию
(в csproj нет ссылок на Seapower-Scripts/Noesis), Harmony-хуки ставятся по именам методов.

## Сборка в песочнице (проверено)
1. `apt-get update; apt-get install -y dotnet-sdk-8.0` (`update` может ругаться на nodesource — не мешает).
2. GameRefs (UnityEngine*.dll, Seapower-Scripts.dll, Noesis.NoesisGUI.dll): репозиторий приватный, из песочницы
   GitHub отдаёт 404, а синхронизация проекта переносит только текстовые файлы → пользователь прикладывает
   `GameRefs.zip` в чат. Распаковать, например, в `/home/claude/game`.
3. BepInEx: `https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip`
   (`BepInEx/core/*.dll`); Anchor Chain: `https://github.com/SeaPower-Modders/AnchorChain/releases/download/V1.1.0/dev.zip`.
   GitHub API даёт rate limit — качать прямыми ссылками.
4. `nuget.config` с пустым списком источников обязателен (nuget.org закрыт, restore иначе падает).
5. `dotnet build -c Release -p:ManagedDir=... -p:BepInExCore=... -p:AnchorChainDll=...` → `Release/SmartWindows.dll`.
6. Анализ игры: `monodis` на текущей сборке игры падает (ссылка на System.Threading.Channels 10.0). Работает ILSpy:
   `https://github.com/icsharpcode/ILSpy/releases/download/v9.1/ILSpy_binaries_9.1.0.7988-x64.zip` → консольная
   программа net8.0 со ссылкой на `ICSharpCode.Decompiler.dll` (CSharpDecompiler.DecompileTypeAsString по всем типам,
   имя файла — `ReflectionName`, иначе generic-типы перезаписывают друг друга). XAML в dll нет (он в ассетах Unity).

## Выдача результата (предпочтения пользователя)
Каждая сборка — `SmartWindows.zip` БЕЗ корневой папки: `Release/` (SmartWindows.dll, _info.ini, thumbnail.jpg),
`src/`, `SmartWindows.csproj`, `nuget.config`, `PUBLISHING.md`, `DEVNOTES.md`. README пользователь пишет сам.
thumbnail (бинарный) в песочнице недоступен — в zip не попадает, пользователь кладёт свой.
Общение — по-русски, коротко; в .cfg — один переключатель на функцию. Править исходники можно; выкладку на GitHub
делает пользователь.

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
* `ExecutedCustomCommandArgs` — struct: в Harmony-постфиксе брать через `object[] __args`.
* `Singleton<T>.Instance` создаёт объект, если его нет → сначала `InstanceExists(false)`. Геттеры MainGameViewModel
  перебирать только нужного типа (некоторые трогают синглтоны).

## Окна миссии (как устроено в игре)
* HUD-окна «хостятся»: их view-model лежат в `WindowHostManager.Instance.CurrentHost.Windows`
  (`ObservableCollection<object>`); открытие/закрытие — `WindowHostCommands.OpenWindowCommand/CloseWindowCommand`
  (`CustomRoutedCommand.Execute(param, UIElement target)`, target — `MainGameView.Instance`),
  обработчики — `WindowHost.OpenNewWindow/CloseWindow`. Элемент `Window` при каждом открытии создаётся заново.
* Кнопки HUD (`MainGameView`: ToggleButton `FormationManagerWindow`, `EventLogWindow`, `UnitStatusWindow`,
  `CameraControlsWindow`, `MapWindow`) — `ToggleWindowBroker` (WindowContent = VM).
* Флаги в `MainGameViewModel`: `MapVisible` (Map = SceneMapViewModel, Tactical Display; `ToggleMapOpen`),
  `UnitStatusVisible` (UnitStatus; `ShowUnitStatus` = Open-команда + флаг; открыть можно только при `HasUnit`).
* Закрытие крестиком окна: `Window.Close` → `CloseAction` или `CloseWindowNow` → Close-команда хоста.

## RememberOpenWindows (работает в игре; мгновенный старт — не проверен)
Хуки `WindowHost.OpenNewWindow/CloseWindow` и сеттеры `MainGameViewModel.XxxVisible` (для которых есть свойство `Xxx`)
ставят VM в очередь; в Update состояние VM (= есть в `Windows` && флаг `XxxVisible`, если он есть) пишется в
[OpenWindows] как `open,ТипVM` / `closed,ТипVM`. Шаблон связывается с типом VM, когда подходящее окно впервые видно
открытым (`LearnOpenWindow`). Запись — только при поднятом HUD (`MainGameView.Instance.IsLoaded`), не во время
загрузки (`LoadingNow`) и не в фазе старта сцены.
Фаза старта: от начала загрузочного экрана (`LoadScreenStarts`) до 0,5 с после его конца. Всю фазу каждый кадр
`RestoreOpenStates` приводит окна к сохранённому состоянию, как только поднят HUD и есть хост (подтверждено: работает;
раньше была фиксированная пауза 1 с — пользователю казалось долго). Открыть = Open-команда (если VM не в хосте) + флаг
true; закрыть = флаг false, иначе Close-команда. В фазе префикс `WindowHost.OpenNewWindow` не даёт игре самой открыть
окно, сохранённое закрытым (`WindowStartsOpen` брокера), и вызывает `CloseWindowCommand.UpdateCanExecute`, чтобы
кнопка HUD не осталась нажатой.

## Unit Reference: прокрутка при смене закладки (подтверждено в игре)
Игра: клик по закладке → `SuppressBringIntoViewBehavior.OnPreviewMouseDown` запоминает VerticalOffset;
`ObjectInformationView.UnitTabs_SelectionChanged` (только при смене `_lastSelectedTab`) → `ScrollToTop()`;
`OnScrollChanged` возвращает `min(запомненный, ScrollableHeight)` (флаг `_isRestoring`). Если восстанавливать нечего,
на следующем кадре сам Noesis делает bring-into-view какого-то элемента содержимого с минимальной прокруткой (низ цели →
низ области). Результат зависит от содержимого: Орлан — полоса закладок доезжает до верха (152,3), Атлант — нет (62,9);
без панели поиска область выше сверху → ещё на её высоту меньше. Первая попытка (+высота панели) не годилась.
Сейчас (`HookTabScroll`): при реальной смене закладки (не первой при открытии) и без восстановления позиции
пользователя прокрутка заменяется на `min(max(offset, верх выбранного TabItem), ScrollableHeight)`. Верх TabItem —
минимальный Y (TranslatePoint к ScrollViewer + VerticalOffset) его видимых потомков (метка выбора рисуется выше).
Если Noesis не прокрутил вовсе — то же через 3 кадра (`RunTabFix`), но не после восстановления позиции игрой.
Восстановление (`_isRestoring`) правку не отменяет: Noesis после него всё равно делает свою прокрутку (Орлан: 4,7 → 137,9),
её и заменяем. Окно ловли — 5 кадров. Орлан и Атлант подтверждены пользователем.
Временная диагностика прокрутки удалена; при Debug = true остаётся строка `tab scroll X -> Y (tab header at Z)`.

## Хуки
LoadScreen `<ExecuteLoadScreen>.MoveNext` (конец загрузки / идёт загрузка), `FileManager.SaveDirectories` (Apply в Mods),
`Window..ctor`, `OnInitialized` (prefix: открыть развёрнутым; postfix: раскладка), `ApplyScreenAnchor`,
`AbsoluteInitialPosition.AssociatedObject_Loaded`, `Kfa_Completed`, `MaximizeChanged`, `Restore_Completed`,
`CommandUnmaximizeWindow`, `HeaderDragDelta`, `ObjectInformationViewModel.set_MuseumEntriesVisible`,
`UnitTreeBrowserViewModel.set_SelectedNation` + RestoreSessionState/OnUnitsLoaded/SetupNations/FinishInitialization,
`WindowHost.OpenNewWindow` (prefix+postfix)/`CloseWindow`,
`ObjectInformationView.UnitTabs_SelectionChanged`, `SuppressBringIntoViewBehavior.OnScrollChanged` (prefix), `MainGameViewModel.set_MapVisible/set_UnitStatusVisible`.
Событие `Loaded` каждого окна — настройка до первого кадра (без мерцания).

## Настройки (.cfg)
[General] WindowPatterns, AddWindowKey, AllowDragOutside, MinVisiblePixels, RememberOpenWindows,
RemoveSizeLimits, MaximizeOnDoubleClick, ImprovedUnitReferenceUI (бывш. ImprovedUI, миграция `BindRenamed`), Debug.
[UnitReference] TreeWidth, TreeVisible, Country. [Layouts] `x,y,w,h[,max]` (раскладка всегда «с деревом»).
[OpenWindows] `open|closed[,ТипVM]`.

## Не подтверждено в игре / открытые вопросы
* Открытие Unit Reference развёрнутым (последняя правка: `,max` снимается только сворачиванием пользователем).
* RememberOpenWindows: восстановление без паузы (фаза старта + блокировка открытия игрой) — не проверено.
* Версия везде 1.0.0 (BepInPlugin, ACPlugin, лог, csproj) — поднять перед выкладкой.
