using System.IO;
using System.Net.Http;
using EnglishStudio.Modules.Dictionary.Data;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Whisper.net;

namespace EnglishStudio.App.Audio;

public sealed class WhisperTranscriber : IWhisperTranscriber, IDisposable
{
    private const string BaseModelUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";
    private const string BaseModelFile = "ggml-base.en.bin";
    private const long BaseModelExpectedSize = 147_951_465L; // ≈142 MB

    private const string MediumModelUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.en.bin";
    private const string MediumModelFile = "ggml-medium.en.bin";
    private const long MediumModelExpectedSize = 1_533_763_059L; // ≈1.5 GB

    private readonly ILogger<WhisperTranscriber> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private WhisperFactory? _baseFactory;
    private WhisperFactory? _mediumFactory;

    public WhisperTranscriber(ILogger<WhisperTranscriber> logger, IHttpClientFactory httpFactory)
    {
        _logger = logger;
        _httpFactory = httpFactory;
    }

    public bool IsModelReady => _baseFactory is not null || _mediumFactory is not null;
    public WhisperModelSize CurrentModelSize => _baseFactory is not null ? WhisperModelSize.Base : WhisperModelSize.Medium;

    private static string ModelDir => Path.Combine(DictionaryPaths.AppDataRoot, "Models");

    private static (string Url, string FileName, long ExpectedSize, string DownloadCaption) Spec(WhisperModelSize size) =>
        size switch
        {
            WhisperModelSize.Base => (BaseModelUrl, BaseModelFile, BaseModelExpectedSize,
                "Скачивание модели Whisper base.en (~142 МБ, одноразово)…"),
            WhisperModelSize.Medium => (MediumModelUrl, MediumModelFile, MediumModelExpectedSize,
                "Скачивание модели Whisper medium.en (~1.5 ГБ, одноразово)…"),
            _ => throw new ArgumentOutOfRangeException(nameof(size))
        };

    public Task<bool> EnsureModelDownloadedAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        => EnsureModelDownloadedAsync(WhisperModelSize.Base, progress, ct);

    public async Task<bool> EnsureModelDownloadedAsync(
        WhisperModelSize size,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var (url, fileName, expectedSize, caption) = Spec(size);
        var modelPath = Path.Combine(ModelDir, fileName);

        await _initGate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(ModelDir);

            if (!File.Exists(modelPath) || new FileInfo(modelPath).Length < expectedSize / 2)
            {
                progress?.Report(caption);
                var http = _httpFactory.CreateClient("Whisper.Download");
                http.Timeout = TimeSpan.FromHours(2); // medium can take a while on slow links

                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? expectedSize;
                var tmp = modelPath + ".tmp";
                long received = 0;
                var lastReport = DateTime.MinValue;

                await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[1 << 16];
                    int n;
                    while ((n = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                        received += n;
                        if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(400))
                        {
                            var mb = received / 1024.0 / 1024.0;
                            var totalMb = total / 1024.0 / 1024.0;
                            progress?.Report($"Скачивание Whisper {size}: {mb:0.0} / {totalMb:0.0} МБ");
                            lastReport = DateTime.UtcNow;
                        }
                    }
                }
                File.Move(tmp, modelPath, overwrite: true);
                progress?.Report("Модель загружена. Инициализация…");
            }

            if (size == WhisperModelSize.Base && _baseFactory is null)
            {
                progress?.Report("Загрузка движка Whisper base…");
                _baseFactory = WhisperFactory.FromPath(modelPath);
            }
            else if (size == WhisperModelSize.Medium && _mediumFactory is null)
            {
                progress?.Report("Загрузка движка Whisper medium…");
                _mediumFactory = WhisperFactory.FromPath(modelPath);
            }

            progress?.Report(string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure whisper model {Size}", size);
            progress?.Report($"Ошибка: {ex.Message}");
            return false;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<string?> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        if (_baseFactory is null)
        {
            var ok = await EnsureModelDownloadedAsync(WhisperModelSize.Base, ct: ct);
            if (!ok || _baseFactory is null) return null;
        }

        try
        {
            await using var processor = _baseFactory.CreateBuilder()
                .WithLanguage("en")
                .Build();
            await using var fs = File.OpenRead(wavPath);

            var sb = new System.Text.StringBuilder();
            await foreach (var segment in processor.ProcessAsync(fs, ct))
            {
                sb.Append(segment.Text);
            }
            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whisper base transcription failed");
            return null;
        }
    }

    public async Task<WhisperTranscriptResult?> TranscribeWithTimestampsAsync(
        string wavPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (_mediumFactory is null)
        {
            // Модель ещё не загружена в память (первый ответ за сессию) — может занять секунды.
            progress?.Report(0);
            var ok = await EnsureModelDownloadedAsync(WhisperModelSize.Medium, ct: ct);
            if (!ok || _mediumFactory is null) return null;
        }

        try
        {
            // Длительность клипа — чтобы перевести позицию обработанного аудио в долю 0..1.
            var totalSeconds = TryGetWavDurationSeconds(wavPath);

            await using var processor = _mediumFactory.CreateBuilder()
                .WithLanguage("en")
                .WithTokenTimestamps()
                .Build();
            await using var fs = File.OpenRead(wavPath);

            var text = new System.Text.StringBuilder();
            var words = new List<WordTimestamp>();

            await foreach (var segment in processor.ProcessAsync(fs, ct))
            {
                text.Append(segment.Text);
                AggregateTokensToWords(segment, words);
                if (totalSeconds > 0)
                    progress?.Report(Math.Clamp(segment.End.TotalSeconds / totalSeconds, 0, 1));
            }
            progress?.Report(1.0);
            return new WhisperTranscriptResult(text.ToString().Trim(), words);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whisper medium transcription failed");
            return null;
        }
    }

    private double TryGetWavDurationSeconds(string wavPath)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            return reader.TotalTime.TotalSeconds;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not probe WAV duration for progress: {Path}", wavPath);
            return 0;
        }
    }

    /// <summary>
    /// Whisper-cpp tokens are sub-word fragments — we re-glue them whenever a fragment starts
    /// without a leading space (a continuation of the previous word). Token Start/End are
    /// expressed in 10 ms ticks per whisper-cpp convention; we convert via /100 → seconds.
    /// </summary>
    private static void AggregateTokensToWords(SegmentData segment, List<WordTimestamp> words)
    {
        if (segment.Tokens is null) return;

        string? currentWord = null;
        double currentStart = 0;
        double currentEnd = 0;

        foreach (var token in segment.Tokens)
        {
            var text = token.Text ?? string.Empty;
            // Whisper emits special markers like "[_BEG_]" — skip anything in brackets or starting with [
            if (text.Length == 0 || text[0] == '[' || text[0] == '<') continue;

            var startSec = token.Start / 100.0;
            var endSec = token.End / 100.0;
            var startsNewWord = text.Length > 0 && text[0] == ' ';

            if (startsNewWord || currentWord is null)
            {
                FlushWord(words, ref currentWord, currentStart, currentEnd);
                currentWord = text.TrimStart();
                currentStart = startSec;
                currentEnd = endSec;
            }
            else
            {
                currentWord += text;
                currentEnd = endSec;
            }
        }

        FlushWord(words, ref currentWord, currentStart, currentEnd);
    }

    private static void FlushWord(List<WordTimestamp> words, ref string? buffer, double start, double end)
    {
        if (string.IsNullOrWhiteSpace(buffer)) { buffer = null; return; }
        var word = buffer.Trim();
        if (word.Length > 0) words.Add(new WordTimestamp(word, start, end));
        buffer = null;
    }

    public void Dispose()
    {
        _baseFactory?.Dispose();
        _mediumFactory?.Dispose();
    }
}
