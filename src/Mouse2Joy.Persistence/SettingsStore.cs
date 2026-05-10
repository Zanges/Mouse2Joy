using System.Text.Json;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence;

public sealed class SettingsStore
{
    public AppSettings Load()
    {
        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.SettingsFile))
            return new AppSettings();
        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions.Default) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureDirectories();
        var json = JsonSerializer.Serialize(settings, JsonOptions.Default);
        AtomicFile.WriteAllText(AppPaths.SettingsFile, json);
    }
}
