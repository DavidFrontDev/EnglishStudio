namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Splits a long text into pages for the reader (F8): by detected chapters, otherwise by a target
/// word count, with boundaries at paragraph/sentence ends (never mid-sentence). Pure; implemented
/// by Agent A. A small text yields a single page (the reader UX is then unchanged).
/// </summary>
public interface IPaginationService
{
    IReadOnlyList<TextPage> Paginate(IReadOnlyList<TextToken> tokens, PaginationOptions? options = null);

    /// <summary>Index of the page containing the given word, or -1 if not found. For "resume from bookmark".</summary>
    int PageOfWord(IReadOnlyList<TextPage> pages, int wordIndex);
}
