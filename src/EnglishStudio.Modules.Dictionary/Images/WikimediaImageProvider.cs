using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Dictionary.Images;

public sealed class WikimediaImageProvider : IImageProvider
{
    public const string HttpClientName = "EnglishStudio.Wikimedia";

    public string Name => "wikimedia";
    public bool IsAvailable => true;

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WikimediaImageProvider> _logger;

    public WikimediaImageProvider(IHttpClientFactory httpFactory, ILogger<WikimediaImageProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<List<ImageResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        var results = new List<ImageResult>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        var http = _httpFactory.CreateClient(HttpClientName);

        // Step 1: try Wikipedia article summary (REST API) — gives main thumbnail.
        var summaryUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(query)}";
        try
        {
            using var resp = await http.GetAsync(summaryUrl, ct);
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("thumbnail", out var thumb))
                {
                    var url = thumb.TryGetProperty("source", out var src) ? src.GetString() : null;
                    int? w = thumb.TryGetProperty("width", out var ww) ? ww.GetInt32() : null;
                    int? h = thumb.TryGetProperty("height", out var hh) ? hh.GetInt32() : null;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        // get also originalimage (full-res)
                        string? origUrl = null;
                        if (doc.RootElement.TryGetProperty("originalimage", out var orig) &&
                            orig.TryGetProperty("source", out var origSrc))
                            origUrl = origSrc.GetString();
                        results.Add(new ImageResult(
                            Url: origUrl ?? url,
                            ThumbnailUrl: url,
                            Attribution: "Wikipedia",
                            License: "CC BY-SA / Public Domain (Wikipedia)",
                            Width: w,
                            Height: h,
                            ProviderName: Name));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Wikipedia summary failed for {Query}", query);
        }

        if (results.Count >= maxResults) return results;

        // Step 2: search Wikimedia Commons for additional images.
        var searchUrl = "https://commons.wikimedia.org/w/api.php" +
            "?action=query&generator=search&gsrnamespace=6&prop=imageinfo&iiprop=url|size" +
            $"&format=json&gsrlimit={Math.Max(maxResults - results.Count, 1)}" +
            $"&gsrsearch={Uri.EscapeDataString(query)}";
        try
        {
            using var resp = await http.GetAsync(searchUrl, ct);
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("query", out var q) &&
                    q.TryGetProperty("pages", out var pages))
                {
                    foreach (var pageProp in pages.EnumerateObject())
                    {
                        if (results.Count >= maxResults) break;
                        var page = pageProp.Value;
                        if (!page.TryGetProperty("imageinfo", out var infoArr) ||
                            infoArr.ValueKind != JsonValueKind.Array || infoArr.GetArrayLength() == 0)
                            continue;
                        var info = infoArr[0];
                        var url = info.TryGetProperty("url", out var u) ? u.GetString() : null;
                        if (string.IsNullOrWhiteSpace(url)) continue;
                        // только bitmap-форматы
                        if (!IsBitmapImage(url)) continue;
                        int? w = info.TryGetProperty("width", out var ww) ? ww.GetInt32() : null;
                        int? h = info.TryGetProperty("height", out var hh) ? hh.GetInt32() : null;
                        results.Add(new ImageResult(
                            Url: url!,
                            ThumbnailUrl: null,
                            Attribution: "Wikimedia Commons",
                            License: "CC BY-SA / Public Domain (Commons)",
                            Width: w,
                            Height: h,
                            ProviderName: Name));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Wikimedia Commons search failed for {Query}", query);
        }

        return results;
    }

    private static bool IsBitmapImage(string url)
    {
        var u = url.ToLowerInvariant();
        return u.EndsWith(".jpg") || u.EndsWith(".jpeg") || u.EndsWith(".png") || u.EndsWith(".webp");
    }
}
