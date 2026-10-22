using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Heat5Music;

class PressButton : Button
{
    bool _down;

    public PressButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = new Font("Impact", 16, FontStyle.Regular, GraphicsUnit.Pixel);
        ForeColor = Theme.White;
        BackColor = Theme.Panel;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            _down = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _down = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_down)
        {
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _down = false;
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected bool ShowPressed
    {
        get { return _down && Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position)); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var face = PaintFace(g, ShowPressed);
        var text = face;
        if (ShowPressed)
        {
            text.Offset(1, 1);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            text,
            Enabled ? ForeColor : Theme.Mute,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected Rectangle PaintFace(Graphics g, bool down)
    {
        g.Clear(Parent != null ? Parent.BackColor : Theme.Asphalt);
        var face = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var back = !Enabled ? Color.FromArgb(22, 22, 24) : (down ? Theme.Track : BackColor);
        using (var fill = new SolidBrush(back))
        {
            g.FillRectangle(fill, face);
        }

        var light = down ? Color.FromArgb(20, 20, 22) : Color.FromArgb(110, 110, 118);
        var shade = down ? Color.FromArgb(140, 140, 148) : Color.FromArgb(8, 8, 10);
        using (var hi = new Pen(light))
        using (var lo = new Pen(shade))
        {
            if (down)
            {
                g.DrawLine(lo, face.Left, face.Top, face.Right - 1, face.Top);
                g.DrawLine(lo, face.Left, face.Top, face.Left, face.Bottom - 1);
                g.DrawLine(hi, face.Left, face.Bottom - 1, face.Right - 1, face.Bottom - 1);
                g.DrawLine(hi, face.Right - 1, face.Top, face.Right - 1, face.Bottom - 1);
            }
            else
            {
                g.DrawLine(hi, face.Left, face.Top, face.Right - 1, face.Top);
                g.DrawLine(hi, face.Left, face.Top, face.Left, face.Bottom - 1);
                g.DrawLine(lo, face.Left, face.Bottom - 1, face.Right - 1, face.Bottom - 1);
                g.DrawLine(lo, face.Right - 1, face.Top, face.Right - 1, face.Bottom - 1);
            }
        }

        return face;
    }
}

enum TransportGlyph
{
    Play,
    Pause,
    Stop,
    Choose,
    Clear
}

sealed class TransportButton : PressButton
{
    public TransportGlyph Glyph { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var face = PaintFace(g, ShowPressed);
        var color = Enabled ? ForeColor : Theme.Mute;
        var nudge = ShowPressed ? 1 : 0;
        var cx = face.Left + face.Width / 2 + nudge;
        var cy = face.Top + face.Height / 2 + nudge;
        using var brush = new SolidBrush(color);
        if (Glyph == TransportGlyph.Play)
        {
            g.FillPolygon(brush, new[]
            {
                new Point(cx - 7, cy - 9),
                new Point(cx - 7, cy + 9),
                new Point(cx + 9, cy)
            });
            return;
        }

        if (Glyph == TransportGlyph.Pause)
        {
            g.FillRectangle(brush, cx - 8, cy - 8, 5, 16);
            g.FillRectangle(brush, cx + 3, cy - 8, 5, 16);
            return;
        }

        if (Glyph == TransportGlyph.Stop)
        {
            g.FillRectangle(brush, cx - 7, cy - 7, 14, 14);
            return;
        }

        using var pen = new Pen(color, 2.2f);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        if (Glyph == TransportGlyph.Choose)
        {
            g.DrawRectangle(pen, cx - 9, cy - 6, 18, 13);
            g.DrawLine(pen, cx - 9, cy - 2, cx - 3, cy - 2);
            g.DrawLine(pen, cx - 3, cy - 2, cx - 1, cy - 6);
            g.DrawLine(pen, cx - 1, cy - 6, cx + 9, cy - 6);
            return;
        }

        if (Glyph == TransportGlyph.Clear)
        {
            g.DrawLine(pen, cx - 7, cy - 7, cx + 7, cy + 7);
            g.DrawLine(pen, cx + 7, cy - 7, cx - 7, cy + 7);
        }
    }
}
