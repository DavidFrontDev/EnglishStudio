using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// IELTS Reading importer.
// Converts a Cambridge "Reading" folder (Test№1..N + Answer/text№N.txt) into the
// ReadingTestDto JSON consumed by ReadingSeedService, plus extracts legend images.
//
// Usage:
//   IeltsReadingImport <readingRoot> <bookNumber> <outJsonPath> <imagesOutDir> [codePrefix]
//
//   readingRoot   folder containing "Test№1".."Test№N" and "Answer"
//   bookNumber    Cambridge book number (e.g. 15) — used in the test code/title
//   outJsonPath   where to write the JSON array of tests
//   imagesOutDir  where to copy legend PNGs (named "<code>.<rel>")
//   codePrefix    optional, default "ielts{book}-r-test"
//
// Card type is detected primarily from the instruction text; the filename prefix is a hint.

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: IeltsReadingImport <readingRoot> <bookNumber> <outJsonPath> <imagesOutDir> [codePrefix]");
    return 1;
}

var readingRoot = args[0];
var book = int.Parse(args[1], CultureInfo.InvariantCulture);
var outJsonPath = args[2];
var imagesOutDir = args[3];
var codePrefix = args.Length >= 5 ? args[4] : $"ielts{book}-r-test";

Directory.CreateDirectory(imagesOutDir);

var tests = new List<TestOut>();

// Test folders are named "Test№1".."Test№N"; discover and order by their number.
var testDirs = Directory.GetDirectories(readingRoot)
    .Select(d => (dir: d, num: TestNumberFromDir(d)))
    .Where(t => t.num > 0)
    .OrderBy(t => t.num)
    .ToList();

foreach (var (dir, num) in testDirs)
{
    var code = $"{codePrefix}{num}";
    // Answer files are named inconsistently across books: "text№N.txt" (Cam 15) or "Test№N.txt"
    // (Cam 16). Accept either.
    var answerDir = Path.Combine(readingRoot, "Answer");
    var answersPath = new[] { $"text№{num}.txt", $"Test№{num}.txt" }
        .Select(n => Path.Combine(answerDir, n))
        .FirstOrDefault(File.Exists);
    if (answersPath is null)
    {
        Console.Error.WriteLine($"WARN: no answer file for test {num} in {answerDir} (looked for text№{num}.txt / Test№{num}.txt); skipping.");
        continue;
    }

    var answers = AnswerKeyParser.Parse(File.ReadAllText(answersPath));
    var test = TestImporter.Import(dir, num, book, code, answers, imagesOutDir);
    tests.Add(test);

    var qCount = test.Parts.Sum(p => p.Groups.Sum(g => g.Questions.Count));
    Console.WriteLine($"Test {num} ({code}): {test.Parts.Count} parts, {test.Parts.Sum(p => p.Groups.Count)} cards, {qCount} questions.");
}

var json = JsonSerializer.Serialize(tests, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
});
File.WriteAllText(outJsonPath, json, new UTF8Encoding(false));
Console.WriteLine($"\nWrote {tests.Count} test(s) → {outJsonPath}");
return 0;

static int TestNumberFromDir(string dir)
{
    var name = Path.GetFileName(dir);
    var m = Regex.Match(name, @"(\d+)");
    return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
}

// ──────────────────────────────────────────────────────────────────────────────
//  Output DTOs (PascalCase → matches ReadingTestDto; reader is case-insensitive)
// ──────────────────────────────────────────────────────────────────────────────

sealed class TestOut
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Mode { get; set; } = "Academic";
    public string? Attribution { get; set; }
    public bool IsExamOnly { get; set; }
    public List<PartOut> Parts { get; set; } = new();
}

sealed class PartOut
{
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? IntroNoteRu { get; set; }
    public List<GroupOut> Groups { get; set; } = new();
}

sealed class GroupOut
{
    public int Order { get; set; }
    public string Layout { get; set; } = "FlatList";
    public string? Instruction { get; set; }
    public List<string>? SharedOptions { get; set; }
    public string? SharedListTitle { get; set; }
    public string? ImagePath { get; set; }
    public string? ExampleStem { get; set; }
    public string? ExampleAnswer { get; set; }
    public string? SummaryTemplate { get; set; }
    public List<QOut> Questions { get; set; } = new();
    // Not serialized: used internally to order groups within a part.
    [System.Text.Json.Serialization.JsonIgnore] public int FirstQuestion { get; set; }
}

sealed class QOut
{
    public int Order { get; set; }
    public string Type { get; set; } = "";
    public string Stem { get; set; } = "";
    public List<string>? Options { get; set; }
    public string AnswerKey { get; set; } = "";
    public List<string>? AcceptableAnswers { get; set; }
    public int Points { get; set; } = 1;
    public int? WordLimitMax { get; set; }
}

// One parsed answer-key entry.
sealed record AnswerSpec(int Number, string Canonical, List<string> Acceptable, bool IsLetter);

// ──────────────────────────────────────────────────────────────────────────────
//  Answer-key parsing  ("N. answer" with IELTS mini-language)
// ──────────────────────────────────────────────────────────────────────────────

static class AnswerKeyParser
{
    private static readonly Regex Line = new(@"^\s*(\d{1,2})[\.\)]\s*(.+?)\s*$", RegexOptions.Compiled);

    public static Dictionary<int, AnswerSpec> Parse(string text)
    {
        var result = new Dictionary<int, AnswerSpec>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var m = Line.Match(raw);
            if (!m.Success) continue;
            var num = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var ans = m.Groups[2].Value.Trim();

            var isLetter = Regex.IsMatch(ans, @"^[A-H]$")
                || Regex.IsMatch(ans, @"^(TRUE|FALSE|NOT GIVEN|YES|NO)$", RegexOptions.IgnoreCase);

            List<string> variants = isLetter
                ? new List<string> { ans.ToUpperInvariant() }
                : Expand(ans);

            result[num] = new AnswerSpec(num, variants[0], variants.Skip(1).Distinct().ToList(), isLetter);
        }
        return result;
    }

    // Expand the IELTS answer mini-language into all accepted surface forms:
    //   "a / b"                 → top-level alternatives
    //   "(x / y / z) word"      → optional slot: {x,y,z,∅} + word
    //   "word (and) word2"      → optional connective word
    //   "car (-) sharing"       → optional hyphen (car-sharing | car sharing)
    public static List<string> Expand(string raw)
    {
        // Handle "(-)" optional-hyphen first, producing two skeletons.
        var skeletons = ExpandOptionalHyphen(raw);

        var all = new List<string>();
        foreach (var s in skeletons)
        {
            foreach (var alt in SplitTopLevel(s, '/'))
                all.AddRange(ExpandParens(alt.Trim()));
        }

        // Clean up whitespace and drop empties / dups, preserve order.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var v in all)
        {
            var clean = Regex.Replace(v, @"\s+", " ").Trim();
            if (clean.Length == 0) continue;
            if (seen.Add(clean)) ordered.Add(clean);
        }
        return ordered.Count > 0 ? ordered : new List<string> { raw };
    }

    private static List<string> ExpandOptionalHyphen(string s)
    {
        var m = Regex.Match(s, @"(\S+)\s*\(-\)\s*(\S+)");
        if (!m.Success) return new List<string> { s };
        var hyphen = s[..m.Index] + m.Groups[1].Value + "-" + m.Groups[2].Value + s[(m.Index + m.Length)..];
        var spaced = s[..m.Index] + m.Groups[1].Value + " " + m.Groups[2].Value + s[(m.Index + m.Length)..];
        return new List<string> { hyphen, spaced };
    }

    // Expand parenthesised optional groups via cartesian product.
    private static List<string> ExpandParens(string s)
    {
        var open = s.IndexOf('(');
        if (open < 0) return new List<string> { s };
        var close = s.IndexOf(')', open);
        if (close < 0) return new List<string> { s.Replace("(", "").Replace(")", "") };

        var before = s[..open];
        var inner = s[(open + 1)..close];
        var after = s[(close + 1)..];

        // Options inside the group, plus the empty (optional) form.
        var opts = SplitTopLevel(inner, '/').Select(o => o.Trim()).ToList();
        opts.Add("");

        var results = new List<string>();
        foreach (var opt in opts)
            foreach (var tail in ExpandParens(after))
                results.Add(before + opt + tail);
        return results;
    }

    // Split on a delimiter that is NOT inside parentheses.
    private static List<string> SplitTopLevel(string s, char delim)
    {
        var parts = new List<string>();
        var depth = 0;
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);

            if (c == delim && depth == 0) { parts.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        parts.Add(sb.ToString());
        return parts;
    }
}

// ──────────────────────────────────────────────────────────────────────────────
//  Test importer
// ──────────────────────────────────────────────────────────────────────────────

static class TestImporter
{
    public static TestOut Import(string dir, int testNum, int book, string code,
        Dictionary<int, AnswerSpec> answers, string imagesOutDir)
    {
        // 1. Passages (part 1/2/3.txt) → parts with body + range.
        var parts = new List<PartOut>();
        var partRanges = new List<(int order, int first, int last)>();
        foreach (var pf in Directory.GetFiles(dir, "part*.txt").OrderBy(f => f))
        {
            var (order, title, body, first, last) = PassageParser.Parse(Reader.Read(pf), pf);
            parts.Add(new PartOut
            {
                Order = order,
                Title = title,
                Body = body,
                IntroNoteRu = first > 0 ? $"Вопросы {first}–{last} относятся к этому тексту." : null
            });
            partRanges.Add((order, first, last));
        }
        parts = parts.OrderBy(p => p.Order).ToList();

        // Some passage files lack the "You should spend … on Questions X-Y" line (e.g. a
        // mid-test passage that only carries its title), leaving the range as (0,0). Infer the
        // missing range from the neighbouring parts so question groups still land on the right
        // passage — first = previous part's last + 1, last = next part's first - 1.
        partRanges = partRanges.OrderBy(r => r.order).ToList();
        for (var i = 0; i < partRanges.Count; i++)
        {
            var (order, first, last) = partRanges[i];
            if (first == 0)
                first = i > 0 ? partRanges[i - 1].last + 1 : 1;
            if (last == 0)
                last = i + 1 < partRanges.Count && partRanges[i + 1].first > 0
                    ? partRanges[i + 1].first - 1
                    : int.MaxValue;
            partRanges[i] = (order, first, last);
        }

        // 2. Card files → groups (one card may yield several groups, e.g. Doble).
        var cardFiles = Directory.GetFiles(dir, "*.txt")
            .Where(f => !IsPassage(f) && !IsLegend(f))
            .ToList();

        var groups = new List<GroupOut>();
        foreach (var cf in cardFiles)
        {
            var text = Reader.Read(cf);
            var legend = LegendFor(cf);
            try
            {
                groups.AddRange(CardParser.Parse(cf, text, legend, answers, code, imagesOutDir));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ! {Path.GetFileName(cf)}: {ex.Message}");
            }
        }

        // 3. Assign each group to its part by first-question number, order by question.
        foreach (var g in groups)
        {
            var pr = partRanges.FirstOrDefault(r => g.FirstQuestion >= r.first && g.FirstQuestion <= r.last);
            var partOrder = pr.order != 0 ? pr.order : 1;
            var part = parts.First(p => p.Order == partOrder);
            part.Groups.Add(g);
        }
        foreach (var p in parts)
        {
            p.Groups = p.Groups.OrderBy(g => g.FirstQuestion).ToList();
            for (var i = 0; i < p.Groups.Count; i++) p.Groups[i].Order = i + 1;
        }

        return new TestOut
        {
            Code = code,
            Title = $"Cambridge IELTS {book} — Reading Test {testNum}",
            Mode = "Academic",
            Attribution = $"Cambridge IELTS {book} Academic / Reading Test {testNum}",
            Parts = parts
        };
    }

    private static bool IsPassage(string f) => Path.GetFileName(f).StartsWith("part", StringComparison.OrdinalIgnoreCase);
    private static bool IsLegend(string f) => Path.GetFileNameWithoutExtension(f).Contains("картинка", StringComparison.OrdinalIgnoreCase);

    // Returns the legend (text + optional png path) paired with a card file, or null.
    private static Legend? LegendFor(string cardFile)
    {
        var baseName = Path.GetFileNameWithoutExtension(cardFile);
        var dir = Path.GetDirectoryName(cardFile)!;
        var legendTxt = Path.Combine(dir, $"{baseName} картинка.txt");
        var legendPng = Path.Combine(dir, $"{baseName} картинка.png");
        if (!File.Exists(legendTxt) && !File.Exists(legendPng)) return null;
        return new Legend(
            File.Exists(legendTxt) ? Reader.Read(legendTxt) : null,
            File.Exists(legendPng) ? legendPng : null);
    }
}

sealed record Legend(string? Text, string? PngPath);

static class Reader
{
    public static string Read(string path) =>
        File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").TrimStart('﻿');
}

// ──────────────────────────────────────────────────────────────────────────────
//  Passage parser
// ──────────────────────────────────────────────────────────────────────────────

static class PassageParser
{
    public static (int order, string title, string body, int first, int last) Parse(string text, string path)
    {
        var lines = text.Split('\n');
        var order = 0;
        var spendIdx = -1;
        var first = 0; var last = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var pm = Regex.Match(line, @"READING PASSAGE\s+(\d+)", RegexOptions.IgnoreCase);
            if (pm.Success) order = int.Parse(pm.Groups[1].Value);
            var rm = Regex.Match(line, @"Questions?\s+(\d+)\s*[-–]\s*(\d+)", RegexOptions.IgnoreCase);
            if (rm.Success && spendIdx < 0)
            {
                spendIdx = i;
                first = int.Parse(rm.Groups[1].Value);
                last = int.Parse(rm.Groups[2].Value);
            }
        }

        if (order == 0)
        {
            var fm = Regex.Match(Path.GetFileNameWithoutExtension(path), @"(\d+)");
            order = fm.Success ? int.Parse(fm.Groups[1].Value) : 1;
        }

        // Title = first non-empty line after the "Questions X-Y" line; body = the rest.
        var titleIdx = -1;
        var startScan = spendIdx >= 0 ? spendIdx + 1 : 0;
        for (var i = startScan; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length > 0) { titleIdx = i; break; }
        }
        var title = titleIdx >= 0 ? lines[titleIdx].Trim() : $"Reading Passage {order}";
        var body = titleIdx >= 0
            ? string.Join("\n", lines.Skip(titleIdx + 1)).Trim()
            : text.Trim();

        return (order, title, body, first, last);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
//  Card parser
// ──────────────────────────────────────────────────────────────────────────────

static class CardParser
{
    // Gap "N ......" — the number may be followed by a period or paren before the dots
    // ("8 ......" in Cam 16 Test 1, "9. ......" in Test 2+), so allow an optional "."/")" separator.
    private static readonly Regex GapNumbered = new(@"(\d{1,2})[.)\s]*\.{3,}", RegexOptions.Compiled);
    private static readonly Regex GapBare = new(@"\.{3,}", RegexOptions.Compiled);
    // Numbered list item — accept "1 text", "1. text" and "1) text" (OCR conventions vary by book).
    private static readonly Regex NumberedItem = new(@"^\s*(\d{1,2})[.)]?\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex OptionLine = new(@"^\s*([A-Z])[\.\)]?\s+(.+)$", RegexOptions.Compiled);
    // Legend item: an UPPER-CASE letter (A–Z) or a lower-case roman numeral (i, ii, … x) tag.
    private static readonly Regex LegendItem = new(@"^([A-Z]|[ivxlcdm]+)[\.\)]?\s+(.+)$", RegexOptions.Compiled);

    // A single OCR'd card file can hold more than one question block of *different* kinds — e.g.
    // Cam 16 Test 3 "Selector 6" carries TRUE/FALSE/NOT GIVEN (27-32) and a "Which section A-H?"
    // matching block (33-37) back to back. Detecting one kind for the whole file would mis-type the
    // second block (its letter answers would render as TFNG chips). So split on each "Questions X-Y"
    // header and detect/parse every block on its own. Single-block files are returned whole (with any
    // "READING PASSAGE …" preamble intact), so this is a no-op for them.
    public static IEnumerable<GroupOut> Parse(string file, string text, Legend? legend,
        Dictionary<int, AnswerSpec> answers, string code, string imagesOutDir)
    {
        var results = new List<GroupOut>();
        foreach (var block in SplitIntoBlocks(text))
            results.AddRange(ParseBlock(file, block, legend, answers, code, imagesOutDir));
        return results;
    }

    private static readonly Regex BlockHeader = new(@"^\s*Questions?\s+\d+", RegexOptions.Compiled);

    private static List<string> SplitIntoBlocks(string text)
    {
        var lines = text.Split('\n');
        var starts = new List<int>();
        for (var i = 0; i < lines.Length; i++)
            if (BlockHeader.IsMatch(lines[i])) starts.Add(i);

        // 0 or 1 header → one block; keep the whole text (preamble and all) exactly as before.
        if (starts.Count <= 1) return new List<string> { text };

        var blocks = new List<string>();
        for (var b = 0; b < starts.Count; b++)
        {
            var to = b + 1 < starts.Count ? starts[b + 1] : lines.Length;
            blocks.Add(string.Join("\n", lines[starts[b]..to]));
        }
        return blocks;
    }

    private static IEnumerable<GroupOut> ParseBlock(string file, string text, Legend? legend,
        Dictionary<int, AnswerSpec> answers, string code, string imagesOutDir)
    {
        var prefix = PrefixOf(file);
        var kind = Detect(prefix, text, legend);

        return kind switch
        {
            CardKind.Doble => ParseDoble(text, answers),
            CardKind.Radio => new[] { ParseRadio(text, answers) },
            CardKind.Tfng => new[] { ParseSelectorTfng(text, answers, yesNo: false) },
            CardKind.Yng => new[] { ParseSelectorTfng(text, answers, yesNo: true) },
            CardKind.MatchingSections => new[] { ParseMatchingSelector(text, answers, legend, code, imagesOutDir, file) },
            CardKind.Headings => new[] { ParseMatchingSelector(text, answers, legend, code, imagesOutDir, file, QuestionType: "MatchingHeadings") },
            CardKind.SentenceEndings => new[] { ParseMatchingSelector(text, answers, legend, code, imagesOutDir, file, QuestionType: "MatchingSentenceEndings") },
            CardKind.Comparison => new[] { ParseMatchingSelector(text, answers, legend, code, imagesOutDir, file, QuestionType: "MatchingFeatures", isComparison: true) },
            CardKind.Table => new[] { ParseTable(text, answers) },
            CardKind.AnketaNotes => new[] { ParseGapBlock(text, answers, "StructuredNotes", "NoteCompletion") },
            CardKind.AnketaSummary => new[] { ParseGapBlock(text, answers, "SummaryFlow", "SummaryCompletion") },
            CardKind.AnketaImage => new[] { ParseAnketaImage(text, answers, legend, code, imagesOutDir, file) },
            CardKind.SentenceCompletion => new[] { ParseSentences(text, answers, "SentenceCompletion") },
            CardKind.ShortAnswer => new[] { ParseSentences(text, answers, "ShortAnswer") },
            CardKind.DiagramLabel => new[] { ParseDiagramLabel(text, answers, legend, code, imagesOutDir, file) },
            _ => throw new InvalidOperationException($"unrecognised card kind for {Path.GetFileName(file)}")
        };
    }

    private static string PrefixOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        // Strip trailing " <number>".
        var m = Regex.Match(name, @"^(.*?)\s+\d+\s*$");
        return (m.Success ? m.Groups[1].Value : name).Trim();
    }

    private static CardKind Detect(string prefix, string text, Legend? legend)
    {
        var low = text.ToLowerInvariant();
        var hasLegend = legend != null || HasInlineLetterList(text);

        // Content signals take priority (handles mislabeled / un-prefixed files).
        // A real grid table uses " | " column separators; a "table" without them is really
        // a sectioned notes block → render as StructuredNotes.
        if (low.Contains("complete the table")) return text.Contains('|') ? CardKind.Table : CardKind.AnketaNotes;
        if (low.Contains("choose two letters")) return CardKind.Doble;
        // Diagram / map / plan labelling — notes-with-gaps over an image (the label box carries the visual).
        if (low.Contains("label the diagram") || low.Contains("label the map") || low.Contains("label the plan"))
            return CardKind.DiagramLabel;
        // Short-answer questions ("Answer the questions below" — each numbered line is a question + a blank).
        if (low.Contains("answer the questions")) return CardKind.ShortAnswer;
        if (low.Contains("complete each sentence with the correct ending")) return CardKind.SentenceEndings;
        // Plain sentence completion ("Complete the sentences below" — gap sits inside each numbered sentence).
        if (low.Contains("complete the sentence")) return CardKind.SentenceCompletion;
        if (Regex.IsMatch(low, @"complete the (summary|notes).*using the list of (?:words|phrases)")) return CardKind.AnketaImage;
        if (low.Contains("complete the notes")) return CardKind.AnketaNotes;
        if (low.Contains("complete the summary")) return CardKind.AnketaSummary;
        if (low.Contains("complete the flow"))   return CardKind.AnketaSummary;
        // Headings must be tested before "sections, A-G", since heading cards mention both
        // "seven sections, A-G" (the passage sections) and "list of headings" (the i-x options).
        if (low.Contains("correct heading") || low.Contains("list of headings")) return CardKind.Headings;
        if (Regex.IsMatch(low, @"\btrue\b") && low.Contains("false") && low.Contains("not given")) return CardKind.Tfng;
        if (Regex.IsMatch(low, @"\byes\b") && Regex.IsMatch(low, @"\bno\b") && low.Contains("not given")) return CardKind.Yng;
        if (low.Contains("which section") || low.Contains("which paragraph") || Regex.IsMatch(low, @"sections?,?\s+[a-z]\s*[-–]")) return CardKind.MatchingSections;
        if (low.Contains("match each") || (low.Contains("list of") && hasLegend)) return CardKind.Comparison;
        if (low.Contains("choose the correct letter")) return CardKind.Radio;

        // Fallback to filename prefix.
        return prefix.ToLowerInvariant() switch
        {
            "table" => CardKind.Table,
            "doble" => CardKind.Doble,
            "radio" => CardKind.Radio,
            "comparison" => CardKind.Comparison,
            "selector_image" => CardKind.SentenceEndings,
            "anketa_image" => CardKind.AnketaImage,
            "selector" => CardKind.Tfng,
            "anketa" => CardKind.AnketaSummary,
            _ => CardKind.AnketaSummary
        };
    }

    private static bool HasInlineLetterList(string text)
    {
        // Trailing block of "A word / B word / ..." lines (≥3 consecutive lettered lines).
        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        var run = 0; var expected = 'A';
        foreach (var l in lines)
        {
            var m = Regex.Match(l, @"^([A-Z])\b");
            if (m.Success && m.Groups[1].Value[0] == expected) { run++; expected++; }
            else if (run > 0 && l.Length == 0) { /* allow blanks */ }
            else { run = m.Success && m.Groups[1].Value == "A" ? 1 : 0; expected = run == 1 ? 'B' : 'A'; }
            if (run >= 3) return true;
        }
        return false;
    }

    // ── Instruction = everything from "Questions ..." down to the first content line. ──
    private static (string instruction, List<string> contentLines, int first, int last) SplitInstruction(string text)
    {
        var lines = text.Split('\n');
        var (first, last) = QuestionRange(text);
        // Content starts at the first numbered item OR the first gap line OR a "|" table row.
        var contentStart = lines.Length;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (i > 0 && (NumberedItem.IsMatch(t) || GapNumbered.IsMatch(t) || t.Contains('|')))
            {
                contentStart = i; break;
            }
        }
        var instruction = string.Join("\n", lines.Take(contentStart)).Trim();
        var content = lines.Skip(contentStart).ToList();
        return (instruction, content, first, last);
    }

    private static (int first, int last) QuestionRange(string text)
    {
        var m = Regex.Match(text, @"Questions?\s+(\d+)\s*(?:[-–]\s*(\d+)|and\s+(\d+))?", RegexOptions.IgnoreCase);
        if (!m.Success) return (0, 0);
        var first = int.Parse(m.Groups[1].Value);
        var last = m.Groups[2].Success ? int.Parse(m.Groups[2].Value)
                 : m.Groups[3].Success ? int.Parse(m.Groups[3].Value)
                 : first;
        return (first, last);
    }

    private static int? WordLimit(string instruction)
    {
        var low = instruction.ToLowerInvariant();
        if (low.Contains("one word")) return 1;
        if (low.Contains("two word")) return 2;
        if (low.Contains("three word")) return 3;
        return null;
    }

    // ── TFNG / YNG selector ──
    private static GroupOut ParseSelectorTfng(string text, Dictionary<int, AnswerSpec> answers, bool yesNo)
    {
        var (instr, content, _, _) = SplitInstruction(text);
        var qs = new List<QOut>();
        foreach (var item in NumberedItems(content))
        {
            var spec = answers.GetValueOrDefault(item.num);
            qs.Add(new QOut
            {
                Order = item.num,
                Type = yesNo ? "YesNoNotGiven" : "TrueFalseNotGiven",
                Stem = item.text,
                AnswerKey = NormalizeTfngAnswer(spec?.Canonical ?? "", yesNo)
            });
        }
        return Group("Selector", instr, qs);
    }

    // Answer keys for TFNG/YNG come in shorthand in some books ("T"/"F"/"NG", "Y"/"N") and with an
    // OCR typo ("NOTE GIVEN"). The card's buttons compare against the full words TRUE/FALSE/NOT GIVEN
    // (or YES/NO/NOT GIVEN), so expand them here — but only inside the TFNG/YNG parser, where a bare
    // "F" unambiguously means FALSE (in a matching card "F" is a real option letter, left untouched).
    private static string NormalizeTfngAnswer(string ans, bool yesNo)
    {
        var a = Regex.Replace(ans.Trim().ToUpperInvariant(), @"\s+", " ");
        if (a is "NG" or "N/G" or "NOT GIVEN" or "NOTE GIVEN") return "NOT GIVEN";
        if (yesNo)
        {
            if (a is "Y" or "YES") return "YES";
            if (a is "N" or "NO") return "NO";
        }
        else
        {
            if (a is "T" or "TRUE") return "TRUE";
            if (a is "F" or "FALSE") return "FALSE";
        }
        return ans;   // unexpected value — leave as-is rather than guess
    }

    // ── Matching to letters (sections / sentence endings / comparison) ──
    private static GroupOut ParseMatchingSelector(string text, Dictionary<int, AnswerSpec> answers, Legend? legend,
        string code, string imagesOutDir, string file, string QuestionType = "MatchingInformation", bool isComparison = false)
    {
        var (instr, content, _, _) = SplitInstruction(text);
        var qs = new List<QOut>();
        foreach (var item in NumberedItems(content))
        {
            var spec = answers.GetValueOrDefault(item.num);
            qs.Add(new QOut { Order = item.num, Type = QuestionType, Stem = item.text, AnswerKey = spec?.Canonical ?? "" });
        }

        var (sharedOptions, listTitle, imagePath) = BuildLegend(text, legend, instr, code, imagesOutDir, file);
        var g = Group("Selector", instr, qs);
        g.SharedOptions = sharedOptions;
        g.SharedListTitle = listTitle;
        g.ImagePath = imagePath;
        return g;
    }

    // ── Doble: split into per-subblock groups, each with 2 MatchingFeatures questions ──
    private static IEnumerable<GroupOut> ParseDoble(string text, Dictionary<int, AnswerSpec> answers)
    {
        var lines = text.Split('\n');
        // Split into subblocks on each "Questions X and Y".
        var blocks = new List<List<string>>();
        List<string>? cur = null;
        foreach (var l in lines)
        {
            if (Regex.IsMatch(l, @"Questions?\s+\d+\s+and\s+\d+", RegexOptions.IgnoreCase))
            {
                cur = new List<string>();
                blocks.Add(cur);
            }
            cur?.Add(l);
        }
        if (blocks.Count == 0) blocks.Add(lines.ToList());

        foreach (var block in blocks)
        {
            var btext = string.Join("\n", block);
            var (first, last) = QuestionRange(btext);
            // The question prompt = lines after instruction, before option lines.
            var options = new List<string>();
            string? prompt = null;
            foreach (var raw in block)
            {
                var t = raw.Trim();
                var om = OptionLine.Match(t);
                if (om.Success && om.Groups[1].Value.Length == 1 && char.IsUpper(om.Groups[1].Value[0]))
                    options.Add($"{om.Groups[1].Value}. {om.Groups[2].Value.Trim()}");
                else if (t.Length > 0 && t.EndsWith('?')) prompt = t;
            }
            var instr = prompt ?? "Choose TWO letters.";

            var qs = new List<QOut>();
            var labels = new[] { "First answer", "Second answer" };
            var n = 0;
            for (var num = first; num <= last && num > 0; num++, n++)
            {
                var spec = answers.GetValueOrDefault(num);
                qs.Add(new QOut
                {
                    Order = num,
                    Type = "MatchingFeatures",
                    Stem = n < labels.Length ? labels[n] : $"Answer {n + 1}",
                    Options = options.Count > 0 ? new List<string>(options) : null,
                    AnswerKey = spec?.Canonical ?? ""
                });
            }
            yield return Group("FlatList", instr, qs);
        }
    }

    // ── Radio: single-choice, per-question A/B/C/D ──
    private static GroupOut ParseRadio(string text, Dictionary<int, AnswerSpec> answers)
    {
        var (instr, content, _, _) = SplitInstruction(text);
        var qs = new List<QOut>();
        QOut? current = null;
        var opts = new List<string>();
        void Flush()
        {
            if (current != null) { current.Options = new List<string>(opts); qs.Add(current); }
            opts = new List<string>();
        }
        foreach (var raw in content)
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;
            var nm = NumberedItem.Match(t);
            var om = OptionLine.Match(t);
            // An option line ("A ....") only counts when we're inside a question.
            if (om.Success && current != null && om.Groups[1].Value.Length == 1)
            {
                opts.Add(om.Groups[2].Value.Trim());   // pure text — ChoiceSingle tags by index
            }
            else if (nm.Success)
            {
                Flush();
                var num = int.Parse(nm.Groups[1].Value);
                var spec = answers.GetValueOrDefault(num);
                current = new QOut { Order = num, Type = "MultipleChoiceSingle", Stem = nm.Groups[2].Value.Trim(), AnswerKey = spec?.Canonical ?? "" };
            }
            else if (current != null && opts.Count > 0)
            {
                // continuation of the previous option (wrapped line)
                opts[^1] = (opts[^1] + " " + t).Trim();
            }
            else if (current != null)
            {
                current.Stem = (current.Stem + " " + t).Trim();
            }
        }
        Flush();
        return Group("FlatList", instr, qs);
    }

    // ── Table ──
    private static GroupOut ParseTable(string text, Dictionary<int, AnswerSpec> answers)
    {
        var (instr, content, _, _) = SplitInstruction(text);
        var limit = WordLimit(instr);

        // Title = first non-empty content line that has no "|" and no gap.
        string? title = null;
        var rows = new List<List<string>>();
        var multiBlank = new HashSet<int>();
        foreach (var raw in content)
        {
            var t = raw.TrimEnd();
            if (t.Trim().Length == 0) continue;
            if (t.Contains('|'))
            {
                var cells = t.Split('|').Select(c => c.Trim()).ToList();
                for (var i = 0; i < cells.Count; i++) cells[i] = ConvertGaps(cells[i], multiBlank);
                rows.Add(cells);
            }
            else if (title == null && !GapNumbered.IsMatch(t)) title = t.Trim();
        }

        // Treat the first row as a header ONLY when it carries no answer gap. A genuine header
        // (e.g. "Part of tree | Traditional use") never contains a "{N}" blank, whereas a
        // header-less timeline table (e.g. "Middle Ages | … {8}") has an answer in its first row —
        // promoting that to a bold header would mislabel real data as a column title.
        var hasHeader = rows.Count > 0 && !rows[0].Any(c => c.Contains('{'));
        var columns = hasHeader ? rows[0] : null;
        var dataRows = hasHeader ? rows.Skip(1).ToList() : rows;

        var tableJson = JsonSerializer.Serialize(new
        {
            Title = title,
            Columns = columns,
            Rows = dataRows
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var qs = BuildGapQuestions(content, answers, "TableCompletion", limit, multiBlank);
        var g = Group("Table", instr, qs);
        g.SummaryTemplate = tableJson;
        return g;
    }

    // ── Gap block: notes (StructuredNotes) / summary (SummaryFlow) ──
    private static GroupOut ParseGapBlock(string text, Dictionary<int, AnswerSpec> answers, string layout, string qType)
    {
        var (instr, content, _, _) = SplitInstruction(text);
        var limit = WordLimit(instr);
        var multiBlank = new HashSet<int>();

        var sb = new StringBuilder();
        foreach (var raw in content)
        {
            var line = ConvertGaps(raw.TrimEnd(), multiBlank);
            if (layout == "StructuredNotes")
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- ")) sb.AppendLine(trimmed);
                else if (trimmed.Length == 0) sb.AppendLine();
                else if (!trimmed.Contains('{') && sb.Length == 0) sb.AppendLine("# " + trimmed); // title
                else sb.AppendLine(trimmed);
            }
            else
            {
                sb.Append(line).Append(' ');
            }
        }

        var qs = BuildGapQuestions(content, answers, qType, limit, multiBlank);
        var g = Group(layout, instr, qs);
        g.SummaryTemplate = layout == "SummaryFlow"
            ? Regex.Replace(sb.ToString(), @"\s+", " ").Trim()
            : sb.ToString().TrimEnd();
        return g;
    }

    // ── Anketa_Image: flowing/notes text with letter-dropdown gaps + legend ──
    private static GroupOut ParseAnketaImage(string text, Dictionary<int, AnswerSpec> answers, Legend? legend,
        string code, string imagesOutDir, string file)
    {
        var (instr, content, _, _) = SplitInstruction(text);
        var multiBlank = new HashSet<int>();

        var sb = new StringBuilder();
        foreach (var raw in content)
        {
            var line = ConvertGaps(raw.TrimEnd(), multiBlank);
            // Stop accumulating once we hit the inline legend (a run of "A word" lines).
            sb.Append(line).Append(' ');
        }
        var template = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        // Remove any inline legend that leaked into the template (letters list with no gaps).
        template = StripInlineLegend(template);

        var qs = new List<QOut>();
        foreach (var num in GapNumbersInOrder(content))
        {
            var spec = answers.GetValueOrDefault(num);
            qs.Add(new QOut { Order = num, Type = "MatchingFeatures", Stem = "", AnswerKey = spec?.Canonical ?? "" });
        }

        var (sharedOptions, listTitle, imagePath) = BuildLegend(text, legend, instr, code, imagesOutDir, file);
        var g = Group("AnketaImage", instr, qs);
        g.SummaryTemplate = template;
        g.SharedOptions = sharedOptions;
        g.SharedListTitle = listTitle;
        g.ImagePath = imagePath;
        return g;
    }

    // ── Sentence completion / short answer: one numbered line = one question (FlatList) ──
    // SentenceCompletion: the gap is inside the sentence ("Daffodils flower early in ...... weather")
    //   → keep the sentence as the stem with the blank shown as the "…" marker existing stems use.
    // ShortAnswer: the line is a question with a trailing blank ("Which body …? ......")
    //   → drop the trailing blank, the answer is typed into a free field.
    private static GroupOut ParseSentences(string text, Dictionary<int, AnswerSpec> answers, string qType)
    {
        var (instr, content, _, _) = SplitInstruction(text);
        var limit = WordLimit(instr);
        var qs = new List<QOut>();
        foreach (var item in NumberedItems(content))
        {
            var spec = answers.GetValueOrDefault(item.num);
            var stem = qType == "ShortAnswer"
                ? Regex.Replace(item.text, @"[\s.]*\.{3,}[\s.]*$", "").Trim()   // strip trailing blank
                : BlankToEllipsis(item.text);                                    // blank → "…" in place
            qs.Add(new QOut
            {
                Order = item.num,
                Type = qType,
                Stem = stem,
                AnswerKey = spec?.Canonical ?? "",
                AcceptableAnswers = spec is { Acceptable.Count: > 0 } ? spec.Acceptable.ToList() : null,
                WordLimitMax = limit
            });
        }
        return Group("FlatList", instr, qs);
    }

    // ── Diagram / map / plan labelling: numbered gaps in notes over an image (MapLabeling) ──
    // Each gap line becomes a SentenceCompletion-style stem ("… to direct the tunnelling") with the
    // diagram image carrying the spatial context; non-gap lines are section captions, folded into the
    // next question's stem so the grouping (e.g. "Persian Qanat" vs "Roman Qanat") isn't lost.
    private static GroupOut ParseDiagramLabel(string text, Dictionary<int, AnswerSpec> answers, Legend? legend,
        string code, string imagesOutDir, string file)
    {
        var (instr, content, _, _) = SplitInstruction(text);
        var limit = WordLimit(instr);
        var qs = new List<QOut>();
        string? caption = null;
        foreach (var raw in content)
        {
            var line = raw.Trim().TrimStart('-', '•', '*').Trim();
            if (line.Length == 0) continue;

            var gm = GapNumbered.Match(line);
            if (gm.Success)
            {
                var num = int.Parse(gm.Groups[1].Value, CultureInfo.InvariantCulture);
                var stem = GapNumbered.Replace(line, "…", 1);
                stem = Regex.Replace(stem, @"\s+", " ").Trim();
                if (caption != null) { stem = $"{caption}: {stem}"; caption = null; }
                var spec = answers.GetValueOrDefault(num);
                qs.Add(new QOut
                {
                    Order = num,
                    Type = "DiagramLabeling",
                    Stem = stem,
                    AnswerKey = spec?.Canonical ?? "",
                    AcceptableAnswers = spec is { Acceptable.Count: > 0 } ? spec.Acceptable.ToList() : null,
                    WordLimitMax = limit
                });
            }
            else
            {
                caption = line;   // a section heading between the gap rows
            }
        }

        var (_, _, imagePath) = BuildLegend(text, legend, instr, code, imagesOutDir, file);
        var g = Group("MapLabeling", instr, qs.OrderBy(q => q.Order).ToList());
        g.ImagePath = imagePath;
        return g;
    }

    // Replace a dotted blank run with the "…" gap marker existing SentenceCompletion stems use,
    // and tidy a dangling sentence period that follows a trailing blank.
    private static string BlankToEllipsis(string s)
    {
        s = GapBare.Replace(s, "…");
        s = Regex.Replace(s, @"…[ \t]*\.\s*$", "…");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    // ── Shared helpers ──

    private static IEnumerable<(int num, string text)> NumberedItems(IEnumerable<string> content)
    {
        (int num, StringBuilder sb)? cur = null;
        foreach (var raw in content)
        {
            var t = raw.Trim();
            var m = NumberedItem.Match(t);
            if (m.Success)
            {
                if (cur != null) yield return (cur.Value.num, cur.Value.sb.ToString().Trim());
                cur = (int.Parse(m.Groups[1].Value), new StringBuilder(m.Groups[2].Value));
            }
            else if (cur != null && t.Length > 0)
            {
                cur.Value.sb.Append(' ').Append(t);
            }
        }
        if (cur != null) yield return (cur.Value.num, cur.Value.sb.ToString().Trim());
    }

    // Convert "N ........." → "{N}"; collapse extra bare blanks into the same number (multi-blank).
    private static string ConvertGaps(string line, HashSet<int> multiBlank)
    {
        var lastNum = 0;
        var result = GapNumbered.Replace(line, m =>
        {
            lastNum = int.Parse(m.Groups[1].Value);
            return "{" + lastNum + "}";
        });
        // Any remaining bare "...." belongs to the most recent numbered gap (e.g. "{7} and ....").
        if (GapBare.IsMatch(result) && lastNum > 0)
        {
            multiBlank.Add(lastNum);
            result = GapBare.Replace(result, "");
            result = Regex.Replace(result, @"\s+and\s+(?=[|,.]|$)", " ");  // tidy dangling "and"
        }
        return result;
    }

    private static IEnumerable<int> GapNumbersInOrder(IEnumerable<string> content)
    {
        var seen = new HashSet<int>();
        foreach (var raw in content)
            foreach (Match m in GapNumbered.Matches(raw))
            {
                var n = int.Parse(m.Groups[1].Value);
                if (seen.Add(n)) yield return n;
            }
    }

    private static List<QOut> BuildGapQuestions(IEnumerable<string> content, Dictionary<int, AnswerSpec> answers,
        string qType, int? limit, HashSet<int> multiBlank)
    {
        var qs = new List<QOut>();
        foreach (var num in GapNumbersInOrder(content))
        {
            var spec = answers.GetValueOrDefault(num);
            var multi = multiBlank.Contains(num);
            var acceptable = spec?.Acceptable.ToList() ?? new List<string>();
            if (multi && spec != null)
            {
                // "leaves (and) bark" → accept either order, with/without connective.
                acceptable.AddRange(BothOrders(spec.Canonical));
                acceptable = acceptable.Distinct(StringComparer.OrdinalIgnoreCase).Where(a => !a.Equals(spec.Canonical, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            qs.Add(new QOut
            {
                Order = num,
                Type = qType,
                Stem = "",
                AnswerKey = spec?.Canonical ?? "",
                AcceptableAnswers = acceptable.Count > 0 ? acceptable : null,
                WordLimitMax = multi ? null : limit
            });
        }
        return qs;
    }

    private static IEnumerable<string> BothOrders(string canonical)
    {
        // "leaves and bark" → also "bark and leaves", "leaves bark", "bark leaves".
        var parts = Regex.Split(canonical, @"\s+and\s+|\s+", RegexOptions.IgnoreCase)
            .Where(p => p.Length > 0 && !p.Equals("and", StringComparison.OrdinalIgnoreCase)).ToList();
        if (parts.Count == 2)
        {
            yield return $"{parts[0]} and {parts[1]}";
            yield return $"{parts[1]} and {parts[0]}";
            yield return $"{parts[0]} {parts[1]}";
            yield return $"{parts[1]} {parts[0]}";
        }
    }

    // Build SharedOptions/list-title/image from a card's legend (file) or inline letter list.
    private static (List<string>? options, string? title, string? image) BuildLegend(
        string text, Legend? legend, string instruction, string code, string imagesOutDir, string file)
    {
        string? listTitle = null;
        var options = new List<string>();
        string? legendBody = legend?.Text;

        if (legendBody == null)
        {
            // Inline legend: trailing block of lettered lines.
            legendBody = ExtractInlineLegend(text);
        }

        if (legendBody != null)
        {
            foreach (var raw in legendBody.Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                var m = LegendItem.Match(t);
                if (m.Success) options.Add($"{m.Groups[1].Value}. {m.Groups[2].Value.Trim()}");
                else if (options.Count == 0) listTitle = t;   // a heading like "List of Explorers"
            }
        }

        if (options.Count == 0)
        {
            // No legend list — derive bare letters from the instruction range, e.g. "A-G".
            var rm = Regex.Match(instruction, @"([A-Z])\s*[-–]\s*([A-Z])");
            if (rm.Success)
            {
                for (var c = rm.Groups[1].Value[0]; c <= rm.Groups[2].Value[0]; c++)
                    options.Add(c.ToString());
            }
        }

        // Copy the legend image for embedding.
        string? imageRel = null;
        if (legend?.PngPath != null)
        {
            imageRel = SafeImageName(file);
            var dest = Path.Combine(imagesOutDir, $"{code}.{imageRel}");
            File.Copy(legend.PngPath, dest, overwrite: true);
        }

        return (options.Count > 0 ? options : null, listTitle, imageRel);
    }

    private static string SafeImageName(string file)
    {
        var baseName = Path.GetFileNameWithoutExtension(file);
        var ascii = Regex.Replace(baseName.ToLowerInvariant(), @"[^a-z0-9]+", "");
        return $"{ascii}.png";
    }

    private static string? ExtractInlineLegend(string text)
    {
        var lines = text.Split('\n');
        // Find the last run of consecutive "A.. B.. C.." lines.
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i].Trim(), @"^A\b") )
            {
                // verify B follows somewhere shortly
                start = i;
            }
        }
        if (start < 0) return null;
        var collected = new List<string>();
        var expected = 'A';
        for (var i = start; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0) { if (collected.Count > 0) break; else continue; }
            var m = Regex.Match(t, @"^([A-Z])\b");
            if (m.Success && m.Groups[1].Value[0] == expected) { collected.Add(t); expected++; }
            else break;
        }
        return collected.Count >= 2 ? string.Join("\n", collected) : null;
    }

    private static string StripInlineLegend(string template)
    {
        // Remove a trailing "A word B word ..." sequence that may have leaked into the flow text.
        return Regex.Replace(template, @"(\s[A-Z]\s+\w+){4,}\s*$", "").Trim();
    }

    private static GroupOut Group(string layout, string instr, List<QOut> qs) => new()
    {
        Layout = layout,
        Instruction = instr,
        Questions = qs,
        FirstQuestion = qs.Count > 0 ? qs.Min(q => q.Order) : 0
    };
}

enum CardKind
{
    Tfng, Yng, MatchingSections, Headings, SentenceEndings, Comparison,
    Doble, Radio, AnketaNotes, AnketaSummary, AnketaImage, Table,
    SentenceCompletion, ShortAnswer, DiagramLabel
}
