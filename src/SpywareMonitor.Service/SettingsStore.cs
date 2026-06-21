using System.Text.Json;
using SpywareMonitor.Core;

namespace SpywareMonitor.Service;

public sealed class SettingsStore
{
    public string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpywareMonitor");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public SettingsStore() => Directory.CreateDirectory(DataDirectory);

    public MonitorSettings Load()
    {
        try
        {
            var settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(SettingsPath), MonitorJson.Options) ?? new()
                : new();
            var logOverride = Environment.GetEnvironmentVariable("SPYWARE_MONITOR_LOG_DIRECTORY");
            return string.IsNullOrWhiteSpace(logOverride) ? settings : settings with { LogDirectory = Path.GetFullPath(logOverride) };
        }
        catch { return new(); }
    }

    public void Save(MonitorSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(settings.LogDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, MonitorJson.Options));
        File.Move(temp, SettingsPath, true);
    }
}
