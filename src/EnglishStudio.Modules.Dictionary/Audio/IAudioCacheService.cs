namespace EnglishStudio.Modules.Dictionary.Audio;

public interface IAudioCacheService
{
    Task<string?> GetOrFetchAsync(
        int wordId,
        AudioVariant variant,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
