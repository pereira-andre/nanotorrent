using System.Net.NetworkInformation;
using System.Net.Sockets;
using TorrentClassic.Core;

namespace TorrentClassic.App;

public sealed class QueueEditor : UserControl
{
    readonly CheckBox enabled = new() { Text = "Ativar fila automática", AutoSize = true };
    readonly NumericUpDown active = Number(1, 1000), downloads = Number(1, 1000), minutes = Number(0, 1000000);
    readonly NumericUpDown ratio = new() { Minimum = 0, Maximum = 10000, DecimalPlaces = 2, Increment = 0.1m, Width = 150 };
    public QueueSettings Result => new() { Enabled = enabled.Checked, MaximumActive = (int)active.Value, MaximumDownloads = (int)downloads.Value, SeedRatio = (double)ratio.Value, SeedMinutes = (int)minutes.Value };
    internal static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 150, ThousandsSeparator = true };
    public QueueEditor(QueueSettings settings)
    {
        Dock = DockStyle.Fill;
        var table = DialogLayout.Table(this, 9); table.ColumnStyles[0].Width = 245;
        enabled.Checked = settings.Enabled; active.Value = settings.MaximumActive; downloads.Value = settings.MaximumDownloads; ratio.Value = (decimal)settings.SeedRatio; minutes.Value = settings.SeedMinutes;
        table.Controls.Add(enabled, 0, 0); table.SetColumnSpan(enabled, 3);
        DialogLayout.Field(table, 1, "Máximo de torrents ativos:", active);
        DialogLayout.Field(table, 2, "Máximo de downloads ativos:", downloads);
        DialogLayout.Field(table, 4, "Parar ao atingir rácio (0 = nunca):", ratio);
        DialogLayout.Field(table, 5, "Ou minutos a partilhar (0 = nunca):", minutes);
        var note = new Label { Text = "Os torrents seguintes iniciam automaticamente quando há uma vaga.\nUsa as setas da barra para mudar a ordem.\n\nO início forçado ignora os limites da fila e os objetivos de partilha,\nmas respeita o agendador. Um torrent pausado manualmente fica em pausa.", Dock = DockStyle.Fill };
        table.Controls.Add(note, 0, 6); table.SetColumnSpan(note, 3); table.SetRowSpan(note, 3);
    }
}

public sealed class ScheduleEditor : UserControl
{
    readonly CheckBox enabled = new() { Text = "Ativar agendador semanal", AutoSize = true };
    readonly NumericUpDown down = QueueEditor.Number(1, 1000000), up = QueueEditor.Number(1, 1000000);
    readonly HourGrid grid;
    public SchedulerSettings Result => new() { Enabled = enabled.Checked, Hours = [.. grid.Hours], DownloadLimitKiB = (int)down.Value, UploadLimitKiB = (int)up.Value };
    public ScheduleEditor(SchedulerSettings settings)
    {
        Dock = DockStyle.Fill; Padding = new Padding(14);
        grid = new HourGrid(settings.Hours) { Dock = DockStyle.Fill };
        enabled.Checked = settings.Enabled; down.Value = settings.DownloadLimitKiB; up.Value = settings.UploadLimitKiB;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 82, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        top.Controls.Add(enabled); top.SetFlowBreak(enabled, true);
        foreach (var mode in Enum.GetValues<ScheduleMode>())
        {
            var button = new Button { Text = SchedulerSettings.Name(mode), Width = 100, Height = 28 };
            button.Click += (_, _) => { grid.Brush = mode; grid.Focus(); }; top.Controls.Add(button);
        }
        var fill = new Button { Text = "Preencher semana", AutoSize = true }; fill.Click += (_, _) => { Array.Fill(grid.Hours, grid.Brush); grid.Invalidate(); }; top.Controls.Add(fill);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90 };
        bottom.Controls.Add(new Label { Text = "Limites do modo limitado (KiB/s):", AutoSize = true }); bottom.SetFlowBreak(bottom.Controls[0], true);
        bottom.Controls.Add(new Label { Text = "Download", AutoSize = true, Margin = new Padding(3, 6, 6, 0) }); bottom.Controls.Add(down);
        bottom.Controls.Add(new Label { Text = "Upload", AutoSize = true, Margin = new Padding(12, 6, 6, 0) }); bottom.Controls.Add(up);
        var note = new Label { Text = "Hora local do Windows. Clica ou arrasta para pintar; botão direito alterna o modo.\nTeclado: setas para escolher uma hora; Espaço para pintar. A app tem de estar aberta.", AutoSize = true, Margin = new Padding(3, 8, 0, 0) };
        bottom.SetFlowBreak(up, true); bottom.Controls.Add(note);
        Controls.Add(grid); Controls.Add(bottom); Controls.Add(top);
    }
    sealed class HourGrid : Control
    {
        public readonly ScheduleMode[] Hours;
        public ScheduleMode Brush = ScheduleMode.Full;
        static readonly Color[] Colors = [Color.FromArgb(75, 158, 85), Color.FromArgb(211, 164, 63), Color.FromArgb(75, 139, 195), Color.FromArgb(108, 112, 117)];
        int focused;
        readonly ToolTip tip = new();
        public HourGrid(ScheduleMode[] hours) { Hours = [.. hours]; DoubleBuffered = true; TabStop = true; AccessibleName = "Grelha semanal do agendador"; }
        Rectangle Cell(int index) => new(55 + index % 24 * Math.Max(1, (Width - 58) / 24), 30 + index / 24 * Math.Max(1, (Height - 38) / 7), Math.Max(1, (Width - 58) / 24) - 2, Math.Max(1, (Height - 38) / 7) - 2);
        int Hit(Point p)
        {
            if (p.X < 55 || p.Y < 30) return -1;
            var column = (p.X - 55) / Math.Max(1, (Width - 58) / 24); var row = (p.Y - 30) / Math.Max(1, (Height - 38) / 7);
            return column < 24 && row < 7 ? row * 24 + column : -1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            string[] days = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];
            for (int hour = 0; hour < 24; hour += 2) TextRenderer.DrawText(e.Graphics, hour.ToString("00"), Font, new Point(Cell(hour).Left, 9), ForeColor);
            for (int day = 0; day < 7; day++) TextRenderer.DrawText(e.Graphics, days[day], Font, new Rectangle(0, Cell(day * 24).Top, 50, Cell(day * 24).Height), ForeColor, TextFormatFlags.VerticalCenter);
            for (var i = 0; i < Hours.Length; i++) { using var brush = new SolidBrush(Colors[(int)Hours[i]]); e.Graphics.FillRectangle(brush, Cell(i)); }
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(Cell(focused), 1, 1), ForeColor, BackColor);
            using var selected = new SolidBrush(Colors[(int)Brush]); e.Graphics.FillRectangle(selected, 4, 8, 15, 15);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus(); var index = Hit(e.Location); if (index < 0) return; focused = index;
            if (e.Button == MouseButtons.Right) Hours[index] = (ScheduleMode)(((int)Hours[index] + 1) % 4); else Hours[index] = Brush;
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var index = Hit(e.Location); if (index < 0) return;
            if (e.Button == MouseButtons.Left) { Hours[index] = Brush; focused = index; Invalidate(); }
            tip.SetToolTip(this, $"{new[] { "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo" }[index / 24]} · {index % 24:00}:00 · {SchedulerSettings.Name(Hours[index])}");
        }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            focused = Math.Clamp(focused + (e.KeyCode switch { Keys.Left => -1, Keys.Right => 1, Keys.Up => -24, Keys.Down => 24, _ => 0 }), 0, 167);
            if (e.KeyCode == Keys.Space) { Hours[focused] = Brush; e.Handled = true; }
            Invalidate(); base.OnKeyDown(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
}

public sealed class RemoteEditor : UserControl
{
    readonly CheckBox enabled = new() { Text = "Ativar controlo remoto", AutoSize = true };
    readonly CheckBox lan = new() { Text = "Permitir outros dispositivos na rede local / VPN", AutoSize = true };
    readonly NumericUpDown port = QueueEditor.Number(1024, 65535);
    readonly TextBox token = new() { Dock = DockStyle.Fill, ReadOnly = true, UseSystemPasswordChar = true };
    public RemoteSettings Result => new() { Enabled = enabled.Checked, AllowLan = lan.Checked, Port = (int)port.Value, Token = token.Text };
    public RemoteEditor(RemoteSettings settings)
    {
        Dock = DockStyle.Fill;
        var table = DialogLayout.Table(this, 11); table.ColumnStyles[0].Width = 160;
        enabled.Checked = settings.Enabled; lan.Checked = settings.AllowLan; port.Value = settings.Port; token.Text = settings.Token.Length > 0 ? settings.Token : RemoteSettings.NewToken();
        table.Controls.Add(enabled, 0, 0); table.SetColumnSpan(enabled, 3);
        table.Controls.Add(lan, 0, 1); table.SetColumnSpan(lan, 3);
        DialogLayout.Field(table, 2, "Porta HTTP:", port);
        DialogLayout.Field(table, 3, "Chave de acesso:", token);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var copy = new Button { Text = "Copiar chave", AutoSize = true }; copy.Click += (_, _) => Clipboard.SetText(token.Text);
        var regenerate = new Button { Text = "Gerar nova chave", AutoSize = true }; regenerate.Click += (_, _) => token.Text = RemoteSettings.NewToken();
        var reveal = new CheckBox { Text = "Mostrar", AutoSize = true, Margin = new Padding(6, 7, 3, 0) }; reveal.CheckedChanged += (_, _) => token.UseSystemPasswordChar = !reveal.Checked;
        actions.Controls.AddRange([copy, regenerate, reveal]); table.Controls.Add(actions, 0, 4); table.SetColumnSpan(actions, 3);
        var addresses = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        void UpdateAddress()
        {
            var ips = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address)).Select(a => $"http://{a.Address}:{port.Value}/").Take(3);
            addresses.Text = $"Neste computador: http://127.0.0.1:{port.Value}/\n" + (lan.Checked ? string.Join("\n", ips) : "Acesso limitado a este computador.");
        }
        port.ValueChanged += (_, _) => UpdateAddress(); lan.CheckedChanged += (_, _) => UpdateAddress(); UpdateAddress();
        table.Controls.Add(addresses, 0, 5); table.SetColumnSpan(addresses, 3); table.SetRowSpan(addresses, 3);
        var note = new Label { Text = "Guarda as preferências e usa Opções → Abrir controlo remoto.\nA chave é necessária para ver ou alterar torrents. A app tem de estar aberta.\n\nO acesso usa HTTP: utiliza uma rede de confiança ou uma VPN.\nNão é um serviço na nuvem; não abre portas no router automaticamente.", Dock = DockStyle.Fill };
        table.Controls.Add(note, 0, 8); table.SetColumnSpan(note, 3); table.SetRowSpan(note, 3);
    }
}
