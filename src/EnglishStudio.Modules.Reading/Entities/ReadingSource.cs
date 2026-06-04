namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>Where a reading text came from.</summary>
public enum ReadingSource
{
    /// <summary>Added by the user (pasted or imported).</summary>
    User = 0,

    /// <summary>Imported from a file/URL.</summary>
    Imported = 1,

    /// <summary>Shipped with the app (graded readers seed).</summary>
    Builtin = 2
}
