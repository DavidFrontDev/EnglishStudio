using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// CMUdict-backed pronunciation lexicon: word → ARPAbet phonemes (de-stressed, one pronunciation
/// per word), ARPAbet → IPA rendering, and a "hard for Russian speakers" check. Singleton; the
/// ~126k-entry table is loaded lazily from the gzipped embedded resource on first use.
/// </summary>
public sealed class PronunciationLexicon : IPronunciationLexicon
{
    private const string ResourceSuffix = "Seed.cmudict.gz";

    // De-stressed ARPAbet → IPA. Stress digits are stripped before lookup.
    private static readonly Dictionary<string, string> ArpabetToIpa = new(StringComparer.Ordinal)
    {
        ["AA"] = "ɑ", ["AE"] = "æ", ["AH"] = "ʌ", ["AO"] = "ɔ", ["AW"] = "aʊ",
        ["AY"] = "aɪ", ["EH"] = "ɛ", ["ER"] = "ɜr", ["EY"] = "eɪ", ["IH"] = "ɪ",
        ["IY"] = "iː", ["OW"] = "oʊ", ["OY"] = "ɔɪ", ["UH"] = "ʊ", ["UW"] = "uː",
        ["B"] = "b", ["CH"] = "tʃ", ["D"] = "d", ["DH"] = "ð", ["F"] = "f",
        ["G"] = "ɡ", ["HH"] = "h", ["JH"] = "dʒ", ["K"] = "k", ["L"] = "l",
        ["M"] = "m", ["N"] = "n", ["NG"] = "ŋ", ["P"] = "p", ["R"] = "ɹ",
        ["S"] = "s", ["SH"] = "ʃ", ["T"] = "t", ["TH"] = "θ", ["V"] = "v",
        ["W"] = "w", ["Y"] = "j", ["Z"] = "z", ["ZH"] = "ʒ",
    };

    // Phonemes English learners with a Russian L1 most often struggle with.
    private static readonly HashSet<string> TrickyForRu = new(StringComparer.Ordinal)
    {
        "TH", "DH", "W", "AE", "NG", "R", "ER", "IH", "IY", "HH",
    };

    private readonly ILogger<PronunciationLexicon> _log;
    private readonly Lazy<Dictionary<string, string[]>> _table;

    public PronunciationLexicon(ILogger<PronunciationLexicon> log)
    {
        _log = log;
        _table = new Lazy<Dictionary<string, string[]>>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool TryGetArpabet(string word, out IReadOnlyList<string> phonemes)
    {
        phonemes = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(word)) return false;

        var key = word.Trim().ToLowerInvariant();
        if (_table.Value.TryGetValue(key, out var arr))
        {
            phonemes = arr;
            return true;
        }
        return false;
    }

    public string ToIpa(IReadOnlyList<string> arpabet)
    {
        if (arpabet is null || arpabet.Count == 0) return string.Empty;
        var sb = new StringBuilder(arpabet.Count * 2);
        foreach (var p in arpabet)
            sb.Append(ToIpaPhoneme(p));
        return sb.ToString();
    }

    /// <summary>IPA glyph for a single ARPAbet code (stress digits ignored); falls back to lowercase.</summary>
    public static string ToIpaPhoneme(string arpabet)
    {
        var code = StripStress(arpabet);
        return ArpabetToIpa.TryGetValue(code, out var ipa) ? ipa : code.ToLowerInvariant();
    }

    public bool IsTrickyForRu(string arpabet) => TrickyForRu.Contains(StripStress(arpabet));

    private static string StripStress(string arpabet)
    {
        if (string.IsNullOrEmpty(arpabet)) return string.Empty;
        var end = arpabet.Length;
        while (end > 0 && char.IsDigit(arpabet[end - 1])) end--;
        return end == arpabet.Length ? arpabet.ToUpperInvariant() : arpabet[..end].ToUpperInvariant();
    }

    private Dictionary<string, string[]> Load()
    {
        var table = new Dictionary<string, string[]>(130_000, StringComparer.OrdinalIgnoreCase);
        // Share phoneme-code string instances across all entries (only ~39 distinct codes).
        var intern = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var asm = typeof(PronunciationLexicon).Assembly;
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal));
            if (name is null)
            {
                _log.LogError("CMUdict embedded resource not found ('{Suffix}').", ResourceSuffix);
                return table;
            }

            using var raw = asm.GetManifestResourceStream(name)!;
            using var gz = new GZipStream(raw, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var sp = line.IndexOf(' ');
                if (sp <= 0 || sp >= line.Length - 1) continue;
                var word = line[..sp];
                var parts = line[(sp + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                for (var i = 0; i < parts.Length; i++)
                {
                    if (intern.TryGetValue(parts[i], out var shared)) parts[i] = shared;
                    else intern[parts[i]] = parts[i];
                }
                table[word] = parts;
            }

            _log.LogInformation("Loaded pronunciation lexicon: {Count} words.", table.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load pronunciation lexicon.");
        }

        return table;
    }
}
