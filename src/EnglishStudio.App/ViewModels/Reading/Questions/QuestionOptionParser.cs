using System.Text.Json;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

internal static class QuestionOptionParser
{
    /// <summary>Parse an OptionsJson array of strings. Returns an empty list on null/invalid input.</summary>
    public static List<string> ParseStringArray(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return new List<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(optionsJson);
            return arr ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
