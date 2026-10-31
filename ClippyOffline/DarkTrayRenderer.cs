using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClippyOffline;

/// <summary>Fully custom-painted dark menu for the notification-area icon.</summary>
internal sealed class DarkTrayRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Bg       = Color.FromArgb(38, 38, 42);
    private static readonly Color Border   = Color.FromArgb(75, 75, 82);
    private static readonly Color Hover    = Color.FromArgb(67, 67, 76);
    private static readonly Color Fg       = Color.FromArgb(235, 235, 240);
    private static readonly Color FgOff    = Color.FromArgb(120, 120, 128);
    private static readonly Color CheckBg  = Color.FromArgb(58, 58, 66);
    private static readonly Color CheckBrd = Color.FromArgb(106, 169, 255);

    public DarkTrayRenderer() : base(new DarkColorTable()) { RoundedEdges = false; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Bg);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Bg);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, r);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!item.Enabled || !(item.Selected || item.Pressed)) return;
        using var brush = new SolidBrush(Hover);
        e.Graphics.FillRectangle(brush, new Rectangle(2, 1, item.Width - 4, item.Height - 2));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Fg : FgOff;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? FgOff : Fg;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        r.Inflate(-1, -1);
        using (var bg = new SolidBrush(CheckBg)) e.Graphics.FillRectangle(bg, r);
        using (var pen = new Pen(CheckBrd)) e.Graphics.DrawRectangle(pen, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1));

        var old = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var tick = new Pen(Fg, 1.8f))
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            e.Graphics.DrawLines(tick, new[]
            {
                new PointF(cx - 4f, cy),
                new PointF(cx - 1.2f, cy + 3f),
                new PointF(cx + 4f, cy - 3.5f)
            });
        }
        e.Graphics.SmoothingMode = old;
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Border);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public DarkColorTable() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuBorder => Border;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color CheckBackground => CheckBg;
        public override Color CheckSelectedBackground => CheckBg;
        public override Color CheckPressedBackground => CheckBg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Bg;
    }
}
