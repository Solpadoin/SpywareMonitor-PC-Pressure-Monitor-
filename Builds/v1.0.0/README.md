# PC Pressure Monitor 1.0.0

First public Windows release.

Download the generated files from [GitHub Release v1.0.0](https://github.com/Solpadoin/SpywareMonitor-PC-Pressure-Monitor-/releases/tag/v1.0.0). Running `build.ps1` also places local copies directly in this directory; large binaries are not committed to Git history.

## Installer

Run `PC-Pressure-Monitor-Setup-1.0.0-win-x64.exe` and approve the UAC prompt. Setup installs the interface and automatic Windows service, creates a desktop shortcut, and registers the uninstaller with Windows.

## Portable package

Extract the ZIP and start `SpywareMonitor.App.exe`. Use **Install and start** in Settings to register the bundled service.

Both packages are self-contained and do not require a preinstalled .NET runtime.
