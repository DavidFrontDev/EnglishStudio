# Модуль «Чтение» — блок F8 (длинные тексты / книги: пагинация) — параллельный план для двух ИИ-агентов

**Создано:** 2026-06-03. Продолжение [`Reading_Study_Module.md`](Reading_Study_Module.md). Доп-блок сверх F1–F7.
**Проблема:** окно чтения рендерит ВЕСЬ текст одним `FlowDocument` с `Run` на каждое слово (нужно для тускнения/заметок/перевода), а WPF инлайновые Run'ы НЕ виртуализирует → тексты-книги (сотни тыс. слов, напр. «Война и мир» ~580k) вешают/тормозят окно. Временно добавлено предупреждение при добавлении текста > `ReadingTokenizer.LargeTextWordThreshold` (20 000 слов).
**Цель F8:** рендерить **по одной странице** (окно из ~1500 слов или главы) с навигацией ← →, сохраняя глобальные индексы слов/смещения, чтобы заметки/закладки/read-along продолжали работать. Тогда даже книга открывается мгновенно.

---

## Что уже готово и переиспользуется

- `ReadingTokenizer.Tokenize` → `TextToken{Text,Kind,StartOffset,Length,WordIndex}` (быстро даже на книге — тормозит РЕНДЕР, не токенизация); `LargeTextWordThreshold = 20_000`.
- `ReaderWindow.BuildDocument` (App): строит `FlowDocument`, `_wordRuns` (global WordIndex→Run), `_wordSpans` (char-range на Run) — переключаем на построение СРЕЗА страницы.
- Read-along (`IReadAlongController`, Tokens→курсор), Whisper-анализ, тускнение (CursorChanged→WordIndex), заметки (F5, по char-offset/WordIndex), закладка (F5 `TextBookmark.WordIndex`) — адаптируем к «текущей странице».
- **Схема БД не меняется** — пагинация рантайм; «продолжить с места» = страница, содержащая F5-закладку.

> ⚠️ **НЕ ТРОГАТЬ** `Modules.Ielts.*`; контракты прошлых этапов — заморожены.

---

## Принцип параллельной работы

1. **ШАГ 0 (контракты)** — DTO `TextPage`/`PaginationOptions` + `IPaginationService`, заморозить.
2. **Fork:** Agent A — чистый пагинатор + тесты; Agent B — переключение окна чтения на постраничный рендер + навигация + адаптация фич.
3. **Развязка:** B пишет тривиальный `FakePaginationService` (режет по N слов без глав), ведёт UI на нём.
4. **Общий** `App.xaml.cs` правит **только B** (одна строка регистрации VM, если понадобится). Пагинатор — pure, регистрируется в `AddReadingStudyModule()` (A).

---

## Разделение прав (ownership)

| Файл | Владелец |
|---|---|
| `Modules.Reading/Services/PaginationModels.cs` (DTO) | ШАГ 0 |
| `Modules.Reading/Services/IPaginationService.cs` | ШАГ 0 |
| `Modules.Reading/Services/TextPaginator.cs` (pure) | **A** |
| `Modules.Reading/ReadingServiceCollectionExtensions.cs` (рег. пагинатора) | **A** |
| `App/ViewModels/ReadingStudy/FakePaginationService.cs` (врем.) | **B** |
| `App/ViewModels/ReadingStudy/ReaderViewModel.cs` (состояние страниц) | **B** |
| `App/Views/ReadingStudy/ReaderWindow.xaml(.cs)` (срез-рендер + навигация) | **B** |
| `App/App.xaml.cs` (если нужна доп. рег.) | **B** |

---

## ШАГ 0 — Контракты (делать ПЕРВЫМ, замораживаются)

`Modules.Reading/Services/PaginationModels.cs`:
```csharp
namespace EnglishStudio.Modules.Reading.Services;

public sealed record PaginationOptions(int TargetWordsPerPage = 1500, bool DetectChapters = true);

/// Одна страница: диапазоны в ГЛОБАЛЬНЫХ координатах текста (WordIndex и char-offset),
/// чтобы заметки/закладки/read-along продолжали маппиться. Heading — заголовок главы, если найден.
public sealed record TextPage(
    int Index, int StartWordIndex, int EndWordIndex,
    int StartCharOffset, int EndCharOffset, string? Heading);
```

`IPaginationService.cs`:
```csharp
public interface IPaginationService
{
    /// Делит токены на страницы (по главам, иначе по TargetWordsPerPage; границы — по абзацу/предложению,
    /// НЕ посреди предложения). Маленький текст → одна страница.
    IReadOnlyList<TextPage> Paginate(IReadOnlyList<TextToken> tokens, PaginationOptions? options = null);

    /// Индекс страницы, содержащей слово (для «продолжить с закладки»). −1 если не найдено.
    int PageOfWord(IReadOnlyList<TextPage> pages, int wordIndex);
}
```

После 2 файлов — `dotnet build` модуля. Fork.

---

## Agent A — пагинатор (pure)

`Modules.Reading/Services/TextPaginator.cs` (`IPaginationService`, без зависимостей, юнит-тестируемый):
- **Определение глав** (`DetectChapters`): по строкам-заголовкам — regex на «Chapter N / CHAPTER / Глава N / Part / Book N» и/или коротким ALL-CAPS/Title-строкам, окружённым пустыми. Каждая глава → начало страницы, `Heading` = текст заголовка.
- **Дробление по размеру:** глава (или весь текст без глав) длиннее `TargetWordsPerPage` → режется на под-страницы; **границы только по концу абзаца, иначе по концу предложения** (не рвать предложение). Целиться в TargetWordsPerPage ±пол-абзаца.
- Каждая `TextPage`: `StartWordIndex/EndWordIndex` (по `TextToken.WordIndex`), `StartCharOffset/EndCharOffset` (по `TextToken.StartOffset`+`Length`), `Heading?`. Страницы покрывают текст без дыр/нахлёста, по порядку.
- Маленький текст (≤ TargetWordsPerPage и без глав) → ровно одна страница (UX не меняется).
- `PageOfWord` — бинарный/линейный поиск по диапазонам.
- DI: `IPaginationService`→`TextPaginator` (singleton) в `AddReadingStudyModule()`. App.xaml.cs не трогать.

**Acceptance A (юнит-тесты):** текст с «Chapter…» → страница на главу + под-дробление длинных глав; текст без глав → ровные страницы по ~N слов с границами по предложениям; маленький текст → 1 страница; `PageOfWord` корректен на границах; покрытие без дыр.

---

## Agent B — постраничный рендер + навигация

### B0. Заглушка
`FakePaginationService` — режет строго по N слов (без глав), для отладки UI без A.

### ReaderViewModel
- Состояние: `IReadOnlyList<TextPage> Pages`, `[ObservableProperty] int currentPageIndex`, `bool HasMultiplePages`, `string PageLabel` («Глава …» / «Стр. X из Y»), команды `NextPage`/`PrevPage`/`GoToPage(index)`.
- `LoadAsync`: после токенизации — `Pages = _pagination.Paginate(Tokens, opts)`. Стартовая страница = страница F5-закладки (`PageOfWord`), иначе 0.
- Отдавать срез текущей страницы (диапазон токенов) для рендера и для read-along/shadowing.

### ReaderWindow (ключевое)
- **BuildDocument строит только срез текущей страницы** (токены в [StartWordIndex..EndWordIndex]), но **сохраняет ГЛОБАЛЬНЫЙ `WordIndex` в Run.Tag и глобальный char-offset в `_wordSpans`** → заметки/закладки/read-along маппятся как раньше. Пере-строение при смене страницы (дёшево — ~1500 слов).
- **Навигация:** ← / → + «Глава/Стр. X из Y» + переход (выпадающий список глав или номер). Клавиши PageUp/PageDown. Видна только если `HasMultiplePages` (маленькие тексты — без хрома).
- На смене страницы: rebuild среза, сброс тускнения (`_dimmedUpTo=0`), повторное наложение подсветки заметок текущей страницы (по offset-overlap), скролл вверх.
- **Заметки (F5):** фильтровать к текущей странице по offset-overlap; добавление — глобальные offset'ы (уже глобальные).
- **Закладка/продолжить:** «🔖» = текущее верхнее слово (глобальный WordIndex); «↩ Продолжить» открывает страницу `PageOfWord(bookmark)` и скроллит.
- **Read-along / shadowing — на ТЕКУЩЕЙ странице:** передавать в контроллер срез токенов страницы (книгу вслух целиком читать неуместно). **Маппинг курсора:** контроллер даёт курсор 0-based по списку, который ему передали (срез) → B мапит `page-local i → page.words[i].WordIndex (global) → _wordRuns[global]`. WPM/анализ — по странице/сессии.

### B. DI
`App.xaml.cs`: на разработке — `FakePaginationService`; на интеграции убрать (реальный из `AddReadingStudyModule`).

**Acceptance B (на Fake):** длинный текст открывается мгновенно постранично; ← → листают; «стр. X из Y»; заметки/закладка/«продолжить» работают на нужной странице; read-along тускнит слова текущей страницы; маленький текст выглядит как раньше (без навигации).

---

## Чекпоинты

1. **CP0** — контракты собираются → fork.
2. **CP1** — A: Acceptance A (тесты). B: Acceptance B (Fake). Оба билдятся.
3. **CP2** — B убирает Fake (реальный `TextPaginator` из модуля). Запуск: добавить большой текст (>20k, подтвердить предупреждение) → открывается постранично без зависания; главы определились; навигация/заметки/закладка/read-along на странице.
4. **CP3** — smoke: реальная книга-глава за главой; заметка на стр.5 → закрыть/открыть → «продолжить» открывает стр.5 с подсветкой; read-along на странице тускнит корректно.

---

## Риски / памятки

- **Глобальные vs page-local индексы** — главный риск. Run.Tag и `_wordSpans` хранить ГЛОБАЛЬНЫМИ; read-along-курсор приходит page-local → маппить через `page.words[i].WordIndex`. Перепутать = заметки/тускнение «съедут».
- **Границы страниц** — не рвать предложение; заголовок главы не отрывать от её текста.
- **Перестроение при листании** дёшево (только срез), но сбрасывать состояние тускнения и переналожить заметки.
- **После F8 — смягчить предупреждение:** для постранично-рендеримых текстов хана-лаг уходит; порог `LargeTextWordThreshold` можно поднять/сделать чисто информационным (или предупреждать только на экстремальных >200–300k). Решить на CP3.
- **Импорт книги как отдельные главы-тексты** — альтернатива (резать файл на N `ReadingText` при добавлении). НЕ основной путь (теряются единые заметки/прогресс по книге); оставлено на будущее, если внутренней пагинации мало.
- **Контракты заморожены** после ШАГ 0; схема БД не меняется (миграции нет).
