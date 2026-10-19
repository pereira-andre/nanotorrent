using MonoTorrent;
using TorrentClassic.Core;

namespace TorrentClassic.App;

public sealed class AddDialog : Form
{
    readonly TextBox source = new() { Dock = DockStyle.Fill };
    readonly TextBox destination = new() { Dock = DockStyle.Fill };
    readonly CheckBox start = new() { Text = "Iniciar a transferência", Checked = true, AutoSize = true };
    public string Source => source.Text.Trim();
    public string Destination => destination.Text.Trim();
    public bool StartDownload => start.Checked;

    public AddDialog(string initial, Preferences preferences, Palette colors)
    {
        Text = "Adicionar torrent";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(580, 215);
        var table = DialogLayout.Table(this, 4);
        source.Text = initial;
        destination.Text = preferences.DownloadDirectory;
        DialogLayout.Field(table, 0, "Ficheiro ou magnet:", source, () => { using var file = new OpenFileDialog { Filter = "Ficheiros torrent|*.torrent" }; if (file.ShowDialog(this) == DialogResult.OK) source.Text = file.FileName; });
        DialogLayout.Field(table, 1, "Guardar em:", destination, () => { using var folder = new FolderBrowserDialog { InitialDirectory = destination.Text }; if (folder.ShowDialog(this) == DialogResult.OK) destination.Text = folder.SelectedPath; });
        table.Controls.Add(start, 1, 2);
        var buttons = DialogLayout.Buttons(table, 3, "Adicionar", () =>
        {
            if (!MagnetLink.TryParse(Source, out _) && (!File.Exists(Source) || !Source.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "Escolhe um ficheiro .torrent válido ou cola um magnet link."); return; }
            if (!Path.IsPathFullyQualified(Destination)) { MessageBox.Show(this, "Escolhe uma pasta de destino com caminho completo."); return; }
            DialogResult = DialogResult.OK;
        });
        AcceptButton = buttons;
        Theme.Apply(this, colors);
    }
}

public sealed class PreferencesDialog : Form
{
    readonly Preferences original;
    readonly TextBox destination = new() { Dock = DockStyle.Fill };
    readonly ThemeComboBox theme = new() { Width = 190 };
    readonly NumericUpDown port = Number(0, 65535);
    readonly NumericUpDown down = Number(0, 1000000);
    readonly NumericUpDown up = Number(0, 1000000);
    readonly NumericUpDown connections = Number(5, 5000);
    readonly CheckBox dht = Check("Ativar a rede DHT");
    readonly CheckBox pex = Check("Ativar troca de peers (PEX)");
    readonly CheckBox lpd = Check("Descobrir peers na rede local");
    readonly CheckBox forward = Check("Mapear portas automaticamente (UPnP / NAT-PMP)");
    readonly CheckBox minimize = Check("Minimizar para a área de notificação");
    readonly CheckBox close = Check("O botão de fechar oculta a janela; Sair fecha a app");
    readonly QueueEditor queue;
    readonly ScheduleEditor scheduler;
    readonly RemoteEditor remote;
    public Preferences Result => original with { Theme = theme.Text, DownloadDirectory = destination.Text.Trim(), Port = (int)port.Value, DownloadLimitKiB = (int)down.Value, UploadLimitKiB = (int)up.Value, MaximumConnections = (int)connections.Value, EnableDht = dht.Checked, EnablePeerExchange = pex.Checked, EnableLocalDiscovery = lpd.Checked, EnablePortForwarding = forward.Checked, MinimizeToTray = minimize.Checked, CloseToTray = close.Checked, Queue = queue.Result, Scheduler = scheduler.Result, Remote = remote.Result };
    static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 7) };
    static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 140, ThousandsSeparator = true };

    public PreferencesDialog(Preferences preferences, Palette colors)
    {
        original = preferences;
        queue = new QueueEditor(preferences.Queue);
        scheduler = new ScheduleEditor(preferences.Scheduler);
        remote = new RemoteEditor(preferences.Remote);
        Text = "Preferências — Torrent Classic";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(750, 530);
        var tabs = new ClassicTabs();
        var general = new TabPage("Geral"); var network = new TabPage("Ligações"); var bandwidth = new TabPage("Largura de banda");
        tabs.TabPages.AddRange([general, network, bandwidth]);
        foreach (var (name, control) in new (string, Control)[] { ("Fila", queue), ("Agendador", scheduler), ("Controlo remoto", remote) })
        {
            var page = new TabPage(name); page.Controls.Add(control); tabs.TabPages.Add(page);
        }
        var a = DialogLayout.Table(general, 6);
        theme.Items.AddRange(["Sistema", "Claro", "Escuro"]); theme.SelectedItem = preferences.Theme;
        DialogLayout.Field(a, 0, "Aspeto:", theme);
        destination.Text = preferences.DownloadDirectory;
        DialogLayout.Field(a, 1, "Pasta de downloads:", destination, () => { using var folder = new FolderBrowserDialog { InitialDirectory = destination.Text }; if (folder.ShowDialog(this) == DialogResult.OK) destination.Text = folder.SelectedPath; });
        minimize.Checked = preferences.MinimizeToTray; close.Checked = preferences.CloseToTray;
        a.Controls.Add(minimize, 0, 2); a.SetColumnSpan(minimize, 3);
        a.Controls.Add(close, 0, 3); a.SetColumnSpan(close, 3);
        var b = DialogLayout.Table(network, 7);
        port.Value = preferences.Port; connections.Value = preferences.MaximumConnections;
        DialogLayout.Field(b, 0, "Porta (0 = automática):", port);
        DialogLayout.Field(b, 1, "Máximo de ligações:", connections);
        dht.Checked = preferences.EnableDht; pex.Checked = preferences.EnablePeerExchange; lpd.Checked = preferences.EnableLocalDiscovery; forward.Checked = preferences.EnablePortForwarding;
        var checks = new[] { dht, pex, lpd, forward };
        for (int i = 0; i < checks.Length; i++) { b.Controls.Add(checks[i], 0, i + 2); b.SetColumnSpan(checks[i], 3); }
        var c = DialogLayout.Table(bandwidth, 4);
        down.Value = preferences.DownloadLimitKiB; up.Value = preferences.UploadLimitKiB;
        DialogLayout.Field(c, 0, "Download (KiB/s):", down);
        DialogLayout.Field(c, 1, "Upload (KiB/s):", up);
        var note = new Label { Text = "0 = sem limite. Os limites são globais e aplicam-se imediatamente.", AutoSize = true, Padding = new Padding(0, 15, 0, 0) };
        c.Controls.Add(note, 0, 2); c.SetColumnSpan(note, 3);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 58, ColumnCount = 3, Padding = new Padding(10) };
        var ok = DialogLayout.Buttons(bottom, 0, "Guardar", () => { try { Result.Validate(); DialogResult = DialogResult.OK; } catch (Exception e) { MessageBox.Show(this, e.Message); } });
        Controls.Add(tabs); Controls.Add(bottom);
        AcceptButton = ok;
        Theme.Apply(this, colors);
    }
    public void SelectPage(int index) => Controls.OfType<ClassicTabs>().Single().SelectedIndex = index;
}

public static class DialogLayout
{
    public static TableLayoutPanel Table(Control parent, int rows)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = rows, Padding = new Padding(16) };
        table.ColumnStyles.Add(new(SizeType.Absolute, 155)); table.ColumnStyles.Add(new(SizeType.Percent, 100)); table.ColumnStyles.Add(new(SizeType.Absolute, 38));
        for (var i = 0; i < rows; i++) table.RowStyles.Add(new(SizeType.Absolute, 38));
        parent.Controls.Add(table); return table;
    }
    public static void Field(TableLayoutPanel table, int row, string name, Control control, Action? browse = null)
    {
        table.Controls.Add(new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right; table.Controls.Add(control, 1, row);
        if (browse is not null) { var button = new Button { Text = "…", Dock = DockStyle.Fill }; button.Click += (_, _) => browse(); table.Controls.Add(button, 2, row); }
        else table.SetColumnSpan(control, 2);
    }
    public static Button Buttons(TableLayoutPanel table, int row, string label, Action accept)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = label, AutoSize = true };
        ok.Click += (_, _) => accept();
        flow.Controls.Add(cancel); flow.Controls.Add(ok);
        table.Controls.Add(flow, 0, row); table.SetColumnSpan(flow, 3);
        if (table.FindForm() is { } form) form.CancelButton = cancel;
        return ok;
    }
    public static string? Prompt(IWin32Window owner, string title, string caption, string initial, Palette palette)
    {
        using var form = new Form { Text = title, ClientSize = new Size(460, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var table = Table(form, 2); var text = new TextBox { Text = initial, Dock = DockStyle.Fill };
        Field(table, 0, caption, text);
        form.AcceptButton = Buttons(table, 1, "OK", () => form.DialogResult = DialogResult.OK);
        Theme.Apply(form, palette);
        return form.ShowDialog(owner) == DialogResult.OK ? text.Text.Trim() : null;
    }
}
