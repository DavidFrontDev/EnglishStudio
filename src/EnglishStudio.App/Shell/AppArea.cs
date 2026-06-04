namespace EnglishStudio.App.Shell;

/// <summary>
/// Top-level navigation area a module belongs to. The sidebar switches between
/// <see cref="Study"/> and <see cref="Ielts"/>; <see cref="Global"/> modules
/// (e.g. Statistics) live in a separate bottom zone visible from any area.
/// </summary>
public enum AppArea
{
    Study,
    Ielts,
    Global
}
