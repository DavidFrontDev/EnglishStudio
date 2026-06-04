namespace EnglishStudio.Modules.Ielts.Core.Entities;

/// <summary>
/// Полноэкзаменационная mock-попытка: оркестрирует 4 дочерних attempt'а (Listening/Reading/Writing/
/// Speaking) и хранит итоговый overall band. Дочерние attempt'ы остаются собственностью своих модулей —
/// FK сюда nullable и при удалении дочернего обнуляются (SetNull), удаление mock на них не каскадит.
/// Состояние секций сериализуется в <see cref="SectionsJson"/> (4 фикс. строки, не нормализуем).
/// </summary>
public class MockAttempt
{
    public int Id { get; set; }

    /// <summary>MockMode.ToString() — "CambridgeBundle" / "RandomMix" / "Custom".</summary>
    public string ModeCode { get; set; } = "CambridgeBundle";

    public int? Book { get; set; }          // 15..20 (от Listening/Speaking бандла)
    public int? TestNumber { get; set; }    // 1..4

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>Текущая секция для resume после паузы/краша. Значение = (int)MockSection, null когда завершён.</summary>
    public int? CurrentSection { get; set; }

    // FK на дочерние attempt'ы (nullable, заполняются по мере старта секций).
    // Reading и Listening оба ссылаются на TestAttempt — две разных навигации по разным FK.
    public int? ListeningAttemptId { get; set; }
    public TestAttempt? ListeningAttempt { get; set; }
    public int? ReadingAttemptId { get; set; }
    public TestAttempt? ReadingAttempt { get; set; }
    public int? WritingAttemptId { get; set; }
    public WritingAttempt? WritingAttempt { get; set; }
    public int? SpeakingAttemptId { get; set; }
    public SpeakingAttempt? SpeakingAttempt { get; set; }

    /// <summary>
    /// Состояние 4 секций как JSON: [{section,status,startedAt,finishedAt,band}].
    /// Почему JSON, а не отдельная таблица: 4 фиксированные строки с простым состоянием на попытку,
    /// нормализовать не стоит — UI рендерит их инлайн.
    /// </summary>
    public string SectionsJson { get; set; } = "[]";

    // Кэш band'ов по секциям (чтобы history не джойнила дочерние таблицы).
    public double? ListeningBand { get; set; }
    public double? ReadingBand { get; set; }
    public double? WritingBand { get; set; }
    public double? SpeakingBand { get; set; }
    public double? OverallBand { get; set; }

    /// <summary>true, если засчитано меньше 4 секций (skipped) — overall по доступным.</summary>
    public bool IsPartial { get; set; }
}
