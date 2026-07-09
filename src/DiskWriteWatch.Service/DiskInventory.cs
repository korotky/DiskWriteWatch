using System.Management;

namespace DiskWriteWatch.Service;

public sealed record DiskDescriptor(int Number, string Model, string SerialNumber, IReadOnlyList<string> Volumes);

public sealed class DiskInventory
{
    private readonly object _gate = new();
    private DateTimeOffset _cacheTime = DateTimeOffset.MinValue;
    private IReadOnlyList<DiskDescriptor>? _cache;

    public IReadOnlyList<DiskDescriptor> GetDisks()
    {
        lock (_gate)
        {
            if (_cache is not null && DateTimeOffset.UtcNow - _cacheTime < TimeSpan.FromSeconds(30))
                return _cache;
        }

        var result = new List<DiskDescriptor>();
        if (!OperatingSystem.IsWindows())
            return result;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model, SerialNumber FROM Win32_DiskDrive");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var number = Convert.ToInt32(item["Index"]);
                    result.Add(new DiskDescriptor(number,
                        item["Model"]?.ToString()?.Trim() ?? "Unknown disk",
                        item["SerialNumber"]?.ToString()?.Trim() ?? string.Empty,
                        GetVolumes(number)));
                }
            }
        }
        catch
        {
            // Inventory is enrichment only; collection remains functional.
        }
        var snapshot = result.OrderBy(x => x.Number).ToArray();
        lock (_gate)
        {
            _cache = snapshot;
            _cacheTime = DateTimeOffset.UtcNow;
        }
        return snapshot;
    }

    public IReadOnlyList<string> GetVolumesForDisk(int diskNumber) =>
        GetDisks().FirstOrDefault(x => x.Number == diskNumber)?.Volumes ?? [];

    public bool PathBelongsToDisk(string path, int diskNumber) =>
        GetVolumesForDisk(diskNumber).Any(volume =>
            path.StartsWith(volume.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    public string GetVolumeFilterText(IReadOnlyList<string>? volumes) =>
        string.Join("|", (volumes ?? []).Select(x => x.TrimEnd('\\')).Where(x => x.Length > 0));

    public static bool PathBelongsToAnyVolume(string? path, string? volumeFilterText)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(volumeFilterText))
            return false;
        return volumeFilterText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(volume => path.StartsWith(volume.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetVolumes(int diskNumber)
    {
        var volumes = new List<string>();
        using var partitions = new ManagementObjectSearcher(
            $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='\\\\.\\PHYSICALDRIVE{diskNumber}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
        foreach (ManagementObject partition in partitions.Get())
        {
            using (partition)
            using (var logicals = new ManagementObjectSearcher(
                       $"ASSOCIATORS OF {{{partition.Path.RelativePath}}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
            {
                foreach (ManagementObject logical in logicals.Get())
                {
                    using (logical)
                        if (logical["DeviceID"]?.ToString() is { } volume)
                            volumes.Add(volume);
                }
            }
        }
        return volumes.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
    }
}
