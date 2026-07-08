using System.Management;

namespace DiskWriteWatch.Service;

public sealed record DiskDescriptor(int Number, string Model, string SerialNumber, IReadOnlyList<string> Volumes);

public sealed class DiskInventory
{
    public IReadOnlyList<DiskDescriptor> GetDisks()
    {
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
        return result.OrderBy(x => x.Number).ToArray();
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
