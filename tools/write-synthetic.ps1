[CmdletBinding()]
param(
    [string]$Path = "$env:TEMP\DiskWriteWatch-synthetic.bin",
    [ValidateRange(1, 4096)][int]$MiB = 256,
    [ValidateRange(4, 1024)][int]$BlockKiB = 1024,
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
$buffer = [byte[]]::new($BlockKiB * 1024)
[Random]::Shared.NextBytes($buffer)
$stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $blocks = [math]::Ceiling($MiB * 1024 / $BlockKiB)
    for ($i = 0; $i -lt $blocks; $i++) { $stream.Write($buffer, 0, $buffer.Length) }
    $stream.Flush()
} finally { $stream.Dispose() }
Write-Host "Wrote $MiB MiB to $Path"
if (-not $Keep) { Remove-Item -LiteralPath $Path -Force }
