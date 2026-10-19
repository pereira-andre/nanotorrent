using System.Text.Json;
using MonoTorrent;
using MonoTorrent.Client;

namespace TorrentClassic.Core;

public sealed record Preferences
{
    public string Theme { get; set; } = "Sistema";
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

    public void Validate()
    {
        if (Port is < 0 or > 65535) throw new ArgumentException("A porta deve estar entre 0 e 65535.");
        if (MaximumConnections is < 5 or > 5000) throw new ArgumentException("Escolhe entre 5 e 5000 ligações.");
        if (DownloadLimitKiB is < 0 or > 1000000 || UploadLimitKiB is < 0 or > 1000000)
            throw new ArgumentException("Os limites devem estar entre 0 e 1 000 000 KiB/s.");
        if (!Path.IsPathFullyQualified(DownloadDirectory)) throw new ArgumentException("Escolhe uma pasta de destino com caminho completo.");
        if (Theme is not ("Sistema" or "Claro" or "Escuro")) throw new ArgumentException("Tema desconhecido.");
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
}

public sealed class TorrentEntry(SavedTorrent saved, TorrentManager manager)
{
    public SavedTorrent Saved { get; } = saved;
    public TorrentManager Manager { get; } = manager;
    public string Id => Saved.Id;
}

public static class JsonStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Load<T>(string path, Func<T> fallback, Action<string>? warning = null)
    {
        if (!File.Exists(path)) return fallback();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new JsonException("Conteúdo vazio"); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Retain the original so an interrupted/corrupt session can be recovered by hand.
            var backup = path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Copy(path, backup, true);
            warning?.Invoke($"Não foi possível ler {Path.GetFileName(path)}. Cópia guardada em {backup}: {e.Message}");
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
        TorrentState.Downloading => "A transferir",
        TorrentState.Seeding => "A partilhar",
        TorrentState.Paused => "Em pausa",
        TorrentState.Stopped => "Parado",
        TorrentState.Stopping => "A parar…",
        TorrentState.Hashing => "A verificar…",
        TorrentState.Metadata => "A obter metadados…",
        TorrentState.Error => "Erro: " + (m.Error?.Exception.Message ?? "desconhecido"),
        _ => m.State.ToString()
    };
    public static bool Active(TorrentManager m) => m.State is not (TorrentState.Stopped or TorrentState.Stopping or TorrentState.Paused or TorrentState.Error);
    public static string PriorityName(Priority p) => p switch { Priority.DoNotDownload => "Não transferir", Priority.High or Priority.Highest or Priority.Immediate => "Alta", Priority.Low or Priority.Lowest => "Baixa", _ => "Normal" };
}
