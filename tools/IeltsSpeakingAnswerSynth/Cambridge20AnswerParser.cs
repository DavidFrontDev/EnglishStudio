using System.Text;
using System.Text.RegularExpressions;
using EnglishStudio.Modules.Ielts.Speaking.Cambridge;

namespace EnglishStudio.IeltsSpeakingAnswerSynth;

public sealed class Cambridge20AnswerParser
{
    private static readonly Regex NumberedParagraph = new(@"^\s*\d+\.\s+", RegexOptions.Compiled);

    public List<ParsedAnswer> Parse(string answerPath, IReadOnlyList<CambridgeSpeakingTest> tests)
    {
        var paragraphs = ReadParagraphs(answerPath);
        var sections = ParseSections(paragraphs, tests);
        var results = new List<ParsedAnswer>();

        foreach (var test in tests.OrderBy(t => t.TestNumber))
        {
            AddPart1(results, test, FindSection(sections, test.TestNumber, 1));
            AddPart2(results, test, FindSection(sections, test.TestNumber, 2));
            AddPart3(results, test, FindSection(sections, test.TestNumber, 3));
        }

        return results;
    }

    private static void AddPart1(List<ParsedAnswer> results, CambridgeSpeakingTest test, Section? section)
    {
        if (section is null) return;
        var answers = SplitNumberedAnswers(section.Paragraphs);
        var count = Math.Min(test.Part1.Questions.Count, answers.Count);
        for (var i = 0; i < count; i++)
        {
            results.Add(new ParsedAnswer(test.Book, test.TestNumber, 1, i + 1, test.Part1.Questions[i], answers[i]));
        }
    }

    private static void AddPart2(List<ParsedAnswer> results, CambridgeSpeakingTest test, Section? section)
    {
        if (section is null || section.Paragraphs.Count == 0) return;
        var answer = JoinParagraphs(section.Paragraphs);
        if (!string.IsNullOrWhiteSpace(answer))
        {
            results.Add(new ParsedAnswer(test.Book, test.TestNumber, 2, 1, test.Part2.CueCardPrompt, answer));
        }
    }

    private static void AddPart3(List<ParsedAnswer> results, CambridgeSpeakingTest test, Section? section)
    {
        if (section is null) return;
        var answers = SplitNumberedAnswers(section.Paragraphs);
        var questions = test.Part3.Subtopics.SelectMany(s => s.Questions).ToList();
        var count = Math.Min(questions.Count, answers.Count);
        for (var i = 0; i < count; i++)
        {
            results.Add(new ParsedAnswer(test.Book, test.TestNumber, 3, i + 1, questions[i], answers[i]));
        }
    }

    private static Section? FindSection(IEnumerable<Section> sections, int testNumber, int part) =>
        sections.LastOrDefault(s => s.TestNumber == testNumber && s.Part == part);

    private static List<Section> ParseSections(IReadOnlyList<string> paragraphs, IReadOnlyList<CambridgeSpeakingTest> tests)
    {
        var sections = new List<Section>();
        var current = new List<string>();
        var testNumber = 1;
        int? part = null;
        var answerPending = false;
        var part1NumberedCount = 0;

        void Flush()
        {
            if (part is not null && current.Count > 0)
            {
                sections.Add(new Section(testNumber, part.Value, current.ToList()));
            }
            current.Clear();
            part = null;
            part1NumberedCount = 0;
        }

        foreach (var paragraph in paragraphs)
        {
            var headingPart = TryParsePartHeading(paragraph);
            if (headingPart is not null)
            {
                var startsNextTest = headingPart == 1
                    && (part == 3 || sections.Any(s => s.TestNumber == testNumber && s.Part == 3));
                Flush();
                if (startsNextTest)
                {
                    testNumber++;
                }
                part = headingPart.Value;
                answerPending = false;
                continue;
            }

            if (IsAnswerHeading(paragraph))
            {
                Flush();
                if (sections.Any(s => s.TestNumber == testNumber && s.Part == 3))
                {
                    testNumber++;
                }
                answerPending = true;
                continue;
            }

            if (part is null)
            {
                if (!answerPending) continue;
                part = 1;
                answerPending = false;
            }

            if (part == 1
                && part1NumberedCount >= ExpectedPart1Count(tests, testNumber)
                && !NumberedParagraph.IsMatch(paragraph))
            {
                Flush();
                part = 2;
            }

            current.Add(paragraph);
            if (part == 1 && NumberedParagraph.IsMatch(paragraph))
            {
                part1NumberedCount++;
            }
        }

        Flush();
        return sections;
    }

    private static int ExpectedPart1Count(IReadOnlyList<CambridgeSpeakingTest> tests, int testNumber) =>
        tests.FirstOrDefault(t => t.TestNumber == testNumber)?.Part1.Questions.Count ?? 4;

    private static int? TryParsePartHeading(string paragraph)
    {
        var s = paragraph.Trim().TrimEnd(':').ToLowerInvariant();
        return s switch
        {
            "p1 answering ideas" or "part1" or "p1" => 1,
            "p2 answering ideas" or "part2" or "p2" => 2,
            "p3 answering ideas" or "part3" or "p3" => 3,
            _ => null
        };
    }

    private static bool IsAnswerHeading(string paragraph) =>
        string.Equals(paragraph.Trim(), "Answer:", StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitNumberedAnswers(IReadOnlyList<string> paragraphs)
    {
        var answers = new List<string>();
        var current = new List<string>();

        foreach (var p in paragraphs)
        {
            if (NumberedParagraph.IsMatch(p))
            {
                if (current.Count > 0)
                {
                    answers.Add(JoinParagraphs(current));
                    current.Clear();
                }
                current.Add(NumberedParagraph.Replace(p, "", 1).Trim());
            }
            else if (current.Count > 0)
            {
                current.Add(p.Trim());
            }
        }

        if (current.Count > 0)
        {
            answers.Add(JoinParagraphs(current));
        }

        return answers;
    }

    private static List<string> ReadParagraphs(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var paragraphs = new List<string>();
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Count > 0)
                {
                    paragraphs.Add(string.Join(Environment.NewLine, current).Trim());
                    current.Clear();
                }
                continue;
            }

            current.Add(line.TrimEnd());
        }

        if (current.Count > 0)
        {
            paragraphs.Add(string.Join(Environment.NewLine, current).Trim());
        }

        return paragraphs;
    }

    private static string JoinParagraphs(IEnumerable<string> paragraphs) =>
        string.Join(Environment.NewLine + Environment.NewLine,
            paragraphs.Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)));

    private sealed record Section(int TestNumber, int Part, List<string> Paragraphs);
}
