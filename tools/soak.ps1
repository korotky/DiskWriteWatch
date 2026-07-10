[CmdletBinding()]
param(
    [ValidateRange(0.1, 168)][double]$DurationHours = 24,
    [ValidateRange(10, 3600)][int]$SampleSeconds = 60,
    [string]$OutputPath = "$env:ProgramData\DiskWriteWatch\soak\soak-$((Get-Date).ToString('yyyyMMdd-HHmmss')).csv",
    [string]$ServiceName = 'DiskWriteWatch'
)
$ErrorActionPreference = 'Stop'
$deadline = (Get-Date).AddHours($DurationHours)
$processorCount = [Environment]::ProcessorCount
$previousCpu = $null
$previousTime = $null
$first = $true
while ((Get-Date) -lt $deadline) {
    $service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    if (-not $service -or -not $service.ProcessId) { throw "Service '$ServiceName' is not running." }
    $process = Get-Process -Id $service.ProcessId
    $now = Get-Date
    $cpuPercent = if ($null -eq $previousCpu) { 0 } else {
        100 * ($process.CPU - $previousCpu) / (($now - $previousTime).TotalSeconds * $processorCount)
    }
    $health = Invoke-RestMethod http://127.0.0.1:8765/api/health
    $row = [pscustomobject]@{
        Timestamp = $now.ToString('o')
        CpuPercent = [math]::Round($cpuPercent, 3)
        PrivateMemoryBytes = $process.PrivateMemorySize64
        WorkingSetBytes = $process.WorkingSet64
        DatabaseBytes = $health.databaseBytes
        PendingBuckets = $health.pendingBuckets
        DroppedBuckets = $health.droppedBuckets
        LostEvents = $health.currentBucketLostEvents
        EtwActive = $health.etwActive
    }
    $directory = Split-Path $OutputPath -Parent
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $row | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Append:(-not $first)
    $first = $false
    $previousCpu = $process.CPU
    $previousTime = $now
    Start-Sleep -Seconds $SampleSeconds
}
Write-Host "Soak results: $OutputPath"
