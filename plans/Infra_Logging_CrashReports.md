# План — Логирование (Serilog) + crash-репорты

**Создано:** 2026-05-29
**Контекст:** Приложение использует `Host.CreateDefaultBuilder()` в `App.OnStartup` (`App.xaml.cs:50`).
Сейчас логирования в файл нет, необработанные исключения роняют приложение без следов. Есть много мест,
где это больно: миграции/seed при старте, subprocess `claude` CLI, скачивание Whisper-моделей, NAudio
запись/воспроизведение, парсинг AI-JSON. Пользовательские данные лежат в `%AppData%\EnglishStudio\`.

**Цель:** структурное логирование через Serilog с ротацией в файл + перехват всех каналов необработанных
исключений с записью crash-дампа и понятным диалогом пользователю.

---

## 1. Пакеты

В `EnglishStudio.App.csproj`:
- `Serilog.Extensions.Hosting` — интеграция с `Microsoft.Extensions.Hosting`
- `Serilog.Sinks.File` — ротация в файл
- `Serilog.Sinks.Debug` — вывод в окно Output при отладке
- `Serilog.Settings.Configuration` + `Serilog.Sinks.Console` (опц.)
- `Serilog.Enrichers.Environment`, `Serilog.Enrichers.Thread` (опц., для контекста)

Логирование инжектится через `ILogger<T>` (Microsoft.Extensions.Logging абстракция) — модульные сервисы
(`Modules.*`) **не** должны ссылаться на Serilog напрямую, только на `Microsoft.Extensions.Logging.Abstractions`
(он уже транзитивно есть через Hosting). Serilog — только в App как провайдер.

---

## 2. Размещение логов

`%AppData%\EnglishStudio\logs\log-.txt` с rolling по дням (`rollingInterval: Day`), лимит размера файла,
`retainedFileCountLimit: 14`. Путь строить через `Environment.GetFolderPath(SpecialFolder.ApplicationData)` +
`EnglishStudio\logs` (тот же корень, что и БД — единое место данных приложения).

Crash-дампы отдельно: `%AppData%\EnglishStudio\crashes\crash-{yyyyMMdd-HHmmss}.log` (см. раздел 4) —
**имя файла со временем формируется в обработчике**, где `DateTime.Now` допустим (не в workflow-скрипте).

---

## 3. Конфигурация Serilog в App.xaml.cs

Сконфигурировать `Log.Logger` **до** создания Host (чтобы ловить ошибки самого старта/миграций), затем
подключить к Host через `.UseSerilog()`:

```csharp
// До Host.CreateDefaultBuilder:
var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                          "EnglishStudio", "logs");
Directory.CreateDirectory(logDir);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File(Path.Combine(logDir, "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 10_000_000,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .WriteTo.Debug()
    .CreateLogger();

_host = Host.CreateDefaultBuilder()
    .UseSerilog()                       // <-- заменяет дефолтных провайдеров логирования
    .ConfigureServices(services => { /* как сейчас */ })
    .Build();
```

В `OnExit` добавить `Log.CloseAndFlush()`.

Обернуть текущий блок старта (миграции + seed, `App.xaml.cs:115-145`) в try/catch с
`Log.Fatal(ex, "Startup failed")` + показ диалога (раздел 4) и graceful shutdown — сейчас падение
миграции/seed убивает приложение молча.

---

## 4. Глобальные обработчики необработанных исключений

Подписаться на **все три** канала (каждый ловит свой класс ошибок):

| Канал | Где | Ловит |
|---|---|---|
| `DispatcherUnhandledException` | `App` (UI-поток WPF) | исключения в обработчиках событий/биндингах |
| `AppDomain.CurrentDomain.UnhandledException` | весь процесс | фоновые потоки, фатальные |
| `TaskScheduler.UnobservedTaskException` | проглоченные `Task` | незаосерванные async-исключения |

Класс `CrashHandler` (в `App/Diagnostics/`):
- `Log.Fatal(ex, ...)` в общий лог
- Пишет отдельный `crash-{timestamp}.log` с: исключение + стек, версия приложения, ОС, .NET runtime, путь к логам, последние N строк из текущего лог-файла (для контекста)
- Показывает диалог пользователю: «Произошла ошибка. Отчёт сохранён в …\crashes\. [Открыть папку] [Закрыть]»
- Для `DispatcherUnhandledException`: `e.Handled = true` только для **некритичных** (иначе приложение в неconsistent-состоянии — лучше упасть после показа диалога). Решить по типу: биндинг-ошибки можно глотать, остальное — лог + завершение.
- `AppDomain.UnhandledException` (`IsTerminating`) — приложение всё равно умрёт; успеть записать crash-файл и `Log.CloseAndFlush()`.

Диалог — простой `ChromedWindow` (базовый класс уже есть для child-окон) или `MessageBox` как минимальный вариант. Предпочтительно лёгкое окно `CrashDialog` в стиле темы.

---

## 5. Расстановка логов по болевым точкам

После инфраструктуры — пройтись `ILogger<T>` по местам, где ошибки сейчас невидимы:

| Область | Что логировать |
|---|---|
| Старт (App) | начало/конец миграций каждого DbContext, результаты seed (сколько импортировано), общее время старта |
| `ClaudeCliClient` (Modules.Ai) | команда (без чувствительного), exit code, длительность, stderr при ошибке, таймауты |
| AI-evaluators | сырой JSON-ответ при ошибке парсинга (Warning), итоговые band'ы (Information) |
| `WhisperTranscriber` | старт/прогресс/конец скачивания модели, путь, ошибки native-загрузки |
| `NAudioRecorder`/`NAudioPlayer`/`ListeningAudioPlayer` | старт/стоп записи, ошибки устройства (нет микрофона/динамика) |
| Seed-сервисы | пропуск (уже засижено) vs импорт N записей |
| `CambridgeSpeakingImportService` | найден/не найден источник, сколько тестов импортировано (важно — это известное хрупкое место) |

Уровни: `Information` — нормальный ход; `Warning` — деградация (CLI не найден, парсинг с fallback);
`Error` — операция упала, но приложение живо; `Fatal` — только в crash-обработчиках.

---

## 6. Просмотр логов из приложения (опц., низкий приоритет)

В `SettingsWindow` добавить кнопку «Открыть папку логов» (`Process.Start("explorer", logDir)`) и, опционально,
«Открыть последний crash-отчёт». Это резко упрощает диагностику без лазания в `%AppData%`.

---

## 7. Фазы

| Фаза | Содержание | Время |
|---|---|---|
| 1 | Пакеты + конфиг `Log.Logger` + `.UseSerilog()` + `CloseAndFlush` в OnExit | 1.5 ч |
| 2 | Обернуть startup (миграции/seed) в try/catch с Fatal + диалог | 1 ч |
| 3 | `CrashHandler` + подписка на 3 канала + crash-файл + диалог | 2.5 ч |
| 4 | Расставить `ILogger<T>` по болевым точкам (раздел 5) | 2.5 ч |
| 5 | Кнопки «Открыть логи/crash» в Settings | 0.5 ч |
| **Итого** | | **~8 ч** |

---

## 8. Acceptance

- При запуске создаётся `%AppData%\EnglishStudio\logs\log-<date>.txt` с записями старта и миграций
- Логи ротируются по дням, хранятся ≤14 файлов
- Брошенное необработанное исключение (тест: кинуть из команды кнопки) → пишется `crashes\crash-*.log` + показывается диалог, приложение не исчезает молча
- Все три канала исключений покрыты (UI-поток, фоновый поток, unobserved task)
- Модульные сервисы используют `ILogger<T>`, не ссылаются на Serilog напрямую
- `Log.CloseAndFlush()` гарантирует, что последний лог не теряется при выходе
- Падение миграции/seed логируется как Fatal и показывает пользователю понятное сообщение
- Из Settings открывается папка логов

---

## 9. Риски

| Риск | Митигация |
|---|---|
| `Log.Logger` не сконфигурирован до первой ошибки старта | Конфигурировать ДО `Host.CreateDefaultBuilder` |
| `e.Handled=true` на критичном исключении → приложение в битом состоянии | Глотать только биндинг/некритичные; остальное — лог + контролируемое завершение |
| Двойное логирование (дефолтные провайдеры + Serilog) | `.UseSerilog()` заменяет дефолтные провайдеры |
| Логи разрастаются | `fileSizeLimitBytes` + `retainedFileCountLimit` |
| Чувствительные данные в логах (текст эссе, токены) | Не логировать тело пользовательских текстов и аргументы CLI с возможными секретами; только метаданные |
| `AppDomain.UnhandledException` не успевает записать перед смертью процесса | Синхронная запись crash-файла + `CloseAndFlush` в обработчике |
