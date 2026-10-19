using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentClassic.Core;

namespace TorrentClassic.Desktop;

internal sealed class TorrentRow(TorrentEntry entry) : INotifyPropertyChanged
{
    public TorrentEntry Entry { get; } = entry;
    public long Order => Entry.Saved.QueueOrder;
    public string Name => Entry.Manager.Name;
    public string Size => Entry.Manager.Torrent is { } t ? Display.Bytes(t.Size) : "—";
    public string Done => $"{Entry.Manager.Progress:0.0}%";
    public string Status => Entry.Status + (Entry.Saved.ForceStarted ? " (forced)" : "");
    public int Seeds => Entry.Manager.Peers.Seeds;
    public int Peers => Entry.Manager.OpenConnections;
    public string Down => Display.Rate(Entry.Manager.Monitor.DownloadRate);
    public string Up => Display.Rate(Entry.Manager.Monitor.UploadRate);
    public string Eta => Entry.Manager.Monitor.DownloadRate > 0 && Entry.Manager.Torrent is { } t ? Display.Duration(TimeSpan.FromSeconds(t.Size * (100 - Entry.Manager.Progress) / 100 / Entry.Manager.Monitor.DownloadRate)) : "∞";
    public string Ratio => Entry.ShareRatio.ToString("0.000");
    public string Label => Entry.Saved.Label;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
internal sealed record TableRow(string First, string Second, string Third, string Fourth = "", string Fifth = "", object? Item = null);
internal sealed record Category(string Key, string Title) { public override string ToString() => Title; }

internal sealed class MainWindow : Window
{
    readonly ClientRuntime runtime;
    TorrentService Service => runtime.Service;
    readonly ObservableCollection<TorrentRow> rows = [];
    readonly DataGrid torrents = Table(("#", "Order", 42), ("Name", "Name", 260), ("Size", "Size", 90), ("Done", "Done", 75), ("Status", "Status", 170), ("Seeds", "Seeds", 60), ("Peers", "Peers", 60), ("Down speed", "Down", 100), ("Up speed", "Up", 100), ("ETA", "Eta", 95), ("Ratio", "Ratio", 70), ("Label", "Label", 100));
    readonly ListBox sidebar = new();
    readonly TextBox search = Ui.Text("", "Filter torrents…");
    readonly TextBlock status = new() { Margin = new Thickness(10, 5) };
    readonly TextBox general = ReadOnlyText(), log = ReadOnlyText();
    readonly DataGrid trackers = Table(("Tracker", "First", 420), ("Status", "Second", 210), ("Next announce", "Third", 100));
    readonly DataGrid peers = Table(("Address", "First", 230), ("Client", "Second", 170), ("Done", "Third", 80), ("Download", "Fourth", 100), ("Upload", "Fifth", 100));
    readonly DataGrid files = Table(("Path", "First", 440), ("Size", "Second", 90), ("Done", "Third", 80), ("Priority", "Fourth", 130));
    readonly SpeedGraph graph = new();
    readonly TabControl details = new();
    readonly Grid transferView = new() { RowDefinitions = new RowDefinitions("*,5,230") };
    readonly RssView rss;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool refreshing, busy, closing, disposed;
    readonly Queue<string> pendingArguments = new();
    bool ready;
    string sidebarStamp = "";
    TorrentEntry? Selected => (torrents.SelectedItem as TorrentRow)?.Entry;
    string Filter => (sidebar.SelectedItem as Category)?.Key ?? "all";
    internal static DataGrid Table(params (string Header, string Path, int Width)[] columns)
    {
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, SelectionMode = DataGridSelectionMode.Single, CanUserResizeColumns = true, CanUserSortColumns = true, RowHeight = 28, FontSize = 12 };
        foreach (var c in columns) table.Columns.Add(new DataGridTextColumn { Header = c.Header, Binding = new Binding(c.Path), Width = new DataGridLength(c.Width) });
        return table;
    }
    static TextBox ReadOnlyText() => new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Margin = new Thickness(8), FontSize = 12 };
    public MainWindow(ClientRuntime runtime)
    {
        this.runtime = runtime; rss = new RssView(this, runtime);
        Title = "Torrent Classic"; Width = 1240; Height = 800; MinWidth = 1000; MinHeight = 660;
        var root = new DockPanel();
        var menu = new Menu { ItemsSource = new[] {
            Menu("File", Menu("Add torrent…", PickAsync), Menu("Add magnet link…", () => AddAsync("")), Menu("Create torrent from file…", () => CreateAsync(false)), Menu("Create torrent from folder…", () => CreateAsync(true)), Menu("Exit", () => { Close(); return Task.CompletedTask; })),
            Menu("Options", Menu("Preferences…", PreferencesAsync), Menu("Appearance", Menu("System", () => ThemeAsync("System")), Menu("Light", () => ThemeAsync("Light")), Menu("Dark", () => ThemeAsync("Dark"))), Menu("Open remote control", OpenRemoteAsync)),
            Menu("Help", Menu("Project on GitHub", () => OpenAsync("https://github.com/pereira-andre/torrent-classic")), Menu("About", () => Ui.Ask(this, "About Torrent Classic", "Torrent Classic 1.1.0\nFree, portable and ad-free. MIT licensed.\nAn independent client inspired by classic BitTorrent interfaces.\nBuilt with MonoTorrent, Avalonia and .NET.\n\nSee LICENSE and THIRD-PARTY-NOTICES.md in the app folder."))) } };
        DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        var tools = new DockPanel { Margin = new Thickness(4) }; search.Width = 205; search.Margin = new Thickness(8, 2); DockPanel.SetDock(search, Dock.Right); tools.Children.Add(search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { Ui.Button("+ Torrent", () => Run(PickAsync)), Ui.Button("↗ Magnet", () => Run(() => AddAsync(""))), Ui.Button("▶ Start", () => Run(() => WithSelected(Service.StartAsync))), Ui.Button("Ⅱ Pause", () => Run(() => WithSelected(Service.PauseAsync))), Ui.Button("■ Stop", () => Run(() => WithSelected(Service.StopAsync))), Ui.Button("× Remove", () => Run(RemoveAsync)), Ui.Button("↑", () => Run(() => WithSelected(e => Service.MoveQueueAsync(e, -1)))), Ui.Button("↓", () => Run(() => WithSelected(e => Service.MoveQueueAsync(e, 1)))), Ui.Button("Preferences", () => Run(PreferencesAsync)) }) buttons.Children.Add(b);
        tools.Children.Add(buttons); DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var center = new Grid { ColumnDefinitions = new ColumnDefinitions("170,5,*") }; sidebar.Margin = new Thickness(2); center.Children.Add(sidebar); var split = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(split, 1); center.Children.Add(split);
        var content = new Grid(); Grid.SetColumn(content, 2); center.Children.Add(content); content.Children.Add(transferView); content.Children.Add(rss); rss.IsVisible = false;
        torrents.ItemsSource = rows; transferView.Children.Add(torrents); var horizontal = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(horizontal, 1); transferView.Children.Add(horizontal);
        foreach (var (title, view) in new (string, Control)[] { ("General", general), ("Trackers", trackers), ("Peers", peers), ("Files", files), ("Speed", graph), ("Log", log) }) details.Items.Add(new TabItem { Header = title, Content = view });
        Grid.SetRow(details, 2); transferView.Children.Add(details); root.Children.Add(center); Content = root;
        torrents.ContextMenu = new ContextMenu { ItemsSource = new[] { Menu("Start", () => WithSelected(Service.StartAsync)), Menu("Force start", () => WithSelected(Service.ForceStartAsync)), Menu("Pause", () => WithSelected(Service.PauseAsync)), Menu("Stop", () => WithSelected(Service.StopAsync)), Menu("Force re-check", () => WithSelected(Service.CheckAsync)), Menu("Move up", () => WithSelected(e => Service.MoveQueueAsync(e, -1))), Menu("Move down", () => WithSelected(e => Service.MoveQueueAsync(e, 1))), Menu("Set label…", LabelAsync), Menu("Copy magnet link", async () => { if (Selected is { } e && Clipboard is not null) await Clipboard.SetTextAsync(e.Manager.MagnetLink.ToV1String()); }), Menu("Open download folder", () => Selected is { } e ? OpenAsync(e.Manager.SavePath) : Task.CompletedTask), Menu("Remove (keep files)", RemoveAsync) } };
        files.SelectionMode = DataGridSelectionMode.Extended;
        files.ContextMenu = new ContextMenu { ItemsSource = new[] { Priority.DoNotDownload, Priority.Low, Priority.Normal, Priority.High }.Select(p => Menu(Display.PriorityName(p), async () => { if (Selected is { } e) await Service.SetPriorityAsync(e, files.SelectedItems.OfType<TableRow>().Select(r => r.Item).OfType<ITorrentManagerFile>(), p); })).ToArray() };
        search.TextChanged += (_, _) => _ = RefreshAsync(); torrents.SelectionChanged += (_, _) => _ = RefreshDetailsAsync(); details.SelectionChanged += (_, _) => _ = RefreshDetailsAsync();
        sidebar.SelectionChanged += (_, _) => { bool isRss = Filter.StartsWith("rss"); transferView.IsVisible = !isRss; rss.IsVisible = isRss; if (isRss) rss.ShowFeed(Filter.Length > 4 ? Filter[4..] : null); _ = RefreshAsync(); };
        timer.Tick += async (_, _) => await RefreshAsync();
        KeyDown += async (_, e) =>
        {
            var command = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
            if (command && e.Key == Key.F) { search.Focus(); e.Handled = true; return; }
            Func<Task>? action = command ? e.Key switch { Key.O => PickAsync, Key.U => () => AddAsync(""), Key.P => PreferencesAsync, _ => null } : null;
            if (action is not null) { e.Handled = true; await Run(action); }
        };
        Opened += async (_, _) => { await Run(async () => { await Service.RestoreAsync(); if (Program.Smoke) await SmokeAsync(); else { await runtime.StartAsync(); timer.Start(); } }); ready = true; if (!Program.Smoke) HandleArguments(Program.Arguments); };
        Closing += async (_, e) => { if (disposed) return; e.Cancel = true; if (closing) return; closing = true; timer.Stop(); IsEnabled = false; Title = "Torrent Classic — Saving the session…"; try { await runtime.DisposeAsync(); } catch (Exception ex) { await Ui.Ask(this, "Save error", ex.Message); } finally { disposed = true; Close(); } };
    }
    MenuItem Menu(string title, Func<Task> action) { var item = new MenuItem { Header = title }; item.Click += async (_, _) => await Run(action); return item; }
    static MenuItem Menu(string title, params MenuItem[] children) => new() { Header = title, ItemsSource = children };
    Task WithSelected(Func<TorrentEntry, Task> action) => Selected is { } e ? action(e) : Task.CompletedTask;
    internal async Task Run(Func<Task> action)
    {
        if (busy || closing) return; busy = true;
        try { await action(); } catch (Exception e)
        {
            Service.Log(e.Message);
            if (Program.Smoke) { Directory.CreateDirectory(Program.Arguments[1]); await File.WriteAllTextAsync(Path.Combine(Program.Arguments[1], "error.txt"), e.ToString()); Environment.ExitCode = 1; Close(); }
            else await Ui.Ask(this, "Torrent Classic", e.Message);
        }
        finally { busy = false; await RefreshAsync(); if (ready && pendingArguments.Count > 0 && !closing) _ = Run(DrainArgumentsAsync); }
    }
    public void HandleArguments(string[] args)
    {
        if (closing) return; Show(); WindowState = WindowState.Normal; Activate();
        foreach (var source in args.Where(a => !a.StartsWith("--"))) pendingArguments.Enqueue(source);
        if (ready && !busy && pendingArguments.Count > 0) _ = Run(DrainArgumentsAsync);
    }
    async Task DrainArgumentsAsync() { while (pendingArguments.Count > 0 && !closing) await AddAsync(pendingArguments.Dequeue()); }
    async Task PickAsync() { foreach (var path in await Ui.PickTorrents(this)) await AddAsync(path); }
    async Task AddAsync(string source)
    {
        var request = await AddTorrentDialog.Show(this, Service.Preferences, source); if (request is null) return;
        await Service.AddAsync(request.Source, request.Destination, request.Start);
    }
    async Task RemoveAsync() { if (Selected is { } e && await Ui.Ask(this, "Remove torrent", $"Remove {e.Manager.Name} from the list?\nDownloaded files will be kept.", true)) await Service.RemoveAsync(e); }
    async Task LabelAsync() { if (Selected is { } e && await Ui.Prompt(this, "Set label", e.Saved.Label) is { } label) await Service.SetLabelAsync(e, label); }
    async Task PreferencesAsync() { var dialog = new PreferencesWindow(Service.Preferences); if (await dialog.ShowDialog<Preferences?>(this) is { } p) { await runtime.ApplyPreferencesAsync(p); ApplyTheme(p.Theme); } }
    async Task ThemeAsync(string theme) { await runtime.ApplyPreferencesAsync(Service.Preferences with { Theme = theme }); ApplyTheme(theme); }
    static void ApplyTheme(string theme) { if (Application.Current is { } app) app.RequestedThemeVariant = theme switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default }; }
    async Task OpenRemoteAsync() { if (!Service.Preferences.Remote.Enabled) { await Ui.Ask(this, "Remote control", "Enable remote control in Preferences first, then copy the access key."); return; } await OpenAsync($"http://127.0.0.1:{Service.Preferences.Remote.Port}/"); }
    internal static Task OpenAsync(string target) { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return Task.CompletedTask; }
    bool Matches(TorrentEntry e)
    {
        bool category = Filter switch { "downloading" => !e.Manager.Complete && e.Saved.Running, "queued" => e.WaitingReason == "Queued", "seeding" => e.Manager.State == TorrentState.Seeding, "completed" => e.Manager.Complete, "active" => Display.Active(e.Manager), "inactive" => !Display.Active(e.Manager), "unlabelled" => e.Saved.Label.Length == 0, _ => Filter.StartsWith("label:") ? e.Saved.Label == Filter[6..] : true };
        return category && (string.IsNullOrEmpty(search.Text) || e.Manager.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
    }
    async Task RefreshAsync()
    {
        if (refreshing || closing) return; refreshing = true;
        try
        {
            var stamp = string.Join('|', Service.Entries.Select(e => e.Saved.Label).Distinct().Order()) + string.Join('|', runtime.Rss.Feeds.Select(f => f.Id + f.Name));
            if (sidebar.Items.Count == 0 || stamp != sidebarStamp)
            {
                sidebarStamp = stamp; var key = Filter;
                var categories = new List<Category> { new("all", "Torrents"), new("downloading", "  Downloading"), new("queued", "  Queued"), new("seeding", "  Seeding"), new("completed", "  Completed"), new("active", "  Active"), new("inactive", "  Inactive"), new("unlabelled", "No label") };
                categories.AddRange(Service.Entries.Select(e => e.Saved.Label).Where(l => l.Length > 0).Distinct().Order().Select(l => new Category("label:" + l, "  " + l)));
                categories.Add(new Category("rss", "RSS feeds")); categories.AddRange(runtime.Rss.Feeds.Select(f => new Category("rss:" + f.Id, "  " + f.Name))); sidebar.ItemsSource = categories; sidebar.SelectedItem = categories.FirstOrDefault(c => c.Key == key) ?? categories[0];
            }
            var entries = Service.Entries.Where(Matches).OrderBy(e => e.Saved.QueueOrder).ToArray(); var selected = Selected?.Id;
            if (!rows.Select(r => r.Entry.Id).SequenceEqual(entries.Select(e => e.Id))) { rows.Clear(); foreach (var entry in entries) rows.Add(new TorrentRow(entry)); torrents.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == selected) ?? rows.FirstOrDefault(); }
            foreach (var row in rows) row.Refresh();
            long down = Service.Entries.Sum(e => e.Manager.Monitor.DownloadRate), up = Service.Entries.Sum(e => e.Manager.Monitor.UploadRate); graph.Add(down, up);
            status.Text = $"{Service.Entries.Count} torrents  •  D: {Display.Rate(down)}  U: {Display.Rate(up)}  •  {SchedulerSettings.Name(Service.Preferences.Scheduler.ModeAt(DateTimeOffset.Now))}  •  Queue {(Service.Preferences.Queue.Enabled ? "on" : "off")}";
            if (rss.IsVisible) rss.Refresh(); else await RefreshDetailsAsync();
        }
        finally { refreshing = false; }
    }
    async Task RefreshDetailsAsync()
    {
        if (closing) return; var e = Selected;
        if (details.SelectedIndex == 5) { log.Text = string.Join('\n', Service.Messages); return; }
        if (e is null) { general.Text = "Select a torrent to see details."; trackers.ItemsSource = peers.ItemsSource = files.ItemsSource = null; return; }
        var m = e.Manager;
        if (details.SelectedIndex == 0) general.Text = $"Name: {m.Name}\nStatus: {e.Status}     Done: {m.Progress:0.00}%     Size: {(m.Torrent is null ? "—" : Display.Bytes(m.Torrent.Size))}\nDownloaded: {Display.Bytes(e.Downloaded)}     Uploaded: {Display.Bytes(e.Uploaded)}     Ratio: {e.ShareRatio:0.000}\nDownload: {Display.Rate(m.Monitor.DownloadRate)}     Upload: {Display.Rate(m.Monitor.UploadRate)}     Seeds: {m.Peers.Seeds}     Peers: {m.OpenConnections}\nAdded: {e.Saved.AddedUtc.ToLocalTime():g}\nSave in: {m.SavePath}\nHash: {e.Id}";
        else if (details.SelectedIndex == 1) trackers.ItemsSource = new[] { new TableRow("[DHT]", m.CanUseDht ? "Enabled" : "Disabled", ""), new TableRow("[Local peer discovery]", m.CanUseLocalPeerDiscovery ? "Enabled" : "Disabled", ""), new TableRow("[Peer exchange]", m.Settings.AllowPeerExchange ? "Enabled" : "Disabled", "") }.Concat(m.TrackerManager.Tiers.SelectMany(t => t.Trackers).Select(t => new TableRow(t.Uri.ToString(), string.IsNullOrEmpty(t.FailureMessage) ? t.Status.ToString() : t.FailureMessage, Display.Duration(t.UpdateInterval - t.TimeSinceLastAnnounce)))).ToArray();
        else if (details.SelectedIndex == 2) { var connected = await m.GetPeersAsync(); if (Selected?.Id == e.Id) peers.ItemsSource = connected.Select(p => new TableRow(p.Uri.ToString(), p.ClientApp.ToString(), $"{p.BitField.PercentComplete:0.0}%", Display.Rate(p.Monitor.DownloadRate), Display.Rate(p.Monitor.UploadRate))).ToArray(); }
        else if (details.SelectedIndex == 3)
        {
            var selected = files.SelectedItems.OfType<TableRow>().Select(r => (r.Item as ITorrentManagerFile)?.Path).ToHashSet();
            files.ItemsSource = m.Files.Select(f => new TableRow(f.Path, Display.Bytes(f.Length), $"{f.BitField.PercentComplete:0.0}%", Display.PriorityName(f.Priority), Item: f)).ToArray();
            foreach (var row in files.ItemsSource.OfType<TableRow>().Where(r => selected.Contains((r.Item as ITorrentManagerFile)?.Path))) files.SelectedItems.Add(row);
        }
    }
    async Task CreateAsync(bool folder)
    {
        string? source;
        if (folder) source = await Ui.PickFolder(this);
        else source = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose source file" })).FirstOrDefault()?.TryGetLocalPath();
        if (source is null) return;
        var tracker = await Ui.Prompt(this, "Tracker URL (optional)", ""); if (tracker is null) return;
        if (tracker.Length > 0 && (!Uri.TryCreate(tracker, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "udp"))) throw new ArgumentException("Enter an HTTP, HTTPS or UDP tracker URL.");
        var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save torrent", SuggestedFileName = Path.GetFileName(source) + ".torrent", DefaultExtension = "torrent" }); var path = output?.TryGetLocalPath(); if (path is null) return;
        var creator = new TorrentCreator { CreatedBy = "Torrent Classic 1.1", PieceLength = 256 * 1024 }; if (tracker.Length > 0) creator.Announces.Add([tracker]);
        status.Text = "Creating torrent…"; await creator.CreateAsync(new TorrentFileSource(source), path); await Ui.Ask(this, "Create torrent", "Torrent created at:\n" + path + "\n\nAdd it with the folder containing the original files to start seeding.");
    }
    async Task SmokeAsync()
    {
        var output = Path.GetFullPath(Program.Arguments[1]); Directory.CreateDirectory(output);
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var folder = Path.Combine(Service.DataDirectory, "fixture" + i); Directory.CreateDirectory(folder); await File.WriteAllBytesAsync(Path.Combine(folder, "Sample " + i + ".txt"), new byte[1024 * (i + 1)]);
                var file = Path.Combine(Service.DataDirectory, $"sample{i}.torrent"); await new TorrentCreator { CreatedBy = "Torrent Classic UI test", PieceLength = 16384 }.CreateAsync(new TorrentFileSource(folder), file);
                var entry = await Service.AddAsync(file, folder, false); await Service.SetLabelAsync(entry, i == 0 ? "Linux" : "Samples");
            }
            await runtime.Rss.SaveFeedAsync(new RssFeed { Name = "Linux releases", Url = "https://example.org/releases.xml", Enabled = false, Include = "*stable*", AutoDownload = true });
            await RefreshAsync();
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ApplyTheme(theme); await Capture(this, Path.Combine(output, theme.ToLowerInvariant() + ".png"));
                var preferences = new PreferencesWindow(Service.Preferences); var pending = preferences.ShowDialog<Preferences?>(this);
                foreach (var (index, title) in new[] { (3, "queue"), (4, "scheduler"), (5, "remote") }) { preferences.SelectPage(index); await Capture(preferences, Path.Combine(output, title + "-" + theme.ToLowerInvariant() + ".png")); }
                preferences.Close(); await pending;
            }
            sidebar.SelectedItem = sidebar.ItemsSource!.OfType<Category>().First(c => c.Key == "rss"); await Capture(this, Path.Combine(output, "rss-dark.png"));
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "PASS: desktop interface rendered; light/dark, queue, scheduler, remote and RSS.\n");
        }
        catch (Exception e) { await File.WriteAllTextAsync(Path.Combine(output, "error.txt"), e.ToString()); Environment.ExitCode = 1; }
        finally { Close(); }
    }
    static async Task Capture(Window window, string path)
    {
        await Task.Delay(180); window.UpdateLayout();
        if (Program.Headless) window.CaptureRenderedFrame()?.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        else
        {
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        if (!File.Exists(path)) throw new InvalidOperationException("The desktop window did not render: " + path);
    }
}

internal sealed class SpeedGraph : Control
{
    readonly Queue<(double Down, double Up)> points = new();
    public void Add(double down, double up) { points.Enqueue((down, up)); while (points.Count > 180) points.Dequeue(); InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var bounds = Bounds; var grid = new Pen(new SolidColorBrush(Color.Parse("#50777777")), 1);
        for (int i = 1; i < 5; i++) context.DrawLine(grid, new Point(0, bounds.Height * i / 5), new Point(bounds.Width, bounds.Height * i / 5));
        var samples = points.ToArray(); if (samples.Length < 2) return; var max = Math.Max(1024, samples.Max(p => Math.Max(p.Down, p.Up))) * 1.15;
        for (int i = 1; i < samples.Length; i++) { double x1 = (i - 1) * bounds.Width / 179, x2 = i * bounds.Width / 179; context.DrawLine(new Pen(Brushes.SeaGreen, 2), new Point(x1, bounds.Height - samples[i - 1].Down / max * bounds.Height), new Point(x2, bounds.Height - samples[i].Down / max * bounds.Height)); context.DrawLine(new Pen(Brushes.SteelBlue, 2), new Point(x1, bounds.Height - samples[i - 1].Up / max * bounds.Height), new Point(x2, bounds.Height - samples[i].Up / max * bounds.Height)); }
        context.DrawText(new FormattedText($"Download (green) / Upload (blue) · scale {Display.Rate(max)} · last 180 s", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.Gray), new Point(8, 8));
    }
}
