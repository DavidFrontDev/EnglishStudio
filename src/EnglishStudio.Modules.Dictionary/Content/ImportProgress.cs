namespace EnglishStudio.Modules.Dictionary.Content;

/// <summary>Прогресс импорта для биндинга в ProgressBar (Maximum=1, Value=Fraction).</summary>
public readonly record struct ImportProgress(
    string Stage,        // "validate" | "copy" | "seed:Reading" | ...
    long BytesDone,
    long BytesTotal,
    double Fraction,     // 0..1
    string? CurrentFile);
