namespace Mouse2Joy.Persistence;

internal static class AtomicFile
{
    /// <summary>
    /// Write text to <paramref name="path"/> via a temp file + replace. Avoids
    /// leaving a half-written file if the process dies mid-write.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
