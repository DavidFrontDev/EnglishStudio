using System.IO;
using EnglishStudio.Modules.Dictionary.Data;
using Microsoft.Data.Sqlite;

namespace EnglishStudio.App.Diagnostics;

/// <summary>
/// Backups of the single SQLite database (all three EF contexts share one file). Uses the SQLite
/// online-backup API, which produces a consistent copy even while the app holds open connections —
/// a plain File.Copy could capture a torn WAL state.
/// </summary>
internal static class DatabaseBackup
{
    private const int MaxAutoBackups = 5;

    public static string BackupDirectory =>
        Path.Combine(DictionaryPaths.AppDataRoot, "Backups");

    /// <summary>
    /// Creates a backup named after <paramref name="reason"/> ("migration" | "manual").
    /// Returns the backup path, or null when there is no database yet.
    /// </summary>
    public static string? Create(string reason)
    {
        if (!File.Exists(DictionaryPaths.DatabaseFilePath)) return null;

        Directory.CreateDirectory(BackupDirectory);
        var dest = Path.Combine(
            BackupDirectory,
            $"dictionary_{reason}_{DateTime.Now:yyyyMMdd_HHmmss}.db");

        using var source = new SqliteConnection(DictionaryPaths.SqliteConnectionString);
        using var target = new SqliteConnection($"Data Source={dest}");
        source.Open();
        target.Open();
        source.BackupDatabase(target);

        TrimAutoBackups();
        return dest;
    }

    /// <summary>Automatic (migration) backups are capped; manual ones are kept until the user deletes them.</summary>
    private static void TrimAutoBackups()
    {
        try
        {
            var stale = Directory.EnumerateFiles(BackupDirectory, "dictionary_migration_*.db")
                .OrderByDescending(File.GetCreationTimeUtc)
                .Skip(MaxAutoBackups)
                .ToList();
            foreach (var file in stale)
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }
        catch
        {
            // Trimming must never fail a backup.
        }
    }
}
