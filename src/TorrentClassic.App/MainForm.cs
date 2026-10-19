using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using MonoTorrent;
using MonoTorrent.Client;
using TorrentClassic.Core;

namespace TorrentClassic.App;

public sealed class MainForm : Form
{
    readonly TorrentService service;
    readonly MenuStrip menu = new();
    readonly ToolStrip toolbar = new() { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(5, 3, 5, 3), ImageScalingSize = new Size(22, 22) };
    readonly StatusStrip status = new();
    readonly ToolStripStatusLabel networkStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel downStatus = new();
    readonly ToolStripStatusLabel upStatus = new();
    readonly ToolStripTextBox search = new() { AutoSize = false, Width = 190, ToolTipText = "Filtrar torrents pelo nome (Ctrl+F)" };
    readonly TreeView categories = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, ShowLines = false, ShowPlusMinus = false, ShowRootLines = false, FullRowSelect = true, ItemHeight = 27, Indent = 17 };
    readonly SplitContainer outer = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Size = new Size(1140, 650), SplitterWidth = 4, Panel1MinSize = 145, SplitterDistance = 185 };
    readonly SplitContainer inner = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(940, 650), SplitterWidth = 5, Panel1MinSize = 120, Panel2MinSize = 145, SplitterDistance = 395 };
    readonly ClassicGrid torrents = new();
    readonly ClassicGrid files = new();
    readonly ClassicGrid peers = new();
    readonly ClassicGrid trackers = new();
    readonly ClassicTabs details = new();
    readonly SpeedGraph graph = new();
    readonly TextBox log = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, BorderStyle = BorderStyle.None, WordWrap = false };
    readonly Dictionary<string, Label> values = [];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly NotifyIcon tray = new() { Text = "Torrent Classic" };
    readonly ToolStripMenuItem themeMenu = new("Aspeto");
    readonly TaskCompletionSource ready = new();
    Palette palette;
    bool refreshing, shutdown, exitRequested, trulyClosed, commandRunning;
    int sortColumn;
    bool descending;
    string? fileOwner;
    public Task Ready => ready.Task;
    public TorrentService Service => service;

    public MainForm(TorrentService torrentService)
    {
        service = torrentService;
        palette = Palette.For(service.Preferences.Theme);
        Text = "Torrent Classic";
        Font = new Font("Segoe UI", 9);
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(940, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        AllowDrop = true;
        MainMenuStrip = menu;
        BuildMenus(); BuildToolbar(); BuildCategories(); BuildDetails();
        foreach (var (name, width, numeric) in new (string, int, bool)[] { ("#", 36, true), ("Nome", 285, false), ("Tamanho", 85, true), ("Concluído", 105, false), ("Estado", 150, false), ("Seeds", 60, true), ("Peers", 60, true), ("Vel. download", 100, true), ("Vel. upload", 100, true), ("Tempo restante", 105, true), ("Rácio", 65, true), ("Etiqueta", 95, false) }) torrents.AddColumn(name, width, numeric);
        torrents.CellPainting += PaintProgress;
        torrents.SelectionChanged += (_, _) => { fileOwner = null; _ = RefreshDetailsAsync(); };
        torrents.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedFolder(); };
        torrents.ColumnHeaderMouseClick += (_, e) => { descending = sortColumn == e.ColumnIndex && !descending; sortColumn = e.ColumnIndex; RefreshRows(); };
        torrents.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; _ = Run(RemoveSelectedAsync); } };
        var context = new ContextMenuStrip();
        context.Items.Add(Item("Iniciar", () => SelectedOperation(service.StartAsync), icon: "start"));
        context.Items.Add(Item("Pausar", () => SelectedOperation(service.PauseAsync), icon: "pause"));
        context.Items.Add(Item("Parar", () => SelectedOperation(service.StopAsync), icon: "stop"));
        context.Items.Add(new ToolStripSeparator());
        context.Items.Add(Item("Abrir pasta de destino", () => { OpenSelectedFolder(); return Task.CompletedTask; }));
        context.Items.Add(Item("Copiar magnet link", () => { if (Selected is { } entry) Clipboard.SetText(entry.Manager.MagnetLink.ToV1String()); return Task.CompletedTask; }));
        context.Items.Add(Item("Definir etiqueta…", SetLabelAsync));
        context.Items.Add(Item("Forçar verificação", () => SelectedOperation(service.CheckAsync)));
        context.Items.Add(Item("Atualizar trackers", AnnounceAsync));
        context.Items.Add(new ToolStripSeparator());
        context.Items.Add(Item("Remover da lista…", RemoveSelectedAsync, icon: "remove"));
        torrents.ContextMenuStrip = context;
        torrents.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !torrents.Rows[e.RowIndex].Selected) { torrents.ClearSelection(); torrents.Rows[e.RowIndex].Selected = true; } };
        outer.Panel1.Controls.Add(categories);
        inner.Panel1.Controls.Add(torrents); inner.Panel2.Controls.Add(details);
        outer.Panel2.Controls.Add(inner);
        status.Items.AddRange([networkStatus, downStatus, upStatus]);
        downStatus.BorderSides = ToolStripStatusLabelBorderSides.Left;
        upStatus.BorderSides = ToolStripStatusLabelBorderSides.Left;
        Controls.Add(outer); Controls.Add(toolbar); Controls.Add(menu); Controls.Add(status);
        tray.Icon = Icon;
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Mostrar Torrent Classic", null, (_, _) => ShowWindow());
        trayMenu.Items.Add("Iniciar todos", null, (_, _) => _ = Run(async () => { foreach (var entry in service.Entries) await service.StartAsync(entry); }));
        trayMenu.Items.Add("Pausar todos", null, (_, _) => _ = Run(async () => { foreach (var entry in service.Entries) await service.PauseAsync(entry); }));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Sair", null, (_, _) => { exitRequested = true; Close(); });
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => ShowWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && service.Preferences.MinimizeToTray) { tray.Visible = true; Hide(); } };
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true || e.Data?.GetDataPresent(DataFormats.UnicodeText) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => _ = Run(async () =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) foreach (var path in paths.Where(p => p.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))) await AddSourceAsync(path);
            else if (e.Data?.GetData(DataFormats.UnicodeText) is string text) await AddSourceAsync(text);
        });
        timer.Tick += async (_, _) => await TickAsync();
        Shown += async (_, _) =>
        {
            try { await service.RestoreAsync(); RefreshRows(); timer.Start(); ready.TrySetResult(); }
            catch (Exception e) { ready.TrySetException(e); ShowError(e); }
        };
        FormClosing += ClosingAsync;
        ApplyTheme();
    }

    ToolStripMenuItem Item(string label, Func<Task> action, Keys shortcut = Keys.None, string? icon = null)
    {
        var item = new ToolStripMenuItem(label) { ShortcutKeys = shortcut };
        if (icon is not null) item.Image = Icons.Make(icon);
        item.Click += (_, _) => _ = Run(action);
        return item;
    }
    void BuildMenus()
    {
        var file = new ToolStripMenuItem("&Ficheiro");
        file.DropDownItems.Add(Item("Adicionar torrent…", ChooseTorrentAsync, Keys.Control | Keys.O, "add"));
        file.DropDownItems.Add(Item("Adicionar magnet link…", () => AddSourceAsync(""), Keys.Control | Keys.U, "link"));
        file.DropDownItems.Add(Item("Criar novo torrent…", CreateTorrentAsync, Keys.Control | Keys.N, "create"));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Sair", () => { exitRequested = true; Close(); return Task.CompletedTask; }, Keys.Alt | Keys.F4));
        var options = new ToolStripMenuItem("&Opções");
        options.DropDownItems.Add(Item("Preferências…", PreferencesAsync, Keys.Control | Keys.P, "settings"));
        foreach (var mode in new[] { "Sistema", "Claro", "Escuro" }) themeMenu.DropDownItems.Add(Item(mode, () => ChangeThemeAsync(mode)));
        options.DropDownItems.Add(themeMenu);
        var view = new ToolStripMenuItem("&Ver");
        var showDetails = new ToolStripMenuItem("Painel de detalhes") { CheckOnClick = true, Checked = true, ShortcutKeys = Keys.F5 };
        showDetails.CheckedChanged += (_, _) => inner.Panel2Collapsed = !showDetails.Checked;
        var showSidebar = new ToolStripMenuItem("Barra lateral") { CheckOnClick = true, Checked = true, ShortcutKeys = Keys.F7 };
        showSidebar.CheckedChanged += (_, _) => outer.Panel1Collapsed = !showSidebar.Checked;
        view.DropDownItems.AddRange([showDetails, showSidebar]);
        view.DropDownItems.Add(Item("Selecionar todos", () => { torrents.SelectAll(); return Task.CompletedTask; }, Keys.Control | Keys.A));
        view.DropDownItems.Add(Item("Pesquisar", () => { search.Focus(); return Task.CompletedTask; }, Keys.Control | Keys.F));
        var help = new ToolStripMenuItem("A&juda");
        help.DropDownItems.Add(Item("Sobre o Torrent Classic", () =>
        {
            MessageBox.Show(this, "Torrent Classic 1.0.0\n\nCliente BitTorrent gratuito e open source.\nInterface inspirada no µTorrent Classic.\nProjeto independente, sem afiliação ao µTorrent ou à Rainberry.\n\nCódigo da aplicação: licença MIT.\nMotor BitTorrent: MonoTorrent (MIT).\nLicenças completas na pasta licenses da distribuição.\n\nSem anúncios, telemetria ou subscrições.", "Sobre", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }));
        menu.Items.AddRange([file, options, view, help]);
    }
    void BuildToolbar()
    {
        void Button(string kind, string tooltip, Func<Task> action)
        {
            var button = new ToolStripButton { Image = Icons.Make(kind), DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = tooltip, AutoSize = false, Width = 32, Height = 29 };
            button.Click += (_, _) => _ = Run(action); toolbar.Items.Add(button);
        }
        Button("add", "Adicionar torrent (Ctrl+O)", ChooseTorrentAsync);
        Button("link", "Adicionar magnet link (Ctrl+U)", () => AddSourceAsync(""));
        Button("create", "Criar novo torrent (Ctrl+N)", CreateTorrentAsync);
        toolbar.Items.Add(new ToolStripSeparator());
        Button("remove", "Remover da lista (Delete)", RemoveSelectedAsync);
        toolbar.Items.Add(new ToolStripSeparator());
        Button("start", "Iniciar", () => SelectedOperation(service.StartAsync));
        Button("pause", "Pausar", () => SelectedOperation(service.PauseAsync));
        Button("stop", "Parar", () => SelectedOperation(service.StopAsync));
        toolbar.Items.Add(new ToolStripSeparator());
        Button("check", "Forçar verificação", () => SelectedOperation(service.CheckAsync));
        Button("settings", "Preferências (Ctrl+P)", PreferencesAsync);
        search.Alignment = ToolStripItemAlignment.Right;
        search.TextBox.PlaceholderText = "Pesquisar torrents";
        search.TextChanged += (_, _) => RefreshRows();
        toolbar.Items.Add(search);
    }
    void BuildCategories()
    {
        foreach (var (key, text) in new[] { ("all", "Torrents"), ("downloading", "A transferir"), ("seeding", "A partilhar"), ("completed", "Concluídos"), ("active", "Ativos"), ("inactive", "Inativos"), ("labels", "Etiquetas"), ("unlabelled", "Sem etiqueta") })
        {
            var node = new TreeNode(text) { Name = key, Tag = text };
            if (key is "downloading" or "seeding" or "completed" or "active" or "inactive") categories.Nodes[0].Nodes.Add(node);
            else if (key == "unlabelled") categories.Nodes["labels"]!.Nodes.Add(node);
            else categories.Nodes.Add(node);
        }
        categories.ExpandAll(); categories.SelectedNode = categories.Nodes[0];
        categories.AfterSelect += (_, _) => RefreshRows();
    }
    void BuildDetails()
    {
        var general = new TabPage("Geral") { AutoScroll = true };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(10, 8, 10, 8) };
        table.ColumnStyles.Add(new(SizeType.Absolute, 112)); table.ColumnStyles.Add(new(SizeType.Percent, 50)); table.ColumnStyles.Add(new(SizeType.Absolute, 112)); table.ColumnStyles.Add(new(SizeType.Percent, 50));
        var labels = new[] { "Nome", "Estado", "Tamanho", "Concluído", "Recebido", "Enviado", "Download", "Upload", "Seeds", "Peers", "Adicionado", "Rácio", "Guardar em", "Hash" };
        for (int i = 0; i < labels.Length; i++)
        {
            var row = i / 2; var col = i % 2 * 2;
            if (col == 0) table.RowStyles.Add(new(SizeType.Absolute, 25));
            table.Controls.Add(new Label { Text = labels[i] + ":", AutoSize = true, Anchor = AnchorStyles.Left }, col, row);
            var value = new Label { Text = "—", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            values[labels[i]] = value; table.Controls.Add(value, col + 1, row);
        }
        general.Controls.Add(table);
        foreach (var (title, control) in new (string, Control)[] { ("Geral", general), ("Trackers", trackers), ("Peers", peers), ("Ficheiros", files), ("Velocidade", graph), ("Registo", log) })
        {
            if (control is TabPage page) details.TabPages.Add(page);
            else { var tab = new TabPage(title); tab.Controls.Add(control); details.TabPages.Add(tab); }
        }
        foreach (var (name, width) in new[] { ("Nome", 420), ("Tamanho", 100), ("Concluído", 100), ("Prioridade", 130) }) files.AddColumn(name, width);
        foreach (var (name, width) in new[] { ("Endereço", 240), ("Cliente", 170), ("Concluído", 100), ("Download", 110), ("Upload", 110), ("Encriptação", 120) }) peers.AddColumn(name, width);
        foreach (var (name, width) in new[] { ("Nome", 430), ("Estado", 150), ("Próxima atualização", 160) }) trackers.AddColumn(name, width);
        var priorities = new ContextMenuStrip();
        foreach (var (label, priority) in new[] { ("Alta", Priority.High), ("Normal", Priority.Normal), ("Baixa", Priority.Low), ("Não transferir", Priority.DoNotDownload) })
            priorities.Items.Add(Item(label, async () => { if (Selected is { } entry) { var selectedFiles = files.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<ITorrentManagerFile>().ToArray(); await service.SetPriorityAsync(entry, selectedFiles, priority); await RefreshDetailsAsync(); } }));
        files.ContextMenuStrip = priorities;
        files.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !files.Rows[e.RowIndex].Selected) { files.ClearSelection(); files.Rows[e.RowIndex].Selected = true; } };
        details.SelectedIndexChanged += (_, _) => _ = RefreshDetailsAsync();
    }

    TorrentEntry? Selected => torrents.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Tag).OfType<TorrentEntry>().FirstOrDefault();
    TorrentEntry[] SelectedEntries => torrents.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Tag).OfType<TorrentEntry>().ToArray();
    bool Matches(TorrentEntry e, string category)
    {
        var m = e.Manager;
        return category switch
        {
            "downloading" => !m.Complete && Display.Active(m), "seeding" => m.State == TorrentState.Seeding,
            "completed" => m.Complete, "active" => m.Monitor.DownloadRate > 0 || m.Monitor.UploadRate > 0,
            "inactive" => m.Monitor.DownloadRate == 0 && m.Monitor.UploadRate == 0,
            "unlabelled" => string.IsNullOrEmpty(e.Saved.Label),
            _ when category.StartsWith("label:", StringComparison.Ordinal) => e.Saved.Label == category[6..], _ => true
        };
    }
    void RefreshRows()
    {
        if (shutdown) return;
        var all = service.Entries;
        categories.BeginUpdate();
        foreach (TreeNode parent in categories.Nodes)
        {
            if (parent.Name != "labels") parent.Text = $"{parent.Tag} ({all.Count(e => Matches(e, parent.Name))})";
            foreach (TreeNode node in parent.Nodes) node.Text = $"{node.Tag} ({all.Count(e => Matches(e, node.Name))})";
        }
        var labelParent = categories.Nodes["labels"]!;
        var labels = all.Select(e => e.Saved.Label).Where(s => s.Length > 0).Distinct().Order().ToArray();
        foreach (var label in labels)
            if (!labelParent.Nodes.ContainsKey("label:" + label)) labelParent.Nodes.Add(new TreeNode(label) { Name = "label:" + label, Tag = label });
        foreach (TreeNode node in labelParent.Nodes.Cast<TreeNode>().Where(n => n.Name.StartsWith("label:", StringComparison.Ordinal) && !labels.Contains(n.Name[6..])).ToArray()) node.Remove();
        categories.EndUpdate();
        var category = categories.SelectedNode?.Name ?? "all";
        var visible = all.Where(e => Matches(e, category) && e.Manager.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase));
        Func<TorrentEntry, IComparable> key = sortColumn switch
        {
            1 => e => e.Manager.Name, 2 => e => e.Manager.Torrent?.Size ?? 0L, 3 => e => e.Manager.Progress, 4 => e => Display.Status(e.Manager),
            5 => e => e.Manager.Peers.Seeds, 6 => e => e.Manager.Peers.Leechs, 7 => e => e.Manager.Monitor.DownloadRate,
            8 => e => e.Manager.Monitor.UploadRate, 10 => e => Ratio(e.Manager), 11 => e => e.Saved.Label, _ => e => e.Saved.AddedUtc
        };
        var rows = (descending ? visible.OrderByDescending(key) : visible.OrderBy(key)).ToArray();
        var selected = SelectedEntries.Select(e => e.Id).ToHashSet();
        var existing = torrents.Rows.Cast<DataGridViewRow>().Select(r => (r.Tag as TorrentEntry)?.Id).ToArray();
        var rebuild = !rows.Select(e => e.Id).SequenceEqual(existing);
        if (rebuild) { torrents.Rows.Clear(); foreach (var e in rows) torrents.Rows[torrents.Rows.Add()].Tag = e; }
        foreach (DataGridViewRow row in torrents.Rows)
        {
            var entry = (TorrentEntry)row.Tag!; var m = entry.Manager;
            row.SetValues(Array.IndexOf(all.ToArray(), entry) + 1, m.Name, m.HasMetadata ? Display.Bytes(m.Torrent!.Size) : "—", m.Progress, Display.Status(m), m.Peers.Seeds, m.Peers.Leechs, Display.Rate(m.Monitor.DownloadRate), Display.Rate(m.Monitor.UploadRate), Eta(m), Ratio(m).ToString("0.000", CultureInfo.CurrentCulture), entry.Saved.Label);
            if (rebuild) row.Selected = selected.Contains(entry.Id);
        }
        foreach (DataGridViewColumn column in torrents.Columns) column.HeaderCell.SortGlyphDirection = column.Index == sortColumn ? (descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
        networkStatus.Text = $"DHT : {service.Engine.Dht.NodeCount} nós     |     {all.Count} torrents";
        downStatus.Text = " ↓ " + Display.Rate(service.Engine.TotalDownloadRate) + "   ";
        upStatus.Text = " ↑ " + Display.Rate(service.Engine.TotalUploadRate) + "   ";
        torrents.Invalidate();
    }
    static double Ratio(TorrentManager m) => m.Monitor.DataBytesSent / (double)Math.Max(1, m.Monitor.DataBytesReceived);
    static string Eta(TorrentManager m) => m.Complete ? "—" : m.Monitor.DownloadRate <= 0 || m.Torrent is null ? "∞" : Display.Duration(TimeSpan.FromSeconds(m.Torrent.Size * (100 - m.Progress) / 100 / m.Monitor.DownloadRate));

    void PaintProgress(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.ColumnIndex != 3 || e.RowIndex < 0 || e.Value is not double percent) return;
        e.PaintBackground(e.ClipBounds, true);
        var bounds = Rectangle.Inflate(e.CellBounds, -5, -5);
        using var background = new SolidBrush(palette.Dark ? Color.FromArgb(48, 50, 52) : Color.FromArgb(230, 234, 230));
        using var fill = new SolidBrush(palette.Accent);
        e.Graphics!.FillRectangle(background, bounds);
        e.Graphics.FillRectangle(fill, new Rectangle(bounds.X, bounds.Y, (int)(bounds.Width * Math.Clamp(percent / 100, 0, 1)), bounds.Height));
        TextRenderer.DrawText(e.Graphics, percent.ToString("0.0", CultureInfo.CurrentCulture) + "%", torrents.Font, e.CellBounds, palette.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        e.Handled = true;
    }
    async Task TickAsync()
    {
        if (refreshing || shutdown || commandRunning) return;
        refreshing = true;
        try
        {
            graph.Add(service.Engine.TotalDownloadRate, service.Engine.TotalUploadRate);
            RefreshRows(); await RefreshDetailsAsync();
        }
        catch (Exception e) { service.Log(e.Message); }
        finally { refreshing = false; }
    }
    async Task RefreshDetailsAsync()
    {
        if (shutdown) return;
        var entry = Selected;
        if (entry is null)
        {
            foreach (var value in values.Values) value.Text = "—";
            files.Rows.Clear(); peers.Rows.Clear(); trackers.Rows.Clear(); fileOwner = null;
        }
        else
        {
            var m = entry.Manager;
            var data = new Dictionary<string, string> { ["Nome"] = m.Name, ["Estado"] = Display.Status(m), ["Tamanho"] = m.Torrent is null ? "—" : Display.Bytes(m.Torrent.Size), ["Concluído"] = $"{m.Progress:0.00}%", ["Recebido"] = Display.Bytes(m.Monitor.DataBytesReceived), ["Enviado"] = Display.Bytes(m.Monitor.DataBytesSent), ["Download"] = Display.Rate(m.Monitor.DownloadRate), ["Upload"] = Display.Rate(m.Monitor.UploadRate), ["Seeds"] = m.Peers.Seeds.ToString(), ["Peers"] = m.OpenConnections.ToString(), ["Adicionado"] = entry.Saved.AddedUtc.ToLocalTime().ToString("g"), ["Rácio"] = Ratio(m).ToString("0.000"), ["Guardar em"] = m.SavePath, ["Hash"] = entry.Id };
            foreach (var pair in data) values[pair.Key].Text = pair.Value;
            if (details.SelectedIndex == 1)
            {
                trackers.Rows.Clear();
                trackers.Rows.Add("[DHT]", m.CanUseDht ? "Ativo" : "Desativado", "—");
                trackers.Rows.Add("[Descoberta local]", m.CanUseLocalPeerDiscovery ? "Ativa" : "Desativada", "—");
                trackers.Rows.Add("[Troca de peers]", m.Settings.AllowPeerExchange && m.Torrent?.IsPrivate != true ? "Ativa" : "Desativada", "—");
                foreach (var tier in m.TrackerManager.Tiers)
                    foreach (var tracker in tier.Trackers)
                    {
                        var next = tracker.UpdateInterval - tracker.TimeSinceLastAnnounce;
                        trackers.Rows.Add(tracker.Uri.ToString(), string.IsNullOrEmpty(tracker.FailureMessage) ? tracker.Status.ToString() : tracker.FailureMessage, next > TimeSpan.Zero ? Display.Duration(next) : "—");
                    }
            }
            if (details.SelectedIndex == 2)
            {
                var connected = await m.GetPeersAsync();
                if (Selected?.Id != entry.Id || shutdown) return;
                peers.Rows.Clear();
                foreach (var peer in connected) peers.Rows.Add(peer.Uri.ToString(), peer.ClientApp.ToString(), $"{peer.BitField.PercentComplete:0.0}%", Display.Rate(peer.Monitor.DownloadRate), Display.Rate(peer.Monitor.UploadRate), peer.EncryptionType.ToString());
            }
            if (details.SelectedIndex == 3)
            {
                if (fileOwner != entry.Id || files.Rows.Count != m.Files.Count)
                {
                    files.Rows.Clear();
                    foreach (var file in m.Files) files.Rows[files.Rows.Add(file.Path, Display.Bytes(file.Length), "", "")].Tag = file;
                    fileOwner = entry.Id;
                }
                foreach (DataGridViewRow row in files.Rows)
                {
                    var file = (ITorrentManagerFile)row.Tag!;
                    row.Cells[2].Value = $"{file.BitField.PercentComplete:0.0}%";
                    row.Cells[3].Value = Display.PriorityName(file.Priority);
                }
            }
        }
        if (details.SelectedIndex == 5)
        {
            var text = string.Join(Environment.NewLine, service.Messages);
            if (log.Text != text) { log.Text = text; log.SelectionStart = log.TextLength; log.ScrollToCaret(); }
        }
    }

    async Task Run(Func<Task> action)
    {
        if (commandRunning || shutdown) return;
        commandRunning = true;
        try { await action(); RefreshRows(); await RefreshDetailsAsync(); }
        catch (Exception e) { ShowError(e); }
        finally { commandRunning = false; }
    }
    void ShowError(Exception e) { service.Log(e.ToString()); if (!shutdown) MessageBox.Show(this, e.Message, "Torrent Classic", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    async Task SelectedOperation(Func<TorrentEntry, Task> action) { foreach (var entry in SelectedEntries) await action(entry); }
    async Task ChooseTorrentAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Ficheiros torrent (*.torrent)|*.torrent", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) foreach (var path in dialog.FileNames) await AddSourceAsync(path);
    }
    public async Task AddSourceAsync(string source)
    {
        using var dialog = new AddDialog(source, service.Preferences, palette);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var entry = await service.AddAsync(dialog.Source, dialog.Destination, dialog.StartDownload);
            RefreshRows();
            foreach (DataGridViewRow row in torrents.Rows) row.Selected = (row.Tag as TorrentEntry)?.Id == entry.Id;
        }
    }
    async Task RemoveSelectedAsync()
    {
        var selected = SelectedEntries;
        if (selected.Length == 0) return;
        if (MessageBox.Show(this, $"Remover {selected.Length} torrent(s) da lista?\n\nOs ficheiros transferidos serão mantidos no disco.", "Remover torrent", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            foreach (var entry in selected) await service.RemoveAsync(entry);
    }
    async Task SetLabelAsync()
    {
        if (Selected is not { } entry) return;
        var label = DialogLayout.Prompt(this, "Etiqueta", "Nome da etiqueta:", entry.Saved.Label, palette);
        if (label is not null) foreach (var item in SelectedEntries) await service.SetLabelAsync(item, label);
    }
    async Task AnnounceAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        foreach (var entry in SelectedEntries) await entry.Manager.TrackerManager.AnnounceAsync(timeout.Token);
    }
    void OpenSelectedFolder()
    {
        if (Selected is { } entry) Process.Start(new ProcessStartInfo("explorer.exe", entry.Manager.SavePath) { UseShellExecute = true });
    }
    async Task PreferencesAsync()
    {
        using var dialog = new PreferencesDialog(service.Preferences, palette);
        if (dialog.ShowDialog(this) == DialogResult.OK) { await service.SavePreferencesAsync(dialog.Result); ApplyTheme(); }
    }
    async Task ChangeThemeAsync(string theme) { await service.SavePreferencesAsync(service.Preferences with { Theme = theme }); ApplyTheme(); }
    public void ApplyTheme()
    {
        palette = Palette.For(service.Preferences.Theme);
        Theme.Apply(this, palette);
        if (torrents.ContextMenuStrip is { } context) Theme.Apply(context, palette);
        if (files.ContextMenuStrip is { } priorities) Theme.Apply(priorities, palette);
        if (tray.ContextMenuStrip is { } trayMenu) Theme.Apply(trayMenu, palette);
        foreach (ToolStripMenuItem item in themeMenu.DropDownItems) item.Checked = item.Text == service.Preferences.Theme;
    }
    async Task CreateTorrentAsync()
    {
        var kind = MessageBox.Show(this, "Criar um torrent a partir de uma pasta?\n\nSim: escolher uma pasta.\nNão: escolher um ficheiro.", "Criar torrent", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        string source;
        if (kind == DialogResult.Yes) { using var folder = new FolderBrowserDialog(); if (folder.ShowDialog(this) != DialogResult.OK) return; source = folder.SelectedPath; }
        else if (kind == DialogResult.No) { using var file = new OpenFileDialog(); if (file.ShowDialog(this) != DialogResult.OK) return; source = file.FileName; }
        else return;
        var announce = DialogLayout.Prompt(this, "Criar torrent", "Tracker (opcional):", "", palette);
        if (announce is null) return;
        if (announce.Length > 0 && (!Uri.TryCreate(announce, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "udp"))) throw new ArgumentException("O endereço do tracker deve começar por https://, http:// ou udp://.");
        using var save = new SaveFileDialog { Filter = "Ficheiros torrent|*.torrent", FileName = Path.GetFileName(source) + ".torrent" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        UseWaitCursor = true; toolbar.Enabled = menu.Enabled = false;
        try
        {
            var creator = new TorrentCreator { CreatedBy = "Torrent Classic 1.0", PieceLength = 256 * 1024 };
            if (announce.Length > 0) creator.Announces.Add(new List<string> { announce });
            await creator.CreateAsync(new TorrentFileSource(source), save.FileName);
            MessageBox.Show(this, "Torrent criado em:\n" + save.FileName + "\n\nAdiciona-o à lista e escolhe a pasta onde estão os ficheiros originais para começar a partilhar.", "Criar torrent");
        }
        finally { UseWaitCursor = false; toolbar.Enabled = menu.Enabled = true; }
    }
    public void ShowWindow()
    {
        Show(); WindowState = FormWindowState.Normal; Activate(); tray.Visible = false;
    }
    public void HandleArguments(string[] args)
    {
        ShowWindow();
        _ = Run(async () => { foreach (var arg in args) if (!arg.StartsWith("--", StringComparison.Ordinal)) await AddSourceAsync(arg); });
    }
    public void RequestExit() { exitRequested = true; Close(); }
    async void ClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (trulyClosed) return;
        e.Cancel = true;
        if (service.Preferences.CloseToTray && !exitRequested && e.CloseReason == CloseReason.UserClosing) { tray.Visible = true; Hide(); return; }
        if (shutdown) return;
        shutdown = true; timer.Stop(); Enabled = false; tray.Visible = false;
        Text = "Torrent Classic — A guardar a sessão…";
        try { await service.DisposeAsync(); }
        catch (Exception error) { MessageBox.Show(this, "Não foi possível guardar toda a sessão: " + error.Message, "Torrent Classic"); }
        finally { tray.Dispose(); timer.Dispose(); trulyClosed = true; Close(); }
    }
}
