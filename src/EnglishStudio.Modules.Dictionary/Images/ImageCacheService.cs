using System.Collections.Concurrent;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Dictionary.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Dictionary.Images;

public sealed class ImageCacheService : IImageCacheService
{
    public const string HttpClientName = "EnglishStudio.ImageDownload";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IEnumerable<IImageProvider> _providers;
    private readonly IMessageLocalizer _messages;
    private readonly ILogger<ImageCacheService> _logger;

    // Serializes fetches per word: concurrent callers (possibly with different maxImages) run one
    // at a time; the second finds the first's rows cached in step 1 instead of re-downloading or
    // racing the dead-row pruning.
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _wordLocks = new();

    public ImageCacheService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IEnumerable<IImageProvider> providers,
        IMessageLocalizer messages,
        ILogger<ImageCacheService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _providers = providers;
        _messages = messages;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetOrFetchAsync(
        int wordId,
        int maxImages = 1,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var gate = _wordLocks.GetOrAdd(wordId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await FetchAsync(wordId, maxImages, progress, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<string>> FetchAsync(
        int wordId, int maxImages, IProgress<string>? progress, CancellationToken ct)
    {
        // 1) check existing MediaAsset rows
        string? headword = null;
        var existing = new List<string>();
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
            var info = await db.Words
                .Where(w => w.Id == wordId)
                .Select(w => new { w.Headword })
                .FirstOrDefaultAsync(ct);
            headword = info?.Headword;
            if (headword is null) return existing;

            var cached = await db.MediaAssets
                .Where(m => m.WordId == wordId && m.Kind == MediaKind.Image)
                .OrderBy(m => m.Id)
                .ToListAsync(ct);
            var dead = new List<MediaAsset>();
            foreach (var m in cached)
            {
                if (!File.Exists(m.LocalPath))
                {
                    dead.Add(m);
                }
                else if (existing.Count < maxImages)
                {
                    existing.Add(m.LocalPath);
                }
            }
            if (dead.Count > 0)
            {
                db.MediaAssets.RemoveRange(dead);
                await db.SaveChangesAsync(ct);
            }
            if (existing.Count >= maxImages) return existing;
        }

        progress?.Report(_messages.Format("Dict_ImagesSearching", headword));

        // 2) ask providers in order of preference (Pexels first if available, then Wikimedia)
        var ordered = _providers
            .OrderByDescending(p => p.IsAvailable && p.Name == "pexels")
            .ToList();

        var newAssets = new List<MediaAsset>();
        var localPaths = new List<string>(existing);
        var imgDir = Path.Combine(DictionaryPaths.MediaRoot, "Images");
        Directory.CreateDirectory(imgDir);

        foreach (var provider in ordered)
        {
            if (localPaths.Count >= maxImages) break;
            if (!provider.IsAvailable) continue;

            List<ImageResult> results;
            try
            {
                results = await provider.SearchAsync(headword!, maxImages - localPaths.Count, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Provider {Provider} search failed for {Headword}", provider.Name, headword);
                continue;
            }

            var http = _httpFactory.CreateClient(HttpClientName);
            foreach (var r in results)
            {
                if (localPaths.Count >= maxImages) break;
                progress?.Report(_messages.Format("Dict_ImagesDownloading", provider.Name));
                try
                {
                    using var resp = await http.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode) continue;
                    var ext = GuessExt(r.Url);
                    var fileName = $"{wordId}_{provider.Name}_{Guid.NewGuid():N}{ext}";
                    var path = Path.Combine(imgDir, fileName);
                    await using (var fs = File.Create(path))
                    await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                        await src.CopyToAsync(fs, ct);

                    localPaths.Add(path);
                    newAssets.Add(new MediaAsset
                    {
                        WordId = wordId,
                        Kind = MediaKind.Image,
                        LocalPath = path,
                        SourceUrl = r.Url,
                        Attribution = r.Attribution,
                        CreatedAt = DateTime.UtcNow,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Image download failed: {Url}", r.Url);
                }
            }
        }

        // 3) persist MediaAsset rows
        if (newAssets.Count > 0)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
            db.MediaAssets.AddRange(newAssets);
            await db.SaveChangesAsync(ct);
        }

        progress?.Report(string.Empty);
        return localPaths;
    }

    private static string GuessExt(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains(".png")) return ".png";
        if (u.Contains(".webp")) return ".webp";
        if (u.Contains(".jpeg")) return ".jpeg";
        return ".jpg";
    }
}
