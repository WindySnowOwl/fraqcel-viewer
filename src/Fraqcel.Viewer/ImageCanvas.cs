// Part of the unofficial Fraqcel host prototype.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Fraqcel.Viewer;

/// <summary>
/// Shows the navigation image and turns mouse input into image pixels. Every position goes
/// through the image's own pixel grid (never a cell grid or a screen grid), so display scaling
/// cannot shift a selection: the cause of the zoom bug in Fraqcel's spreadsheet.
/// </summary>
internal sealed class ImageCanvas : Control
{
    private Bitmap? image;
    private Point? dragStart; // image pixel (x = column, y = row)
    private Point dragEnd;
    private Point? hover;

    public ImageCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(32, 32, 32);
        SetStyle(ControlStyles.Selectable, true);
    }

    /// <summary>A box was dragged: rows and columns of its two corners, counted from 0.</summary>
    public event Action<int, int, int, int>? BoxSelected;

    /// <summary>A pixel was clicked without dragging: row, column, and the button.</summary>
    public event Action<int, int, MouseButtons>? PixelClicked;

    /// <summary>The pixel under the cursor (row, column), or null when it leaves the image.</summary>
    public event Action<Point?>? PixelHovered;

    /// <summary>
    /// One wheel step over the image: row, column, and +1 (away from the user, zoom in) or -1.
    /// Fine-grained wheels add up to a step at a time.
    /// </summary>
    public event Action<int, int, int>? WheelStep;

    private int wheelDelta;

    /// <summary>The navigation image's zoom factor, for drawing a click's target frame.</summary>
    public double ClickZoomFactor { get; set; } = 4;

    /// <summary>Set to false while a render is running: the image no longer matches the view.</summary>
    public bool Interactive { get; set; } = true;

    /// <summary>What the canvas says while it has no image.</summary>
    public string EmptyText
    {
        get => emptyText;
        set
        {
            emptyText = value;
            Invalidate();
        }
    }

    private string emptyText = "Rendering…";

    public Bitmap? Image
    {
        get => image;
        set
        {
            image = value;
            dragStart = null;
            Invalidate();
        }
    }

    /// <summary>Where the image is drawn, and the scale from image pixels to client pixels.</summary>
    private RectangleF DisplayRect(out float scale)
    {
        scale = 1;
        if (image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return RectangleF.Empty;
        float s = Math.Min((float)ClientSize.Width / image.Width, (float)ClientSize.Height / image.Height);
        if (s >= 1) s = (float)Math.Floor(s); // whole-number upscaling keeps pixels square and sharp
        scale = s;
        float w = image.Width * s, h = image.Height * s;
        return new RectangleF((float)Math.Floor((ClientSize.Width - w) / 2), (float)Math.Floor((ClientSize.Height - h) / 2), w, h);
    }

    /// <summary>The image pixel under a client point, clamped to the image when <paramref name="clamp"/> is set.</summary>
    private Point? ToPixel(Point p, bool clamp)
    {
        if (image == null) return null;
        var rect = DisplayRect(out float s);
        double x = Math.Floor((p.X + 0.5 - rect.X) / s);
        double y = Math.Floor((p.Y + 0.5 - rect.Y) / s);
        if (!clamp && (x < 0 || y < 0 || x >= image.Width || y >= image.Height)) return null;
        x = Math.Max(0, Math.Min(image.Width - 1, x));
        y = Math.Max(0, Math.Min(image.Height - 1, y));
        return new Point((int)x, (int)y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        if (image == null)
        {
            TextRenderer.DrawText(g, emptyText, Font, ClientRectangle, Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        var rect = DisplayRect(out float s);
        if (s >= 1)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half; // without it, nearest-neighbor is half a pixel off
        }
        else
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
        g.DrawImage(image, rect);
        g.PixelOffsetMode = PixelOffsetMode.Default;
        g.InterpolationMode = InterpolationMode.Default;

        if (dragStart is Point a && a != dragEnd)
        {
            int c1 = Math.Min(a.X, dragEnd.X), c2 = Math.Max(a.X, dragEnd.X);
            int r1 = Math.Min(a.Y, dragEnd.Y), r2 = Math.Max(a.Y, dragEnd.Y);
            // The selected pixels.
            var sel = new RectangleF(rect.X + c1 * s, rect.Y + r1 * s, (c2 - c1 + 1) * s, (r2 - r1 + 1) * s);
            using (var pen = new Pen(Color.White, 1))
                g.DrawRectangle(pen, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);

            // Where the zoom will land (selectionMove): centered half a pixel past the middle
            // cell, spanning the box's larger side.
            int centerR = r1 + (r2 - r1) / 2, centerC = c1 + (c2 - c1) / 2;
            int side = Math.Max(r2 - r1, c2 - c1);
            float cx = rect.X + (centerC + 1) * s, cy = rect.Y + (centerR + 1) * s;
            var target = new RectangleF(cx - side * s / 2, cy - side * s / 2, side * s, side * s);
            using var dash = new Pen(Color.Yellow, 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dash, target.X, target.Y, target.Width, target.Height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (!Interactive || e.Button != MouseButtons.Left) return;
        if (ToPixel(e.Location, clamp: false) is Point p)
        {
            dragStart = p;
            dragEnd = p;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = ToPixel(e.Location, clamp: false);
        if (h != hover)
        {
            hover = h;
            PixelHovered?.Invoke(h);
        }
        if (dragStart != null && ToPixel(e.Location, clamp: true) is Point p && p != dragEnd)
        {
            dragEnd = p;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && dragStart is Point a)
        {
            Capture = false;
            dragStart = null;
            Invalidate();
            if (!Interactive) return;
            Point b = ToPixel(e.Location, clamp: true) ?? a;
            if (a == b) PixelClicked?.Invoke(a.Y, a.X, MouseButtons.Left);
            else BoxSelected?.Invoke(a.Y, a.X, b.Y, b.X);
            return;
        }
        if (!Interactive) return;
        if ((e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) && ToPixel(e.Location, clamp: false) is Point p)
            PixelClicked?.Invoke(p.Y, p.X, e.Button);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Interactive || dragStart != null || ToPixel(e.Location, clamp: false) is not Point p)
        {
            wheelDelta = 0;
            return;
        }
        // Each step is one notch (120); a wheel that sends smaller deltas fills a notch first.
        wheelDelta += e.Delta;
        int steps = wheelDelta / 120;
        if (steps == 0) return;
        wheelDelta -= steps * 120;
        WheelStep?.Invoke(p.Y, p.X, Math.Sign(steps));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        // With "scroll inactive windows" off, Windows sends the wheel to the focused control, so
        // take focus here. Not from a text box: passing over the image shouldn't interrupt typing.
        if (FindForm()?.ActiveControl is not TextBoxBase) Focus();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != null)
        {
            hover = null;
            PixelHovered?.Invoke(null);
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Escape || base.IsInputKey(keyData);
}
