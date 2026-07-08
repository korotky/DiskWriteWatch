namespace DiskWriteWatch.Core;

public static class TimelineMerger
{
    public static IReadOnlyList<TimelinePoint> Merge(IEnumerable<TimelinePoint> points) =>
        points.GroupBy(x => x.BucketUnixSeconds)
            .Select(group =>
            {
                var preferred = group.Any(x => x.Persisted)
                    ? group.Where(x => x.Persisted)
                    : group;
                return new TimelinePoint(group.Key, preferred.Sum(x => x.LogicalBytes),
                    preferred.Sum(x => x.PhysicalBytes), group.Any(x => x.Persisted),
                    preferred.Any(x => x.Anomaly));
            })
            .OrderBy(x => x.BucketUnixSeconds)
            .ToArray();
}
