using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;

namespace Heat5Music;

static class MusicBanner
{
    static Image? _logo;
    static Image? _header;

    public static int HeaderHeight(int windowHeight)
    {
        if (windowHeight < 8)
        {
            return 64;
        }

        return Math.Max(56, windowHeight / 8);
    }

    public static Icon AppIcon()
    {
        var image = LoadImage("Heat5Music.icon-music.jpg");
        if (image == null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        var sizes = new[] { 16, 32, 48, 256 };
        var pngs = new byte[sizes.Length][];
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = sizes[i];
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            using var ms = new MemoryStream();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(image, new Rectangle(0, 0, size, size));
            bmp.Save(ms, ImageFormat.Png);
            pngs[i] = ms.ToArray();
        }

        image.Dispose();
        using var ico = new MemoryStream();
        using var writer = new BinaryWriter(ico);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);
        var offset = 6 + (16 * sizes.Length);
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = sizes[i];
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs)
        {
            writer.Write(png);
        }

        writer.Flush();
        using var loaded = new Icon(new MemoryStream(ico.ToArray()));
        return (Icon)loaded.Clone();
    }

    public static void Draw(Graphics g, Rectangle dest)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Black);
        if (dest.Width < 1 || dest.Height < 1)
        {
            return;
        }

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        _header ??= LoadImage("Heat5Music.banner-header.jpg");
        var banner = _header;
        if (banner != null && banner.Width > 1 && banner.Height > 1)
        {
            var scale = dest.Height / (float)banner.Height;
            var drawnW = banner.Width * scale;
            if (drawnW <= dest.Width + 0.5f)
            {
                var w = Math.Max(1, (int)Math.Round(drawnW));
                g.DrawImage(banner, new Rectangle(dest.Right - w, dest.Y, w, dest.Height));
            }
            else
            {
                var srcW = dest.Width / scale;
                g.DrawImage(banner, dest, banner.Width - srcW, 0f, srcW, banner.Height, GraphicsUnit.Pixel);
            }
        }

        var titleX = dest.X + 16f;
        var logo = Logo();
        if (logo != null && logo.Width > 0 && logo.Height > 0)
        {
            var lh = dest.Height * 0.62f;
            var lw = lh * logo.Width / logo.Height;
            var maxW = dest.Width * 0.28f;
            if (lw > maxW)
            {
                lw = maxW;
                lh = lw * logo.Height / logo.Width;
            }

            var ly = dest.Y + (dest.Height - lh) / 2f;
            using var attr = new ImageAttributes();
            attr.SetColorKey(Color.FromArgb(0, 0, 0), Color.FromArgb(18, 18, 18));
            g.DrawImage(
                logo,
                new Rectangle((int)(dest.X + 12), (int)ly, Math.Max(1, (int)lw), Math.Max(1, (int)lh)),
                0,
                0,
                logo.Width,
                logo.Height,
                GraphicsUnit.Pixel,
                attr);
            titleX = dest.X + 20f + lw;
        }

        DrawTitle(g, new RectangleF(titleX, dest.Y, Math.Max(48f, dest.Right - titleX - dest.Width * 0.36f), dest.Height));
    }

    static Image? Logo()
    {
        _logo ??= LoadImage("Heat5Music.heat5-logo.png");
        return _logo;
    }

    static void DrawTitle(Graphics g, RectangleF slot)
    {
        if (slot.Width < 8 || slot.Height < 8)
        {
            return;
        }

        var size = Math.Max(12f, slot.Height * 0.34f);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        for (var i = 0; i < 12; i++)
        {
            using var font = new Font("Segoe UI", size, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel);
            var measured = g.MeasureString("Music Editor", font, PointF.Empty, format);
            if (measured.Width <= slot.Width || size <= 11f)
            {
                Theme.DrawOutlinedText(g, "Music Editor", font, slot, format);
                return;
            }

            size -= 1.5f;
        }
    }

    static Image? LoadImage(string resource)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream == null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        using var loaded = Image.FromStream(copy);
        return new Bitmap(loaded);
    }
}
