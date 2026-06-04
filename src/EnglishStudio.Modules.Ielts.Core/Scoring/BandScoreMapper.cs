using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

public sealed class BandScoreMapper : IBandScoreMapper
{
    // Each tuple = (minimum raw score, band). Lookup is "first row whose threshold ≤ raw".
    // Tables are Cambridge IELTS official conversion (academic Reading slightly stricter than GT Reading).

    private static readonly (int MinRaw, double Band)[] AcademicReading =
    {
        (39, 9.0), (37, 8.5), (35, 8.0), (33, 7.5), (30, 7.0),
        (27, 6.5), (23, 6.0), (19, 5.5), (15, 5.0), (13, 4.5),
        (10, 4.0), (8, 3.5), (6, 3.0), (4, 2.5), (3, 2.0),
        (2, 1.5), (1, 1.0), (0, 0.0)
    };

    private static readonly (int MinRaw, double Band)[] GeneralTrainingReading =
    {
        (40, 9.0), (39, 8.5), (37, 8.0), (36, 7.5), (34, 7.0),
        (32, 6.5), (30, 6.0), (27, 5.5), (23, 5.0), (19, 4.5),
        (15, 4.0), (12, 3.5), (9, 3.0), (6, 2.5), (4, 2.0),
        (2, 1.5), (1, 1.0), (0, 0.0)
    };

    private static readonly (int MinRaw, double Band)[] Listening =
    {
        (39, 9.0), (37, 8.5), (35, 8.0), (32, 7.5), (30, 7.0),
        (26, 6.5), (23, 6.0), (18, 5.5), (16, 5.0), (13, 4.5),
        (10, 4.0), (8, 3.5), (6, 3.0), (4, 2.5), (3, 2.0),
        (2, 1.5), (1, 1.0), (0, 0.0)
    };

    public double RawToBand(int rawScore, IeltsSection section, IeltsTestMode mode)
    {
        var clamped = Math.Clamp(rawScore, 0, 40);

        var table = section switch
        {
            IeltsSection.Reading when mode == IeltsTestMode.Academic => AcademicReading,
            IeltsSection.Reading when mode == IeltsTestMode.GeneralTraining => GeneralTrainingReading,
            IeltsSection.Listening => Listening,
            _ => throw new ArgumentException(
                $"RawToBand is defined only for Reading/Listening; got {section}.", nameof(section))
        };

        foreach (var (minRaw, band) in table)
        {
            if (clamped >= minRaw) return band;
        }
        return 0.0;
    }
}
