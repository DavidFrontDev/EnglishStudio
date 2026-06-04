# EnglishStudio — IELTS Sections Implementation Plan

**Создано:** 2026-05-22
**Цель:** превратить словарный тренажёр M0–M6 в полноценный IELTS-симулятор уровня экзамена, способный по результатам тестов выставлять реалистичный band score (0.0–9.0) по официальной шкале IELTS Academic.

## Статус (актуально на 2026-06-03)

> **Все 4 IELTS-секции + полный mock-экзамен закрыты.** Приложение функционально полное: 8 рабочих
> модулей в sidebar, ни одной заглушки. Ниже — обновлённая таблица; деталь по mock'у — в
> `plans/M11_Mock_Remaining.md`, по тестам — в `plans/Infra_UnitTests.md`.

| Этап | Статус | Что готово |
|---|---|---|
| **M7.0** Архитектура (Shell + Core + Ai) | ✅ done | Sidebar-навигация, 6 module classlib, IeltsDbContext + миграция, scoring/answer-checking, Claude CLI клиент v2.1.148, evaluators с полными rubrics, расширенный SettingsWindow с общей dark-шапкой через `ChromedWindow` |
| **M7.1** Reading: SeedService + топики | ✅ done | `ReadingTestDto`, `ReadingSeedService`, embedded `ielts_reading_tests.json` |
| **M7.2** Reading: сервисный слой | ✅ done | `IReadingTestService` + read-models через `IDbContextFactory<IeltsDbContext>` |
| **M7.3** Reading: ViewModels | ✅ done | 5 question-VM (TFNG/Text/MCQ-Single/MCQ-Multi/Matching) покрывают все 14+3 типа, `ReadingTestViewModel` с таймером и auto-save, `ReadingHubViewModel`, `ReadingResultViewModel` |
| **M7.4** Reading: Views XAML | ✅ done | Hub + Test (2-pane: passage / questions) + Result (band card + breakdown + wrong-answer review). Auto-wrap во всех колонках, `WrapPanel` для TFNG-чипов, отдельные `BoolToVis`/`NullToVis` ресурсы |
| **M7.5** Reading: контент 30 тестов | ✅ done | В seed **30 тестов** (`acad-r-test01`…`acad-r-test30`), импортированы из `C:\Users\tvore\Downloads\Тесты\Tests Academic\Reading\` по правилам [FORMATTING_RULES.md](../../Downloads/Тесты/Tests%20Academic/FORMATTING_RULES.md). 6 тестов с изображениями: test13 (карта Крита), test17 (карта Великой стены), test19 (план Олимпии), test22 (план Помпей), test27 (диаграмма вулкана), test28 (карта Транссиба). Tool `tools/IeltsReadingGen/` остаётся доступным для генерации дополнительных тестов. |
| **M7.6** Reading: экзаменационные тесты | ✅ done | 4 теста **(test31–test34) помечены `IsExamOnly=true`** в JSON и в TestSet (новое поле + миграция `AddIsExamOnly`). Для exam-only: кнопка «Тренировка» в Hub отключена, обратная навигация (`PrevPart`, `JumpToQuestion` к более ранним passage) заблокирована, badge «🎯 Экзамен» в карточке. `ChoiceAnswerChecker` расширен `AcceptableAnswers` для MCQ-Single, чтобы корректно обрабатывать «выбери ДВЕ буквы из A-E». Один из этих 4 тестов реально попадался на экзамене. |
| **M9.0** Writing: архитектура + UI + 4 верифицированных Cambridge-теста | ✅ done | `Modules.Ielts.Writing` classlib, сущности `WritingTask`/`WritingModelAnswer`/`WritingAttempt` + `WritingTaskKind`/`WritingChartType` в Core, миграции `AddWritingSchema` и `AddWritingTestSetLink` (WritingTask.TestSetId nullable FK + OrderInSet — переиспользование Core.TestSet с Section=Writing). Сервисы: `IWritingTaskService` (Hub-список TestSet + детали), `WritingFeedbackService` (Claude CLI evaluator wrapper), `WritingSeedService` (TestSet+2 Tasks+image extract). Seed: **4 Cambridge IELTS 19 теста** (LineGraph/Map/ProcessDiagram/MultipleCharts × 4 essay типа) с embedded PNG (4.6 МБ) и 8 band-8 model answers, оригинальные (270-330 слов, IELTS-style). UI: WritingHubView (карточки TestSet'ов), `WritingSessionView` (единый таймер 60 мин, табы Task 1/Task 2 с индикатором word-count, 2-pane image+prompt / textbox с auto-save), `WritingResultView` (combined overall band по официальной формуле T1×⅓+T2×⅔ → .5-snap, отдельные карточки Task 1/Task 2 с 4 критериями + RU/EN feedback + issues, Expander с ответом, toggle «📘 Показать эталон band 8»). FORMATTING_RULES.md в `Tests Academic\Writing\`. Smoke-тест пройден: Claude CLI оценивает обе задачи и возвращает все 4 band. |
| **M9.1** Writing: расширение контента | ✅ done | Пользователь вручную импортировал **24 теста** (Cambridge 15–20, по 4 на книгу; `acad-w-testNN`, book/test зашиты в Title «IELTS Test Book {N}, Test {M}»). Каждый — Task 1 + Task 2 с band-8 эталонами. |
| **M10** Speaking | ✅ done | `Modules.Ielts.Speaking`: Cambridge .txt-импорт (24 теста), Whisper-medium с word-timestamps, `SpeechMetricsAnalyzer` (WPM/паузы/филлеры/TTR), AI band-оценка через Claude CLI, cue-card subpoints в UI. Full mock привязан к Cambridge-тесту (`StartFullMockAsync(part2BankId)`). Прогресс транскрибации (%) + полноценный плеер ответов (play/pause/stop/seek) в результате. |
| **M8** Listening | ✅ done | 24 теста Cambridge 15–20 с реальными MP3, `IListeningTestService` через `ITestRunner`/`TestAttempt`, карусель из 5 типов карточек, AI-оценка. |
| **M11** Mock Test | ✅ done | Модуль `Modules.Ielts.Mock`: `MockBundlePicker` (все 4 секции из одной Cambridge-книги — L/R/S по коду, Writing по Title), `MockSessionService` (оркестратор Start/Begin/Complete/Skip/Finalise + посекционный resume), хост-автомат L→R→W→S, `MockResultView` (4 sub-band + overall по официальной формуле + разбор каждой секции). 19 интеграционных тестов (`tests/EnglishStudio.Integration.Tests`). Осталась только ручная проверка живьём (пройдена пользователем). |

**Параллельно сделано вне исходного плана:**
- Общий `ChromedWindow` базовый класс + стиль `ChromedWindowStyle` — все child-окна имеют одинаковую тёмную шапку с Min/Max/Close через `SystemCommands`.
- Тематический `ConfirmWindow` заменил системный `MessageBox` во всех hub'ах (clear-history); `MessageBox.Show` в App не осталось.
- Увеличение картинок заданий L/R/W: `ZoomableImage` (лупа на hover) + `ImageZoomWindow` (открывается на 50%, ползунок до 200%, окно авто-подгоняется под картинку ≤92% экрана, перетаскивание + крестик).
- Первый тест-проект репо `tests/EnglishStudio.Integration.Tests` (xUnit + EF Core Sqlite in-memory).
- Уязвимость `System.Drawing.Common 4.7.0` (NU1904, транзитивно из DotNetZip) закрыта override'ом на 9.0.9.
- Палитра подправлена: `MutedTextBrush` и `CaptionTextBrush` стали светлее.

**Не закрыто (полировка/инфра, не блокирует):** режим Custom в mock'е; «тёплый» resume черновиков Writing + чистка orphan-attempt'ов (follow-ups в `M11_Mock_Remaining.md`); светлая тема (`Light.xaml` — заглушка) и переключение темы; широкое покрытие unit-тестами (`Infra_UnitTests.md`); installer и логирование/crash-репорты (`Infra_*.md`); улучшения произношения M6+ (`M6plus_Pronunciation.md`).

---

## 0. Контекст

### Что уже сделано (M0–M6)

- WPF .NET 10 каркас, EF Core 10 + SQLite, DI через Microsoft.Extensions.Hosting, тема DarkBlue
- Словарь: 6 250 Words + 150 PhrasalVerbs + 304 Collocations + 16 600 Translations + 48 235 Examples (включая Tatoeba)
- Аудио произношений (M2): lazy bulk-download split-zip, NAudio плеер
- Категории/теги для IELTS (M3): 15 Speaking topics, irregular verbs, linking phrases, Task 1 trend vocabulary, collocations
- Изображения и расширенные примеры (M4): Wikimedia + Pexels providers, MediaAsset
- SRS-тренажёр (M5): FSRS-4.5 на Word/PhrasalVerb/Collocation, polymorphic UserWordProgress, ReviewLog
- Тренировка произношения (M6): NAudio recorder + Whisper.net (ggml-base.en) + Levenshtein scoring + PronunciationAttempt entity

### Существующие проекты

```
src/
├── EnglishStudio.App/                    (WPF UI, ViewModels, темы)
└── EnglishStudio.Modules.Dictionary/     (entities, DbContext, services)
```

### Существующий MainWindow

3-табовый TabControl: Словарь / Тренажёр / Статистика. Перерастёт это с приходом 4 IELTS-секций — план меняет на Shell+Sidebar.

---

## 1. Зафиксированные решения (из обсуждения 2026-05-22)

| № | Вопрос | Решение |
|---|---|---|
| 1 | AI для оценки Writing/Speaking | Claude CLI subprocess (`claude -p ... --output-format json`), использует Max-подписку, версия 2.1.148 уже установлена |
| 2 | TTS для Listening | Windows.Media.SpeechSynthesis (WinRT), 4 нейронных голоса установлены: Sonia (UK F), Ryan (UK M), Aria (US F), Guy (US M) |
| 3 | Хостинг контента | Не нужен — личное использование, всё локально в `%AppData%\EnglishStudio\IeltsContent\` |
| 4 | Whisper-модель | `ggml-medium.en` (~1.5 ГБ) для Speaking long-form; `ggml-base.en` остаётся для M6 single-word |
| 5 | Module split | 5 новых classlib: `Modules.Ai`, `Modules.Ielts.Core`, `Modules.Ielts.Reading`, `Modules.Ielts.Listening`, `Modules.Ielts.Writing`, `Modules.Ielts.Speaking` |
| 6 | Экзаменационная точность | Все 4 секции по официальному IELTS формату + Mock Test (M11) с расчётом overall band по официальной формуле округления |
| 7 | Генерация контента | Главный агент (я), без параллельных подагентов, последовательно, с фокусом на качество. Объём — максимальный (Variant A). |
| 8 | Порядок реализации | M7.0 → M7 Reading → M9 Writing → M10 Speaking → M8 Listening → M11 Mock |

---

## 2. Целевая структура проекта

```
src/
├── EnglishStudio.App/                            (UI, ViewModels, темы, Shell)
├── EnglishStudio.Modules.Dictionary/             (без изменений)
├── EnglishStudio.Modules.Ai/                     (новый: Claude CLI клиент, evaluators)
├── EnglishStudio.Modules.Ielts.Core/             (новый: общая инфра квизов, scoring)
├── EnglishStudio.Modules.Ielts.Reading/          (новый)
├── EnglishStudio.Modules.Ielts.Writing/          (новый)
├── EnglishStudio.Modules.Ielts.Speaking/         (новый)
└── EnglishStudio.Modules.Ielts.Listening/        (новый)

tools/
├── IeltsReadingGen/                              (новый: генератор Reading через Claude CLI)
├── IeltsListeningGen/                            (новый: генератор Listening transcripts + TTS)
├── IeltsWritingPromptGen/                        (новый: генератор Writing prompts)
├── IeltsTask1ChartGen/                           (новый: ScottPlot генератор графиков)
├── IeltsSpeakingBankGen/                         (новый: генератор Speaking question bank)
└── IeltsContentBuilder/                          (новый: оркестратор всех генераторов)
```

---

## 3. Объём контента

| Секция | Объём | Источник |
|---|---|---|
| Reading | 30 полных тестов × 3 passage × 13-14 q = **~1200 вопросов** | Главный агент через Claude CLI генерирует passage + questions + answer key |
| Listening | 30 полных тестов × 4 секции × 10 q = **1200 вопросов** + 120 MP3 | Главный агент генерирует transcripts; WindowsTtsService озвучивает |
| Writing | **100 Task1Academic + 50 Task1GT + 150 Task2 = 300 prompts** | Главный агент генерирует prompts + model answers band 7/8/9 для половины |
| Speaking | **40 Part1 topics × 5q + 50 Part2 cue cards + 50 × 8 Part3 = 600+ q** | Главный агент генерирует question bank |
| **Итого** | **~3500 экзаменационных единиц** | |

---

# M7.0 — Архитектурная подготовка

**Цель:** заложить инфраструктуру для всех IELTS-секций до начала их реализации.

## M7.0.1 — Shell + Sidebar навигация

Замена `TabControl` в MainWindow на двухпанельный Shell.

| Файл | Назначение |
|---|---|
| `EnglishStudio.App/Shell/IModuleDescriptor.cs` | `Code` (string), `NameRu` (string), `IconGlyph` (string, emoji), `Order` (int), `ViewFactory` (Func), `ViewModelType` |
| `EnglishStudio.App/Shell/ModuleDescriptor.cs` | record-реализация |
| `EnglishStudio.App/Shell/ShellViewModel.cs` | `ObservableCollection<IModuleDescriptor> Modules`, `IModuleDescriptor CurrentModule`, `NavigateCommand` |
| `EnglishStudio.App/Shell/ShellView.xaml` | Grid: левая колонка 220px (sidebar), правая `ContentControl{Binding CurrentView}`. Sidebar = `ItemsControl` с `ToggleButton`-стилем |
| `EnglishStudio.App/Themes/Controls.xaml` | + стиль `SidebarItem` (selected state, hover, icon+text) |
| `MainWindow.xaml` | TabControl → `<shell:ShellView />`; шапка остаётся |

**Зарегистрированные модули после M7.0:**
- 📖 Словарь (DictionaryView)
- 🎯 Тренажёр (TrainerView)
- 📊 Статистика (StatsView)
- 📚 Reading (заглушка)
- 🎧 Listening (заглушка)
- ✍ Writing (заглушка)
- 🎤 Speaking (заглушка)
- 🏁 Mock Test (заглушка, добавится в M11)

## M7.0.2 — Module classlib проекты

Создать 6 новых classlib через `dotnet new classlib -f net10.0`:
- `EnglishStudio.Modules.Ai`
- `EnglishStudio.Modules.Ielts.Core`
- `EnglishStudio.Modules.Ielts.Reading`
- `EnglishStudio.Modules.Ielts.Listening`
- `EnglishStudio.Modules.Ielts.Writing`
- `EnglishStudio.Modules.Ielts.Speaking`

Каждый получает: `AddXxxModule(IServiceCollection)` extension, ссылку на `EnglishStudio.Modules.Dictionary` (общий DbContext) или `Modules.Ielts.Core`.

Регистрация в `App.xaml.cs.ConfigureServices`:
```csharp
services.AddDictionaryModule()
        .AddAiModule()
        .AddIeltsCoreModule()
        .AddIeltsReadingModule()
        .AddIeltsListeningModule()
        .AddIeltsWritingModule()
        .AddIeltsSpeakingModule();
```

## M7.0.3 — Общая квиз-инфра (`Ielts.Core`)

**Сущности (миграция `AddIeltsCoreSchema` в DictionaryDbContext):**

```csharp
public enum IeltsSection { Reading, Listening, Writing, Speaking }
public enum IeltsTestMode { Academic, GeneralTraining }
public enum ContentSource { Bundled, Generated, UserImported }

public enum QuestionType
{
    TrueFalseNotGiven,
    YesNoNotGiven,
    MultipleChoiceSingle,
    MultipleChoiceMulti,
    MatchingHeadings,
    MatchingInformation,
    MatchingFeatures,
    MatchingSentenceEndings,
    SentenceCompletion,
    SummaryCompletion,
    NoteCompletion,
    TableCompletion,
    FlowChartCompletion,
    ShortAnswer,
    FormCompletion,      // Listening
    MapLabeling,         // Listening
    DiagramLabeling      // Listening
}

public class TestSet
{
    public int Id;
    public string Code;              // unique
    public string Title;
    public IeltsSection Section;
    public IeltsTestMode Mode = IeltsTestMode.Academic;
    public ContentSource Source;
    public string? AuthorAttribution;
    public DateTime CreatedAt;
    public ICollection<TestPart> Parts;
}

public class TestPart
{
    public int Id;
    public int TestSetId;
    public int OrderInTest;
    public string Title;
    public string? BodyText;         // passage / transcript / writing prompt
    public string? IntroNoteRu;
    public int? AudioMediaAssetId;
    public int? ImageMediaAssetId;
    public ICollection<TestQuestion> Questions;
}

public class TestQuestion
{
    public int Id;
    public int TestPartId;
    public int OrderInPart;
    public int? GroupId;             // объединение вопросов в группу
    public QuestionType Type;
    public string Stem;
    public string? OptionsJson;      // JSON массив для choices/matchings
    public string AnswerKeyJson;     // canonical answer
    public string? AcceptableAnswersJson;  // синонимы, эквиваленты
    public int Points = 1;
    public int? WordLimitMax;        // NMTW лимит
}

public class TestAttempt
{
    public int Id;
    public int TestSetId;
    public DateTime StartedAt;
    public DateTime? FinishedAt;
    public int DurationSeconds;
    public int RawScore;
    public double BandEstimate;
    public bool IsTrainingMode;      // training = no timer / unlimited replays
    public ICollection<TestAnswer> Answers;
}

public class TestAnswer
{
    public int Id;
    public int TestAttemptId;
    public int TestQuestionId;
    public string UserAnswerJson;
    public bool IsCorrect;
    public int PointsEarned;
}
```

**Сервисы (`Ielts.Core/Scoring/`):**

| Файл | Что делает |
|---|---|
| `IBandScoreMapper` + `BandScoreMapper` | Static lookup: Academic Reading 39-40→9, 37-38→8.5, 35-36→8, 33-34→7.5 и т.д. Academic Listening аналогично. |
| `IAnswerChecker` + реализации | `TextAnswerChecker` (case/whitespace/punctuation normalize, plural-singular, digit↔word, NMTW enforcement), `ChoiceAnswerChecker`, `MatchingAnswerChecker` |
| `OverallBandCalculator` | Среднее по 4 секциям с официальным IELTS-округлением (.25 → .5 вверх, .75 → следующее целое вверх) |
| `ITestRunner` | Управление активным TestAttempt: timer, current question index, save answer, finish |

## M7.0.4 — AI модуль (`Modules.Ai`)

| Файл | Назначение |
|---|---|
| `IClaudeCliClient` + `ClaudeCliClient` | `RunAsync(prompt, outputFormat, sessionId?, ct)` → `ClaudeResponse(Text, SessionId, Cost?)`. Process.Start с redirected I/O. |
| `ClaudeCliLocator` | Auto-detect `claude.exe`: PATH → `%LOCALAPPDATA%\Programs\claude\` → fallback на user settings |
| `ClaudeOutputFormat` enum | `Text`, `Json`, `StreamJson` |
| `ClaudeSessionCache` | Кэширует `session-id` per evaluator type для prompt caching эффекта |
| `IIeltsEssayEvaluator` + `ClaudeIeltsEssayEvaluator` | `EvaluateAsync(taskType, prompt, userText, ct)` → `EssayScoreReport` |
| `IIeltsSpeakingEvaluator` + `ClaudeIeltsSpeakingEvaluator` | `EvaluateAsync(partType, questions, transcripts, metrics, ct)` → `SpeakingScoreReport` |
| Embedded resource `IeltsRubric_Writing.md` | Полный официальный rubric Writing band 0-9 по 4 критериям |
| Embedded resource `IeltsRubric_Speaking.md` | Полный официальный rubric Speaking band 0-9 по 4 критериям |

**Report types:**

```csharp
public record EssayScoreReport(
    double TaskAchievement,
    double CoherenceCohesion,
    double LexicalResource,
    double GrammaticalRangeAccuracy,
    double Overall,
    string FeedbackEn,
    string FeedbackRu,
    IReadOnlyList<EssayIssue> Issues);

public record EssayIssue(string Category, string Quote, string ExplanationRu, string Suggestion);
```

**`Settings` расширение:** `ClaudeCliPath` (auto-detected), `ClaudeMaxConcurrent` (default 1 — Max-подписка имеет per-account rate limit).

## M7.0.5 — Settings расширение

`SettingsWindow.xaml` обновить — добавить разделы:
- **AI:** статус Claude CLI («✓ Найден, v2.1.148» / «✗ Не установлен» + кнопка «Проверить заново»)
- **Speaking:** качество Whisper (Base / Medium)
- **Listening:** TTS-голоса (4 dropdown: UK F / UK M / US F / US M, по умолчанию Sonia/Ryan/Aria/Guy)

## M7.0 — Объём и acceptance ✅

**Файлов:** ~25 .cs + 4 .xaml + 1 миграция (`AddIeltsCoreSchema`) + ~500 строк rubric.md
**Размер:** ~1800 строк
**Acceptance (все выполнены):**
- ✅ MainWindow показывает Shell с 8 модулями в sidebar
- ✅ Клик по любому переключает ContentControl
- ✅ 5 IELTS-модулей зарегистрированы (Reading — реальный, Listening/Writing/Speaking/Mock — placeholder'ы)
- ✅ Миграция `AddIeltsCoreSchema` применена, 5 таблиц видны в БД
- ✅ Claude CLI auto-detect работает (v2.1.148 найден через `where claude`)
- ✅ В Settings новые разделы AI / Speaking (Whisper Base/Medium) / Listening voices
- ✅ Общий `ChromedWindow` базовый класс для child-окон

---

# M7 — Reading

## Сущности
Используют `Ielts.Core` напрямую. Дополнительные миграции не нужны.

## Контент

**Объём:** 30 полных Academic Reading тестов = 90 passages × 13-14 вопросов = ~1200 вопросов.

**Топики (curated seed, без пересечений):**

30 unique topics в трёх группах сложности:
- Group 1 (passage 1, factual): history of inventions, ancient civilizations, animal behaviour, plants, archaeology, geography, transport history, food production, sports origins, traditional crafts
- Group 2 (passage 2, opinion/argument): education methods, urban planning, environmental policy, workplace trends, social media impact, public health, economic theories, technology adoption, language preservation, immigration
- Group 3 (passage 3, academic complex): cognitive science, climate research, materials science, astronomy, evolutionary biology, linguistics, neuroscience, economics theory, philosophy of science, anthropology

Полный seed-список топиков фиксируется в `tools/IeltsReadingGen/Seeds/topics.json` до начала генерации.

**Question type density per test (соответствует реальному IELTS):**
- Passage 1: 13 q = 7 sentence completion + 6 T/F/NG
- Passage 2: 14 q = 5 multiple choice + 4 matching headings + 5 short answer
- Passage 3: 13 q = 4 matching information + 5 matching features + 4 summary completion

Распределение варьируется между тестами, но все 11 типов покрыты.

## Генератор (`tools/IeltsReadingGen/`)

C# console app, последовательная генерация. На каждый passage:
1. Загружает rubric для Reading из embedded resource
2. Загружает один topic seed
3. Формирует prompt для Claude CLI: `--output-format json`, выход — структурированный JSON по схеме
4. Запускает `claude -p` через `ClaudeCliClient`
5. Валидирует ответ против JSON schema
6. Запускает второй проход «validator»: отдельный CLI-запрос проверяет сгенерированный тест по 20-чек-листу
7. Если validator score < 8/10 → regen (до 3 попыток)
8. Записывает passing tests в `Seed/reading_tests.json` + бэкап в `tools/IeltsReadingGen/output/`

**Время генерации:** ~30 passages × ~60 сек на passage + ~30 сек на validator = ~50 минут. Без параллелизма, чтобы избежать rate-limit и сохранить качество.

## Сервисы (`Modules.Ielts.Reading/`)

| Класс | Что |
|---|---|
| `ReadingTestService` | `GetAvailableTestsAsync()`, `StartAttemptAsync(testId, trainingMode)`, `SubmitAnswerAsync(attemptId, qId, answer)`, `FinishAttemptAsync(attemptId)` |
| `ReadingSeedService` | Импорт `reading_tests.json` в БД при первом запуске |
| `ReadingAnswerChecker` | Использует `IAnswerChecker` + специфика NMTW |

## ViewModels / Views (`EnglishStudio.App/Views/Reading/`)

| Файл | Назначение |
|---|---|
| `ReadingHubViewModel/View` | Список тестов (карточки с topic + difficulty + last attempt band), фильтр completed/new, кнопка «Начать» (выбор training/exam mode) |
| `ReadingTestViewModel/View` | 2 колонки: passage (Scroll, выделение текста, highlight найденных ответов) ↔ panel вопросов. Top bar: таймер 60 мин (визуальный прогресс), индикатор «Passage 2/3», кнопки «Prev/Next passage», «Завершить». Bottom bar: progress 22/40 |
| `ReadingQuestionTemplateSelector` | DataTemplateSelector по `QuestionType` → один из 11 UserControl'ов |
| `Views/Reading/QuestionTemplates/TfngTemplate.xaml` и ещё 10 шаблонов | Каждый тип — отдельный UserControl ~50 строк XAML |
| `ReadingResultViewModel/View` | Финальный экран: raw score X/40, band big, breakdown по типам вопросов (где слабее), список ошибок с возможностью кликнуть → подсветка правильного ответа в passage |

## Acceptance M7 ✅
- ✅ **30/30 тестов в seed** (`acad-r-test01`…`acad-r-test30`), импортированы из `Tests Academic\Reading\` с выверкой по Answer-файлу. 6 тестов с изображениями: test13/17/19/22/27/28 (карты/планы/диаграмма)
- ✅ Все 14+ типов вопросов работают, checker даёт корректный raw score (TFNG/YNNG/MCQ-Single/MCQ-Multi/Matching×4/Completion×6/ShortAnswer + резерв на Listening Map/Form)
- ✅ Таймер 60 мин с auto-finish; training mode без таймера
- ✅ BandEstimate выводится по официальной academic-таблице (Cambridge raw→band)
- ✅ Attempts сохраняются в БД (TestAttempts/TestAnswers)
- ✅ Результат-экран показывает band, breakdown по типам, неверные ответы с правильными

**Объём:** ~30 .cs + 15 .xaml + ~3000 строк + seed.

### Подэтапы выполнения
- **M7.1** ✅ SeedService + DTO + embedded JSON
- **M7.2** ✅ ReadingTestService + summary read-models
- **M7.3** ✅ Question VMs (5 типов покрывают 17 enum-значений через factory) + Test/Hub/Result VMs
- **M7.4** ✅ XAML views (Hub + Test 2-pane + Result + Module-router) + DI-регистрация + замена placeholder на ReadingModuleView в sidebar
- **M7.5** ✅ Контент. Основной путь: **ручной импорт** из `C:\Users\tvore\Downloads\Тесты\Tests Academic\Reading\` (30/30 готово). Авто-генератор `tools/IeltsReadingGen/` сохраняется для генерации дополнительных тестов по запросу.

---

# M9 — Writing

## Сущности (миграция `AddWritingSchema` в `Modules.Ielts.Writing`)

```csharp
public enum WritingTaskType { Task1Academic, Task1GeneralTraining, Task2 }
public enum WritingChartType { LineGraph, BarChart, PieChart, Table, ProcessDiagram, Map, MultipleCharts }

public class WritingTask
{
    public int Id;
    public string Code;
    public WritingTaskType Type;
    public string PromptText;
    public string? ChartSpecJson;       // для Task1: JSON-спека для ScottPlot
    public string? ImagePath;           // для Task1 GT (письма): не используется; для Process/Map — путь к PNG
    public int MinWords;                // 150 для Task1, 250 для Task2
    public int RecommendedMinutes;      // 20 / 40
    public WritingChartType? ChartType;
    public string? TopicCategory;       // education / environment / technology / etc.
}

public class WritingModelAnswer
{
    public int Id;
    public int WritingTaskId;
    public int BandLevel;               // 7 / 8 / 9
    public string AnswerText;
    public string? AnnotationJson;      // подсветка lexical/grammatical strengths
}

public class WritingAttempt
{
    public int Id;
    public int WritingTaskId;
    public DateTime StartedAt;
    public DateTime? SubmittedAt;
    public int WordCount;
    public int DurationSeconds;
    public string UserText;
    public double? BandTaskAchievement;
    public double? BandCoherence;
    public double? BandLexical;
    public double? BandGrammar;
    public double? BandOverall;
    public string? FeedbackJson;
}
```

## Контент

**Объём:**
- 100 Task 1 Academic: 25 line graphs + 25 bar charts + 20 pie charts + 15 tables + 10 process diagrams + 5 maps
- 50 Task 1 General Training: формальные/полуформальные/неформальные письма (по 17 каждого)
- 150 Task 2: разбивка по типам — Opinion (40), Discussion (30), Problem-Solution (30), Advantages-Disadvantages (30), Two-Part Question (20)

**Model answers:**
- Для половины prompts (75 + 25 + 75 = 175 model answers) генерируем band 8 ответы
- Для 25% (87 prompts) дополнительно band 9 ответы
- Для 10% (35 prompts) — band 7 ответы для сравнения с user attempts

## Chart Generator (`tools/IeltsTask1ChartGen/`)

C# console + **ScottPlot 5.0**. На вход JSON-спека:
```json
{
  "chartType": "LineGraph",
  "title": "International tourism arrivals in five countries, 2010-2020",
  "xAxis": { "label": "Year", "values": [2010, 2012, ...] },
  "yAxis": { "label": "Million arrivals", "min": 0, "max": 80 },
  "series": [
    { "name": "France", "values": [76, 78, 82, ...] },
    { "name": "Spain", "values": [52, 55, 58, ...] }
  ]
}
```

На выход PNG 800×500 в `%AppData%\EnglishStudio\IeltsContent\Writing\Charts\{taskCode}.png`.

## Сервисы (`Modules.Ielts.Writing/`)

| Класс | Что |
|---|---|
| `WritingTaskService` | `GetTasksAsync(type, filter)`, `GetRandomTaskAsync(type)`, `StartAttemptAsync(taskId)`, `SaveDraftAsync(attemptId, text)`, `SubmitAttemptAsync(attemptId)` |
| `WritingSeedService` | Импорт `writing_prompts.json` + `writing_model_answers.json` |
| `IeltsWordCounter` | Подсчёт слов по официальным IELTS-правилам: hyphenated = 1, числа = 1 word, dates = 1 word, contractions = 1 |
| `WritingFeedbackService` | Оркестрирует `IIeltsEssayEvaluator` (через `Modules.Ai`), сохраняет результат в `WritingAttempt` |

## Prompt для Claude CLI (system prompt с кешем)

```
You are an IELTS examiner certified to band score Writing tasks.

[FULL IELTS WRITING RUBRIC band 0-9, 4 criteria]
[OFFICIAL BAND DESCRIPTORS, ~2500 tokens]

When evaluating an essay, respond ONLY with valid JSON matching this schema:
{
  "taskAchievement": 0.0-9.0,
  "coherenceCohesion": 0.0-9.0,
  "lexicalResource": 0.0-9.0,
  "grammaticalRangeAccuracy": 0.0-9.0,
  "overall": 0.0-9.0,
  "feedbackEn": "...",
  "feedbackRu": "...",
  "issues": [
    {"category": "grammar|lexical|coherence|task", "quote": "...", "explanationRu": "...", "suggestion": "..."}
  ]
}
```

User prompt: task type + prompt text + user essay.

## ViewModels / Views

| Файл | Назначение |
|---|---|
| `WritingHubViewModel/View` | Tabs: Task 1 Academic / Task 1 GT / Task 2 / История. Карточки задач с превью + последний band |
| `WritingTaskViewModel/View` | Left: prompt + chart image + таймер. Right: TextBox (большой), счётчик слов real-time (красный <min, зелёный ≥min). Кнопки: Submit / Save draft / Show model answer / Reset |
| `WritingResultViewModel/View` | Top: 4 круговые progress (TA/CC/LR/GRA) + overall band крупно. Middle: feedback RU + feedback EN раздельно. Bottom: список issues с подсветкой в user text + suggestion |
| `WritingHistoryViewModel/View` | Линейный график band-прогресса по неделям + список всех attempts |

## Acceptance M9
- 300 prompts (100+50+150) в seed
- 175 model answers band 8 (+ band 7/9 для части)
- Chart generator работает, рисует 6 типов графиков из JSON
- Word counter по официальным IELTS-правилам
- Claude CLI оценка возвращает все 4 band + overall + comments + issues
- Без CLI: hub и запись работают, скоринг показывает «Claude CLI не найден»
- История с графиком band-прогресса

**Объём:** ~25 .cs + 10 .xaml + ~2500 строк + seed (~5 МБ) + 100 PNG графиков (~30 МБ).

---

# M10 — Speaking

## Сущности (миграция `AddSpeakingSchema`)

```csharp
public enum SpeakingPartType { Part1, Part2, Part3 }

public class SpeakingQuestionBank
{
    public int Id;
    public SpeakingPartType Part;
    public string TopicCode;            // hometown, work, technology, ...
    public string? CueCardPrompt;       // только для Part2
    public string? CueCardSubpointsJson; // 4 bullet points для Part2
}

public class SpeakingQuestion
{
    public int Id;
    public int BankId;
    public string Text;
    public int OrderInBank;
    public int? FollowUpToQuestionId;   // для Part3 — связь с Part2 topic
}

public class SpeakingAttempt
{
    public int Id;
    public SpeakingPartType Part;
    public int? TopicBankId;
    public DateTime StartedAt;
    public DateTime? FinishedAt;
    public double? BandFluencyCoherence;
    public double? BandLexicalResource;
    public double? BandGrammar;
    public double? BandPronunciation;
    public double? BandOverall;
    public string? FeedbackJson;
    public ICollection<SpeakingResponse> Responses;
}

public class SpeakingResponse
{
    public int Id;
    public int SpeakingAttemptId;
    public int SpeakingQuestionId;
    public string AudioPath;
    public string? Transcript;
    public int DurationSeconds;
    public double? WpmRate;
    public double? PauseRatio;
    public int? FillerCount;
    public double? TypeTokenRatio;
}
```

## Контент

**Объём:**
- 40 Part 1 topics × 5 questions = 200 questions
- 50 Part 2 cue cards (describe person/place/event/object/experience)
- Для каждой Part 2 темы — 8 связных Part 3 follow-up questions = 400 q
- **Итого:** 200 + 50 + 400 = 650 вопросов

**Темы Part 1 (familiar):** hometown, work, study, family, friends, hobbies, music, films, books, sports, food, weather, clothes, shopping, transport, holidays, free time, weekends, technology, social media, internet, mobile phones, photography, art, animals, plants, environment, school memories, childhood, dreams, success, happiness, health, sleep, languages, travel, neighbours, gifts, traditions, festivals.

**Темы Part 2 (descriptive):** a person who influenced you, a place you visited, an important event in your life, an object you treasure, a skill you want to learn, a book that impacted you, a difficult decision, a time you helped someone, a memorable journey, a useful gadget, etc.

## Сервисы (`Modules.Ielts.Speaking/`)

| Класс | Что |
|---|---|
| `SpeakingTestService` | Управление 3-part flow: Part1 (random 5q from topic) → Part2 (1 min prep + 2 min response) → Part3 (4-5 follow-ups связные с Part2 topic) |
| `SpeakingSeedService` | Импорт `speaking_bank.json` |
| `ISpeechMetricsAnalyzer` + `SpeechMetricsAnalyzer` | WPM (words/minute), pause ratio (silence > 0.4s / total), filler count («um», «uh», «like», «you know»), TTR (unique words / total) |
| `IIeltsSpeakingEvaluator` (в `Modules.Ai`) | Принимает part type, questions, transcripts, metrics → 4 band + overall + comments |

## Обновление Whisper

| Аспект | Было | Станет |
|---|---|---|
| Модель | `ggml-base.en` (142 МБ) | `ggml-medium.en` (1.5 ГБ) — lazy download при первом Speaking |
| Word-level timestamps | нет | да — нужны для pause analysis |
| Совместимость с M6 | M6 single-word по-прежнему использует base.en | base.en остаётся как «fallback small» опция |

**Изменения в `WhisperTranscriber`:** добавить параметр `WhisperModelSize` (Base / Medium) + downloader для medium.en с прогрессом. Default для Speaking = Medium, для pronunciation training = Base.

## ViewModels / Views

| Файл | Назначение |
|---|---|
| `SpeakingHubViewModel/View` | Опции: Full mock (P1+P2+P3 по одному topic) / Только Part 1 / Только Part 2 / Только Part 3. История + средний band |
| `SpeakingPart1ViewModel/View` | 5 вопросов sequentially. На каждый: 30 сек подготовки (опц), запись (max 30 сек), auto-stop, transcript появляется ниже |
| `SpeakingPart2ViewModel/View` | Cue card visible. Timer: 1:00 prep (countdown, можно делать заметки) → автоматическое начало записи → 2:00 response → auto-stop |
| `SpeakingPart3ViewModel/View` | 4-5 follow-ups (60 сек на каждый), аналогично Part 1 но с депшерными вопросами |
| `SpeakingResultViewModel/View` | 4 круговые band + overall + metrics (WPM, pause%, fillers, TTR) + transcript per response + кнопка replay audio per response |

## Acceptance M10
- 650 questions в seed
- Full mock test Part1→2→3 проходится end-to-end
- Audio сохраняется per response, можно переслушать после
- Whisper medium.en транскрибирует passages
- Speech metrics считаются (WPM, pause, fillers, TTR)
- Claude CLI оценка возвращает 4 band + overall + comments
- Без CLI: запись + транскрипция + metrics работают; band от self-assessment

**Объём:** ~30 .cs + 12 .xaml + ~3000 строк + seed (~200 КБ).

---

# M8 — Listening

Самая инфраструктурно сложная секция: TTS + multi-voice concatenation + 30 MP3 файлов.

## Сущности
Используют `Ielts.Core`. Доп. поле `TestPart.AudioMediaAssetId` уже есть.

## Контент

**Объём:** 30 полных тестов × 4 секции = 120 секций (120 MP3, ~450 МБ total).

**Структура каждого теста (соответствует IELTS):**
- Section 1: социальная беседа (бронирование, регистрация), 2 голоса, 10 q (form completion + sentence completion)
- Section 2: монолог (экскурсия, презентация), 1 голос, 10 q (matching + map labeling + multiple choice)
- Section 3: академическая беседа 2-4 человека (студент-преподаватель), 10 q (multiple choice + matching)
- Section 4: академическая лекция, 1 голос, 10 q (sentence completion + note completion)

## TTS Pipeline (`Modules.Ielts.Listening/Tts/`)

| Файл | Что |
|---|---|
| `WindowsRtTtsService` | Использует `Windows.Media.SpeechSynthesis.SpeechSynthesizer` из WinRT (через `Microsoft.Windows.SDK.Contracts`). Получает text + voiceId → WAV stream |
| `ConversationSynthesizer` | На вход transcript с разметкой `[VOICE: sonia]Hi there.[/VOICE][VOICE: ryan]Hello![/VOICE]`. Парсит, синтезирует каждый фрагмент своим голосом, конкатенирует WAV через NAudio, конвертит в MP3 (NAudio LameMP3FileWriter) |
| `ListeningAudioBuilder` | Берёт structured transcript JSON → ConversationSynthesizer → MP3 файл в `%AppData%\EnglishStudio\IeltsContent\Listening\{testCode}\section{1-4}.mp3` |

**Голоса (закреплены):**
- Section 1: Sonia (UK F) + Ryan (UK M), реже Aria/Guy
- Section 2: один из 4 (rotation по тестам)
- Section 3: 2-4 голоса в комбинации
- Section 4: один академический голос (Ryan / Aria preferred)

## Генератор (`tools/IeltsListeningGen/`)

На каждый тест:
1. Главный агент генерирует через Claude CLI structured transcript JSON (4 sections, каждая со своими speakers/voices)
2. Validator pass проверяет: длина каждой секции 5-7 мин текста (~750-1000 слов), 10 вопросов с answer key, типы вопросов соответствуют сечениям
3. `ListeningAudioBuilder` синтезирует 4 MP3
4. Записывает test в `Seed/listening_tests.json` + аудио в `%AppData%\EnglishStudio\IeltsContent\Listening\`

**Время:** ~30 тестов × (~3 мин на генерацию текста + ~2 мин на синтез аудио) = ~2.5 часа.

## Сервисы (`Modules.Ielts.Listening/`)

| Класс | Что |
|---|---|
| `ListeningTestService` | `GetAvailableTestsAsync()`, `StartAttemptAsync(testId, trainingMode)`, плеер state |
| `IListeningAudioPlayer` | Wrapper над `IAudioPlayer` с track-events: section start/end, current position для UI overlay |
| `ListeningSeedService` | Импорт `listening_tests.json` |

## ViewModels / Views

| Файл | Назначение |
|---|---|
| `ListeningHubViewModel/View` | Список тестов, история |
| `ListeningTestViewModel/View` | Top: audio progress bar (visualization sections 1/2/3/4 как сегменты) + текущее время + индикатор «Section 2 of 4». Center: вопросы текущей секции (auto-scroll при переходе аудио). Special: за 20 сек до начала секции показываются вопросы для preview (стандартный IELTS-flow) |
| `ListeningResultView` | Аналогично Reading: raw X/40 + band + breakdown. Bonus: «Анализ» — переслушать любую секцию + увидеть transcript с подсветкой ответов |

## Map labeling реализация

Новый UserControl: фон-изображение (генерируем простую карту через ScottPlot или используем готовые SVG-шаблоны) + наложенные ToggleButton-маркеры с цифрами 1-7. Пользователь кликает на маркер → ComboBox выбирает букву A-J. Answer = mapping marker→letter.

## Acceptance M8
- 30 тестов в seed + 120 MP3 в `%AppData%`
- TTS-конкатенация многоголосых конверсаций работает
- Все типы вопросов включая map/form labeling
- Single-play по умолчанию (одно прослушивание = реальный IELTS)
- Training mode с replay + transcript-on-demand
- 10-минутный transfer time эмулируется

**Объём:** ~30 .cs + 15 .xaml + ~3000 строк + seed (~3 МБ) + 450 МБ MP3 в %AppData%.

---

# M11 — Full Mock Exam

## Сущности (миграция `AddMockExamSchema`)

```csharp
public class MockExamAttempt
{
    public int Id;
    public DateTime StartedAt;
    public DateTime? FinishedAt;
    public MockExamStage CurrentStage;
    public int? ListeningAttemptId;
    public int? ReadingAttemptId;
    public int? WritingAttemptId;
    public int? SpeakingAttemptId;
    public double? BandListening;
    public double? BandReading;
    public double? BandWriting;
    public double? BandSpeaking;
    public double? OverallBand;
}

public enum MockExamStage { NotStarted, Listening, Reading, Writing, Speaking, Finished, Abandoned }
```

## Flow

Real IELTS:
1. Listening 30 мин + 10 мин на перенос = 40 мин
2. Reading 60 мин
3. Writing 60 мин (Task1 + Task2)
4. (короткий перерыв)
5. Speaking 11-14 мин (в реальном экзамене может быть в другой день)

**Реализация:** sequential через `MockExamService.AdvanceToNextStageAsync()`. Между секциями — breaks 2-5 мин (visible countdown, можно skip).

## Overall Band — официальная формула IELTS

```csharp
public static double CalculateOverall(double l, double r, double w, double s)
{
    var avg = (l + r + w + s) / 4.0;
    // Official IELTS rounding: ends in .25 → round up to .5; ends in .75 → round up to next whole
    var truncated = Math.Floor(avg * 4) / 4;        // round to nearest .25
    var fraction = avg - truncated;
    if (fraction >= 0.125) truncated += 0.25;       // .125+ → up
    // Now snap to allowed values (whole or .5)
    var snapped = Math.Round(truncated * 2) / 2;
    return Math.Clamp(snapped, 0, 9);
}
```

(Точная формула проверится против официальных тестов и при необходимости откорректируется.)

## Acceptance M11
- Full exam flow: 4 секции end-to-end за ~2.5 часа
- Прерывание сохраняет abandoned attempt
- Resume не разрешён в exam-mode (как в реальном экзамене)
- Overall band по официальной формуле
- Финальный сертификат-стиль экран с 4 секциями + overall

**Объём:** ~10 .cs + 5 .xaml + ~800 строк.

---

# Стратегия генерации контента (детально)

## Принципы
1. **Главный агент (я), без параллельных подагентов** — каждый тест/prompt создаётся sequentially с полным контекстом
2. **3-stage pipeline на каждую единицу:** curated seed → generation with strict rubric → validator pass → accept/regen
3. **Ручной spot-check** первых 3-5 единиц каждой секции перед массовой генерацией
4. **Backup output** в `tools/IeltsXxxGen/output/` параллельно с записью в БД — на случай если БД нужно перестроить

## Workflow для каждой секции

| Шаг | Что |
|---|---|
| 1 | Создаю `tools/IeltsXxxGen/` проект |
| 2 | Embedded rubric document (полная официальная IELTS-rubric для типа задания) |
| 3 | Embedded topic seeds (curated, без пересечений) |
| 4 | Generator runs sequentially per topic |
| 5 | Для каждого результата — validator CLI-запрос |
| 6 | Сохранение в `output/{topicCode}.json` |
| 7 | После всей пачки — bulk import в `Seed/*.json` (embedded resource) или прямо в `%AppData%` (для Listening MP3) |
| 8 | SeedService импортирует в БД при первом запуске |

## Время генерации (оценка)

| Секция | Единиц | Время на единицу | Итого |
|---|---:|---|---|
| Reading | 30 тестов × 3 passages = 90 | ~4 мин (gen+validate+regen) | ~6 часов |
| Writing prompts | 300 | ~1 мин | ~5 часов |
| Writing model answers | 175 | ~2 мин | ~6 часов |
| Speaking bank | 650 q (батчами по 50) | ~2 мин на батч | ~30 минут |
| Listening | 30 тестов × 4 секции = 120 | ~5 мин (gen+TTS+validate) | ~10 часов |
| **Итого dev-time на контент** | | | **~27.5 часов** |

Разбивается на несколько сессий — не блокирующая работа.

---

# Setup checklist

✅ Готово:
- Claude CLI 2.1.148 в PATH
- 4 neural TTS voices: Sonia, Ryan, Aria, Guy
- Whisper.net + NAudio infrastructure (M6)
- DI + EF Core + миграции

⏳ Установится по ходу M7.0+:
- 6 новых classlib projects
- ScottPlot 5 NuGet (`tools/IeltsTask1ChartGen/`)
- `Microsoft.Windows.SDK.Contracts` NuGet (WinRT projections для TTS)
- `Whisper.net.AllRuntimes` NuGet (если нужны runtime для medium.en)

---

# Риски и митигации

| Риск | Митигация |
|---|---|
| Claude CLI rate limit (Max subscription) | Sequential генерация, no parallelism. Если упирается — добавить delay между запросами |
| Качество AI-сгенерированного контента | Validator pass + ручной spot-check first 3-5 + iteratively refine prompt |
| TTS-конкатенация даёт unnatural pauses | Использовать SSML с `<break time="500ms"/>` тегами между репликами |
| Whisper medium.en слишком тяжёл для CPU-only | Опция Base в Settings + предупреждение «Medium даёт лучшее качество но медленнее» |
| Overall band формула отличается от официальной | Проверить против реальных IELTS official scoring guides на 5-10 эталонных кейсах |
| Map labeling рендеринг сложен | Использовать template-based maps (5-6 готовых SVG schemes) вместо динамической генерации |
| EF Core миграции конфликтуют | Каждый module добавляет свою миграцию через `services.AddDbContext` — все таблицы в общей dictionary.db, но через separate `IModelCustomizer` |

---

# Итоговая сводка

| Milestone | Файлов | Строк | Контент | Время разработки |
|---|---:|---:|---|---:|
| M7.0 | ~25 .cs + 4 .xaml | ~1800 | — | 3 дня |
| M7 Reading | ~30 .cs + 15 .xaml | ~3000 | 30 тестов (~6 ч генерации) | 5 дней + 6 ч контента |
| M9 Writing | ~25 .cs + 10 .xaml | ~2500 | 300 prompts + 175 models (~11 ч генерации) | 5 дней + 11 ч контента |
| M10 Speaking | ~30 .cs + 12 .xaml | ~3000 | 650 q (~30 мин генерации) | 6 дней + 0.5 ч контента |
| M8 Listening | ~30 .cs + 15 .xaml | ~3000 | 30 тестов + 120 MP3 (~10 ч генерации) | 6 дней + 10 ч контента |
| M11 Mock | ~10 .cs + 5 .xaml | ~800 | — | 2 дня |
| **Итого** | **~150 файлов** | **~14 100 строк** | **~28 ч генерации контента** | **~27 дней разработки** |
