using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("PC Pressure Monitor Uninstall")]
[assembly: System.Reflection.AssemblyProduct("PC Pressure Monitor")]
[assembly: System.Reflection.AssemblyVersion("1.0.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.1.0")]

internal static class Program
{
    private const string ProductName = "PC Pressure Monitor";
    private const string ServiceName = "SpywareMonitor";
    private const string DriverServiceName = "SpywareMonitorDriver";
    private static readonly string ProductDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpywareMonitor");
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "PCPressureMonitor-Uninstall.log");
    private static string InstallRoot = "";

    [STAThread]
    private static void Main()
    {
        bool first;
        using (var mutex = new Mutex(true, @"Global\PCPressureMonitor.Uninstaller", out first))
        {
            if (!first) { MessageBox.Show("PC Pressure Monitor Uninstall is already running.", ProductName); return; }
            InstallRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (MessageBox.Show("Remove the monitoring service, application, and all snapshot logs?", "Uninstall " + ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallForm());
        }
    }

    private sealed class UninstallForm : Form
    {
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private bool _complete;

        public UninstallForm()
        {
            Text = "Uninstall " + ProductName;
            Width = 520; Height = 225; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(9, 13, 23); ForeColor = Color.FromArgb(238, 242, 250);
            var title = new Label { Text = "Removing PC Pressure Monitor", Left = 28, Top = 25, Width = 440, Height = 30, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = ForeColor };
            var detail = new Label { Text = "Service → application → snapshot logs", Left = 30, Top = 62, Width = 440, Height = 22, Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(145, 157, 178) };
            _progress = new ProgressBar { Left = 30, Top = 105, Width = 440, Height = 7, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25 };
            _status = new Label { Text = "Preparing…", Left = 30, Top = 127, Width = 440, Height = 28, Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(145, 157, 178) };
            Controls.Add(title); Controls.Add(detail); Controls.Add(_progress); Controls.Add(_status);
            Shown += delegate { Task.Run((Action)Uninstall); };
            FormClosing += OnClosing;
        }

        private void Uninstall()
        {
            try
            {
                SetStatus("Removing monitoring services…");
                StopDeleteService(DriverServiceName);
                StopDeleteService(ServiceName);
                SetStatus("Closing the desktop application…");
                CloseApplication();
                SetStatus("Removing application files…");
                DeleteDirectory(Path.Combine(InstallRoot, "app"));
                DeleteDirectory(Path.Combine(InstallRoot, "service"));
                SetStatus("Removing snapshot logs…");
                DeleteLogs();
                RemoveRegistration();
                Log("Uninstall completed.");
                BeginInvoke((Action)delegate { _complete = true; _progress.Style = ProgressBarStyle.Blocks; _progress.Value = 100; _status.Text = "Uninstall complete. Closing…"; var timer = new System.Windows.Forms.Timer { Interval = 900 }; timer.Tick += delegate { timer.Stop(); Close(); }; timer.Start(); });
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex);
                BeginInvoke((Action)delegate { _progress.Style = ProgressBarStyle.Blocks; _status.Text = "Uninstall failed. See: " + LogPath; MessageBox.Show(ex.Message, "Uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error); });
            }
        }

        private void SetStatus(string text) { Log(text); BeginInvoke((Action)delegate { _status.Text = text; }); }
        private void OnClosing(object sender, FormClosingEventArgs e) { if (!_complete && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; } if (_complete) ScheduleSelfDelete(); }
    }

    private static void StopDeleteService(string name)
    {
        RunSc("stop", name); Thread.Sleep(900); RunSc("delete", name);
        for (var i = 0; i < 20; i++) { var result = RunSc("query", name); if (result.Item1 == 1060) break; Thread.Sleep(250); }
    }

    private static Tuple<int, string> RunSc(params string[] args)
    {
        var info = new ProcessStartInfo("sc.exe", string.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(info))
        {
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return Tuple.Create(process.ExitCode, output);
        }
    }

    private static string Quote(string value) { return value.IndexOf(' ') >= 0 ? "\"" + value.Replace("\"", "\\\"") + "\"" : value; }

    private static void CloseApplication()
    {
        foreach (var process in Process.GetProcessesByName("SpywareMonitor.App"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule.FileName;
                    if (!path.StartsWith(InstallRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(2500)) process.Kill();
                }
                catch { }
            }
        }
    }

    private static void DeleteLogs()
    {
        var external = GetConfiguredLogDirectory();
        if (!string.IsNullOrWhiteSpace(external) && Directory.Exists(external) && !IsInside(external, ProductDataRoot))
        {
            foreach (var file in Directory.GetFiles(external, "metrics-*.jsonl", SearchOption.TopDirectoryOnly).Concat(Directory.GetFiles(external, "snapshots-*.jsonl", SearchOption.TopDirectoryOnly))) TryDeleteFile(file);
            try { if (!Directory.EnumerateFileSystemEntries(external).Any()) Directory.Delete(external); } catch { }
        }
        DeleteDirectory(ProductDataRoot);
    }

    private static string GetConfiguredLogDirectory()
    {
        try
        {
            var path = Path.Combine(ProductDataRoot, "settings.json");
            if (!File.Exists(path)) return null;
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            object value; return data.TryGetValue("logDirectory", out value) ? Convert.ToString(value) : null;
        }
        catch { return null; }
    }

    private static void RemoveRegistration()
    {
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SpywareMonitor", false);
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ProductName + ".lnk"));
    }

    private static bool IsInside(string path, string parent)
    {
        var child = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return child.Equals(root, StringComparison.OrdinalIgnoreCase) || child.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (Exception ex) { Log("Delete failed: " + path + " - " + ex.Message); } }
    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static void Log(string text) { try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + text + Environment.NewLine); } catch { } }

    private static void ScheduleSelfDelete()
    {
        var safeRoot = Path.GetFullPath(InstallRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (safeRoot.Equals(Path.GetPathRoot(safeRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return;
        var command = "/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"" + safeRoot + "\"";
        Process.Start(new ProcessStartInfo("cmd.exe", command) { UseShellExecute = false, CreateNoWindow = true });
    }
}
