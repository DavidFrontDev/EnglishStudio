using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.Modules.Dictionary.Srs;

/// <summary>
/// FSRS-4.5 scheduler. Pure stateless algorithm — no DB.
/// Refs: https://github.com/open-spaced-repetition/fsrs4anki
/// </summary>
public sealed class FsrsScheduler : IFsrsScheduler
{
    private const double Decay = -0.5;
    private const double Factor = 19.0 / 81.0;

    private readonly FsrsParameters _params;

    public FsrsScheduler(FsrsParameters? parameters = null)
    {
        _params = parameters ?? new FsrsParameters();
    }

    public ReviewLog InitializeFromFirstReview(UserWordProgress progress, SrsRating rating, DateTime now)
    {
        var w = _params.W;
        var r = (int)rating;

        var sBefore = progress.Stability;
        var dBefore = progress.Difficulty;
        var stateBefore = progress.State;

        var s = Math.Max(0.1, w[r - 1]);
        var d = Clamp01_10(w[4] - Math.Exp(w[5] * (r - 1)) + 1);

        progress.Stability = s;
        progress.Difficulty = d;
        progress.State = rating == SrsRating.Again ? SrsState.Relearning : SrsState.Review;
        progress.LastReviewedAt = now;
        progress.ReviewCount = 1;
        if (rating == SrsRating.Again) progress.LapseCount++;
        progress.UpdatedAt = now;

        var intervalDays = NextIntervalDays(s);
        progress.NextReviewAt = now.AddDays(intervalDays);

        return new ReviewLog
        {
            ReviewedAt = now,
            Rating = rating,
            StateBefore = stateBefore,
            StateAfter = progress.State,
            StabilityBefore = sBefore,
            StabilityAfter = progress.Stability,
            DifficultyBefore = dBefore,
            DifficultyAfter = progress.Difficulty,
            ElapsedDays = 0,
            ScheduledIntervalDays = intervalDays,
        };
    }

    public ReviewLog Schedule(UserWordProgress progress, SrsRating rating, DateTime now)
    {
        if (progress.State == SrsState.New || progress.LastReviewedAt is null || progress.Stability <= 0)
        {
            return InitializeFromFirstReview(progress, rating, now);
        }

        var w = _params.W;
        var r = (int)rating;

        var sBefore = progress.Stability;
        var dBefore = progress.Difficulty;
        var stateBefore = progress.State;
        var elapsedDays = Math.Max(0, (now - progress.LastReviewedAt!.Value).TotalDays);

        var retrievability = Math.Pow(1 + Factor * elapsedDays / sBefore, Decay);

        // Difficulty update (with mean reversion)
        var nextDLinear = dBefore - w[6] * (r - 3);
        var initialDForEasy = w[4] - Math.Exp(w[5] * (((int)SrsRating.Easy) - 1)) + 1;
        var nextD = w[7] * initialDForEasy + (1 - w[7]) * nextDLinear;
        nextD = Clamp01_10(nextD);

        double nextS;
        if (rating == SrsRating.Again)
        {
            // forgetting: lapse formula
            nextS = w[11]
                * Math.Pow(dBefore, -w[12])
                * (Math.Pow(sBefore + 1, w[13]) - 1)
                * Math.Exp((1 - retrievability) * w[14]);
            progress.LapseCount++;
        }
        else
        {
            var hardPenalty = rating == SrsRating.Hard ? w[15] : 1.0;
            var easyBonus = rating == SrsRating.Easy ? w[16] : 1.0;

            nextS = sBefore * (
                1 + Math.Exp(w[8])
                * (11 - dBefore)
                * Math.Pow(sBefore, -w[9])
                * (Math.Exp((1 - retrievability) * w[10]) - 1)
                * hardPenalty
                * easyBonus
            );
        }

        nextS = Math.Clamp(nextS, 0.1, _params.MaximumIntervalDays);

        progress.Stability = nextS;
        progress.Difficulty = nextD;
        progress.State = rating == SrsRating.Again ? SrsState.Relearning : SrsState.Review;
        progress.LastReviewedAt = now;
        progress.ReviewCount++;
        progress.UpdatedAt = now;

        var intervalDays = NextIntervalDays(nextS);
        progress.NextReviewAt = now.AddDays(intervalDays);

        return new ReviewLog
        {
            ReviewedAt = now,
            Rating = rating,
            StateBefore = stateBefore,
            StateAfter = progress.State,
            StabilityBefore = sBefore,
            StabilityAfter = nextS,
            DifficultyBefore = dBefore,
            DifficultyAfter = nextD,
            ElapsedDays = elapsedDays,
            ScheduledIntervalDays = intervalDays,
        };
    }

    private double NextIntervalDays(double stability)
    {
        // I = S * (target_retention^(1/DECAY) - 1) / FACTOR
        var i = stability * (Math.Pow(_params.TargetRetention, 1.0 / Decay) - 1) / Factor;
        i = Math.Round(i);
        return Math.Clamp(i, _params.MinimumIntervalDays, _params.MaximumIntervalDays);
    }

    private static double Clamp01_10(double d) => Math.Clamp(d, 1.0, 10.0);
}
