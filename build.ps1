[CmdletBinding()]
param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'artifacts'
$version = '1.0.1'
$publishRoot = Join-Path $out "publish\v$version\$Runtime"
$serviceOut = Join-Path $publishRoot 'service'
$appOut = Join-Path $publishRoot 'app'
$uninstallerOut = Join-Path $publishRoot 'uninstaller'
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
New-Item -ItemType Directory -Force -Path $uninstallerOut | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { throw '.NET Framework C# compiler was not found.' }
$uninstallerManifest = Join-Path $root 'src\SpywareMonitor.Uninstaller\app.manifest'
$uninstallerIcon = Join-Path $root 'src\SpywareMonitor.Setup\Assets\PressureMonitor.ico'
$uninstallerExe = Join-Path $uninstallerOut 'SpywareMonitor.Uninstaller.exe'
$uninstallerSource = Join-Path $root 'src\SpywareMonitor.Uninstaller\Program.cs'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/win32manifest:$uninstallerManifest" "/win32icon:$uninstallerIcon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/out:$uninstallerExe" $uninstallerSource
if ($LASTEXITCODE -ne 0) { throw 'uninstaller build failed' }
$payloadStage = Join-Path $publishRoot 'setup-payload'
$payloadZip = Join-Path $publishRoot 'setup-payload.zip'
if (Test-Path $payloadStage) { Remove-Item -LiteralPath $payloadStage -Recurse -Force }
if (Test-Path $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $payloadStage 'app'),(Join-Path $payloadStage 'service') | Out-Null
Copy-Item (Join-Path $appOut 'SpywareMonitor.App.exe') (Join-Path $payloadStage 'app\SpywareMonitor.App.exe')
Copy-Item (Join-Path $serviceOut 'SpywareMonitor.Service.exe') (Join-Path $payloadStage 'service\SpywareMonitor.Service.exe')
Copy-Item $uninstallerExe (Join-Path $payloadStage 'Uninstall.exe')
Compress-Archive -Path (Join-Path $payloadStage '*') -DestinationPath $payloadZip -CompressionLevel Optimal
New-Item -ItemType Directory -Force -Path $setupOut | Out-Null
$installerManifest = Join-Path $root 'src\SpywareMonitor.Installer\app.manifest'
$installerSource = Join-Path $root 'src\SpywareMonitor.Installer\Program.cs'
$installerExe = Join-Path $setupOut 'SpywareMonitor.Setup.exe'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/win32manifest:$installerManifest" "/win32icon:$uninstallerIcon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll "/resource:$payloadZip,Payload.SetupPayload.zip" "/out:$installerExe" $installerSource
if ($LASTEXITCODE -ne 0) { throw 'installer build failed' }

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
