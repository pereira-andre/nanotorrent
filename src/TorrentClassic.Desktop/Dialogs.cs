using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TorrentClassic.Core;

namespace TorrentClassic.Desktop;

internal static class Ui
{
    public static Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Padding = new Thickness(10, 5), Margin = new Thickness(2) };
        button.Click += async (_, _) => await action(); return button;
    }
    public static TextBox Text(string text = "", string? hint = null) => new() { Text = text, PlaceholderText = hint, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 5) };
    public static NumericUpDown Number(decimal value, decimal max = 1000000, decimal min = 0) => new() { Value = value, Minimum = min, Maximum = max, Width = 150, HorizontalAlignment = HorizontalAlignment.Left, FormatString = "0.##" };
    public static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 10 }; foreach (var child in children) panel.Children.Add(child); return panel;
    }
    public static Control Field(string name, Control value, Button? browse = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(browse is null ? "220,*" : "220,*,42"), Margin = new Thickness(0, 3) };
        grid.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1); grid.Children.Add(value);
        if (browse is not null) { Grid.SetColumn(browse, 2); grid.Children.Add(browse); }
        return grid;
    }
    public static Window Dialog(string title, Control body, Control footer, double width = 740, double height = 500)
    {
        var panel = new DockPanel { Margin = new Thickness(14) }; DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(body);
        return new Window { Title = title, Width = width, Height = height, MinWidth = width, MinHeight = height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
    }
    public static StackPanel Buttons(params Control[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var button in buttons) row.Children.Add(button); return row;
    }
    public static async Task<bool> Ask(Window owner, string title, string text, bool confirm = false)
    {
        Window dialog = null!;
        var yes = Button(confirm ? "Yes" : "OK", () => { dialog.Close(true); return Task.CompletedTask; });
        var no = Button("Cancel", () => { dialog.Close(false); return Task.CompletedTask; });
        dialog = Dialog(title, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12) }, confirm ? Buttons(no, yes) : Buttons(yes), 530, 235);
        return await dialog.ShowDialog<bool>(owner);
    }
    public static async Task<string?> PickFolder(Window owner)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose download folder", AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    public static async Task<string[]> PickTorrents(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Add torrent", AllowMultiple = true, FileTypeFilter = [new FilePickerFileType("Torrent files") { Patterns = ["*.torrent"] }] });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }
    public static async Task<string?> Prompt(Window owner, string title, string initial)
    {
        var text = Text(initial); Window dialog = null!;
        dialog = Dialog(title, Stack(text), Buttons(Button("Cancel", () => { dialog.Close(); return Task.CompletedTask; }), Button("OK", () => { dialog.Close(text.Text ?? ""); return Task.CompletedTask; })), 530, 190);
        return await dialog.ShowDialog<string?>(owner);
    }
}

internal sealed record AddRequest(string Source, string Destination, bool Start);
internal static class AddTorrentDialog
{
    public static async Task<AddRequest?> Show(Window owner, Preferences preferences, string initial)
    {
        var source = Ui.Text(initial, "Torrent file or magnet link"); var folder = Ui.Text(preferences.DownloadDirectory); var start = Ui.Check("Start downloading", true); var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        Window dialog = null!;
        var body = Ui.Stack(Ui.Field("File or magnet:", source, Ui.Button("…", async () => { var files = await Ui.PickTorrents(dialog); if (files.Length > 0) source.Text = files[0]; })), Ui.Field("Save in:", folder, Ui.Button("…", async () => { var selected = await Ui.PickFolder(dialog); if (selected is not null) folder.Text = selected; })), start, error);
        dialog = Ui.Dialog("Add torrent", body, Ui.Buttons(Ui.Button("Cancel", () => { dialog.Close(); return Task.CompletedTask; }), Ui.Button("Add", () =>
        {
            if (string.IsNullOrWhiteSpace(source.Text)) { error.Text = "Enter a torrent file or magnet link."; return Task.CompletedTask; }
            if (!Path.IsPathFullyQualified(folder.Text ?? "")) { error.Text = "Choose a download folder with an absolute path."; return Task.CompletedTask; }
            dialog.Close(new AddRequest(source.Text.Trim(), folder.Text!, start.IsChecked == true)); return Task.CompletedTask;
        })), 720, 285);
        return await dialog.ShowDialog<AddRequest?>(owner);
    }
}

internal sealed class PreferencesWindow : Window
{
    readonly Preferences original;
    readonly TabControl tabs = new();
    readonly ComboBox theme;
    readonly TextBox destination;
    readonly NumericUpDown port, connections, down, up, active, downloads, ratio, minutes, limitedDown, limitedUp, remotePort;
    readonly CheckBox dht, pex, lpd, forward, queue, schedule, remote, lan;
    readonly TextBox token;
    readonly ScheduleMode[] hours;
    readonly List<Button> hourButtons = [];
    public Preferences Result => original with
    {
        Theme = theme.SelectedItem?.ToString() ?? "System", DownloadDirectory = destination.Text?.Trim() ?? "", Port = (int)(port.Value ?? 0), MaximumConnections = (int)(connections.Value ?? 200), DownloadLimitKiB = (int)(down.Value ?? 0), UploadLimitKiB = (int)(up.Value ?? 0),
        EnableDht = dht.IsChecked == true, EnablePeerExchange = pex.IsChecked == true, EnableLocalDiscovery = lpd.IsChecked == true, EnablePortForwarding = forward.IsChecked == true,
        Queue = new QueueSettings { Enabled = queue.IsChecked == true, MaximumActive = (int)(active.Value ?? 8), MaximumDownloads = (int)(downloads.Value ?? 3), SeedRatio = (double)(ratio.Value ?? 0), SeedMinutes = (int)(minutes.Value ?? 0) },
        Scheduler = new SchedulerSettings { Enabled = schedule.IsChecked == true, Hours = [.. hours], DownloadLimitKiB = (int)(limitedDown.Value ?? 128), UploadLimitKiB = (int)(limitedUp.Value ?? 32) },
        Remote = new RemoteSettings { Enabled = remote.IsChecked == true, AllowLan = lan.IsChecked == true, Port = (int)(remotePort.Value ?? 8080), Token = token.Text ?? "" }
    };
    public PreferencesWindow(Preferences preferences)
    {
        original = preferences; hours = [.. preferences.Scheduler.Hours];
        Title = "Preferences — Torrent Classic"; Width = 820; Height = 600; MinWidth = 820; MinHeight = 600; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        theme = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = preferences.Theme, Width = 170 };
        destination = Ui.Text(preferences.DownloadDirectory);
        port = Ui.Number(preferences.Port, 65535); connections = Ui.Number(preferences.MaximumConnections, 5000, 5); down = Ui.Number(preferences.DownloadLimitKiB); up = Ui.Number(preferences.UploadLimitKiB);
        dht = Ui.Check("Enable DHT", preferences.EnableDht); pex = Ui.Check("Enable peer exchange (PEX)", preferences.EnablePeerExchange); lpd = Ui.Check("Discover local peers", preferences.EnableLocalDiscovery); forward = Ui.Check("Automatically map BitTorrent ports (UPnP / NAT-PMP)", preferences.EnablePortForwarding);
        queue = Ui.Check("Enable automatic queue", preferences.Queue.Enabled); active = Ui.Number(preferences.Queue.MaximumActive, 1000, 1); downloads = Ui.Number(preferences.Queue.MaximumDownloads, 1000, 1); ratio = Ui.Number((decimal)preferences.Queue.SeedRatio, 10000); ratio.Increment = 0.1m; minutes = Ui.Number(preferences.Queue.SeedMinutes);
        schedule = Ui.Check("Enable weekly scheduler", preferences.Scheduler.Enabled); limitedDown = Ui.Number(preferences.Scheduler.DownloadLimitKiB, 1000000, 1); limitedUp = Ui.Number(preferences.Scheduler.UploadLimitKiB, 1000000, 1);
        remote = Ui.Check("Enable remote control", preferences.Remote.Enabled); lan = Ui.Check("Allow other devices on the local network / VPN", preferences.Remote.AllowLan); remotePort = Ui.Number(preferences.Remote.Port, 65535, 1024); token = Ui.Text(preferences.Remote.Token.Length > 0 ? preferences.Remote.Token : RemoteSettings.NewToken()); token.PasswordChar = '•'; token.IsReadOnly = true;
        AddTab("General", Ui.Stack(Ui.Field("Appearance:", theme), Ui.Field("Download folder:", destination, Ui.Button("…", async () => { var path = await Ui.PickFolder(this); if (path is not null) destination.Text = path; })), Note("All app data stays in the portable Data folder.\nKeep the app running for RSS, scheduling and remote access.")));
        AddTab("Connections", Ui.Stack(Ui.Field("Port (0 = automatic):", port), Ui.Field("Maximum connections:", connections), dht, pex, lpd, forward));
        AddTab("Bandwidth", Ui.Stack(Ui.Field("Download (KiB/s):", down), Ui.Field("Upload (KiB/s):", up), Note("0 = unlimited. Global limits apply immediately.")));
        AddTab("Queue", Ui.Stack(queue, Ui.Field("Maximum active torrents:", active), Ui.Field("Maximum downloads:", downloads), Ui.Field("Stop at share ratio (0 = never):", ratio), Ui.Field("Or seeding minutes (0 = never):", minutes), Note("Queued torrents start when a slot is available. Use the toolbar arrows to reorder.\nForce start bypasses queue limits and seeding goals, but respects the scheduler.\nManually paused torrents stay paused.")));
        AddTab("Scheduler", BuildScheduler());
        var show = Ui.Check("Show key", false); show.IsCheckedChanged += (_, _) => token.PasswordChar = show.IsChecked == true ? '\0' : '•';
        AddTab("Remote control", Ui.Stack(remote, lan, Ui.Field("HTTP port:", remotePort), Ui.Field("Access key:", token), Ui.Buttons(Ui.Button("Copy key", async () => { if (Clipboard is not null) await Clipboard.SetTextAsync(token.Text ?? ""); }), Ui.Button("Generate new key", () => { token.Text = RemoteSettings.NewToken(); return Task.CompletedTask; }), show), Note("Save, then use Options → Open remote control.\nOn this computer: http://127.0.0.1:PORT/\nOn another device: http://THIS-COMPUTER-IP:PORT/\n\nHTTP is intended for trusted networks or a VPN. Keep the app running.\nNo cloud account, router mapping or firewall changes are made by the app.")));
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        var footer = Ui.Stack(error, Ui.Buttons(Ui.Button("Cancel", () => { Close(); return Task.CompletedTask; }), Ui.Button("Save", () => { try { Result.Validate(); Close(Result); } catch (Exception e) { error.Text = e.Message; } return Task.CompletedTask; })));
        var panel = new DockPanel { Margin = new Thickness(12) }; DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(tabs); Content = panel;
    }
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 12 };
    void AddTab(string title, Control control) { control.Margin = new Thickness(14); tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = control } }); }
    public void SelectPage(int index) => tabs.SelectedIndex = index;
    Control BuildScheduler()
    {
        var mode = new ComboBox { ItemsSource = Enum.GetValues<ScheduleMode>().Select(SchedulerSettings.Name).ToArray(), SelectedIndex = 0, Width = 140 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("42,*"), RowDefinitions = new RowDefinitions("24,*") };
        var labels = new UniformGrid { Columns = 24, Rows = 1 }; for (int h = 0; h < 24; h++) labels.Children.Add(new TextBlock { Text = h % 2 == 0 ? h.ToString("00") : "", FontSize = 11 }); Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        var days = new UniformGrid { Columns = 1, Rows = 7 }; foreach (var day in new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }) days.Children.Add(new TextBlock { Text = day, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }); Grid.SetRow(days, 1); grid.Children.Add(days);
        var cells = new UniformGrid { Columns = 24, Rows = 7, Height = 190 };
        static IBrush Colour(ScheduleMode value) => new SolidColorBrush(value switch { ScheduleMode.Limited => Color.Parse("#d3a43f"), ScheduleMode.SeedOnly => Color.Parse("#4b8bc3"), ScheduleMode.Paused => Color.Parse("#6c7075"), _ => Color.Parse("#4b9e55") });
        for (int i = 0; i < hours.Length; i++)
        {
            int index = i; var button = new Button { Background = Colour(hours[i]), Padding = new Thickness(0), Margin = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            void Paint() { hours[index] = (ScheduleMode)Math.Max(0, mode.SelectedIndex); button.Background = Colour(hours[index]); }
            button.Click += (_, _) => Paint(); button.PointerEntered += (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) Paint(); };
            ToolTip.SetTip(button, $"{new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }[i / 24]} {i % 24:00}:00");
            hourButtons.Add(button); cells.Children.Add(button);
        }
        Grid.SetColumn(cells, 1); Grid.SetRow(cells, 1); grid.Children.Add(cells);
        return Ui.Stack(schedule, Ui.Buttons(mode, Ui.Button("Fill the week", () => { for (int i = 0; i < hours.Length; i++) { hours[i] = (ScheduleMode)Math.Max(0, mode.SelectedIndex); hourButtons[i].Background = Colour(hours[i]); } return Task.CompletedTask; })), grid, Ui.Field("Limited download (KiB/s):", limitedDown), Ui.Field("Limited upload (KiB/s):", limitedUp), Note("Local time. Select a mode, then click or drag across hours.\nThe app must be running. Manual pauses are preserved."));
    }
}

internal static class FeedDialog
{
    public static async Task<RssFeed?> Show(Window owner, RssFeed feed)
    {
        var name = Ui.Text(feed.Name); var url = Ui.Text(feed.Url); var include = Ui.Text(feed.Include); var exclude = Ui.Text(feed.Exclude); var path = Ui.Text(feed.Destination, "Default download folder"); var interval = Ui.Number(feed.RefreshMinutes, 1440, 2); var enabled = Ui.Check("Enable this subscription", feed.Enabled); var automatic = Ui.Check("Automatically download items matching the filters", feed.AutoDownload); var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        Window dialog = null!;
        var body = Ui.Stack(Ui.Field("Name:", name), Ui.Field("RSS / Atom URL:", url), Ui.Field("Include:", include), Ui.Field("Exclude:", exclude), Ui.Field("Folder (blank = default):", path, Ui.Button("…", async () => { var selected = await Ui.PickFolder(dialog); if (selected is not null) path.Text = selected; })), Ui.Field("Refresh interval (minutes):", interval), enabled, automatic, new TextBlock { Text = "Filters ignore case. Use * and ? as wildcards, | for alternatives.\nExample: Ubuntu*|Debian* · Exclude: *beta*|*arm*\nAuto-download includes items already present in the feed.", TextWrapping = TextWrapping.Wrap, FontSize = 12 }, error);
        dialog = Ui.Dialog("RSS subscription", new ScrollViewer { Content = body }, Ui.Buttons(Ui.Button("Cancel", () => { dialog.Close(); return Task.CompletedTask; }), Ui.Button("Save", () =>
        {
            var result = feed with { Name = name.Text?.Trim() ?? "", Url = url.Text?.Trim() ?? "", Include = include.Text ?? "", Exclude = exclude.Text ?? "", Destination = path.Text?.Trim() ?? "", RefreshMinutes = (int)(interval.Value ?? 15), Enabled = enabled.IsChecked == true, AutoDownload = automatic.IsChecked == true };
            try { result.Validate(); dialog.Close(result); } catch (Exception e) { error.Text = e.Message; } return Task.CompletedTask;
        })), 780, 610);
        return await dialog.ShowDialog<RssFeed?>(owner);
    }
}
