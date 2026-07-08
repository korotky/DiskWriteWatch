using DiskWriteWatch.Core;

namespace DiskWriteWatch.Tests;

public sealed class ClockJumpTests
{
    [Fact]
    public void ForwardJumpProducesBoundedEmptyBuckets()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_200);
        var engine = new AggregationEngine(60, 100, start);
        var buckets = engine.CompleteElapsed(start.AddMinutes(3));
        Assert.Equal(3, buckets.Count);
        Assert.Equal(new long[] { 1_200, 1_260, 1_320 }, buckets.Select(x => x.StartUnixSeconds));
    }

    [Fact]
    public void BackwardJumpDoesNotCloseCurrentBucket()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_200);
        var engine = new AggregationEngine(60, 100, start);
        Assert.Empty(engine.CompleteElapsed(start.AddMinutes(-5)));
    }
}
