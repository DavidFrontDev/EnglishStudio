namespace EnglishStudio.Modules.Dictionary.Srs;

public sealed class FsrsParameters
{
    /// <summary>
    /// FSRS-4.5 default weights (17 floats). Source: open-spaced-repetition/fsrs4anki.
    /// </summary>
    public static readonly double[] DefaultWeights =
    {
        0.4072, 1.1829, 3.1262, 15.4722,
        7.2102, 0.5316, 1.0651, 0.0234,
        1.616,  0.1544, 1.0824, 1.9813,
        0.0953, 0.2975, 2.2042, 0.2407,
        2.9466
    };

    public double[] W { get; init; } = DefaultWeights;
    public double TargetRetention { get; set; } = 0.9;
    public double MinimumIntervalDays { get; init; } = 1.0;
    public double MaximumIntervalDays { get; init; } = 36500.0;
}
