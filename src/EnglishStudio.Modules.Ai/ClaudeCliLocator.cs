using System.Diagnostics;

namespace EnglishStudio.Modules.Ai;

/// <summary>
/// Finds the `claude` (or `claude.cmd`) executable on Windows.
/// Search order:
///   1. Path explicitly provided (settings).
///   2. <c>where claude</c> via cmd — covers PATH-resolved npm shims.
///   3. Common install locations.
/// </summary>
public static class ClaudeCliLocator
{
    public static string? Locate(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        // Use `where` which respects PATH and PATHEXT and finds shims.
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "claude",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            if (p.Start())
            {
                var stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                if (p.ExitCode == 0)
                {
                    var first = stdout
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(first) && File.Exists(first))
                    {
                        return first;
                    }
                }
            }
        }
        catch (Exception)
        {
            // fall through to manual probes
        }

        // Common locations for the official installer / npm-global shim.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "claude", "claude.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm", "claude.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm", "claude.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "local", "claude.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public static async Task<string?> ProbeVersionAsync(string exePath, CancellationToken ct = default)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            p.Start();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var stdout = await p.StandardOutput.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
