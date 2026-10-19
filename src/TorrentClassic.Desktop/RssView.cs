using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TorrentClassic.Core;

namespace TorrentClassic.Desktop;
internal sealed record FeedRow(string Title, string Subscription, string Published, string State, string FeedId, string ItemId);
internal sealed class RssView : DockPanel
{
    readonly MainWindow owner; readonly ClientRuntime runtime;
    readonly DataGrid table = MainWindow.Table(("Title", "Title", 360), ("Subscription", "Subscription", 150), ("Published", "Published", 150), ("Status", "State", 150));
    readonly TextBlock info = new() { Margin = new Thickness(10), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    string? feedId; string stamp = "";
    RssFeed? SelectedFeed => runtime.Rss.Feeds.FirstOrDefault(f => f.Id == ((table.SelectedItem as FeedRow)?.FeedId ?? feedId));
    public RssView(MainWindow owner, ClientRuntime runtime)
    {
        this.owner = owner; this.runtime = runtime;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Button("+ Subscription", () => owner.Run(async () => { if (await FeedDialog.Show(owner, new RssFeed()) is { } feed) await runtime.Rss.SaveFeedAsync(feed); })));
        buttons.Children.Add(Ui.Button("Edit", () => owner.Run(async () => { if (SelectedFeed is { } f && await FeedDialog.Show(owner, f) is { } edited) await runtime.Rss.SaveFeedAsync(edited); })));
        buttons.Children.Add(Ui.Button("Remove", () => owner.Run(async () => { if (SelectedFeed is { } f && await Ui.Ask(owner, "Remove subscription", $"Remove {f.Name}? Existing torrents and files will be kept.", true)) await runtime.Rss.RemoveFeedAsync(f.Id); })));
        buttons.Children.Add(Ui.Button("Refresh", () => owner.Run(async () => { foreach (var f in runtime.Rss.Feeds.Where(f => feedId is null || f.Id == feedId)) await runtime.Rss.RefreshAsync(f.Id); })));
        buttons.Children.Add(Ui.Button("Download selected", () => owner.Run(async () => { if (table.SelectedItem is FeedRow item) await runtime.Rss.DownloadAsync(item.FeedId, item.ItemId); })));
        SetDock(buttons, Dock.Top); Children.Add(buttons); SetDock(info, Dock.Bottom); Children.Add(info); Children.Add(table);
    }
    public void ShowFeed(string? id) { feedId = id; stamp = ""; Refresh(); }
    public void Refresh()
    {
        var feeds = runtime.Rss.Feeds.Where(f => feedId is null || f.Id == feedId).ToArray();
        info.Text = feeds.Length == 0 ? "Add an RSS or Atom subscription to follow new torrents. Use filters for automatic downloads." : string.Join('\n', feeds.Select(f => $"{f.Name} · {(f.Enabled ? "Enabled" : "Disabled")} · {(f.AutoDownload ? "Auto-download" : "Manual")} · Last checked: {f.LastChecked?.LocalDateTime.ToString("g") ?? "Never"}" + (f.LastError is { } error ? " · " + error : "")));
        var items = feeds.SelectMany(f => f.Items.Select(i => new FeedRow(i.Title, f.Name, i.Published?.LocalDateTime.ToString("g") ?? "—", i.Downloaded ? "Added" : FeedParser.Matches(i.Title, f.Include, f.Exclude) ? "Matches filter" : "Filtered out", f.Id, i.Id))).ToArray();
        var next = string.Join('|', items.Select(i => i.ToString())); if (next == stamp) return; stamp = next; var selected = table.SelectedItem as FeedRow; table.ItemsSource = items; table.SelectedItem = items.FirstOrDefault(i => i.ItemId == selected?.ItemId && i.FeedId == selected.FeedId);
    }
}
