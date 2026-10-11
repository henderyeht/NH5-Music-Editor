using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using NAudio.Wave;

namespace Heat5Music;

public sealed class MainForm : Form
{
    const int HeaderHeight = 110;
    readonly ListView _songs;
    readonly TextBox _title;
    readonly TextBox _artist;
    readonly Label _status;
    readonly Label _path;
    readonly Label _playLabel;
    WaveOutEvent? _wave;
    WaveStream? _source;
    int _playIndex = -1;
    bool _paused;
    readonly List<string> _files = new List<string>();
    readonly Icon _icon;
    readonly Image? _logo;
    Panel? _header;
    Panel? _locateRow;
    bool _filling;
    bool _hosted;
    string? _root;

    public MainForm()
    {
        Text = "NASCAR Heat 5 Music Editor";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(980, 700);
        MinimumSize = new Size(860, 620);
        BackColor = Theme.Asphalt;
        ForeColor = Theme.White;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9.5f);
        _logo = LoadLogo();
        _icon = MakeIcon();
        Icon = _icon;

        _header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = Theme.Asphalt };
        _header.Paint += (_, e) => DrawHeader(e.Graphics, _header.Width);

        var note = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = Theme.Panel,
            Padding = new Padding(12, 8, 12, 8)
        };
        note.Paint += (_, e) => Theme.PaintLeftAccent(e.Graphics, note.Height);
        note.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.White,
            BackColor = Theme.Panel,
            Padding = new Padding(8, 0, 0, 0),
            Text = "Close NASCAR Heat 5 before making changes.\r\n"
                 + "The first APPLY saves a copy of your current music files. That copy is your game as it is, not a stock original.\r\n"
                 + "Only the two music banks are replaced. Your other mods stay as they are. RESTORE puts those two files back to that copy."
        });

        _locateRow = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Theme.Asphalt };
        var locate = new LinkLabel
        {
            Text = "Locate game…",
            Location = new Point(16, 4),
            AutoSize = true,
            LinkColor = Theme.Yellow,
            ActiveLinkColor = Theme.White,
            VisitedLinkColor = Theme.Yellow,
            BackColor = Theme.Asphalt
        };
        locate.LinkClicked += (_, __) => BrowseGame();
        _path = new Label
        {
            Location = new Point(130, 4),
            Size = new Size(820, 20),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = Theme.Mute,
            BackColor = Theme.Asphalt,
            AutoEllipsis = true
        };
        _locateRow.Controls.Add(locate);
        _locateRow.Controls.Add(_path);

        _songs = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            OwnerDraw = true,
            BackColor = Theme.Track,
            ForeColor = Theme.White,
            BorderStyle = BorderStyle.FixedSingle,
            AllowDrop = true
        };
        _songs.Columns.Add("In-game song", 250);
        _songs.Columns.Add("Your file", 220);
        _songs.Columns.Add("Shown title", 230);
        _songs.Columns.Add("Shown artist", 220);
        _songs.DrawColumnHeader += DrawColumnHeader;
        _songs.DrawItem += (_, __) => { };
        _songs.DrawSubItem += DrawSubItem;
        _songs.SelectedIndexChanged += (_, __) => ShowSelected();
        _songs.DragEnter += OnDragEnter;
        _songs.DragDrop += OnDragDrop;

        var edit = new Panel { Dock = DockStyle.Bottom, Height = 128, BackColor = Theme.Asphalt };
        edit.Controls.Add(new Label
        {
            Text = "Shown title",
            ForeColor = Theme.Yellow,
            BackColor = Theme.Asphalt,
            Location = new Point(16, 10),
            AutoSize = true
        });
        _title = Field(130, 6, 340);
        edit.Controls.Add(new Label
        {
            Text = "Shown artist",
            ForeColor = Theme.Yellow,
            BackColor = Theme.Asphalt,
            Location = new Point(490, 10),
            AutoSize = true
        });
        _artist = Field(600, 6, 340);
        _title.TextChanged += (_, __) => StoreSelected();
        _artist.TextChanged += (_, __) => StoreSelected();
        var choose = MakeButton("CHOOSE MP3", Color.FromArgb(24, 24, 26), Theme.Yellow, new Point(16, 42), new Size(150, 36), 2);
        choose.FlatAppearance.BorderColor = Theme.Yellow;
        var clear = MakeButton("CLEAR", Color.FromArgb(24, 24, 26), Theme.Yellow, new Point(176, 42), new Size(120, 36), 2);
        clear.FlatAppearance.BorderColor = Theme.Yellow;
        var apply = MakeButton("APPLY", Theme.Red, Theme.White, new Point(306, 42), new Size(150, 36), 0);
        var restore = MakeButton("RESTORE", Color.FromArgb(24, 24, 26), Theme.Yellow, new Point(466, 42), new Size(150, 36), 2);
        restore.FlatAppearance.BorderColor = Theme.Yellow;
        choose.Click += (_, __) => ChooseFiles();
        clear.Click += (_, __) => ClearSelected();
        apply.Click += (_, __) => Apply();
        restore.Click += (_, __) => Restore();
        var play = MakeButton("PLAY", Color.FromArgb(24, 24, 26), Theme.Yellow, new Point(16, 84), new Size(110, 36), 2);
        play.FlatAppearance.BorderColor = Theme.Yellow;
        var pause = MakeButton("PAUSE", Color.FromArgb(24, 24, 26), Theme.Yellow, new Point(136, 84), new Size(110, 36), 2);
        pause.FlatAppearance.BorderColor = Theme.Yellow;
        var stop = MakeButton("STOP", Color.FromArgb(24, 24, 26), Theme.Yellow, new Point(256, 84), new Size(110, 36), 2);
        stop.FlatAppearance.BorderColor = Theme.Yellow;
        play.Click += (_, __) => PlaySelected();
        pause.Click += (_, __) => PauseAudio();
        stop.Click += (_, __) => StopAudio();
        _playLabel = new Label
        {
            Location = new Point(380, 90),
            Size = new Size(560, 24),
            ForeColor = Theme.Mute,
            BackColor = Theme.Asphalt,
            AutoEllipsis = true
        };
        edit.Controls.Add(_title);
        edit.Controls.Add(_artist);
        edit.Controls.Add(choose);
        edit.Controls.Add(clear);
        edit.Controls.Add(apply);
        edit.Controls.Add(restore);
        edit.Controls.Add(play);
        edit.Controls.Add(pause);
        edit.Controls.Add(stop);
        edit.Controls.Add(_playLabel);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            ForeColor = Theme.Mute,
            BackColor = Theme.Asphalt,
            Padding = new Padding(16, 8, 12, 0),
            Text = "Locate the NASCAR Heat 5 folder."
        };

        Controls.Add(_songs);
        Controls.Add(edit);
        Controls.Add(_status);
        Controls.Add(note);
        Controls.Add(_locateRow);
        Controls.Add(_header);
        Shown += (_, __) =>
        {
            if (_hosted)
            {
                return;
            }

            var saved = ReadSaved();
            if (!string.IsNullOrWhiteSpace(saved))
            {
                UseFolder(saved);
                return;
            }

            var found = GameLocator.Find();
            if (!string.IsNullOrWhiteSpace(found))
            {
                UseFolder(found);
            }
        };
    }

    public void PlaceIn(Control parent)
    {
        _hosted = true;
        TopLevel = false;
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        MinimumSize = Size.Empty;
        if (_header != null)
        {
            Controls.Remove(_header);
            _header.Dispose();
            _header = null;
        }

        if (_locateRow != null)
        {
            Controls.Remove(_locateRow);
            _locateRow.Dispose();
            _locateRow = null;
        }

        parent.Controls.Add(this);
        Show();
    }

    public void UseFolder(string path)
    {
        var root = GameLocator.Resolve(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            SetStatus(Theme.Error, "That folder is not a NASCAR Heat 5 install.");
            return;
        }

        if (string.Equals(_root, root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _root = root;
        if (!_path.IsDisposed)
        {
            _path.Text = root;
        }

        WriteSaved(root);
        Reload();
    }

    void DrawHeader(Graphics g, int width)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Theme.FillCheckered(g, new Rectangle(0, 0, width, HeaderHeight), 10);
        using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            g.FillRectangle(shade, 0, 0, width, HeaderHeight);
        }

        var logoDest = new Rectangle(14, 14, 248, 81);
        if (_logo != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(_logo, logoDest);
        }

        using (var titleFont = new Font("Segoe UI", 15.5f, FontStyle.Bold))
        using (var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
        {
            var titleRect = _logo != null
                ? new RectangleF(270, 14, Math.Max(40, width - 284), 81)
                : new RectangleF(20, 14, width - 40, 81);
            Theme.DrawOutlinedText(g, "Music Editor", titleFont, titleRect, format);
        }

        Theme.DrawHeaderStripes(g, HeaderHeight, width);
    }

    void BrowseGame()
    {
        using var dlg = new FolderBrowserDialog { Description = "Select the NASCAR Heat 5 folder." };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        UseFolder(dlg.SelectedPath);
    }

    void Reload()
    {
        StopAudio();
        _files.Clear();
        _songs.Items.Clear();
        if (string.IsNullOrWhiteSpace(_root))
        {
            SetStatus(Theme.Mute, "Locate the NASCAR Heat 5 folder.");
            return;
        }

        try
        {
            var songs = MusicBank.ReadSongs(_root);
            while (_files.Count < songs.Count)
            {
                _files.Add("");
            }

            for (var i = 0; i < songs.Count; i++)
            {
                var item = new ListViewItem(songs[i].Title);
                item.SubItems.Add("");
                item.SubItems.Add(songs[i].Title);
                item.SubItems.Add(songs[i].Artist);
                item.Tag = songs[i];
                _songs.Items.Add(item);
            }

            SetStatus(Theme.Mute, songs.Count + " songs. The shown title and artist are what Heat displays.");
        }
        catch (Exception ex)
        {
            SetStatus(Theme.Error, ex.Message);
        }
    }

    void ShowSelected()
    {
        if (_songs.SelectedItems.Count == 0)
        {
            return;
        }

        var item = _songs.SelectedItems[0];
        _filling = true;
        _title.Text = item.SubItems[2].Text;
        _artist.Text = item.SubItems[3].Text;
        _filling = false;
    }

    void StoreSelected()
    {
        if (_filling || _songs.SelectedItems.Count == 0)
        {
            return;
        }

        var item = _songs.SelectedItems[0];
        item.SubItems[2].Text = _title.Text;
        item.SubItems[3].Text = _artist.Text;
    }

    void ChooseFiles()
    {
        if (_songs.SelectedItems.Count == 0 && _songs.Items.Count > 0)
        {
            _songs.Items[0].Selected = true;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "Audio (*.mp3;*.wav)|*.mp3;*.wav|All files|*.*",
            Multiselect = true,
            Title = "Choose music"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        Assign(dialog.FileNames, _songs.SelectedItems.Count == 0 ? 0 : _songs.SelectedItems[0].Index);
    }

    void Assign(string[] paths, int start)
    {
        var row = start;
        foreach (var path in paths)
        {
            if (row >= _songs.Items.Count)
            {
                break;
            }

            while (_files.Count <= row)
            {
                _files.Add("");
            }

            var tags = MusicTags.Read(path);
            _files[row] = path;
            var item = _songs.Items[row];
            item.SubItems[1].Text = Path.GetFileName(path);
            item.SubItems[2].Text = tags.Title;
            item.SubItems[3].Text = tags.Artist;
            row++;
        }

        if (start < _songs.Items.Count)
        {
            _songs.Items[start].Selected = true;
            ShowSelected();
        }
    }

    void ClearSelected()
    {
        if (_songs.SelectedItems.Count == 0)
        {
            return;
        }

        var item = _songs.SelectedItems[0];
        var song = (HeatSong)item.Tag!;
        _files[item.Index] = "";
        item.SubItems[1].Text = "";
        item.SubItems[2].Text = song.Title;
        item.SubItems[3].Text = song.Artist;
        ShowSelected();
    }

    void Apply()
    {
        if (string.IsNullOrWhiteSpace(_root))
        {
            SetStatus(Theme.Error, "Locate the NASCAR Heat 5 folder.");
            return;
        }

        StoreSelected();
        var files = new string?[_songs.Items.Count];
        var titles = new string[_songs.Items.Count];
        var artists = new string[_songs.Items.Count];
        for (var i = 0; i < _songs.Items.Count; i++)
        {
            files[i] = _files[i];
            titles[i] = _songs.Items[i].SubItems[2].Text.Trim();
            artists[i] = _songs.Items[i].SubItems[3].Text.Trim();
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            SetStatus(Theme.Yellow, "Writing music. Leave Heat closed.");
            MusicBank.Apply(_root, files, titles, artists);
            SetStatus(Theme.Yellow, "Saved. Heat will show the title and artist you entered.");
        }
        catch (Exception ex)
        {
            SetStatus(Theme.Error, ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    void Restore()
    {
        if (string.IsNullOrWhiteSpace(_root))
        {
            return;
        }

        try
        {
            MusicBank.Restore(_root);
            Reload();
            SetStatus(Theme.Yellow, "Restored the music copy from before this tool changed it.");
        }
        catch (Exception ex)
        {
            SetStatus(Theme.Error, ex.Message);
        }
    }

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (dropped == null || dropped.Length == 0)
        {
            return;
        }

        var files = new List<string>();
        foreach (var path in dropped)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.GetFiles(path, "*.mp3"));
                files.AddRange(Directory.GetFiles(path, "*.wav"));
            }
            else if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(path);
            }
        }

        var point = _songs.PointToClient(new Point(e.X, e.Y));
        var hit = _songs.HitTest(point);
        var start = hit.Item == null ? 0 : hit.Item.Index;
        Assign(files.ToArray(), start);
    }

    void DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Panel);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var bounds = e.Bounds;
        bounds.X += 6;
        bounds.Width -= 8;
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", _songs.Font, bounds, Theme.Yellow, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var back = e.Item != null && e.Item.Selected ? Theme.Blue : Theme.Track;
        using var brush = new SolidBrush(back);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var bounds = e.Bounds;
        bounds.X += 6;
        bounds.Width -= 8;
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", _songs.Font, bounds, Theme.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void SetStatus(Color color, string text)
    {
        _status.ForeColor = color;
        _status.Text = text;
    }

    static TextBox Field(int x, int y, int width)
    {
        return new TextBox
        {
            Location = new Point(x, y),
            Width = width,
            BackColor = Theme.Track,
            ForeColor = Theme.White,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    static Button MakeButton(string text, Color back, Color fore, Point loc, Size size, int border)
    {
        var button = new Button
        {
            Text = text,
            Location = loc,
            Size = size,
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Impact", 16, FontStyle.Regular, GraphicsUnit.Pixel),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = border;
        return button;
    }

    static Image? LoadLogo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Heat5Music.heat5-logo.png");
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

    static Icon MakeIcon()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.None;
            Theme.FillCheckered(g, new Rectangle(0, 0, 32, 32), 8);
            using (var blue = new SolidBrush(Theme.Blue))
            {
                g.FillRectangle(blue, 0, 0, 4, 32);
            }

            using (var yellow = new Pen(Theme.Yellow, 3))
            {
                g.DrawLine(yellow, 8, 22, 24, 10);
            }
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    static string SavedFile()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NASCAR Heat 5 Music Editor");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "folder.txt");
    }

    static string? ReadSaved()
    {
        var path = SavedFile();
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path).Trim();
        return text.Length == 0 ? null : text;
    }

    static void WriteSaved(string root)
    {
        File.WriteAllText(SavedFile(), root);
    }

    void PlaySelected()
    {
        if (_songs.SelectedItems.Count == 0)
        {
            SetStatus(Theme.Error, "Select a song.");
            return;
        }

        var index = _songs.SelectedItems[0].Index;
        if (_wave != null && _playIndex == index)
        {
            if (_paused)
            {
                _wave.Play();
                _paused = false;
                _playLabel.Text = "Playing " + SongName(index);
            }

            return;
        }

        StopAudio();
        try
        {
            Cursor = Cursors.WaitCursor;
            var file = index < _files.Count ? _files[index] : "";
            WaveStream source;
            if (!string.IsNullOrWhiteSpace(file))
            {
                if (!File.Exists(file))
                {
                    throw new InvalidOperationException("Missing file: " + file);
                }

                source = new AudioFileReader(file);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_root))
                {
                    throw new InvalidOperationException("Locate the NASCAR Heat 5 folder.");
                }

                var song = (HeatSong)_songs.Items[index].Tag!;
                var pcm = MusicBank.PreviewPcm(_root, song.SampleIndex);
                source = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(48000, 16, 2));
            }

            var wave = new WaveOutEvent();
            wave.PlaybackStopped += OnPlaybackStopped;
            wave.Init(source);
            _source = source;
            _wave = wave;
            _playIndex = index;
            _paused = false;
            wave.Play();
            _playLabel.Text = "Playing " + SongName(index);
        }
        catch (Exception ex)
        {
            StopAudio();
            SetStatus(Theme.Error, ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    void PauseAudio()
    {
        if (_wave == null || _wave.PlaybackState != PlaybackState.Playing)
        {
            return;
        }

        _wave.Pause();
        _paused = true;
        _playLabel.Text = "Paused";
    }

    void StopAudio()
    {
        _paused = false;
        _playIndex = -1;
        if (_playLabel != null && !_playLabel.IsDisposed)
        {
            _playLabel.Text = "";
        }

        var wave = _wave;
        var source = _source;
        _wave = null;
        _source = null;
        if (wave != null)
        {
            wave.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                wave.Stop();
            }
            catch (Exception)
            {
            }

            wave.Dispose();
        }

        source?.Dispose();
    }

    void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(_wave, sender))
                {
                    return;
                }

                _paused = false;
                _playIndex = -1;
                _playLabel.Text = "";
                var wave = _wave;
                var source = _source;
                _wave = null;
                _source = null;
                wave?.Dispose();
                source?.Dispose();
            }));
        }
        catch (InvalidOperationException)
        {
        }
    }

    string SongName(int index)
    {
        var item = _songs.Items[index];
        var shown = item.SubItems.Count > 2 ? item.SubItems[2].Text : "";
        return string.IsNullOrWhiteSpace(shown) ? item.Text : shown;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAudio();
            _icon.Dispose();
            _logo?.Dispose();
        }

        base.Dispose(disposing);
    }
}
