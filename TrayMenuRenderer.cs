using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Minimemizer;

internal static class TrayMenuTheme
{
    internal static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { return false; }
    }

    internal static void Apply(Forms.ContextMenuStrip menu)
    {
        var dark = IsDarkMode();
        var colors = new TrayColorTable(dark);
        menu.Renderer = new TrayMenuRenderer(colors);
        menu.BackColor = colors.Background;
        menu.ForeColor = colors.Foreground;
        menu.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.Padding = new Forms.Padding(6);
        menu.MinimumSize = new Size(190, 0);
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = colors.Foreground;
            if (item is Forms.ToolStripMenuItem) item.Padding = new Forms.Padding(10, 7, 18, 7);
        }
    }

    internal static void ApplyRoundedRegion(Forms.ContextMenuStrip menu)
    {
        if (menu.Width <= 0 || menu.Height <= 0) return;
        var radius = Math.Max(8, (int)Math.Round(10 * menu.DeviceDpi / 96d));
        using var path = RoundedRectangle(new Rectangle(0, 0, menu.Width, menu.Height), radius);
        var old = menu.Region;
        menu.Region = new Region(path);
        old?.Dispose();
    }

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class TrayMenuRenderer(TrayColorTable colors) : Forms.ToolStripProfessionalRenderer(colors)
{
    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) return;
        var bounds = new Rectangle(3, 2, e.Item.Width - 6, e.Item.Height - 4);
        using var path = TrayMenuTheme.RoundedRectangle(bounds, 6);
        using var brush = new SolidBrush(colors.Selected);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? colors.Foreground : colors.Disabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(colors.Separator);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var path = TrayMenuTheme.RoundedRectangle(bounds, 10);
        using var pen = new Pen(colors.Border);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class TrayColorTable(bool dark) : Forms.ProfessionalColorTable
{
    internal Color Background { get; } = dark ? Color.FromArgb(255, 43, 43, 43) : Color.FromArgb(255, 249, 249, 249);
    internal Color Foreground { get; } = dark ? Color.FromArgb(255, 245, 245, 245) : Color.FromArgb(255, 28, 28, 28);
    internal Color Selected { get; } = dark ? Color.FromArgb(255, 62, 62, 62) : Color.FromArgb(255, 232, 232, 232);
    internal Color Disabled { get; } = dark ? Color.FromArgb(255, 135, 135, 135) : Color.FromArgb(255, 145, 145, 145);
    internal Color Separator { get; } = dark ? Color.FromArgb(255, 75, 75, 75) : Color.FromArgb(255, 218, 218, 218);
    internal Color Border { get; } = dark ? Color.FromArgb(255, 83, 83, 83) : Color.FromArgb(255, 205, 205, 205);

    public override Color ToolStripDropDownBackground => Background;
    public override Color ImageMarginGradientBegin => Background;
    public override Color ImageMarginGradientMiddle => Background;
    public override Color ImageMarginGradientEnd => Background;
    public override Color MenuBorder => Border;
    public override Color MenuItemBorder => Selected;
    public override Color MenuItemSelected => Selected;
    public override Color SeparatorDark => Separator;
    public override Color SeparatorLight => Separator;
}
