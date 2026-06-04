using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;
using Microsoft.Extensions.Logging;
using Vosk;

namespace EnglishStudio.App.Reading.Audio;

/// <summary>
/// Owns the offline Vosk speech model (lazy one-time download + load) and hands out a fresh
/// <see cref="VoskStreamSession"/> per reading session. Registered as a singleton so the
/// ~50 MB model is loaded once and reused; each session gets its own stateful recognizer.
/// </summary>
public sealed class VoskSpeechRecognizer : IDisposable
{
    private const string ModelUrl = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";
    private const string ModelArchiveTopFolder = "vosk-model-small-en-us-0.15";
    public const int SampleRate = 16000;

    private readonly ILogger<VoskSpeechRecognizer> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private Model? _model;

    static VoskSpeechRecognizer()
    {
        // Silence the native engine's stderr chatter.
        try { global::Vosk.Vosk.SetLogLevel(-1); }
        catch { /* native lib may not be loadable yet; harmless */ }
    }

    public VoskSpeechRecognizer(ILogger<VoskSpeechRecognizer> logger, IHttpClientFactory httpFactory)
    {
        _logger = logger;
        _httpFactory = httpFactory;
    }

    public bool IsModelReady => _model is not null;

    private static string ModelDir => Path.Combine(DictionaryPaths.AppDataRoot, "Models", "vosk-en");

    private static bool IsModelInstalled() =>
        Directory.Exists(Path.Combine(ModelDir, "am")) && Directory.Exists(Path.Combine(ModelDir, "conf"));

    /// <summary>
    /// Ensures the model is on disk and loaded into memory. Idempotent and safe to call from
    /// multiple sessions. Reports human-readable status via <paramref name="progress"/>.
    /// </summary>
    public async Task<bool> EnsureModelAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (_model is not null) return true;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_model is not null) return true;

            if (!IsModelInstalled())
            {
                await DownloadAndExtractAsync(progress, ct);
            }

            if (!IsModelInstalled())
            {
                _logger.LogWarning("Vosk model missing after download attempt.");
                return false;
            }

            progress?.Report("Загрузка распознавателя речи…");
            _model = await Task.Run(() => new Model(ModelDir), ct);
            progress?.Report(string.Empty);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Vosk model.");
            progress?.Report($"Ошибка распознавателя: {ex.Message}");
            return false;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>Creates a fresh streaming recognition session. Caller must dispose it.</summary>
    public VoskStreamSession CreateSession()
    {
        if (_model is null)
            throw new InvalidOperationException("Vosk model is not loaded. Call EnsureModelAsync first.");
        return new VoskStreamSession(_model, SampleRate);
    }

    private async Task DownloadAndExtractAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var modelsRoot = Path.Combine(DictionaryPaths.AppDataRoot, "Models");
        Directory.CreateDirectory(modelsRoot);
        var zipPath = Path.Combine(modelsRoot, "vosk-en.zip.tmp");
        var extractTmp = Path.Combine(modelsRoot, ".vosk-extract-tmp");

        progress?.Report("Скачивание модели распознавания (~40 МБ, одноразово)…");
        var http = _httpFactory.CreateClient("Reading.VoskDownload");
        http.Timeout = TimeSpan.FromMinutes(30);

        using (var resp = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            long received = 0;
            var lastReport = TimeSpan.Zero;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using (var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1 << 16];
                int n;
                while ((n = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    received += n;
                    if (sw.Elapsed - lastReport > TimeSpan.FromMilliseconds(400))
                    {
                        var mb = received / 1024.0 / 1024.0;
                        var msg = total > 0
                            ? $"Скачивание модели: {mb:0.0} / {total / 1024.0 / 1024.0:0.0} МБ"
                            : $"Скачивание модели: {mb:0.0} МБ";
                        progress?.Report(msg);
                        lastReport = sw.Elapsed;
                    }
                }
            }
        }

        progress?.Report("Распаковка модели…");
        if (Directory.Exists(extractTmp)) Directory.Delete(extractTmp, recursive: true);
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractTmp), ct);

        // The archive nests everything under a single top folder; move it into place as vosk-en.
        var inner = Path.Combine(extractTmp, ModelArchiveTopFolder);
        var source = Directory.Exists(inner)
            ? inner
            : Directory.GetDirectories(extractTmp).FirstOrDefault() ?? extractTmp;

        if (Directory.Exists(ModelDir)) Directory.Delete(ModelDir, recursive: true);
        Directory.Move(source, ModelDir);

        try { if (Directory.Exists(extractTmp)) Directory.Delete(extractTmp, recursive: true); } catch { /* best effort */ }
        try { File.Delete(zipPath); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        _model?.Dispose();
        _model = null;
        _initGate.Dispose();
    }
}

/// <summary>Words recognized from one PCM chunk; <see cref="IsFinal"/> marks end-of-utterance.</summary>
public readonly record struct VoskChunkResult(IReadOnlyList<string> Words, bool IsFinal);

/// <summary>
/// A single live recognition stream over the shared model. Not thread-safe — feed it from one
/// thread (the capture callback). Words come back lowercased; the controller normalizes them.
/// </summary>
public sealed class VoskStreamSession : IDisposable
{
    private static readonly string[] EmptyWords = [];
    private readonly VoskRecognizer _rec;

    internal VoskStreamSession(Model model, int sampleRate)
    {
        _rec = new VoskRecognizer(model, sampleRate);
        _rec.SetWords(true);
    }

    /// <summary>Feeds a PCM-16 chunk; returns recognized words (partial or final).</summary>
    public VoskChunkResult Accept(byte[] buffer, int count)
    {
        var isFinal = _rec.AcceptWaveform(buffer, count);
        return isFinal
            ? new VoskChunkResult(ParseResultWords(_rec.Result()), true)
            : new VoskChunkResult(ParsePartialWords(_rec.PartialResult()), false);
    }

    /// <summary>Flushes the trailing utterance at stop.</summary>
    public VoskChunkResult Flush() => new(ParseResultWords(_rec.FinalResult()), true);

    private static IReadOnlyList<string> ParseResultWords(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var words = new List<string>(arr.GetArrayLength());
                foreach (var item in arr.EnumerateArray())
                    if (item.TryGetProperty("word", out var w) && w.GetString() is { Length: > 0 } s)
                        words.Add(s);
                if (words.Count > 0) return words;
            }
            if (root.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } t)
                return t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (JsonException) { /* malformed — treat as no words */ }
        return EmptyWords;
    }

    private static IReadOnlyList<string> ParsePartialWords(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("partial", out var p) && p.GetString() is { Length: > 0 } s)
                return s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (JsonException) { /* malformed — treat as no words */ }
        return EmptyWords;
    }

    public void Dispose() => _rec.Dispose();
}
