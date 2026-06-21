using System.Threading;
using System.Windows;

namespace SpywareMonitor.Setup;

public partial class App : Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, @"Global\PCPressureMonitor.Setup", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("PC Pressure Monitor Setup is already running.", "Setup", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
