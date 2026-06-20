using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SpywareMonitor.Core;

namespace SpywareMonitor.App;

public partial class MainWindow : Window
{
    private readonly MonitorClient _client = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private SystemSnapshot? _snapshot;
    private Button? _activeNavigationButton;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { _activeNavigationButton = OverviewNav; await LoadStatusAsync(); await RefreshAsync(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void NavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var index)) return;
        SelectPage(index, button);
    }

    private void SelectPage(int index, Button? button = null)
    {
        Tabs.SelectedIndex = index;
        button ??= index switch { 0 => OverviewNav, 1 => ProcessesNav, 2 => EventsNav, _ => SettingsNav };
        if (_activeNavigationButton is not null) _activeNavigationButton.Style = (Style)FindResource("NavButton");
        button.Style = (Style)FindResource("NavButtonActive");
        _activeNavigationButton = button;
        PageTitle.Text = index switch { 0 => "System overview", 1 => "Processes", 2 => "Event log", _ => "Settings" };
    }

    private async Task RefreshAsync()
    {
        try
        {
            _snapshot = await _client.SendAsync<SystemSnapshot>(new("latest"), 1500);
            if (_snapshot is null) return;
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(63, 211, 154)); StatusText.Text = $"Service active · {_snapshot.Timestamp:HH:mm:ss}";
            CpuText.Text = $"{_snapshot.CpuPercent:F0}%"; CpuBar.Value = _snapshot.CpuPercent;
            MemoryText.Text = $"{_snapshot.MemoryPercent:F0}%"; MemoryBar.Value = _snapshot.MemoryPercent;
            IoText.Text = FormatRate(_snapshot.TotalReadBytesPerSecond + _snapshot.TotalWriteBytesPerSecond); IoSubText.Text = $"↓ {FormatRate(_snapshot.TotalReadBytesPerSecond)}  ↑ {FormatRate(_snapshot.TotalWriteBytesPerSecond)}";
            ProcessText.Text = $"{_snapshot.ProcessCount} / {_snapshot.ThreadCount}";
            var rows = _snapshot.Processes.Select(p => new ProcessRow(p)).ToList();
            ProcessesGrid.ItemsSource = rows; TopGrid.ItemsSource = rows.Take(12); AlertsList.ItemsSource = _snapshot.Alerts.Take(20);
            if (Tabs.SelectedIndex == 2) await LoadEventsAsync();
        }
        catch
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(240, 93, 108));
            StatusText.Text = IsServiceInstalled() ? "Service stopped" : "Service not installed";
        }
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            var status = await _client.SendAsync<ServiceStatus>(new("status")); if (status is null) return;
            IntervalBox.Text = status.Settings.SampleIntervalMs.ToString(); RetentionBox.Text = status.Settings.RetentionDays.ToString();
            CommandsCheck.IsChecked = status.Settings.CaptureCommandLines; NetworkCheck.IsChecked = status.Settings.CaptureNetworkEndpoints; HistoryCheck.IsChecked = status.Settings.PersistHistory; DataPathText.Text = status.DataDirectory;
            LogPathBox.Text = status.Settings.LogDirectory;
        }
        catch { }
    }

    private async Task LoadEventsAsync()
    {
        try { var events = await _client.SendAsync<IReadOnlyList<ProcessEvent>>(new("events", 500)); EventsGrid.ItemsSource = events?.Select(e => new EventRow(e)); } catch { }
    }

    private void ProcessSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is not ProcessRow row) return;
        var p = row.Source; DetailName.Text = p.Name; DetailPid.Text = $"PID {p.ProcessId}"; DetailPath.Text = p.Path ?? "Unavailable"; DetailCommand.Text = p.CommandLine ?? "Unavailable";
        EndpointsList.ItemsSource = p.NetworkEndpoints.Select(x => $"{x.Protocol}  {x.RemoteAddress}:{x.RemotePort}  {x.State}");
        DetailStats.Text = $"CPU: {p.CpuPercent:F1}%\nWorking set: {FormatBytes(p.WorkingSetBytes)}\nPrivate memory: {FormatBytes(p.PrivateMemoryBytes)}\nRead: {FormatRate(p.ReadBytesPerSecond)}\nWrite: {FormatRate(p.WriteBytesPerSecond)}\nThreads: {p.ThreadCount} · Handles: {p.HandleCount}\nResponding: {(p.Responding is null ? "no window" : p.Responding.Value ? "yes" : "NO")}";
        SelectPage(1);
    }

    private async void SaveSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out var interval) || !int.TryParse(RetentionBox.Text, out var days)) { MessageBox.Show("Check the numeric settings."); return; }
        if (string.IsNullOrWhiteSpace(LogPathBox.Text) || !Path.IsPathRooted(LogPathBox.Text)) { MessageBox.Show("Select an absolute log directory."); return; }
        try
        {
            var current = (await _client.SendAsync<ServiceStatus>(new("status")))?.Settings ?? new();
            var updated = current with { SampleIntervalMs = interval, RetentionDays = days, LogDirectory = LogPathBox.Text.Trim(), CaptureCommandLines = CommandsCheck.IsChecked == true, CaptureNetworkEndpoints = NetworkCheck.IsChecked == true, PersistHistory = HistoryCheck.IsChecked == true };
            var saved = await _client.SendAsync<MonitorSettings>(new("settings", Settings: updated)); DataPathText.Text = saved?.LogDirectory ?? updated.LogDirectory; MessageBox.Show("Settings saved.", "PC Pressure Monitor");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Unable to save settings"); }
    }

    private void BrowseLogsClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Select the snapshot log directory", UseDescriptionForTitle = true, SelectedPath = Directory.Exists(LogPathBox.Text) ? LogPathBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) LogPathBox.Text = dialog.SelectedPath;
    }

    private void EnableServiceClick(object sender, RoutedEventArgs e)
    {
        var serviceExe = FindServiceExecutable();
        if (serviceExe is null)
        {
            MessageBox.Show("SpywareMonitor.Service.exe was not found. Use the portable package or reinstall the application.", "Unable to install service");
            return;
        }
        var escaped = serviceExe.Replace("'", "''");
        RunElevated($"$s=Get-Service -Name '{MonitorConstants.ServiceName}' -ErrorAction SilentlyContinue; if(-not $s){{ sc.exe create {MonitorConstants.ServiceName} binPath= '\"{escaped}\"' start= auto DisplayName= 'PC Pressure Monitor' | Out-Null; sc.exe description {MonitorConstants.ServiceName} 'Local system performance diagnostics' | Out-Null; sc.exe failure {MonitorConstants.ServiceName} reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null }} else {{ sc.exe config {MonitorConstants.ServiceName} start= auto | Out-Null }}; Start-Service {MonitorConstants.ServiceName}; Start-Sleep -Seconds 1");
    }
    private void DisableServiceClick(object sender, RoutedEventArgs e) => RunElevated($"Stop-Service {MonitorConstants.ServiceName} -Force -ErrorAction SilentlyContinue; sc.exe config {MonitorConstants.ServiceName} start= disabled | Out-Null");
    private void RestartServiceClick(object sender, RoutedEventArgs e) => RunElevated($"Restart-Service {MonitorConstants.ServiceName} -Force");
    private static string? FindServiceExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[] { Path.Combine(baseDir, "SpywareMonitor.Service.exe"), Path.GetFullPath(Path.Combine(baseDir, "..", "service", "SpywareMonitor.Service.exe")), Path.Combine(baseDir, "service", "SpywareMonitor.Service.exe") };
        return candidates.FirstOrDefault(File.Exists);
    }
    private static bool IsServiceInstalled()
    {
        try { using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{MonitorConstants.ServiceName}"); return key is not null; } catch { return false; }
    }
    private static void RunElevated(string command)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}") { Verb = "runas", UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Operation cancelled"); }
    }
    private static string FormatBytes(long n) => n >= 1L << 30 ? $"{n / (double)(1L << 30):F2} GB" : n >= 1L << 20 ? $"{n / (double)(1L << 20):F1} MB" : $"{n / 1024d:F0} KB";
    private static string FormatRate(long n) => FormatBytes(n) + "/s";

    public sealed class ProcessRow
    {
        public ProcessSnapshot Source { get; } public string Name => Source.Name; public int ProcessId => Source.ProcessId; public int ThreadCount => Source.ThreadCount; public int HandleCount => Source.HandleCount;
        public string CpuDisplay => $"{Source.CpuPercent:F1}%"; public string MemoryDisplay => FormatBytes(Source.WorkingSetBytes); public string PrivateDisplay => FormatBytes(Source.PrivateMemoryBytes); public string ReadDisplay => FormatRate(Source.ReadBytesPerSecond); public string WriteDisplay => FormatRate(Source.WriteBytesPerSecond); public string IoDisplay => FormatRate(Source.ReadBytesPerSecond + Source.WriteBytesPerSecond); public string ScoreDisplay => $"{Source.PressureScore:F0}";
        public ProcessRow(ProcessSnapshot source) => Source = source;
    }
    public sealed class EventRow
    {
        public int ProcessId { get; } public int? ParentProcessId { get; } public string Name { get; } public string TimeDisplay { get; } public string KindDisplay { get; } public string Description { get; }
        public EventRow(ProcessEvent e) { ProcessId = e.ProcessId; ParentProcessId = e.ParentProcessId; Name = e.Name; TimeDisplay = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"); KindDisplay = e.Kind == "started" ? "Started" : "Stopped"; Description = e.CommandLine ?? e.Path ?? ""; }
    }
}
