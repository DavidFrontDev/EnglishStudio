namespace EnglishStudio.Modules.Dictionary.Images;

public enum WhisperModelSize
{
    /// <summary>ggml-base.en (~142 МБ) — быстрый, для single-word pronunciation (M6).</summary>
    Base = 1,

    /// <summary>ggml-medium.en (~1.5 ГБ) — точный, для long-form Speaking (M10).</summary>
    Medium = 2
}
