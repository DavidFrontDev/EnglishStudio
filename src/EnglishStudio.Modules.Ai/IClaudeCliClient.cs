namespace EnglishStudio.Modules.Ai;

public interface IClaudeCliClient
{
    /// <summary>
    /// Returns true if `claude.exe` was found in PATH or a configured location.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Path to the resolved claude.exe (or null if not found).
    /// </summary>
    string? ExecutablePath { get; }

    /// <summary>
    /// Detected claude CLI version string, if available.
    /// </summary>
    string? Version { get; }

    /// <summary>
    /// Re-runs auto-detection.
    /// </summary>
    Task<bool> RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Execute one round-trip against the Claude CLI in headless `-p` mode.
    /// Prompt is sent via stdin so it can be arbitrarily long.
    /// </summary>
    /// <param name="imagePaths">
    /// Optional absolute paths to image files (PNG/JPEG/WebP/GIF) that Claude should see.
    /// Each path is referenced as <c>@&lt;path&gt;</c> at the top of the prompt; the CLI's
    /// Read tool ingests it. Non-existent paths are logged and skipped. Null/empty = text-only.
    /// </param>
    Task<ClaudeCliResponse> RunAsync(
        string prompt,
        ClaudeOutputFormat outputFormat = ClaudeOutputFormat.Json,
        string? resumeSessionId = null,
        TimeSpan? timeout = null,
        IReadOnlyList<string>? imagePaths = null,
        CancellationToken ct = default);
}
