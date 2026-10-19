using System.Security.Cryptography;

namespace TorrentClassic.Core;

public enum ScheduleMode { Full, Limited, SeedOnly, Paused }

public sealed record SchedulerSettings
{
    public bool Enabled { get; set; }
    // Monday first, 24 local wall-clock hours per day. Repeated DST hours share a slot.
    public ScheduleMode[] Hours { get; set; } = new ScheduleMode[168];
    public int DownloadLimitKiB { get; set; } = 128;
    public int UploadLimitKiB { get; set; } = 32;
    public ScheduleMode ModeAt(DateTimeOffset localTime) => !Enabled ? ScheduleMode.Full : Hours[((int)localTime.DayOfWeek + 6) % 7 * 24 + localTime.Hour];
    public void Validate()
    {
        if (Hours is null || Hours.Length != 168 || Hours.Any(h => !Enum.IsDefined(h))) throw new ArgumentException("O agendador precisa de 168 horas válidas.");
        if (DownloadLimitKiB is < 1 or > 1000000 || UploadLimitKiB is < 1 or > 1000000) throw new ArgumentException("Os limites do agendador devem estar entre 1 e 1 000 000 KiB/s.");
    }
    public static string Name(ScheduleMode mode) => mode switch { ScheduleMode.Limited => "Limitado", ScheduleMode.SeedOnly => "Só partilha", ScheduleMode.Paused => "Pausa", _ => "Normal" };
}

public sealed record QueueSettings
{
    public bool Enabled { get; set; } = true;
    public int MaximumActive { get; set; } = 8;
    public int MaximumDownloads { get; set; } = 3;
    public double SeedRatio { get; set; }
    public int SeedMinutes { get; set; }
    public void Validate()
    {
        if (MaximumActive is < 1 or > 1000 || MaximumDownloads is < 1 or > 1000 || MaximumDownloads > MaximumActive) throw new ArgumentException("A fila precisa de 1 a 1000 torrents ativos; os downloads não podem exceder esse total.");
        if (!double.IsFinite(SeedRatio) || SeedRatio is < 0 or > 10000 || SeedMinutes is < 0 or > 1000000) throw new ArgumentException("Objetivo de partilha inválido.");
    }
}

public sealed record RemoteSettings
{
    public bool Enabled { get; set; }
    public bool AllowLan { get; set; }
    public int Port { get; set; } = 8080;
    public string Token { get; set; } = "";
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public void Validate()
    {
        if (Port is < 1024 or > 65535) throw new ArgumentException("A porta remota deve estar entre 1024 e 65535.");
        if (Enabled && (Token.Length != 64 || !Token.All(Uri.IsHexDigit))) throw new ArgumentException("Gera uma chave de acesso remoto válida.");
    }
}

public sealed record QueueCandidate(string Id, bool Desired, bool Complete, bool Forced, bool Error, long Order);
public static class QueuePlanner
{
    public static Dictionary<string, string?> Plan(IEnumerable<QueueCandidate> torrents, QueueSettings settings, ScheduleMode mode)
    {
        var result = new Dictionary<string, string?>();
        int active = 0, downloads = 0;
        foreach (var item in torrents.OrderBy(t => t.Order))
        {
            string? reason = null;
            if (!item.Desired || item.Error) continue;
            if (mode == ScheduleMode.Paused) reason = "Agendador: em pausa";
            else if (mode == ScheduleMode.SeedOnly && !item.Complete) reason = "Agendador: só partilha";
            else if (settings.Enabled && !item.Forced && (active >= settings.MaximumActive || (!item.Complete && downloads >= settings.MaximumDownloads))) reason = "Em fila";
            else if (!item.Forced) { active++; if (!item.Complete) downloads++; }
            result[item.Id] = reason;
        }
        return result;
    }
}
