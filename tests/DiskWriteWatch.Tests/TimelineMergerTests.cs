using DiskWriteWatch.Core;

namespace DiskWriteWatch.Tests;

public sealed class TimelineMergerTests
{
    [Fact]
    public void PersistedBucketReplacesSameInMemoryBucket()
    {
        var points = TimelineMerger.Merge([
            new TimelinePoint(100, 10, 20, false, false),
            new TimelinePoint(100, 10, 20, true, false),
            new TimelinePoint(160, 5, 7, false, false)
        ]);
        Assert.Equal(2, points.Count);
        Assert.Equal(10, points[0].LogicalBytes);
        Assert.True(points[0].Persisted);
        Assert.False(points[1].Persisted);
    }
}
