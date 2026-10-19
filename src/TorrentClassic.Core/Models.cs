using System.Text.Json;
using MonoTorrent;
using MonoTorrent.Client;

namespace TorrentClassic.Core;

public sealed record Preferences
{
    public string Theme { get; set; } = "System";
    public string DownloadDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int Port { get; set; } = 51483;
    public int DownloadLimitKiB { get; set; }
    public int UploadLimitKiB { get; set; }
    public int MaximumConnections { get; set; } = 200;
    public bool EnableDht { get; set; } = true;
    public bool EnablePeerExchange { get; set; } = true;
    public bool EnableLocalDiscovery { get; set; } = true;
    public bool EnablePortForwarding { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; }
    public QueueSettings Queue { get; set; } = new();
    public SchedulerSettings Scheduler { get; set; } = new();
    public RemoteSettings Remote { get; set; } = new();

    public void Validate()
    {
        if (Port is < 0 or > 65535) throw new ArgumentException("The port must be between 0 and 65535.");
        if (MaximumConnections is < 5 or > 5000) throw new ArgumentException("Choose between 5 and 5000 connections.");
        if (DownloadLimitKiB is < 0 or > 1000000 || UploadLimitKiB is < 0 or > 1000000)
            throw new ArgumentException("Rate limits must be between 0 and 1,000,000 KiB/s.");
        if (!Path.IsPathFullyQualified(DownloadDirectory)) throw new ArgumentException("Choose a download folder with an absolute path.");
        if (Theme is not ("System" or "Light" or "Dark")) throw new ArgumentException("Unknown theme.");
        Queue.Validate(); Scheduler.Validate(); Remote.Validate();
    }
}

public sealed record SavedTorrent
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string SavePath { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Running { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, Priority> Priorities { get; set; } = [];
    public long QueueOrder { get; set; }
    public bool ForceStarted { get; set; }
    public long Downloaded { get; set; }
    public long Uploaded { get; set; }
    public double SeededSeconds { get; set; }
}

public sealed class TorrentEntry(SavedTorrent saved, TorrentManager manager)
{
    public SavedTorrent Saved { get; } = saved;
    public TorrentManager Manager { get; } = manager;
    public string Id => Saved.Id;
    public string? WaitingReason { get; internal set; }
    internal DateTimeOffset? LastSeedSample { get; set; }
    internal double SessionSeededSeconds { get; set; }
    public long Downloaded => Saved.Downloaded + Manager.Monitor.DataBytesReceived;
    public long Uploaded => Saved.Uploaded + Manager.Monitor.DataBytesSent;
    public double SeededSeconds => Saved.SeededSeconds + SessionSeededSeconds;
    public double ShareRatio => Uploaded / (double)Math.Max(1, Math.Max(Downloaded, Manager.Torrent?.Size ?? 0));
    public string Status => WaitingReason ?? Display.Status(Manager);
}

public static class JsonStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Load<T>(string path, Func<T> fallback, Action<string>? warning = null)
    {
        if (!File.Exists(path)) return fallback();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new JsonException("Empty content"); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Retain the original so an interrupted/corrupt session can be recovered by hand.
            var backup = path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Copy(path, backup, true);
            warning?.Invoke($"Could not read {Path.GetFileName(path)}. A backup was saved to {backup}: {e.Message}");
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Options);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }
}

public static class Display
{
    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes:0.##} {units[i]}";
    }
    public static string Rate(double bytes) => Bytes(bytes) + "/s";
    public static string Duration(TimeSpan time) => time.TotalDays >= 1 ? $"{(int)time.TotalDays} d {time.Hours} h" : time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{(int)time.TotalMinutes} min {time.Seconds} s";
    public static string Status(TorrentManager m) => m.State switch
    {
        TorrentState.Downloading => "Downloading",
        TorrentState.Seeding => "Seeding",
        TorrentState.Paused => "Paused",
        TorrentState.Stopped => "Stopped",
        TorrentState.Stopping => "Stopping…",
        TorrentState.Hashing => "Checking…",
        TorrentState.Metadata => "Downloading metadata…",
        TorrentState.Error => "Error: " + (m.Error?.Exception.Message ?? "unknown"),
        _ => m.State.ToString()
    };
    public static bool Active(TorrentManager m) => m.State is not (TorrentState.Stopped or TorrentState.Stopping or TorrentState.Paused or TorrentState.Error);
    public static string PriorityName(Priority p) => p switch { Priority.DoNotDownload => "Do not download", Priority.High or Priority.Highest or Priority.Immediate => "High", Priority.Low or Priority.Lowest => "Low", _ => "Normal" };
}
