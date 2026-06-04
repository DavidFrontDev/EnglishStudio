namespace EnglishStudio.Modules.Dictionary.Images;

public sealed record ImageResult(
    string Url,
    string? ThumbnailUrl,
    string? Attribution,
    string? License,
    int? Width,
    int? Height,
    string ProviderName);

public interface IImageProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<List<ImageResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default);
}
