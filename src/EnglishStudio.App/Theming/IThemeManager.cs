namespace EnglishStudio.App.Theming;

public interface IThemeManager
{
    AppTheme Current { get; }
    event EventHandler<AppTheme>? ThemeChanged;
    void Apply(AppTheme theme);
}
