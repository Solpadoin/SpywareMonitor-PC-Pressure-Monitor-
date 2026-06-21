using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("PC Pressure Monitor Setup")]
[assembly: AssemblyProduct("PC Pressure Monitor")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

internal static class Program
{
    private const string ProductName = "PC Pressure Monitor";
    private const string ServiceName = "SpywareMonitor";
    private static readonly string DefaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);
    private static readonly string SetupLog = Path.Combine(Path.GetTempPath(), "PCPressureMonitor-Setup.log");

    [STAThread]
    private static void Main()
    {
        bool first;
        using (var mutex = new Mutex(true, @"Global\PCPressureMonitor.Setup", out first))
        {
            if (!first) { MessageBox.Show("PC Pressure Monitor Setup is already running.", "Setup", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }
    }

    private sealed class InstallerForm : Form
    {
        private readonly TextBox _path;
        private readonly Button _browse;
        private readonly Button _action;
        private readonly Button _cancel;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly string _registeredRoot;
        private string _installRoot;

        public InstallerForm()
        {
            _registeredRoot = GetRegisteredRoot();
            _installRoot = _registeredRoot ?? DefaultRoot;
            Text = "PC Pressure Monitor Setup"; Width = 650; Height = 445; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(9, 13, 23); ForeColor = Color.FromArgb(238, 242, 250);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new Panel { Left = 0, Top = 0, Width = 650, Height = 82, BackColor = Color.FromArgb(13, 20, 34) };
            var logo = new PictureBox { Left = 27, Top = 19, Width = 44, Height = 44, SizeMode = PictureBoxSizeMode.StretchImage };
            try { logo.Image = Icon.ToBitmap(); } catch { }
            header.Controls.Add(logo);
            header.Controls.Add(NewLabel("PC PRESSURE MONITOR", 84, 18, 350, 25, 13, true, Color.FromArgb(238, 242, 250)));
            header.Controls.Add(NewLabel(_registeredRoot == null ? "Installing version 1.0.1" : "Update or repair version 1.0.1", 84, 45, 350, 20, 9, false, Color.FromArgb(137, 150, 172)));
            Controls.Add(header);

            Controls.Add(NewLabel(_registeredRoot == null ? "Ready to install" : "Ready to update or repair", 32, 108, 560, 35, 20, true, ForeColor));
            Controls.Add(NewLabel("The desktop application and automatic monitoring service will be installed. Existing logs are preserved during updates.", 34, 151, 560, 42, 10, false, Color.FromArgb(154, 167, 186)));
            Controls.Add(NewLabel("INSTALL LOCATION", 34, 213, 300, 20, 8, true, Color.FromArgb(113, 128, 154)));

            _path = new TextBox { Left = 34, Top = 237, Width = 475, Height = 32, Text = _installRoot, BackColor = Color.FromArgb(11, 18, 32), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
            _browse = NewButton("Browse", 519, 235, 91, 36, Color.FromArgb(38, 50, 72)); _browse.Click += Browse;
            Controls.Add(_path); Controls.Add(_browse);

            _progress = new ProgressBar { Left = 34, Top = 292, Width = 576, Height = 7, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Visible = false };
            _status = NewLabel("", 34, 310, 576, 24, 9, false, Color.FromArgb(137, 150, 172));
            Controls.Add(_progress); Controls.Add(_status);

            _cancel = NewButton("Cancel", 398, 355, 95, 40, Color.FromArgb(38, 50, 72)); _cancel.Click += delegate { Close(); };
            _action = NewButton(_registeredRoot == null ? "Install" : "Update / Repair", 503, 355, 107, 40, Color.FromArgb(108, 124, 255)); _action.Click += StartInstall;
            Controls.Add(_cancel); Controls.Add(_action);
            Log("Installer opened. ExistingRoot=" + (_registeredRoot ?? "none"));
        }

        private async void StartInstall(object sender, EventArgs e)
        {
            try { _installRoot = ValidateRoot(_path.Text); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid install location", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SetBusy(true);
            try
            {
                await Task.Run((Action)Install);
                _progress.Style = ProgressBarStyle.Blocks; _progress.Value = 100; _status.Text = "Installation complete.";
                _action.Text = "Launch"; _action.Enabled = true; _action.Click -= StartInstall; _action.Click += Launch; _cancel.Visible = false;
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex); SetBusy(false); _status.Text = "Installation failed.";
                MessageBox.Show(ex.Message + "\r\n\r\nDetailed log: " + SetupLog, "Setup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Install()
        {
            SetStatus("Closing the running application…"); CloseApplication(_registeredRoot, _installRoot);
            SetStatus("Stopping the monitoring service…"); StopDeleteService();
            if (!string.IsNullOrEmpty(_registeredRoot) && !SamePath(_registeredRoot, _installRoot)) DeleteDirectory(_registeredRoot);
            var appDir = Path.Combine(_installRoot, "app"); var serviceDir = Path.Combine(_installRoot, "service");
            Directory.CreateDirectory(appDir); Directory.CreateDirectory(serviceDir);
            SetStatus("Extracting application files…"); ExtractPayload(appDir, serviceDir);
            SetStatus("Registering and starting the service…");
            var serviceExe = Path.Combine(serviceDir, "SpywareMonitor.Service.exe");
            RunSc(true, "create", ServiceName, "binPath=", "\"" + serviceExe + "\"", "start=", "auto", "DisplayName=", ProductName);
            RunSc(false, "description", ServiceName, "Local CPU, memory, disk and process pressure diagnostics");
            RunSc(false, "failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000");
            RunSc(true, "start", ServiceName);
            SetStatus("Creating shortcut and uninstall entry…"); CreateShortcut(Path.Combine(appDir, "SpywareMonitor.App.exe")); RegisterUninstaller();
            Log("Installation completed: " + _installRoot);
        }

        private void ExtractPayload(string appDir, string serviceDir)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.SetupPayload.zip"))
            {
                if (stream == null) throw new InvalidOperationException("The setup payload is missing.");
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
                {
                    Extract(archive, "app/SpywareMonitor.App.exe", Path.Combine(appDir, "SpywareMonitor.App.exe"));
                    Extract(archive, "service/SpywareMonitor.Service.exe", Path.Combine(serviceDir, "SpywareMonitor.Service.exe"));
                    Extract(archive, "Uninstall.exe", Path.Combine(_installRoot, "Uninstall.exe"));
                }
            }
        }

        private static void Extract(ZipArchive archive, string name, string target)
        {
            var entry = archive.Entries.FirstOrDefault(x => x.FullName.Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) throw new InvalidOperationException("The setup payload is damaged: " + name);
            var temp = target + ".new";
            using (var input = entry.Open()) using (var output = File.Create(temp)) input.CopyTo(output);
            if (File.Exists(target)) File.Delete(target); File.Move(temp, target);
        }

        private void Browse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose the PC Pressure Monitor install location", SelectedPath = Directory.Exists(_path.Text) ? _path.Text : DefaultRoot })
                if (dialog.ShowDialog() == DialogResult.OK) _path.Text = dialog.SelectedPath;
        }

        private void Launch(object sender, EventArgs e) { Process.Start(Path.Combine(_installRoot, "app", "SpywareMonitor.App.exe")); Close(); }
        private void SetBusy(bool busy) { _path.Enabled = !busy; _browse.Enabled = !busy; _action.Enabled = !busy; _cancel.Enabled = !busy; _progress.Visible = busy; _status.Text = busy ? "Preparing installation…" : ""; }
        private void SetStatus(string text) { Log(text); BeginInvoke((Action)delegate { _status.Text = text; }); }

        private void RegisterUninstaller()
        {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SpywareMonitor"))
            {
                key.SetValue("DisplayName", ProductName); key.SetValue("DisplayVersion", "1.0.1"); key.SetValue("Publisher", "Solpadoin"); key.SetValue("InstallLocation", _installRoot);
                key.SetValue("DisplayIcon", Path.Combine(_installRoot, "app", "SpywareMonitor.App.exe")); key.SetValue("UninstallString", "\"" + Path.Combine(_installRoot, "Uninstall.exe") + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        private static Button NewButton(string text, int left, int top, int width, int height, Color color) { return new Button { Text = text, Left = left, Top = top, Width = width, Height = height, BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold) }; }
        private static Label NewLabel(string text, int left, int top, int width, int height, float size, bool bold, Color color) { return new Label { Text = text, Left = left, Top = top, Width = width, Height = height, ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) }; }
    }

    private static string ValidateRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value)) throw new InvalidOperationException("Choose an absolute install location.");
        var full = Path.GetFullPath(value.Trim()).TrimEnd(Path.DirectorySeparatorChar); var drive = Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar);
        if (full.Equals(drive, StringComparison.OrdinalIgnoreCase) || full.Equals(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Windows and drive root directories cannot be used.");
        return full;
    }

    private static void CloseApplication(params string[] roots)
    {
        foreach (var process in Process.GetProcessesByName("SpywareMonitor.App")) using (process) try { var path = process.MainModule.FileName; if (!roots.Any(x => !string.IsNullOrEmpty(x) && path.StartsWith(x, StringComparison.OrdinalIgnoreCase))) continue; process.CloseMainWindow(); if (!process.WaitForExit(2500)) process.Kill(); } catch { }
    }

    private static void StopDeleteService()
    {
        RunSc(false, "stop", ServiceName);
        for (var i = 0; i < 20; i++) { var query = RunScCapture("query", ServiceName); if (query.Item1 == 1060 || !query.Item2.Contains("RUNNING")) break; Thread.Sleep(250); }
        RunSc(false, "delete", ServiceName);
        for (var i = 0; i < 20; i++) { if (RunScCapture("query", ServiceName).Item1 == 1060) break; Thread.Sleep(250); }
    }
    private static Tuple<int, string> RunScCapture(params string[] args) { var info = new ProcessStartInfo("sc.exe", string.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; using (var process = Process.Start(info)) { var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("Service Control Manager timed out."); } return Tuple.Create(process.ExitCode, output); } }
    private static void RunSc(bool required, params string[] args) { var result = RunScCapture(args); if (required && result.Item1 != 0) throw new InvalidOperationException(result.Item2.Trim()); }
    private static string Quote(string value) { return value.IndexOf(' ') >= 0 ? "\"" + value.Replace("\"", "\\\"") + "\"" : value; }
    private static bool SamePath(string a, string b) { return Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
    private static void DeleteDirectory(string path) { if (string.IsNullOrEmpty(path) || SamePath(path, Path.GetPathRoot(path))) return; try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    private static string GetRegisteredRoot() { try { using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SpywareMonitor")) return Convert.ToString(key == null ? null : key.GetValue("InstallLocation")); } catch { return null; } }

    private static void CreateShortcut(string appPath)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell"); if (type == null) throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(type); dynamic shortcut = shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ProductName + ".lnk"));
        shortcut.TargetPath = appPath; shortcut.WorkingDirectory = Path.GetDirectoryName(appPath); shortcut.Description = ProductName; shortcut.IconLocation = appPath; shortcut.Save();
    }

    private static void Log(string text) { try { File.AppendAllText(SetupLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + text + Environment.NewLine); } catch { } }
}
