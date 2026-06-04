using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class PhonemeFeedbackServiceTests
{
    private static PhonemeFeedbackService Make() =>
        new(new PronunciationLexicon(NullLogger<PronunciationLexicon>.Instance));

    [Fact]
    public void BuildGuide_breaks_word_into_phonemes_with_tricky_flags()
    {
        var guide = Make().BuildGuide("think");

        Assert.True(guide.Found);
        Assert.Equal("θɪŋk", guide.Ipa);
        Assert.Equal(4, guide.Units.Count);

        Assert.Equal("TH", guide.Units[0].Arpabet);
        Assert.Equal("θ", guide.Units[0].Ipa);
        Assert.True(guide.Units[0].IsTrickyForRu);   // θ
        Assert.True(guide.Units[2].IsTrickyForRu);   // ŋ
        Assert.False(guide.Units[3].IsTrickyForRu);  // k
    }

    [Fact]
    public void BuildGuide_not_found_for_unknown_word()
    {
        var guide = Make().BuildGuide("zzzqxnotaword");
        Assert.False(guide.Found);
        Assert.Empty(guide.Units);
    }

    [Fact]
    public void Compare_flags_th_to_s_substitution()
    {
        var fb = Make().Compare("think", "sink");

        Assert.True(fb.HasData);
        var sub = Assert.Single(fb.Diffs, d => d.Kind == PhonemeDiffKind.Substitution);
        Assert.Equal("θ", sub.Reference);
        Assert.Equal("s", sub.Said);
        Assert.Contains("/θ/ → /s/", fb.FeedbackRu);
    }

    [Fact]
    public void Compare_identical_words_report_accurate()
    {
        var fb = Make().Compare("water", "water");
        Assert.True(fb.HasData);
        Assert.All(fb.Diffs, d => Assert.Equal(PhonemeDiffKind.Match, d.Kind));
        Assert.Equal("Произношение точное.", fb.FeedbackRu);
    }

    [Fact]
    public void Compare_has_no_data_when_word_missing()
    {
        var fb = Make().Compare("think", "zzzqxnotaword");
        Assert.False(fb.HasData);
        Assert.Empty(fb.Diffs);
    }
}
