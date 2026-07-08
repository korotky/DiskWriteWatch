[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\DiskWriteWatch",
    [ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$ServiceName = 'DiskWriteWatch',
    [switch]$PurgeData
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this uninstaller from an elevated PowerShell session.'
}
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    & sc.exe delete $ServiceName | Out-Null
}
if (Test-Path -LiteralPath $InstallDirectory) { Remove-Item -LiteralPath $InstallDirectory -Recurse -Force }
if ($PurgeData) {
    $programDataDirectory = Join-Path $env:ProgramData 'DiskWriteWatch'
    if (Test-Path -LiteralPath $programDataDirectory) {
        Remove-Item -LiteralPath $programDataDirectory -Recurse -Force
    }
}
Write-Host 'DiskWriteWatch uninstalled.'
if (-not $PurgeData) { Write-Host 'Configuration and data were preserved.' }
