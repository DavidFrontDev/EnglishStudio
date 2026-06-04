using System.Windows;
using EnglishStudio.App.ViewModels.Content;
using EnglishStudio.App.Views.Content;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.App.Content;

/// <summary>
/// Single place that opens the content-import window, so Settings and every hub banner launch it
/// identically. Resolves a fresh window + VM from DI and shows it modally (blocks until closed),
/// matching the Settings-window open pattern (<c>ShellViewModel.OpenSettings</c>). See plan §B1.
/// </summary>
public sealed class ContentImportLauncher
{
    private readonly IServiceProvider _services;

    public ContentImportLauncher(IServiceProvider services) => _services = services;

    /// <summary>Opens the importer modally over <paramref name="owner"/> (defaults to the main window).</summary>
    public void Show(Window? owner = null)
    {
        var window = _services.GetRequiredService<ContentImportWindow>();
        window.DataContext = _services.GetRequiredService<ContentImportViewModel>();
        window.Owner = owner ?? Application.Current.MainWindow;
        window.ShowDialog();
    }
}
