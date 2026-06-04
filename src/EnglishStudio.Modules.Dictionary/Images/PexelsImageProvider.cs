using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Dictionary.Images;

public sealed class PexelsImageProvider : IImageProvider
{
    public const string HttpClientName = "EnglishStudio.Pexels";

    public string Name => "pexels";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_settings.PexelsApiKey);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IAppSettings _settings;
    private readonly ILogger<PexelsImageProvider> _logger;

    public PexelsImageProvider(
        IHttpClientFactory httpFactory,
        IAppSettings settings,
        ILogger<PexelsImageProvider> logger)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<ImageResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        var results = new List<ImageResult>();
        if (!IsAvailable || string.IsNullOrWhiteSpace(query)) return results;

        var http = _httpFactory.CreateClient(HttpClientName);
        var url = $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(query)}&per_page={maxResults}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(_settings.PexelsApiKey!);

        try
        {
            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("Pexels search returned {Status} for {Query}", resp.StatusCode, query);
                return results;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("photos", out var photos) ||
                photos.ValueKind != JsonValueKind.Array) return results;

            foreach (var p in photos.EnumerateArray())
            {
                if (results.Count >= maxResults) break;
                if (!p.TryGetProperty("src", out var src)) continue;
                var imgUrl = src.TryGetProperty("large", out var l) ? l.GetString() : null;
                var thumb  = src.TryGetProperty("medium", out var m) ? m.GetString() : null;
                if (string.IsNullOrWhiteSpace(imgUrl)) continue;
                int? w = p.TryGetProperty("width", out var ww) ? ww.GetInt32() : null;
                int? h = p.TryGetProperty("height", out var hh) ? hh.GetInt32() : null;
                var photographer = p.TryGetProperty("photographer", out var ph) ? ph.GetString() : null;
                results.Add(new ImageResult(
                    Url: imgUrl!,
                    ThumbnailUrl: thumb,
                    Attribution: photographer is null ? "Pexels" : $"Pexels / {photographer}",
                    License: "Pexels License (free use)",
                    Width: w,
                    Height: h,
                    ProviderName: Name));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pexels failed for {Query}", query);
        }

        return results;
    }
}
