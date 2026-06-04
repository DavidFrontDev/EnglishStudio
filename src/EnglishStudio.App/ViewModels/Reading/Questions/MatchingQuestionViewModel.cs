using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

/// <summary>
/// All matching-style questions (Headings, Information, Features, Sentence Endings, Map/Diagram).
/// Each instance represents ONE item that maps to one option tag (A/B/C/D/...).
/// Options come either from the question's own <c>OptionsJson</c> or, more commonly, from the
/// parent group's <c>SharedOptionsJson</c> passed in by the factory.
/// </summary>
public partial class MatchingQuestionViewModel : ObservableObject, IReadingQuestionViewModel
{
    [ObservableProperty]
    private MatchingTag? _selectedTag;

    public int QuestionId { get; }
    public int DisplayNumber { get; }
    public string Stem { get; }
    public QuestionType Type { get; }
    public ObservableCollection<MatchingTag> AvailableTags { get; }

    public bool HasAnswer => SelectedTag is not null;

    public MatchingQuestionViewModel(TestQuestion source, int displayNumber, IReadOnlyList<string>? sharedOptions = null)
    {
        QuestionId = source.Id;
        DisplayNumber = displayNumber;
        Stem = source.Stem;
        Type = source.Type;

        IReadOnlyList<string> rawOptions =
            sharedOptions is { Count: > 0 }
                ? sharedOptions
                : QuestionOptionParser.ParseStringArray(source.OptionsJson);

        AvailableTags = new ObservableCollection<MatchingTag>(
            rawOptions.Select((text, i) =>
            {
                var tag = ExtractTag(text, i);
                var label = ExtractLabel(text);
                return new MatchingTag(tag, label);
            }));
    }

    partial void OnSelectedTagChanged(MatchingTag? value) => OnPropertyChanged(nameof(HasAnswer));

    public string GetAnswerJson() => JsonSerializer.Serialize(SelectedTag?.Tag ?? string.Empty);

    private static string ExtractTag(string raw, int fallbackIndex)
    {
        var trimmed = raw.TrimStart();
        // Accept both "A. text", "A: text", "A — text", "i. text", "xiii. text".
        for (var i = 1; i < Math.Min(trimmed.Length, 6); i++)
        {
            var c = trimmed[i];
            if (c == '.' || c == ')' || c == ':' || c == '—' || c == '-')
            {
                return trimmed[..i].Trim();
            }
        }
        return ((char)('A' + fallbackIndex)).ToString();
    }

    private static string ExtractLabel(string raw)
    {
        var trimmed = raw.TrimStart();
        for (var i = 1; i < Math.Min(trimmed.Length, 6); i++)
        {
            var c = trimmed[i];
            if (c == '.' || c == ')' || c == ':' || c == '—' || c == '-')
            {
                return trimmed[(i + 1)..].Trim();
            }
        }
        return raw;
    }
}

public sealed record MatchingTag(string Tag, string Label)
{
    public string Display => string.Equals(Tag, Label, StringComparison.Ordinal)
        ? Tag
        : $"{Tag} — {Label}";

    public override string ToString() => Display;
}
