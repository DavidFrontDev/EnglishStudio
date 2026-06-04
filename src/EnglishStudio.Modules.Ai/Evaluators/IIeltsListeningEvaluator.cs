using EnglishStudio.Modules.Ai.Reports;

namespace EnglishStudio.Modules.Ai.Evaluators;

/// <summary>One part of a completed Listening attempt — the transcript + the user's answers vs. the key for the questions in that part.</summary>
public sealed record ListeningPartContext(
    int PartNumber,
    string PartTitle,
    string Transcript,
    IReadOnlyList<ListeningQuestionContext> Questions);

/// <summary>One question in a part — user's answer vs. the official key, plus question type for AI to reason about patterns.</summary>
public sealed record ListeningQuestionContext(
    int QuestionNumber,
    string QuestionType,        // e.g. "NoteCompletion", "MultipleChoiceSingle", "MatchingFeatures"
    string Stem,
    string UserAnswer,
    string CorrectAnswer,
    bool IsCorrect);

public interface IIeltsListeningEvaluator
{
    /// <summary>
    /// Score / explain a completed IELTS Listening attempt through the Claude CLI.
    /// Auto-checker has already decided correctness — Claude's job here is to (a) explain
    /// individual wrong answers using the transcript, (b) diagnose per-part / per-type weak
    /// areas, and (c) give 3-5 concrete tips. Returns null when CLI is unavailable / the
    /// response can't be parsed.
    /// </summary>
    Task<ListeningScoreReport?> EvaluateAsync(
        string testTitle,
        int rawScore,
        int totalQuestions,
        double bandEstimate,
        IReadOnlyList<ListeningPartContext> parts,
        CancellationToken ct = default);
}
