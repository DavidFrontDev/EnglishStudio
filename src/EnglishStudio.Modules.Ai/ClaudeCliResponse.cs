namespace EnglishStudio.Modules.Ai;

/// <summary>
/// Result of a single `claude -p` invocation.
/// </summary>
public sealed record ClaudeCliResponse(
    string Text,
    string? SessionId,
    double? CostUsd,
    int DurationMs,
    bool IsError);
