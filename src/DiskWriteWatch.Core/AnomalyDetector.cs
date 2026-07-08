namespace DiskWriteWatch.Core;

public static class AnomalyDetector
{
    public static bool IsAnomaly(long currentBytes, IEnumerable<long> previousBytes, AnomalyOptions options)
    {
        if (!options.Enabled)
            return false;
        var absolute = (long)options.AbsoluteMiBPerMinute * 1024 * 1024;
        if (currentBytes < absolute)
            return false;
        var values = previousBytes.TakeLast(options.BaselineMinutes).Order().ToArray();
        if (values.Length == 0)
            return true;
        var median = values.Length % 2 == 1
            ? values[values.Length / 2]
            : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2d;
        return median <= 0 || currentBytes >= median * options.Multiplier;
    }
}
