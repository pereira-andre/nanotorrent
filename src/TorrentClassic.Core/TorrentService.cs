using System.Collections.Concurrent;
using System.Net;
using MonoTorrent;
using MonoTorrent.Client;

namespace TorrentClassic.Core;

public sealed class TorrentService : IAsyncDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly List<TorrentEntry> entries = [];
    readonly List<SavedTorrent> unrestored = [];
    readonly ConcurrentQueue<string> messages = new();
    readonly CancellationTokenSource lifetime = new();
    readonly List<Task> metadataTasks = [];
    readonly string root;
    bool disposed;
    public ClientEngine Engine { get; }
    public Preferences Preferences { get; private set; }
    public IReadOnlyList<TorrentEntry> Entries => entries.ToArray();
    public string DataDirectory => root;

    public TorrentService(string dataDirectory, Preferences? preferences = null)
    {
        root = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "torrents"));
        Preferences = preferences ?? JsonStore.Load(Path.Combine(root, "preferences.json"), () => new Preferences { DownloadDirectory = Path.Combine(Path.GetDirectoryName(root)!, "Downloads") }, Log);
        Preferences.DownloadDirectory = ExpandPath(Preferences.DownloadDirectory);
        try { Preferences.Validate(); }
        catch (ArgumentException e) { Log("Preferências inválidas: " + e.Message); Preferences = new Preferences { DownloadDirectory = Path.Combine(Path.GetDirectoryName(root)!, "Downloads") }; }
        Engine = new ClientEngine(BuildSettings(Preferences));
    }

    EngineSettings BuildSettings(Preferences p) => new EngineSettingsBuilder
    {
        CacheDirectory = Path.Combine(root, "cache"),
        AutoSaveLoadFastResume = true,
        AutoSaveLoadDhtCache = p.EnableDht,
        AutoSaveLoadMagnetLinkMetadata = true,
        AllowPortForwarding = p.EnablePortForwarding,
        AllowLocalPeerDiscovery = p.EnableLocalDiscovery,
        MaximumConnections = p.MaximumConnections,
        MaximumDownloadRate = p.DownloadLimitKiB * 1024,
        MaximumUploadRate = p.UploadLimitKiB * 1024,
        DhtEndPoint = p.EnableDht ? new IPEndPoint(IPAddress.Any, p.Port) : null,
        ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Any, p.Port), ["ipv6"] = new(IPAddress.IPv6Any, p.Port) }
    }.ToSettings();

    TorrentSettings TorrentSettings() => new TorrentSettingsBuilder
    {
        AllowDht = Preferences.EnableDht,
        AllowPeerExchange = Preferences.EnablePeerExchange,
        MaximumConnections = Math.Min(Preferences.MaximumConnections, 100)
    }.ToSettings();

    public void Log(string message)
    {
        messages.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (messages.Count > 1000) messages.TryDequeue(out _);
    }
    public string[] Messages => messages.ToArray();
    const string PortablePrefix = "@portable/";
    string ExpandPath(string path) => path.StartsWith(PortablePrefix, StringComparison.Ordinal) ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(root)!, path[PortablePrefix.Length..])) : path;
    string StorePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return path;
        var relative = Path.GetRelativePath(Path.GetDirectoryName(root)!, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathFullyQualified(relative) ? PortablePrefix + relative : path;
    }
    void SavePreferences() => JsonStore.Save(Path.Combine(root, "preferences.json"), Preferences with { DownloadDirectory = StorePath(Preferences.DownloadDirectory) });
    void SaveSession() => JsonStore.Save(Path.Combine(root, "session.json"), entries.Select(e => e.Saved).Concat(unrestored).Select(s => s with { Source = StorePath(s.Source), SavePath = StorePath(s.SavePath) }).ToArray());

    public async Task RestoreAsync()
    {
        var saved = JsonStore.Load<SavedTorrent[]>(Path.Combine(root, "session.json"), () => [], Log);
        foreach (var item in saved)
        {
            try { item.Source = ExpandPath(item.Source); item.SavePath = ExpandPath(item.SavePath); await AddAsync(item.Source, item.SavePath, item.Running, item); }
            catch (Exception e) { unrestored.Add(item); Log($"Não foi possível restaurar {item.Source}: {e.Message}"); }
        }
        // Failed entries remain in the saved session so a temporarily unavailable drive can recover.
    }

    public async Task<TorrentEntry> AddAsync(string source, string destination, bool start, SavedTorrent? saved = null)
    {
        await gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            destination = Path.GetFullPath(destination);
            Directory.CreateDirectory(destination);
            source = source.Trim().Trim('"');
            var magnet = MagnetLink.TryParse(source, out var parsed) ? parsed : null;
            Torrent? torrent = magnet is null ? await Torrent.LoadAsync(source) : null;
            var hashes = magnet?.InfoHashes ?? torrent!.InfoHashes;
            if (Engine.Contains(hashes)) throw new InvalidOperationException("Este torrent já está na lista.");
            var id = hashes.V1OrV2.ToHex();
            var storedSource = source;
            if (torrent is not null)
            {
                storedSource = Path.Combine(root, "torrents", id + ".torrent");
                if (!string.Equals(Path.GetFullPath(source), storedSource, StringComparison.OrdinalIgnoreCase)) File.Copy(source, storedSource, true);
            }
            var manager = magnet is not null ? await Engine.AddAsync(magnet, destination, TorrentSettings()) : await Engine.AddAsync(torrent!, destination, TorrentSettings());
            var record = saved ?? new SavedTorrent { Id = id, SavePath = destination, Source = storedSource, Running = start };
            record.Id = id;
            record.Source = storedSource;
            var entry = new TorrentEntry(record, manager);
            manager.TorrentStateChanged += (_, e) => Log($"{manager.Name}: {e.NewState}");
            entries.Add(entry);
            if (record.Priorities.Count > 0)
            {
                if (manager.HasMetadata) await ApplyPrioritiesAsync(entry);
                else metadataTasks.Add(RestorePrioritiesAsync(entry));
            }
            if (saved is null) SaveSession();
            Log("Adicionado: " + manager.Name);
            if (start) await manager.StartAsync();
            return entry;
        }
        finally { gate.Release(); }
    }

    async Task ApplyPrioritiesAsync(TorrentEntry entry)
    {
        foreach (var file in entry.Manager.Files)
            if (entry.Saved.Priorities.TryGetValue(file.Path, out var priority)) await entry.Manager.SetFilePriorityAsync(file, priority);
    }
    async Task RestorePrioritiesAsync(TorrentEntry entry)
    {
        try
        {
            await entry.Manager.WaitForMetadataAsync(lifetime.Token);
            await gate.WaitAsync(lifetime.Token);
            try { if (entries.Contains(entry)) await ApplyPrioritiesAsync(entry); }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log("Não foi possível restaurar prioridades: " + e.Message); }
    }

    public async Task StartAsync(TorrentEntry entry) => await MutateAsync(async () => { await entry.Manager.StartAsync(); entry.Saved.Running = true; });
    public async Task PauseAsync(TorrentEntry entry) => await MutateAsync(async () => { await entry.Manager.PauseAsync(); entry.Saved.Running = false; });
    public async Task StopAsync(TorrentEntry entry) => await MutateAsync(async () => { await entry.Manager.StopAsync(TimeSpan.FromSeconds(5)); entry.Saved.Running = false; });
    public async Task CheckAsync(TorrentEntry entry) => await MutateAsync(async () =>
    {
        entry.Saved.Running = false;
        await entry.Manager.StopAsync(TimeSpan.FromSeconds(5));
        await entry.Manager.HashCheckAsync(false);
    });
    public async Task RemoveAsync(TorrentEntry entry) => await MutateAsync(async () =>
    {
        await entry.Manager.StopAsync(TimeSpan.FromSeconds(5));
        await Engine.RemoveAsync(entry.Manager);
        entries.Remove(entry);
        Log("Removido da lista (ficheiros mantidos): " + entry.Manager.Name);
    });
    public async Task SetLabelAsync(TorrentEntry entry, string label) => await MutateAsync(() => { entry.Saved.Label = label.Trim(); return Task.CompletedTask; });
    public async Task SetPriorityAsync(TorrentEntry entry, IEnumerable<ITorrentManagerFile> files, Priority priority) => await MutateAsync(async () =>
    {
        foreach (var file in files) { await entry.Manager.SetFilePriorityAsync(file, priority); entry.Saved.Priorities[file.Path] = priority; }
    });
    async Task MutateAsync(Func<Task> action)
    {
        await gate.WaitAsync();
        try { ObjectDisposedException.ThrowIf(disposed, this); await action(); SaveSession(); }
        finally { gate.Release(); }
    }

    public async Task SavePreferencesAsync(Preferences preferences)
    {
        preferences.Validate();
        await gate.WaitAsync();
        try
        {
            await Engine.UpdateSettingsAsync(BuildSettings(preferences));
            Preferences = preferences;
            foreach (var entry in entries)
            {
                var settings = new TorrentSettingsBuilder(entry.Manager.Settings) { AllowDht = preferences.EnableDht, AllowPeerExchange = preferences.EnablePeerExchange, MaximumConnections = Math.Min(preferences.MaximumConnections, 100) };
                await entry.Manager.UpdateSettingsAsync(settings.ToSettings());
            }
            SavePreferences();
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await lifetime.CancelAsync();
        await Task.WhenAll(metadataTasks);
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            try
            {
                try { SaveSession(); SavePreferences(); }
                finally { await Engine.StopAllAsync(TimeSpan.FromSeconds(5)); }
            }
            finally { Engine.Dispose(); disposed = true; }
        }
        finally { gate.Release(); }
        lifetime.Dispose();
    }
}
