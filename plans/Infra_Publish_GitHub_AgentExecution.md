# План исполнения для двух агентов — Публикация EnglishStudio на GitHub (вынос контента)

**Создано:** 2026-06-03
**Базируется на:** `plans/Infra_Publish_GitHub.md` (исходный план, 9 фаз). Этот документ — детальная,
готовая к исполнению версия, разбитая на **два параллельно работающих агента (A — backend, B — UI)**
с точными файлами, сигнатурами, границами ответственности, точками синхронизации, git-процессом и
заделом под будущие модули/версии.

> **Статус кода:** на момент создания плана код **не написан** — это спецификация. Все блоки кода ниже
> — это *целевые сигнатуры* (контракт), а не закоммиченный код.

---

## 0. Как читать этот документ

1. **§1 Архитектурные решения** — что уточнили против исходного плана и почему (читают оба агента).
2. **§2 Фаза 0 (общий контракт)** — делается ПЕРВОЙ, до развилки. Один общий коммит. Оба агента
   ревьюят. После неё работа идёт параллельно.
3. **§3 Разделение труда + границы файлов** — кто что трогает (чтобы не было merge-конфликтов).
4. **§4 Точки синхронизации** — где агент B ждёт артефакт агента A.
5. **§5 Агент A (backend)** и **§6 Агент B (UI)** — пошагово, со всеми деталями.
6. **§7 Совместная фаза I** — интеграционные тесты + доки + проверка чистого клона.
7. **§8 Git-процесс**, **§9 Расширяемость**, **§10 Реестр файлов**, **§11 Acceptance**, **§12 Риски**.

---

## 1. Архитектурные решения (уточнения к исходному плану)

Все факты ниже **проверены по коду** (2026-06-03).

### 1.1 Где живёт контракт `IContentStore` — в `Modules.Dictionary`, НЕ в Core

Граф зависимостей проектов (проверено по `.csproj`):

```
Dictionary  (базовый, без внутренних зависимостей)
   ▲
   │ ссылается
Ielts.Core  ──► Dictionary
Modules.Ai  (рубрики/AI-фидбек; не зависит от IELTS-модулей)
   ▲
   │
Ielts.Reading                       ──► Core ──► Dictionary
Ielts.{Listening,Writing,Speaking}  ──► Core + Modules.Ai   (AI-фидбек)
Ielts.Mock                          ──► Core
   ▲
App  ──► все модули
```

> **Уточнение по `.csproj` (проверено):** Reading ссылается **только** на `Ielts.Core`; а
> **Listening, Writing и Speaking** ссылаются дополнительно на **`EnglishStudio.Modules.Ai`** (для
> AI-фидбека), не только на Core. На размещение `IContentStore` это не влияет — `Dictionary` всё равно
> ниже и Core, и Ai, и всех IELTS-модулей.

`Dictionary.SeedService` (Oxford/PHaVE) тоже должен потреблять `IContentStore`, а Dictionary **не**
ссылается на Core. Значит общий контракт обязан лежать в **самом нижнем** проекте, который видят все
seed-сервисы, включая Dictionary → это **`Modules.Dictionary`**. Исходный план говорил «Core или App»
— это ошибка для Dictionary. **Решение:** новый неймспейс `EnglishStudio.Modules.Dictionary.Content`.

### 1.2 Новый проект-оркестратор `EnglishStudio.Content`

`ContentImportService` должен звать seed-сервисы из **всех** IELTS-модулей + `Dictionary.SeedService`.
Сейчас единственный проект, видящий все модули, — это `App`, но он WPF-`WinExe`: ссылаться на него из
тест-проекта для интеграционного теста импорта — больно. **Решение:** ввести **новую class-library**
`src/EnglishStudio.Content/` (ссылается на Dictionary + Reading + Listening + Writing + Speaking), куда
кладём реализацию `ContentImportService`. На неё ссылаются и `App`, и тест-проект. Это:
- делает импорт тестируемым без ссылки на WPF-exe;
- укладывается в «проект растёт, добавляются модули» — будущие модули просто подключаются к оркестратору
  в одном месте.

> **Интерфейс** `IContentImportService` и DTO (`ImportProgress`, `ImportResult`) кладём в
> `Dictionary.Content` (см. §2), чтобы VM в App ссылался на интерфейс, а реализация пришла из
> `EnglishStudio.Content` позже — это и есть seam для параллельной работы (агент B кодит против
> интерфейса, пока агент A пишет реализацию).
>
> *Fallback (если не хотим новый проект):* положить `ContentImportService` в `App`, а интеграционный
> тест импорта сделать через прямой вызов seed-сервисов в тест-проекте (минуя оркестратор). Рекомендуем
> основной вариант с `EnglishStudio.Content`.

### 1.3 Инъектируемый корень контента (главный пробел исходного плана)

Все seed-сервисы строят путь из **статического** `DictionaryPaths.AppDataRoot`
(`…/IeltsContent/<Module>/<code>/`). Для (а) интеграционного теста, импортирующего во **временную**
папку, и (б) чистого DI нужен один источник правды.

**Решение (минимальный диф, в духе кодовой базы со статиками):**
- Добавить в `DictionaryPaths` тест-сем (settable static override) + готовый корень:
  ```csharp
  // DictionaryPaths.cs
  private static string? _appDataRootOverride;
  /// <summary>Тест-сем: если задан, перекрывает %AppData% (для интеграционных тестов).</summary>
  public static string? AppDataRootOverride { get => _appDataRootOverride; set => _appDataRootOverride = value; }

  public static string AppDataRoot =>
      _appDataRootOverride
      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

  public const string IeltsContentFolderName = "IeltsContent";
  public static string IeltsContentRoot => Path.Combine(AppDataRoot, IeltsContentFolderName);
  ```
- `IContentStore.ContentRoot` возвращает `DictionaryPaths.IeltsContentRoot`. Все новые проверки идут
  через `IContentStore`; существующие `Resolve*AbsolutePath`/`Get*ContentRoot` остаются статикой, но
  уже указывают на тот же корень (тест-override влияет и на них).
- В тестах: `DictionaryPaths.AppDataRootOverride = tempDir` в начале, `= null` в Dispose; импорт-тесты
  собрать в **не-параллельную** xUnit-коллекцию (`[Collection("ContentIO")]`), т.к. override — глобальный
  статик.

### 1.4 Размещение реализаций (итог)

| Тип | Проект | Неймспейс |
|---|---|---|
| `IContentStore`, `ContentSection`, `ContentManifest`, `IContentImportService`, `ImportProgress`, `ImportResult`, `ImportedSection` | **Modules.Dictionary** | `…Dictionary.Content` |
| `FileSystemContentStore : IContentStore` | **Modules.Dictionary** | `…Dictionary.Content` |
| `ContentImportService : IContentImportService` | **EnglishStudio.Content** (новый) | `EnglishStudio.Content` |
| `ContentImportWindow` + `ContentImportViewModel`, `ContentMissingView` | **App** | `…App.Views/ViewModels` |
| `ContentPackBuilder` (CLI) | **tools/ContentPackBuilder** (новый) | — |

### 1.5 Прочие проверенные факты, влияющие на план

- **Конструкторы seed-сервисов идентичны:** Reading/Listening/Writing —
  `(IDbContextFactory<IeltsDbContext> dbFactory, ILogger<…> log)`; Dictionary `SeedService` —
  `(DictionaryDbContext db, ILogger<SeedService> logger)`. Добавляем параметр `IContentStore`.
- **Speaking уже готов к Фазе H:** `CambridgeSpeakingImportService(..., string? baseFolder = null)` —
  достаточно прокинуть `IeltsContentRoot/Speaking` в DI; код самого сервиса почти не трогаем.
- **Регистрации seed-сервисов:** Reading/Listening/Writing — **Singleton**; `SeedService` — **Scoped**;
  `CambridgeSpeakingImportService` — **Singleton**. `IContentStore` будет Singleton — совместимо. Импорт
  в рантайме (ре-сидинг) делать через `IServiceScopeFactory.CreateScope()` (как уже делает
  `App.OnStartup`).
- **БД-контексты:** `IeltsDbContext` — через `IDbContextFactory` (Reading/Listening/Writing/Speaking);
  `DictionaryDbContext` — scoped; `ReadingDbContext` (study-модуль) — отдельная фабрика. Импорт IELTS
  трогает только `IeltsDbContext` и `DictionaryDbContext`. Mock контента не сидит (собирает из L/R/W/S
  в рантайме) — ре-сидинг Mock не нужен.
- **Навигация:** модули регистрируются как `IModuleDescriptor` в `App.xaml.cs → RegisterModuleDescriptors`
  (≈ строки 204–297). Настройки открываются **не** как пункт навигации, а кнопкой
  `OpenSettingsCommand` в `Shell/ShellView.xaml` (≈ стр. 98–104) → модальное `SettingsWindow`.
- **Переключение хаб/экран:** у хабов есть `CurrentScreen` (`[ObservableProperty]`) + computed
  `IsHubVisible => CurrentScreen is null`; вид `ReadingModuleView.xaml` показывает `ReadingHubView`
  по `IsHubVisible` и `ContentControl Content="{Binding CurrentScreen}"`. Это точка вставки баннера.
- **Прогресс по байтам:** `AudioCacheService.GetOrFetchAsync` (Dictionary/Audio) использует
  `IProgress<string>` (только текст). Готового байт-прогресса нет — заводим свой `IProgress<ImportProgress>`
  с долей `0..1`, биндим `ProgressBar` (стиль `FlatProgressBar`, `Themes/Controls.xaml`).
- **Стили для UI:** `Card`, `AccentButton`, `FlatButton`, `FlatProgressBar`, типографика
  (`H1/H2/H3/MutedText`), кисти (`StrongTextBrush/MutedTextBrush/SuccessBrush/OverlayDark20Brush`) — в
  `App/Themes/Controls.xaml` и `App/Themes/Palettes/DarkBlue.xaml`. Базовый класс окна —
  `App/Shell/ChromedWindow.cs`; эталон модалки с прогрессом — `Views/Writing/AiProcessingWindow.xaml`.
- **Настройки:** `IAppSettings`/`AppSettings` (Dictionary/Images), персист в
  `%AppData%\EnglishStudio\settings.json`, патч через `SettingsUpdate` (есть `Optional<T>`). Можно
  добавить `LastContentPackPath` (опционально).
- **`.gitignore`** уже игнорирует весь копирайт-контент; AWL/AVL — не игнорятся. Контент **есть на диске**
  (reading json ~2 МБ, 96 mp3 в Listening/Audio, writing/dictionary seeds) — Фаза C может собрать pack.

---

## 2. Фаза 0 — Общий контракт (ДЕЛАЕТСЯ ПЕРВОЙ, один общий коммит)

> Цель: зафиксировать типы, на которые оба агента ссылаются, чтобы дальше работать без блокировок.
> Делает один из агентов (рекомендуем A) или совместно; мёрджится до развилки веток.

### 2.1 Файлы (все в `src/EnglishStudio.Modules.Dictionary/Content/`)

**`ContentSection.cs`**
```csharp
namespace EnglishStudio.Modules.Dictionary.Content;

public enum ContentSection
{
    DictionaryOxford,
    DictionaryPhave,
    Reading,
    Listening,
    Writing,
    Speaking,
}
```

**`ContentManifest.cs`**
```csharp
namespace EnglishStudio.Modules.Dictionary.Content;

/// <summary>Содержимое manifest.json в корне content-pack.</summary>
public sealed record ContentManifest
{
    public int PackVersion { get; init; } = 1;
    public string CreatedAt { get; init; } = "";
    /// <summary>Ключи: "dictionaryOxford","dictionaryPhave","reading","listening","writing","speaking".</summary>
    public Dictionary<string, bool> Sections { get; init; } = new();

    public bool Has(ContentSection s) => Sections.TryGetValue(KeyOf(s), out var v) && v;

    public static string KeyOf(ContentSection s) => s switch
    {
        ContentSection.DictionaryOxford => "dictionaryOxford",
        ContentSection.DictionaryPhave  => "dictionaryPhave",
        ContentSection.Reading          => "reading",
        ContentSection.Listening        => "listening",
        ContentSection.Writing          => "writing",
        ContentSection.Speaking         => "speaking",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}
```

**`IContentStore.cs`**
```csharp
using System.IO;

namespace EnglishStudio.Modules.Dictionary.Content;

public interface IContentStore
{
    /// <summary>%AppData%\EnglishStudio\IeltsContent.</summary>
    string ContentRoot { get; }

    /// <summary>Корень модуля: ContentRoot/&lt;moduleFolder&gt; (напр. "Reading").</summary>
    string ModuleRoot(string moduleFolder);

    /// <summary>Импортирована ли секция (ключевой JSON / .txt на диске).</summary>
    bool IsImported(ContentSection section);

    /// <summary>Манифест из ContentRoot/manifest.json (null, если нет).</summary>
    ContentManifest? ReadManifest();

    /// <summary>Открыть JSON ContentRoot/&lt;moduleFolder&gt;/&lt;fileName&gt; или null, если файла нет.</summary>
    Stream? OpenJson(string moduleFolder, string fileName);

    /// <summary>Абсолютный путь ContentRoot/&lt;moduleFolder&gt;/&lt;code&gt;/&lt;relative&gt; (или null, если нет).</summary>
    string? ResolveFile(string moduleFolder, string code, string relative);
}
```

**`ImportProgress.cs`**
```csharp
namespace EnglishStudio.Modules.Dictionary.Content;

/// <summary>Прогресс импорта для биндинга в ProgressBar.</summary>
public readonly record struct ImportProgress(
    string Stage,        // "validate" | "copy" | "seed:Reading" | ...
    long BytesDone,
    long BytesTotal,
    double Fraction,     // 0..1 для ProgressBar.Value (Maximum=1)
    string? CurrentFile);
```

**`ImportResult.cs`**
```csharp
namespace EnglishStudio.Modules.Dictionary.Content;

public sealed record ImportedSection(ContentSection Section, int ItemCount, bool Reseeded);

public sealed record ImportResult(
    bool Success,
    IReadOnlyList<ImportedSection> Sections,
    IReadOnlyList<string> Errors);
```

**`IContentImportService.cs`**
```csharp
using System.Threading;
using System.Threading.Tasks;

namespace EnglishStudio.Modules.Dictionary.Content;

public interface IContentImportService
{
    /// <summary>Прочитать manifest из папки ИЛИ .zip без импорта (для валидации/превью).</summary>
    ContentManifest? PeekManifest(string packPathFolderOrZip);

    /// <summary>Импортировать pack (папка или .zip) в ContentRoot и пере-сидить затронутые модули.</summary>
    Task<ImportResult> ImportAsync(
        string packPathFolderOrZip,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
```

### 2.2 Правки `DictionaryPaths.cs`

См. §1.3 — добавить `AppDataRootOverride`, `IeltsContentFolderName`, `IeltsContentRoot`; в
`EnsureDirectoriesExist()` добавить `Directory.CreateDirectory(IeltsContentRoot);`.

### 2.3 Реализация `FileSystemContentStore` (тоже Фаза 0, чтобы DI собирался)

**`src/EnglishStudio.Modules.Dictionary/Content/FileSystemContentStore.cs`**
```csharp
using System.IO;
using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;

namespace EnglishStudio.Modules.Dictionary.Content;

public sealed class FileSystemContentStore : IContentStore
{
    public string ContentRoot => DictionaryPaths.IeltsContentRoot;
    public string ModuleRoot(string moduleFolder) => Path.Combine(ContentRoot, moduleFolder);

    public bool IsImported(ContentSection section) => section switch
    {
        ContentSection.DictionaryOxford => File.Exists(Path.Combine(ContentRoot, "Dictionary", "oxford_5000.json")),
        ContentSection.DictionaryPhave  => File.Exists(Path.Combine(ContentRoot, "Dictionary", "phave.json")),
        ContentSection.Reading          => File.Exists(Path.Combine(ContentRoot, "Reading", "ielts_reading_tests.json")),
        ContentSection.Listening        => File.Exists(Path.Combine(ContentRoot, "Listening", "ielts_listening_tests.json")),
        ContentSection.Writing          => File.Exists(Path.Combine(ContentRoot, "Writing", "writing_tests.json")),
        ContentSection.Speaking         => Directory.Exists(Path.Combine(ContentRoot, "Speaking"))
                                           && Directory.EnumerateFiles(Path.Combine(ContentRoot, "Speaking"), "*.txt", SearchOption.AllDirectories).Any(),
        _ => false,
    };

    public ContentManifest? ReadManifest()
    {
        var p = Path.Combine(ContentRoot, "manifest.json");
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(p),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public Stream? OpenJson(string moduleFolder, string fileName)
    {
        var p = Path.Combine(ContentRoot, moduleFolder, fileName);
        return File.Exists(p) ? File.OpenRead(p) : null;
    }

    public string? ResolveFile(string moduleFolder, string code, string relative)
    {
        var p = Path.Combine(ContentRoot, moduleFolder, code, relative);
        return File.Exists(p) ? p : null;
    }
}
```

### 2.4 DI-регистрация контракта

В `DictionaryServiceCollectionExtensions.AddDictionaryModule` добавить:
```csharp
services.AddSingleton<IContentStore, FileSystemContentStore>();
```
(Регистрация `IContentImportService` придёт от агента A вместе с проектом `EnglishStudio.Content` — см. §5.5.)

### 2.5 Acceptance Фазы 0
- Решение собирается (`dotnet build EnglishStudio.slnx`).
- `IContentStore` резолвится в DI; `IsImported` возвращает корректные значения на пустом/непустом
  `IeltsContent`.
- Контракт-типы видны из App (для агента B) — проверить тривиальным использованием в любом VM (можно
  временно).

---

## 3. Разделение труда и границы файлов (антиконфликт)

| Зона | Агент A (Backend) | Агент B (UI) |
|---|---|---|
| `Modules.Dictionary` | `Content/*` (после Фазы 0), `Seed/SeedService.cs`, `Seed/SeedManifest.cs`, `Data/DictionaryPaths.cs`, `.csproj` | — |
| `Modules.Ielts.{Reading,Listening,Writing}` | `Seed/*SeedService.cs`, `.csproj` | — |
| `Modules.Ielts.Speaking` | `Cambridge/CambridgeSpeakingImportService.cs`, `SpeakingServiceCollectionExtensions.cs` | — |
| **`EnglishStudio.Content`** (новый) | весь проект | — |
| `tools/ContentPackBuilder` (новый) | весь проект | — |
| `App/Views/Content/*`, `App/ViewModels/Content/*`, `App/Views/Controls/ContentMissingView.*` | — | весь |
| `App/ViewModels/**Hub*.cs`, `App/Views/**Hub*.xaml`, `DictionaryView*`, `ReadingModuleView.xaml` и т.п. | — | весь |
| `App/Views/Settings/SettingsWindow.xaml` + `ViewModels/SettingsViewModel.cs` | — | весь |
| `App.xaml.cs` (**общий файл — риск!**) | блок DI бэкенда + блок `OnStartup`-сидинга | блок DI вьюх/VM + `RegisterModuleDescriptors` |
| `EnglishStudio.slnx` (**общий**) | добавляет `EnglishStudio.Content`, `tools/ContentPackBuilder` | — |
| `tests/EnglishStudio.Integration.Tests` | тесты стора/сидинга/импорта | (опц.) тесты gating-логики VM |
| `docs/`, `README.md` | `docs/CONTENT_PACK.md` | раздел «How to add content» в README |

**Анти-конфликт по `App.xaml.cs`:**
- Это единственный реально общий файл кода. Чтобы развести правки, в Фазе 0 ввести **явные якоря-комменты**:
  ```
  // ── content-infra services (Agent A) ──
  // ── content-infra services END ──
  // ── view-models & windows (Agent B) ──
  ...
  ```
- Агент A правит только секцию A (вызов `services.AddContentModule();` + try/catch вокруг seed в
  `OnStartup`). Агент B — только секцию B (регистрация `ContentImportViewModel`/`ContentImportWindow` +
  при желании дескриптор). При слиянии конфликт локализован и тривиален.
- `EnglishStudio.slnx` правит только агент A.

---

## 4. Точки синхронизации

| SP | Что | Кто ждёт | Разблокировка |
|---|---|---|---|
| **SP0** | Фаза 0 (контракт) смёржена в feature-ветку | оба | оба создают свои под-ветки от неё |
| **SP1** | Агент A выложил `EnglishStudio.Content` с реальным `IContentImportService` + DI-регистрацию | агент B (заменить фейк на реальный сервис) | до SP1 агент B использует `FakeContentImportService` (локальный тест-дубль, имитирует прогресс/результат) |
| **SP2** | Оба влили A и B в feature-ветку | оба | переходят к §7 (Фаза I совместно) |

> До SP1 агент B полностью самодостаточен: VM зависит от `IContentImportService` (интерфейс из
> `Dictionary.Content`, доступен после SP0). Фейк отдаёт `ImportProgress` с растущей `Fraction` и
> финальный `ImportResult` — достаточно, чтобы довести UI/прогресс/сводку до конца.

---

## 5. Агент A — Backend (фазы B, C, D, G, H + backend-тесты)

Под-ветка: `feat/content-backend`.

### A1. Перевод seed-сервисов на файловый источник (Фаза B)

Паттерн один на Reading/Listening/Writing; Dictionary — отдельно.

**Общий приём:** в конструктор добавить `IContentStore contentStore` (сохранить в поле `_content`).
В начале `SeedIfMissingAsync` — мягкий гейт:
```csharp
if (!_content.IsImported(ContentSection.<Section>)) { _log.LogInformation("<Module>: контент не импортирован, пропуск."); return; }
```

#### A1.1 Reading — `Modules.Ielts.Reading/Seed/ReadingSeedService.cs`
- ctor: `(IDbContextFactory<IeltsDbContext> dbFactory, IContentStore content, ILogger<ReadingSeedService> log)`.
- `LoadEmbedded()` → `LoadFromContent()`:
  ```csharp
  private List<ReadingTestDto> LoadFromContent()
  {
      using var s = _content.OpenJson(ReadingContentFolder, ResourceFileName); // "Reading","ielts_reading_tests.json"
      if (s is null) return new();
      return JsonSerializer.Deserialize<List<ReadingTestDto>>(s,
          new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
  }
  ```
- **Удалить** `CopyImagesToAppData(...)` и его вызов (строка ≈59) — медиа уже в `IeltsContent/Reading/<code>/`
  после импорта. `ImageResourcePrefix` константу удалить (больше не нужна).
- **Оставить** `ResolveImageAbsolutePath`/`GetTestContentRoot` (пути уже верные).
- Добавить гейт `IsImported(ContentSection.Reading)` в начало `SeedIfMissingAsync`.
- Удалить `using System.Reflection;` если стал не нужен.

#### A1.2 Listening — `Modules.Ielts.Listening/Seed/ListeningSeedService.cs`
- ctor: добавить `IContentStore content`.
- `LoadEmbedded()` → читать из `OpenJson("Listening","ielts_listening_tests.json")`; нет → пустой список.
- **Удалить** `CopyAssetsToAppData` + `ExtractResource` + их вызовы; константы
  `Image/Audio/TranscriptResourcePrefix` удалить.
- `LoadTranscript(code)` → читать из файла:
  ```csharp
  var path = _content.ResolveFile("Listening", code, "transcript.txt");
  if (path is null) return /* пустой набор частей */;
  var text = File.ReadAllText(path);
  // далее тот же разбор по PartMarker
  ```
- Гейт `IsImported(ContentSection.Listening)`.

#### A1.3 Writing — `Modules.Ielts.Writing/Seed/WritingSeedService.cs`
- ctor: добавить `IContentStore content`.
- `LoadEmbedded()` → `OpenJson("Writing","writing_tests.json")`; нет → пустой.
- **Удалить** `ExtractImage` + вызовы (строки ≈76–77); `ImageResourcePrefix` удалить.
- **Оставить** `ResolveImageAbsolutePath`/`GetContentRoot`.
- Гейт `IsImported(ContentSection.Writing)`.

#### A1.4 Dictionary — `Modules.Dictionary/Seed/SeedService.cs` + `SeedManifest.cs`
- `SeedService` ctor: `(DictionaryDbContext db, IContentStore content, ILogger<SeedService> logger)`.
- `SeedOxford5000Async` и `BackfillAudioPathsAsync`: заменить `SeedManifest.OpenOxford5000()` на
  `_content.OpenJson("Dictionary","oxford_5000.json")`; если `null` — `_logger.LogInformation(...)` и
  `return` (мягкий выход, без исключения). Десериализация в `OxfordSeedDocument` — без изменений.
- `SeedPhaveIfEmptyAsync`: `_content.OpenJson("Dictionary","phave.json")`; null → мягкий выход.
- `SeedAwlIfEmptyAsync`/`SeedAvlIfEmptyAsync`: **без изменений** (остаются `SeedManifest.OpenAwl()/OpenAvl()`, CC0, embedded).
- `SeedManifest.cs`: **удалить** `Oxford5000ResourceName`, `PhaveResourceName`, `OpenOxford5000()`,
  `OpenPhave()`. **Оставить** Awl/Avl + `Open(...)` + `ListEmbeddedResources()`.
  > ВАЖНО: после A2 embedded-ресурсов oxford/phave не будет — оставшиеся ссылки на них сломают сборку.
  > Поэтому удаление методов и правка `.csproj` (A2) — в одном коммите.

#### A1.5 Тонкость со Scoped `SeedService`
`SeedService` — Scoped, `IContentStore` — Singleton: инъекция Singleton в Scoped допустима. ОК.

### A2. Чистка `.csproj` от копирайт-`EmbeddedResource` (Фаза B, тот же коммит, что A1.4)

- `Modules.Dictionary.csproj`: убрать `<EmbeddedResource Include="Seed\oxford_5000.json" />` и
  `Seed\phave.json`. **Оставить** `awl.json`, `avl.json`. (DotNetZip/System.Drawing override — не трогать.)
- `Modules.Ielts.Reading.csproj`: убрать `Seed\ielts_reading_tests.json` и `Seed\Images\*.*`.
- `Modules.Ielts.Listening.csproj`: убрать `ielts_listening_tests.json`, `Seed\Images\*.*`,
  `Seed\Audio\*.*`, `Seed\Transcripts\*.*`.
- `Modules.Ielts.Writing.csproj`: убрать `writing_tests.json`, `Seed\Images\*.*`.
- **Проверка:** `dotnet build` проходит даже если эти файлы удалить с диска (имитация чистого клона —
  временно переименуй папки `Seed/Images` и т.п., собери, верни обратно).

### A3. Унификация Speaking (Фаза H)

- В `SpeakingServiceCollectionExtensions.cs` зарегистрировать `CambridgeSpeakingImportService` с
  `baseFolder = DictionaryPaths.IeltsContentRoot + "/Speaking"`:
  ```csharp
  services.AddSingleton(sp => new CambridgeSpeakingImportService(
      sp.GetRequiredService<IDbContextFactory<IeltsDbContext>>(),
      sp.GetRequiredService<CambridgeSpeakingTestParser>(),
      sp.GetRequiredService<ILogger<CambridgeSpeakingImportService>>(),
      baseFolder: Path.Combine(DictionaryPaths.IeltsContentRoot, "Speaking")));
  ```
- В `CambridgeSpeakingImportService`: сменить раскладку поиска. Сейчас
  `{base}\Ielts {book}\Speaking\Test№{t}\Test№{t}.txt`. **Целевая раскладка pack:**
  `Speaking/Ielts {book}/Test№{t}.txt`. Поправить `ParseAvailableTests`:
  ```csharp
  var file = Path.Combine(_baseFolder, $"Ielts {book}", $"Test№{t}.txt");
  ```
  (убрать промежуточный сегмент `Speaking\Test№{t}\`). `DefaultRelativeBase` const удалить/занулить —
  персональный путь больше не дефолт.
- `ImportIfPossibleAsync` уже мягко выходит, если `_baseFolder` не существует → стартовая безопасность ОК.

### A4. `tools/ContentPackBuilder` (Фаза C)

Новый консольный проект `tools/ContentPackBuilder/EnglishStudio.ContentPackBuilder.csproj` (net10.0,
`<OutputType>Exe</OutputType>`). Добавить в `EnglishStudio.slnx` (папка `/tools/`). Ссылается на
Reading/Listening/Writing модули (чтобы переиспользовать DTO и константы имён ресурсов) **или** содержит
минимальные собственные DTO (только `code` + поля медиа). Рекомендуем ссылаться на модули для точности.

**Аргументы CLI:** `--seed-root <repo/src>` (по умолчанию вычислить относительно exe),
`--out <папка>` (по умолчанию `./EnglishStudio-Content`), `--speaking-src <путь к Telegram-папке>`
(для копирования .txt).

**Логика (зеркалит то, что делали `Copy*ToAppData`):**
- **Dictionary:** скопировать `Modules.Dictionary/Seed/oxford_5000.json`, `…/phave.json` → `out/Dictionary/`.
- **Reading:** скопировать `…/Seed/ielts_reading_tests.json` → `out/Reading/`. Распарсить JSON → для
  каждого теста `code` и каждого относительного пути картинки (`group.imagePath`): источник
  `…/Seed/Images/<code>.<rel>`, цель `out/Reading/<code>/<rel>`.
- **Listening:** json → `out/Listening/`. Для каждого `code`: каждый `part.audioFile` из
  `…/Seed/Audio/<code>.<audioFile>` → `out/Listening/<code>/<audioFile>`; картинки из `…/Seed/Images/` →
  `out/Listening/<code>/<rel>`; транскрипт `…/Seed/Transcripts/<code>.transcript.txt` →
  `out/Listening/<code>/transcript.txt`.
- **Writing:** json → `out/Writing/`. Для каждого `code`: `task1.imageFile`, `task2.imageFile` из
  `…/Seed/Images/<code>.<imageFile>` → `out/Writing/<code>/<imageFile>`.
- **Speaking:** из `--speaking-src` скопировать `Ielts {book}\Speaking\Test№{t}\Test№{t}.txt` →
  `out/Speaking/Ielts {book}/Test№{t}.txt` (целевая раскладка из A3).
- **manifest.json:** `packVersion=1`, `createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd")`, `sections` —
  `true` для модулей, где что-то реально скопировано.

**Замечание про разбор имён:** имя файла на диске = `<code>.<rel>` (часть после embedded-префикса).
Поскольку PackBuilder знает `code` и `rel` из JSON, он строит источник как `Seed/Images/<code>.<rel>`
напрямую — не нужно угадывать границу code/rel.

Результат — твой приватный pack (раздаёшь пользователям отдельно, в git не кладёшь).

### A5. `EnglishStudio.Content` + `ContentImportService` (Фаза D)

#### A5.1 Новый проект
`src/EnglishStudio.Content/EnglishStudio.Content.csproj` (net10.0, class library). ProjectReference на
Dictionary, Ielts.Reading, Ielts.Listening, Ielts.Writing, Ielts.Speaking. Добавить в `.slnx` (папка `/src/`).
`System.IO.Compression` (входит в net10) для zip — без доп. пакетов.

#### A5.2 `ContentImportService : IContentImportService`
ctor: `(IContentStore content, IServiceScopeFactory scopeFactory, ILogger<ContentImportService> log)`.

`PeekManifest(path)`:
- если `path` оканчивается на `.zip` → открыть архив, найти `manifest.json` (в корне или в единственной
  верхней папке), десериализовать `ContentManifest`;
- иначе → прочитать `<path>/manifest.json`.

`ImportAsync(path, progress, ct)`:
1. **Подготовить источник:** если `.zip` → распаковать во временную папку
   (`Path.Combine(Path.GetTempPath(), "es-import-" + Guid)`), `ZipFile.ExtractToDirectory`; в finally —
   удалить temp. Если в архиве одна корневая папка (`EnglishStudio-Content/`) — взять её как
   `packFolder`.
2. **Валидация:** `manifest.json` обязателен; для каждой секции `manifest.Sections[k]==true` проверить
   наличие ключевого JSON/.txt. Ошибки собирать в список; если нет манифеста → вернуть
   `ImportResult(Success:false, …)` сразу.
3. **Подсчёт байт:** просуммировать размеры всех файлов pack для `BytesTotal`.
4. **Копирование** `packFolder/*` → `IeltsContentRoot` (рекурсивно, с перезаписью), отправляя
   `progress.Report(new ImportProgress("copy", done, total, done/(double)total, relPath))` по каждому файлу.
5. **Ре-сидинг в новом scope:**
   ```csharp
   using var scope = _scopeFactory.CreateScope();
   var sp = scope.ServiceProvider;
   progress?.Report(new("seed:Reading", total, total, 1, null));
   await sp.GetRequiredService<ReadingSeedService>().SeedIfMissingAsync(ct);
   await sp.GetRequiredService<ListeningSeedService>().SeedIfMissingAsync(ct);
   await sp.GetRequiredService<WritingSeedService>().SeedIfMissingAsync(ct);
   await sp.GetRequiredService<CambridgeSpeakingImportService>().ImportIfPossibleAsync(ct);
   var seed = sp.GetRequiredService<SeedService>();
   await seed.SeedIfEmptyAsync(ct);
   await seed.SeedPhaveIfEmptyAsync(ct);
   await seed.BackfillAudioPathsAsync(ct);
   ```
   (Все идемпотентны: `SeedIfMissing`/`SeedIfEmpty`.)
6. **Сводка:** собрать `ImportedSection` по секциям, присутствующим в манифесте (счётчики можно взять из
   манифеста/числа файлов либо запросить из БД), `Success = errors.Count == 0`.

#### A5.3 DI-расширение
`EnglishStudio.Content/ContentServiceCollectionExtensions.cs`:
```csharp
public static IServiceCollection AddContentModule(this IServiceCollection services)
{
    services.AddSingleton<IContentImportService, ContentImportService>();
    return services;
}
```
В `App.xaml.cs` (секция A) добавить `.AddContentModule()` в цепочку модулей.

### A6. Стартовая безопасность (Фаза G)

В `App.xaml.cs.OnStartup`, блок сидинга (≈ строки 178–197):
- Миграции БД (`MigrateAsync`) **всегда** — не зависят от контента (без изменений).
- Каждый вызов сидинга обернуть в try/catch с логированием, чтобы сбой одной секции не валил старт:
  ```csharp
  try { await readingSeed.SeedIfMissingAsync(); } catch (Exception ex) { logger.LogError(ex, "Reading seed failed"); }
  ```
  (или один общий try/catch вокруг блока). AWL/AVL сидятся всегда (embedded).
- Проверить, что `DictionaryPaths.EnsureDirectoriesExist()` вызывается до сидинга (он создаёт
  `IeltsContentRoot`).

### A7. Backend-тесты (часть Фазы I, владелец — A)

В `tests/EnglishStudio.Integration.Tests`:
- Добавить ProjectReference на `EnglishStudio.Content` (и он притянет модули).
- `Infrastructure/ContentIoCollection.cs`: `[CollectionDefinition("ContentIO", DisableParallelization = true)]`.
- `Content/ContentStoreTests.cs`: на temp-override — `IsImported` == false на пустом; кладём
  `Reading/ielts_reading_tests.json` → true; `ReadManifest` парсит.
- `Content/SeedFromFileTests.cs`: положить мини-`ielts_reading_tests.json` (1 тест) в
  `tempRoot/IeltsContent/Reading/`, выполнить `ReadingSeedService.SeedIfMissingAsync` (на Sqlite
  in-memory `IeltsDbContext`), проверить запись в БД; затем без файла — повторный no-op без исключения.
- `Content/ContentImportServiceTests.cs`: собрать мини-pack (папка + zip) из 1 теста на секцию во
  временной папке, вызвать `ImportAsync`, проверить: файлы скопированы в `IeltsContentRoot`, БД засидена,
  `ImportResult.Success`, идемпотентность (второй прогон не дублирует).
- Все — в `[Collection("ContentIO")]`, в Dispose сбрасывать `AppDataRootOverride = null` и чистить temp.

### A8. Acceptance агента A
- Чистый клон (без копирайт-файлов на диске) собирается: `dotnet build EnglishStudio.slnx`.
- На пустом `IeltsContent` старт не падает; все IELTS-сидеры мягко выходят.
- `ContentPackBuilder` собирает валидный pack из текущего `Seed/`.
- `ContentImportService.ImportAsync` (папка и zip) наполняет `IeltsContent` и сидит БД; идемпотентен.
- Backend-тесты зелёные.

---

## 6. Агент B — UI (фазы E, F + точки входа)

Под-ветка: `feat/content-ui`. До SP1 — против `IContentImportService` + локального `FakeContentImportService`.

### B0. Локальный фейк (только в App, временно, удалить после SP1)
`App/Content/FakeContentImportService.cs : IContentImportService` — `PeekManifest` возвращает фиктивный
манифест; `ImportAsync` шлёт серию `ImportProgress` с растущей `Fraction` (через `Task.Delay`) и
возвращает успешный `ImportResult`. Зарегистрировать временно в секции B DI.

### B1. UI импортёра (Фаза E)

**`App/ViewModels/Content/ContentImportViewModel.cs`** (transient):
- deps: `IContentImportService import`, `IServiceProvider services` (или фабрика окна), опц. `IAppSettings`.
- свойства (`[ObservableProperty]`): `string? SelectedPath`, `double Progress` (0..1), `string StatusText`,
  `bool IsImporting`, `ObservableCollection<string> Summary`, `ObservableCollection<string> Errors`,
  `bool IsDone`.
- команды:
  - `ChooseFolderCommand` → `OpenFolderDialog` (Microsoft.Win32, .NET 8+/WPF) → `SelectedPath`;
    `PeekManifest` для предпросмотра секций.
  - `ChooseZipCommand` → `OpenFileDialog{ Filter = "Content pack (*.zip)|*.zip" }` → `SelectedPath`.
  - `ImportCommand` (async, `CanExecute = SelectedPath != null && !IsImporting`):
    ```csharp
    var progress = new Progress<ImportProgress>(p => { Progress = p.Fraction; StatusText = $"{p.Stage} {p.CurrentFile}"; });
    IsImporting = true;
    var result = await _import.ImportAsync(SelectedPath!, progress);
    // заполнить Summary/Errors из result; IsImporting=false; IsDone=true;
    ```
    `Progress<T>` создаётся на UI-потоке → колбэк маршалится в UI автоматически.
  - `DoneCommand` → закрыть окно; поднять событие/Messenger «контент импортирован» (хабы перечитают).
- Прогресс по байтам: `ProgressBar Maximum="1" Value="{Binding Progress}"` (стиль `FlatProgressBar`).

**`App/Views/Content/ContentImportWindow.xaml(.cs)`** — `ChromedWindow`, `ResizeMode=NoResize`,
`WindowStartupLocation=CenterOwner`, `ShowInTaskbar=False`. Композиция как `AiProcessingWindow.xaml`
(градиент/Card). Содержит: кнопки «Выбрать папку…» / «Выбрать ZIP» (`FlatButton`), путь, список
найденных секций (из `PeekManifest`), `ProgressBar` (`FlatProgressBar`), `StatusText` (`MutedText`),
сводку/ошибки, кнопку «Импортировать» (`AccentButton`) и «Готово».

**Открытие окна** — паттерн как `WritingHubViewModel` открывает `AiProcessingWindow`
(`WritingHubViewModel.cs` ≈ 276–306): резолв окна/VM из `IServiceProvider`, `Owner = ...`, `ShowDialog()`.
Завести единый хелпер `ContentImportLauncher` (App), чтобы и Settings, и баннер открывали окно одинаково.

**DI (секция B):** `ContentImportViewModel` (transient), `ContentImportWindow` (transient).

### B2. Заглушка «контент не импортирован» (Фаза F)

**`App/Views/Controls/ContentMissingView.xaml(.cs)`** — `UserControl`, переиспользуемый баннер:
- разметка: `Border Style="{StaticResource Card}"` + 🔒 + заголовок «Контент раздела не импортирован» +
  описание (`MutedText`) + кнопка «Импортировать контент» (`AccentButton`).
- DependencyProperty: `public ICommand ImportCommand` и `public string MessageText` — чтобы хаб задавал
  свой текст (для Mock — «не хватает: …») и привязывал свою команду открытия импортёра.

### B3. Гейтинг в хабах (Фаза F)

В каждый Hub-VM добавить:
- ctor-зависимость `IContentStore content` (Reading/Listening/Writing/Speaking/Mock/Dictionary VM).
- `[ObservableProperty] bool isContentMissing;` + `string contentMissingText;`
- `IRelayCommand OpenImportCommand` → открыть импортёр (через `ContentImportLauncher`), по закрытии —
  `await LoadAsync()`.
- в начале `LoadAsync()`:
  ```csharp
  IsContentMissing = !_content.IsImported(ContentSection.<Section>);
  if (IsContentMissing) { /* очистить списки; задать ContentMissingText; */ return; }
  ```

Маппинг секций: Reading→`Reading`, Listening→`Listening`, Writing→`Writing`, Speaking→`Speaking`.

**Словарь — Oxford блокирует, PHaVE — частичный контент (явное решение):**
- Полная блокировка `ContentMissingView` для словаря — **только** при `!IsImported(DictionaryOxford)`
  (Oxford 5000 — костяк словаря; без него AWL/AVL-каркас малополезен).
- **PHaVE — отдельный частичный контент, НЕ блокирует словарь.** Если Oxford есть, а PHaVE нет — словарь
  работает, просто без фразовых глаголов. Если оба импортированы — обе секции сидятся независимо
  (`SeedIfEmptyAsync` + `SeedPhaveIfEmptyAsync` уже вызываются раздельно в
  `src/EnglishStudio.App/App.xaml.cs:195`).
- **Сопутствующий фикс (в рамках работы B по словарю):** в
  `src/EnglishStudio.App/ViewModels/DictionaryViewModel.cs` (блок source-фильтров, стр. 167–173) сейчас
  НЕТ фильтра PHaVE, хотя `WordSource.Phave` существует. Добавить
  `SourceOptions.Add(new SourceFilterItem { Source = WordSource.Phave, Label = "Фразовые глаголы (PHaVE)" });`.
  Показывать этот фильтр всегда (при отсутствии контента список по нему просто пуст) либо условно по
  `IsImported(DictionaryPhave)` — на усмотрение B; рекомендуем добавлять всегда для простоты.
- Опционально: при `IsImported(DictionaryOxford) && !IsImported(DictionaryPhave)` — мягкая подсказка
  «Фразовые глаголы не импортированы» рядом с фильтром (не баннер-блокер).

**Mock:** `IsContentMissing = !(R&&L&&W&&S)`; текст перечисляет недостающие секции.

В каждый Hub-View (`ReadingHubView.xaml` и т.д. — точные пути в карте навигации) добавить оверлей:
```xml
<controls:ContentMissingView
    Visibility="{Binding IsContentMissing, Converter={StaticResource BoolToVis}}"
    ImportCommand="{Binding OpenImportCommand}"
    MessageText="{Binding ContentMissingText}" />
```
и скрывать обычный контент при `IsContentMissing` (через тот же конвертер с инверсией или
`DataTrigger`). `BooleanToVisibilityConverter` уже используется в `ReadingModuleView.xaml`.

Затронуть: Reading, Listening, Writing, Speaking, Mock хабы + `DictionaryView`/`DictionaryViewModel`.

### B4. Точка входа из Настроек (Фаза E)

- `SettingsWindow.xaml`: добавить секцию `📦 Контент` (Border `Card`) с кнопкой
  «Импортировать контент…» (`FlatButton`) перед строкой статуса сохранения (≈ стр. 174).
- `SettingsViewModel`: команда `OpenContentImportCommand` → `ContentImportLauncher`. Добавить нужную
  зависимость (`IServiceProvider`/лаунчер) в ctor.

### B5. Acceptance агента B
- Импортёр (папка и ZIP) открывается из Настроек и из баннера; прогресс по байтам идёт; сводка/ошибки
  показываются.
- Все IELTS-хабы и словарь при отсутствии секции показывают `ContentMissingView`; по кнопке
  открывается импортёр; после импорта (на реальном сервисе после SP1) хабы перечитывают и показывают
  контент.
- Mock показывает приглашение с перечнем недостающих секций.
- Сборка зелёная; запуск приложения без падений.

---

## 7. Совместная фаза I (после SP2)

1. **Замена фейка:** удалить `FakeContentImportService` и его регистрацию; убедиться, что DI
   биндит реальный `IContentImportService` из `EnglishStudio.Content` (`AddContentModule`).
2. **Интеграционный тест полного цикла:** мини-pack (по 1 тесту на секцию) → `ImportAsync` → проверка,
   что хаб-сервисы (`IReadingTestService.ListAsync` и т.д.) видят данные. (Владелец — A, но ревью B.)
3. **`docs/CONTENT_PACK.md`:** точная раскладка pack (§2 исходного плана + раздел Speaking
   `Speaking/Ielts {book}/Test№{t}.txt`), схема `manifest.json`, как собрать свой через
   `tools/ContentPackBuilder`.
4. **README:** раздел «How to add content» (скачать app → получить/собрать pack → импортёр).
5. **Проверка чистого клона** (критично):
   - Временно убрать копирайт-файлы с диска (или клонировать в чистую папку, где их нет).
   - `dotnet build EnglishStudio.slnx` — зелёная.
   - Запуск → все IELTS-разделы и Oxford-словарь показывают «Импортировать»; AWL/AVL + тренажёр
     работают; старт без падений.
   - Импорт собранного pack → всё наполняется; разделы/Mock работают.
   - `dotnet test` — зелёный (текущий baseline 100 тестов + новые).

---

## 8. Git-процесс и ведение истории

> Репозиторий ещё **не инициализирован** (`.git` отсутствует). Публикация на GitHub — конечная цель;
> аккаунт пока не создан (в `LICENSE` плейсхолдер `[Your Name]`). Реальные `git init`/`push`/создание
> репозитория и подстановку имени правообладателя делает пользователь (требует его GitHub-аккаунт).

### 8.1 Подготовка к первому коммиту (до начала работы агентов или сразу после Фазы 0)
1. `git init`, дефолтная ветка `main`.
2. **Аудит, что копирайт не попадёт в индекс** (рабочая среда — Windows/PowerShell):
   ```powershell
   git add -A
   # PowerShell (Select-String регистронезависим по умолчанию):
   git ls-files | Select-String -Pattern 'oxford_5000|phave|ielts_(reading|listening)_tests|writing_tests|\.mp3$|Seed/(Audio|Transcripts|Images)/'
   ```
   Альтернатива через ripgrep (если установлен):
   ```powershell
   git ls-files | rg -i 'oxford_5000|phave|ielts_(reading|listening)_tests|writing_tests|\.mp3$|Seed/(Audio|Transcripts|Images)/'
   ```
   Вывод должен быть **пустым** (всё перечисленное в `.gitignore`). AWL/AVL (`awl.json`,`avl.json`) —
   ожидаемо присутствуют (CC0). Эту же проверку добавить как ручной чек перед каждым релизом (§8.4).
3. Первый коммит текущего состояния (инфра уже есть: LICENSE/README/NOTICE/.gitignore).

### 8.2 Ветвление под двух агентов
```
main
 └─ feat/content-externalization        (интеграционная feature-ветка)
     ├─ feat/content-backend   (Агент A)
     └─ feat/content-ui        (Агент B)
```
- Фаза 0 коммитится в `feat/content-externalization` (или в `main` до развилки), затем обе под-ветки
  ответвляются от неё.
- Агенты регулярно ребейзят свои ветки на `feat/content-externalization` (особенно после правок
  `App.xaml.cs`/`.slnx`).
- Слияние: A → feature, затем B → feature (или наоборот); конфликт ожидается только в `App.xaml.cs`
  (локализован якорями §3) и `.slnx` (правит только A — конфликта нет).
- После Фазы I: feature → `main`.

### 8.3 Конвенции
- **Commit messages:** Conventional Commits — `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`.
  Один логический шаг = один коммит (напр. `refactor(reading): seed from IeltsContent instead of embedded`).
  Футер каждого коммита:
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```
- **Версионирование:** SemVer. После успешной проверки чистого клона — тег первой OSS-версии
  (напр. `v0.1.0`). Вести `CHANGELOG.md` (Keep a Changelog).
- **Версия pack:** независимая, через `manifest.packVersion`. Бампить при изменении раскладки pack;
  фиксировать в `docs/CONTENT_PACK.md`. Импортёр принимает `packVersion <= поддерживаемой`, при большей —
  предупреждает.

### 8.4 История дальше (проект развивается)
- Каждый новый модуль/фича — отдельная feature-ветка → PR → squash-merge в `main` → тег при релизе.
- Перед каждым релизом — повтор «проверки чистого клона» (§7.5), чтобы публичная сборка не ломалась.
- Копирайт-аудит (§8.1.2) добавить как ручной чек перед каждым релизом (в идеале — pre-commit hook,
  опциональный follow-up).

---

## 9. Расширяемость под будущие модули и версии

Чтобы добавить будущий модуль с защищённым контентом, нужно (чек-лист):
1. `ContentSection` — добавить значение; `ContentManifest.KeyOf` — ключ.
2. `FileSystemContentStore.IsImported` — кейс (ключевой файл секции).
3. Pack-раскладка: новая подпапка `<Module>/`; `ContentPackBuilder` — ветка копирования.
4. Seed-сервис нового модуля — конструктор с `IContentStore`, чтение через `OpenJson`, гейт
   `IsImported`, удаление любых embedded-копирований.
5. `ContentImportService` (ре-сидинг) — добавить вызов нового сидера в scope-блок.
6. Hub-VM/View — `IsContentMissing` + `ContentMissingView` (как §B3).
7. `manifest.packVersion` — бамп, если раскладка изменилась; запись в `docs/CONTENT_PACK.md`.

Манифест **forward-compatible**: неизвестные секции игнорируются, отсутствующие = не импортированы —
старое приложение спокойно читает новый pack (в пределах поддерживаемого `packVersion`).

---

## 10. Реестр файлов (создать / изменить)

> Все пути — от корня репозитория `C:\Users\tvore\source\repos\EnglishStudio\` (разделитель `/` для краткости).

**Фаза 0 (общая):**
- ➕ `src/EnglishStudio.Modules.Dictionary/Content/{ContentSection,ContentManifest,IContentStore,FileSystemContentStore,ImportProgress,ImportResult,IContentImportService}.cs`
- ✏ `src/EnglishStudio.Modules.Dictionary/Data/DictionaryPaths.cs` (override + IeltsContentRoot)
- ✏ `src/EnglishStudio.Modules.Dictionary/DictionaryServiceCollectionExtensions.cs` (регистрация `IContentStore`)

**Агент A:**
- ✏ `src/EnglishStudio.Modules.Ielts.Reading/Seed/ReadingSeedService.cs` + `src/EnglishStudio.Modules.Ielts.Reading/EnglishStudio.Modules.Ielts.Reading.csproj`
- ✏ `src/EnglishStudio.Modules.Ielts.Listening/Seed/ListeningSeedService.cs` + `src/EnglishStudio.Modules.Ielts.Listening/EnglishStudio.Modules.Ielts.Listening.csproj`
- ✏ `src/EnglishStudio.Modules.Ielts.Writing/Seed/WritingSeedService.cs` + `src/EnglishStudio.Modules.Ielts.Writing/EnglishStudio.Modules.Ielts.Writing.csproj`
- ✏ `src/EnglishStudio.Modules.Dictionary/Seed/SeedService.cs`, `src/EnglishStudio.Modules.Dictionary/Seed/SeedManifest.cs`, `src/EnglishStudio.Modules.Dictionary/EnglishStudio.Modules.Dictionary.csproj`
- ✏ `src/EnglishStudio.Modules.Ielts.Speaking/Cambridge/CambridgeSpeakingImportService.cs`, `src/EnglishStudio.Modules.Ielts.Speaking/SpeakingServiceCollectionExtensions.cs`
- ➕ `src/EnglishStudio.Content/EnglishStudio.Content.csproj` + `src/EnglishStudio.Content/ContentImportService.cs` + `src/EnglishStudio.Content/ContentServiceCollectionExtensions.cs`
- ➕ `tools/ContentPackBuilder/EnglishStudio.ContentPackBuilder.csproj` + `tools/ContentPackBuilder/Program.cs`
- ✏ `EnglishStudio.slnx` (новые проекты)
- ✏ `src/EnglishStudio.App/App.xaml.cs` (секция A: `.AddContentModule()`, try/catch в сидинге)
- ➕ `tests/EnglishStudio.Integration.Tests/Content/{ContentStoreTests,SeedFromFileTests,ContentImportServiceTests}.cs`, `tests/EnglishStudio.Integration.Tests/Infrastructure/ContentIoCollection.cs`; ✏ `tests/EnglishStudio.Integration.Tests/EnglishStudio.Integration.Tests.csproj`

**Агент B:**
- ➕ `src/EnglishStudio.App/ViewModels/Content/ContentImportViewModel.cs`
- ➕ `src/EnglishStudio.App/Views/Content/ContentImportWindow.xaml(.cs)`
- ➕ `src/EnglishStudio.App/Views/Controls/ContentMissingView.xaml(.cs)`
- ➕ `src/EnglishStudio.App/Content/FakeContentImportService.cs` (врем., удалить после SP1), `src/EnglishStudio.App/Content/ContentImportLauncher.cs`
- ✏ `src/EnglishStudio.App/ViewModels/Reading/ReadingHubViewModel.cs`, `…/ViewModels/Listening/ListeningHubViewModel.cs`, `…/ViewModels/Writing/WritingHubViewModel.cs`, `…/ViewModels/Speaking/SpeakingHubViewModel.cs`, `…/ViewModels/Mock/MockHubViewModel.cs`, `…/ViewModels/DictionaryViewModel.cs`
- ✏ `src/EnglishStudio.App/Views/Reading/ReadingHubView.xaml`, `…/Views/Listening/ListeningHubView.xaml`, `…/Views/Writing/WritingHubView.xaml`, `…/Views/Speaking/SpeakingHubView.xaml`, `…/Views/Mock/MockHubView.xaml`, `…/Views/DictionaryView.xaml` (точные имена сверить при работе)
- ✏ `src/EnglishStudio.App/Views/Settings/SettingsWindow.xaml`, `src/EnglishStudio.App/ViewModels/SettingsViewModel.cs`
- ✏ `src/EnglishStudio.App/App.xaml.cs` (секция B: регистрация VM/окна)

**Совместно (Фаза I):**
- ➕ `docs/CONTENT_PACK.md`, ✏ `README.md`, ➕ `CHANGELOG.md`
- ➕ `tests/EnglishStudio.Integration.Tests/Content/FullImportFlowTests.cs` (интеграционный тест полного импорта)

---

## 11. Acceptance (полный чек-лист)

- ✅ Свежий `git clone` собирается (`dotnet build EnglishStudio.slnx`) **без** копирайт-файлов.
- ✅ В индексе git нет ни одного файла Cambridge/Oxford/PHaVE (аудит §8.1.2 пуст).
- ✅ На чистой машине без pack приложение стартует, не падает; IELTS-разделы и Oxford-словарь показывают
  приглашение импортировать; AWL/AVL и тренажёр работают.
- ✅ Импортёр из папки И из ZIP наполняет `IeltsContent/` и сидит БД; 4 секции + Mock + Oxford-словарь
  становятся доступны.
- ✅ Повторный импорт идемпотентен.
- ✅ `dotnet test` зелёный (текущий baseline 100 тестов + новые тесты стора/сидинга/импорта).
- ✅ `tools/ContentPackBuilder` собирает валидный pack из текущего `Seed/`.
- ✅ `ContentMissingView` отображается во всех незаполненных разделах, кнопка открывает импортёр.
- ✅ Git: `main` + feature-ветки, Conventional Commits с Co-Authored-By, тег `v0.1.0`, `CHANGELOG.md`.

---

## 12. Риски и решения

| Риск / вопрос | Решение |
|---|---|
| Удаление embedded-методов `SeedManifest` ломает сборку до правки `.csproj` | A1.4 + A2 — один коммит |
| Глобальный `AppDataRootOverride` → гонки в параллельных тестах | xUnit-коллекция `ContentIO` с `DisableParallelization=true`; сброс в Dispose |
| `App` — WinExe, тяжело тестировать импорт | Импорт вынесен в `EnglishStudio.Content` (§1.2) |
| Конфликт правок `App.xaml.cs` между агентами | Якоря-секции §3; правки локализованы и тривиальны |
| `IeltsContent/` уже частично заполнен (извлечён из DLL ранее) | Очистить `IeltsContent/` перед первым прогоном нового пути; импортёр перезаписывает |
| Раскладка Speaking в pack vs текущий парсер | A3 фиксирует `Speaking/Ielts {book}/Test№{t}.txt` и правит `ParseAvailableTests` под неё; PackBuilder кладёт так же |
| Band-descriptor рубрики `Modules.Ai/Rubrics/*.md` — возможный копирайт IELTS | Проверить перед публикацией, что текст свой/перефразирован; иначе вынести аналогично |
| Размер pack ~760 МБ | Манифест посекционный → можно бить на под-pack'и (опц. follow-up) |
| Пользователь раздаёт pack публично | Вне зоны кода; README прямо указывает: контент — свой, легально полученный |
| `git init`/публикация требуют GitHub-аккаунта пользователя | Действие пользователя; подставить имя в `LICENSE` при создании репо |

---

## 13. Оценка по агентам

| Агент | Фазы | Время |
|---|---|---|
| Общая Фаза 0 | контракт + стор + DI | ~1.5 ч |
| Агент A | B (seed→файлы + csproj), C (PackBuilder), D (Content+Import), G (старт), H (Speaking), backend-тесты | ~8.5 ч |
| Агент B | E (UI импортёра), F (gating всех хабов + словарь), точки входа | ~6 ч |
| Совместно | Фаза I (интеграция, доки, чистый клон) | ~2 ч |
| **Итого (с распараллеливанием A‖B)** | | **~12 ч стенового времени** |
