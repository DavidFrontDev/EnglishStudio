namespace EnglishStudio.Modules.Dictionary.Images;

public interface IImageCacheService
{
    Task<IReadOnlyList<string>> GetOrFetchAsync(
        int wordId,
        int maxImages = 1,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
