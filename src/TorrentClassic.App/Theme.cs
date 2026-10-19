using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TorrentClassic.App;

public sealed record Palette(Color Background, Color Surface, Color Text, Color Muted, Color Border, Color Selection, Color Accent, bool Dark)
{
    public static Palette For(string theme)
    {
        var dark = theme == "Dark";
        if (theme == "System")
        {
            try { dark = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0; }
            catch { dark = false; }
        }
        return dark
            ? new(Color.FromArgb(32, 33, 35), Color.FromArgb(24, 25, 27), Color.FromArgb(224, 225, 227), Color.FromArgb(154, 157, 162), Color.FromArgb(59, 61, 65), Color.FromArgb(48, 72, 57), Color.FromArgb(91, 186, 104), true)
            : new(Color.FromArgb(240, 240, 240), Color.White, Color.FromArgb(28, 28, 28), Color.FromArgb(105, 105, 105), Color.FromArgb(210, 210, 210), Color.FromArgb(217, 236, 218), Color.FromArgb(58, 145, 64), false);
    }
}

public static class Theme
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string? id);

    public static void Apply(Control root, Palette p)
    {
        root.BackColor = root is TextBoxBase or TreeView or DataGridView or ListBox ? p.Surface : p.Background;
        root.ForeColor = p.Text;
        if (root is Form form)
        {
            int dark = p.Dark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        }
        if (root is ToolStrip strip)
        {
            strip.Renderer = new StripRenderer(p);
            ApplyItems(strip.Items, p);
        }
        if (root is DataGridView grid)
        {
            grid.BackgroundColor = p.Surface;
            grid.GridColor = p.Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = p.Background;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Background;
            grid.DefaultCellStyle.BackColor = p.Surface;
            grid.DefaultCellStyle.ForeColor = p.Text;
            grid.DefaultCellStyle.SelectionBackColor = p.Selection;
            grid.DefaultCellStyle.SelectionForeColor = p.Text;
        }
        if (root is TreeView tree) SetWindowTheme(tree.Handle, p.Dark ? "DarkMode_Explorer" : "Explorer", null);
        if (root is ClassicTabs tabs) tabs.Colors = p;
        if (root is ThemeComboBox combo) combo.Colors = p;
        if (root is SpeedGraph graph) graph.Colors = p;
        if (root is Button button)
        {
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = p.Border;
            button.FlatAppearance.MouseOverBackColor = p.Selection;
        }
        if (root is TextBoxBase text) text.BorderStyle = text.BorderStyle == BorderStyle.None ? BorderStyle.None : BorderStyle.FixedSingle;
        foreach (Control child in root.Controls) Apply(child, p);
        root.Invalidate(true);
    }

    static void ApplyItems(ToolStripItemCollection items, Palette p)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = p.Text;
            if (item is ToolStripDropDownItem drop) { drop.DropDown.Renderer = new StripRenderer(p); ApplyItems(drop.DropDownItems, p); }
        }
    }

    sealed class Colors(Palette p) : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => p.Background;
        public override Color ToolStripGradientMiddle => p.Background;
        public override Color ToolStripGradientEnd => p.Background;
        public override Color MenuStripGradientBegin => p.Background;
        public override Color MenuStripGradientEnd => p.Background;
        public override Color StatusStripGradientBegin => p.Background;
        public override Color StatusStripGradientEnd => p.Background;
        public override Color ToolStripDropDownBackground => p.Background;
        public override Color ImageMarginGradientBegin => p.Background;
        public override Color ImageMarginGradientMiddle => p.Background;
        public override Color ImageMarginGradientEnd => p.Background;
        public override Color MenuItemSelected => p.Selection;
        public override Color MenuItemSelectedGradientBegin => p.Selection;
        public override Color MenuItemSelectedGradientEnd => p.Selection;
        public override Color MenuItemPressedGradientBegin => p.Selection;
        public override Color MenuItemPressedGradientEnd => p.Selection;
        public override Color MenuBorder => p.Border;
        public override Color MenuItemBorder => p.Border;
        public override Color SeparatorDark => p.Border;
        public override Color SeparatorLight => p.Background;
    }
    sealed class StripRenderer(Palette p) : ToolStripProfessionalRenderer(new Colors(p));
}
