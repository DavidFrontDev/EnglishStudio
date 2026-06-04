using System.Text.RegularExpressions;
using EnglishStudio.Modules.Ai.Evaluators;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace EnglishStudio.Modules.Ielts.Speaking;

/// <summary>
/// One word emitted by Whisper with its on-clock start/end (seconds). Defined here so the
/// Speaking module doesn't need to take a direct dependency on the App-level Whisper layer;
/// callers pass in plain records.
/// </summary>
public sealed record SpokenWord(string Word, double StartSec, double EndSec);

/// <summary>
/// Computes the four IELTS-relevant fluency metrics from a Whisper transcript and the
/// underlying WAV. <see cref="ComputeAsync(string, string, IReadOnlyList{SpokenWord}, int, CancellationToken)"/>
/// is the single entry point; the analyzer prefers word-level timestamps when available and
/// falls back to NAudio RMS-based silence detection otherwise.
/// </summary>
public sealed class SpeechMetricsAnalyzer
{
    private const double PauseThresholdSec = 0.4;     // 400 ms — common IELTS examiner cutoff
    private const double SilenceRmsThresholdDb = -40; // RMS below -40 dBFS = silence
    private const int SilenceBucketMs = 100;

    private static readonly string[] FillerPhrases =
    {
        "um", "uh", "uhm", "er", "erm", "ah", "mm", "hmm",
        "like", "you know", "i mean", "kind of", "sort of", "basically", "actually"
    };

    // Pre-compile fillers into a single boundary-aware regex: \b(um|uh|...)\b
    private static readonly Regex FillerRegex = new(
        @"\b(" + string.Join("|", FillerPhrases.Select(Regex.Escape)) + @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TokenizerRegex = new(@"[A-Za-z][A-Za-z'\-]*", RegexOptions.Compiled);

    private readonly ILogger<SpeechMetricsAnalyzer> _log;

    public SpeechMetricsAnalyzer(ILogger<SpeechMetricsAnalyzer> log)
    {
        _log = log;
    }

    public async Task<SpeakingMetrics> ComputeAsync(
        string wavPath,
        string transcript,
        IReadOnlyList<SpokenWord> words,
        int fallbackDurationSeconds,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            // Empty transcript → user said nothing intelligible. Treat as pure silence.
            return new SpeakingMetrics(0, 1.0, 0, 0);
        }

        var durationSec = await ResolveDurationAsync(wavPath, words, fallbackDurationSeconds, ct);
        if (durationSec < 5)
        {
            _log.LogWarning("SpeechMetricsAnalyzer: clip is only {Sec:0.0}s — metrics may be noisy.", durationSec);
        }
        if (durationSec <= 0)
        {
            return new SpeakingMetrics(0, 1.0, CountFillers(transcript), ComputeTtr(transcript));
        }

        var wpm = ComputeWpm(transcript, words, durationSec);
        var pauseRatio = await ComputePauseRatioAsync(wavPath, words, durationSec, ct);
        var fillers = CountFillers(transcript);
        var ttr = ComputeTtr(transcript);

        return new SpeakingMetrics(wpm, pauseRatio, fillers, ttr);
    }

    private static double ComputeWpm(string transcript, IReadOnlyList<SpokenWord> words, double durationSec)
    {
        // Prefer the timestamp count when present — it's already filtered to actual spoken tokens.
        var wordCount = words.Count > 0 ? words.Count : TokenizerRegex.Matches(transcript).Count;
        if (wordCount == 0 || durationSec <= 0) return 0;

        // Use active span (first→last word) when timestamps available, to discount leading/trailing silence.
        if (words.Count >= 2)
        {
            var activeSec = Math.Max(1.0, words[^1].EndSec - words[0].StartSec);
            return wordCount / (activeSec / 60.0);
        }
        return wordCount / (durationSec / 60.0);
    }

    private async Task<double> ComputePauseRatioAsync(
        string wavPath, IReadOnlyList<SpokenWord> words, double durationSec, CancellationToken ct)
    {
        if (words.Count >= 2)
        {
            double pauseSec = 0;
            for (var i = 1; i < words.Count; i++)
            {
                var gap = words[i].StartSec - words[i - 1].EndSec;
                if (gap > PauseThresholdSec) pauseSec += gap;
            }
            // Add leading/trailing silence (clipped at 0).
            pauseSec += Math.Max(0, words[0].StartSec);
            pauseSec += Math.Max(0, durationSec - words[^1].EndSec);
            return Math.Clamp(pauseSec / durationSec, 0, 1);
        }

        if (!File.Exists(wavPath))
        {
            return 0.5; // unknown; midpoint is conservative
        }

        return await Task.Run(() => RmsSilenceRatio(wavPath), ct);
    }

    /// <summary>
    /// Scans a 16 kHz mono PCM WAV in 100 ms buckets and returns the fraction of buckets
    /// whose RMS amplitude is below -40 dBFS. This is the timestamp-less fallback.
    /// </summary>
    private double RmsSilenceRatio(string wavPath)
    {
        try
        {
            using var reader = new AudioFileReader(wavPath);
            var sampleRate = reader.WaveFormat.SampleRate;
            var channels = reader.WaveFormat.Channels;
            var samplesPerBucket = Math.Max(1, (int)(sampleRate * channels * SilenceBucketMs / 1000.0));

            var buffer = new float[samplesPerBucket];
            int silentBuckets = 0;
            int totalBuckets = 0;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                double sumSquares = 0;
                for (var i = 0; i < read; i++) sumSquares += buffer[i] * buffer[i];
                var rms = Math.Sqrt(sumSquares / read);
                var dbfs = rms > 1e-7 ? 20 * Math.Log10(rms) : -120;
                totalBuckets++;
                if (dbfs < SilenceRmsThresholdDb) silentBuckets++;
            }
            return totalBuckets == 0 ? 0.5 : (double)silentBuckets / totalBuckets;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RMS silence analysis failed for {Path}", wavPath);
            return 0.5;
        }
    }

    private static int CountFillers(string transcript) => FillerRegex.Matches(transcript).Count;

    private static double ComputeTtr(string transcript)
    {
        var tokens = TokenizerRegex.Matches(transcript)
            .Select(m => m.Value.ToLowerInvariant())
            .ToList();
        if (tokens.Count == 0) return 0;
        var unique = new HashSet<string>(tokens).Count;
        return (double)unique / tokens.Count;
    }

    private async Task<double> ResolveDurationAsync(
        string wavPath, IReadOnlyList<SpokenWord> words, int fallbackSec, CancellationToken ct)
    {
        if (words.Count > 0) return Math.Max(words[^1].EndSec, fallbackSec);
        if (!File.Exists(wavPath)) return fallbackSec;

        try
        {
            return await Task.Run(() =>
            {
                using var reader = new AudioFileReader(wavPath);
                return reader.TotalTime.TotalSeconds;
            }, ct);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Falling back to client-supplied duration for {Path}", wavPath);
            return fallbackSec;
        }
    }
}
