using EnglishStudio.Modules.Dictionary.Localization;

namespace EnglishStudio.App.Localization;

/// <summary>
/// Bridges message keys emitted by the modules (<see cref="IMessageLocalizer"/>) to the app's
/// resx-based <see cref="ILocalizer"/>, so module progress/validation texts follow the UI language.
/// </summary>
public sealed class ModuleMessageLocalizer : IMessageLocalizer
{
    private readonly ILocalizer _localizer;

    public ModuleMessageLocalizer(ILocalizer localizer) => _localizer = localizer;

    public string Format(string key, params object[] args) => _localizer.Format(key, args);
}
