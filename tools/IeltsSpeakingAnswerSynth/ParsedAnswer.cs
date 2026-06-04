namespace EnglishStudio.IeltsSpeakingAnswerSynth;

public sealed record ParsedAnswer(
    int Book,
    int TestNumber,
    int Part,
    int QuestionIndex,
    string QuestionText,
    string ModelAnswer);

public sealed record SynthesisTarget(
    int QuestionId,
    int Book,
    int TestNumber,
    int Part,
    int QuestionIndex,
    string TopicCode,
    string TopicLabel,
    string QuestionText,
    string PromptQuestionText,
    string? CurrentModelAnswer);
