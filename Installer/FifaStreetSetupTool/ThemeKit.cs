using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReStreet.Ui;

// Shared look of the ReStreet launcher and setup: a dark street court, a skewed wordmark, slanted
// buttons like the game's own menus, and one glass panel. The glass is a blurred copy of the
// backdrop under a light veil, painted once, so it works on every Windows version.
//
// Everything is laid out on an 8 px grid in "design" pixels, scaled to the real DPI:
//   outer margin 48 (left column) / 32 (glass panel), glass padding 24,
//   controls 40 high (combo boxes 36), captions 16 high, gaps 8 / 16 / 24 / 32.
// A form that owns a pre-painted backdrop, so rounded controls can show it behind their corners.
internal interface ISceneHost { Bitmap SceneBitmap { get; } }

// A flat panel that paints its own solid background.
internal interface IFlatPanel { }

internal static class StreetTheme
{
    // Paints what is behind a control (the form's scene, or a flat panel's colour), so a rounded
    // control can leave its corners looking transparent.
    public static void PaintBehind(Control control, Graphics g)
    {
        var form = control.FindForm();
        for (Control? parent = control.Parent; parent != null && parent != form; parent = parent.Parent)
        {
            if (parent is IFlatPanel)
            {
                using var flat = new SolidBrush(PanelFlat);
                g.FillRectangle(flat, 0, 0, control.Width, control.Height);
                return;
            }
        }
        if (form is ISceneHost host && control.IsHandleCreated)
        {
            var at = form.PointToClient(control.PointToScreen(Point.Empty));
            g.DrawImage(host.SceneBitmap, new Rectangle(0, 0, control.Width, control.Height),
                new Rectangle(at.X, at.Y, control.Width, control.Height), GraphicsUnit.Pixel);
            return;
        }
        using var fallback = new SolidBrush(control.Parent?.BackColor ?? Asphalt);
        g.FillRectangle(fallback, 0, 0, control.Width, control.Height);
    }

    public static readonly Color Asphalt = Color.FromArgb(17, 19, 21);
    public static readonly Color Yellow = Color.FromArgb(244, 196, 12);
    public static readonly Color YellowDark = Color.FromArgb(22, 19, 10);
    public static readonly Color Ink = Color.FromArgb(242, 242, 238);
    public static readonly Color Muted = Color.FromArgb(176, 178, 178);
    public static readonly Color Field = Color.FromArgb(24, 27, 31);
    public static readonly Color PanelFlat = Color.FromArgb(29, 30, 32);

    public const int DesignWidth = 930;

    // Extra scale applied by a window that shrinks itself to fit a small screen (1.0 = none).
    public static float FitScale = 1f;

    // Pixel size of one design pixel for a control: system DPI (96 = 1.0) times the fit scale.
    public static float K(Control control) => (control.DeviceDpi / 96f) * FitScale;

    static string? condensed;

    public static string CondensedFamily
    {
        get
        {
            if (condensed != null) return condensed;
            using var fonts = new InstalledFontCollection();
            var names = new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in new[] { "Bahnschrift SemiBold Condensed", "Bahnschrift Condensed", "Arial Narrow", "Segoe UI Semibold" })
                if (names.Contains(candidate)) return condensed = candidate;
            return condensed = "Segoe UI";
        }
    }

    public static Font Condensed(float pt, FontStyle style = FontStyle.Regular) => new(CondensedFamily, pt, style);
    public static Font Body(float pt, FontStyle style = FontStyle.Regular) => new("Segoe UI", pt, style);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Parallelogram with the right edge cut, like the game's menu bars.
    public static PointF[] Slant(RectangleF r, float cut) =>
        new[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right - cut, r.Bottom), new PointF(r.X, r.Bottom) };

    public static Bitmap RenderBackdrop(Size size, float designWidth, float designHeight)
    {
        float s = size.Width / designWidth;
        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Asphalt);
        g.ScaleTransform(s, s);
        float w = designWidth, h = designHeight;

        using (var grain = new Bitmap(128, 128, PixelFormat.Format32bppPArgb))
        {
            var rnd = new Random(7);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    grain.SetPixel(x, y, Color.FromArgb(rnd.Next(0, 22), 255, 255, 255));
            using var brush = new TextureBrush(grain, WrapMode.Tile);
            g.FillRectangle(brush, 0, 0, w, h + 400);
        }

        using (var fence = new Pen(Color.FromArgb(15, 255, 255, 255), 1f))
        {
            for (float i = -h; i < w + h; i += 16)
            {
                g.DrawLine(fence, i, 0, i + h, h);
                g.DrawLine(fence, i + h, 0, i, h);
            }
        }

        using (var light = new GraphicsPath())
        {
            light.AddEllipse(w * 0.2f, -h * 1.1f, w * 1.1f, h * 1.9f);
            using var brush = new PathGradientBrush(light)
            {
                CenterColor = Color.FromArgb(54, 255, 244, 214),
                SurroundColors = new[] { Color.FromArgb(0, 255, 244, 214) },
                CenterPoint = new PointF(w * 0.78f, 0)
            };
            g.FillPath(brush, light);
        }

        // Court markings: a centre circle and the halfway line, kept away from the text columns.
        using var line = new Pen(Color.FromArgb(40, 255, 255, 255), 3f);
        g.DrawEllipse(line, w * 0.24f - h * 0.5f, h * 1.1f - h * 0.5f, h, h);
        g.DrawLine(line, -10, h * 0.76f, w + 10, h * 0.76f);
        return bmp;
    }

    // Cheap, smooth blur: shrink a lot, then stretch back.
    public static Bitmap Blur(Bitmap source, int divisor)
    {
        int w = Math.Max(1, source.Width / divisor), h = Math.Max(1, source.Height / divisor);
        using var small = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, w, h));
        }
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(small, new Rectangle(0, 0, source.Width, source.Height));
        }
        return result;
    }

    // Glass sheet in design coordinates; `g` already carries the design->pixel scale.
    public static void DrawGlass(Graphics g, Bitmap blurred, float scale, RectangleF rect, float radius)
    {
        using var path = Round(rect, radius);
        var state = g.Save();
        g.SetClip(path, CombineMode.Intersect);
        var source = new RectangleF(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);
        g.DrawImage(blurred, rect, source, GraphicsUnit.Pixel);
        using (var veil = new SolidBrush(Color.FromArgb(34, 236, 240, 245))) g.FillPath(veil, path);
        using (var sheen = new LinearGradientBrush(new RectangleF(rect.X, rect.Y, rect.Width, 120), Color.FromArgb(44, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            g.FillRectangle(sheen, rect.X, rect.Y, rect.Width, 120);
        g.Restore(state);
        using (var edge = new Pen(Color.FromArgb(58, 255, 255, 255), 1f)) g.DrawPath(edge, path);
        using (var top = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
            g.DrawLine(top, rect.X + radius, rect.Y + 0.5f, rect.Right - radius, rect.Y + 0.5f);
    }

    // Wordmark drawn as a path so it can be sheared like the game's lettering. The ink's left edge
    // and top edge land exactly on `origin`.
    public static void DrawWordmark(Graphics g, string text, PointF origin, float pixelSize, Color color)
    {
        using var family = new FontFamily(CondensedFamily);
        using var path = new GraphicsPath(FillMode.Winding);
        path.AddString(text, family, (int)FontStyle.Bold, pixelSize, PointF.Empty, StringFormat.GenericTypographic);
        using (var shear = new Matrix(1, 0, -0.14f, 1, 0, 0)) path.Transform(shear);
        var bounds = path.GetBounds();
        using (var move = new Matrix()) { move.Translate(origin.X - bounds.X, origin.Y - bounds.Y); path.Transform(move); }
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    // Text for the static scene with no hidden left padding.
    public static void DrawText(Graphics g, string text, Font font, Color color, float x, float y)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic);
    }

    // "Label: value" with an even gap after the colon (GDI+ squeezes the space after "y:").
    public static void DrawLabelValue(Graphics g, string label, string value, Font font, Color labelColor, Color valueColor, float x, float y)
    {
        DrawText(g, label, font, labelColor, x, y);
        float width = g.MeasureString(label, font, PointF.Empty, StringFormat.GenericTypographic).Width;
        DrawText(g, value, font, valueColor, x + width + font.Size * 0.3f, y);
    }

    public static void DrawSpaced(Graphics g, string text, Font font, Brush brush, PointF at, float tracking)
    {
        float x = at.X;
        foreach (char c in text)
        {
            string s = c.ToString();
            g.DrawString(s, font, brush, x, at.Y, StringFormat.GenericTypographic);
            x += g.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic).Width + tracking;
            if (c == ' ') x += font.Size * 0.15f;
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyDarkTitleBar(IntPtr handle)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));
            int caption = Asphalt.R | (Asphalt.G << 8) | (Asphalt.B << 16);
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            int text = Ink.R | (Ink.G << 8) | (Ink.B << 16);
            DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows keeps the default title bar.
        }
    }
}

// Text with no hidden left padding, so its ink starts exactly at the control's left edge.
internal sealed class ThemedLabel : Control
{
    public ThemedLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    public ContentAlignment TextAlign { get; set; } = ContentAlignment.TopLeft;
    public bool AutoEllipsis { get; set; }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnForeColorChanged(EventArgs e) { Invalidate(); base.OnForeColorChanged(e); }
    protected override void OnFontChanged(EventArgs e) { Invalidate(); base.OnFontChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
        if (AutoEllipsis) flags = (flags & ~TextFormatFlags.WordBreak) | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        flags |= TextAlign switch
        {
            ContentAlignment.MiddleLeft => TextFormatFlags.VerticalCenter | TextFormatFlags.Left,
            ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter | TextFormatFlags.Right,
            ContentAlignment.MiddleCenter => TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight => TextFormatFlags.Top | TextFormatFlags.Right,
            _ => TextFormatFlags.Top | TextFormatFlags.Left
        };
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
    }
}

internal enum SlantKind { Primary, Glass, Text }

// Button drawn by hand: a slanted yellow bar, a glass chip, or plain text.
internal sealed class SlantButton : Button
{
    bool hot, down;

    [DefaultValue(SlantKind.Primary)]
    public SlantKind Kind { get; set; } = SlantKind.Primary;

    // Marks a toggle button whose panel is open.
    public bool Active { get; set; }

    // Horizontal text alignment of the Text kind (Near = left edge of the control, Far = right edge).
    public StringAlignment Align { get; set; } = StringAlignment.Center;

    public SlantButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        var fillRect = new RectangleF(0, 0, Width, Height);
        bool enabled = Enabled;
        Color text;
        float k = StreetTheme.K(this);
        float cut = Kind == SlantKind.Primary ? Math.Min(16f * k, Height * 0.2f) : 0;
        switch (Kind)
        {
            case SlantKind.Primary:
            {
                var fill = !enabled ? Color.FromArgb(90, StreetTheme.Yellow) : down ? Color.FromArgb(214, 168, 6) : hot ? Color.FromArgb(255, 212, 48) : StreetTheme.Yellow;
                using var brush = new SolidBrush(fill);
                g.FillPolygon(brush, StreetTheme.Slant(fillRect, cut));
                text = enabled ? StreetTheme.YellowDark : Color.FromArgb(120, StreetTheme.YellowDark);
                break;
            }
            case SlantKind.Glass:
            {
                int alpha = !enabled ? 18 : down ? 70 : Active ? 62 : hot ? 56 : 36;
                using var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                using var path = StreetTheme.Round(rect, 8 * k);
                g.FillPath(brush, path);
                using var pen = new Pen(Active ? StreetTheme.Yellow : Color.FromArgb(enabled ? 70 : 30, 255, 255, 255));
                g.DrawPath(pen, path);
                text = !enabled ? StreetTheme.Muted : Active ? StreetTheme.Yellow : StreetTheme.Ink;
                break;
            }
            default:
                text = !enabled ? Color.FromArgb(110, StreetTheme.Muted) : hot ? Color.White : StreetTheme.Muted;
                break;
        }
        if (Focused && ShowFocusCues && Kind != SlantKind.Primary)
        {
            using var pen = new Pen(Color.FromArgb(160, StreetTheme.Yellow));
            g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
        var format = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        format |= Kind == SlantKind.Text
            ? Align == StringAlignment.Near ? TextFormatFlags.Left : Align == StringAlignment.Far ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter
            : TextFormatFlags.HorizontalCenter;
        var area = new Rectangle(0, Kind == SlantKind.Primary ? 1 : 0, Width - (int)cut, Height);
        TextRenderer.DrawText(g, Text, Font, area, text, format);
        if (Kind == SlantKind.Text && hot && enabled)
        {
            var size = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            using var underline = new Pen(Color.FromArgb(170, 255, 255, 255));
            float left = Align == StringAlignment.Near ? 0 : Align == StringAlignment.Far ? Width - size.Width : (Width - size.Width) / 2f;
            float y = Height / 2f + size.Height / 2f;
            g.DrawLine(underline, left, y, left + size.Width, y);
        }
    }
}

// Progress bar as a skewed track with a yellow fill. Same members as ProgressBar for what the setup uses.
internal sealed class SlantProgress : Control
{
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    int value;
    float phase;
    ProgressBarStyle style = ProgressBarStyle.Continuous;

    public SlantProgress()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
        timer.Tick += (_, _) => { phase = (phase + 0.025f) % 1f; Invalidate(); };
    }

    public ProgressBarStyle Style
    {
        get => style;
        set { style = value; timer.Enabled = value == ProgressBarStyle.Marquee; Invalidate(); }
    }

    public int Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cut = Height * 0.7f;
        var track = new RectangleF(0, 0, Width - 1, Height - 1);
        bool idle = style != ProgressBarStyle.Marquee && value == 0;
        using (var brush = new SolidBrush(Color.FromArgb(idle ? 52 : 110, 0, 0, 0)))
            g.FillPolygon(brush, StreetTheme.Slant(track, cut));
        var state = g.Save();
        using (var clip = new GraphicsPath())
        {
            clip.AddPolygon(StreetTheme.Slant(track, cut));
            g.SetClip(clip, CombineMode.Intersect);
        }
        using (var fill = new SolidBrush(StreetTheme.Yellow))
        {
            if (style == ProgressBarStyle.Marquee)
            {
                float block = Width * 0.28f;
                float x = -block + (Width + block) * phase;
                g.FillRectangle(fill, x, 0, block, Height);
            }
            else if (value > 0)
            {
                g.FillRectangle(fill, 0, 0, (Width - 1) * value / 100f + cut, Height);
            }
        }
        g.Restore(state);
        using var edge = new Pen(Color.FromArgb(idle ? 28 : 50, 255, 255, 255));
        g.DrawPolygon(edge, StreetTheme.Slant(track, cut));
    }
}

// Check box drawn as a switch: glass track, yellow when on.
internal sealed class GlassToggle : CheckBox
{
    public GlassToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        float k = StreetTheme.K(this);
        return new Size((int)(44 * k) + text.Width + 2, Math.Max((int)(28 * k), text.Height + 4));
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = StreetTheme.K(this);
        float trackH = 20 * k, trackW = 36 * k;
        float top = (Height - trackH) / 2f;
        var track = new RectangleF(0.5f, top, trackW, trackH);
        using (var path = StreetTheme.Round(track, trackH / 2))
        {
            using var fill = new SolidBrush(Checked ? (Enabled ? StreetTheme.Yellow : Color.FromArgb(110, StreetTheme.Yellow)) : Color.FromArgb(Enabled ? 58 : 28, 255, 255, 255));
            g.FillPath(fill, path);
            using var edge = new Pen(Color.FromArgb(Checked ? 0 : 70, 255, 255, 255));
            g.DrawPath(edge, path);
        }
        float knob = 14 * k, inset = 3 * k;
        float x = Checked ? track.Right - knob - inset : track.X + inset;
        using (var brush = new SolidBrush(Checked ? StreetTheme.YellowDark : Color.FromArgb(Enabled ? 235 : 120, 255, 255, 255)))
            g.FillEllipse(brush, x, top + inset, knob, knob);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Color.FromArgb(160, StreetTheme.Yellow));
            using var ring = StreetTheme.Round(new RectangleF(-1.5f, top - 1.5f, trackW + 4, trackH + 3), trackH / 2 + 1.5f);
            g.DrawPath(pen, ring);
        }
        int textX = (int)(44 * k);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textX, 0, Math.Max(1, Width - textX), Height), Enabled ? ForeColor : StreetTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}

// Rounded dark frame around a text box, same look as the combo boxes.
internal sealed class FieldFrame : Control
{
    public TextBox Box { get; }

    public FieldFrame(TextBox box)
    {
        Box = box;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = StreetTheme.Field;
        box.BorderStyle = BorderStyle.None;
        box.BackColor = StreetTheme.Field;
        box.ForeColor = StreetTheme.Ink;
        Controls.Add(box);
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
        box.EnabledChanged += (_, _) => Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int height = Box.PreferredHeight;
        int pad = (int)(12 * StreetTheme.K(this));
        Box.SetBounds(pad, (Height - height) / 2, Math.Max(10, Width - 2 * pad), height);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        StreetTheme.PaintBehind(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = StreetTheme.Round(rect, 8 * StreetTheme.K(this));
        using (var fill = new SolidBrush(StreetTheme.Field)) g.FillPath(fill, path);
        using var edge = new Pen(Box.Focused ? Color.FromArgb(200, StreetTheme.Yellow) : Color.FromArgb(60, 255, 255, 255));
        g.DrawPath(edge, path);
    }
}

// Combo box with the same rounded dark look as FieldFrame; items still come from the normal ComboBox API.
internal sealed class GlassCombo : ComboBox
{
    const int WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014, WM_PRINTCLIENT = 0x0318;
    bool hot;

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public int left, top, right, bottom;
        public bool fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

    public GlassCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 28;
        IntegralHeight = false;
        BackColor = StreetTheme.Field;
        ForeColor = StreetTheme.Ink;
        Font = StreetTheme.Body(10);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var background = new SolidBrush(selected ? Color.FromArgb(52, 54, 58) : StreetTheme.Field)) e.Graphics.FillRectangle(background, e.Bounds);
        if (selected)
        {
            using var bar = new SolidBrush(StreetTheme.Yellow);
            e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
        }
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height),
            StreetTheme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
        if (m.Msg == WM_PAINT)
        {
            var ps = new PAINTSTRUCT { rgbReserved = new byte[32] };
            BeginPaint(Handle, out ps);
            try
            {
                using var g = Graphics.FromHdc(ps.hdc);
                using var buffer = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
                using (var b = Graphics.FromImage(buffer)) PaintSelf(b);
                g.DrawImageUnscaled(buffer, 0, 0);
            }
            finally { EndPaint(Handle, ref ps); }
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == WM_PRINTCLIENT)
        {
            // Used by DrawToBitmap and screenshots.
            using var g = Graphics.FromHdc(m.WParam);
            using var buffer = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            using (var b = Graphics.FromImage(buffer)) PaintSelf(b);
            g.DrawImageUnscaled(buffer, 0, 0);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    void PaintSelf(Graphics g)
    {
        StreetTheme.PaintBehind(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = StreetTheme.K(this);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = StreetTheme.Round(rect, 8 * k);
        using (var fill = new SolidBrush(StreetTheme.Field)) g.FillPath(fill, path);
        bool active = Focused || DroppedDown;
        using (var edge = new Pen(active ? Color.FromArgb(200, StreetTheme.Yellow) : hot && Enabled ? Color.FromArgb(110, 255, 255, 255) : Color.FromArgb(60, 255, 255, 255)))
            g.DrawPath(edge, path);
        string text = SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : Text;
        TextRenderer.DrawText(g, text, Font, new Rectangle((int)(12 * k), 0, Width - (int)(46 * k), Height), Enabled ? StreetTheme.Ink : StreetTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        float cx = Width - 20 * k, cy = Height / 2f;
        using var chevron = new Pen(Enabled ? StreetTheme.Muted : Color.FromArgb(90, StreetTheme.Muted), 1.6f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(chevron, new[] { new PointF(cx - 4.5f * k, cy - 2.2f * k), new PointF(cx, cy + 2.3f * k), new PointF(cx + 4.5f * k, cy - 2.2f * k) });
    }
}
