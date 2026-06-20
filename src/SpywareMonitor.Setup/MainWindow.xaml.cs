using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SpywareMonitor.Setup;

public partial class MainWindow : Window
{
    private const string ProductName = "PC Pressure Monitor";
    private const string ServiceName = "SpywareMonitor";
    private static readonly string InstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);
    private readonly bool _uninstall = Environment.GetCommandLineArgs().Any(x => x.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

    public MainWindow()
    {
        InitializeComponent();
        LocationText.Text = InstallRoot;
        if (!_uninstall) return;
        Title = $"Удаление {ProductName}"; HeaderSubtitle.Text = "Удаление версии 1.0.0"; TitleText.Text = "Удалить PC Pressure Monitor?";
        DescriptionText.Text = "Служба мониторинга будет остановлена и удалена вместе с приложением. Собранная история в ProgramData останется на компьютере.";
        ActionButton.Content = "Удалить"; ActionButton.Background = System.Windows.Media.Brushes.IndianRed;
    }

    private async void ActionClick(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            if (_uninstall) await Task.Run(Uninstall); else await Task.Run(Install);
            Progress.Visibility = Visibility.Collapsed; StatusText.Text = _uninstall ? "Приложение удалено." : "Установка завершена.";
            ActionButton.Content = _uninstall ? "Закрыть" : "Запустить"; ActionButton.IsEnabled = true; ActionButton.Click -= ActionClick; ActionButton.Click += FinishClick; CancelButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SetBusy(false); StatusText.Text = "Ошибка: " + ex.Message; MessageBox.Show(ex.ToString(), "Ошибка установки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Install()
    {
        UpdateStatus("Остановка предыдущей версии…");
        RunSc(false, "stop", ServiceName); Thread.Sleep(1200); RunSc(false, "delete", ServiceName); Thread.Sleep(500);
        var appDir = Path.Combine(InstallRoot, "app"); var serviceDir = Path.Combine(InstallRoot, "service");
        Directory.CreateDirectory(appDir); Directory.CreateDirectory(serviceDir);
        UpdateStatus("Распаковка приложения…");
        Extract("Payload.SpywareMonitor.App.exe", Path.Combine(appDir, "SpywareMonitor.App.exe"));
        Extract("Payload.SpywareMonitor.Service.exe", Path.Combine(serviceDir, "SpywareMonitor.Service.exe"));
        var currentSetup = Environment.ProcessPath ?? throw new InvalidOperationException("Не найден путь установщика");
        File.Copy(currentSetup, Path.Combine(InstallRoot, "Uninstall.exe"), true);
        UpdateStatus("Регистрация системной службы…");
        var serviceExe = Path.Combine(serviceDir, "SpywareMonitor.Service.exe");
        RunSc(true, "create", ServiceName, "binPath=", $"\"{serviceExe}\"", "start=", "auto", "DisplayName=", ProductName);
        RunSc(false, "description", ServiceName, "Local CPU, memory, disk and process pressure diagnostics");
        RunSc(false, "failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000");
        RunSc(true, "start", ServiceName);
        CreateShortcut(Path.Combine(appDir, "SpywareMonitor.App.exe")); RegisterUninstaller();
    }

    private void Uninstall()
    {
        UpdateStatus("Остановка службы…"); RunSc(false, "stop", ServiceName); Thread.Sleep(1200); RunSc(false, "delete", ServiceName);
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ProductName + ".lnk");
        if (File.Exists(shortcut)) File.Delete(shortcut);
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SpywareMonitor", false);
        var cmd = $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallRoot}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
    }

    private static void Extract(string resource, string target)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Установочный пакет повреждён: {resource}");
        using var output = File.Create(target); source.CopyTo(output);
    }

    private static void RunSc(bool required, params string[] args)
    {
        var info = new ProcessStartInfo("sc.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить Service Control Manager"); process.WaitForExit(30000);
        if (required && process.ExitCode != 0) throw new InvalidOperationException(process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
    }

    private static void CreateShortcut(string appPath)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host недоступен");
        dynamic shell = Activator.CreateInstance(type)!; dynamic shortcut = shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ProductName + ".lnk"));
        shortcut.TargetPath = appPath; shortcut.WorkingDirectory = Path.GetDirectoryName(appPath); shortcut.Description = ProductName; shortcut.Save();
    }

    private static void RegisterUninstaller()
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SpywareMonitor");
        key.SetValue("DisplayName", ProductName); key.SetValue("DisplayVersion", "1.0.0"); key.SetValue("Publisher", "Solpadoin"); key.SetValue("InstallLocation", InstallRoot);
        key.SetValue("UninstallString", $"\"{Path.Combine(InstallRoot, "Uninstall.exe")}\" --uninstall"); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private void SetBusy(bool value) { ActionButton.IsEnabled = !value; CancelButton.IsEnabled = !value; Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed; StatusText.Text = value ? "Подготовка…" : ""; }
    private void UpdateStatus(string value) => Dispatcher.Invoke(() => StatusText.Text = value);
    private void FinishClick(object sender, RoutedEventArgs e) { if (!_uninstall) Process.Start(new ProcessStartInfo(Path.Combine(InstallRoot, "app", "SpywareMonitor.App.exe")) { UseShellExecute = true }); Close(); }
    private void CancelClick(object sender, RoutedEventArgs e) => Close();
}
