using System.Text.Json;
using EnglishStudio.Modules.Ielts.Reading.Seed;

namespace EnglishStudio.IeltsReadingGen;

/// <summary>
/// Local structural validation of the generated JSON. Does not call out to an LLM —
/// just enforces shape, ranges, enum membership, and field consistency.
/// </summary>
internal static class ReadingValidator
{
    public static ValidationResult Validate(string rawJson, string expectedCode)
    {
        ReadingTestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ReadingTestDto>(rawJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            return ValidationResult.Fail($"JSON parse error: {ex.Message}");
        }
        if (dto is null) return ValidationResult.Fail("Deserialised to null.");

        var issues = new List<string>();

        if (!string.Equals(dto.Code, expectedCode, StringComparison.OrdinalIgnoreCase))
            issues.Add($"code mismatch (got '{dto.Code}', expected '{expectedCode}')");

        if (string.IsNullOrWhiteSpace(dto.Title))
            issues.Add("title empty");

        if (!string.Equals(dto.Mode, "Academic", StringComparison.OrdinalIgnoreCase))
            issues.Add($"mode must be 'Academic' (got '{dto.Mode}')");

        if (dto.Parts is null || dto.Parts.Count != 3)
            issues.Add($"expected exactly 3 parts (got {dto.Parts?.Count ?? 0})");

        if (dto.Parts is not null)
        {
            foreach (var part in dto.Parts)
            {
                var prefix = $"part {part.Order}:";
                if (part.Order is < 1 or > 3) issues.Add($"{prefix} order out of range");
                if (string.IsNullOrWhiteSpace(part.Title)) issues.Add($"{prefix} title empty");
                if (string.IsNullOrWhiteSpace(part.Body)) issues.Add($"{prefix} body empty");

                var wordCount = CountWords(part.Body ?? string.Empty);
                if (wordCount is < 550 or > 1100)
                    issues.Add($"{prefix} body word count {wordCount} outside 550-1100 range");

                var partQuestions = part.Groups?.SelectMany(g => g.Questions ?? new List<ReadingQuestionDto>()).ToList() ?? new List<ReadingQuestionDto>();
                if (partQuestions.Count is < 12 or > 14)
                    issues.Add($"{prefix} expected 12-14 questions (got {partQuestions.Count})");

                foreach (var q in partQuestions)
                {
                    var qPrefix = $"{prefix} q{q.Order}:";
                    if (string.IsNullOrWhiteSpace(q.Stem)) issues.Add($"{qPrefix} stem empty");
                    if (!Enum.TryParse<EnglishStudio.Modules.Ielts.Core.Entities.QuestionType>(q.Type, ignoreCase: true, out _))
                        issues.Add($"{qPrefix} unknown type '{q.Type}'");
                    if (q.AnswerKey.ValueKind == JsonValueKind.Undefined || q.AnswerKey.ValueKind == JsonValueKind.Null)
                        issues.Add($"{qPrefix} answerKey missing");
                }
            }

            // Total questions must be in IELTS range (38-42, official is exactly 40 but allow ±2 for flexibility).
            var total = dto.Parts.Sum(p => p.Groups?.Sum(g => g.Questions?.Count ?? 0) ?? 0);
            if (total is < 38 or > 42)
                issues.Add($"total questions {total} outside 38-42 range");
        }

        return issues.Count == 0
            ? ValidationResult.Ok(dto)
            : ValidationResult.Fail(string.Join("; ", issues));
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}

internal readonly record struct ValidationResult(bool Success, string? Error, ReadingTestDto? Dto)
{
    public static ValidationResult Ok(ReadingTestDto dto) => new(true, null, dto);
    public static ValidationResult Fail(string error) => new(false, error, null);
}
