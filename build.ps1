[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
dotnet restore .\DiskWriteWatch.slnx --locked-mode
dotnet build .\DiskWriteWatch.slnx -c $Configuration --no-restore
dotnet test .\DiskWriteWatch.slnx -c $Configuration --no-build --collect:"XPlat Code Coverage"
