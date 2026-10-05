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
`UnitTreeBrowserViewModel.set_SelectedNation` + RestoreSessionState/OnUnitsLoaded/SetupNations/FinishInitialization.
Событие `Loaded` каждого окна — настройка до первого кадра (без мерцания).

## Настройки (.cfg)
[General] WindowPatterns, AddWindowKey, AllowDragOutside, MinVisiblePixels, RememberOpenWindows,
RemoveSizeLimits, MaximizeOnDoubleClick, ImprovedUnitReferenceUI (бывш. ImprovedUI, миграция `BindRenamed`), Debug.
[UnitReference] TreeWidth, TreeVisible, Country. [Layouts] `x,y,w,h[,max]` (раскладка всегда «с деревом»).
[OpenWindows] open/closed.

## Не подтверждено в игре / открытые вопросы
* Открытие Unit Reference развёрнутым (последняя правка: `,max` снимается только сворачиванием пользователя).
* RememberOpenWindows: открытие/закрытие через источник привязки Visibility окна — не проверено.
* Версия везде 1.0.0 (BepInPlugin, ACPlugin, лог, csproj) — поднять перед выкладкой.
