using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.Modules.Reading.Services;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Phoneme/IPA breakdown of a word (F3) for display: a row of phoneme tiles with the tricky-for-
/// Russian sounds flagged, plus an optional best-effort "what you said" hint. A presentation
/// wrapper built from a <see cref="WordPronunciationGuide"/> — created in code, not via DI.
/// </summary>
public partial class PhonemeGuideViewModel : ObservableObject
{
    public string Word { get; }
    public string Ipa { get; }
    public bool Found { get; }

    public ObservableCollection<PhonemeUnitViewModel> Units { get; } = new();

    /// <summary>Best-effort phoneme-diff hint (HasFeedback=false when unavailable). A hint, not a verdict.</summary>
    public bool HasFeedback { get; }
    public string? FeedbackRu { get; }

    public PhonemeGuideViewModel(WordPronunciationGuide guide, WordPhonemeFeedback? feedback = null)
    {
        Word = guide.Word;
        Ipa = guide.Ipa;
        Found = guide.Found;

        foreach (var u in guide.Units)
            Units.Add(new PhonemeUnitViewModel(u));

        if (feedback is { HasData: true } && !string.IsNullOrWhiteSpace(feedback.FeedbackRu))
        {
            HasFeedback = true;
            FeedbackRu = feedback.FeedbackRu;
        }
    }
}

/// <summary>One phoneme tile.</summary>
public sealed class PhonemeUnitViewModel
{
    public string Ipa { get; }
    public string Arpabet { get; }
    public bool IsTricky { get; }

    public PhonemeUnitViewModel(PhonemeUnit unit)
    {
        Ipa = string.IsNullOrEmpty(unit.Ipa) ? unit.Arpabet : unit.Ipa;
        Arpabet = unit.Arpabet;
        IsTricky = unit.IsTrickyForRu;
    }

    public string ToolTipText => IsTricky
        ? $"/{Ipa}/ ({Arpabet}) — трудный для русскоговорящих звук"
        : $"/{Ipa}/ ({Arpabet})";
}
