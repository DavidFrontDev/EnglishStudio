using EnglishStudio.App.Audio;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.Reading;

/// <summary>
/// Post-read analysis: transcribes the recorded WAV with Whisper (medium, word timestamps),
/// aligns the hypothesis against the reference word tokens (weighted edit distance), and emits
/// per-word outcomes (spoken / skipped / mispronounced + score) plus an accuracy and a list of
/// difficult words. Returns null when Whisper can't run (model unavailable / transcription
/// failed) so the UI can show "analysis unavailable" rather than blocking.
/// </summary>
public sealed class WhisperReadingAnalysisService : IReadingAnalysisService
{
    // Char-similarity (0..100) bands for classifying an aligned reference word.
    private const double CorrectThreshold = 75;
    private const double SpokenThreshold = 40;
    private const double DifficultThreshold = 60;

    // Guard against pathological O(m*n) DP on very large texts.
    private const long MaxAlignmentCells = 12_000_000;

    private readonly IWhisperTranscriber _whisper;
    private readonly PronunciationAssessor _assessor;
    private readonly ILogger<WhisperReadingAnalysisService> _logger;

    public WhisperReadingAnalysisService(
        IWhisperTranscriber whisper,
        PronunciationAssessor assessor,
        ILogger<WhisperReadingAnalysisService> logger)
    {
        _whisper = whisper;
        _assessor = assessor;
        _logger = logger;
    }

    public async Task<ReadingAnalysis?> AnalyzeAsync(
        string wavPath,
        IReadOnlyList<TextToken> tokens,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wavPath) || !System.IO.File.Exists(wavPath))
        {
            _logger.LogWarning("Reading analysis: WAV not found at {Path}.", wavPath);
            return null;
        }

        var referenceTokens = tokens
            .Where(t => t.Kind == TokenKind.Word && t.WordIndex.HasValue)
            .OrderBy(t => t.WordIndex!.Value)
            .ToList();

        if (referenceTokens.Count == 0) return null;

        // Transcription dominates the time; map its 0..1 to 0..0.98 and finish at 1.0.
        var transcriptionProgress = new Progress<double>(f => progress?.Report(Math.Clamp(f, 0, 1) * 0.98));
        WhisperTranscriptResult? transcript;
        try
        {
            transcript = await _whisper.TranscribeWithTimestampsAsync(wavPath, transcriptionProgress, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading analysis: Whisper transcription failed.");
            return null;
        }

        if (transcript is null) return null;

        var reference = referenceTokens
            .Select(t => ReadingTokenizer.NormalizeWord(t.Text))
            .ToList();
        var hypothesis = transcript.Words
            .Select(w => ReadingTokenizer.NormalizeWord(w.Word))
            .Where(w => w.Length > 0)
            .ToList();

        var outcomes = Align(referenceTokens, reference, hypothesis);
        progress?.Report(1.0);

        var spoken = outcomes.Count(o => o.Spoken);
        var skipped = outcomes.Count(o => o.Skipped);
        var accuracy = outcomes.Count == 0
            ? 0
            : outcomes.Average(o => o.Spoken ? o.Score ?? 0 : 0);
        var difficult = outcomes
            .Where(o => o.Skipped || o.Mispronounced || (o.Spoken && o.Score is < DifficultThreshold))
            .Select(o => o.TokenIndex)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        return new ReadingAnalysis(
            Math.Round(accuracy, 1), referenceTokens.Count, spoken, skipped, outcomes, difficult);
    }

    /// <summary>
    /// Weighted edit-distance alignment of the hypothesis onto the reference, then per-word
    /// classification. Substitution cost reflects dissimilarity (0 = identical .. 1 = unrelated)
    /// so a skipped word becomes a deletion rather than a forced substitution of a far word.
    /// </summary>
    private List<ReadingWordOutcome> Align(
        IReadOnlyList<TextToken> referenceTokens,
        IReadOnlyList<string> reference,
        IReadOnlyList<string> hypothesis)
    {
        var m = reference.Count;
        var n = hypothesis.Count;

        if ((long)(m + 1) * (n + 1) > MaxAlignmentCells)
        {
            _logger.LogInformation("Reading analysis: text too large ({M}x{N}); using coarse alignment.", m, n);
            return CoarseAlign(referenceTokens, m, n);
        }

        // dp[i,j] = min cost to align reference[0..i) with hypothesis[0..j).
        var dp = new float[m + 1, n + 1];
        var back = new byte[m + 1, n + 1]; // 0=diag(sub/match) 1=up(delete ref) 2=left(insert hyp)
        for (var i = 1; i <= m; i++) { dp[i, 0] = i; back[i, 0] = 1; }
        for (var j = 1; j <= n; j++) { dp[0, j] = j; back[0, j] = 2; }

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var subCost = dp[i - 1, j - 1] + (float)(1.0 - CharSimilarity(reference[i - 1], hypothesis[j - 1]) / 100.0);
                var delCost = dp[i - 1, j] + 1f;
                var insCost = dp[i, j - 1] + 1f;

                if (subCost <= delCost && subCost <= insCost) { dp[i, j] = subCost; back[i, j] = 0; }
                else if (delCost <= insCost) { dp[i, j] = delCost; back[i, j] = 1; }
                else { dp[i, j] = insCost; back[i, j] = 2; }
            }
        }

        // Backtrack: record, for each reference word, the hypothesis word it paired with (or -1).
        var pairedHyp = new int[m];
        Array.Fill(pairedHyp, -1);
        var ri = m;
        var hj = n;
        while (ri > 0 || hj > 0)
        {
            var dir = (ri > 0 && hj > 0) ? back[ri, hj] : (ri > 0 ? (byte)1 : (byte)2);
            if (dir == 0) { pairedHyp[ri - 1] = hj - 1; ri--; hj--; }
            else if (dir == 1) { ri--; }
            else { hj--; }
        }

        var outcomes = new List<ReadingWordOutcome>(m);
        for (var i = 0; i < m; i++)
            outcomes.Add(Classify(referenceTokens[i].WordIndex!.Value, reference[i],
                pairedHyp[i] >= 0 ? hypothesis[pairedHyp[i]] : null));
        return outcomes;
    }

    private ReadingWordOutcome Classify(int wordIndex, string refWord, string? hypWord)
    {
        if (hypWord is null)
            return new ReadingWordOutcome(wordIndex, Spoken: false, Skipped: true, Mispronounced: false, Score: null);

        var score = _assessor.Assess(refWord, hypWord).Score;
        if (score >= CorrectThreshold)
            return new ReadingWordOutcome(wordIndex, Spoken: true, Skipped: false, Mispronounced: false, Score: score);
        if (score >= SpokenThreshold)
            return new ReadingWordOutcome(wordIndex, Spoken: true, Skipped: false, Mispronounced: true, Score: score);
        // Too dissimilar to count as an attempt at this word — treat as skipped.
        return new ReadingWordOutcome(wordIndex, Spoken: false, Skipped: true, Mispronounced: false, Score: null);
    }

    /// <summary>Fallback for huge texts: mark words spoken proportionally, no per-word detail.</summary>
    private static List<ReadingWordOutcome> CoarseAlign(IReadOnlyList<TextToken> referenceTokens, int m, int n)
    {
        var spokenCount = Math.Min(m, n);
        var outcomes = new List<ReadingWordOutcome>(m);
        for (var i = 0; i < m; i++)
        {
            var spoken = i < spokenCount;
            outcomes.Add(new ReadingWordOutcome(
                referenceTokens[i].WordIndex!.Value, spoken, Skipped: !spoken, Mispronounced: false,
                Score: spoken ? 100 : null));
        }
        return outcomes;
    }

    private static double CharSimilarity(string a, string b)
    {
        if (a == b) return 100;
        var max = Math.Max(a.Length, b.Length);
        if (max == 0) return 100;
        return 100.0 * (1.0 - (double)Levenshtein(a, b) / max);
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
