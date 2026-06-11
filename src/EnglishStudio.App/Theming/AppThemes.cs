namespace EnglishStudio.App.Theming;

/// <summary>
/// Описание одной темы: enum-значение, имя файла палитры в Themes/Palettes
/// и ключ локализации отображаемого имени (<see cref="LabelKey"/>) для переключателя в настройках.
/// Имя резолвится в текущем языке на стороне ViewModel, а не один раз здесь — иначе при смене
/// языка названия тем «застывали» бы на языке запуска.
/// </summary>
public sealed record ThemeDescriptor(AppTheme Theme, string PaletteFile, string LabelKey);

/// <summary>
/// Реестр доступных тем. Добавить тему = создать палитру в Themes/Palettes,
/// добавить значение в <see cref="AppTheme"/> и строку сюда — больше нигде править не нужно.
/// </summary>
public static class AppThemes
{
    public static readonly IReadOnlyList<ThemeDescriptor> All =
    [
        new(AppTheme.DarkBlue,  "DarkBlue",  "Theme_DarkBlue"),
        new(AppTheme.Light,     "Light",     "Theme_Light"),
        new(AppTheme.Solarized, "Solarized", "Theme_Solarized"),
        new(AppTheme.Forest,    "Forest",    "Theme_Forest"),
        new(AppTheme.Violet,    "Violet",    "Theme_Violet"),
        new(AppTheme.Sepia,     "Sepia",     "Theme_Sepia"),
        new(AppTheme.Nord,      "Nord",      "Theme_Nord"),
        new(AppTheme.RosePine,  "RosePine",  "Theme_RosePine"),
        new(AppTheme.Sage,      "Sage",      "Theme_Sage"),
        new(AppTheme.Mocha,     "Mocha",     "Theme_Mocha"),
        new(AppTheme.Blush,     "Blush",     "Theme_Blush"),
    ];

    /// <summary>Имя файла палитры для темы; DarkBlue — безопасный дефолт.</summary>
    public static string PaletteFileFor(AppTheme theme) =>
        All.FirstOrDefault(t => t.Theme == theme)?.PaletteFile ?? "DarkBlue";
}
