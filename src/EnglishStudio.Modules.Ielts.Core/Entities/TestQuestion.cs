namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class TestQuestion
{
    public int Id { get; set; }
    public int TestPartId { get; set; }
    public TestPart TestPart { get; set; } = null!;

    public int OrderInPart { get; set; }

    /// <summary>Optional grouping of consecutive questions (e.g. several questions sharing one summary box).</summary>
    public int? GroupId { get; set; }
    public TestQuestionGroup? Group { get; set; }

    public QuestionType Type { get; set; }
    public string Stem { get; set; } = string.Empty;

    /// <summary>JSON array of options for choices / matchings / form fields.</summary>
    public string? OptionsJson { get; set; }

    /// <summary>Canonical answer (string or JSON depending on type).</summary>
    public string AnswerKeyJson { get; set; } = string.Empty;

    /// <summary>Optional JSON array of synonyms / equivalent spellings accepted as correct.</summary>
    public string? AcceptableAnswersJson { get; set; }

    public int Points { get; set; } = 1;

    /// <summary>IELTS NMTW (No More Than N Words) limit for completion-type questions.</summary>
    public int? WordLimitMax { get; set; }
}
