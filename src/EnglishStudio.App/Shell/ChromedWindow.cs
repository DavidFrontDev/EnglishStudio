using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace EnglishStudio.App.Shell;

/// <summary>
/// Base class for all in-app windows that use the custom dark chrome.
/// Provides the SystemCommands wiring (Minimize/Maximize/Restore/Close) so window-control
/// buttons inside the template can use <c>Command="{x:Static SystemCommands.*}"</c> with no
/// per-window code-behind, and caps the maximized bounds to the monitor's work area so a
/// borderless (WindowStyle=None) window never covers the taskbar.
/// </summary>
public class ChromedWindow : Window
{
    /// <summary>
    /// When true, the title bar shows the RU/EN language switch (left of the caption buttons).
    /// Off by default so dialogs stay clean; set to true on the main window only.
    /// </summary>
    public static readonly DependencyProperty ShowLanguageToggleProperty =
        DependencyProperty.Register(
            nameof(ShowLanguageToggle), typeof(bool), typeof(ChromedWindow),
            new PropertyMetadata(false));

    public bool ShowLanguageToggle
    {
        get => (bool)GetValue(ShowLanguageToggleProperty);
        set => SetValue(ShowLanguageToggleProperty, value);
    }

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
            ApplyWorkAreaBounds(hwnd, lParam);
        return IntPtr.Zero;
    }

    /// <summary>
    /// Constrains the maximized position/size to the work area of the monitor the window is on,
    /// so the borderless window leaves the taskbar visible (per-monitor correct).
    /// </summary>
    private static void ApplyWorkAreaBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var work = monitorInfo.rcWork;
        var screen = monitorInfo.rcMonitor;

        // ptMaxPosition is relative to the monitor's origin, not the virtual screen.
        mmi.ptMaxPosition.X = work.Left - screen.Left;
        mmi.ptMaxPosition.Y = work.Top - screen.Top;
        mmi.ptMaxSize.X = work.Right - work.Left;
        mmi.ptMaxSize.Y = work.Bottom - work.Top;

        Marshal.StructureToPtr(mmi, lParam, false);
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
