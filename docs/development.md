# Сборка, тесты и структура проекта

## Сборка из исходников

Нужны:

- [.NET SDK 10](https://dotnet.microsoft.com/download) (версия из `SourceRefresher.csproj`);
- Visual Studio с компонентом **«Разработка классических приложений на C++»** (компоновщик NativeAOT);
- Git.

```bash
git clone https://github.com/VallFreya/SourceRefresher.git
cd SourceRefresher
dotnet publish -c Release -r win-x64
```

Готовая библиотека: `bin\Release\net10.0-windows\win-x64\publish\SourceRefresher.dll`. Скопируйте её в папку плагинов OBS (`<папка OBS>\obs-plugins\64bit`) при закрытом OBS и запустите его.

Если компоновщик не находит `vswhere.exe`, соберите из «Developer PowerShell for VS» или добавьте `C:\Program Files (x86)\Microsoft Visual Studio\Installer` в `PATH`.

## Тесты

Проверки лежат в `tests/SourceRefresher.Tests` (xUnit). Исходники плагина подключаются в них напрямую, OBS не нужен:

```bash
cd tests/SourceRefresher.Tests
dotnet test
```

Тесты окна открывают настоящие окна, поэтому им нужен рабочий стол Windows. Все файлы тесты пишут во временную папку, настоящие `config.json` и журнал не затрагиваются.

| Файл | Что проверяется |
|---|---|
| `RuleStateTests.cs` | Логика правила: таймер, ожидание тишины, сброс тишины звуком, неактивная сцена, «при запуске», плашка состояния |
| `ManagerTests.cs` | Менеджер с подменой OBS: ожидание конца загрузки, сохранение отсчёта при переименовании, одинаковые номера правил, сбои обновления и измерителя |
| `ConfigTests.cs` | Границы значений, испорченный и неполный `config.json`, запись и чтение, атомарная запись, форматы времени |
| `UiTests.cs` | Счётчик со стрелками, окно с десятками правил и любыми размерами, обе темы, источник без кнопки обновления |

## Структура проекта

| Файл / папка | Назначение |
|---|---|
| `Plugin.cs` | Точка входа: регистрация в OBS, конец загрузки, конфиг, окно, журнал |
| `PluginConfig.cs` | Модель конфига и границы значений |
| `RefreshRuleState.cs` | Логика одного правила (не знает про OBS) |
| `RefreshManager.cs` | Таймер, измерители источников, запуск обновления |
| `ObsRefresher.cs` | Обновление источника через свойства OBS |
| `LevelProbes.cs`, `ObsSceneApi.cs` | Измеритель звука и список сцен и источников |
| `SettingsForm.cs` | Окно настроек |
| `ThemedControls.cs`, `ThemedWidgets.cs`, `ThemedExtras.cs`, `ThemedDialog.cs` | Собственные контролы в стиле OBS |
| `ObsTheme.cs`, `Ui.cs`, `Loc.cs`, `AtomicFile.cs` | Темы, оформление, тексты на русском и английском, безопасная запись файла |
| `tests/SourceRefresher.Tests/` | Автоматические проверки |
