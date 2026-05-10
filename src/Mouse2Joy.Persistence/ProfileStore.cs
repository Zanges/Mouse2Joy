using System.Text.Json;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence;

public sealed class ProfileStore
{
    public IReadOnlyList<Profile> LoadAll()
    {
        AppPaths.EnsureDirectories();
        var results = new List<Profile>();
        foreach (var file in Directory.EnumerateFiles(AppPaths.ProfilesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions.Default);
                if (profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
                    results.Add(profile);
            }
            catch (JsonException)
            {
                // Skip corrupt files; they remain on disk for the user to inspect.
            }
        }
        return results;
    }

    public Profile? Load(string name)
    {
        var path = AppPaths.ProfileFilePath(name);
        if (!File.Exists(path))
            return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Profile>(json, JsonOptions.Default);
    }

    public void Save(Profile profile)
    {
        AppPaths.EnsureDirectories();
        var path = AppPaths.ProfileFilePath(profile.Name);
        var json = JsonSerializer.Serialize(profile, JsonOptions.Default);
        AtomicFile.WriteAllText(path, json);
    }

    public void Delete(string name)
    {
        var path = AppPaths.ProfileFilePath(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void Rename(string oldName, string newName)
    {
        var profile = Load(oldName) ?? throw new FileNotFoundException($"Profile '{oldName}' not found.");
        var renamed = profile with { Name = newName };
        Save(renamed);
        if (!string.Equals(AppPaths.SanitizeProfileFileName(oldName), AppPaths.SanitizeProfileFileName(newName), StringComparison.OrdinalIgnoreCase))
            Delete(oldName);
    }
}
