using System.Windows;

namespace EnglishStudio.App.Theming;

public class ThemeManager : IThemeManager
{
    private readonly Application _app;

    public ThemeManager(Application app)
    {
        _app = app;
    }

    public AppTheme Current { get; private set; } = AppTheme.DarkBlue;

    public event EventHandler<AppTheme>? ThemeChanged;

    public void Apply(AppTheme theme)
    {
        var paletteName = theme switch
        {
            AppTheme.DarkBlue => "DarkBlue",
            AppTheme.Light    => "Light",
            _                 => "DarkBlue",
        };

        var uri = new Uri(
            $"pack://application:,,,/EnglishStudio.App;component/Themes/Palettes/{paletteName}.xaml",
            UriKind.Absolute);

        var palette = new ResourceDictionary { Source = uri };

        // По соглашению палитра — нулевой элемент MergedDictionaries.
        if (_app.Resources.MergedDictionaries.Count == 0)
        {
            _app.Resources.MergedDictionaries.Add(palette);
        }
        else
        {
            _app.Resources.MergedDictionaries[0] = palette;
        }

        Current = theme;
        ThemeChanged?.Invoke(this, theme);
    }
}
