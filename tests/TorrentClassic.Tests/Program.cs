using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentClassic.Core;

var root = Path.Combine(Path.GetTempPath(), "TorrentClassic-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
try
{
    if (args.FirstOrDefault() == "--remote-preview") { await AdvancedTests.Preview(root); return 0; }
    if (args.FirstOrDefault() == "--advanced") { passed = await AdvancedTests.Run(root, args.ElementAtOrDefault(1)); Console.WriteLine($"PASS: {passed} advanced scenarios."); return 0; }
    await Test("Invalid preferences and malformed state are handled", async () =>
    {
        var preferences = new Preferences { Port = -1 };
        AssertThrows<ArgumentException>(preferences.Validate);
        var file = Path.Combine(root, "corrupt.json");
        await File.WriteAllTextAsync(file, "{invalid");
        var warnings = new List<string>();
        var result = JsonStore.Load(file, () => new Preferences(), warnings.Add);
        Assert(result.Port == 51483 && warnings.Count == 1, "Fallback preferences were not loaded");
        Assert(Directory.GetFiles(root, "corrupt.json.invalid-*").Length == 1, "Corrupt original not retained");
        JsonStore.Save(file, result);
        Assert(JsonStore.Load(file, () => new Preferences { Port = 1 }).Port == 51483, "Atomic JSON save failed");
    });
    await Test("Real loopback transfer: magnet metadata, pause, restart, priority and SHA256", async () =>
    {
        var sourceFolder = Path.Combine(root, "source"); Directory.CreateDirectory(sourceFolder);
        var source = Path.Combine(sourceFolder, "payload.bin");
        var bytes = new byte[1024 * 1024]; new Random(42).NextBytes(bytes); await File.WriteAllBytesAsync(source, bytes);
        var torrentPath = Path.Combine(root, "test.torrent");
        var creator = new TorrentCreator { PieceLength = 16384, CreatedBy = "Torrent Classic integration test" };
        await creator.CreateAsync(new TorrentFileSource(source), torrentPath);
        var seedPort = FreePort();
        await using var seed = new TorrentService(Path.Combine(root, "seed", "Data"), Offline(seedPort));
        var seedEntry = await seed.AddAsync(torrentPath, sourceFolder, true);
        await Wait(() => seedEntry.Manager.State == TorrentState.Seeding, "Seeder failed to hash original file");
        var clientRoot = Path.Combine(root, "portable", "Data");
        var downloads = Path.Combine(root, "portable", "Downloads");
        var peer = new PeerInfo(new Uri($"ipv4://127.0.0.1:{seedPort}"));
        string id;
        await using (var client = new TorrentService(clientRoot, Offline(FreePort()) with { DownloadLimitKiB = 64, DownloadDirectory = downloads }))
        {
            var entry = await client.AddAsync(seedEntry.Manager.MagnetLink.ToV1String(), downloads, true);
            id = entry.Id;
            await entry.Manager.AddPeerAsync(peer);
            await Wait(() => entry.Manager.HasMetadata && entry.Manager.Monitor.DataBytesReceived > 16384, "Magnet metadata/payload did not arrive");
            await client.PauseAsync(entry);
            Assert(entry.Manager.State == TorrentState.Paused, "Pause did not pause the transfer");
            await client.SetPriorityAsync(entry, entry.Manager.Files, Priority.Low);
            await client.SetLabelAsync(entry, "Teste");
            await AssertThrowsAsync<InvalidOperationException>(() => client.AddAsync(torrentPath, downloads, false));
        }
        await using (var client = new TorrentService(clientRoot))
        {
            await client.RestoreAsync();
            Assert(client.Entries.Count == 1, "Saved session was not restored");
            var entry = client.Entries.Single();
            Assert(entry.Id == id && !entry.Saved.Running && entry.Manager.State == TorrentState.Stopped, "Paused torrent auto-started after restart");
            Assert(entry.Saved.Label == "Teste", "Label was not restored");
            Assert(entry.Downloaded > 0, "Lifetime transfer counters were not restored");
            await client.SavePreferencesAsync(client.Preferences with { DownloadLimitKiB = 0 });
            await client.StartAsync(entry);
            await entry.Manager.AddPeerAsync(peer);
            await Wait(() => entry.Manager.Complete, "Resumed download did not finish", 60);
            Assert(entry.Manager.Files.Single().Priority == Priority.Low, "File priority was not restored");
            await client.StopAsync(entry);
            var downloaded = entry.Manager.Files.Single().FullPath;
            Assert(File.Exists(downloaded), "Downloaded file missing");
            Assert(SHA256.HashData(await File.ReadAllBytesAsync(downloaded)).SequenceEqual(SHA256.HashData(bytes)), "Downloaded bytes differ from original");
            await client.RemoveAsync(entry);
            Assert(File.Exists(downloaded) && client.Entries.Count == 0, "Remove deleted downloaded data");
        }
    });
    await Test("Portable folder can move with its settings, torrents and downloaded files", async () =>
    {
        var original = Path.Combine(root, "original"); var moved = Path.Combine(root, "moved");
        var data = Path.Combine(original, "Data"); var downloads = Path.Combine(original, "Downloads");
        Directory.CreateDirectory(downloads);
        var source = Path.Combine(downloads, "portable.txt"); await File.WriteAllTextAsync(source, "This file moves with the portable application.");
        var torrent = Path.Combine(root, "portable.torrent");
        await new TorrentCreator { PieceLength = 16384 }.CreateAsync(new TorrentFileSource(source), torrent);
        await using (var client = new TorrentService(data, Offline(0) with { DownloadDirectory = downloads, Theme = "Escuro" }))
        {
            await client.AddAsync(torrent, downloads, false);
        }
        Directory.Move(original, moved);
        await using (var client = new TorrentService(Path.Combine(moved, "Data")))
        {
            await client.RestoreAsync();
            Assert(client.Entries.Count == 1 && client.Preferences.Theme == "Escuro", "Moved settings/session failed to load");
            Assert(client.Preferences.DownloadDirectory == Path.Combine(moved, "Downloads"), "Download preference was not rebased");
            var entry = client.Entries.Single();
            Assert(entry.Manager.SavePath == Path.Combine(moved, "Downloads"), "Torrent destination was not rebased");
            await client.StartAsync(entry);
            await Wait(() => entry.Manager.State == TorrentState.Seeding, "Moved files did not hash correctly");
            Assert(entry.Manager.Complete, "Moved payload no longer complete");
        }
    });
    await Test("Unavailable torrents remain recoverable after saving another session", async () =>
    {
        var folder = Path.Combine(root, "unavailable", "Data"); Directory.CreateDirectory(folder);
        var missing = new SavedTorrent { Id = "missing", Source = Path.Combine(root, "absent.torrent"), SavePath = Path.Combine(root, "later") };
        JsonStore.Save(Path.Combine(folder, "session.json"), new[] { missing });
        await using (var client = new TorrentService(folder, Offline(0)))
        {
            await client.RestoreAsync();
            Assert(client.Entries.Count == 0 && client.Messages.Any(m => m.Contains("restaurar")), "Unavailable torrent was not reported");
        }
        Assert(JsonStore.Load<SavedTorrent[]>(Path.Combine(folder, "session.json"), () => []).Single().Id == "missing", "Unavailable torrent was lost from saved session");
    });
    passed += await AdvancedTests.Run(root);
    Console.WriteLine($"PASS: {passed} integration scenarios.");
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e); return 1; }
finally { try { Directory.Delete(root, true); } catch (IOException) { } }

async Task Test(string name, Func<Task> body) { Console.WriteLine("TEST " + name); await body(); passed++; Console.WriteLine("PASS " + name); }
static Preferences Offline(int port) => new() { Port = port, EnableDht = false, EnablePeerExchange = false, EnableLocalDiscovery = false, EnablePortForwarding = false };
static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static void AssertThrows<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static async Task AssertThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static async Task Wait(Func<bool> predicate, string failure, int seconds = 45)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    while (!predicate()) { if (timeout.IsCancellationRequested) throw new TimeoutException(failure); await Task.Delay(100); }
}
