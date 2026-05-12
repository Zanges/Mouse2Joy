using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// Peek the schemaVersion field on the JSON, then deserialize through a
    /// chained migration pipeline. Migrations run in order, each bringing the
    /// in-memory JSON up to the next schema version. The next Save() persists
    /// the migrated document in the current shape.
    ///
    /// <para>Pipeline:</para>
    /// <list type="bullet">
    ///   <item>v1 (or unversioned) → v2: typed-record rebuild via
    ///   <see cref="V1ToV2"/> (structural change).</item>
    ///   <item>v2 → v3: JSON-node rewrite via <see cref="V2ToV3"/>
    ///   (sensitivity → outputScale rename).</item>
    ///   <item>v3 → v4: version-stamp bump via <see cref="V3ToV4"/>
    ///   (defaulted <c>transitionStyle</c> field on segmentedResponseCurve;
    ///   constructor default handles deserialization, migration just bumps
    ///   the stamp).</item>
    ///   <item>v4 → v5: version-stamp bump via <see cref="V4ToV5"/>
    ///   (defaulted <c>shape</c> field plus two new transitionStyle values;
    ///   constructor default handles deserialization).</item>
    ///   <item>v5 → v6: version-stamp bump via <see cref="V5ToV6"/>
    ///   (added <c>parametricCurve</c> modifier kind; no existing profiles
    ///   to rewrite).</item>
    ///   <item>v6 → v7: version-stamp bump via <see cref="V6ToV7"/>
    ///   (added <c>curveEditor</c> modifier kind; no existing profiles
    ///   to rewrite).</item>
    /// </list>
    /// </summary>
    internal static Profile? DeserializeProfile(string json)
    {
        var version = PeekSchemaVersion(json);

        // v1 → v2: structural migration through legacy types. Re-serialize the
        // v2 result so subsequent JSON-node migrations operate on uniform input.
        if (version < 2)
        {
            var legacy = JsonSerializer.Deserialize<LegacyProfile>(json, JsonOptions.Default);
            if (legacy is null) return null;
            var v2 = V1ToV2.Migrate(legacy);
            json = JsonSerializer.Serialize(v2, JsonOptions.Default);
            version = 2;
        }

        // v2 → v3: JSON-node rewrite (sensitivity → outputScale).
        if (version < 3)
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;
            V2ToV3.Apply(node);
            json = node.ToJsonString(JsonOptions.Default);
            version = 3;
        }

        // v3 → v4: version-stamp bump (defaulted transitionStyle field).
        if (version < 4)
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;
            V3ToV4.Apply(node);
            json = node.ToJsonString(JsonOptions.Default);
            version = 4;
        }

        // v4 → v5: version-stamp bump (defaulted shape field + new style values).
        if (version < 5)
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;
            V4ToV5.Apply(node);
            json = node.ToJsonString(JsonOptions.Default);
            version = 5;
        }

        // v5 → v6: version-stamp bump (added parametricCurve modifier kind).
        if (version < 6)
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;
            V5ToV6.Apply(node);
            json = node.ToJsonString(JsonOptions.Default);
            version = 6;
        }

        // v6 → v7: version-stamp bump (added curveEditor modifier kind).
        if (version < 7)
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;
            V6ToV7.Apply(node);
            json = node.ToJsonString(JsonOptions.Default);
            version = 7;
        }

        return JsonSerializer.Deserialize<Profile>(json, JsonOptions.Default);
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
