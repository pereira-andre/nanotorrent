using MonoTorrent.Client;

namespace TorrentClassic.Core;

public sealed partial class TorrentService
{
    public ScheduleMode CurrentSchedule => Preferences.Scheduler.ModeAt(DateTimeOffset.Now);
    public async Task ApplyPolicyAsync(DateTimeOffset? localTime = null)
    {
        await gate.WaitAsync();
        try { if (!disposed) await ApplyPolicyLockedAsync(localTime ?? DateTimeOffset.Now); }
        finally { gate.Release(); }
    }

    async Task ApplyPolicyLockedAsync(DateTimeOffset now)
    {
        var mode = Preferences.Scheduler.ModeAt(now);
        var down = Preferences.DownloadLimitKiB * 1024;
        var up = Preferences.UploadLimitKiB * 1024;
        if (mode == ScheduleMode.Limited)
        {
            down = Math.Min(down == 0 ? int.MaxValue : down, Preferences.Scheduler.DownloadLimitKiB * 1024);
            up = Math.Min(up == 0 ? int.MaxValue : up, Preferences.Scheduler.UploadLimitKiB * 1024);
        }
        if (Engine.Settings.MaximumDownloadRate != down || Engine.Settings.MaximumUploadRate != up)
            await Engine.UpdateSettingsAsync(new EngineSettingsBuilder(Engine.Settings) { MaximumDownloadRate = down, MaximumUploadRate = up }.ToSettings());
        var save = false;
        foreach (var entry in entries)
        {
            if (entry.Manager.State == TorrentState.Seeding && entry.LastSeedSample is { } last)
                entry.SessionSeededSeconds += Math.Clamp((now - last).TotalSeconds, 0, 5);
            entry.LastSeedSample = now;
            if (!entry.Saved.Running || !entry.Manager.Complete || entry.Saved.ForceStarted) continue;
            var ratioDone = Preferences.Queue.SeedRatio > 0 && entry.ShareRatio >= Preferences.Queue.SeedRatio;
            var timeDone = Preferences.Queue.SeedMinutes > 0 && entry.SeededSeconds >= Preferences.Queue.SeedMinutes * 60;
            if (ratioDone || timeDone)
            {
                entry.Saved.Running = false;
                entry.WaitingReason = "Seeding goal reached";
                save = true;
            }
        }
        var plan = QueuePlanner.Plan(entries.Select(e => new QueueCandidate(e.Id, e.Saved.Running, e.Manager.Complete, e.Saved.ForceStarted, e.Manager.State == TorrentState.Error, e.Saved.QueueOrder)), Preferences.Queue, mode);
        // Pause displaced entries before starting successors, so limits hold during transitions.
        foreach (var entry in entries)
        {
            if (plan.TryGetValue(entry.Id, out var reason)) entry.WaitingReason = reason;
            var allowed = entry.Saved.Running && plan.TryGetValue(entry.Id, out var waiting) && waiting is null;
            if (!allowed && Display.Active(entry.Manager)) await entry.Manager.PauseAsync();
        }
        foreach (var entry in entries)
            if (entry.Saved.Running && plan.TryGetValue(entry.Id, out var waiting) && waiting is null && entry.Manager.State is TorrentState.Stopped or TorrentState.Paused)
                await entry.Manager.StartAsync();
        if (save) SaveSession();
    }

    public async Task MoveQueueAsync(TorrentEntry entry, int direction) => await MutateAsync(() =>
    {
        if (direction is not (-1 or 1)) throw new ArgumentException("Invalid direction.");
        var ordered = entries.OrderBy(e => e.Saved.QueueOrder).ToArray();
        var index = Array.IndexOf(ordered, entry);
        var next = index + direction;
        if (index >= 0 && next >= 0 && next < ordered.Length)
            (entry.Saved.QueueOrder, ordered[next].Saved.QueueOrder) = (ordered[next].Saved.QueueOrder, entry.Saved.QueueOrder);
        return Task.CompletedTask;
    });
}
