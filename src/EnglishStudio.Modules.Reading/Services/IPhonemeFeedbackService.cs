namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Phoneme-level pronunciation feedback (F3). Implemented by Agent A; consumed by the reader UI.
/// </summary>
public interface IPhonemeFeedbackService
{
    /// <summary>
    /// RELIABLE (no ASR): the word's phoneme/IPA breakdown with tricky-for-Russian sounds flagged.
    /// </summary>
    WordPronunciationGuide BuildGuide(string word);

    /// <summary>
    /// BEST-EFFORT (stretch): phoneme diff of the intended word vs what was recognized.
    /// <see cref="WordPhonemeFeedback.HasData"/> is false when phoneme data is missing.
    /// </summary>
    WordPhonemeFeedback Compare(string referenceWord, string recognizedWord);
}
