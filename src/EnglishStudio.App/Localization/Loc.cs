using EnglishStudio.Modules.Dictionary.Images;

namespace EnglishStudio.App.Localization;

/// <summary>
/// Static facade over <see cref="LocalizationManager.Instance"/> for places without DI
/// (value converters, code-behind, static helpers). Prefer injecting <see cref="ILocalizer"/>
/// in view-models.
/// </summary>
public static class Loc
{
    public static ILocalizer Instance => LocalizationManager.Instance;

    /// <summary>True when the current interface language is English — for picking En/Ru data columns.</summary>
    public static bool IsEnglish => LocalizationManager.Instance.Current == AppLanguage.En;

    /// <summary>
    /// Picks the English text in English mode, otherwise Russian — falling back to the other side
    /// when the chosen one is empty (e.g. AI reports saved before the English fields existed).
    /// </summary>
    public static string Pick(string? en, string? ru) =>
        IsEnglish
            ? (string.IsNullOrWhiteSpace(en) ? ru ?? string.Empty : en)
            : (string.IsNullOrWhiteSpace(ru) ? en ?? string.Empty : ru);

    /// <summary>Looks up a key in the current language.</summary>
    public static string Tr(string key) => LocalizationManager.Instance[key];

    /// <summary>Formatted lookup, e.g. <c>Loc.Format("Writing_EvalProgress", i, total)</c>.</summary>
    public static string Format(string key, params object[] args) =>
        LocalizationManager.Instance.Format(key, args);
}
