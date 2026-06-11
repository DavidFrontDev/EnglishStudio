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

    public string? GetDisplayIpa(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        var key = word.Trim().ToLowerInvariant();

        // 1) Direct CMUdict hit.
        if (TryGetArpabet(key, out var p)) return ToIpa(p);

        // 2) British→American spelling variant (scepticism→skepticism, colour→color, …).
        var amer = Americanize(key);
        var hasAmer = !string.Equals(amer, key, StringComparison.Ordinal);
        if (hasAmer && TryGetArpabet(amer, out p)) return ToIpa(p);

        // 3) Morphological guess (approximate): strip a known affix and look up the stem.
        if (TryMorphArpabet(key, out var morph)) return ToIpa(morph);
        if (hasAmer && TryMorphArpabet(amer, out morph)) return ToIpa(morph);

        return null;
    }

    // ── British→American spelling normalization (fallback only) ─────────────

    private static readonly Dictionary<string, string> IrregularBritish = new(StringComparer.Ordinal)
    {
        ["sceptic"] = "skeptic", ["sceptics"] = "skeptics", ["sceptical"] = "skeptical",
        ["sceptically"] = "skeptically", ["scepticism"] = "skepticism",
        ["cheque"] = "check", ["cheques"] = "checks", ["grey"] = "gray",
        ["plough"] = "plow", ["mould"] = "mold", ["aluminium"] = "aluminum",
        ["draught"] = "draft", ["kerb"] = "curb", ["pyjamas"] = "pajamas",
        ["tyre"] = "tire", ["storey"] = "story", ["sceptre"] = "scepter",
    };

    private static string Americanize(string w)
    {
        if (IrregularBritish.TryGetValue(w, out var direct)) return direct;

        var s = w;
        // -ise / -isation / -yse families → -ize / -ization / -yze
        s = ReplaceSuffix(s, "isation", "ization");
        s = ReplaceSuffix(s, "isations", "izations");
        s = ReplaceSuffix(s, "ising", "izing");
        s = ReplaceSuffix(s, "ised", "ized");
        s = ReplaceSuffix(s, "ises", "izes");
        s = ReplaceSuffix(s, "ise", "ize");
        s = ReplaceSuffix(s, "ysing", "yzing");
        s = ReplaceSuffix(s, "ysed", "yzed");
        s = ReplaceSuffix(s, "yses", "yzes");
        s = ReplaceSuffix(s, "yse", "yze");
        // -our → -or (colour→color, favourite→favorite)
        s = ReplaceSuffix(s, "ours", "ors");
        s = ReplaceSuffix(s, "oured", "ored");
        s = ReplaceSuffix(s, "ouring", "oring");
        s = ReplaceSuffix(s, "our", "or");
        // æ/œ digraphs → e (anaemia→anemia, oesophagus→esophagus)
        s = s.Replace("ae", "e").Replace("oe", "e");
        return s;
    }

    private static string ReplaceSuffix(string s, string from, string to) =>
        s.EndsWith(from, StringComparison.Ordinal) && s.Length > from.Length
            ? string.Concat(s.AsSpan(0, s.Length - from.Length), to)
            : s;

    // ── Morphological fallback (approximate) ────────────────────────────────

    private static readonly (string Suffix, string[] Add)[] DerivationSuffixes =
    {
        ("ly",   new[] { "L", "IY" }),
        ("ness", new[] { "N", "AH", "S" }),
        ("less", new[] { "L", "AH", "S" }),
        ("ful",  new[] { "F", "AH", "L" }),
        ("ment", new[] { "M", "AH", "N", "T" }),
        ("ing",  new[] { "IH", "NG" }),
        ("ed",   new[] { "D" }),
        ("es",   new[] { "IH", "Z" }),
        ("s",    new[] { "Z" }),
    };

    private static readonly (string Prefix, string[] Add)[] DerivationPrefixes =
    {
        ("under", new[] { "AH", "N", "D", "ER" }),
        ("over",  new[] { "OW", "V", "ER" }),
        ("dis",   new[] { "D", "IH", "S" }),
        ("mis",   new[] { "M", "IH", "S" }),
        ("non",   new[] { "N", "AA", "N" }),
        ("pre",   new[] { "P", "R", "IY" }),
        ("re",    new[] { "R", "IY" }),
        ("un",    new[] { "AH", "N" }),
        ("ir",    new[] { "IH", "R" }),
        ("im",    new[] { "IH", "M" }),
        ("il",    new[] { "IH", "L" }),
        ("in",    new[] { "IH", "N" }),
    };

    /// <summary>Approximate ARPAbet for an out-of-dictionary word by stripping one known affix and
    /// looking up the stem. Returns the stem's phonemes glued to the affix's (best-effort, no stress).</summary>
    private bool TryMorphArpabet(string word, out IReadOnlyList<string> phonemes)
    {
        phonemes = Array.Empty<string>();
        var table = _table.Value;

        foreach (var (suffix, add) in DerivationSuffixes)
        {
            if (word.Length <= suffix.Length + 2 || !word.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var stem = word[..^suffix.Length];
            if (table.TryGetValue(stem, out var sp))           // happy+ly, walk+ing
            {
                phonemes = Combine(sp, add);
                return true;
            }
            if (table.TryGetValue(stem + "e", out var spe))    // make+ing (drop-e), use+ed
            {
                phonemes = Combine(spe, add);
                return true;
            }
        }

        foreach (var (prefix, add) in DerivationPrefixes)
        {
            if (word.Length <= prefix.Length + 2 || !word.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var stem = word[prefix.Length..];
            if (table.TryGetValue(stem, out var sp))           // ir+reducible, over+simplified
            {
                phonemes = Combine(add, sp);
                return true;
            }
        }

        return false;
    }

    private static string[] Combine(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var arr = new string[a.Count + b.Count];
        for (var i = 0; i < a.Count; i++) arr[i] = a[i];
        for (var i = 0; i < b.Count; i++) arr[a.Count + i] = b[i];
        return arr;
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
