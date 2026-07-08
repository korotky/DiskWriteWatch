[CmdletBinding()]
param([ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$ServiceName = 'DiskWriteWatch')
$ErrorActionPreference = 'Stop'
Restart-Service -Name $ServiceName -Force
Get-Service -Name $ServiceName
