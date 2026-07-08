namespace DiskWriteWatch.Core;

public sealed class MonitorOptions
{
    public StorageOptions Storage { get; init; } = new();
    public CollectionOptions Collection { get; init; } = new();
    public DashboardOptions Dashboard { get; init; } = new();
    public AnomalyOptions AnomalyDetection { get; init; } = new();
    public PrivacyOptions Privacy { get; init; } = new();

    public MonitorOptions ExpandEnvironmentVariables()
    {
        Storage.DataDirectory = Environment.ExpandEnvironmentVariables(Storage.DataDirectory);
        return this;
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Collection.BucketSeconds is < 10 or > 3600)
            errors.Add("Collection.BucketSeconds must be between 10 and 3600.");
        if (Storage.FlushIntervalSeconds is < 10 or > 3600)
            errors.Add("Storage.FlushIntervalSeconds must be between 10 and 3600.");
        if (Storage.FlushIntervalSeconds < Collection.BucketSeconds ||
            Storage.FlushIntervalSeconds % Collection.BucketSeconds != 0)
            errors.Add("Storage.FlushIntervalSeconds must be a multiple of and not less than Collection.BucketSeconds.");
        if (Collection.MaxKeysPerBucket is < 1_000 or > 2_000_000)
            errors.Add("Collection.MaxKeysPerBucket must be between 1,000 and 2,000,000.");
        if (Collection.UnavailableStorageBufferMinutes is < 1 or > 1440)
            errors.Add("Collection.UnavailableStorageBufferMinutes must be between 1 and 1440.");
        if (Collection.MaxBufferedMemoryMb is < 32 or > 8192)
            errors.Add("Collection.MaxBufferedMemoryMb must be between 32 and 8192.");
        if (Storage.MinuteRetentionDays is < 1 or > 365)
            errors.Add("Storage.MinuteRetentionDays must be between 1 and 365.");
        if (Storage.HourlyRetentionDays < Storage.MinuteRetentionDays || Storage.HourlyRetentionDays > 3650)
            errors.Add("Storage.HourlyRetentionDays must be >= minute retention and <= 3650.");
        if (Storage.MaxDatabaseSizeMb is < 128 or > 1_048_576)
            errors.Add("Storage.MaxDatabaseSizeMb must be between 128 and 1,048,576.");
        if (string.IsNullOrWhiteSpace(Storage.DataDirectory))
            errors.Add("Storage.DataDirectory is required.");
        if (!Uri.TryCreate(Dashboard.ListenUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              System.Net.IPAddress.TryParse(uri.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip)))
            errors.Add("Dashboard.ListenUrl must be an HTTP loopback URL.");
        if (Dashboard.RefreshSeconds is < 2 or > 3600)
            errors.Add("Dashboard.RefreshSeconds must be between 2 and 3600.");
        if (AnomalyDetection.AbsoluteMiBPerMinute < 1 || AnomalyDetection.Multiplier < 1)
            errors.Add("Anomaly thresholds must be positive.");
        return errors;
    }
}

public sealed class StorageOptions
{
    public string DataDirectory { get; set; } = @"%ProgramData%\DiskWriteWatch\data";
    public int FlushIntervalSeconds { get; init; } = 300;
    public int MinuteRetentionDays { get; init; } = 30;
    public int HourlyRetentionDays { get; init; } = 365;
    public int MaxDatabaseSizeMb { get; init; } = 5120;
}

public sealed class CollectionOptions
{
    public int BucketSeconds { get; init; } = 60;
    public bool CollectFileIo { get; init; } = true;
    public bool CollectDiskIo { get; init; } = true;
    public int MaxKeysPerBucket { get; init; } = 250_000;
    public int UnavailableStorageBufferMinutes { get; init; } = 10;
    public int MaxBufferedMemoryMb { get; init; } = 512;
}

public sealed class DashboardOptions
{
    public string ListenUrl { get; init; } = "http://127.0.0.1:8765";
    public int RefreshSeconds { get; init; } = 10;
}

public sealed class AnomalyOptions
{
    public bool Enabled { get; init; } = true;
    public int AbsoluteMiBPerMinute { get; init; } = 256;
    public int BaselineMinutes { get; init; } = 60;
    public double Multiplier { get; init; } = 5;
}

public sealed class PrivacyOptions
{
    public bool StoreFullPaths { get; init; } = true;
    public bool StoreCommandLines { get; init; }
}
