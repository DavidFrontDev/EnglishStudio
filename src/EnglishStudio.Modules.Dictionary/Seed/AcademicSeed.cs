using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnglishStudio.Modules.Dictionary.Seed;

internal static class AcademicSeedReader
{
    public static List<AwlEntry> ReadAwl(Stream json)
    {
        var doc = JsonDocument.Parse(json);
        var list = new List<AwlEntry>();
        foreach (var sublistProp in doc.RootElement.EnumerateObject())
        {
            if (!sublistProp.Name.StartsWith("sublist_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(sublistProp.Name.AsSpan("sublist_".Length), out var sublist))
                continue;
            foreach (var headProp in sublistProp.Value.EnumerateObject())
            {
                var family = new List<string>();
                if (headProp.Value.TryGetProperty("subwords", out var sub) &&
                    sub.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sub.EnumerateArray())
                    {
                        if (s.ValueKind == JsonValueKind.String) family.Add(s.GetString()!);
                    }
                }
                list.Add(new AwlEntry(headProp.Name, sublist, family));
            }
        }
        return list;
    }

    public static List<AvlEntry> ReadAvl(Stream json)
    {
        var doc = JsonDocument.Parse(json);
        var list = new List<AvlEntry>();
        foreach (var bandProp in doc.RootElement.EnumerateObject())
        {
            if (!bandProp.Name.StartsWith("band_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(bandProp.Name.AsSpan("band_".Length), out var band))
                continue;
            foreach (var lemmaProp in bandProp.Value.EnumerateObject())
            {
                int? freq = null;
                string? pos = null;
                if (lemmaProp.Value.TryGetProperty("frequency", out var f) &&
                    f.ValueKind == JsonValueKind.Number)
                {
                    freq = f.GetInt32();
                }
                if (lemmaProp.Value.TryGetProperty("POS", out var p) &&
                    p.ValueKind == JsonValueKind.String)
                {
                    pos = p.GetString();
                }
                list.Add(new AvlEntry(lemmaProp.Name, band, freq, pos));
            }
        }
        return list;
    }
}

internal sealed record AwlEntry(string Headword, int Sublist, IReadOnlyList<string> Family);

internal sealed record AvlEntry(string Lemma, int Band, int? Frequency, string? PosCode);

internal static class AvlPosMap
{
    public static string ToOurPos(string? raw) => raw?.ToLowerInvariant() switch
    {
        "n" => "n",
        "v" => "v",
        "j" => "adj",
        "r" => "adv",
        "i" => "prep",
        "d" => "det",
        "p" => "pron",
        "c" => "conj",
        "m" => "num",
        "x" => "other",
        "a" => "det",
        "u" => "other",
        _   => "other",
    };
}
