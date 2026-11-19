# Alternative Game Interface: сборка, проверка и выкладка в Steam Workshop

> Preparing a release (version, change lists, README, translations, build, commit rules)? Read the Sea Power project doc `claude/release-howto.md` first (also: `claude/steam-workshop.md` for the Workshop ids and how to check what is published).

## 0. Переименование (было «Window Memory»)

Мод переименован в **Alternative Game Interface**: папка и DLL `AlternativeGameInterface`, настройки `Sea Power_Data/StreamingAssets/user/AlternativeGameInterface/AlternativeGameInterface.cfg` (как у AWC/ADM; файл BepInEx/config/com.rgreat.seapower.alternativegameinterface.cfg версий до 1.6.0 копируется туда один раз).
* При первом запуске плагин **один раз копирует** старый `com.rgreat.seapower.windowmemory.cfg` в новый файл (настройки и сохранённые
  окна не теряются). Старый cfg потом можно удалить.
* **Удалите старый `SeaPowerWindowMemory.dll`** из `BepInEx\plugins` и старую папку мода из StreamingAssets, если она там есть.
  Иначе будут работать две копии под разными именами; плагин напишет предупреждение в `LogOutput.log`.

## 1. Структура проекта

```
AlternativeGameInterface/                 <- проект (хранится у вас, в Workshop не уходит)
├─ AlternativeGameInterface.csproj        <- сборка; после Build копирует dll в Release
├─ nuget.config               <- пустой список источников NuGet (сборка без сети)
├─ description.bbcode.txt     <- текст для страницы в Workshop (BBCode), в мод не входит
├─ src/
│  ├─ AlternativeGameInterfacePlugin.cs           <- сам плагин (BepInEx BaseUnityPlugin)
│  ├─ SettingsUI.cs                   <- шестерёнка в заголовках окон, применение настроек
│  ├─ SettingsWindow.cs               <- окно настроек (как в Alternative Damage Model)
│  ├─ Snap.cs                         <- притягивание краёв окон (SnapWindows)
│  ├─ TreeTweaks.cs                   <- оптимизация дерева Unit Reference (OptimizeTree)
│  ├─ TreeScroll.cs                   <- прокрутка дерева Unit Reference к выбранному (ScrollToSelected)
│  ├─ ArmedFilter.cs                  <- галочка «Armed only» на панели поиска Unit Reference
│  ├─ StatusReference.cs              <- вкладка Reference в окне Unit Status
│  ├─ TooltipFrame.cs                 <- рамка вокруг всплывающих подсказок ([WindowBorder] ToolTips)
│  ├─ Backgrounds.cs                  <- фоновые картинки модов вместо игровых ([General] Backgrounds)
│  ├─ Pickers.cs                      <- _Custom Mission, папка редактора, синие ini, старые логи
│  ├─ TreeSearch.cs / HoverHelp.cs    <- опции дерева Unit Reference 1.6.0, подсказки по ссылкам
│  ├─ UnitMenu.cs / CampaignLoadouts.cs    <- меню панели юнита, StrictLoadouts
│  ├─ Loc.cs                                              <- локализация (таблица D(...), файлы lang/<код>.ini)
│  ├─ MaxFit.cs                       <- развёрнутое окно в пределах экрана
│  └─ AnchorChainEntry.cs             <- точка входа Anchor Chain ([ACPlugin] + IAnchorChainMod)
└─ Release/                   <- ЭТО и есть мод: содержимое этой папки выкладываете
   ├─ _info.ini                       <- название/описание в менеджере модов + версия игры
   ├─ AlternativeGameInterface.dll                <- плагин
   ├─ thumbnail.jpg                   <- картинка для Workshop (JPEG 512x512; лимит Steam < 1 МБ)
   └─ README.md                       <- описание для пользователей (пишете сами)
```

В StreamingAssets папка мода должна называться **`AlternativeGameInterface`**, а не `Release`: по имени этой папки игра хранит
галочку мода в `usersettings.ini`, и по нему же мод проверяет, включён ли он в меню **Mods**.

Структура мода повторяет ваш пример (`3807187341`): `_info.ini` + dll + картинка + README лежат в корне папки мода.
В папку мода **не кладите** `AnchorChain.dll`, `BepInEx.dll`, `0Harmony.dll` и вообще чужие dll: Anchor Chain
пытается загружать все dll из папки мода и может попытаться загрузить самого себя.

## 2. Сборка

Нужны .NET SDK 6 или новее (https://dot.net), установленная игра с BepInEx и Anchor Chain из Workshop.
Пути по умолчанию в csproj рассчитаны на ваш каталог (`D:\Steam\steamapps\common\Sea Power`). Иначе передайте свои:

```
dotnet build -c Release -p:GameDir="D:\Steam\steamapps\common\Sea Power"
```

AnchorChain.dll берётся из `<Steam>\steamapps\workshop\content\1286220\3380210757\AnchorChain.dll`
(если путь другой: `-p:AnchorChainDll="..."`). Результат автоматически копируется в
`Release\AlternativeGameInterface.dll`. Готовая dll уже лежит в этой папке.

## 3. Локальная проверка (до выкладки)

1. Удалите `AlternativeGameInterface.dll` из `Sea Power\BepInEx\plugins\` (иначе будет две копии; вторую плагин сам не
   запустит, но проверять надо именно путь через Anchor Chain).
2. Скопируйте содержимое папки `Release` в
   `D:\Steam\steamapps\common\Sea Power\Sea Power_Data\StreamingAssets\AlternativeGameInterface`.
3. Запустите игру -> **Mods** -> включите **Anchor Chain** и **Alternative Game Interface** -> Apply -> **полностью перезапустите игру**
   (код-моды загружаются только при старте процесса; Apply перезагружает лишь сцену).
4. В `BepInEx\LogOutput.log` должно быть `Alternative Game Interface 1.7.0 (build ...) loaded` и позже `Window hooks installed.`
   Предупреждения `Alternative Game Interface ...` покажут, если игра изменилась.
5. Проверьте: положение окон запоминается, окно добавляется в настройках (Remembered Windows), окна уходят за край.

Важно: вариант «через Anchor Chain» я проверил только сборкой против настоящего `AnchorChain.dll` и тестами логики окон;
в самой игре запуск из меню Mods не проверял. Если мод не стартует, пришлите `LogOutput.log`.

## 4. Выкладка в Workshop (в игре)

Нужно, чтобы Steam был запущен.

1. Главное меню -> **Mods** (менеджер модов) -> кнопка **Create Mod** (активна, только если игра видит Steam).
2. Для первой выкладки галочку **Update Existing** не ставьте.
3. **Pick Folder** -> выберите `StreamingAssets\AlternativeGameInterface` (папка верхнего уровня в StreamingAssets;
   `original` игра отклонит).
4. **Mod Name** (заголовок в Workshop): `Alternative Game Interface`.
   **Mod Description** (текст под картинкой): коротко, 2-3 предложения (можно взять первый абзац из `description.bbcode.txt`).
   Полное описание с BBCode потом вставьте на странице предмета в браузере или клиенте Steam.
5. **Pick Image** -> `thumbnail.jpg` из папки мода (меньше 1 МБ, лучше квадратная).
6. **Visibility**: для первой выкладки выберите ограниченную видимость (например, Private, если такой пункт есть),
   проверьте страницу, потом сделайте Public.
7. **Submit Mod**. Игра откроет страницу отправки в клиенте Steam: там видно, успешна ли загрузка.
   При первой публикации Steam может попросить принять соглашение Workshop (Steam Workshop Legal Agreement).
8. Steam присвоит предмету длинный номер (ID в адресе страницы), в менеджере модов имя может сначала показываться этим
   номером. `_info.ini` заменяет его на «Alternative Game Interface» / «Alternative Game Interface».

## 5. После выкладки

* Откройте страницу предмета: вставьте описание из `description.bbcode.txt`, добавьте скриншоты (положение окон до/после,
  окно за краем экрана).
* Если на странице предмета есть пункт про необходимые предметы (Required items), добавьте **Anchor Chain**
  (`https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757`): подписчики получат подсказку.
* Если используется тег типа «Mod Type: Scripting» (как у других модов на Anchor Chain), выставьте его, если он доступен.
* Чтобы проверить подписку: подпишитесь на свой мод и **удалите** локальную папку из StreamingAssets, иначе Anchor Chain найдёт две
  копии и загрузит только первую (в логе `Attempted to load a duplicate plugin`).
  Файлы подписки лежат в `steamapps\workshop\content\1286220\<ID>\`.

## 6. Обновление

1. Соберите новую dll (`dotnet build -c Release`), скопируйте содержимое папки `Release` в
   `StreamingAssets\AlternativeGameInterface` поверх старого.
2. **Mods -> Create Mod**, поставьте **Update Existing**, выберите свой мод из списка (показываются только первые 50 ваших модов).
   Поля имени и описания подтянутся из Steam.
3. Заполните **Change Log**, нажмите **Submit Mod**. Не создавайте новый предмет: получится дубль.

## 7. Что проверить перед публикацией

* 0.9.0 (2026-11-12): `_info.ini` now has `GreaterThanEqualToVersion` (now 0.9.0: older game versions are not supported) instead of `ApproximateVersion` (that one needs the same major.minor and fails on 0.9.0). The text below is about the old key.
* `_info.ini`: `ApproximateVersion` — нижняя граница: игра считает мод совместимым при той же 0.8 и патче не меньше указанного
  (0.8.3 принимает 0.8.3 … 0.8.6...). Несовместимому моду игра не скачивает обновления из Workshop, поэтому
  поднимайте значение только когда мод действительно перестал работать на старых версиях.
* Версия плагина: константа `PluginVersion` в `AlternativeGameInterfacePlugin.cs` (её берут `[BepInPlugin]`, `[ACPlugin]` и лог) и `<Version>` в csproj.
* В папке мода только 4 файла (`_info.ini`, dll, `thumbnail.jpg`, `README.md`).
* Текст на русском в `_info.ini` сохранён как UTF-8.
