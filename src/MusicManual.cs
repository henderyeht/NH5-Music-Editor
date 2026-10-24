using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Heat5Music;

sealed class MusicHelpMark : Control
{
    public MusicHelpMark()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(28, 28);
        Cursor = Cursors.Hand;
        BackColor = Theme.Asphalt;
        AccessibleName = "Manual";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height) - 8;
        var rect = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
        using var fill = new SolidBrush(Theme.Yellow);
        e.Graphics.FillEllipse(fill, rect);
        using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var text = new SolidBrush(Color.Black);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString("?", font, text, rect, format);
    }
}

static class MusicManual
{
    public const string Text =
        "Music Editor\r\n\r\n" +
        "This window replaces the menu music in one NASCAR Heat 5 folder.\r\n\r\n" +
        "Locate the game folder. That can be a Steam install, a retail copy, or a copy that already has Next Gen cars. The music files are Music Bank.streams.bank and Music Bank.bank under NASCARHeat5_Data\\StreamingAssets\\PC\\FMOD.\r\n\r\n" +
        "The list shows the songs in that bank. Choose an mp3 or wav for a row if you want to replace it. The play button starts the selected row. If you chose a file, it plays that file. If you did not, it plays the song that is in the game. Pause holds the place. Stop goes back to the start. The rail next to those buttons moves the preview only. Drag the thumb to another part of the song. The game still starts that song at the beginning and plays it through to the end. Choosing another row does not switch the sound until you press play.\r\n\r\n" +
        "Close NASCAR Heat 5 before APPLY or RESTORE.\r\n\r\n" +
        "The first APPLY copies those two bank files as they are on your computer. That copy is your game at that moment. RESTORE writes that copy back. If songs were already replaced with an older version of this editor, APPLY keeps those songs and makes each one play to the end of the file that is in the game now.\r\n\r\n" +
        "APPLY sets each song to play to the end of its file. You can APPLY with no mp3 chosen when a song is being cut off or was already replaced. On the original soundtrack, Right There All Along is left out of the playlist and the next song is skipped with it. APPLY puts that song back. That changes one name in Master Bank.strings.bank, and RESTORE puts that file back too. Paints, tracks, and other mods are left alone.";

    public static void Open(Form owner)
    {
        using var form = new Form
        {
            Text = "Music Editor",
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Asphalt,
            ForeColor = Theme.White,
            Font = new Font("Segoe UI", 11f),
            ClientSize = new Size(640, 520),
            MinimizeBox = false,
            MaximizeBox = false
        };
        if (owner.Icon != null)
        {
            form.Icon = (Icon)owner.Icon.Clone();
        }

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Theme.Panel,
            ForeColor = Theme.White,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 11f),
            Text = Text
        };
        form.Controls.Add(box);
        box.SelectionStart = 0;
        box.SelectionLength = 0;
        form.ShowDialog(owner);
    }
}
