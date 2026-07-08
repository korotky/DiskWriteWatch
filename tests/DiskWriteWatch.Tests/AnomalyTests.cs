using DiskWriteWatch.Core;

namespace DiskWriteWatch.Tests;

public sealed class AnomalyTests
{
    [Fact]
    public void RequiresAbsoluteAndRelativeThresholds()
    {
        var options = new AnomalyOptions { AbsoluteMiBPerMinute = 256, Multiplier = 5 };
        Assert.False(AnomalyDetector.IsAnomaly(100L * 1024 * 1024, [1], options));
        Assert.True(AnomalyDetector.IsAnomaly(300L * 1024 * 1024,
            Enumerable.Repeat(10L * 1024 * 1024, 60), options));
        Assert.False(AnomalyDetector.IsAnomaly(300L * 1024 * 1024,
            Enumerable.Repeat(100L * 1024 * 1024, 60), options));
    }
}
