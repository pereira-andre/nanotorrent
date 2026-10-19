using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MonoTorrent;

namespace TorrentClassic.Core;

public sealed class RemoteControlServer(TorrentService service, RssService rss) : IAsyncDisposable
{
    WebApplication? application;
    RemoteSettings settings = new();
    readonly HttpClient http = TorrentImporter.CreateClient();
    public bool IsRunning => application is not null;
    public string LocalUrl => $"http://127.0.0.1:{settings.Port}/";

    public async Task ConfigureAsync(RemoteSettings value)
    {
        value.Validate();
        if (settings == value && (application is not null || !value.Enabled)) return;
        var previous = settings;
        await StopAsync();
        try { if (value.Enabled) await StartAsync(value); settings = value with { }; }
        catch
        {
            if (previous.Enabled) await StartAsync(previous);
            throw;
        }
    }
    async Task StartAsync(RemoteSettings config)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = service.DataDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = TorrentImporter.MaximumBytes;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Listen(config.AllowLan ? IPAddress.Any : IPAddress.Loopback, config.Port);
        });
        var app = builder.Build();
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(config.Token));
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var header = context.Request.Headers.Authorization.ToString();
                if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(header[7..]))))
                { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "Chave de acesso inválida." }); return; }
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}")
                { context.Response.StatusCode = 403; return; }
            }
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or IOException or System.Text.Json.JsonException or MonoTorrent.TorrentException)
            {
                service.Log("Controlo remoto: " + e.Message);
                if (!context.Response.HasStarted) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = e.Message }); }
            }
        });
        string Resource(string name)
        {
            using var stream = typeof(RemoteControlServer).Assembly.GetManifestResourceStream("TorrentClassic.Core.Remote." + name)!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var html = Resource("index.html"); var js = Resource("app.js"); var css = Resource("style.css");
        app.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));
        app.MapGet("/app.js", () => Results.Content(js, "text/javascript; charset=utf-8"));
        app.MapGet("/style.css", () => Results.Content(css, "text/css; charset=utf-8"));
        app.MapGet("/api/status", () => Results.Json(new
        {
            version = "1.1.0", downloadRate = service.Engine.TotalDownloadRate, uploadRate = service.Engine.TotalUploadRate,
            schedule = SchedulerSettings.Name(service.CurrentSchedule),
            downloadLimitKiB = service.Preferences.DownloadLimitKiB, uploadLimitKiB = service.Preferences.UploadLimitKiB,
            torrents = service.Entries.OrderBy(e => e.Saved.QueueOrder).Select(e => new { id = e.Id, name = e.Manager.Name, size = e.Manager.Torrent?.Size, progress = e.Manager.Progress, status = e.Status, desired = e.Saved.Running, forced = e.Saved.ForceStarted, order = e.Saved.QueueOrder, downloadRate = e.Manager.Monitor.DownloadRate, uploadRate = e.Manager.Monitor.UploadRate, downloaded = e.Downloaded, uploaded = e.Uploaded, ratio = e.ShareRatio })
        }));
        app.MapPost("/api/torrents", async (ImportRequest request, CancellationToken token) =>
        {
            var entry = await TorrentImporter.ImportAsync(service, http, request.Source, service.Preferences.DownloadDirectory, request.Start, token);
            return Results.Json(new { id = entry.Id });
        });
        app.MapPost("/api/upload", async (HttpRequest request, CancellationToken token) =>
        {
            var bytes = await TorrentImporter.ReadBoundedAsync(request.Body, token);
            var entry = await TorrentImporter.ImportBytesAsync(service, bytes, service.Preferences.DownloadDirectory, true, token);
            return Results.Json(new { id = entry.Id });
        });
        app.MapPost("/api/torrents/{id}/{action}", async (string id, string action) =>
        {
            var entry = service.Entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return Results.NotFound();
            switch (action)
            {
                case "start": await service.StartAsync(entry); break;
                case "force": await service.ForceStartAsync(entry); break;
                case "pause": await service.PauseAsync(entry); break;
                case "stop": await service.StopAsync(entry); break;
                case "check": await service.CheckAsync(entry); break;
                case "remove": await service.RemoveAsync(entry); break;
                case "up": await service.MoveQueueAsync(entry, -1); break;
                case "down": await service.MoveQueueAsync(entry, 1); break;
                default: return Results.BadRequest(new { error = "Ação desconhecida." });
            }
            return Results.Ok();
        });
        app.MapGet("/api/torrents/{id}/files", (string id) =>
        {
            var entry = service.Entries.FirstOrDefault(e => e.Id == id);
            return entry is null ? Results.NotFound() : Results.Json(entry.Manager.Files.Select(f => new { path = f.Path, size = f.Length, progress = f.BitField.PercentComplete, priority = (int)f.Priority }));
        });
        app.MapPost("/api/priority/{id}", async (string id, PriorityRequest request) =>
        {
            var entry = service.Entries.FirstOrDefault(e => e.Id == id); if (entry is null) return Results.NotFound();
            if (request.Priority is not (Priority.High or Priority.Normal or Priority.Low or Priority.DoNotDownload)) throw new ArgumentException("Prioridade inválida.");
            await service.SetPriorityAsync(entry, entry.Manager.Files.Where(f => request.Paths.Contains(f.Path)).ToArray(), request.Priority);
            return Results.Ok();
        });
        app.MapPost("/api/limits", async (LimitRequest request) =>
        {
            await service.SavePreferencesAsync(service.Preferences with { DownloadLimitKiB = request.Download, UploadLimitKiB = request.Upload });
            return Results.Ok();
        });
        app.MapGet("/api/rss", () => Results.Json(rss.Feeds.Select(f => new { id = f.Id, name = f.Name, lastChecked = f.LastChecked, error = f.LastError, items = f.Items })));
        app.MapPost("/api/rss/{id}/refresh", async (string id, CancellationToken token) => { await rss.RefreshAsync(id, token); return Results.Ok(); });
        app.MapPost("/api/rss/{id}/download/{item}", async (string id, string item, CancellationToken token) => { await rss.DownloadAsync(id, item, token); return Results.Ok(); });
        try { await app.StartAsync(); application = app; }
        catch { await app.DisposeAsync(); throw; }
        service.Log($"Controlo remoto ativo na porta {config.Port} ({(config.AllowLan ? "rede local/VPN" : "apenas este computador")}).");
    }
    async Task StopAsync()
    {
        if (application is not { } app) return;
        application = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await app.StopAsync(timeout.Token); } finally { await app.DisposeAsync(); }
    }
    public async ValueTask DisposeAsync() { await StopAsync(); http.Dispose(); }
    public sealed record ImportRequest(string Source, bool Start = true);
    public sealed record LimitRequest(int Download, int Upload);
    public sealed record PriorityRequest(string[] Paths, Priority Priority);
}
