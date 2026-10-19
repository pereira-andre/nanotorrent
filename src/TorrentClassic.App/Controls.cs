using System.Drawing.Drawing2D;
using TorrentClassic.Core;

namespace TorrentClassic.App;

public sealed class ClassicGrid : DataGridView
{
    public ClassicGrid()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        BorderStyle = BorderStyle.None;
        RowHeadersVisible = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToOrderColumns = true;
        ReadOnly = true;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        CellBorderStyle = DataGridViewCellBorderStyle.None;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        ColumnHeadersHeight = 25;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        RowTemplate.Height = 23;
        DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
        DefaultCellStyle.Font = new Font("Segoe UI", 9);
        StandardTab = true;
    }
    public void AddColumn(string name, int width, bool numeric = false)
    {
        var column = new DataGridViewTextBoxColumn { Name = name, HeaderText = name, Width = width, SortMode = DataGridViewColumnSortMode.Programmatic };
        if (numeric) column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        Columns.Add(column);
    }
}

public sealed class ClassicTabs : TabControl
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Palette Colors { get; set; } = Palette.For("Claro");
    public ClassicTabs() { DrawMode = TabDrawMode.OwnerDrawFixed; Dock = DockStyle.Fill; Padding = new Point(12, 4); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var bounds = GetTabRect(e.Index);
        using var brush = new SolidBrush(e.Index == SelectedIndex ? Colors.Surface : Colors.Background);
        e.Graphics.FillRectangle(brush, bounds);
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, bounds, Colors.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (e.Index == SelectedIndex)
        {
            using var pen = new Pen(Colors.Accent, 2);
            e.Graphics.DrawLine(pen, bounds.Left + 3, bounds.Top + 1, bounds.Right - 3, bounds.Top + 1);
        }
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F || TabCount == 0) return;
        // Native TabControl ignores BackColor for its unused header and page frame.
        using var graphics = Graphics.FromHwnd(Handle);
        using var brush = new SolidBrush(Colors.Background);
        var last = GetTabRect(TabCount - 1);
        graphics.FillRectangle(brush, last.Right + 1, 0, Math.Max(0, Width - last.Right - 1), last.Bottom + 1);
        var page = DisplayRectangle;
        graphics.FillRectangle(brush, 0, last.Bottom + 1, page.Left, Height - last.Bottom - 1);
        graphics.FillRectangle(brush, page.Right, last.Bottom + 1, Math.Max(0, Width - page.Right), Height - last.Bottom - 1);
        graphics.FillRectangle(brush, 0, page.Bottom, Width, Math.Max(0, Height - page.Bottom));
        graphics.FillRectangle(brush, 0, last.Bottom, Width, Math.Max(0, page.Top - last.Bottom));
    }
}

public sealed class ThemeComboBox : ComboBox
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Palette Colors { get; set; } = Palette.For("Claro");
    public ThemeComboBox() { DrawMode = DrawMode.OwnerDrawFixed; DropDownStyle = ComboBoxStyle.DropDownList; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var brush = new SolidBrush(e.State.HasFlag(DrawItemState.Selected) ? Colors.Selection : Colors.Surface);
        e.Graphics.FillRectangle(brush, e.Bounds);
        if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, e.Bounds, Colors.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

public sealed class SpeedGraph : Control
{
    readonly Queue<(long Down, long Up)> history = new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Palette Colors { get; set; } = Palette.For("Claro");
    public SpeedGraph() { DoubleBuffered = true; Dock = DockStyle.Fill; }
    public void Add(long down, long up) { history.Enqueue((down, up)); while (history.Count > 180) history.Dequeue(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Colors.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new Rectangle(80, 16, Math.Max(20, Width - 104), Math.Max(20, Height - 57));
        var peak = Math.Max(1024, history.Select(v => Math.Max(v.Down, v.Up)).DefaultIfEmpty().Max() * 1.2);
        using var grid = new Pen(Colors.Border);
        for (var i = 0; i <= 4; i++)
        {
            var y = area.Bottom - area.Height * i / 4;
            g.DrawLine(grid, area.Left, y, area.Right, y);
            TextRenderer.DrawText(g, Display.Rate(peak * i / 4), Font, new Rectangle(0, y - 8, 74, 20), Colors.Muted, TextFormatFlags.Right);
        }
        for (var i = 0; i <= 6; i++) { var x = area.Left + i * area.Width / 6; g.DrawLine(grid, x, area.Top, x, area.Bottom); }
        var samples = history.ToArray();
        if (samples.Length > 1)
        {
            foreach (var upload in new[] { false, true })
            {
                var points = samples.Select((s, i) => new PointF(area.Right - (samples.Length - 1 - i) * area.Width / 179f, area.Bottom - (float)((upload ? s.Up : s.Down) / peak) * area.Height)).ToArray();
                using var pen = new Pen(upload ? Color.FromArgb(78, 151, 220) : Colors.Accent, 1.7f);
                g.DrawLines(pen, points);
            }
        }
        TextRenderer.DrawText(g, "↓ Download", Font, new Point(area.Left, area.Bottom + 12), Colors.Accent);
        TextRenderer.DrawText(g, "↑ Upload", Font, new Point(area.Left + 130, area.Bottom + 12), Color.FromArgb(78, 151, 220));
        TextRenderer.DrawText(g, "Últimos 3 minutos", Font, new Rectangle(area.Right - 150, area.Bottom + 12, 150, 24), Colors.Muted, TextFormatFlags.Right);
    }
}

public static class Icons
{
    public static Bitmap Make(string kind, int size = 20)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 24f, size / 24f);
        using var green = new SolidBrush(Color.FromArgb(70, 157, 66));
        using var red = new SolidBrush(Color.FromArgb(200, 69, 58));
        using var gold = new SolidBrush(Color.FromArgb(211, 159, 39));
        using var pen = new Pen(Color.FromArgb(106, 122, 132), 1.8f);
        switch (kind)
        {
            case "start": g.FillPolygon(green, [new(7, 4), new(20, 12), new(7, 20)]); break;
            case "pause": g.FillRectangle(gold, 6, 5, 4, 15); g.FillRectangle(gold, 14, 5, 4, 15); break;
            case "stop": g.FillRectangle(red, 5, 5, 15, 15); break;
            case "remove": using (var p = new Pen(Color.FromArgb(200, 69, 58), 3)) { g.DrawLine(p, 6, 6, 18, 18); g.DrawLine(p, 18, 6, 6, 18); } break;
            case "link": g.DrawEllipse(pen, 3, 5, 12, 8); g.DrawEllipse(pen, 10, 11, 12, 8); break;
            case "settings": g.DrawEllipse(pen, 4, 4, 16, 16); g.DrawEllipse(pen, 9, 9, 6, 6); for (int i = 0; i < 8; i++) { g.TranslateTransform(12, 12); g.RotateTransform(45); g.TranslateTransform(-12, -12); g.DrawLine(pen, 12, 1, 12, 5); } break;
            case "create": g.DrawRectangle(pen, 5, 3, 13, 18); g.DrawLine(pen, 9, 8, 15, 8); g.DrawLine(pen, 9, 12, 15, 12); break;
            case "down": g.FillPolygon(green, [new(9, 4), new(15, 4), new(15, 12), new(21, 12), new(12, 21), new(3, 12), new(9, 12)]); break;
            case "up": g.FillPolygon(green, [new(9, 20), new(15, 20), new(15, 12), new(21, 12), new(12, 3), new(3, 12), new(9, 12)]); break;
            case "check": using (var p = new Pen(Color.FromArgb(70, 157, 66), 3)) { g.DrawLine(p, 4, 12, 10, 18); g.DrawLine(p, 10, 18, 21, 5); } break;
            default: g.DrawRectangle(pen, 4, 3, 14, 18); g.FillRectangle(green, 13, 11, 9, 4); g.FillRectangle(green, 15.5f, 8.5f, 4, 9); break;
        }
        return bitmap;
    }
}
