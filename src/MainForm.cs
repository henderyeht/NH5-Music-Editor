using System.Drawing.Drawing2D;
using NAudio.Wave;

namespace Heat5Music;

public sealed class MainForm : Form
{
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
    Panel? _header;
    Panel? _locateRow;
    bool _filling;
    bool _dirty;
    bool _hosted;
    string? _root;

    public MainForm()
    {
        Text = "NASCAR Heat 5 Music Editor";
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(944, 700);
        MinimumSize = new Size(860, 620);
        BackColor = Theme.Asphalt;
        ForeColor = Theme.White;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9.5f);
        _icon = MusicBanner.AppIcon();
        Icon = _icon;

        _header = new Panel { Dock = DockStyle.Top, Height = MusicBanner.HeaderHeight(700), BackColor = Color.Black };
        _header.Paint += (_, e) => MusicBanner.Draw(e.Graphics, _header.ClientRectangle);
        Resize += (_, __) =>
        {
            if (_header != null)
            {
                _header.Height = MusicBanner.HeaderHeight(ClientSize.Height);
            }
        };

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
            Size = new Size(790, 20),
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
        _title = Field(130, 6, 220);
        edit.Controls.Add(new Label
        {
            Text = "Shown artist",
            ForeColor = Theme.Yellow,
            BackColor = Theme.Asphalt,
            Location = new Point(370, 10),
            AutoSize = true
        });
        _artist = Field(480, 6, 220);
        _title.TextChanged += (_, __) => StoreSelected();
        _artist.TextChanged += (_, __) => StoreSelected();
        var choose = Transport("Choose mp3", TransportGlyph.Choose, new Point(16, 42));
        var clear = Transport("Clear", TransportGlyph.Clear, new Point(76, 42));
        var apply = MakeButton("Apply", Theme.Red, Theme.White, new Point(136, 42), new Size(84, 36), 0);
        var restore = MakeButton("Restore", Color.FromArgb(24, 24, 26), Theme.Yellow, new Point(228, 42), new Size(108, 36), 0);
        choose.Click += (_, __) => ChooseFiles();
        clear.Click += (_, __) => ClearSelected();
        apply.Click += (_, __) => { if (Ask("Are you sure you want to Apply?")) Apply(); };
        restore.Click += (_, __) => { if (Ask("Are you sure you want to Restore?")) Restore(); };
        FormClosing += (_, e) =>
        {
            if (!_dirty)
            {
                return;
            }

            var answer = MessageBox.Show(this, "Do you want to Apply these Changes?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (answer == DialogResult.No)
            {
                return;
            }

            Apply();
            if (_dirty)
            {
                e.Cancel = true;
            }
        };
        var tips = new ToolTip();
        tips.SetToolTip(choose, "Choose mp3");
        tips.SetToolTip(clear, "Clear");
        tips.SetToolTip(apply, "Apply");
        tips.SetToolTip(restore, "Restore");
        var play = Transport("Play", TransportGlyph.Play, new Point(16, 84));
        var pause = Transport("Pause", TransportGlyph.Pause, new Point(80, 84));
        var stop = Transport("Stop", TransportGlyph.Stop, new Point(144, 84));
        play.Click += (_, __) => PlaySelected();
        pause.Click += (_, __) => PauseAudio();
        stop.Click += (_, __) => StopAudio();
        _playLabel = new Label
        {
            Location = new Point(210, 90),
            Size = new Size(700, 24),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
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
            Dock = DockStyle.Fill,
            ForeColor = Theme.Mute,
            BackColor = Theme.Asphalt,
            Padding = new Padding(16, 8, 12, 0),
            Text = "Locate the NASCAR Heat 5 folder."
        };
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = Theme.Asphalt };
        var help = new MusicHelpMark();
        help.Click += (_, __) => MusicManual.Open(this);
        foot.Controls.Add(_status);
        foot.Controls.Add(help);

        Controls.Add(_songs);
        Controls.Add(edit);
        Controls.Add(foot);
        Controls.Add(_locateRow);
        Controls.Add(_header);
        Shown += (_, __) =>
        {
            var area = Program.ScreenArea ?? Screen.FromHandle(Handle).WorkingArea;
            var clientW = Math.Min(ClientSize.Width, Math.Max(640, area.Width - 24));
            var clientH = Math.Min(ClientSize.Height, Math.Max(480, area.Height - 48));
            ClientSize = new Size(clientW, clientH);
            if (_header != null)
            {
                _header.Height = MusicBanner.HeaderHeight(ClientSize.Height);
            }

            Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
            Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
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

    bool Ask(string text)
    {
        return MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
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
        _dirty = true;
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
        _dirty = true;
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
            _dirty = false;
            SetStatus(Theme.Yellow, "Saved.");
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
            _dirty = false;
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
        return new PressButton
        {
            Text = text,
            Location = loc,
            Size = size,
            BackColor = back,
            ForeColor = fore
        };
    }

    static TransportButton Transport(string name, TransportGlyph glyph, Point loc)
    {
        return new TransportButton
        {
            Text = "",
            AccessibleName = name,
            Glyph = glyph,
            Location = loc,
            Size = new Size(52, 36),
            BackColor = Color.FromArgb(24, 24, 26),
            ForeColor = Theme.Yellow
        };
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
        }

        base.Dispose(disposing);
    }
}
