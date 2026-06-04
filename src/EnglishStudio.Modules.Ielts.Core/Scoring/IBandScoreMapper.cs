using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

public interface IBandScoreMapper
{
    /// <summary>
    /// Convert raw score (0–40) to official IELTS band (0.0–9.0) for the given section and mode.
    /// Source: Cambridge IELTS official band conversion tables.
    /// </summary>
    double RawToBand(int rawScore, IeltsSection section, IeltsTestMode mode);
}
