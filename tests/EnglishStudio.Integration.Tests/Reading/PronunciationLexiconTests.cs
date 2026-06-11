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

    // ── GetDisplayIpa: CMUdict + spelling normalization + morphology fallback ──

    [Fact]
    public void DisplayIpa_direct_hit()
    {
        Assert.Equal("θɪŋk", Make().GetDisplayIpa("think"));
    }

    [Theory]
    [InlineData("analyse", "analyze")]       // -yse → -yze rule
    [InlineData("scepticism", "skepticism")] // irregular British set
    public void DisplayIpa_falls_back_to_american_spelling(string british, string american)
    {
        var lex = Make();
        lex.TryGetArpabet(american, out var p);
        var expected = lex.ToIpa(p);

        Assert.False(lex.TryGetArpabet(british, out _)); // British spelling truly absent from CMUdict
        Assert.Equal(expected, lex.GetDisplayIpa(british));
    }

    [Fact]
    public void DisplayIpa_morphology_fills_oov_prefix_word()
    {
        var lex = Make();
        // "irreducible" is in neither CMUdict nor the spelling map, but "reducible" is.
        Assert.False(lex.TryGetArpabet("irreducible", out _));

        var ipa = lex.GetDisplayIpa("irreducible");
        Assert.False(string.IsNullOrEmpty(ipa));

        lex.TryGetArpabet("reducible", out var stem);
        Assert.EndsWith(lex.ToIpa(stem), ipa);          // stem phonemes preserved at the tail
        Assert.StartsWith(lex.ToIpa(new[] { "IH", "R" }), ipa); // "ir" prefix prepended
    }

    [Fact]
    public void DisplayIpa_returns_null_for_genuine_nonword()
    {
        Assert.Null(Make().GetDisplayIpa("zzzqxnotaword"));
    }
}
