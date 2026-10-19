namespace TorrentClassic.Core;

public sealed class ClientRuntime : IAsyncDisposable
{
    readonly CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim settingsGate = new(1, 1);
    Task policyLoop = Task.CompletedTask, rssLoop = Task.CompletedTask;
    bool started, disposed;
    public TorrentService Service { get; }
    public RssService Rss { get; }
    public RemoteControlServer Remote { get; }
    public ClientRuntime(TorrentService service)
    {
        Service = service; Rss = new RssService(service); Remote = new RemoteControlServer(service, Rss);
    }
    public async Task StartAsync()
    {
        if (started) return; started = true;
        try { await Remote.ConfigureAsync(Service.Preferences.Remote); }
        catch (Exception e) { Service.Log("Remote control: " + e.Message); }
        policyLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try { while (await timer.WaitForNextTickAsync(lifetime.Token)) { try { await Service.ApplyPolicyAsync(); } catch (Exception e) { Service.Log("Queue/scheduler: " + e.Message); } } }
            catch (OperationCanceledException) { }
        });
        rssLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            try
            {
                do { try { await Rss.RefreshDueAsync(lifetime.Token); } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; } catch (Exception e) { Service.Log("RSS: " + e.Message); } }
                while (await timer.WaitForNextTickAsync(lifetime.Token));
            }
            catch (OperationCanceledException) { }
        });
    }
    public async Task ApplyPreferencesAsync(Preferences preferences)
    {
        preferences.Validate();
        await settingsGate.WaitAsync();
        var old = Service.Preferences;
        try
        {
            await Remote.ConfigureAsync(preferences.Remote);
            try { await Service.SavePreferencesAsync(preferences); }
            catch { await Remote.ConfigureAsync(old.Remote); throw; }
        }
        finally { settingsGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        await lifetime.CancelAsync();
        await Task.WhenAll(policyLoop, rssLoop);
        await Remote.DisposeAsync();
        await Rss.DisposeAsync();
        await Service.DisposeAsync();
        settingsGate.Dispose(); lifetime.Dispose();
    }
}
