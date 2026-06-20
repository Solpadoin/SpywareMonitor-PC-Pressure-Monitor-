#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$service = Get-Service -Name 'SpywareMonitor' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service SpywareMonitor -Force }
    sc.exe delete SpywareMonitor | Out-Null
}
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'PC Pressure Monitor.lnk'
if (Test-Path $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
Write-Host 'Service removed. History remains in C:\ProgramData\SpywareMonitor.' -ForegroundColor Yellow
