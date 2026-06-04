namespace EnglishStudio.IeltsWritingBandGen;

internal static class JsonUtil
{
    /// <summary>
    /// Strips Markdown code fences if present and returns the substring from the first '{'
    /// to the last '}'. Mirrors the helper in ClaudeIeltsEssayEvaluator + IeltsReadingGen.
    /// </summary>
    public static string? ExtractObject(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) trimmed = trimmed[..lastFence];
        }
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return (start >= 0 && end > start) ? trimmed[start..(end + 1)] : null;
    }
}
