using System.Text.Json;

namespace CodexMonitor_App;

public sealed class MonitorSettings
{
    public bool AlwaysOnTop { get; set; }
    public int RefreshIntervalSeconds { get; set; } = 60;

    public static MonitorSettings Load(string directory)
    {
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var settings = JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(path)) ?? new();
            settings.RefreshIntervalSeconds = Math.Clamp(settings.RefreshIntervalSeconds, 1, 99999);
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
