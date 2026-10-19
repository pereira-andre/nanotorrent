using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentClassic.Core;

static class AdvancedTests
{
    public static async Task<int> Run(string root, string? filter = null)
    {
        int passed = 0;
        async Task Test(string name, Func<Task> body) { if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return; Console.WriteLine("TEST " + name); await body(); passed++; Console.WriteLine("PASS " + name); }
        await Test("Weekly scheduler and queue allocation respect caps, order, forced starts and seed-only", () =>
        {
            var schedule = new SchedulerSettings { Enabled = true };
            schedule.Hours[0] = ScheduleMode.Paused; schedule.Hours[167] = ScheduleMode.SeedOnly;
            Assert(schedule.ModeAt(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(1))) == ScheduleMode.Paused, "Monday slot mismatch");
            Assert(schedule.ModeAt(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero)) == ScheduleMode.SeedOnly, "Sunday slot mismatch");
            var candidates = new[] { new QueueCandidate("a", true, false, false, false, 1), new QueueCandidate("b", true, false, false, false, 2), new QueueCandidate("c", true, true, false, false, 3), new QueueCandidate("forced", true, false, true, false, 4), new QueueCandidate("manual-pause", false, false, false, false, 5) };
            var queue = new QueueSettings { MaximumActive = 2, MaximumDownloads = 1 };
            var plan = QueuePlanner.Plan(candidates, queue, ScheduleMode.Full);
            Assert(plan["a"] is null && plan["b"] == "Em fila" && plan["c"] is null && plan["forced"] is null && !plan.ContainsKey("manual-pause"), "Queue caps/forced/manual intent failed");
            plan = QueuePlanner.Plan(candidates, queue, ScheduleMode.SeedOnly);
            Assert(plan["c"] is null && plan["forced"] is not null && plan["a"] is not null, "Seed-only schedule was bypassed");
            Assert(QueuePlanner.Plan(candidates, queue, ScheduleMode.Paused).Values.All(v => v is not null), "Paused schedule was bypassed");
            return Task.CompletedTask;
        });
        await Test("Native engine applies schedule rates, manual pause, reordering and forced queue starts", async () =>
        {
            var folder = Path.Combine(root, "policy"); var first = await Fixture(folder, "one"); var second = await Fixture(folder, "two");
            await using var service = new TorrentService(Path.Combine(folder, "app", "Data"), Offline() with { Queue = new QueueSettings { MaximumActive = 1, MaximumDownloads = 1 } });
            var a = await service.AddAsync(first, Path.Combine(folder, "download-one"), true);
            var b = await service.AddAsync(second, Path.Combine(folder, "download-two"), true);
            Assert(b.WaitingReason == "Em fila" && !Display.Active(b.Manager), "Second transfer did not queue");
            await service.PauseAsync(a);
            await Wait(() => Display.Active(b.Manager));
            Assert(!a.Saved.Running && a.Manager.State == TorrentState.Paused, "Manual pause intent lost");
            await service.StartAsync(a); await service.MoveQueueAsync(b, -1);
            Assert(a.WaitingReason == "Em fila", "Reordering did not displace previous transfer");
            await service.ForceStartAsync(a);
            Assert(Display.Active(a.Manager) && Display.Active(b.Manager), "Forced start did not bypass caps");
            await service.PauseAsync(a);
            await service.SavePreferencesAsync(service.Preferences with { Scheduler = new SchedulerSettings { Enabled = true, Hours = Enumerable.Repeat(ScheduleMode.Paused, 168).ToArray() } });
            Assert(b.Saved.Running && b.Manager.State == TorrentState.Paused && b.Status.Contains("Agendador"), "Schedule pause erased resume intent");
            await service.SavePreferencesAsync(service.Preferences with { DownloadLimitKiB = 10, Scheduler = new SchedulerSettings { Enabled = true, Hours = Enumerable.Repeat(ScheduleMode.Limited, 168).ToArray(), DownloadLimitKiB = 20, UploadLimitKiB = 8 } });
            Assert(Display.Active(b.Manager) && !a.Saved.Running && !Display.Active(a.Manager), "Scheduler resumed manually paused torrent");
            Assert(service.Engine.Settings.MaximumDownloadRate == 10 * 1024 && service.Engine.Settings.MaximumUploadRate == 8 * 1024, "Schedule/global minimum rate failed");
            await service.SavePreferencesAsync(service.Preferences with { Scheduler = new SchedulerSettings { Enabled = true, Hours = Enumerable.Repeat(ScheduleMode.SeedOnly, 168).ToArray() } });
            Assert(!Display.Active(b.Manager) && b.Saved.Running, "Seed-only mode did not hold incomplete download");
            var seedFile = await Fixture(folder, "seed-goal");
            var completed = await service.AddAsync(seedFile, folder, false);
            await service.CheckAsync(completed);
            completed.Saved.Uploaded = completed.Manager.Torrent!.Size;
            await service.SavePreferencesAsync(service.Preferences with { Queue = service.Preferences.Queue with { SeedRatio = 1 } });
            await service.StartAsync(completed);
            Assert(!completed.Saved.Running && completed.Status == "Objetivo de partilha atingido", "Seed ratio goal did not stop the seed");
            await service.ForceStartAsync(completed);
            Assert(completed.Saved.Running && Display.Active(completed.Manager), "Forced start did not bypass seed goal");
        });
        await Test("Queue automatically starts successor after a real download completes", async () =>
        {
            var folder = Path.Combine(root, "promotion");
            var first = await Fixture(folder, "first", 256 * 1024); var second = await Fixture(folder, "second", 256 * 1024);
            int port = FreePort();
            await using var seed = new TorrentService(Path.Combine(folder, "seed", "Data"), Offline(port));
            var seedA = await seed.AddAsync(first, folder, true); var seedB = await seed.AddAsync(second, folder, true);
            await Wait(() => seedA.Manager.State == TorrentState.Seeding && seedB.Manager.State == TorrentState.Seeding);
            var client = new TorrentService(Path.Combine(folder, "client", "Data"), Offline() with { DownloadLimitKiB = 64, Queue = new QueueSettings { MaximumActive = 2, MaximumDownloads = 1 } });
            await using var runtime = new ClientRuntime(client);
            var a = await client.AddAsync(first, Path.Combine(folder, "client", "Downloads"), true);
            var b = await client.AddAsync(second, Path.Combine(folder, "client", "Downloads"), true);
            Assert(b.Status == "Em fila", "Successor should initially queue");
            await runtime.StartAsync();
            await a.Manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
            await Wait(() => a.Manager.Complete, 45);
            await Wait(() => b.Manager.State == TorrentState.Downloading, 10);
            await b.Manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
            try { await Wait(() => b.Manager.Complete, 90); }
            catch { Console.WriteLine($"Successor state: {b.Status}, {b.Manager.State}, {b.Manager.Progress}%, {b.Manager.OpenConnections} peers"); Console.WriteLine(string.Join("\n", client.Messages.TakeLast(20))); throw; }
            Assert(a.Manager.Complete && b.Manager.Complete && b.WaitingReason is null, "Automatic promotion failed");
        });
        await Test("RSS/Atom parsing, filters and XML boundaries", async () =>
        {
            var rss = "<rss version='2.0'><channel><item><guid>a</guid><title><![CDATA[Ubuntu stable x64]]></title><enclosure url='/linux.torrent'/></item><item><guid>a</guid><link>/duplicate.torrent</link></item><item><title>No source</title></item><item><link>file:///private/file</link></item></channel></rss>";
            var items = FeedParser.Parse(Encoding.UTF8.GetBytes(rss), new Uri("https://example.test/rss"));
            Assert(items.Length == 1 && items[0].Source == "https://example.test/linux.torrent", "RSS enclosure/relative links/dedup failed");
            var atom = "<feed xmlns='http://www.w3.org/2005/Atom'><entry><id>b</id><title>Debian</title><link rel='enclosure' href='magnet:?xt=urn:btih:0123456789012345678901234567890123456789'/></entry></feed>";
            Assert(FeedParser.Parse(Encoding.UTF8.GetBytes(atom), new Uri("https://example.test/rss")).Single().Source.StartsWith("magnet:"), "Atom/magnet parsing failed");
            Assert(FeedParser.Matches("Ubuntu stable x64", "Debian*|Ubuntu*", "*beta*|*arm*"), "RSS include filter failed");
            Assert(!FeedParser.Matches("Ubuntu beta arm64", "Ubuntu*", "*beta*|*arm*"), "RSS exclude filter failed");
            try { FeedParser.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><rss>&x;</rss>"), new Uri("https://example.test")); throw new Exception("DTD accepted"); } catch (XmlException) { }
            try { await TorrentImporter.ReadBoundedAsync(new MemoryStream(new byte[TorrentImporter.MaximumBytes + 1]), default); throw new Exception("Oversized input accepted"); } catch (InvalidDataException) { }
        });
        await Test("RSS auto-download, bad-item isolation, ETag and persisted duplicate prevention", async () =>
        {
            var folder = Path.Combine(root, "rss"); var torrent = await Fixture(folder, "linux"); var bytes = await File.ReadAllBytesAsync(torrent);
            int goodRequests = 0; bool conditional = false;
            using var http = new HttpClient(new StubHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/good.torrent") { goodRequests++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }
                if (request.RequestUri.AbsolutePath == "/bad.torrent") return new(HttpStatusCode.OK) { Content = new StringContent("Not a torrent") };
                conditional = request.Headers.Contains("If-None-Match");
                if (conditional) return new(HttpStatusCode.NotModified);
                var xml = "<rss><channel><item><guid>bad</guid><title>Linux broken</title><enclosure url='/bad.torrent'/></item><item><guid>good</guid><title>Linux stable</title><enclosure url='/good.torrent'/></item><item><guid>exclude</guid><title>Linux beta</title><enclosure url='/good.torrent'/></item></channel></rss>";
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) }; response.Headers.ETag = new EntityTagHeaderValue("\"version1\""); return response;
            }));
            await using var service = new TorrentService(Path.Combine(folder, "app", "Data"), Offline());
            var feed = new RssFeed { Name = "Distributions", Url = "https://feed.test/rss", AutoDownload = true, Include = "Linux*", Exclude = "*beta*", Destination = Path.Combine(folder, "app", "Downloads") };
            await using (var rss = new RssService(service, http))
            {
                await rss.SaveFeedAsync(feed); await rss.RefreshAsync(feed.Id);
                Assert(service.Entries.Count == 1 && goodRequests == 1 && rss.Feeds.Single().LastError is not null, "Bad item prevented valid auto-download, or filter failed");
                await rss.RefreshAsync(feed.Id);
                Assert(conditional && goodRequests == 1 && service.Entries.Count == 1, "ETag/duplicate handling failed");
                await service.RemoveAsync(service.Entries.Single());
            }
            await using (var rss = new RssService(service, http))
            {
                await rss.RefreshAsync(feed.Id);
                Assert(service.Entries.Count == 0 && goodRequests == 1, "Previously downloaded item was re-added after RSS restart");
            }
            Assert((await File.ReadAllTextAsync(Path.Combine(service.DataDirectory, "rss.json"))).Contains("@portable/"), "RSS destination was not portable");
        });
        await Test("Authenticated remote HTTP API, upload, CSRF boundary, limits and token rotation", async () =>
        {
            var folder = Path.Combine(root, "remote"); var torrent = await Fixture(folder, "remote-data");
            await using var service = new TorrentService(Path.Combine(folder, "app", "Data"), Offline() with { DownloadDirectory = folder });
            await using var rss = new RssService(service);
            await using var server = new RemoteControlServer(service, rss);
            var config = new RemoteSettings { Enabled = true, Port = FreePort(), Token = RemoteSettings.NewToken() };
            await server.ConfigureAsync(config);
            using var http = new HttpClient { BaseAddress = new Uri(server.LocalUrl), Timeout = TimeSpan.FromSeconds(20) };
            Assert((await http.GetAsync("api/status")).StatusCode == HttpStatusCode.Unauthorized, "Unauthenticated API access allowed");
            var page = await http.GetAsync(""); Assert(page.IsSuccessStatusCode && page.Headers.Contains("Content-Security-Policy"), "Remote panel/CSP not served");
            Assert(!(await page.Content.ReadAsStringAsync()).Contains(config.Token), "Secret embedded in public HTML");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
            Assert((await http.GetAsync("api/status")).IsSuccessStatusCode, "Authenticated API rejected");
            using (var crossSite = new HttpRequestMessage(HttpMethod.Post, "api/limits") { Content = JsonContent.Create(new { download = 3, upload = 3 }) })
            {
                crossSite.Headers.Add("Origin", "https://foreign.test"); Assert((await http.SendAsync(crossSite)).StatusCode == HttpStatusCode.Forbidden, "Cross-origin mutation allowed");
            }
            Assert((await http.PostAsJsonAsync("api/torrents", new { source = "file:///etc/passwd" })).StatusCode == HttpStatusCode.BadRequest, "Remote API accepted filesystem source");
            using var upload = new ByteArrayContent(await File.ReadAllBytesAsync(torrent));
            var response = await http.PostAsync("api/upload", upload); response.EnsureSuccessStatusCode();
            var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
            var entry = service.Entries.Single();
            (await http.PostAsync($"api/torrents/{id}/pause", null)).EnsureSuccessStatusCode();
            Assert(!entry.Saved.Running, "Remote pause did not update persistent intent");
            (await http.PostAsync($"api/torrents/{id}/start", null)).EnsureSuccessStatusCode();
            Assert(entry.Saved.Running, "Remote start failed");
            Assert((await http.PostAsJsonAsync("api/limits", new { download = -1, upload = 1 })).StatusCode == HttpStatusCode.BadRequest, "Invalid limit accepted");
            (await http.PostAsJsonAsync("api/limits", new { download = 42, upload = 7 })).EnsureSuccessStatusCode();
            Assert(service.Preferences.DownloadLimitKiB == 42 && service.Engine.Settings.MaximumUploadRate == 7 * 1024, "Remote rate limits not applied");
            (await http.PostAsJsonAsync($"api/priority/{id}", new { paths = new[] { entry.Manager.Files.Single().Path }, priority = 8 })).EnsureSuccessStatusCode();
            Assert(entry.Manager.Files.Single().Priority == Priority.High, "Remote file priority failed");
            var rotated = config with { Token = RemoteSettings.NewToken() }; await server.ConfigureAsync(rotated);
            Assert((await http.GetAsync("api/status")).StatusCode == HttpStatusCode.Unauthorized, "Old access token still works");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.Token);
            (await http.PostAsync($"api/torrents/{id}/remove", null)).EnsureSuccessStatusCode();
            Assert(service.Entries.Count == 0 && File.Exists(Path.Combine(folder, "remote-data.bin")), "Remote remove deleted payload");
            await server.ConfigureAsync(rotated with { Enabled = false }); Assert(!server.IsRunning, "Disabled remote listener remained running");
        });
        return passed;
    }
    static Preferences Offline(int port = 0) => new() { Port = port, EnableDht = false, EnablePeerExchange = false, EnableLocalDiscovery = false, EnablePortForwarding = false };
    public static async Task Preview(string root)
    {
        var folder = Path.Combine(root, "preview"); var fixture = await Fixture(folder, "Distribuicao-livre", 1024 * 1024);
        var second = await Fixture(folder, "Fotografias-exemplo", 512 * 1024);
        await using var service = new TorrentService(Path.Combine(folder, "app", "Data"), Offline() with { DownloadDirectory = folder });
        foreach (var torrent in new[] { fixture, second }) { var entry = await service.AddAsync(torrent, folder, false); await service.CheckAsync(entry); }
        var xml = "<rss><channel><item><title>Distribuição de exemplo</title><enclosure url='https://preview.test/example.torrent'/></item></channel></rss>";
        using var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(xml) }));
        await using var rss = new RssService(service, http);
        var feed = new RssFeed { Name = "Exemplos livres", Url = "https://preview.test/rss" }; await rss.SaveFeedAsync(feed); await rss.RefreshAsync(feed.Id);
        await using var server = new RemoteControlServer(service, rss);
        await server.ConfigureAsync(new RemoteSettings { Enabled = true, Port = FreePort(), Token = new string('a', 64) });
        Console.WriteLine("PREVIEW " + server.LocalUrl);
        using var cancelled = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled.Cancel(); };
        try { await Task.Delay(Timeout.Infinite, cancelled.Token); } catch (OperationCanceledException) { }
    }
    static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    static async Task<string> Fixture(string folder, string name, int size = 16384)
    {
        Directory.CreateDirectory(folder); var source = Path.Combine(folder, name + ".bin");
        var bytes = new byte[size]; new Random(StringComparer.Ordinal.GetHashCode(name)).NextBytes(bytes); await File.WriteAllBytesAsync(source, bytes);
        var torrent = source + ".torrent"; await new TorrentCreator { PieceLength = 16384 }.CreateAsync(new TorrentFileSource(source), torrent); return torrent;
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task Wait(Func<bool> predicate, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Advanced transfer condition timed out"); await Task.Delay(100); }
    }
    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
