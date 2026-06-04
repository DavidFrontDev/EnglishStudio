using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.Reading;

/// <summary>
/// Shadowing (F4): splits a text into sentences (via <see cref="SentenceSplitter"/>) and scores the
/// user's spoken repeat of one sentence by reusing the Phase-4 <see cref="IReadingAnalysisService"/>
/// over the sentence's reference word tokens. The UI records the repeat WAV through
/// <c>IReadingAudioCapture</c> and passes its path here.
/// </summary>
public sealed class ShadowingService : IShadowingService
{
    private readonly SentenceSplitter _splitter;
    private readonly IReadingAnalysisService _analysis;
    private readonly ILogger<ShadowingService> _log;

    public ShadowingService(
        SentenceSplitter splitter,
        IReadingAnalysisService analysis,
        ILogger<ShadowingService> log)
    {
        _splitter = splitter;
        _analysis = analysis;
        _log = log;
    }

    public IReadOnlyList<ShadowingSentence> GetSentences(IReadOnlyList<TextToken> tokens) =>
        _splitter.Split(tokens);

    public async Task<ShadowingScore?> ScoreRepeatAsync(
        string wavPath,
        ShadowingSentence sentence,
        IReadOnlyList<TextToken> tokens,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wavPath) || sentence is null || tokens is null) return null;

        var slice = tokens
            .Where(t => t.Kind == TokenKind.Word && t.WordIndex is int wi
                        && wi >= sentence.StartWordIndex && wi <= sentence.EndWordIndex)
            .OrderBy(t => t.WordIndex!.Value)
            .ToList();
        if (slice.Count == 0) return null;

        var analysis = await _analysis.AnalyzeAsync(wavPath, slice, progress: null, ct);
        if (analysis is null)
        {
            _log.LogInformation("Shadowing: analysis unavailable for sentence {Index}.", sentence.Index);
            return null;
        }

        return new ShadowingScore(analysis.AccuracyPct, analysis.WordsExpected, analysis.WordsSpoken, analysis.Words);
    }
}
