using System.IO;
using System.Reflection;
using EnglishStudio.Modules.Dictionary.Data;

namespace EnglishStudio.App.Content;

/// <summary>
/// Lets startup skip the content-seeding pass when nothing relevant changed since the last
/// successful one. The token captures: the app version (embedded seeds change with releases),
/// the imported content-pack manifest (re-import → different token) and the DB file's creation
/// time (deleting the DB → different token). Any mismatch simply re-runs the seeders, which are
/// all idempotent — a stale stamp can never corrupt data, only cost one slower start.
/// </summary>
internal static class SeedStamp
{
    private static string StampPath => Path.Combine(DictionaryPaths.AppDataRoot, "seed.stamp");

    public static string CurrentToken()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";

        var manifest = Path.Combine(DictionaryPaths.IeltsContentRoot, "manifest.json");
        var manifestPart = File.Exists(manifest)
            ? $"{File.GetLastWriteTimeUtc(manifest).Ticks}-{new FileInfo(manifest).Length}"
            : "no-content";

        var db = DictionaryPaths.DatabaseFilePath;
        var dbPart = File.Exists(db)
            ? File.GetCreationTimeUtc(db).Ticks.ToString()
            : "no-db";

        return $"{version}|{manifestPart}|{dbPart}";
    }

    public static bool IsCurrent()
    {
        try
        {
            return File.Exists(StampPath) && File.ReadAllText(StampPath) == CurrentToken();
        }
        catch
        {
            return false;
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DictionaryPaths.AppDataRoot);
            File.WriteAllText(StampPath, CurrentToken());
        }
        catch
        {
            // Best effort — worst case the seeders re-run on the next start.
        }
    }
}
