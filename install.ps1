#Requires -RunAsAdministrator
[CmdletBinding()]
param([switch]$NoStart)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'artifacts\publish\v1.0.0\win-x64'
if (!(Test-Path (Join-Path $source 'service\SpywareMonitor.Service.exe'))) { throw 'Run .\build.ps1 first.' }
$target = Join-Path $env:ProgramFiles 'PC Pressure Monitor'
$serviceDir = Join-Path $target 'service'
$appDir = Join-Path $target 'app'

$existing = Get-Service -Name 'SpywareMonitor' -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name 'SpywareMonitor' -Force; $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20)) }
    sc.exe delete SpywareMonitor | Out-Null
    Start-Sleep -Milliseconds 700
}
New-Item -ItemType Directory -Force -Path $serviceDir, $appDir | Out-Null
Copy-Item (Join-Path $source 'service\*') $serviceDir -Recurse -Force
Copy-Item (Join-Path $source 'app\*') $appDir -Recurse -Force
$exe = Join-Path $serviceDir 'SpywareMonitor.Service.exe'
sc.exe create SpywareMonitor binPath= "`"$exe`"" start= auto DisplayName= "PC Pressure Monitor" | Out-Null
sc.exe description SpywareMonitor "Local CPU, memory, disk and process pressure diagnostics" | Out-Null
sc.exe failure SpywareMonitor reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
if (!$NoStart) { Start-Service SpywareMonitor }

$desktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $desktop 'PC Pressure Monitor.lnk'))
$shortcut.TargetPath = Join-Path $appDir 'SpywareMonitor.App.exe'
$shortcut.WorkingDirectory = $appDir
$shortcut.Save()
Write-Host "Installed. UI: $($shortcut.TargetPath)" -ForegroundColor Green
