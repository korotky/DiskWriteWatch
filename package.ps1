[CmdletBinding()]
param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$artifactDirectory = Join-Path $PSScriptRoot 'artifacts'
$stage = Join-Path $artifactDirectory "DiskWriteWatch-$Version-win-x64"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

dotnet publish .\src\DiskWriteWatch.Service\DiskWriteWatch.Service.csproj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o $stage
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Copy-Item .\config.sample.json, .\LICENSE, .\README.md -Destination $stage
Copy-Item .\packaging\*.ps1 -Destination $stage
Copy-Item .\docs -Destination $stage -Recurse
Copy-Item .\tools -Destination $stage -Recurse

$zip = Join-Path $artifactDirectory "DiskWriteWatch-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
Write-Host $zip
Write-Host "SHA-256: $hash"
