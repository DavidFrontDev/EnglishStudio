namespace EnglishStudio.Modules.Dictionary.Entities;

public enum CefrLevel
{
    Unknown = 0,
    A1 = 1,
    A2 = 2,
    B1 = 3,
    B2 = 4,
    C1 = 5,
    C2 = 6,
}

public enum WordSource
{
    Unknown = 0,
    Seed = 1,
    Api = 2,
    User = 3,
    Awl = 10,
    Avl = 11,
    Phave = 12,
    /// <summary>Generated on-demand by Claude (e.g. looked up while reading). See Word.IsAiGenerated.</summary>
    Ai = 13,
}
