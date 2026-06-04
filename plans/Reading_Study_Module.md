# Модуль «Чтение» (область Учёба) — план реализации

**Создано:** 2026-06-03.
**Область:** Учёба (`AppArea.Study`). **ВАЖНО:** это НЕ IELTS Reading (`Modules.Ielts.Reading`, экзаменационная карусель) — отдельный самостоятельный модуль `EnglishStudio.Modules.Reading` с UI-меткой **«Чтение»**.

## Видение

Окно, где пользователь читает английский текст **вслух в микрофон**: прочитанная часть тускнеет в реальном времени, считается скорость (WPM), любое выделенное слово/фразу можно перевести во всплывающей карточке. Незнакомые слова, которых нет в словаре, **подкачиваются через Claude CLI и сразу сохраняются в постоянный словарь** со всеми полями — поэтому чтение становится **движком роста словаря**: читаешь → пополняется словарь → слова попадают в SRS-тренажёр. Петля обучения замыкается.

## Зафиксированные решения (с пользователем, 2026-06-03)

1. **Live-трекинг чтения = гибрид Vosk + Whisper.**
   - **Vosk** (потоковый offline-ASR, NuGet `Vosk` + модель `vosk-model-small-en-us` ~40–50 МБ) ведёт курсор и тускление в реальном времени с низкой задержкой.
   - **Whisper** (уже в проекте: `IWhisperTranscriber.TranscribeWithTimestampsAsync`, Medium, word-level timestamps) — пост-анализ полного WAV: точные тайминги, пропуски, оценка произношения (reuse `PronunciationAssessor`).
   - Это **forced alignment по известному эталону**, не открытое распознавание: каждые ~1.5 с распознанные слова нечётко матчатся впереди курсора в эталоне → курсор двигается. Ошибки ASR терпимы.

2. **Перевод по выделению = словарь + Claude CLI fallback с авто-обогащением.**
   - Сначала локальный `DictionaryDbContext` (Words → Senses → Translations).
   - Если слова нет — Claude CLI (`Modules.Ai`) генерит полную словарную карточку → запись персистится в словарь (новый `WordSource.Ai` + флаг «не проверено»). Второй раз — мгновенно из БД.

## Переиспользуемое (что уже есть в репозитории)

| Компонент | Файл | Для чего |
|---|---|---|
| Whisper + word timestamps | `App/Audio/IWhisperTranscriber.cs` (`TranscribeWithTimestampsAsync`) | пост-анализ, фонемы |
| Запись микрофона | `App/Audio/NAudioRecorder.cs` (16 kHz mono PCM) | нужно расширить: отдавать live-поток PCM из `OnDataAvailable`, не только файл на стопе |
| Оценка произношения | `App/Audio/PronunciationAssessor.cs` (Levenshtein по словам) | accuracy, пропуски |
| Словарь | `Modules.Dictionary/Data/DictionaryDbContext.cs` (Words/Senses/Translations/PhrasalVerbs/Collocations/WordForms) | перевод по выделению |
| FrequencyRank | `Word.FrequencyRank` | авто-оценка CEFR текста + словарный охват |
| SRS | `Modules.Dictionary/Srs/ISrsService.cs` (`AddWord`) | «📚 в изучение» из окна чтения, pre-teach |
| Claude CLI | `Modules.Ai` (subprocess, Max sub) | обогащение словаря, вопросы на понимание |
| Регистрация модуля | `App.xaml.cs/RegisterModuleDescriptors` + `IModuleDescriptor` (поле `Area`) | sidebar |

## Скелет модуля

```
src/EnglishStudio.Modules.Reading/            (classlib net10.0)
├── EnglishStudio.Modules.Reading.csproj       → ref: Modules.Dictionary (+ позже Modules.Ai)
├── Entities/   ReadingText, ReadingSession, ReadingWordStat, TextBookmark, TextNote
├── Data/       ReadingDbContext (reading.db в %AppData%/EnglishStudio/), ReadingPaths, Migrations/
├── Services/
│   ├── ITextLibraryService / TextLibraryService      — CRUD текстов, импорт, оценка CEFR, словарный охват
│   ├── ITextLookupService  / TextLookupService       — выделение → перевод (DB → Claude → персист)
│   ├── IReadAlongTracker   / VoskReadAlongTracker     — live: PCM-поток → курсор + WPM (движок за интерфейсом)
│   ├── IReadingAnalysisService / WhisperReadingAnalysis — пост-анализ WAV (Whisper) + accuracy/произношение
│   └── IReadingSessionService / ReadingSessionService — сохранение/история сессий, агрегаты для графиков
├── ReadingTokenizer.cs                         — текст → токены (слово/пунктуация/пробел) + char-offsets, предложения
└── ReadingServiceCollectionExtensions.cs       — AddReadingModule()
```

UI (в `App`, как у прочих модулей):
```
App/Views/Reading/        ReadingModuleView (роутер) · ReadingLibraryView (хаб) · ReadingSessionView (окно чтения) · ReadingSummaryView
App/ViewModels/Reading/   ReadingHubViewModel · ReadingLibraryViewModel · ReadingSessionViewModel · ReadingSummaryViewModel · TranslationPopupViewModel · TokenViewModel
```
Регистрация: `services.AddReadingModule();` + `IModuleDescriptor` (code `reading-study`, nameRu «Чтение», icon 📕, order 40, **area `AppArea.Study`**).

---

## Фазы

> **СТАТУС (2026-06-03):** Фазы 0–4 и блоки **F1–F7** ВЫПОЛНЕНЫ и интегрированы (live-чтение, анализ, pre-teach→SRS, вопросы Claude, пофонемная подсветка, shadowing/TTS, заметки/закладки, графики, graded-readers; голосовой CP3 отложен). Детальные планы Фаз 3–4 / F1+F2 / F3+F4 — выполнены и удалены; `Reading_F5-F6-F7_Parallel.md` — выполнен.
>
> **Доп-блок F8 (книги/пагинация):** добавлено предупреждение при добавлении текста > 20 000 слов (рендер не виртуализирован → большие тексты вешают окно). Полное решение — постраничный рендер — расписано в [`Reading_F8_Books_Parallel.md`](Reading_F8_Books_Parallel.md).

| Фаза | Содержание | Результат |
|---|---|---|
| **0** | Shell-restructure (переключатель областей + Статистика вниз) | ✅ СДЕЛАНО 2026-06-03 |
| **1** | Скелет модуля + библиотека текстов + добавление своих текстов | ✅ СДЕЛАНО |
| **2** | Окно чтения: рендер + выделение → перевод (Claude+обогащение) | ✅ СДЕЛАНО |
| **3** | Vosk live-трекинг: тускление + WPM | ✅ СДЕЛАНО (live-smoke CP3 отложен) |
| **4** | Whisper пост-анализ + произношение + история/сводка | ✅ СДЕЛАНО |
| **F1+F2** | Pre-teach→SRS + вопросы на понимание (Claude) | → `Reading_F1-F2_Parallel.md` |
| **F3–F7** | Остальные будущие блоки (см. ниже) | по мере желания |

---

## Фаза 1 — скелет + библиотека текстов + свои тексты

### Сущности (миграция `ReadingInitial`)

```csharp
class ReadingText {
    int Id;
    string Title;
    string BodyText;                 // сырой текст (токенизация — в памяти при открытии)
    ReadingSource Source;            // User / Imported / Builtin
    int WordCount;
    CefrLevel EstimatedCefr;         // оценка по FrequencyRank словаря (см. ниже)
    string? Tags;                    // CSV, опц.
    DateTime CreatedAt; DateTime? LastOpenedAt;
}
class ReadingSession {              // заполняется с Фазы 3
    int Id; int ReadingTextId;
    DateTime StartedAt; int DurationSec;
    int WordsRead; double Wpm; double AccuracyPct;
    string? AudioPath; bool Completed;
}
class ReadingWordStat {            // пер-словная статистика, с Фазы 4
    int Id; int ReadingSessionId;
    int TokenIndex; bool Skipped; bool Mispronounced; double? Score;
}
enum ReadingSource { User, Imported, Builtin }
```
`ReadingDbContext` — отдельный файл `reading.db` (как у Ielts.Core свой контекст). Перевод тянется из `DictionaryDbContext` через `ITextLookupService` (ссылка на `Modules.Dictionary`).

### `ITextLibraryService`
- `Task<IReadOnlyList<ReadingTextListItem>> ListAsync()` — карточки (заголовок, кол-во слов, CEFR, дата, последний WPM).
- `Task<int> AddAsync(string title, string body, ReadingSource source)` — нормализует переводы строк, считает `WordCount`, оценивает `EstimatedCefr`, сохраняет.
- `Task<ReadingText> GetAsync(int id)`; `Task DeleteAsync(int id)`; `Task RenameAsync(...)`.
- **Оценка CEFR** (`EstimateCefrAsync`): токенизировать → для каждой леммы взять `Word.FrequencyRank`/`CefrLevel` из словаря → распределение → медиана/перцентиль → CEFR. Дешёвая эвристика, пересчитывается при добавлении.

### `ReadingTokenizer`
- `IReadOnlyList<TextToken> Tokenize(string body)` — `TextToken{ string Text; TokenKind Kind(Word/Punct/Space/Break); int StartOffset; int Length; int? WordIndex; }`.
- Разбивка на предложения/абзацы (по `\n\n` и пунктуации) — для рендера и будущих вопросов.
- Используется и окном чтения (рендер по словам), и оценкой CEFR/охвата.

### UI Фазы 1
- `ReadingLibraryView`: грид карточек текстов (`Card`-стиль), кнопка **«➕ Добавить текст»**, по карточке — «Читать» / «☰ переименовать/удалить».
- Диалог добавления (на базе `ChromedWindow`, как `ConfirmWindow`): поле Title + большой `FlatTextBox` (multiline, вставка из буфера) + «Импорт .txt» (`Microsoft.Win32.OpenFileDialog`). Кнопка Save → `AddAsync`.
- `ReadingModuleView` — роутер (Library ↔ Session ↔ Summary), как `ReadingModuleView`/`MockModuleView` в IELTS.
- Регистрация модуля в `App.xaml.cs` (area Study). Применить миграцию при старте (как `dictionary.db`).

**Критерий готовности Ф1:** добавил текст (вставкой или .txt) → он в библиотеке с оценкой CEFR и кол-вом слов → открывается (пока статичный рендер).

---

## Фаза 2 — окно чтения: рендер + перевод по выделению

### Рендер текста
- `ReadingSessionView`: read-only `RichTextBox` с `FlowDocument`, **каждое слово — отдельный `Run`** (через `ReadingTokenizer`), чтобы (а) гасить `Opacity`/`Foreground` пословно в Ф3 и (б) иметь нативное выделение мышью.
- Пунктуация/пробелы — обычные Run без word-index. Абзацы — `Paragraph`.
- Крупный шрифт, комфортная ширина строки, тёмная тема проекта.

### Перевод по выделению (`ITextLookupService`)
```
TextLookupResult Lookup(selectedText):
  norm = normalize(lower, strip punct)
  1) одно слово → лемматизация (как есть → WordForms.Form → простой стемминг)
       → DictionaryDbContext: Words by Headword/Lemma → первый Sense → Translations + DefinitionRu
  2) фраза → Collocations.LinkedText / PhrasalVerbs.Headword (exact) → TranslationRu
  3) нет в БД → флаг NeedsAiFetch=true (Ф2 fallback ниже)
```
- На `RichTextBox.SelectionChanged` (debounce ~250 мс) → `Lookup` → `Popup`-карточка у курсора: слово, POS, IPA, RU-переводы, 🔊 (reuse audio), кнопки **«📚 в изучение»** (`ISrsService.AddWord`) и (для AI-слов) «🤖 уточнить».

### Claude CLI fallback + авто-обогащение
- Нет в БД → Popup показывает «⏳ ищу перевод…» → асинхронно `IDictionaryEnrichmentService.FetchAndPersistAsync(lemma, contextSentence)`:
  - Claude CLI (через `Modules.Ai`) с structured-prompt: вернуть JSON `{ isRealWord, headword, lemma, pos, ipaUk, ipaUs, cefr, definitionEn, definitionRu, translationsRu[], examples[{en,ru}] }`.
  - `isRealWord=false` (имя собственное/опечатка/не-слово) → не персистим, Popup «нет перевода».
  - Иначе: map POS через существующий `PartOfSpeechSeedMap`; INSERT `Word`(Source=`WordSource.Ai`, флаг `IsAiGenerated`/needs-review) + `Sense` + `Translations` + `Examples`. Уникальность `(Headword, PartOfSpeechId)` уже в схеме — дублей не будет.
  - Popup заполняется. Слово теперь в «Словаре» (с бейджем 🤖) и доступно в SRS.
- **Изменения в `Modules.Dictionary`:** `WordSource.Ai` (=13) + `Word.IsAiGenerated bool` (миграция `AddAiWordProvenance`); бейдж 🤖 + фильтр-источник «AI» в `DictionaryView`.
- **Оффлайн/без CLI:** graceful — показать что есть в БД либо «перевод недоступен оффлайн». Без падений.
- **Контекст-чувствительность (опц.):** передавать предложение, в котором выделено слово, чтобы Claude выбрал нужное значение полисемичного слова.

**Критерий готовности Ф2:** выделяю знакомое слово → мгновенный перевод; выделяю незнакомое → спиннер → перевод появляется и слово навсегда в словаре; «в изучение» добавляет в SRS.

---

## Фаза 3 — Vosk live-трекинг: тускление + WPM

### Расширение записи
- `NAudioRecorder`: добавить событие/`IObservable`/callback с live-буферами PCM из `OnDataAvailable` (16 kHz mono) — параллельно записи в WAV (WAV нужен Ф4). Не ломать существующий контракт M6 (`StartRecording`/`StopRecording`/путь).
- Альтернатива, чтобы не трогать общий рекордер: отдельный `IPcmStreamSource` для модуля чтения. Решить при реализации; предпочтительно расширить существующий аккуратным событием `DataAvailable`.

### `IReadAlongTracker` (реализация `VoskReadAlongTracker`)
- Вход: эталонные токены (слова с индексами) + поток PCM. Выход: события `CursorAdvanced(int wordIndex)`, `WpmUpdated(double)`, `Finished`.
- Vosk: `Model` (lazy-download ~50 МБ с `IProgress`, кэш в `%AppData%/EnglishStudio/Models/vosk-en/`), `VoskRecognizer` с `SetWords(true)` → partial/final гипотезы со словами и таймингами.
- **Алгоритм курсора:** держим указатель `cursor`. На каждую гипотезу берём её слова, нормализуем, ищем нечётко в окне `[cursor .. cursor+N]` эталона (допускаем пропуски/повторы/мисрекогн). Самый дальний уверенно совпавший эталонный индекс → новый `cursor` (только вперёд, без откатов на дрожании).
- **WPM:** `cursor`-слова / прошедшие минуты; сглаживание (скользящее окно). Финальный + средний.

### UI Ф3
- В `ReadingSessionView` шапка: таймер, слов прочитано / всего, текущий WPM, прогресс-бар (cursor/total).
- Кнопки **▶ Начать чтение** (mic+таймер+tracker) / **⏹ Стоп**.
- На `CursorAdvanced` → пословно гасим `Run.Opacity` (≈0.35) до курсора через `Dispatcher`. Плавно (можно `DoubleAnimation`).
- На стопе/конце текста → создать `ReadingSession`, перейти в `ReadingSummaryView` (Ф4 дополнит).

**Критерий готовности Ф3:** читаю вслух — прочитанные слова тускнеют почти без задержки, WPM живой; на стопе сохраняется сессия с WPM/временем.

---

## Фаза 4 — Whisper пост-анализ + произношение + сводка/история

- На стопе полный WAV → `IReadingAnalysisService.AnalyzeAsync(wav, тokens)`:
  - `IWhisperTranscriber.TranscribeWithTimestampsAsync` (Medium) → точные per-word тайминги.
  - Выравнивание распознанного на эталон (Levenshtein/`PronunciationAssessor`) → пер-словные `ReadingWordStat`: пропущено / произнесено / score.
  - Accuracy% = совпавшие/всего; список «трудных» слов.
- `ReadingSummaryView`: WPM (этой сессии vs среднее), время, accuracy, подсветка пропусков/ошибок прямо в тексте (цветом), топ трудных слов → кнопка «добавить в SRS».
- История сессий по тексту (список + мини-тренд WPM).

---

## Будущие блоки (F+)

Каждый — самостоятельная под-фаза, можно брать в любом порядке.

### F1. Pre-teach незнакомых слов перед чтением → SRS
- Перед открытием: токенизировать → найти слова **вне словаря** или с низким знанием (нет в `UserWordProgress` / высокий `FrequencyRank` / отсутствуют переводы).
- Экран «Перед чтением»: список таких слов (батч-обогащение через Claude как в Ф2) с переводами; кнопка **«Изучить все»** → `ISrsService.AddWord` пачкой. Опц. мини-квиз перед стартом.
- Reuse: `ITextLookupService`/enrichment + `ISrsService`. Цель — снизить трение и поднять понимание до чтения.

### F2. Авто-вопросы на понимание (Claude CLI — уже есть)
- После чтения: Claude по тексту генерит 3–6 вопросов (MCQ + open) → экран ответов → Claude оценивает open-ответы (как в Writing/Speaking).
- Сущности: `ComprehensionQuestion`/`ComprehensionAttempt` в `ReadingDbContext`. Кэшировать сгенерированные вопросы на `ReadingTextId` (генерить один раз).
- Reuse: `Modules.Ai` subprocess + паттерн структурированной оценки из Writing.

### F3. Пофонемная подсветка ошибок произношения (Whisper + assessor)
- Поверх Ф4: для «трудных» слов перевести эталон и распознанное в фонемы через **CMUdict** (англ. слово → ARPAbet; embedded-ресурс ~3–4 МБ) и сравнить по фонемам (выравнивание).
- UI: проблемные звуки подсвечиваются в слове; подсказка «звук /θ/ → произнёс /s/». Опора на `PronunciationAssessor` (расширить до фонемного уровня).
- Риск: точность распознавания фонем у Whisper ограничена; начинать с пословного скоринга, фонемы — поверх.

### F4. Shadowing (TTS читает → ты повторяешь)
- Кнопка «Прослушать» на предложении/абзаце: TTS озвучивает эталон, затем запись повтора → сравнение (reuse Ф4 анализ).
- TTS: `Windows.Media.SpeechSynthesis` (как в M8 Listening — голоса Sonia/Ryan/Aria/Guy) или System.Speech. Режим «фраза → пауза → повтор → оценка».
- Сущность `ShadowingAttempt` (опц.). Темп TTS регулируется (0.75×–1×).

### F5. Заметки и закладки
- `TextBookmark{ ReadingTextId, TokenIndex, CreatedAt }` — «продолжить с места».
- `TextNote{ ReadingTextId, StartOffset, Length, NoteText, Color }` — выделил → «добавить заметку», маркер в тексте, панель заметок сбоку.
- UI: подсветка диапазонов в `FlowDocument`; список заметок/закладок текста.

### F6. Графики скорости и словарного охвата по времени
- Источник: агрегаты `ReadingSession` (WPM, accuracy, дата) + словарный охват (доля слов текста, уже известных по `UserWordProgress`).
- Можно вынести плитки в **общую «Статистику»** (она теперь сквозная) или вкладка «Прогресс чтения» в библиотеке: WPM-тренд, минут чтения/слов прочитано в неделю, рост покрытия словаря.
- Графики: лёгкий self-drawn (как `StatsView`) или OxyPlot/LiveCharts (решить при реализации).

### F7. Встроенная библиотека graded-readers
- Seed-тексты по уровням (A1–C1) как embedded-ресурс (`Seed/graded_readers.json`, Source=`Builtin`), сидинг при первом запуске (идемпотентно, как Oxford 5000).
- Источники: public-domain/CC адаптированные тексты; фильтровать по `EstimatedCefr`.
- UI: вкладка «Библиотека» (встроенные, фильтр по CEFR/теме) рядом с «Мои тексты».

---

## Открытые вопросы / риски
- **Vosk live из NAudio:** убедиться, что формат буферов (16 kHz mono PCM 16-bit) совпадает с ожиданиями Vosk; ресемплинг не нужен. Гонки между записью в WAV и стримом — через копию буфера.
- **Latency Claude CLI** на выделении: всегда показывать кэш мгновенно, AI — асинхронно; не блокировать UI.
- **Качество AI-словаря:** провенанс `IsAiGenerated` + бейдж, чтобы потом вычитывать; guard `isRealWord`.
- **Размер моделей:** Vosk small (~50 МБ) + Whisper medium (~1.5 ГБ, уже опционально для Speaking) — качать lazy с прогрессом, переиспользовать общий кэш `%AppData%/EnglishStudio/Models/`.
- **`ReadingDbContext` vs общий:** держим отдельный `reading.db`; перевод/SRS — кросс-контекстно через сервисы `Modules.Dictionary`.
