[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\DiskWriteWatch",
    [string]$DataDirectory = "$env:ProgramData\DiskWriteWatch\data",
    [ValidateRange(1, 65535)][int]$Port = 8765,
    [ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$ServiceName = 'DiskWriteWatch',
    [ValidateSet('true', 'false')][string]$StartService = 'true'
)

$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem -or $env:OS -ne 'Windows_NT') {
    throw 'DiskWriteWatch requires 64-bit Windows 10 or 11.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installer from an elevated PowerShell session.'
}

$sourceExe = Join-Path $PSScriptRoot 'DiskWriteWatch.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) {
    $sourceExe = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiskWriteWatch.exe'
}
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'DiskWriteWatch.exe was not found beside the installer.' }

$existingPort = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
if ($existingPort) { throw "TCP port $Port is already in use." }
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service '$ServiceName' already exists. Uninstall it first."
}

$programDataDirectory = Join-Path $env:ProgramData 'DiskWriteWatch'
$configPath = Join-Path $programDataDirectory 'config.json'
New-Item -ItemType Directory -Force -Path $InstallDirectory, $programDataDirectory, $DataDirectory | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination (Join-Path $InstallDirectory 'DiskWriteWatch.exe') -Force

$sample = Join-Path (Split-Path $PSScriptRoot -Parent) 'config.sample.json'
if (-not (Test-Path -LiteralPath $sample)) { $sample = Join-Path $PSScriptRoot 'config.sample.json' }
if (-not (Test-Path -LiteralPath $configPath)) {
    $config = Get-Content -LiteralPath $sample -Raw | ConvertFrom-Json
    $config.Storage.DataDirectory = $DataDirectory
    $config.Dashboard.ListenUrl = "http://127.0.0.1:$Port"
    $json = $config | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($configPath, $json, [Text.UTF8Encoding]::new($false))
}

$exe = Join-Path $InstallDirectory 'DiskWriteWatch.exe'
& $exe --validate-config --config $configPath
if ($LASTEXITCODE -ne 0) { throw "Configuration validation failed with exit code $LASTEXITCODE." }

$binPath = '"{0}" --config "{1}" --service-name "{2}"' -f $exe, $configPath, $ServiceName
New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName 'DiskWriteWatch disk-write monitor' `
    -Description 'Collects Windows kernel ETW disk writes and serves a loopback-only dashboard.' `
    -StartupType Automatic | Out-Null
& sc.exe config $ServiceName 'start=' 'delayed-auto' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe delayed-auto configuration failed with exit code $LASTEXITCODE." }
& sc.exe description $ServiceName 'Collects Windows kernel ETW disk writes and serves a loopback-only dashboard.' | Out-Null
& sc.exe failure $ServiceName 'reset=' '86400' 'actions=' 'restart/5000/restart/30000/none/0' | Out-Null

if ([Convert]::ToBoolean($StartService)) { Start-Service -Name $ServiceName }
Write-Host "Installed $ServiceName. Dashboard: http://127.0.0.1:$Port"
Write-Host "Configuration: $configPath"
Write-Host "Data: $DataDirectory"
