using TorrentClassic.Core;

namespace TorrentClassic.App;

public sealed class RssPanel : UserControl
{
    readonly ClientRuntime runtime;
    readonly ClassicGrid grid = new();
    readonly ToolStrip toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(10), AutoEllipsis = true };
    string? selectedFeed;
    bool busy;
    public event Action? FeedsChanged;
    public RssPanel(ClientRuntime runtime)
    {
        this.runtime = runtime; Dock = DockStyle.Fill;
        foreach (var (label, action) in new (string, Func<Task>)[] { ("Adicionar feed…", AddAsync), ("Editar…", EditAsync), ("Remover feed", RemoveAsync), ("Atualizar", RefreshAsync), ("Transferir selecionados", DownloadAsync) })
        {
            var button = new ToolStripButton(label); button.Click += (_, _) => _ = RunAsync(action); toolbar.Items.Add(button);
        }
        foreach (var (name, width) in new[] { ("Título", 330), ("Feed", 140), ("Publicado", 140), ("Estado", 120), ("Origem", 360) }) grid.AddColumn(name, width);
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) _ = RunAsync(DownloadAsync); };
        Controls.Add(grid); Controls.Add(toolbar); Controls.Add(status);
    }
    public void SelectFeed(string? id) { selectedFeed = id; RefreshSnapshot(); }
    public void RefreshSnapshot()
    {
        if (busy) return;
        var feeds = runtime.Rss.Feeds;
        var rows = feeds.Where(f => selectedFeed is null || f.Id == selectedFeed).SelectMany(f => f.Items.Select(i => (Feed: f, Item: i))).ToArray();
        var existing = grid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag is ValueTuple<string, string> pair ? pair.Item1 + pair.Item2 : "").ToArray();
        var ids = rows.Select(r => r.Feed.Id + r.Item.Id).ToArray();
        var selected = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).ToHashSet();
        var rebuild = !existing.SequenceEqual(ids);
        if (rebuild) grid.Rows.Clear();
        for (var i = 0; i < rows.Length; i++)
        {
            var (feed, item) = rows[i];
            if (rebuild) grid.Rows.Add();
            var row = grid.Rows[i]; row.Tag = (feed.Id, item.Id);
            row.SetValues(item.Title, feed.Name, item.Published?.ToLocalTime().ToString("g") ?? "—", item.Downloaded ? "Adicionado" : FeedParser.Matches(item.Title, feed.Include, feed.Exclude) ? "Disponível" : "Excluído pelo filtro", item.Source);
            if (rebuild) row.Selected = selected.Contains(row.Tag);
        }
        var current = feeds.FirstOrDefault(f => f.Id == selectedFeed);
        status.Text = current is null ? $"{feeds.Count} feeds RSS · {rows.Length} itens. Seleciona um feed na barra lateral para o gerir." : $"{current.Name} · {(current.Enabled ? "Ativo" : "Desativado")} · {(current.AutoDownload ? "Automático" : "Manual")} · Última atualização: {current.LastChecked?.ToLocalTime().ToString("g") ?? "nunca"}" + (current.LastError is null ? "" : " · " + current.LastError);
    }
    RssFeed? CurrentFeed() => runtime.Rss.Feeds.FirstOrDefault(f => f.Id == selectedFeed) ?? (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is ValueTuple<string, string> pair ? runtime.Rss.Feeds.FirstOrDefault(f => f.Id == pair.Item1) : null);
    Palette CurrentPalette => Palette.For(runtime.Service.Preferences.Theme);
    async Task AddAsync()
    {
        using var dialog = new RssDialog(new RssFeed(), runtime.Service.Preferences.DownloadDirectory, CurrentPalette);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await runtime.Rss.SaveFeedAsync(dialog.Result); selectedFeed = dialog.Result.Id; FeedsChanged?.Invoke();
        await runtime.Rss.RefreshAsync(selectedFeed);
    }
    async Task EditAsync()
    {
        if (CurrentFeed() is not { } feed) throw new InvalidOperationException("Seleciona um feed na barra lateral.");
        using var dialog = new RssDialog(feed, runtime.Service.Preferences.DownloadDirectory, CurrentPalette);
        if (dialog.ShowDialog(this) == DialogResult.OK) { await runtime.Rss.SaveFeedAsync(dialog.Result); FeedsChanged?.Invoke(); }
    }
    async Task RemoveAsync()
    {
        if (CurrentFeed() is not { } feed) return;
        if (MessageBox.Show(this, $"Remover a subscrição «{feed.Name}»?\nOs torrents e ficheiros transferidos serão mantidos.", "Remover feed", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await runtime.Rss.RemoveFeedAsync(feed.Id); selectedFeed = null; FeedsChanged?.Invoke();
    }
    async Task RefreshAsync()
    {
        foreach (var feed in runtime.Rss.Feeds.Where(f => selectedFeed is null || f.Id == selectedFeed)) await runtime.Rss.RefreshAsync(feed.Id);
    }
    async Task DownloadAsync()
    {
        var items = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<ValueTuple<string, string>>().ToArray();
        foreach (var (feed, item) in items) await runtime.Rss.DownloadAsync(feed, item);
    }
    async Task RunAsync(Func<Task> action)
    {
        if (busy) return; busy = true; toolbar.Enabled = false; status.Text = "A atualizar RSS…";
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { runtime.Service.Log("RSS: " + e.Message); MessageBox.Show(this, e.Message, "RSS", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { busy = false; toolbar.Enabled = true; RefreshSnapshot(); }
    }
}

public sealed class RssDialog : Form
{
    readonly RssFeed original;
    readonly TextBox name = new() { Dock = DockStyle.Fill }, url = new() { Dock = DockStyle.Fill }, include = new() { Dock = DockStyle.Fill }, exclude = new() { Dock = DockStyle.Fill }, destination = new() { Dock = DockStyle.Fill };
    readonly CheckBox enabled = new() { Text = "Ativar esta subscrição", AutoSize = true }, automatic = new() { Text = "Transferir automaticamente os itens que correspondem aos filtros", AutoSize = true };
    readonly NumericUpDown interval = QueueEditor.Number(2, 1440);
    public RssFeed Result => original with { Name = name.Text.Trim(), Url = url.Text.Trim(), Include = include.Text.Trim(), Exclude = exclude.Text.Trim(), Destination = destination.Text.Trim(), Enabled = enabled.Checked, AutoDownload = automatic.Checked, RefreshMinutes = (int)interval.Value };
    public RssDialog(RssFeed feed, string defaultFolder, Palette palette)
    {
        original = feed; Text = "Subscrição RSS"; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ClientSize = new Size(720, 465);
        var table = DialogLayout.Table(this, 11); table.ColumnStyles[0].Width = 180;
        name.Text = feed.Name; url.Text = feed.Url; include.Text = feed.Include; exclude.Text = feed.Exclude; destination.Text = feed.Destination; destination.PlaceholderText = defaultFolder;
        enabled.Checked = feed.Enabled; automatic.Checked = feed.AutoDownload; interval.Value = feed.RefreshMinutes;
        DialogLayout.Field(table, 0, "Nome:", name); DialogLayout.Field(table, 1, "Endereço RSS / Atom:", url);
        DialogLayout.Field(table, 2, "Incluir:", include); DialogLayout.Field(table, 3, "Excluir:", exclude);
        DialogLayout.Field(table, 4, "Pasta (vazia = predefinida):", destination, () => { using var folder = new FolderBrowserDialog(); if (folder.ShowDialog(this) == DialogResult.OK) destination.Text = folder.SelectedPath; });
        DialogLayout.Field(table, 5, "Atualizar a cada (min):", interval);
        table.Controls.Add(enabled, 0, 6); table.SetColumnSpan(enabled, 3);
        table.Controls.Add(automatic, 0, 7); table.SetColumnSpan(automatic, 3);
        var note = new Label { Text = "Filtros sem distinção de maiúsculas: * e ? são caracteres universais; | separa alternativas.\nExemplo: Linux*|Ubuntu* · Excluir: *arm*|*beta*\nIncluir vazio aceita tudo. A transferência automática inclui os itens já presentes no feed.", Dock = DockStyle.Fill };
        table.Controls.Add(note, 0, 8); table.SetColumnSpan(note, 3); table.SetRowSpan(note, 2);
        AcceptButton = DialogLayout.Buttons(table, 10, "Guardar", () => { try { Result.Validate(); DialogResult = DialogResult.OK; } catch (Exception e) { MessageBox.Show(this, e.Message); } });
        Theme.Apply(this, palette);
    }
}
