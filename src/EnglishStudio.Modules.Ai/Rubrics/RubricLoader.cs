using System.IO;
using EnglishStudio.Modules.Dictionary.Data;

namespace EnglishStudio.Modules.Ai.Rubrics;

/// <summary>
/// Loads the IELTS rubric markdown files from the imported content-pack
/// (<c>%AppData%\EnglishStudio\IeltsContent\Rubrics\</c>). The rubrics are derived from the official
/// IELTS public band descriptors — copyright content shipped via the pack, NOT embedded in the
/// assembly. Returns <c>null</c> when the pack hasn't been imported (AI grading is only reachable
/// after the relevant Writing/Speaking content — part of the same pack — has been imported).
/// </summary>
public static class RubricLoader
{
    public const string RubricsFolder = "Rubrics";
    public const string WritingFileName = "IeltsRubric_Writing.md";
    public const string SpeakingFileName = "IeltsRubric_Speaking.md";

    /// <summary>Writing band descriptors, or null if the content-pack isn't imported.</summary>
    public static string? Writing => Read(WritingFileName);

    /// <summary>Speaking band descriptors, or null if the content-pack isn't imported.</summary>
    public static string? Speaking => Read(SpeakingFileName);

    /// <summary>True when both rubric files are present under the imported content root.</summary>
    public static bool IsAvailable =>
        File.Exists(PathFor(WritingFileName)) && File.Exists(PathFor(SpeakingFileName));

    private static string PathFor(string fileName) =>
        Path.Combine(DictionaryPaths.IeltsContentRoot, RubricsFolder, fileName);

    private static string? Read(string fileName)
    {
        var p = PathFor(fileName);
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }
}
