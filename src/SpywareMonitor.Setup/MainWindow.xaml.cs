using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SpywareMonitor.Setup;

public partial class MainWindow : Window
{
    private const string ProductName = "PC Pressure Monitor";
    private const string ServiceName = "SpywareMonitor";
    private const string DriverServiceName = "SpywareMonitorDriver";
    private static readonly string DefaultInstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);
    private static readonly string ProductDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpywareMonitor");
    private static readonly string SetupLogPath = Path.Combine(Path.GetTempPath(), "PCPressureMonitor-Setup.log");
    private readonly bool _uninstall;
    private readonly string? _registeredRoot;
    private string _installRoot = DefaultInstallRoot;
    private bool _operationComplete;
    private bool _deleteAfterClose;

    public MainWindow()
    {
        InitializeComponent();
        var executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        _uninstall = Environment.GetCommandLineArgs().Any(x => x.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || x.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
            || executableName.Equals("Uninstall", StringComparison.OrdinalIgnoreCase);
        _registeredRoot = GetRegisteredInstallRoot();
        _installRoot = _uninstall && executableName.Equals("Uninstall", StringComparison.OrdinalIgnoreCase)
            ? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
            : _registeredRoot ?? DefaultInstallRoot;
        InstallPathBox.Text = _installRoot;
        UpdateSpaceText(_installRoot);

        if (_uninstall)
        {
            Title = $"Uninstall {ProductName}";
            HeaderSubtitle.Text = "Uninstalling version 1.0.0";
            TitleText.Text = "Remove PC Pressure Monitor?";
            DescriptionText.Text = "The monitoring service is removed first, followed by the application and all snapshot logs.";
            ActionButton.Content = "Uninstall";
            ActionButton.Background = System.Windows.Media.Brushes.IndianRed;
            InstallPathBox.IsReadOnly = true;
            BrowseButton.Visibility = Visibility.Collapsed;
        }
        else if (_registeredRoot is not null)
        {
            HeaderSubtitle.Text = "Update or repair version 1.0.0";
            TitleText.Text = "Ready to update or repair";
            DescriptionText.Text = "Setup will stop the monitoring service, replace application files, and start the service again without touching existing logs.";
            ActionButton.Content = "Update / Repair";
        }

        Log($"Setup opened. Mode={(_uninstall ? "uninstall" : "install")}; Root={_installRoot}");
    }

    private async void ActionClick(object sender, RoutedEventArgs e)
    {
        if (!_uninstall)
        {
            try { _installRoot = ValidateInstallRoot(InstallPathBox.Text); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid install location", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        }

        SetBusy(true);
        try
        {
            if (_uninstall) await Task.Run(UninstallLegacy); else await Task.Run(Install);
            _operationComplete = true;
            Progress.Visibility = Visibility.Collapsed;
            StatusText.Text = _uninstall ? "Application, service, and logs removed." : "Installation complete.";
            ActionButton.Content = _uninstall ? "Close" : "Launch";
            ActionButton.IsEnabled = true;
            ActionButton.Click -= ActionClick;
            ActionButton.Click += FinishClick;
            CancelButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            SetBusy(false);
            StatusText.Text = "Error: " + ex.Message;
            MessageBox.Show($"{ex.Message}\n\nDetailed log: {SetupLogPath}", "Setup error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Install()
    {
        UpdateStatus("Closing the running application…");
        CloseInstalledApps(_registeredRoot, _installRoot);
        UpdateStatus("Stopping the monitoring service…");
        StopAndDeleteService(ServiceName);

        if (_registeredRoot is not null && !PathsEqual(_registeredRoot, _installRoot))
            DeleteInstallDirectory(_registeredRoot);

        var appDir = Path.Combine(_installRoot, "app");
        var serviceDir = Path.Combine(_installRoot, "service");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(serviceDir);

        UpdateStatus("Extracting application files…");
        ExtractPayload(appDir, serviceDir, _installRoot);

        UpdateStatus("Registering the Windows service…");
        var serviceExe = Path.Combine(serviceDir, "SpywareMonitor.Service.exe");
        RunSc(true, "create", ServiceName, "binPath=", $"\"{serviceExe}\"", "start=", "auto", "DisplayName=", ProductName);
        RunSc(false, "description", ServiceName, "Local CPU, memory, disk and process pressure diagnostics");
        RunSc(false, "failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000");
        RunSc(true, "start", ServiceName);

        UpdateStatus("Creating shortcuts and uninstall entry…");
        CreateShortcut(Path.Combine(appDir, "SpywareMonitor.App.exe"));
        RegisterUninstaller(_installRoot);
        Log("Installation completed.");
    }

    // Backward-compatible path for old releases that copied Setup.exe as Uninstall.exe.
    private void UninstallLegacy()
    {
        UpdateStatus("Removing the monitoring service…");
        StopAndDeleteService(DriverServiceName);
        StopAndDeleteService(ServiceName);
        UpdateStatus("Closing and removing the application…");
        CloseInstalledApps(_installRoot);
        TryDeleteDirectory(Path.Combine(_installRoot, "app"));
        TryDeleteDirectory(Path.Combine(_installRoot, "service"));
        UpdateStatus("Removing snapshot logs…");
        DeleteSnapshotLogs();
        RemoveRegistration();
        _deleteAfterClose = true;
        Log("Legacy uninstall completed.");
    }

    private static string ValidateInstallRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value)) throw new InvalidOperationException("Choose an absolute install location.");
        var full = Path.GetFullPath(value.Trim()).TrimEnd(Path.DirectorySeparatorChar);
        var driveRoot = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(full, driveRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(full, Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows and drive root directories cannot be used as the install location.");
        return full;
    }

    private static void CloseInstalledApps(params string?[] roots)
    {
        foreach (var process in Process.GetProcessesByName("SpywareMonitor.App"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null || !roots.Any(root => root is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))) continue;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(2500)) process.Kill(true);
                }
                catch { }
            }
        }
    }

    private static void StopAndDeleteService(string name)
    {
        RunSc(false, "stop", name);
        for (var i = 0; i < 20 && ServiceExists(name) && ServiceRunning(name); i++) Thread.Sleep(250);
        RunSc(false, "delete", name);
        for (var i = 0; i < 20 && ServiceExists(name); i++) Thread.Sleep(250);
    }

    private static bool ServiceExists(string name) => RunScCapture("query", name).ExitCode != 1060;
    private static bool ServiceRunning(string name) => RunScCapture("query", name).Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);

    private static void ExtractPayload(string appDir, string serviceDir, string installRoot)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.SetupPayload.zip") ?? throw new InvalidOperationException("The setup payload is missing.");
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, false);
        ExtractEntry(archive, "app/SpywareMonitor.App.exe", Path.Combine(appDir, "SpywareMonitor.App.exe"));
        ExtractEntry(archive, "service/SpywareMonitor.Service.exe", Path.Combine(serviceDir, "SpywareMonitor.Service.exe"));
        ExtractEntry(archive, "Uninstall.exe", Path.Combine(installRoot, "Uninstall.exe"));
    }

    private static void ExtractEntry(ZipArchive archive, string entryName, string target)
    {
        var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(entryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The setup payload is damaged: {entryName}");
        var temp = target + ".new";
        using (var source = entry.Open())
        using (var output = File.Create(temp)) source.CopyTo(output);
        File.Move(temp, target, true);
    }

    private static (int ExitCode, string Output) RunScCapture(params string[] args)
    {
        var info = new ProcessStartInfo("sc.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Unable to start Service Control Manager.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("Service Control Manager timed out."); }
        return (process.ExitCode, output);
    }

    private static void RunSc(bool required, params string[] args)
    {
        var result = RunScCapture(args);
        if (required && result.ExitCode != 0) throw new InvalidOperationException(result.Output.Trim());
    }

    private static void DeleteSnapshotLogs()
    {
        var configuredLogDirectory = GetConfiguredLogDirectory();
        if (configuredLogDirectory is not null && Directory.Exists(configuredLogDirectory) && !IsInside(configuredLogDirectory, ProductDataRoot))
        {
            foreach (var file in Directory.EnumerateFiles(configuredLogDirectory, "metrics-*.jsonl", SearchOption.TopDirectoryOnly).Concat(Directory.EnumerateFiles(configuredLogDirectory, "snapshots-*.jsonl", SearchOption.TopDirectoryOnly))) TryDeleteFile(file);
            try { if (!Directory.EnumerateFileSystemEntries(configuredLogDirectory).Any()) Directory.Delete(configuredLogDirectory); } catch { }
        }
        TryDeleteDirectory(ProductDataRoot);
    }

    private static string? GetConfiguredLogDirectory()
    {
        try
        {
            var settingsPath = Path.Combine(ProductDataRoot, "settings.json");
            if (!File.Exists(settingsPath)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return document.RootElement.TryGetProperty("logDirectory", out var value) ? value.GetString() : null;
        }
        catch { return null; }
    }

    private static void DeleteInstallDirectory(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)) return;
        TryDeleteDirectory(root);
    }

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static bool IsInside(string path, string parent) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || PathsEqual(path, parent);
    private static bool PathsEqual(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static string? GetRegisteredInstallRoot()
    {
        try { return Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SpywareMonitor")?.GetValue("InstallLocation")?.ToString(); }
        catch { return null; }
    }

    private static void CreateShortcut(string appPath)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ProductName + ".lnk"));
        shortcut.TargetPath = appPath; shortcut.WorkingDirectory = Path.GetDirectoryName(appPath); shortcut.Description = ProductName; shortcut.IconLocation = appPath; shortcut.Save();
    }

    private static void RegisterUninstaller(string root)
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SpywareMonitor");
        key.SetValue("DisplayName", ProductName); key.SetValue("DisplayVersion", "1.0.0"); key.SetValue("Publisher", "Solpadoin"); key.SetValue("InstallLocation", root);
        key.SetValue("DisplayIcon", Path.Combine(root, "app", "SpywareMonitor.App.exe")); key.SetValue("UninstallString", $"\"{Path.Combine(root, "Uninstall.exe")}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void RemoveRegistration()
    {
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SpywareMonitor", false);
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ProductName + ".lnk"));
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose the PC Pressure Monitor install location", UseDescriptionForTitle = true, SelectedPath = Directory.Exists(InstallPathBox.Text) ? InstallPathBox.Text : DefaultInstallRoot };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { InstallPathBox.Text = dialog.SelectedPath; UpdateSpaceText(dialog.SelectedPath); }
    }

    private void UpdateSpaceText(string path)
    {
        try { var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!); SpaceText.Text = $"{drive.AvailableFreeSpace / 1024d / 1024 / 1024:F1} GB available"; } catch { SpaceText.Text = ""; }
    }

    private void SetBusy(bool value)
    {
        ActionButton.IsEnabled = !value; CancelButton.IsEnabled = !value; InstallPathBox.IsEnabled = !value && !_uninstall; BrowseButton.IsEnabled = !value;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed; StatusText.Text = value ? "Preparing installation…" : "";
    }

    private void UpdateStatus(string value) { Log(value); Dispatcher.Invoke(() => StatusText.Text = value); }
    private static void Log(string value) { try { File.AppendAllText(SetupLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {value}{Environment.NewLine}"); } catch { } }

    private void FinishClick(object sender, RoutedEventArgs e)
    {
        if (!_uninstall) Process.Start(new ProcessStartInfo(Path.Combine(_installRoot, "app", "SpywareMonitor.App.exe")) { UseShellExecute = true });
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_deleteAfterClose && _operationComplete) ScheduleRootDeletion(_installRoot);
        base.OnClosed(e);
    }

    private static void ScheduleRootDeletion(string root)
    {
        var command = $"/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"{root}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, UseShellExecute = false });
    }
}
