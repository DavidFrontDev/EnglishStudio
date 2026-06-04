using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class PronunciationLexiconTests
{
    private static PronunciationLexicon Make() => new(NullLogger<PronunciationLexicon>.Instance);

    [Fact]
    public void Looks_up_arpabet_de_stressed()
    {
        var lex = Make();
        Assert.True(lex.TryGetArpabet("think", out var p));
        Assert.Equal(new[] { "TH", "IH", "NG", "K" }, p);
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        var lex = Make();
        Assert.True(lex.TryGetArpabet("THINK", out var p1));
        Assert.True(lex.TryGetArpabet("Think", out var p2));
        Assert.Equal(p1, p2);
    }

    [Fact]
    public void Unknown_word_returns_false()
    {
        var lex = Make();
        Assert.False(lex.TryGetArpabet("zzzqxnotaword", out var p));
        Assert.Empty(p);
    }

    [Fact]
    public void Renders_ipa_from_arpabet()
    {
        var lex = Make();
        lex.TryGetArpabet("think", out var p);
        Assert.Equal("θɪŋk", lex.ToIpa(p));
    }

    [Theory]
    [InlineData("TH")]
    [InlineData("DH")]
    [InlineData("W")]
    [InlineData("AE")]
    [InlineData("NG")]
    [InlineData("AH0")] // stress digits ignored... (AH is not tricky, see below) — sanity for stripping
    public void Strips_stress_digits_for_tricky_check(string code)
    {
        var lex = Make();
        // The point is no exception and digit-stripping; AH is intentionally not tricky.
        var tricky = lex.IsTrickyForRu(code);
        if (code.StartsWith("AH")) Assert.False(tricky);
        else Assert.True(tricky);
    }

    [Theory]
    [InlineData("S", false)]
    [InlineData("T", false)]
    [InlineData("TH", true)]
    [InlineData("R", true)]
    public void Tricky_for_russian_flags_expected_sounds(string code, bool expected) =>
        Assert.Equal(expected, Make().IsTrickyForRu(code));
}
