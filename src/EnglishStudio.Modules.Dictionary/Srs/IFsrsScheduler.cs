using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.Modules.Dictionary.Srs;

public interface IFsrsScheduler
{
    /// <summary>
    /// Initialize a fresh card from the user's first rating.
    /// Mutates and returns the same progress instance.
    /// </summary>
    ReviewLog InitializeFromFirstReview(UserWordProgress progress, SrsRating rating, DateTime now);

    /// <summary>
    /// Apply a review rating to a card that already has Stability/Difficulty.
    /// Mutates the progress and returns the ReviewLog (not yet persisted).
    /// </summary>
    ReviewLog Schedule(UserWordProgress progress, SrsRating rating, DateTime now);
}
