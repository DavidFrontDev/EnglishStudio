using EnglishStudio.Modules.Dictionary.Data;
using Ionic.Zip;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Dictionary.Audio;

public sealed class AudioCacheService : IAudioCacheService
{
    public const string AudioBaseUrl =
        "https://raw.githubusercontent.com/winterdl/oxford-5000-vocabulary-audio-definition/main/audio";

    public const string HttpClientName = "EnglishStudio.Audio";

    private static readonly string[] SplitSuffixes =
        { ".z01", ".z02", ".z03", ".zip" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AudioCacheService> _logger;

    private readonly SemaphoreSlim _ukLock = new(1, 1);
    private readonly SemaphoreSlim _usLock = new(1, 1);

    public AudioCacheService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<AudioCacheService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string?> GetOrFetchAsync(
        int wordId,
        AudioVariant variant,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        string? remoteName;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
            remoteName = await db.Words
                .Where(w => w.Id == wordId)
                .Select(w => variant == AudioVariant.Uk ? w.AudioUkPath : w.AudioUsPath)
                .FirstOrDefaultAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(remoteName))
        {
            return null;
        }

        var localDir = GetVariantDirectory(variant);
        Directory.CreateDirectory(localDir);

        var safeFile = Path.GetFileName(remoteName);
        var localPath = Path.Combine(localDir, safeFile);
        if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
        {
            return localPath;
        }

        if (!await EnsureVariantUnpackedAsync(variant, progress, ct))
        {
            return null;
        }

        return File.Exists(localPath) ? localPath : null;
    }

    private async Task<bool> EnsureVariantUnpackedAsync(
        AudioVariant variant,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var localDir = GetVariantDirectory(variant);
        var marker = Path.Combine(localDir, ".unpacked");
        if (File.Exists(marker))
        {
            return true;
        }

        var gate = variant == AudioVariant.Uk ? _ukLock : _usLock;
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(marker)) return true;

            Directory.CreateDirectory(localDir);
            var dlDir = Path.Combine(DictionaryPaths.AudioRoot, "_dl", VariantSlug(variant));
            Directory.CreateDirectory(dlDir);

            var baseName = $"{VariantSlug(variant)}_audio_split_24m";

            progress?.Report($"Скачиваем {VariantSlug(variant).ToUpperInvariant()} аудио (~95 МБ)…");

            var http = _httpClientFactory.CreateClient(HttpClientName);
            for (var i = 0; i < SplitSuffixes.Length; i++)
            {
                var suffix = SplitSuffixes[i];
                var url = $"{AudioBaseUrl}/{baseName}{suffix}";
                var target = Path.Combine(dlDir, baseName + suffix);
                if (File.Exists(target) && new FileInfo(target).Length > 0)
                {
                    continue;
                }

                progress?.Report($"Скачиваем часть {i + 1} из {SplitSuffixes.Length} ({VariantSlug(variant).ToUpperInvariant()})…");
                _logger.LogInformation("Downloading {Url}", url);

                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var tmp = target + ".tmp";
                await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var src = await response.Content.ReadAsStreamAsync(ct))
                {
                    await src.CopyToAsync(fs, ct);
                }
                File.Move(tmp, target, overwrite: true);
            }

            progress?.Report($"Распаковка {VariantSlug(variant).ToUpperInvariant()} аудио…");

            var zipPath = Path.Combine(dlDir, baseName + ".zip");
            ExtractFlat(zipPath, localDir);

            try
            {
                Directory.Delete(dlDir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to clean download dir {Dir}", dlDir);
            }

            await File.WriteAllTextAsync(marker, DateTime.UtcNow.ToString("O"), ct);
            progress?.Report(string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch+unpack audio variant {Variant}", variant);
            progress?.Report($"Ошибка загрузки: {ex.Message}");
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void ExtractFlat(string zipPath, string targetDir)
    {
        using var zip = ZipFile.Read(zipPath);
        foreach (var entry in zip)
        {
            if (entry.IsDirectory) continue;
            var name = Path.GetFileName(entry.FileName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.Contains("..", StringComparison.Ordinal)) continue;

            var outPath = Path.Combine(targetDir, name);
            using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
            entry.Extract(fs);
        }
    }

    private static string GetVariantDirectory(AudioVariant variant) =>
        variant == AudioVariant.Uk ? DictionaryPaths.AudioUkRoot : DictionaryPaths.AudioUsRoot;

    private static string VariantSlug(AudioVariant variant) =>
        variant == AudioVariant.Uk ? "uk" : "us";
}
