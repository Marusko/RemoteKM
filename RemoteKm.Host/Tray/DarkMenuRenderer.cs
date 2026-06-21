using System.Drawing;
using WinForms = System.Windows.Forms;

namespace RemoteKm.Host.Tray;

/// <summary>
/// A dark, accent-aware renderer for the tray <see cref="WinForms.ContextMenuStrip"/> so it
/// matches the rest of the app instead of the default light Windows menu.
/// </summary>
public sealed class DarkMenuRenderer : WinForms.ToolStripProfessionalRenderer
{
    public static readonly Color Background = ColorTranslator.FromHtml("#16172B");
    public static readonly Color Text = ColorTranslator.FromHtml("#F2F3FB");
    public static readonly Color Muted = ColorTranslator.FromHtml("#9AA0C0");
    public static readonly Color Accent = ColorTranslator.FromHtml("#6C5CE7");
    public static readonly Color Separator = ColorTranslator.FromHtml("#2C2F52");

    public DarkMenuRenderer() : base(new DarkColors()) { }

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
    {
        var r = e.Item.Bounds;
        int y = r.Height / 2;
        using var pen = new Pen(Separator);
        e.Graphics.DrawLine(pen, 12, y, r.Width - 12, y);
    }

    private sealed class DarkColors : WinForms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Separator;
        public override Color MenuItemBorder => Accent;
        public override Color MenuItemSelected => Accent;
        public override Color MenuItemSelectedGradientBegin => Accent;
        public override Color MenuItemSelectedGradientEnd => Accent;
        public override Color MenuItemPressedGradientBegin => Background;
        public override Color MenuItemPressedGradientEnd => Background;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
    }
}
