namespace SpaceTranslate;

internal static class BrandTheme
{
    internal static readonly Color Blue = Color.FromArgb(8, 127, 184);
    internal static readonly Color DarkBlue = Color.FromArgb(7, 87, 128);
    internal static readonly Color Background = Color.FromArgb(244, 249, 253);
    internal static readonly Color Ink = Color.FromArgb(27, 55, 75);
    internal static readonly Color Muted = Color.FromArgb(79, 103, 122);
    internal static readonly Color Border = Color.FromArgb(199, 220, 233);
    internal static readonly Color Hover = Color.FromArgb(229, 243, 251);
}

// Native Windows progress bars ignore ForeColor when visual styles are enabled.
// Paint only the visual indicator; the preparation workflow still owns its value.
internal sealed class BrandProgressBar : Control
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 40 };
    private ProgressBarStyle style;
    private int value, offset;
    internal ProgressBarStyle Style
    {
        get => style;
        set { style = value; animation.Enabled = style == ProgressBarStyle.Marquee; Invalidate(); }
    }
    internal int Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, 100); Invalidate(); }
    }
    internal BrandProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false; AccessibleRole = AccessibleRole.ProgressBar; AccessibleName = "模型准备进度";
        animation.Tick += (_, _) => { offset = (offset + 8) % Math.Max(1, Width + Width / 3); Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BrandTheme.Hover);
        using var brush = new SolidBrush(BrandTheme.Blue);
        if (Style == ProgressBarStyle.Marquee)
            e.Graphics.FillRectangle(brush, offset - Width / 3, 0, Width / 3, Height);
        else e.Graphics.FillRectangle(brush, 0, 0, Width * Value / 100, Height);
        base.OnPaint(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) animation.Dispose();
        base.Dispose(disposing);
    }
}
