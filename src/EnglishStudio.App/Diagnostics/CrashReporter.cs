using System.IO;
using System.Reflection;
using System.Text;
using EnglishStudio.Modules.Dictionary.Data;

namespace EnglishStudio.App.Diagnostics;

/// <summary>
/// File-based crash reports under %AppData%\EnglishStudio\Crashes. Written by the global
/// exception handlers (which must never throw, so every method here swallows its own errors);
/// unacknowledged reports are surfaced to the user on the next startup.
/// </summary>
internal static class CrashReporter
{
    private const string SeenExtension = ".seen";
    private const int MaxReports = 20;
    private static readonly object Gate = new();

    public static string CrashDirectory =>
        Path.Combine(DictionaryPaths.AppDataRoot, "Crashes");

    /// <param name="fatal">
    /// Fatal reports ("crash_*") trigger the next-startup prompt; non-fatal ones ("error_*") are
    /// kept for diagnostics only — the user already saw a dialog when they happened.
    /// </param>
    public static void Write(string source, Exception ex, bool fatal)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(CrashDirectory);

                var sb = new StringBuilder();
                sb.AppendLine($"Time (UTC):  {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Version:     {Assembly.GetExecutingAssembly().GetName().Version}");
                sb.AppendLine($"OS:          {Environment.OSVersion}");
                sb.AppendLine($"Source:      {source}");
                sb.AppendLine();
                sb.AppendLine(ex.ToString());

                var prefix = fatal ? "crash" : "error";
                var path = Path.Combine(
                    CrashDirectory, $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.txt");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

                TrimOldReports();
            }
            catch
            {
                // Crash reporting must never crash.
            }
        }
    }

    /// <summary>Reports written before this run that the user has not been shown yet.</summary>
    public static bool HasUnacknowledgedReports()
    {
        try
        {
            return Directory.Exists(CrashDirectory)
                   && Directory.EnumerateFiles(CrashDirectory, "crash_*.txt").Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Marks all current reports as seen so the startup prompt fires once per crash.</summary>
    public static void AcknowledgeAll()
    {
        lock (Gate)
        {
            try
            {
                if (!Directory.Exists(CrashDirectory)) return;
                foreach (var file in Directory.EnumerateFiles(CrashDirectory, "crash_*.txt").ToList())
                {
                    var target = file + SeenExtension;
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(file, target);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static void TrimOldReports()
    {
        var all = Directory.EnumerateFiles(CrashDirectory)
            .OrderByDescending(File.GetCreationTimeUtc)
            .Skip(MaxReports)
            .ToList();
        foreach (var stale in all)
        {
            try { File.Delete(stale); } catch { /* best effort */ }
        }
    }
}
