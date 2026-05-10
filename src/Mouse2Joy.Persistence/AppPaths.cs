namespace Mouse2Joy.Persistence;

public static class AppPaths
{
    public const string AppFolderName = "Mouse2Joy";

    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);

    public static string ProfilesDirectory { get; } = Path.Combine(AppDataRoot, "profiles");

    public static string LogsDirectory { get; } = Path.Combine(AppDataRoot, "logs");

    public static string SettingsFile { get; } = Path.Combine(AppDataRoot, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// Replace characters that aren't safe in a Windows filename. Display name
    /// authoritative source is the JSON; this is only the on-disk handle.
    /// </summary>
    public static string SanitizeProfileFileName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return "_unnamed";

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(profileName.Select(c => invalid.Contains(c) ? '_' : c));
        sanitized = sanitized.Trim().TrimEnd('.');
        if (sanitized.Length == 0)
            sanitized = "_unnamed";
        if (sanitized.Length > 120)
            sanitized = sanitized[..120];
        return sanitized;
    }

    public static string ProfileFilePath(string profileName)
        => Path.Combine(ProfilesDirectory, SanitizeProfileFileName(profileName) + ".json");
}
