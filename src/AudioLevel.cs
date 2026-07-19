namespace DopaRushMixer;

internal static class AudioLevel
{
    private const double MinimumDecibels = -60;

    public static double ToDisplayPercent(float linearPeak)
    {
        var decibels = 20 * Math.Log10(Math.Max(linearPeak, 0.001f));
        return Math.Clamp((decibels - MinimumDecibels) / -MinimumDecibels * 100, 0, 100);
    }

    public static double DecayTarget(double currentTarget, double sampledPercent) =>
        Math.Max(sampledPercent, currentTarget * 0.84);

    public static double Interpolate(double displayedPercent, double targetPercent) =>
        displayedPercent + (targetPercent - displayedPercent) * 0.22;
}
