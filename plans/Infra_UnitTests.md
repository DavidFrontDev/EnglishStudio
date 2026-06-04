# План — Unit-тесты для EnglishStudio

> **Апдейт 2026-06-02:** фундамент частично заложен в рамках M11 Шаг 9. Создан
> `tests/EnglishStudio.Integration.Tests` (xUnit 2.9.3 + Test.Sdk 17.14.1 + EF Core Sqlite 10.0.8),
> добавлен в `EnglishStudio.slnx` под `/tests/`. Есть переиспользуемая `Infrastructure/SqliteInMemoryDb`
> (открытое `:memory:`-соединение + `IDbContextFactory`-обёртка; пока `EnsureCreated()`, не `Migrate()`).
> **NSubstitute заменён ручными двойниками** (`Infrastructure/Fakes.cs`) — пакета нет в офлайн-кэше; если
> понадобится для AI-evaluator'ов (раздел 3 §6), сначала проверить доступность в кэше/сети. Покрыты
> `MockBundlePicker` и `MockSessionService` (18 тестов). Остальные разделы ниже — ещё не сделаны.

**Создано:** 2026-05-29
**Контекст:** В решении **0 тестов** и ни одного тестового проекта. Стек: .NET 10, EF Core 10.0.8 + SQLite,
WPF (net10.0-windows), CommunityToolkit.Mvvm. Два DbContext: `DictionaryDbContext`
(`Modules.Dictionary`) и `IeltsDbContext` (`Modules.Ielts.Core`, через `IDbContextFactory`).
Критичный для экзамена scoring-код (band-маппинг, answer-checkers, FSRS) не покрыт ни одним тестом.

**Цель:** покрыть тестами в первую очередь **чистую детерминированную логику** (scoring, нормализация
ответов, подсчёт слов, FSRS, произношение), затем сервисный слой через SQLite in-memory. UI/WPF не тестируем.

---

## 0. Зона ответственности

| Слой | Файлы |
|---|---|
| **Test solution folder** | `tests/` (новая) |
| **Проекты** | `tests/EnglishStudio.Core.Tests/` (логика без БД), `tests/EnglishStudio.Integration.Tests/` (сервисы + EF) |
| **Solution** | `EnglishStudio.slnx` — добавить оба проекта в `<Folder Name="/tests/">` |
| **CI hook** | `dotnet test` должен проходить из корня без интерактива |

**НЕ ТРОГАЙ** продакшн-код, кроме случаев, когда тест выявил настоящий баг (тогда фикс отдельным шагом
с пометкой в acceptance). Не добавляй `InternalsVisibleTo`, пока не упрёшься в `internal`-член — почти всё `public`.

---

## 1. Технологический выбор

| Компонент | Выбор | Обоснование |
|---|---|---|
| Test runner | **xUnit** (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) | Дефолт для .NET, хорошо живёт с `dotnet test` |
| Assertions | **встроенные `Assert`** (без FluentAssertions) | FluentAssertions сменил лицензию на платную с v8 — не тащить |
| Моки | **NSubstitute** | Нужен для `IClaudeCliClient`, `IWhisperTranscriber`, `IAudioPlayer` |
| EF для интеграции | **SQLite in-memory** (`Microsoft.Data.Sqlite` + `UseSqlite(connection)` на открытом `:memory:` соединении) | НЕ EF InMemory provider — он не проверяет реляционные констрейнты/индексы, а у нас filtered unique indexes (polymorphic FK), которые надо проверять. SQLite in-memory исполняет реальные миграции. |

`global.json` уже пинит .NET 10.0.102 — тестовые проекты наследуют SDK.

---

## 2. Проект 1 — `EnglishStudio.Core.Tests` (логика без БД)

`dotnet new xunit -f net10.0`. ProjectReferences: `Modules.Ielts.Core`, `Modules.Ielts.Writing`,
`Modules.Dictionary`, `EnglishStudio.App` (для `PronunciationAssessor` — он лежит в App; если ссылка на
WPF-проект из теста проблемна, вынести `PronunciationAssessor` и `FsrsScheduler` тестируемую логику в
отдельную ссылаемую сборку или дублировать через `link`-файл — реши на месте, предпочтительно ссылка).

### Тестовые классы и кейсы

| Тестируемый класс | Файл | Ключевые кейсы |
|---|---|---|
| `OverallBandCalculator` | `Scoring/OverallBandCalculatorTests.cs` | официальное округление: avg .125→.25↑, .25→.5, .75→next whole, .875→next whole; clamp [0,9]; NaN→0; табличные `[Theory]` с реальными комбинациями 4 band'ов (взять из официальных IELTS-примеров) |
| `BandScoreMapper` | `Scoring/BandScoreMapperTests.cs` | Academic Reading raw→band по всей таблице (40→9, 39→9, 37→8.5, …, 0→… ); Listening аналогично; граничные значения; raw вне [0,40] |
| `AnswerNormalization` | `Scoring/AnswerNormalizationTests.cs` | case/whitespace/пунктуация; digit↔word (3↔three); plural/singular; ведущие/замыкающие пробелы; апострофы |
| `TextAnswerChecker` | `Scoring/TextAnswerCheckerTests.cs` | NMTW enforcement (ONE WORD AND/OR A NUMBER); acceptable answers list; синонимы; case-insensitive match; превышение word limit → wrong |
| `ChoiceAnswerChecker` | `Scoring/ChoiceAnswerCheckerTests.cs` | MCQ-Single; MCQ-Multi (выбор ДВУХ из A-E — порядок не важен, лишний выбор = wrong); частичный выбор |
| `MatchingAnswerChecker` | `Scoring/MatchingAnswerCheckerTests.cs` | matching headings/features; пропущенный матч; неверная буква |
| `IeltsWordCounter` | `Writing/IeltsWordCounterTests.cs` | hyphenated=1; числа=1; даты=1; contractions=1; пустой текст=0; множественные пробелы/переводы строк |
| `PronunciationAssessor` | `Audio/PronunciationAssessorTests.cs` | точное совпадение→100/Excellent; null/empty recognized→0/Unrecognized; частичное→Good/Poor границы 90/70; нормализация (регистр, пунктуация) |
| `FsrsScheduler` | `Srs/FsrsSchedulerTests.cs` | InitializeFromFirstReview для каждого рейтинга; стабильность растёт при Good/Easy; Again→Relearning+LapseCount++; интервал монотонен по рейтингу; детерминизм (одинаковый вход→одинаковый выход); все вычисления передают `now` явно (никаких `DateTime.Now` внутри) |

**Правило времени:** все методы, принимающие `now`, тестируются с фиксированным `DateTime`. Если внутри
найдётся скрытый `DateTime.Now`/`UtcNow` — это баг тестируемости, зафиксировать в acceptance и предложить
рефактор на инъекцию `TimeProvider`.

---

## 3. Проект 2 — `EnglishStudio.Integration.Tests` (сервисы + EF)

`dotnet new xunit`. ProjectReferences на все `Modules.*`.

### Инфраструктура

`SqliteInMemoryFixture` (базовый класс/фикстура):
```csharp
// Держит ОТКРЫТОЕ соединение ":memory:" на время теста (закрытие = потеря БД).
var conn = new SqliteConnection("DataSource=:memory:");
conn.Open();
var opts = new DbContextOptionsBuilder<IeltsDbContext>().UseSqlite(conn).Options;
using var db = new IeltsDbContext(opts);
db.Database.Migrate();   // прогоняет реальные миграции — проверяет filtered unique indexes
```
Для `IeltsDbContext` нужен `IDbContextFactory` — обернуть фабрикой над тем же соединением, либо
тестировать сервисы, принимающие фабрику, через тестовую реализацию `IDbContextFactory<IeltsDbContext>`,
возвращающую контексты на общем открытом соединении.

Аналогичная фикстура для `DictionaryDbContext`.

### Тестовые сценарии

| Сервис | Кейсы |
|---|---|
| `SeedService` (Dictionary) | идемпотентность `SeedIfEmptyAsync` (второй вызов не дублирует); `SeedAwl/Avl/Phave` теги; `BackfillAudioPathsAsync` |
| `ReadingSeedService` / `WritingSeedService` / `ListeningSeedService` | `SeedIfMissingAsync` импортирует ожидаемое число TestSet'ов (Reading 34, Writing 24, Listening 24) из embedded JSON; повторный вызов — no-op |
| `TestRunner` (`ITestRunner`) | старт attempt → SubmitAnswer → Finish; RawScore считается через answer-checkers; BandEstimate из `BandScoreMapper`; training-mode без таймера |
| `ReadingTestService` / `ListeningTestService` | `ListAsync` отдаёт корректные summary; `GetFullAsync` грузит Parts+Questions; `GetAttemptAsync` с ответами; `ClearAllAttemptsAsync` |
| `WritingTaskService` | `StartAttemptAsync` создаёт пустой attempt; `SubmitAttemptAsync` сохраняет текст + WordCount; `DeleteAttemptAsync` для Cancel; история |
| `SpeakingTestService` | `StartFullMockAsync` собирает `FullMockBundle` (Part1+Part2+Part3 связаны); `StartAttemptAsync` по режимам; `SaveResponseAsync`; `ClearHistoryAsync` |

**AI-сервисы (`WritingFeedbackService`, `SpeakingFeedbackService`, `ListeningFeedbackService`)** —
не дёргать реальный Claude CLI. Мок `IClaudeCliClient` через NSubstitute, возвращающий фиксированный JSON;
проверять, что сервис корректно парсит отчёт и сохраняет band'ы в attempt. Парсинг JSON-ответа evaluator'а —
отдельный приоритетный тест (хрупкое место).

---

## 4. Фазы

| Фаза | Содержание | Время |
|---|---|---|
| 1 | Создать `tests/EnglishStudio.Core.Tests`, добавить в slnx, написать тесты scoring (раздел 2: OverallBand + BandScoreMapper + checkers + WordCounter) | 3 ч |
| 2 | Дописать `PronunciationAssessor` + `FsrsScheduler` тесты | 2 ч |
| 3 | Создать `EnglishStudio.Integration.Tests` + `SqliteInMemoryFixture` + фабрика для IeltsDbContext | 2 ч |
| 4 | Seed-сервисы (идемпотентность + счётчики) + `TestRunner` | 3 ч |
| 5 | Сервисы секций (Reading/Listening/Writing/Speaking) | 3 ч |
| 6 | AI-evaluators с моком `IClaudeCliClient` (парсинг отчётов) | 2 ч |
| **Итого** | | **~15 ч** |

---

## 5. Acceptance

- `dotnet test` из корня репозитория проходит без интерактивного ввода, 0 падений
- Оба тестовых проекта в `EnglishStudio.slnx`, видны в VS
- Покрыты ВСЕ классы из раздела 2 (scoring/нормализация/WordCounter/Pronunciation/FSRS)
- Минимум по 1 интеграционному тесту на каждый seed-сервис и каждый сервис секции
- Каждый найденный при написании тестов баг продакшн-кода зафиксирован отдельным коммитом `fix:` со ссылкой на упавший тест
- Тесты детерминированы: нет обращений к сети, к реальному Claude CLI, к `%AppData%`, к системным часам без инъекции

---

## 6. Риски

| Риск | Митигация |
|---|---|
| Тест ссылается на WPF-проект (`PronunciationAssessor`/`FsrsScheduler` в App) → t* зависит от net10.0-windows | Вынести чистую логに в ссылаемую non-WPF сборку, либо `<Compile Include="..\..\src\...\X.cs" Link="X.cs" />` как временное решение, либо таргетировать тест-проект на net10.0-windows |
| EF InMemory не ловит constraint-нарушения polymorphic FK | Использовать SQLite in-memory с реальными миграциями (раздел 3) |
| `:memory:` теряется при закрытии соединения | Держать `SqliteConnection` открытым на всё время теста/фикстуры |
| Скрытый `DateTime.Now` в FSRS/scheduler делает тест флаки | Зафиксировать как баг, предложить `TimeProvider`; до фикса — допускать в тесте |
| Seed-счётчики (34/24/24) изменятся при добавлении контента | Тестировать `>= N` и идемпотентность, а не строгое `== N`, либо вынести ожидаемое число в константу рядом с seed |
