# PC Pressure Monitor

A local Windows application for finding processes that cause persistent stutter and system pressure. Data collection runs as an automatic Windows service; the WPF interface runs without administrator privileges.

## What it monitors

- per-process and system CPU usage;
- working set, private memory, threads, and handles;
- process read/write throughput and aggregate disk I/O;
- unresponsive GUI windows;
- process start/stop events, parent PID, executable path, and command line;
- IPv4 TCP endpoints with local/remote address, port, and state;
- pressure scoring and configurable alerts;
- compact local JSONL history with configurable storage directory and retention.

Every persisted snapshot contains an ISO timestamp and a dedicated `snapshotTime` field in `HH:mm:ss` format. The application does not capture keystrokes, window contents, or user file contents, and sends no telemetry.

Disk history is written every 30 seconds and contains the system summary plus the top 15 pressure sources. Command lines and TCP endpoint arrays remain available in the live interface but are not duplicated into every historical record. Typical disk growth is roughly 8 MB per day instead of hundreds of megabytes.

## Install

Download `PC-Pressure-Monitor-Setup-1.0.1-win-x64.exe` from [GitHub Release v1.0.1](https://github.com/Solpadoin/SpywareMonitor-PC-Pressure-Monitor-/releases/tag/v1.0.1). The graphical setup requests UAC, installs the desktop application and service, enables automatic startup, creates a shortcut, and registers the uninstaller with Windows.

Setup prevents duplicate instances, supports Update/Repair, and lets the user select an installation directory. The dedicated uninstaller removes the monitoring service first, then application files, snapshot logs, and itself.

Checksums and the portable package are published with the release and described under `Builds/v1.0.1`.

## Build from source

Windows 10/11 and .NET SDK 7+ are required for building. Published packages are self-contained.

```powershell
.\build.ps1
```

The script writes intermediate output under `artifacts` and creates the versioned release package under `Builds/v1.0.1`.

For development, run `dotnet run --project src/SpywareMonitor.Service`, then start the WPF project separately.

## Performance safeguards

The default sampling interval is one second and cannot be set below 500 ms. In-memory history and process counts are bounded. Command-line capture, TCP endpoint capture, and disk history can be disabled.

## Tracing scope

The current release records process lifecycle and network destinations. Full system-call or per-file-operation tracing requires a bounded ETW capture session and produces substantial data, so it is intentionally not enabled as permanent background monitoring.
