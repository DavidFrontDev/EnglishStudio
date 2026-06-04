using System.IO;
using System.Text;

namespace EnglishStudio.Modules.Ielts.Speaking.Cambridge;

/// <summary>
/// Parses a single Cambridge IELTS Speaking test from the user's local .txt file.
/// The canonical format (after normalisation across books 15–20) uses fixed markers:
/// PART 1 / EXAMPLE / PART 2 / You should say: / PART 3 / Discussion topics:.
/// </summary>
public sealed class CambridgeSpeakingTestParser
{
    public CambridgeSpeakingTest Parse(int book, int testNumber, string filePath)
    {
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);

        var p1 = FindLine(lines, 0, "PART 1");
        var p2 = FindLine(lines, p1 + 1, "PART 2");
        var p3 = FindLine(lines, p2 + 1, "PART 3");

        if (p1 < 0 || p2 < 0 || p3 < 0)
            throw new InvalidDataException(
                $"Speaking test file is missing PART headers: {filePath}");

        var part1 = ParsePart1(lines, p1, p2);
        var part2 = ParsePart2(lines, p2, p3);
        var part3 = ParsePart3(lines, p3, lines.Length);

        return new CambridgeSpeakingTest(book, testNumber, part1, part2, part3);
    }

    private static CambridgePart1 ParsePart1(string[] lines, int start, int end)
    {
        // After the EXAMPLE marker, the first non-blank line is the sub-topic header,
        // then questions follow as "- "-prefixed lines.
        var ex = FindLine(lines, start, "EXAMPLE", end);
        var i = (ex >= 0 ? ex : start) + 1;

        while (i < end && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= end) return new CambridgePart1("Part 1", Array.Empty<string>());

        var topic = lines[i++].Trim();
        var questions = new List<string>();
        for (; i < end; i++)
        {
            var l = lines[i].Trim();
            if (l.StartsWith("- "))
                questions.Add(l[2..].Trim());
            else if (l.StartsWith("-"))
                questions.Add(l[1..].Trim());
        }
        return new CambridgePart1(topic, questions);
    }

    private static CambridgePart2 ParsePart2(string[] lines, int start, int end)
    {
        // First non-blank line after PART 2 = cue card prompt.
        var i = start + 1;
        while (i < end && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= end) return new CambridgePart2("", Array.Empty<string>());
        var prompt = lines[i++].Trim();

        // Look for "You should say:" — bullets follow until the next blank line.
        var ysy = FindLine(lines, i, "You should say", end);
        var bullets = new List<string>();
        if (ysy >= 0)
        {
            i = ysy + 1;
            for (; i < end; i++)
            {
                var l = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(l)) break;
                if (l.StartsWith("- ")) l = l[2..].Trim();
                else if (l.StartsWith("-")) l = l[1..].Trim();
                bullets.Add(l);
            }

            // Optional closing sentence ("and explain why…") on the next non-blank line.
            while (i < end && string.IsNullOrWhiteSpace(lines[i])) i++;
            if (i < end)
            {
                var closing = lines[i].Trim();
                if (closing.Length > 0
                    && !closing.StartsWith("You will have to", StringComparison.OrdinalIgnoreCase)
                    && !closing.StartsWith("PART ", StringComparison.OrdinalIgnoreCase))
                {
                    bullets.Add(closing);
                }
            }
        }
        return new CambridgePart2(prompt, bullets);
    }

    private static CambridgePart3 ParsePart3(string[] lines, int start, int end)
    {
        // Sections separated by blank lines after "Discussion topics:" —
        // first line of each section = sub-topic header, following non-blanks = questions.
        var dt = FindLine(lines, start, "Discussion topics", end);
        var i = (dt >= 0 ? dt : start) + 1;
        var subtopics = new List<CambridgePart3Subtopic>();

        while (i < end)
        {
            while (i < end && string.IsNullOrWhiteSpace(lines[i])) i++;
            if (i >= end) break;
            var header = lines[i++].Trim();

            while (i < end && string.IsNullOrWhiteSpace(lines[i])) i++;

            var questions = new List<string>();
            while (i < end && !string.IsNullOrWhiteSpace(lines[i]))
            {
                var question = lines[i++].Trim();
                if (question.StartsWith("Example questions", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                questions.Add(question);
            }

            if (questions.Count > 0)
                subtopics.Add(new CambridgePart3Subtopic(header, questions));
        }
        return new CambridgePart3(subtopics);
    }

    private static int FindLine(string[] lines, int from, string prefix, int max = -1)
    {
        if (max < 0) max = lines.Length;
        for (var i = from; i < max; i++)
        {
            if (lines[i].TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
