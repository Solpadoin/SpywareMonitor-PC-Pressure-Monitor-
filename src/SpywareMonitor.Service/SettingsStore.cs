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
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(SettingsPath), MonitorJson.Options) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save(MonitorSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, MonitorJson.Options));
        File.Move(temp, SettingsPath, true);
    }
}
