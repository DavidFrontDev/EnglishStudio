namespace EnglishStudio.Modules.Dictionary.Data;

public static class DictionaryPaths
{
    public const string AppFolderName = "EnglishStudio";
    public const string DbFileName = "dictionary.db";
    public const string MediaFolderName = "Media";
    public const string AudioFolderName = "Audio";
    public const string IeltsContentFolderName = "IeltsContent";

    private static string? _appDataRootOverride;

    /// <summary>
    /// Тест-сем: если задан, перекрывает %AppData% (для интеграционных тестов, импортирующих контент
    /// во временную папку). В обычном рантайме — null. См. plans/Infra_Publish_GitHub_AgentExecution.md §1.3.
    /// </summary>
    public static string? AppDataRootOverride
    {
        get => _appDataRootOverride;
        set => _appDataRootOverride = value;
    }

    public static string AppDataRoot =>
        _appDataRootOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string DatabaseFilePath => Path.Combine(AppDataRoot, DbFileName);

    public static string MediaRoot => Path.Combine(AppDataRoot, MediaFolderName);

    /// <summary>Канонический корень импортированного IELTS/словарного контента.</summary>
    public static string IeltsContentRoot => Path.Combine(AppDataRoot, IeltsContentFolderName);

    public static string AudioRoot => Path.Combine(MediaRoot, AudioFolderName);

    public static string AudioUkRoot => Path.Combine(AudioRoot, "uk");

    public static string AudioUsRoot => Path.Combine(AudioRoot, "us");

    public static string SqliteConnectionString =>
        $"Data Source={DatabaseFilePath}";

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(MediaRoot);
        Directory.CreateDirectory(AudioRoot);
        Directory.CreateDirectory(AudioUkRoot);
        Directory.CreateDirectory(AudioUsRoot);
        Directory.CreateDirectory(IeltsContentRoot);
    }
}
