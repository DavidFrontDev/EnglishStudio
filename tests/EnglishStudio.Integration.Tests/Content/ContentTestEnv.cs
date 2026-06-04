using System.IO;
using EnglishStudio.Modules.Dictionary.Data;

namespace EnglishStudio.Integration.Tests.Content;

/// <summary>
/// Creates a throwaway %AppData% root and points <see cref="DictionaryPaths.AppDataRootOverride"/>
/// at it for the lifetime of the object; resets the override and deletes the folder on Dispose.
/// All consumers live in the non-parallel "ContentIO" collection (the override is a global static).
/// </summary>
public sealed class ContentTestEnv : IDisposable
{
    public string Root { get; }

    /// <summary>%AppData-root%/IeltsContent — what IContentStore.ContentRoot resolves to here.</summary>
    public string ContentRoot => DictionaryPaths.IeltsContentRoot;

    public ContentTestEnv()
    {
        Root = Path.Combine(Path.GetTempPath(), "es-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DictionaryPaths.AppDataRootOverride = Root;
        DictionaryPaths.EnsureDirectoriesExist();
    }

    /// <summary>Writes <paramref name="text"/> to ContentRoot/&lt;moduleFolder&gt;/&lt;relative&gt;.</summary>
    public string WriteContentFile(string moduleFolder, string relative, string text)
    {
        var path = Path.Combine(ContentRoot, moduleFolder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Builds a separate content-pack folder (NOT under ContentRoot) for import tests.</summary>
    public string NewPackDir()
    {
        var dir = Path.Combine(Root, "pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        DictionaryPaths.AppDataRootOverride = null;
        try { Directory.Delete(Root, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}

/// <summary>Minimal-but-valid pack JSON fixtures (one item per section).</summary>
internal static class PackFixtures
{
    public const string ReadingJson = """
    [
      {
        "code": "test-reading-1",
        "title": "Mini Reading Test",
        "mode": "Academic",
        "attribution": null,
        "parts": [
          {
            "order": 1, "title": "Part 1", "body": "Some passage text.", "introNoteRu": null,
            "groups": [
              {
                "order": 1, "layout": "FlatList", "instruction": "Decide.",
                "sharedOptions": null, "sharedListTitle": null, "imagePath": null,
                "exampleStem": null, "exampleAnswer": null, "summaryTemplate": null,
                "questions": [
                  { "order": 1, "type": "TrueFalseNotGiven", "stem": "The sky is blue.",
                    "options": null, "answerKey": "True", "acceptableAnswers": null,
                    "points": 1, "wordLimitMax": null }
                ]
              }
            ]
          }
        ]
      }
    ]
    """;

    public const string ListeningJson = """
    [
      {
        "code": "test-listening-1",
        "title": "Mini Listening Test",
        "attribution": null,
        "parts": [
          {
            "order": 1, "title": "Part 1", "audioFile": null, "introNoteRu": null,
            "groups": [
              {
                "order": 1, "layout": "FlatList", "instruction": "Answer.",
                "sharedOptions": null, "sharedListTitle": null, "imagePath": null,
                "exampleStem": null, "exampleAnswer": null, "summaryTemplate": null,
                "questions": [
                  { "order": 1, "type": "ShortAnswer", "stem": "What animal?",
                    "options": null, "answerKey": "cat", "acceptableAnswers": null,
                    "points": 1, "wordLimitMax": 1 }
                ]
              }
            ]
          }
        ]
      }
    ]
    """;

    public const string WritingJson = """
    [
      {
        "code": "test-writing-1",
        "title": "Mini Writing Test",
        "attribution": null,
        "task1": { "code": "test-writing-1-t1", "kind": "Task1Academic",
                   "promptText": "Describe the chart.", "imageFile": null,
                   "minWords": 150, "recommendedMinutes": 20, "chartType": "None", "modelAnswers": [] },
        "task2": { "code": "test-writing-1-t2", "kind": "Task2",
                   "promptText": "Discuss both views.", "imageFile": null,
                   "minWords": 250, "recommendedMinutes": 40, "modelAnswers": [] }
      }
    ]
    """;

    public const string OxfordJson = """
    {
      "schemaVersion": 1,
      "words": [
        { "headword": "apple", "pos": "n", "cefr": "A1", "definitionEn": "a round fruit", "exampleEn": "An apple a day." },
        { "headword": "run",   "pos": "v", "cefr": "A2", "definitionEn": "to move quickly", "exampleEn": "I run daily." }
      ]
    }
    """;

    public const string PhaveJson = """
    {
      "SchemaVersion": 1,
      "Entries": [
        { "Rank": 1, "Phrase": "give up",
          "Senses": [ { "Num": 1, "Particle": "up", "DefinitionEn": "to stop trying", "ExampleEn": "Don't give up." } ] }
      ]
    }
    """;

    public const string RubricWritingMd = "# Writing rubric (test)\nBand 9: fully satisfies the task.\n";
    public const string RubricSpeakingMd = "# Speaking rubric (test)\nBand 9: speaks fluently.\n";
}
