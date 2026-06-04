using System.Windows;
using System.Windows.Input;

namespace EnglishStudio.App.Shell;

/// <summary>
/// Base class for all in-app windows that use the custom dark chrome.
/// Provides the SystemCommands wiring (Minimize/Maximize/Restore/Close) so window-control
/// buttons inside the template can use <c>Command="{x:Static SystemCommands.*}"</c> with no
/// per-window code-behind.
/// </summary>
public class ChromedWindow : Window
{
    public ChromedWindow()
    {
        CommandBindings.Add(new CommandBinding(
            SystemCommands.MinimizeWindowCommand,
            (_, _) => SystemCommands.MinimizeWindow(this)));

        CommandBindings.Add(new CommandBinding(
            SystemCommands.MaximizeWindowCommand,
            (_, _) => SystemCommands.MaximizeWindow(this)));

        CommandBindings.Add(new CommandBinding(
            SystemCommands.RestoreWindowCommand,
            (_, _) => SystemCommands.RestoreWindow(this)));

        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand,
            (_, _) => SystemCommands.CloseWindow(this)));
    }
}
