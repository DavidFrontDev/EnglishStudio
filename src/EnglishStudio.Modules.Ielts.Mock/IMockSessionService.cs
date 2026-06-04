namespace EnglishStudio.Modules.Ielts.Mock;

/// <summary>Четыре секции полного IELTS-экзамена, в порядке прохождения.</summary>
public enum MockSection { Listening = 1, Reading = 2, Writing = 3, Speaking = 4 }

/// <summary>Состояние отдельной секции внутри mock-попытки.</summary>
public enum MockSectionStatus { Pending = 0, InProgress = 1, Completed = 2, Skipped = 3, Failed = 4 }

public enum MockMode
{
    /// <summary>Все 4 секции из одного Cambridge-теста (full real-exam simulation).</summary>
    CambridgeBundle,
    /// <summary>L+S из одного Cambridge-теста, R+W подбираются отдельно (v1 default).</summary>
    RandomMix,
    /// <summary>Пользователь сам выбирает каждую секцию (advanced).</summary>
    Custom
}

/// <summary>
/// Доступный bundle для старта mock'а. Speaking Part2-банк — «якорь» (book/test парсятся из его кода),
/// Listening подбирается по совпадению book/test, Reading/Writing — ротацией (опция A плана).
/// </summary>
public sealed record MockBundleSummary(
    int Book,                  // 15..20
    int TestNumber,            // 1..4
    int? ListeningTestId,      // null если у Cambridge X Test Y нет Listening-теста в БД
    int? ReadingTestSetId,
    int? WritingTestSetId,
    int SpeakingPart2BankId,   // anchor — Part2 cue-card bank
    int AvailableSections);    // 1..4 — сколько секций реально импортировано

public sealed record MockAttemptSummary(
    int AttemptId,
    MockMode Mode,
    int? Book,
    int? TestNumber,
    DateTime StartedAt,
    DateTime? FinishedAt,
    MockSection? CurrentSection,
    double? OverallBand,
    double? ListeningBand,
    double? ReadingBand,
    double? WritingBand,
    double? SpeakingBand,
    bool IsPartial);

public sealed record MockAttemptDetail(
    MockAttemptSummary Summary,
    int? ListeningAttemptId,
    int? ReadingAttemptId,
    int? WritingAttemptId,
    int? SpeakingAttemptId,
    IReadOnlyList<MockSectionState> Sections);

public sealed record MockSectionState(
    MockSection Section,
    MockSectionStatus Status,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int? ChildAttemptId,
    int? SecondaryChildAttemptId,   // Writing Task2 attempt (другие секции — null)
    double? Band);

public interface IMockSessionService
{
    /// <summary>Cambridge bundles, по одному на (book, test). Для полностью заполненной БД — до 24.</summary>
    Task<IReadOnlyList<MockBundleSummary>> ListAvailableBundlesAsync(CancellationToken ct = default);

    /// <summary>Случайный bundle для «surprise me» UX.</summary>
    Task<MockBundleSummary?> PickRandomBundleAsync(CancellationToken ct = default);

    Task<int> StartAttemptAsync(MockMode mode, MockBundleSummary? bundle, CancellationToken ct = default);

    /// <summary>
    /// Помечает секцию как InProgress и делает её текущей (для resume). Дочерний attempt НЕ создаётся
    /// здесь: секционные VM (Reading/Listening/Writing/Speaking) стартуют его сами через свой штатный
    /// сервис. Метод возвращает <b>source-id для запуска</b> секции: testSetId для Listening/Reading/Writing,
    /// SpeakingPart2BankId для Speaking. UI ведёт секцию до конца, затем вызывает <see cref="CompleteSectionAsync"/>
    /// с реальным id созданного дочернего attempt. Идемпотентен (повторный вход не сбрасывает StartedAt).
    /// </summary>
    Task<int> BeginSectionAsync(int mockAttemptId, MockSection section, CancellationToken ct = default);

    /// <summary>
    /// Завершает секцию: линкует дочерний attempt, кэширует band, сдвигает <c>CurrentSection</c>.
    /// <paramref name="secondaryChildAttemptId"/> используется только Writing (Task2; Task1 — в
    /// <paramref name="childAttemptId"/>), чтобы разбор секции мог открыть обе задачи. Для остальных
    /// секций — null.
    /// </summary>
    Task CompleteSectionAsync(int mockAttemptId, MockSection section, int childAttemptId, double? band, int? secondaryChildAttemptId = null, CancellationToken ct = default);

    Task SkipSectionAsync(int mockAttemptId, MockSection section, string reason, CancellationToken ct = default);

    /// <summary>
    /// Финализирует mock: подтягивает band'ы из дочерних attempt'ов, считает overall через
    /// <c>OverallBandCalculator</c>, проставляет FinishedAt. Возвращает overall band.
    /// </summary>
    Task<double> FinaliseAsync(int mockAttemptId, CancellationToken ct = default);

    Task<MockAttemptDetail?> GetAsync(int mockAttemptId, CancellationToken ct = default);

    /// <summary>Незаконченный mock (FinishedAt == null) для Resume-баннера в Hub, либо null.</summary>
    Task<MockAttemptSummary?> FindResumableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MockAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default);

    Task DeleteAsync(int mockAttemptId, CancellationToken ct = default);

    Task<int> ClearHistoryAsync(CancellationToken ct = default);
}
