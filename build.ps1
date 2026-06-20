[CmdletBinding()]
param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'artifacts'
$version = '1.0.0'
$publishRoot = Join-Path $out "publish\v$version\$Runtime"
$serviceOut = Join-Path $publishRoot 'service'
$appOut = Join-Path $publishRoot 'app'
$setupOut = Join-Path $publishRoot 'setup'
dotnet restore (Join-Path $root 'SpywareMonitor.sln')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
dotnet test (Join-Path $root 'SpywareMonitor.sln') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed' }
dotnet publish (Join-Path $root 'src\SpywareMonitor.Service\SpywareMonitor.Service.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $serviceOut
if ($LASTEXITCODE -ne 0) { throw 'service publish failed' }
dotnet publish (Join-Path $root 'src\SpywareMonitor.App\SpywareMonitor.App.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'app publish failed' }
$portableService = Join-Path $appOut 'service'
New-Item -ItemType Directory -Force -Path $portableService | Out-Null
Copy-Item (Join-Path $serviceOut '*') $portableService -Recurse -Force
dotnet publish (Join-Path $root 'src\SpywareMonitor.Setup\SpywareMonitor.Setup.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:EmbedPayload=true "-p:PayloadRoot=$publishRoot" -o $setupOut
if ($LASTEXITCODE -ne 0) { throw 'setup publish failed' }

$release = Join-Path $root "Builds\v$version"
New-Item -ItemType Directory -Force -Path $release | Out-Null
$setupTarget = Join-Path $release "PC-Pressure-Monitor-Setup-$version-$Runtime.exe"
Copy-Item (Join-Path $setupOut 'SpywareMonitor.Setup.exe') $setupTarget -Force
$portableTarget = Join-Path $release "PC-Pressure-Monitor-Portable-$version-$Runtime.zip"
if (Test-Path $portableTarget) { Remove-Item -LiteralPath $portableTarget -Force }
Compress-Archive -Path (Join-Path $appOut '*') -DestinationPath $portableTarget -CompressionLevel Optimal
$hashLines = @($setupTarget, $portableTarget) | ForEach-Object { $hash = Get-FileHash $_ -Algorithm SHA256; "$($hash.Hash.ToLower())  $([IO.Path]::GetFileName($_))" }
$hashLines | Set-Content (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Build ready: $out" -ForegroundColor Green
Write-Host "Release ready: $release" -ForegroundColor Green
