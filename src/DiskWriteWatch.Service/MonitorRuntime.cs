using DiskWriteWatch.Core;

namespace DiskWriteWatch.Service;

public sealed class MonitorRuntime
{
    private readonly object _gate = new();
    private readonly MonitorOptions _options;
    private readonly List<BucketSnapshot> _pending = [];
    private long _droppedBuckets;
    private long _bufferedEstimateBytes;
    private string? _lastError;
    private DateTimeOffset? _lastFlush;
    private bool _etwActive;

    public MonitorRuntime(MonitorOptions options)
    {
        _options = options;
        Aggregator = new AggregationEngine(options.Collection.BucketSeconds,
            options.Collection.MaxKeysPerBucket);
    }

    public AggregationEngine Aggregator { get; }

    public void CompleteElapsed(DateTimeOffset now)
    {
        var buckets = Aggregator.CompleteElapsed(now);
        if (buckets.Count == 0)
            return;
        lock (_gate)
        {
            _pending.AddRange(buckets);
            _bufferedEstimateBytes += buckets.Sum(EstimateBytes);
            var maxBuckets = Math.Max(1,
                _options.Collection.UnavailableStorageBufferMinutes * 60 / _options.Collection.BucketSeconds);
            var maxBytes = _options.Collection.MaxBufferedMemoryMb * 1024L * 1024L;
            while (_pending.Count > maxBuckets || _bufferedEstimateBytes > maxBytes)
            {
                _bufferedEstimateBytes -= EstimateBytes(_pending[0]);
                _pending.RemoveAt(0);
                _droppedBuckets++;
            }
        }
    }

    public IReadOnlyList<BucketSnapshot> GetLiveBuckets()
    {
        lock (_gate)
            return [.. _pending, Aggregator.GetCurrentSnapshot()];
    }

    public IReadOnlyList<BucketSnapshot> TakePending()
    {
        lock (_gate)
        {
            var result = _pending.ToArray();
            _pending.Clear();
            _bufferedEstimateBytes = 0;
            return result;
        }
    }

    public void Requeue(IReadOnlyList<BucketSnapshot> buckets, Exception error)
    {
        lock (_gate)
        {
            _pending.InsertRange(0, buckets);
            _bufferedEstimateBytes += buckets.Sum(EstimateBytes);
            _lastError = error.Message;
            TrimBuffer();
        }
    }

    public void MarkFlushSucceeded(DateTimeOffset time)
    {
        lock (_gate)
        {
            _lastFlush = time;
            _lastError = null;
        }
    }

    public void SetEtwActive(bool active, string? error = null)
    {
        lock (_gate)
        {
            _etwActive = active;
            if (error is not null)
                _lastError = error;
        }
    }

    public RuntimeHealth GetHealth()
    {
        lock (_gate)
            return new RuntimeHealth(_etwActive, _pending.Count, _lastFlush, _lastError,
                _droppedBuckets, _bufferedEstimateBytes, Aggregator.GetCurrentSnapshot().LostEvents,
                _options.Collection.BucketSeconds, _options.Storage.FlushIntervalSeconds);
    }

    private void TrimBuffer()
    {
        var maxBuckets = Math.Max(1,
            _options.Collection.UnavailableStorageBufferMinutes * 60 / _options.Collection.BucketSeconds);
        var maxBytes = _options.Collection.MaxBufferedMemoryMb * 1024L * 1024L;
        while (_pending.Count > maxBuckets || _bufferedEstimateBytes > maxBytes)
        {
            _bufferedEstimateBytes -= EstimateBytes(_pending[0]);
            _pending.RemoveAt(0);
            _droppedBuckets++;
        }
    }

    private static long EstimateBytes(BucketSnapshot bucket) =>
        256L + bucket.FileWrites.Sum(x => 256L + 2L * (x.Path.Length + x.Directory.Length +
            x.Extension.Length + x.Process.Name.Length + x.Process.ImagePath.Length)) +
        bucket.DiskWrites.Sum(x => 192L + 2L * (x.Process.Name.Length + x.Process.ImagePath.Length));
}

public sealed record RuntimeHealth(
    bool EtwActive,
    int PendingBuckets,
    DateTimeOffset? LastSuccessfulFlush,
    string? LastError,
    long DroppedBuckets,
    long BufferedEstimateBytes,
    long CurrentBucketLostEvents,
    int BucketSeconds,
    int FlushIntervalSeconds);
