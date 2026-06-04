# План — Публикация EnglishStudio на GitHub (вынос контента + импортёр)

**Создано:** 2026-06-03
**Цель:** сделать репозиторий пригодным для публичной публикации под MIT, не нарушая копирайт.
Весь защищённый контент (Cambridge IELTS, Oxford 5000, PHaVE) выносится из исходников во внешний
**content-pack**, который пользователь скачивает отдельно и загружает через встроенный **импортёр**.
Разделы без импортированного контента показывают приглашение импортировать.

**Связанные документы:** `README.md` (раздел Content & Legal), `NOTICE`, `.gitignore` (контент уже
игнорируется). Инфра-файлы (LICENSE/README/NOTICE/.gitignore) уже созданы — этот план про код.

---

## 0. Текущее состояние (факты из кода — на чём строим)

Очень удобно: рантайм-папка контента **уже** `%AppData%\EnglishStudio\IeltsContent\<Module>\<code>\`.

| Модуль | Что embedded (`.csproj`) | Как грузится | Куда кладёт медиа |
|---|---|---|---|
| Dictionary | `oxford_5000.json`, `phave.json` (копирайт); `awl.json`, `avl.json` (CC0) | `SeedManifest.Open*()` → embedded stream | — (только в БД) |
| Reading | `ielts_reading_tests.json` + `Seed/Images/*` | `GetManifestResourceStream` | `IeltsContent/Reading/<code>/` (`Copy*ToAppData` + `ResolveImageAbsolutePath`) |
| Listening | `ielts_listening_tests.json` + `Seed/{Audio,Images,Transcripts}/*` (~760 МБ MP3) | `GetManifestResourceStream` | `IeltsContent/Listening/<code>/` |
| Writing | `writing_tests.json` + `Seed/Images/*` | `GetManifestResourceStream` | `IeltsContent/Writing/<code>/` |
| Speaking | **ничего** | `CambridgeSpeakingImportService.ImportIfPossibleAsync()` читает `.txt` из **внешней** папки (`Downloads\Telegram Desktop`) | — |
| Ai | `Rubrics/*.md` | `RubricLoader` | — |

Ключевые helper'ы: `DictionaryPaths.AppDataRoot`, у каждого IELTS-сервиса —
`Get*ContentRoot(code)` = `Path.Combine(AppDataRoot, "IeltsContent", "<Module>", code)`,
`Resolve*AbsolutePath` и `Copy*ToAppData` (извлекает embedded → IeltsContent). Сервисы вызываются в
`App.xaml.cs.OnStartup` (`SeedIfMissingAsync`, `ImportIfPossibleAsync`, `SeedService.*`).

**Вывод:** «вынос» = `IeltsContent/` наполняет **импортёр из pack'а**, а не извлечение из DLL; seed-
сервисы читают JSON из `IeltsContent/`, а не из embedded-ресурса. Медиа уже резолвятся в `IeltsContent/`.

---

## 1. Целевая модель

```
[Content Pack]  --импортёр-->  %AppData%\EnglishStudio\IeltsContent\  --seed-->  dictionary.db + медиа на диске
 (скачивается                  (канонический рантайм-корень,                     (как сейчас)
  отдельно,                      идентичен layout'у pack'а)
  не в git)
```

- Pack — обычная папка/ZIP, которую пользователь скачивает отдельно (из приватного источника, не из
  публичного репо — Cambridge раздавать нельзя).
- Импортёр копирует pack → `IeltsContent/`, валидирует, запускает (ре)сидинг затронутых модулей.
- Без импорта: AWL/AVL (CC0) и базовый каркас работают; разделы Reading/Listening/Writing/Speaking/Mock
  и словарь Oxford показывают «Контент не импортирован → Импортировать».

---

## 2. Структура content-pack (канон = layout `IeltsContent/`)

```
EnglishStudio-Content/
├── manifest.json
├── Dictionary/
│   ├── oxford_5000.json
│   └── phave.json
├── Reading/
│   ├── ielts_reading_tests.json
│   └── <code>/<image>.png              (напр. acad-r-test27/volcano_diagram.png)
├── Listening/
│   ├── ielts_listening_tests.json
│   └── <code>/{audio1.mp3,…,<image>.png,transcript.txt}
├── Writing/
│   ├── writing_tests.json
│   └── <code>/<image>.png
└── Speaking/
    └── <Cambridge .txt структура>      (как ждёт CambridgeSpeakingTestParser)
```

Важно: медиа лежат в подпапках `<code>/`, потому что `Resolve*AbsolutePath` строит путь как
`IeltsContent/<Module>/<code>/<relative>`. Embedded-имена сейчас плоские
(`ielts15-test2.audio1.mp3`); pack-builder (Фаза C) раскладывает их по `<code>/`.

`manifest.json`:
```json
{
  "packVersion": 1,
  "createdAt": "2026-06-03",
  "sections": {
    "dictionaryOxford": true,
    "dictionaryPhave": true,
    "reading": true,
    "listening": true,
    "writing": true,
    "speaking": true
  }
}
```
Манифест задаёт, какие секции присутствуют (для gating и сообщений), и версию (для будущих обновлений).

---

## 3. Фазы реализации

### Фаза A — Корень контента + слой доступности (≈1.5 ч)

- В `DictionaryPaths` добавить `IeltsContentRoot => Path.Combine(AppDataRoot, "IeltsContent")`.
- Новый сервис `IContentStore` (в `Modules.Ielts.Core` или App):
  - `string ContentRoot { get; }`
  - `bool IsImported(ContentSection section)` — наличие ключевых файлов (JSON/манифест) на диске.
  - `ContentManifest? ReadManifest()`.
  - `Stream? OpenJson(string module, string file)` / `string? ResolveFile(...)`.
- `enum ContentSection { DictionaryOxford, DictionaryPhave, Reading, Listening, Writing, Speaking }`.
- Регистрация в DI.

### Фаза B — Перевод seed-сервисов с embedded на файловый источник (≈3 ч)

Систематично, по одному паттерну для Reading/Listening/Writing:
- JSON: вместо `GetManifestResourceStream(ResourceFileName)` →
  `File.OpenRead(Path.Combine(ContentRoot, "<Module>", ResourceFileName))`. **Если файла нет — мягкий
  выход (секция просто пуста), без исключения.**
- Удалить `Copy*ToAppData` (извлечение embedded больше не нужно — медиа уже в `IeltsContent/` после
  импорта). `Resolve*AbsolutePath`/`Get*ContentRoot` оставить как есть (пути уже корректны).
- `SeedIfMissingAsync`: в начале `if (!contentStore.IsImported(<section>)) { _log...; return; }`.

Dictionary (`SeedService`):
- `SeedIfEmptyAsync` (Oxford): `SeedManifest.OpenOxford5000()` → читать из `ContentRoot/Dictionary/oxford_5000.json`; если нет — пропустить (словарь без Oxford = почти пустой, это норм для не-импортированного состояния).
- `SeedPhaveIfEmptyAsync`: аналогично из `ContentRoot/Dictionary/phave.json`.
- `SeedAwlIfEmptyAsync` / `SeedAvlIfEmptyAsync`: **оставить embedded** (CC0).
- `BackfillAudioPathsAsync` (Oxford-произношения): аудио и так качается split-zip'ом в рантайме — не трогаем.

`.csproj` — убрать копирайт-`EmbeddedResource`:
- Dictionary: убрать `oxford_5000.json`, `phave.json`; **оставить** `awl.json`, `avl.json`.
- Reading: убрать `ielts_reading_tests.json`, `Seed/Images/*`.
- Listening: убрать `ielts_listening_tests.json`, `Seed/{Audio,Images,Transcripts}/*`.
- Writing: убрать `writing_tests.json`, `Seed/Images/*`.
- Ai `Rubrics/*.md` — оставить (свой текст; см. риск про band descriptors).

После этого **свежий клон собирается без копирайт-файлов** (главная цель публикации). DTO/парсеры
не меняются — меняется только источник байт.

### Фаза C — Сборка pack'а из текущего контента (≈1.5 ч)

Одноразовая C#-утилита `tools/ContentPackBuilder/`:
- Вход: текущие `src/.../Seed/` папки (файлы есть на диске, даже будучи git-ignored).
- Выход: папка `EnglishStudio-Content/` в layout'е из §2.
- Логика: для каждого модуля скопировать JSON в `<Module>/`; медиа с плоским именем
  `<code>.<rel>` разложить в `<Module>/<code>/<rel>` (распарсить code по тем же правилам, что
  `Copy*ToAppData`); Speaking — скопировать `.txt`-структуру; сгенерировать `manifest.json`.
- Результат — твой готовый pack (хранишь приватно, раздаёшь пользователям отдельно).

### Фаза D — Сервис импорта (≈2 ч)

`ContentImportService` (App или Core):
- `Task<ImportResult> ImportAsync(string packFolder, IProgress<ImportProgress>, ct)`:
  1. Валидировать: есть `manifest.json` и ожидаемые JSON по секциям.
  2. Скопировать pack → `IeltsContentRoot` (с прогрессом по файлам/байтам; перезапись = обновление).
  3. Запустить (ре)сидинг затронутых модулей: `ReadingSeedService.SeedIfMissingAsync` и т.д.,
     `SeedService.SeedIfEmptyAsync/SeedPhaveIfEmptyAsync`, `CambridgeSpeakingImportService.ImportIfPossibleAsync`.
     (Сидинг идемпотентен — `SeedIfMissing`/`SeedIfEmpty`.)
  4. Вернуть сводку: какие секции импортированы, счётчики, ошибки.
- Поддержать также импорт из **ZIP** (распаковать во временную папку → как из папки). Через
  `System.IO.Compression` (это обычный zip, не split — DotNetZip не нужен).

### Фаза E — UI импортёра (≈2 ч)

- Раздел в `SettingsWindow` **или** отдельный экран «Импорт контента»:
  - Кнопка «Выбрать папку с контентом» (`OpenFolderDialog` .NET 8+/WPF) или «Выбрать ZIP».
  - Прогресс (определённый, по байтам — как в split-zip аудио M2).
  - Итоговая сводка + ошибки; кнопка «Готово» перезагружает хабы.
- Тематически — на базе `ChromedWindow`/стилей приложения (как `AiProcessingWindow`).

### Фаза F — Gating разделов без контента (≈2.5 ч)

- Переиспользуемый `Views/Controls/ContentMissingView` (баннер/заглушка): «🔒 Контент раздела не
  импортирован» + текст + кнопка «Импортировать контент» (открывает Фазу E).
- В каждом Hub-VM (`ReadingHubViewModel`, `ListeningHubViewModel`, `WritingHubViewModel`,
  `SpeakingHubViewModel`, `MockHubViewModel`) + словарь: при загрузке проверять
  `contentStore.IsImported(section)`; если нет — показывать `ContentMissingView` вместо контента
  (свойство `IsContentMissing` + DataTrigger/Visibility).
- Mock: показывать приглашение, если не хватает хотя бы одной из L/R/W/S (или мягко — бандлы просто
  не соберутся; лучше явное сообщение).
- Словарь: если Oxford не импортирован — баннер (AWL/AVL-каркас сам по себе мало полезен).

### Фаза G — Поведение при старте (≈0.5 ч)

`App.xaml.cs.OnStartup`: сидинг-вызовы уже идут через `SeedIfMissing`/`SeedIfEmpty`. После Фазы B они
сами мягко выходят при отсутствии контента. Убедиться, что **старт не падает** на чистой машине без
pack'а (миграции БД применяются всегда; AWL/AVL сидятся всегда; всё остальное — по наличию).

### Фаза H — Унификация Speaking (≈1 ч)

- `CambridgeSpeakingImportService` сейчас читает из `Downloads\Telegram Desktop` (const
  `DefaultRelativeBase`). Перенацелить на `IeltsContent/Speaking/` (часть pack'а), убрать персональный
  дефолт. Импорт Speaking встроить в общий `ContentImportService` (Фаза D).

### Фаза I — Тесты + документация + проверка чистого клона (≈2 ч)

- Интеграционные тесты (`tests/EnglishStudio.Integration.Tests`):
  - `ContentImportService.ImportAsync` копирует pack во временный `IeltsContentRoot` и сидит БД (на
    мини-pack'е из 1 теста на секцию).
  - `IsImported` корректен до/после импорта.
  - Seed-сервис читает из папки (а не из embedded) и при отсутствии файла — no-op.
- `docs/CONTENT_PACK.md` — точный layout pack'а (§2) + manifest schema + как собрать свой.
- `README.md` — дополнить разделом «How to add content»: скачать app → собрать/получить pack →
  запустить импортёр.
- **Проверка чистого клона:** `git clone` (без копирайт-файлов, они git-ignored) → `dotnet build`
  проходит → приложение стартует → все IELTS-разделы показывают «Импортировать» → после импорта pack'а
  всё работает.

---

## 4. Поток пользователя (целевой)

1. Скачивает приложение (релиз/сборка из исходников).
2. Скачивает content-pack (отдельно — свой легально полученный).
3. Запускает приложение → IELTS-разделы показывают «Контент не импортирован → Импортировать».
4. Открывает импортёр, указывает папку/ZIP pack'а → прогресс → готово.
5. Контент сидится из `IeltsContent/`; все тесты и разделы IELTS работают.
6. Без шага 4 — словарь (AWL/AVL-каркас) и тренажёр базово работают; IELTS-разделы заблокированы с
   подсказкой.

(Автоматически качается только бесплатное: модель Whisper, предложения Tatoeba, Oxford-аудио split-zip —
не часть pack'а, не копирайт-блокер репозитория.)

---

## 5. Acceptance

- ✅ Свежий `git clone` собирается (`dotnet build EnglishStudio.slnx`) **без** копирайт-файлов.
- ✅ В репо нет ни одного файла Cambridge/Oxford/PHaVE (проверка: `.gitignore` + ручной аудит `src/`).
- ✅ На чистой машине без pack'а приложение стартует, не падает; IELTS-разделы и Oxford-словарь
  показывают приглашение импортировать; AWL/AVL и тренажёр работают.
- ✅ Импортёр из папки И из ZIP корректно наполняет `IeltsContent/` и сидит БД; все 4 секции + Mock и
  Oxford-словарь становятся доступны.
- ✅ Повторный импорт идемпотентен (обновляет, не дублирует).
- ✅ `dotnet test` зелёный (старые 19 + новые тесты импорта).
- ✅ `tools/ContentPackBuilder` собирает валидный pack из текущего `Seed/`.

---

## 6. Риски и заметки

| Риск / вопрос | Решение |
|---|---|
| Литеральные `EmbeddedResource Include` на удалённые файлы ломают сборку | Фаза B их убирает целиком — клон собирается чисто. AWL/AVL остаются |
| `IeltsContent/` уже частично заполнен у тебя (извлечён из DLL) | Импортёр перезаписывает; либо очистить `IeltsContent/` перед первым тестом нового пути |
| Band-descriptor рубрики в `Modules.Ai/Rubrics/` могут быть копирайтом IELTS | Перед публикацией проверить, что это перефразировка/свой текст; иначе тоже вынести |
| Oxford-произношения (split-zip с чужого GitHub) | Остаются рантайм-загрузкой, не в репо — не блокер; упомянуть в NOTICE (уже есть) |
| GitHub лимит 100 МБ/файл, большой репо | После выноса репо «лёгкий» (только код + AWL/AVL); pack раздаётся вне git |
| Пользователь раздаёт pack публично | Не наша ответственность в коде; README прямо говорит: материалы — свои, легально полученные |
| Размер pack'а ~760 МБ | Можно бить на под-pack'и по секциям (манифест уже посекционный) — опциональный follow-up |

---

## 7. Оценка

| Фаза | Время |
|---|---|
| A — content root + IContentStore | 1.5 ч |
| B — seed-сервисы на файлы + csproj | 3 ч |
| C — ContentPackBuilder | 1.5 ч |
| D — ContentImportService (папка + ZIP) | 2 ч |
| E — UI импортёра | 2 ч |
| F — gating разделов | 2.5 ч |
| G — поведение при старте | 0.5 ч |
| H — унификация Speaking | 1 ч |
| I — тесты + доки + проверка клона | 2 ч |
| **Итого** | **~16 ч** |

Можно вести двумя потоками после Фазы A: backend (B, C, D, G, H) и UI (E, F), сходятся на I.
