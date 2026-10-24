using System.Drawing.Drawing2D;

namespace Heat5Music;

sealed class SeekRail : Control
{
    double _fraction;
    bool _drag;

    public SeekRail()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Asphalt;
        Cursor = Cursors.Hand;
        AccessibleName = "Song position";
        TabStop = false;
    }

    public bool Dragging => _drag;

    public double Fraction
    {
        get => _fraction;
        set
        {
            var next = value < 0 ? 0 : value > 1 ? 1 : value;
            if (Math.Abs(next - _fraction) < 0.0001)
            {
                return;
            }

            _fraction = next;
            Invalidate();
        }
    }

    public event EventHandler? PositionPicked;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _drag = true;
        Capture = true;
        MoveThumb(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_drag)
        {
            return;
        }

        MoveThumb(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_drag || e.Button != MouseButtons.Left)
        {
            return;
        }

        _drag = false;
        Capture = false;
        MoveThumb(e.X);
        PositionPicked?.Invoke(this, EventArgs.Empty);
    }

    void MoveThumb(int x)
    {
        var track = Track();
        if (track.Width <= 1)
        {
            Fraction = 0;
            return;
        }

        Fraction = (x - track.Left) / (double)track.Width;
    }

    Rectangle Track()
    {
        var y = (Height - 8) / 2;
        return new Rectangle(8, y, Math.Max(1, Width - 16), 8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = Track();
        using (var rest = new SolidBrush(Theme.Track))
        {
            g.FillRectangle(rest, track);
        }

        var played = (int)Math.Round(track.Width * _fraction);
        if (played > 0)
        {
            using var done = new SolidBrush(Theme.Yellow);
            g.FillRectangle(done, track.X, track.Y, Math.Min(played, track.Width), track.Height);
        }

        var thumbX = track.X + played - 7;
        if (thumbX < track.X - 7)
        {
            thumbX = track.X - 7;
        }

        if (thumbX > track.Right - 8)
        {
            thumbX = track.Right - 8;
        }

        var thumb = new Rectangle(thumbX, (Height - 22) / 2, 14, 22);
        using var face = new SolidBrush(Theme.Yellow);
        using var edge = new Pen(Color.Black, 1.4f);
        g.FillRectangle(face, thumb);
        g.DrawRectangle(edge, thumb);
    }
}
