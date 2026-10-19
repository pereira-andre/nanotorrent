namespace TorrentClassic.Core;

/// <summary>Prepare a user-selected portable folder without relying on its apparent permissions.</summary>
public static class PortableWorkspace
{
    public static string Prepare(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        VerifyWritable(fullPath);
        var data = Path.Combine(fullPath, "Data");
        VerifyWritable(data);
        return data;
    }

    static void VerifyWritable(string folder)
    {
        Directory.CreateDirectory(folder);
        var probe = Path.Combine(folder, ".torrentclassic-write-test-" + Guid.NewGuid().ToString("N"));
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        stream.WriteByte(0);
    }
}

/// <summary>Reporting an error must never hide it behind another filesystem failure.</summary>
public static class StartupDiagnostics
{
    public static string? Write(Exception error, params string[] candidateFolders)
    {
        foreach (var folder in candidateFolders)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "startup-error.log");
                File.WriteAllText(path, DateTimeOffset.Now.ToString("O") + Environment.NewLine + error);
                return path;
            }
            catch (Exception) { /* Try the next writable location; logging is best effort. */ }
        }
        return null;
    }
}
