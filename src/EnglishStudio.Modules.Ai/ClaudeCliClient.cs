using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishStudio.Modules.Ai;

public sealed class ClaudeCliClient : IClaudeCliClient
{
    private readonly ILogger<ClaudeCliClient> _log;
    private readonly IOptionsMonitor<ClaudeCliOptions> _options;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    private string? _executablePath;
    private string? _version;

    public ClaudeCliClient(ILogger<ClaudeCliClient> log, IOptionsMonitor<ClaudeCliOptions> options)
    {
        _log = log;
        _options = options;
        _executablePath = ClaudeCliLocator.Locate(_options.CurrentValue.ConfiguredPath);
    }

    public bool IsAvailable => !string.IsNullOrEmpty(_executablePath);
    public string? ExecutablePath => _executablePath;
    public string? Version => _version;

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        _executablePath = ClaudeCliLocator.Locate(_options.CurrentValue.ConfiguredPath);
        if (_executablePath is null)
        {
            _version = null;
            return false;
        }
        _version = await ClaudeCliLocator.ProbeVersionAsync(_executablePath, ct);
        return true;
    }

    public async Task<ClaudeCliResponse> RunAsync(
        string prompt,
        ClaudeOutputFormat outputFormat = ClaudeOutputFormat.Json,
        string? resumeSessionId = null,
        TimeSpan? timeout = null,
        IReadOnlyList<string>? imagePaths = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt must not be empty.", nameof(prompt));
        }
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "Claude CLI not found. Set ClaudeCliPath in Settings or install claude code.");
        }

        var validImages = FilterImages(imagePaths);

        // Serialize calls — Max subscription has per-account concurrency; one at a time is safest.
        await _gate.WaitAsync(ct);
        try
        {
            return await RunCoreAsync(prompt, outputFormat, resumeSessionId, timeout ?? TimeSpan.FromMinutes(2), validImages, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<string> FilterImages(IReadOnlyList<string>? imagePaths)
    {
        var valid = new List<string>();
        if (imagePaths is null) return valid;
        foreach (var p in imagePaths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (!File.Exists(p))
            {
                _log.LogWarning("Claude CLI: image path does not exist, skipping: {Path}", p);
                continue;
            }
            var size = new FileInfo(p).Length;
            if (size > 4 * 1024 * 1024)
            {
                _log.LogWarning("Claude CLI: image {Path} is {Size} bytes (>4MB); Anthropic may reject.", p, size);
            }
            valid.Add(Path.GetFullPath(p));
        }
        return valid;
    }

    private async Task<ClaudeCliResponse> RunCoreAsync(
        string prompt, ClaudeOutputFormat outputFormat, string? resumeSessionId, TimeSpan timeout,
        IReadOnlyList<string> imagePaths, CancellationToken ct)
    {
        var args = BuildArgs(outputFormat, resumeSessionId, imagePaths);
        var finalPrompt = imagePaths.Count == 0 ? prompt : PrependImageRefs(prompt, imagePaths);
        _log.LogDebug("Claude CLI {Exe} {Args} (prompt={Length} chars, images={ImageCount})",
            _executablePath, args, finalPrompt.Length, imagePaths.Count);

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath!,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Write prompt to stdin so we are not bounded by Windows command-line length.
        await process.StandardInput.WriteAsync(finalPrompt.AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        sw.Stop();

        if (process.ExitCode != 0)
        {
            var errorText = string.Join(
                Environment.NewLine,
                new[] { stdout.Trim(), stderr.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
            _log.LogWarning("Claude CLI exit {Code}, output: {Err}", process.ExitCode, errorText);
            return new ClaudeCliResponse(
                Text: errorText,
                SessionId: null,
                CostUsd: null,
                DurationMs: (int)sw.ElapsedMilliseconds,
                IsError: true);
        }

        if (outputFormat == ClaudeOutputFormat.Json)
        {
            return ParseJsonEnvelope(stdout, sw.ElapsedMilliseconds);
        }
        return new ClaudeCliResponse(stdout.Trim(), null, null, (int)sw.ElapsedMilliseconds, false);
    }

    private static string BuildArgs(ClaudeOutputFormat fmt, string? resumeSessionId, IReadOnlyList<string> imagePaths)
    {
        var sb = new StringBuilder();
        sb.Append("-p");
        sb.Append(" --output-format ").Append(fmt switch
        {
            ClaudeOutputFormat.Text => "text",
            ClaudeOutputFormat.Json => "json",
            ClaudeOutputFormat.StreamJson => "stream-json",
            _ => "json"
        });

        if (!string.IsNullOrWhiteSpace(resumeSessionId))
        {
            sb.Append(" --resume ").Append(resumeSessionId);
        }

        if (imagePaths.Count > 0)
        {
            // The Read tool needs explicit permission for paths outside the CWD. Grant it
            // for the parent directories of the supplied images only (least privilege),
            // and whitelist Read as the only tool the model is allowed to call.
            // Use --allowedTools BEFORE the prompt stdin so the variadic <tools...> arg
            // doesn't consume anything else.
            sb.Append(" --allowedTools Read");

            var dirs = imagePaths
                .Select(p => Path.GetDirectoryName(p)!)
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
            {
                sb.Append(" --add-dir \"").Append(d).Append('"');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Prepends each image as <c>@&lt;absolute-path&gt;</c> on its own line so the CLI's
    /// Read tool ingests it before processing the rest of the prompt. Discovery verified
    /// Claude Code 2.1.x recognises this in-prompt syntax for local image files.
    /// </summary>
    private static string PrependImageRefs(string prompt, IReadOnlyList<string> imagePaths)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < imagePaths.Count; i++)
        {
            if (imagePaths.Count > 1)
            {
                sb.Append("Image ").Append(i + 1).Append(" of ").Append(imagePaths.Count).Append(": ");
            }
            sb.Append('@').Append(imagePaths[i]).AppendLine();
        }
        sb.AppendLine();
        sb.Append(prompt);
        return sb.ToString();
    }

    private static ClaudeCliResponse ParseJsonEnvelope(string stdout, long durationMs)
    {
        try
        {
            var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout, JsonOptions);
            if (env is null)
            {
                return new ClaudeCliResponse(stdout, null, null, (int)durationMs, true);
            }
            return new ClaudeCliResponse(
                Text: env.Result ?? string.Empty,
                SessionId: env.SessionId,
                CostUsd: env.TotalCostUsd,
                DurationMs: (int)durationMs,
                IsError: env.IsError);
        }
        catch (JsonException)
        {
            // CLI returned non-JSON for some reason — surface raw stdout.
            return new ClaudeCliResponse(stdout, null, null, (int)durationMs, true);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private sealed record JsonEnvelope(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("result")] string? Result,
        [property: JsonPropertyName("session_id")] string? SessionId,
        [property: JsonPropertyName("total_cost_usd")] double? TotalCostUsd,
        [property: JsonPropertyName("is_error")] bool IsError);
}

public sealed class ClaudeCliOptions
{
    public string? ConfiguredPath { get; set; }
}
