# EnglishStudio — ROADMAP

WPF .NET 10 приложение для изучения английского языка (с заделом под другие языки).
Модульная архитектура: блок «Английский словарь» — первый из планируемых.

**Создано:** 2026-05-22
**Статус (на 2026-06-03):** **ВСЕ milestone'ы M0–M11 закрыты — приложение функционально полное.**
8 рабочих модулей в sidebar (Словарь / Тренажёр / Статистика / Reading / Listening / Writing /
Speaking / Полный экзамен), ни одной заглушки. Все 4 IELTS-секции + полный mock-экзамен (overall band
по официальной формуле, все 4 секции из одной Cambridge-книги) работают end-to-end. Остались только
полировка и инфра (тесты/installer/логирование/светлая тема) — см. раздел «Что предстоит».
Детальный план: [IELTS_PLAN.md](IELTS_PLAN.md).

**Видение проекта:** не просто словарь, а тренажёр подготовки к IELTS на максимальный балл.
Будущие модули: чтение, аудирование, письмо, говорение, полный экзаменационный flow.

---

## ✅ Сделано

### M0 — Каркас проекта

| Компонент | Подробности |
|---|---|
| Solution | `EnglishStudio.slnx` (новый .NET 10 формат) в `C:\Users\tvore\source\repos\EnglishStudio` |
| Pin SDK | `global.json` → .NET 10.0.102 (rollForward latestFeature) |
| Проекты | `src/EnglishStudio.App` (WPF net10.0-windows) + `src/EnglishStudio.Modules.Dictionary` (classlib net10.0) |
| Инфраструктура | Microsoft.Extensions.Hosting + DI + CommunityToolkit.Mvvm 8.4.2 + Polly |
| ORM | EF Core 10.0.8 + SQLite, БД в `%AppData%\EnglishStudio\dictionary.db` |
| Сущности (12) | Word, PartOfSpeech, Sense, Translation, Example, WordForm, Category + WordCategory (M:N), Tag + WordTag (M:N), MediaAsset, UserWordProgress |
| Миграции | Initial с индексами на Headword/Lemma/FrequencyRank, FK, unique (Headword,POS) |
| Smoke-тест | Приложение стартует, БД создаётся при первом запуске |

### M1 step 1 — Расширение модели CEFR

- Enum `CefrLevel` (Unknown / A1 / A2 / B1 / B2 / C1 / C2)
- Enum `WordSource` (Unknown / Seed / Api / User) — провенанс слова
- Поля `Word.CefrLevel` + `Word.Source` + индексы
- Миграция Initial пересоздана с чистого листа

### M1 step 2 — Seed-данные Oxford 5000

- Источник: [winterdl/oxford-5000-vocabulary-audio-definition](https://github.com/winterdl/oxford-5000-vocabulary-audio-definition)
- 5943 записи: headword + POS + CEFR + IPA UK/US + definitionEn + exampleEn + audio file refs
- Конвертер `tools/convert-oxford-seed.ps1` → наша схема
- Embedded resource `Seed/oxford_5000.json` (1.8 МБ) внутри Dictionary.dll
- `SeedManifest.OpenOxford5000()` для доступа

### M1 step 3 — SeedService

- DTO `OxfordSeedDocument` под System.Text.Json
- `PartOfSpeechSeedMap` — мэппинг 18 POS-кодов на EN+RU имена
- Группировка по (headword, pos), батч-вставки 500, отключение AutoDetectChanges
- Идемпотентность: пропуск если `Words.Any()`
- DI регистрация `AddDictionaryModule()` + автовызов `SeedIfEmptyAsync` после `Migrate`
- **Результат:** 5942 Words / 5943 Senses / 5902 Examples / 18 PartsOfSpeech за ~4 сек, БД 2 МБ

### M1 step 4 — UI словаря

| Файл | Назначение |
|---|---|
| `Views/Dictionary/DictionaryView.xaml` | Двухпанельная вёрстка: поиск + CEFR-чипы + POS-комбобокс / список / карточка |
| `ViewModels/DictionaryViewModel.cs` | Debounce-поиск (250 мс), фильтры, IServiceScopeFactory для async-запросов, пагинация по 200 |
| `ViewModels/WordListItem.cs`, `WordDetailViewModel.cs`, `CefrFilterItem.cs`, `PosFilterItem.cs` | Read-models |
| `Converters/CefrToBrushConverter.cs` | CEFR → цветная пилюля |
| `Converters/CefrToStringConverter.cs` | CEFR → "A1"/"—" |
| `Converters/NullToVisibilityConverter.cs` | Empty-state видимость |
| `MainWindowViewModel.cs` | Shell ViewModel, держит DictionaryViewModel |

**Функционал:** префиксный поиск по Headword+Lemma, multi-select фильтр по CEFR, фильтр по части речи, виртуализированный список, карточка деталей с IPA UK/US, английским определением, примерами.

### M1 step 5 — Стилизация (порт MapDownloader2 + ThemeManager)

```
src/EnglishStudio.App/
├── Themes/
│   ├── Palettes/
│   │   ├── DarkBlue.xaml      # ~50 brushes из MapDownloader2
│   │   └── Light.xaml         # stub под будущее
│   └── Controls.xaml          # стили на {DynamicResource}
└── Theming/
    ├── AppTheme.cs
    ├── IThemeManager.cs
    └── ThemeManager.cs        # Apply() подменяет MergedDictionaries[0]
```

**Палитра DarkBlue:** база `#073a5a` + радиальный градиент `#3fbfec→#053049`, шапка `#212121→#525252`, CEFR-пиллы (зелёный A1/A2 → синий B1/B2 → фиолетовый C1/C2).

**Стили (портированы + новые):**
- ChromeButton, ChromeCloseButton, FlatButton, AccentButton, IconButton
- ChipToggleButton (CEFR-фильтры)
- FlatTextBox, FlatComboBox + Item, FlatCheckBox, FlatRadioButton
- FlatListBox + FlatListBoxItem (с виртуализацией)
- Card / CardCompact (Border-обёртки)
- FlatProgressBar, ShimmerLink
- ScrollBar + ScrollThumb (тонкий, тёмный)
- FlatTabControl + FlatTabItem
- Typography: H1/H2/H3/BodyText/MutedText/Caption/MonoText

**MainWindow:** AllowsTransparency, WindowChrome с CornerRadius=10, фоновые слои (radial+brushed-metal+vignette), кастомная шапка с Min/Max/Close.

### M2 — Аудио произношений UK/US

**Источник:** [winterdl/oxford-5000-vocabulary-audio-definition](https://github.com/winterdl/oxford-5000-vocabulary-audio-definition) — MP3 упакованы в split-ZIP архивы по 4 части (z01/z02/z03/zip), ~95 МБ на акцент.

**Стратегия:** lazy bulk-download при первом клике 🔊 UK или 🔊 US:
1. Скачиваем 4 split-части `{uk|us}_audio_split_24m.{z01,z02,z03,zip}` в `_dl/`.
2. Распаковываем через DotNetZip (стандартный System.IO.Compression не поддерживает split-ZIP).
3. Извлекаем все MP3 flat в `%AppData%\EnglishStudio\Media\Audio\{uk|us}\`.
4. Удаляем `_dl/`, ставим маркер `.unpacked` для идемпотентности.

После одной загрузки на акцент — все 5942 слова играются мгновенно из локального кэша.

| Файл | Назначение |
|---|---|
| `Entities/Word.cs` + миграция `AddAudioPaths` | Новые поля `AudioUkPath`, `AudioUsPath` (имена MP3 из seed). |
| `Seed/SeedService.cs` | + `BackfillAudioPathsAsync()` — заполняет пути для уже-засиденной БД (5942 слов). |
| `Audio/AudioVariant.cs`, `IAudioCacheService.cs`, `AudioCacheService.cs` | Сервис кэша: lock per variant, split-ZIP extract, IProgress&lt;string&gt; статус. |
| `App/Audio/IAudioPlayer.cs` + `NAudioPlayer.cs` | NAudio 2.2.1 wrapper (WaveOutEvent + AudioFileReader). |
| `DictionaryServiceCollectionExtensions.cs` | + AddHttpClient (5 мин timeout, Polly retry) + AddSingleton&lt;IAudioCacheService&gt;. |
| `ViewModels/DictionaryViewModel.cs` | `PlayUk/PlayUs` команды, `IsAudioBusy`, `AudioStatus`, IProgress. |
| `WordDetailViewModel` | + `HasAudioUk`, `HasAudioUs` для CanExecute. |
| `Views/Dictionary/DictionaryView.xaml` | Кнопки 🔊 UK / 🔊 US в третьей колонке IPA-таблицы + статус-строка под ней. |
| `Themes/Controls.xaml` | + стиль `AudioButton` (овальная пилюля). |

**Зависимости:** NAudio 2.2.1, DotNetZip 1.16.0 (Polly уже было).

### M1 step 6 — Русские переводы

5 волн через параллельные подагенты (всего 46 агентов), все 100% match без несоответствий:

| CEFR | Слов | Агентов | Translation rows |
|---|---:|---:|---:|
| A1 | 1076 | 8 | 2034 |
| A2 | 990 | 8 | 2261 |
| B1 | 902 | 7 | 2306 |
| B2 | 1571 | 12 | 3950 |
| C1 | 1404 | 11 | 3919 |
| **Итого** | **5943** | **46** | **14 466** |

**Инфраструктура** (`tools/Translate/`):
- `generate-batches.ps1` — нарезка seed.json по CEFR на батчи N×135
- `Importer/Program.cs` — C# тулза: читает output/CEFR/*.json → INSERT Translation rows + UPDATE Sense.DefinitionRu

**UI:** русские переводы как chip-пилюли в детальной карточке, ниже — русское определение, под ним курсивом — английское определение.

### Шаг 1 — Расширение лексики (AWL + AVL)

Под IELTS-цель нужны академические списки помимо Oxford 5000.

**Инфраструктура (миграция `AddPhrasalVerbsAndPolymorphicSenseExample`):**
- `WordSource` расширен значениями `Awl=10`, `Avl=11`, `Phave=12`.
- `PhrasalVerb` entity: `BaseWordId → Word` (nullable), `Particle`, `Headword` denorm («give up»), своя коллекция `Senses`/`Examples`.
- `Sense.WordId` и `Example.WordId` теперь nullable + добавлены `PhrasalVerbId` (XOR: либо Word, либо PhrasalVerb владеет sense).
- `PartOfSpeech` "phrasal_verb" добавлен в seed.

**Источники:**
- AWL Coxhead 2000 (570 семей × 10 sublists) — [lpmi-13/machine_readable_wordlists/Academic/AWL/AWL.json](https://github.com/lpmi-13/machine_readable_wordlists), CC0
- AVL Gardner & Davies 2014 — тот же репо (AVL.json), CC0. JSON содержит полные ~20k COCA-lemmas; в коде отфильтровано `rank ≤ 3000` под классический AVL top.

**Результат сидинга (через `SeedAwlIfEmptyAsync` / `SeedAvlIfEmptyAsync`):**

| Метрика | AWL | AVL |
|---|---:|---:|
| Existing words tagged | 968 | 2 936 |
| New stub words created (Source=Awl/Avl) | 28 | 176 |
| Tag rows | 1 992 | 5 832 |
| Время | 269 мс | 500 мс |

Теги: `awl`, `awl-sublist-1..10`, `avl`, `avl-band-1..6` (bucket = 5 AVL-bands ≈ 500 ranks).

**Дедуп vs Oxford 5000:** для каждой леммы AWL/AVL ищем существующий `Word.Headword`/`Word.Lemma` (case-insensitive); если найден — добавляем теги, не создаём дубль. Если нет — создаём stub `Word` с `Source=Awl/Avl` без `Senses` (Definition+Translations будут заполнены позже).

**Tools:**
- `tools/AvlCleanup/` — одноразовая C# утилита удаления AVL overshoot stubs (для миграции с overshoot-сидинга на фильтрованный).

### Шаг 1 — финал

**PHaVE** (Phrasal Verbs List, Garnier & Schmitt 2014):
- Источник: 5 PDF на `norbertschmitt.co.uk`. Парсер на `UglyToad.PdfPig` 0.1.10 (`tools/PhaveImport/`) — regex выделяет 150 phrasal headers + senses + %-occurrence + example.
- Seed: `Seed/phave.json` (60 КБ) → `SeedPhaveIfEmptyAsync` создаёт 150 PhrasalVerb + 288 Sense + 288 Example за 224 мс. `BaseWordId` линкует на базовый глагол ("give" в Words) когда тот существует.

**Обогащение stubs через Free Dictionary API** (`tools/EnrichStubs/`):
- Для 204 AWL/AVL stub-слов: GET `https://api.dictionaryapi.dev/api/v2/entries/en/{lemma}` с 600 мс паузой.
- Парсинг ответа: phonetics (IPA UK/US по audio-URL суффиксу) + meanings → Sense + Example.
- POS-фильтр: предпочесть meaning с тем же POS что у нашего Word; fallback на все meanings если ничего не сматчилось.
- Результат: **173 stubs обогащены** (по 1-13 senses), 31 пропущены — это этнонимы (American, French, Israeli, etc.), которые API считает proper nouns и не выдаёт.

**Перевод EN→RU (3 параллельных подагента, ~10 мин):**
- `tools/Translate/Stubs/` — экспорт обогащённых Words и всех PhrasalVerbs в `input/stubs/batch_stubs_*.json` (3 батча по 103–110 items).
- Каждый агент получает свой батч + системный prompt «IELTS-точный перевод, Oxford Russian Dictionary стиль».
- `tools/Translate/StubsImporter/` — импорт результата обратно: 323 items, 843 Sense.DefinitionRu обновлено, **2 165 Translation rows** добавлено, 0 промахов.

**UI:**
- `SourceFilterItem` + ComboBox «Все источники / Oxford 5000 / AWL / AVL / Мои слова» в `DictionaryView`.
- Чекбокс «Скрыть без определений» (`HideStubs`, дефолт включён) — фильтрует слова, у которых нет `Senses.Any()`.

**Финальное состояние БД после шага 1:**

| Метрика | Значение |
|---|---:|
| Words (всего) | 6 146 |
| — Source = Seed (Oxford 5000) | 5 942 |
| — Source = AWL | 28 |
| — Source = AVL | 176 |
| PhrasalVerbs | 150 |
| Senses (всего) | 6 250 |
| Translations | ~16 600 |
| WordTag rows | 7 824 |
| Tags (типов) | 19 (awl, awl-sublist-1..10, avl, avl-band-1..6, phave) |

### M3 — Категории и тематические разделы (IELTS-extended) ✅ 2026-05-22

Расширение M3 под IELTS-фокус.

**Инфраструктура (миграция `AddCollocations`):**
- `Collocation` entity: `HeadWordId → Word?`, `Headword`, `LinkedText`, `Pattern` (enum: VerbNoun, AdjectiveNoun, VerbAdverb, AdverbAdjective, NounNoun, NounPrepNoun), `DefinitionEn`, `TranslationRu`, `ExampleEn`, `FrequencyRank`, `Source`. Unique index (LinkedText, Pattern).
- `Category` уже была — расширена сидингом 15 IELTS Speaking topics.

**Подэтапы:**

**M3.1 — Категории + IELTS Speaking topical wordlists:**
- 15 категорий первого уровня: travel, education, work, health, food, technology, environment, family, hobbies, sports, media, money, crime, fashion, housing.
- `IeltsCategoriesSeed.Topics` + `SeedIeltsCategoriesIfEmptyAsync` → `Category.Code = "ielts-topic-{code}"`.
- Через подагента сгенерирован `ielts_topics.json` (750 слов = 15 × 50).
- `tools/IeltsTopicsImport/` создал **662 WordCategory связи** (181 missed — слова за пределами текущей БД).

**M3.2 — Неправильные глаголы:**
- Подагент сгенерировал `irregular_verbs.json` (208 verbs, base/past/pastParticiple/translationRu).
- `tools/IrregularVerbsImport/` → **329 WordForm rows** (IsIrregular=true, Kind=PastSimple/PastParticiple). 59 verbs missed (не в БД).

**M3.3 — Linking + Academic phrases (IELTS Writing):**
- 100 фраз по 10 категориям (contrast/addition/cause-effect/example/sequence/emphasis/conclusion/concession/comparison/opinion).
- Хранятся как **новые `Word` с POS="phrase"** + 100 senses + 100 Translation rows. Теги: `ielts-writing-linking` + `ielts-writing-{category}`.

**M3.4 — Task 1 trend vocabulary:**
- 60 единиц по 8 категориям (increase/decrease/stability/fluctuation/degree/comparison/extremes/proportion).
- Слова в основном уже были в БД → 60 existing Words помечены тегами `ielts-writing-task1` + `ielts-task1-{category}`.

**M3.5 — Collocations:**
- 304 collocations по 6 паттернам (V+N, Adj+N, V+Adv, Adv+Adj, N+N, N+prep+N).
- `tools/M3Import/` → инициализация записей в новую таблицу `Collocations`.

**M3.6 — UI расширения:**
- `CategoryFilterItem` + `TagFilterItem` в ViewModels.
- В `DictionaryView.xaml` добавлен второй ряд с двумя ComboBox: «Все категории» / «Все теги».
- Фильтр в `DictionaryViewModel.ReloadAsync` через `WordCategories.Any(wc.CategoryId == catId)` и `WordTags.Any(wt.TagId == tagId)`.
- Опции тегов подгружаются динамически из БД (`Tags.Where(t.WordTags.Any())`).

**Tools (новые):** `tools/IeltsTopicsImport/`, `tools/IrregularVerbsImport/`, `tools/M3Import/` (linking + task1 + collocations в одном проходе).

**Финал БД после M3:**

| Метрика | Значение |
|---|---:|
| Words (всего) | ~6 250 (включая 100 linking phrases с POS="phrase") |
| PhrasalVerbs | 150 |
| Collocations | 304 |
| WordForms (irregular) | 329 |
| Categories (IELTS topics) | 15 |
| WordCategory links | 662 |
| Tags (типов) | 34+ (awl, awl-sublist-1..10, avl, avl-band-1..6, phave, ielts-writing-linking, ielts-writing-{10 категорий}, ielts-writing-task1, ielts-task1-{8 категорий}) |

### M4 — Изображения + расширенные примеры ✅ 2026-05-22

**M4.1 — Tatoeba EN-RU sentence pairs:**
- Источник: tatoeba.org/exports/per_language/eng/ (`eng_sentences.tsv.bz2`, `rus_sentences.tsv.bz2`, `eng-rus_links.tsv.bz2`). Скачивание ~50 MB.
- Распаковка через `SharpZipLib.BZip2`. Join по ID, фильтр по headword (tokenize + Dictionary&lt;lemma, wordIds&gt;).
- Фильтры: длина предложения 25–180 символов, top 8 примеров на слово, dedupe по TextEn.
- `tools/TatoebaImport/`: **41 724 Examples** для 5 937 distinct слов за 7 сек.

**M4.2 — ImageCacheService (Wikimedia + Pexels):**
- `IImageProvider` interface, `ImageResult` record (Url, ThumbnailUrl, Attribution, License, Size, ProviderName).
- `WikimediaImageProvider` (default, без ключа): Wikipedia REST API summary endpoint + MediaWiki Commons search fallback.
- `PexelsImageProvider`: `https://api.pexels.com/v1/search` с Authorization header. `IsAvailable` ↔ наличие ключа в `IAppSettings`.
- `IAppSettings` + `AppSettings` — persist в `%AppData%\EnglishStudio\settings.json`.
- `ImageCacheService.GetOrFetchAsync(wordId, maxImages, IProgress)`:
  - In-flight dedup через `ConcurrentDictionary&lt;wordId, Task&gt;`.
  - Проверка существующих `MediaAsset.Kind=Image`.
  - Iterate providers (Pexels first if available, иначе Wikimedia).
  - Скачивание в `Media/Images/{wordId}_{provider}_{guid}.{jpg|png|webp}`, запись MediaAsset.
- DI: `AddHttpClient` для 3 клиентов (Wikimedia, Pexels, ImageDownload), AddSingleton всех провайдеров.

**M4.3 — UI — расширенные примеры + галерея:**
- `WordDetailViewModel.Examples` теперь `IReadOnlyList&lt;ExampleDetail&gt;` (TextEn + TextRu + Source) вместо строк.
- В XAML каждый Example отрисован двумя строками (EN жирно, RU курсивом серым).
- В `DictionaryViewModel` добавлены `CurrentImagePath` и `ImageStatus`. После `LoadDetailAsync` стартует `StartImageLoadAsync` с `CancellationTokenSource` (отменяется при смене слова).
- В XAML — Image control max 240×360 + caption «Иллюстрация».

**M4.4 — Settings UI:**
- `SettingsViewModel` (CommunityToolkit.Mvvm) с PexelsApiKey + Save/Clear commands.
- `Views/Settings/SettingsWindow.xaml` — modal-окно 520×320 со ссылкой на pexels.com/api.
- Кнопка `⚙` в правом верхнем углу `DictionaryView` (`IconButton` стиль).

**Финал БД после M4:**

| Метрика | До M4 | После M4 |
|---|---:|---:|
| Examples | 6 326 | **48 235** |
| Examples с Source="tatoeba" | 0 | 41 724 |
| Examples с TextRu (двуязычные) | ~16 600 (Translation rows) | **41 724** (Examples.TextRu) |
| MediaAssets — Image | 0 | заполняется lazy при открытии карточки |

**Tools:** `tools/TatoebaImport/`.

### M5 — SRS-тренажёр (FSRS-4.5) ✅ 2026-05-22

Главная learning-фича. FSRS-4.5 алгоритм с 17 weights, polymorphic `UserWordProgress` (Word/PhrasalVerb/Collocation), полное trainer-UI с rating-кнопками, статистика, настройки daily limits.

**Сущности (миграция `AddSrsSchema`):**
- `UserWordProgress` расширен: `WordId?`, `PhrasalVerbId?`, `CollocationId?` (polymorphic XOR FK с filtered unique индексами), `CreatedAt`, `UpdatedAt`. `Stability`, `Difficulty`, `State` (New/Learning/Review/Relearning), `LastReviewedAt`, `NextReviewAt`, `ReviewCount`, `LapseCount` — уже было.
- `ReviewLog` (новая) — история reviews: Rating, State/Stability/Difficulty before/after, ElapsedDays, ScheduledIntervalDays, ReviewedAt. Для retention rate и графиков.
- `SrsRating` enum (Again=1, Hard=2, Good=3, Easy=4).

**FSRS-4.5 алгоритм (`Srs/FsrsScheduler.cs`):**
- 17 default weights из open-spaced-repetition/fsrs4anki. `FsrsParameters` configurable.
- Pure-функциональный: `InitializeFromFirstReview(progress, rating, now)` и `Schedule(progress, rating, now)` — мутируют progress, возвращают `ReviewLog`.
- Формулы Retrievability `R = (1 + 19/81 * elapsed/S)^(-0.5)`, Stability update с hard-penalty/easy-bonus, Difficulty с mean reversion, Interval `I = S * (R^(1/-0.5) - 1) / (19/81)`.
- State machine: New → Review (после первого rating, Again → Relearning); Review + Again → Relearning + LapseCount++; Relearning + Good/Easy → Review.

**Сервис (`Srs/SrsService.cs`):**
- `AddWord/PhrasalVerb/Collocation` (idempotent).
- `BuildSessionAsync(maxNew, maxReview, now)` — берёт due cards (NextReviewAt ≤ now) и новые до квот, interleave-mix.
- `RateAsync(progressId, rating, now)` — применяет FSRS scheduler, сохраняет ReviewLog.
- `GetStatsAsync(now)` → `SrsStats(Total, byState, DueToday, ReviewedToday, LapsesToday, RetentionRate30d)`.

**UI:**

| View | Что |
|---|---|
| `MainWindow.xaml` | `TabControl` с 3 табами: 📖 Словарь / 🎯 Тренажёр / 📊 Статистика. При переходе на Stats — авто `RefreshAsync()`. |
| `TrainerView` | Карточка с Headword + IPA + 🔊 Audio (UK/US). Кнопка «Показать перевод» → раскрывает chips с переводами + DefinitionRu + DefinitionEn + до 3 примеров. 4 кнопки оценки: Снова/Сложно/Хорошо/Легко. Счётчик «Сегодня: X из N». Empty state со ссылкой на словарь. |
| `StatsView` | 4 верхние плитки (Всего / К повторению сегодня / Просмотрено сегодня / Retention 30 дней). Нижний блок — счётчики по States (Новые/Изучаются/Повторяются/Забыты). |
| `DictionaryView` | Кнопка «📚 В изучение» в карточке слова, превращается в «✓ В изучении» (DataTrigger) когда добавлено. |
| `SettingsWindow` | Расширен: Daily new limit / Daily review limit / Target retention (0.70–0.99) + Pexels API key. |

**TrainerCardViewModel** — общий вид для Word/PhrasalVerb/Collocation: Headword, POS, IPA, HasAudio*, TranslationsRu, DefinitionRu/En, Examples. Mapping в `ToCard(UserWordProgress)`.

**Settings (`IAppSettings` расширен):** `DailyNewLimit=20`, `DailyReviewLimit=100`, `TargetRetention=0.9`. Persisted в `%AppData%\EnglishStudio\settings.json`.

### M6 — Тренировка произношения (Whisper.net) ✅ 2026-05-22

Pronunciation training через offline-Whisper для IELTS Speaking pronunciation criterion.

**Архитектура:**
- `IAudioRecorder` (`NAudioRecorder`) — NAudio `WaveInEvent` → WAV 16 kHz mono PCM в `%AppData%\EnglishStudio\Pronunciation\`. Singleton, потокобезопасный через lock.
- `IWhisperTranscriber` (`WhisperTranscriber`) — Whisper.net 1.7.4 с `Whisper.net.Runtime`. Lazy model download (ggml-base.en ~142 МБ) с `IProgress<string>` для UI. Кэш модели в `%AppData%\EnglishStudio\Models\ggml-base.en.bin`. `WhisperFactory.FromPath` создаётся один раз.
- `PronunciationAssessor` — нормализованный Levenshtein на словах: `score = 100*(1 - distance/maxLen)`. Категории: ≥90 = Excellent, ≥70 = Good, <70 = Poor.
- `PronunciationAttempt` entity + миграция `AddPronunciationAttempts`: Id, WordId, TargetText, RecognizedText, Score, Category, RecordedAt.

**Flow при первом запуске:**
1. Пользователь кликает «🎤 Произнести» в карточке слова.
2. Открывается inline-панель в карточке.
3. Whisper модель скачивается (с прогрессом «X.X / 142.0 МБ»). Один раз навсегда.
4. После загрузки — кнопка «🎤 Начать запись», микрофон активируется через NAudio.
5. Пользователь произносит слово, жмёт «⏹ Остановить запись».
6. Whisper транскрибирует WAV в текст (~1–3 сек на CPU).
7. Assessor сравнивает с target headword → score 0-100.
8. Показывается балл + feedback + распознанный текст. Attempt сохраняется в БД.

**UI:**
- В карточке слова в `DictionaryView` — кнопка «🎤 Произнести» рядом с «📚 В изучение».
- Inline-панель с эталоном (🔊 UK / 🔊 US), кнопкой записи (с DataTrigger «🎤 Начать запись» ↔ «⏹ Остановить запись»), статусом, и блоком результата (балл 0/100 + feedback + recognized text).

**Подводные камни (записаны в память):**
- `IHttpClientFactory` требует `using System.Net.Http` в WPF проекте (не подтягивается транзитивно через Hosting).
- Whisper.net требует native runtime — пакет `Whisper.net.Runtime` подтягивает x64 dll автоматически при build.
- Whisper.cpp ожидает строго **16 kHz mono PCM**. NAudio `WaveFormat(16000, 16, 1)` именно это.
- Enum конфликт `App.Audio.PronunciationCategory` vs `Entities.PronunciationCategory` — решён через `using` aliases.

### Дополнительная инфраструктура

- `tools/Diag/Program.cs` — диагностика БД (counts, distributions, sample queries)
- `tools/Translate/Importer/Program.cs` — батч-импортёр переводов
- `tools/convert-oxford-seed.ps1` — генератор seed.json из исходника

---

## IELTS-секции — статус (всё закрыто)

**Полный план реализации M7–M11:** [IELTS_PLAN.md](IELTS_PLAN.md)

- ✅ **M7.0** — Shell + Sidebar + `Modules.Ielts.Core` + `Modules.Ai` (Claude CLI subprocess) + `ChromedWindow` для child-окон
- ✅ **M7 Reading** — UI готов, импорт практических + 4 экзаменационных (`IsExamOnly`) тестов; дополнительно импортированы **все Cambridge IELTS 15–20** (`ielts{book}-r-test{n}`) — итого 58 reading-наборов. Все 14+ типов вопросов
- ✅ **M9 Writing** — `Modules.Ielts.Writing`, сущности в Core, сервисы `IWritingTaskService`/`WritingFeedbackService`/`WritingSeedService`. UI: Hub + `WritingSessionView` (таймер 60 мин, табы Task 1/2, word-count, 2-pane image+prompt) + `WritingResultView` (combined band T1×⅓+T2×⅔, 4 критерия, RU/EN feedback, эталон band 8). Контент расширен до **24 тестов** (Cambridge 15–20, импорт пользователя). Claude CLI оценка работает
- ✅ **M10 Speaking** — Cambridge .txt-импорт (24 теста), Whisper medium.en с word-timestamps, `SpeechMetricsAnalyzer` (WPM/паузы/филлеры/TTR), AI band-оценка, cue-card subpoints в UI. Full mock привязан к Cambridge-тесту. Прогресс транскрибации (%) + плеер ответов (play/pause/stop/seek) в результате
- ✅ **M8 Listening** — 24 теста Cambridge 15–20 с реальными MP3, `IListeningTestService` через `ITestRunner`/`TestAttempt`, карусель из 5 типов карточек, AI-оценка
- ✅ **M11 Mock Test** — `Modules.Ielts.Mock`: `MockBundlePicker` (все 4 секции из одной Cambridge-книги), `MockSessionService` (оркестратор + посекционный resume), хост-автомат L→R→W→S, `MockResultView` (4 sub-band + overall по официальной формуле + разбор секций). 19 интеграционных тестов

## ⏳ Что предстоит (полировка / инфра — не блокирует)

### Возможные дополнения к M6

- **Phonetic comparison** — конвертировать recognized text → IPA через CMUdict, сравнивать с эталоном по фонемам, давать feedback по проблемным звукам
- **Shadowing mode** — прослушать эталон → произнести → сравнить
- **History UI** — последние N попыток на слово с тенденцией (улучшается/нет)

### Долгосрочные / не-milestone задачи

**Архитектура и качество**
- Расширить unit-тесты (xUnit). Заложен фундамент — `tests/EnglishStudio.Integration.Tests` (19 тестов по Mock, EF Core Sqlite in-memory); план покрытия scoring/checkers/FSRS/seed — `plans/Infra_UnitTests.md`
- CI на GitHub Actions: build + test на каждом push
- Installer (Inno Setup или WiX) для распространения — `plans/Infra_Installer.md`
- Логирование в файл (Serilog) + просмотр логов из приложения, crash-репорты — `plans/Infra_Logging_CrashReports.md`
- Версионирование (semver, changelog)

**Block-based навигация** — ✅ сделано
- Shell с левым sidebar + `ContentControl` реализован; 8 модулей через `AddXModule()` + `IModuleDescriptor` (`Code`/`NameRu`/`IconGlyph`/`Order`/`ViewFactory`). `MainWindow` = `ShellView`

**UX и опции**
- Settings UI: переключение темы (DarkBlue ↔ Light), размер шрифта, плотность списка
- Light палитра — допилить (сейчас stub)
- Многоязычность интерфейса (en/ru) через RESX
- Хоткеи: Ctrl+F (поиск), Ctrl+1..6 (фильтр CEFR), стрелки для навигации по списку
- Drag & drop для импорта пользовательских списков слов
- Экспорт прогресса в JSON (для бэкапа в OneDrive)
- Печать карточек или экспорт в Anki

**Расширение контента**
- Free Dictionary API для слов вне seed (когда юзер ищет незнакомое слово — подкачка на лету)
- Fuzzy-search (typo-tolerant) — Levenshtein на индексе
- Этимология через Wiktionary
- Синонимы / антонимы (WordNet через REST)
- Словосочетания (collocations) — частотные связи слов

**Мультиязычность (заложенный потенциал)**
- Сейчас структура поддерживает только English→Russian
- Будущее: добавить другие пары (German→Russian, Spanish→Russian)
- Каждый язык-модуль = отдельный `EnglishStudio.Modules.X` classlib с собственным seed и DbContext (или общая БД с Language entity)

**Опциональные интеграции**
- Облачная синхронизация прогресса (DbContext в OneDrive-папке работает прямо сейчас, но без conflict resolution)
- Импорт списков слов из Anki / Quizlet
- Веб-версия через Blazor (если станет интересно)

---

## Сводка по объёму

| Этап | Файлов кода | Строк (примерно) | Время |
|---|---:|---:|---:|
| Сделано (M0+M1) | ~50 .cs + ~10 .xaml + 4 PowerShell + JSON | ~3500 | 1 день |
| M2 | ~10 .cs | ~500 | 0.5-1 день |
| M3 | ~15 .cs + 3 .xaml + seed | ~800 | 1-2 дня |
| M4 | ~10 .cs + 2 .xaml | ~600 | 1-2 дня |
| M5 | ~20 .cs + 5 .xaml | ~1500 | 2-3 дня |
| M6 | ~8 .cs + 1 .xaml | ~500 | 1 день |
| **Всего** | **~120 файлов** | **~7400 строк** | **~7-10 рабочих дней** |

Это только блок «Английский словарь». Параллельно или после — будущие блоки приложения (грамматика, тесты, диалоги и т.д.).
