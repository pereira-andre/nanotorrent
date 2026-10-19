using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MonoTorrent;

namespace TorrentClassic.Core;

public sealed record RssItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTimeOffset? Published { get; set; }
    public bool Downloaded { get; set; }
    public string? TorrentId { get; set; }
}
public sealed record RssFeed
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool AutoDownload { get; set; }
    public string Include { get; set; } = "";
    public string Exclude { get; set; } = "";
    public string Destination { get; set; } = "";
    public int RefreshMinutes { get; set; } = 15;
    public DateTimeOffset? LastChecked { get; set; }
    public string? LastError { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public List<RssItem> Items { get; set; } = [];
    public List<string> DownloadedIds { get; set; } = [];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120) throw new ArgumentException("Enter a feed name (up to 120 characters).");
        TorrentImporter.ValidateHttpUrl(Url);
        if (Include.Length > 256 || Exclude.Length > 256) throw new ArgumentException("RSS filters may contain up to 256 characters.");
        if (RefreshMinutes is < 2 or > 1440) throw new ArgumentException("The RSS refresh interval must be between 2 and 1440 minutes.");
        if (Destination.Length > 0 && !Path.IsPathFullyQualified(Destination)) throw new ArgumentException("The feed download folder must be an absolute path.");
    }
}

public static class FeedParser
{
    public static RssItem[] Parse(byte[] xml, Uri feedUrl)
    {
        using var stream = new MemoryStream(xml);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = TorrentImporter.MaximumBytes });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName is not ("rss" or "RDF" or "feed")) throw new FormatException("The URL did not return an RSS or Atom feed.");
        return document.Descendants().Where(e => e.Name.LocalName is "item" or "entry").Take(500).Select(item =>
        {
            string Text(string name) => item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
            var enclosures = item.Elements().Where(e => e.Name.LocalName == "enclosure" || e.Name.LocalName == "link" && (string?)e.Attribute("rel") == "enclosure");
            var source = enclosures.Select(e => (string?)e.Attribute("url") ?? (string?)e.Attribute("href")).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                ?? item.Elements().Where(e => e.Name.LocalName == "link").Select(e => (string?)e.Attribute("href") ?? e.Value.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
            if (source.Length == 0) return null;
            if (Uri.TryCreate(feedUrl, source, out var uri)) source = uri.ToString();
            if (!MagnetLink.TryParse(source, out _) && (!Uri.TryCreate(source, UriKind.Absolute, out var link) || link.Scheme is not ("http" or "https"))) return null;
            var title = Text("title");
            var key = Text("guid"); if (key.Length == 0) key = Text("id"); if (key.Length == 0) key = source;
            var date = Text("pubDate"); if (date.Length == 0) date = Text("published"); if (date.Length == 0) date = Text("updated");
            return new RssItem { Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))), Title = title.Length == 0 ? source : title, Source = source, Published = DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var published) ? published : null };
        }).OfType<RssItem>().DistinctBy(i => i.Id).ToArray();
    }
    public static bool Matches(string title, string include, string exclude)
    {
        static bool Match(string value, string patterns) => patterns.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(pattern =>
            pattern.IndexOfAny(['*', '?']) < 0 ? value.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                : Regex.IsMatch(value, Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", "."), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100)));
        return (string.IsNullOrWhiteSpace(include) || Match(title, include)) && (string.IsNullOrWhiteSpace(exclude) || !Match(title, exclude));
    }
}

public static class TorrentImporter
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public static HttpClient CreateClient() => new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20), DefaultRequestHeaders = { { "User-Agent", "NanoTorrent/1.1" } } };
    public static Uri ValidateHttpUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : throw new ArgumentException("The URL must begin with https:// or http://.");
    public static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var result = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (result.Length + read > MaximumBytes) throw new InvalidDataException("The feed or torrent file exceeds 4 MiB.");
            result.Write(buffer, 0, read);
        }
        return result.ToArray();
    }
    public static async Task<TorrentEntry> ImportAsync(TorrentService service, HttpClient client, string source, string destination, bool start, CancellationToken token = default)
    {
        if (MagnetLink.TryParse(source, out _)) return await service.AddAsync(source, destination, start);
        var uri = ValidateHttpUrl(source);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(token), token);
        return await ImportBytesAsync(service, bytes, destination, start, token);
    }
    public static async Task<TorrentEntry> ImportBytesAsync(TorrentService service, byte[] bytes, string destination, bool start, CancellationToken token = default)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("The torrent file exceeds 4 MiB.");
        var temporary = Path.Combine(service.DataDirectory, "import-" + Guid.NewGuid().ToString("N") + ".torrent");
        try { await File.WriteAllBytesAsync(temporary, bytes, token); return await service.AddAsync(temporary, destination, start); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class RssService : IAsyncDisposable
{
    readonly TorrentService service;
    readonly HttpClient http;
    readonly bool ownsClient;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    readonly List<RssFeed> feeds;
    RssFeed[] snapshot = [];
    string StatePath => Path.Combine(service.DataDirectory, "rss.json");
    public IReadOnlyList<RssFeed> Feeds => Volatile.Read(ref snapshot);
    public RssService(TorrentService service, HttpClient? client = null)
    {
        this.service = service; ownsClient = client is null; http = client ?? TorrentImporter.CreateClient();
        feeds = JsonStore.Load<List<RssFeed>>(StatePath, () => [], service.Log);
        foreach (var feed in feeds) { if (feed.Destination.Length > 0) feed.Destination = service.ExpandPath(feed.Destination); }
        Publish();
    }
    void Publish() => Volatile.Write(ref snapshot, feeds.Select(f => f with { Items = f.Items.Select(i => i with { }).ToList(), DownloadedIds = [.. f.DownloadedIds] }).ToArray());
    void Save()
    {
        JsonStore.Save(StatePath, feeds.Select(f => f with { Destination = service.StorePath(f.Destination) }).ToArray()); Publish();
    }
    public async Task SaveFeedAsync(RssFeed feed)
    {
        feed.Validate();
        await gate.WaitAsync();
        try
        {
            var index = feeds.FindIndex(f => f.Id == feed.Id);
            if (index < 0) feeds.Add(feed with { Items = [], DownloadedIds = [] });
            else
            {
                var previous = feeds[index];
                feeds[index] = feed with { Items = previous.Url == feed.Url ? previous.Items : [], DownloadedIds = previous.Url == feed.Url ? previous.DownloadedIds : [], LastChecked = previous.Url == feed.Url ? previous.LastChecked : null, ETag = previous.Url == feed.Url ? previous.ETag : null, LastModified = previous.Url == feed.Url ? previous.LastModified : null };
            }
            Save();
        }
        finally { gate.Release(); }
    }
    public async Task RemoveFeedAsync(string id)
    {
        await gate.WaitAsync(); try { feeds.RemoveAll(f => f.Id == id); Save(); } finally { gate.Release(); }
    }
    public async Task RefreshDueAsync(CancellationToken token)
    {
        foreach (var feed in Feeds.Where(f => f.Enabled && (f.LastChecked is null || DateTimeOffset.UtcNow - f.LastChecked >= TimeSpan.FromMinutes(f.RefreshMinutes))))
            await RefreshAsync(feed.Id, token);
    }
    public async Task RefreshAsync(string id, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token); token = linked.Token;
        await gate.WaitAsync(token);
        try
        {
            var feed = feeds.FirstOrDefault(f => f.Id == id); if (feed is null) return;
            try
            {
                feed.Validate();
                using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
                if (feed.ETag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", feed.ETag);
                request.Headers.IfModifiedSince = feed.LastModified;
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (response.StatusCode != HttpStatusCode.NotModified)
                {
                    response.EnsureSuccessStatusCode();
                    var items = FeedParser.Parse(await TorrentImporter.ReadBoundedAsync(await response.Content.ReadAsStreamAsync(token), token), new Uri(feed.Url));
                    var previous = feed.Items.ToDictionary(i => i.Id);
                    feed.Items = items.Select(item => previous.TryGetValue(item.Id, out var old) ? item with { Downloaded = old.Downloaded, TorrentId = old.TorrentId } : item with { Downloaded = feed.DownloadedIds.Contains(item.Id) }).ToList();
                    feed.ETag = response.Headers.ETag?.ToString(); feed.LastModified = response.Content.Headers.LastModified;
                }
                feed.LastError = null;
                if (feed.Enabled && feed.AutoDownload)
                    foreach (var item in feed.Items.Where(i => !i.Downloaded && FeedParser.Matches(i.Title, feed.Include, feed.Exclude)))
                    {
                        try { await DownloadLockedAsync(feed, item, token); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception e) { feed.LastError = e.Message; service.Log($"RSS {feed.Name}: {item.Title}: {e.Message}"); }
                    }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) { feed.LastError = e.Message; service.Log($"RSS {feed.Name}: {e.Message}"); }
            finally { feed.LastChecked = DateTimeOffset.UtcNow; Save(); }
        }
        finally { gate.Release(); }
    }
    public async Task DownloadAsync(string feedId, string itemId, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token); token = linked.Token;
        await gate.WaitAsync(token);
        try
        {
            var feed = feeds.First(f => f.Id == feedId); var item = feed.Items.First(i => i.Id == itemId);
            await DownloadLockedAsync(feed, item, token); Save();
        }
        finally { gate.Release(); }
    }
    async Task DownloadLockedAsync(RssFeed feed, RssItem item, CancellationToken token)
    {
        try
        {
            var entry = await TorrentImporter.ImportAsync(service, http, item.Source, feed.Destination.Length == 0 ? service.Preferences.DownloadDirectory : feed.Destination, true, token);
            item.TorrentId = entry.Id;
        }
        catch (InvalidOperationException e) when (e.Message == "This torrent is already in the list.") { }
        item.Downloaded = true;
        if (!feed.DownloadedIds.Contains(item.Id)) feed.DownloadedIds.Add(item.Id);
        if (feed.DownloadedIds.Count > 2000) feed.DownloadedIds.RemoveRange(0, feed.DownloadedIds.Count - 2000);
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await gate.WaitAsync();
        try { if (ownsClient) http.Dispose(); }
        finally { gate.Release(); }
        // A cancelled HTTP continuation can still release the gate; no OS wait handle is allocated.
        lifetime.Dispose();
    }
}
