using System.Text.Json;
using Mouse2Joy.Persistence.Legacy.V1;
using Mouse2Joy.Persistence.Migration;
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
                var profile = DeserializeProfile(json);
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
        return DeserializeProfile(json);
    }

    public void Save(Profile profile)
    {
        AppPaths.EnsureDirectories();
        var path = AppPaths.ProfileFilePath(profile.Name);
        // Always write at the current schema version so migrated profiles
        // are persisted in the new shape on first save.
        var current = profile.SchemaVersion == Profile.CurrentSchemaVersion
            ? profile
            : profile with { SchemaVersion = Profile.CurrentSchemaVersion };
        var json = JsonSerializer.Serialize(current, JsonOptions.Default);
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

    /// <summary>
    /// Peek the schemaVersion field on the JSON, then deserialize through
    /// the appropriate path. v1 documents are migrated to v2 in memory; the
    /// next Save() will rewrite them in the new shape.
    /// </summary>
    internal static Profile? DeserializeProfile(string json)
    {
        var version = PeekSchemaVersion(json);
        if (version >= Profile.CurrentSchemaVersion)
            return JsonSerializer.Deserialize<Profile>(json, JsonOptions.Default);

        // v1 (or unversioned, treated as v1) — go through the legacy types.
        var legacy = JsonSerializer.Deserialize<LegacyProfile>(json, JsonOptions.Default);
        if (legacy is null) return null;
        return V1ToV2.Migrate(legacy);
    }

    private static int PeekSchemaVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var n))
                return n;
        }
        catch (JsonException)
        {
            // Fall through to v1 default.
        }
        return 1;
    }
}
