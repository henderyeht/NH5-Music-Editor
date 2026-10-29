using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Heat5Music
{
    internal sealed class HeatSong
    {
        public int Index;
        public int SampleIndex;
        public string Title = "";
        public string Artist = "";
        public int Samples;
        public bool InPlaylist = true;
        public bool Custom;
        public bool RestoreOriginal;
    }

    internal static partial class MusicBank
    {
        private const int HeaderBytes = 60;
        private const int MaxSeconds = 15 * 60;

        static string BackupOf(string path)
        {
            return GameBackup.Keep(path, ".nh5music.bak");
        }

        public static string StreamsPath(string root)
        {
            return Path.Combine(root, "NASCARHeat5_Data", "StreamingAssets", "PC", "FMOD", "Music Bank.streams.bank");
        }

        public static IReadOnlyList<HeatSong> ReadSongs(string root)
        {
            var streams = StreamsPath(root);
            if (!File.Exists(streams))
            {
                throw new InvalidOperationException("Music bank was not found in that Heat 5 folder.");
            }

            var metaPath = MetaPath(root);
            List<(string Title, string Artist)> titles;
            try
            {
                titles = MusicMeta.Read(metaPath);
            }
            catch (Exception)
            {
                titles = new List<(string Title, string Artist)>();
            }

            ParsedBank? parsed = null;
            try
            {
                parsed = Parse(File.ReadAllBytes(streams));
            }
            catch (InvalidOperationException)
            {
            }

            int[]? map = null;
            try
            {
                map = MusicMeta.SampleMap(metaPath, streams);
            }
            catch (Exception)
            {
            }

            var sampleCount = parsed == null ? 0 : parsed.Samples.Count;
            var custom = new bool[sampleCount];
            try
            {
                var flags = ChangedSamples(streams, BackupOf(streams));
                if (flags.Length == custom.Length)
                {
                    custom = flags;
                }
            }
            catch (Exception)
            {
            }

            var songs = new List<HeatSong>();
            if (titles.Count > 0)
            {
                var paired = Pair(titles.Count, sampleCount, map);
                for (var i = 0; i < titles.Count; i++)
                {
                    var sample = paired[i];
                    var title = titles[i].Title;
                    songs.Add(new HeatSong
                    {
                        Index = songs.Count,
                        SampleIndex = sample,
                        Title = string.IsNullOrWhiteSpace(title) ? "Song " + (songs.Count + 1) : title,
                        Artist = titles[i].Artist,
                        Samples = sample >= 0 && parsed != null ? parsed.Samples[sample].Samples : 0,
                        InPlaylist = true,
                        Custom = sample >= 0 && sample < custom.Length && custom[sample]
                    });
                }
            }
            else if (parsed != null)
            {
                for (var i = 0; i < parsed.Samples.Count; i++)
                {
                    var name = parsed.Samples[i].Name;
                    songs.Add(new HeatSong
                    {
                        Index = i,
                        SampleIndex = i,
                        Title = string.IsNullOrWhiteSpace(name) ? "Song " + (i + 1) : name,
                        Artist = "",
                        Samples = parsed.Samples[i].Samples,
                        InPlaylist = true,
                        Custom = i < custom.Length && custom[i]
                    });
                }
            }

            if (songs.Count == 0)
            {
                throw new InvalidOperationException("Music bank was not recognized.");
            }

            return songs;
        }

        public static IReadOnlyList<(string Title, string Artist)>? ReadOriginal(string root)
        {
            var metaBak = BackupOf(MetaPath(root));
            if (!File.Exists(metaBak))
            {
                return null;
            }

            try
            {
                return MusicMeta.Read(metaBak);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] PreviewOriginal(string root, int eventIndex)
        {
            var streamsBak = BackupOf(StreamsPath(root));
            var metaBak = BackupOf(MetaPath(root));
            if (!File.Exists(streamsBak) || !File.Exists(metaBak))
            {
                throw new InvalidOperationException("There is no music backup to restore.");
            }

            var map = MusicMeta.SampleMap(metaBak, streamsBak);
            if (eventIndex < 0 || eventIndex >= map.Length || map[eventIndex] < 0)
            {
                throw new InvalidOperationException("That song is not in the restore file.");
            }

            return PreviewPcm(root, map[eventIndex], streamsBak);
        }

        public static void Apply(string root, IReadOnlyList<string?> files, IReadOnlyList<string> titles, IReadOnlyList<string> artists, IReadOnlyList<int>? sampleIndexes, IReadOnlyList<bool>? inPlaylist, Action<string>? progress = null, IReadOnlyList<bool>? restoreOriginal = null, IReadOnlyList<int>? removeEvents = null)
        {
            progress?.Invoke("Reading the music bank");
            EnsureClosed(root);
            if (removeEvents != null)
            {
                var pending = new List<int>();
                foreach (var index in removeEvents)
                {
                    if (index >= 0 && !pending.Contains(index))
                    {
                        pending.Add(index);
                    }
                }

                pending.Sort();
                for (var i = pending.Count - 1; i >= 0; i--)
                {
                    progress?.Invoke("Removing a song");
                    RemovePlaylistEvent(root, pending[i]);
                }
            }
            var streams = StreamsPath(root);
            if (!File.Exists(streams))
            {
                throw new InvalidOperationException("Music bank was not found in that Heat 5 folder.");
            }

            var any = false;
            for (var i = 0; i < files.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(files[i]) || (restoreOriginal != null && i < restoreOriginal.Count && restoreOriginal[i]))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                SaveShownNames(root, titles, artists, progress);
                return;
            }

            var metaPath = MetaPath(root);
            var bak = BackupOf(streams);
            var metaBak = BackupOf(metaPath);
            // First APPLY copies the user's current music banks. Do not replace that copy.
            // A later APPLY keeps audio that is already different from the copy.
            if (!File.Exists(bak))
            {
                File.Copy(streams, bak, false);
            }

            if (!File.Exists(metaBak))
            {
                File.Copy(metaPath, metaBak, false);
            }

            // A bank the old editor already changed stays as it is. The saved copy is only the starting point when the game still matches it.
            var edited = File.Exists(bak) && !SameFile(streams, bak);
            var bank = File.ReadAllBytes(edited ? streams : bak);
            var metaSource = edited ? metaPath : metaBak;
            var streamSource = edited ? streams : bak;
            var parsed = Parse(bank);
            if (parsed.Mode != 16)
            {
                if (AnyRestore(restoreOriginal))
                {
                    throw new InvalidOperationException("That music bank has to be saved once before a song can be restored.");
                }

                ApplyVorbis(root, streams, metaPath, metaSource, streamSource, bank, parsed, files, titles, artists, sampleIndexes, inPlaylist, progress);
                return;
            }
            int[]? map = null;
            try
            {
                map = MusicMeta.SampleMap(metaSource, streamSource);
            }
            catch (Exception)
            {
            }

            var paired = PairRows(files.Count, parsed.Samples.Count, map, sampleIndexes);
            if (parsed.Samples.Count == 0)
            {
                throw new InvalidOperationException("Music bank was not recognized.");
            }

            var pieces = new byte[parsed.Samples.Count][];
            var counts = new int[parsed.Samples.Count];
            var newTitles = new string?[files.Count];
            var newArtists = new string?[files.Count];
            for (var i = 0; i < parsed.Samples.Count; i++)
            {
                var sample = parsed.Samples[i];
                pieces[i] = new byte[sample.Length];
                Buffer.BlockCopy(bank, sample.DataAt, pieces[i], 0, sample.Length);
                counts[i] = sample.Samples;
            }

            for (var i = 0; i < files.Count; i++)
            {
                if (restoreOriginal != null && i < restoreOriginal.Count && restoreOriginal[i])
                {
                    var slot = paired[i];
                    if (slot < 0)
                    {
                        continue;
                    }

                    var restored = i < titles.Count && !string.IsNullOrWhiteSpace(titles[i]) ? titles[i].Trim() : "song " + (i + 1);
                    progress?.Invoke("Restoring " + restored);
                    var original = OriginalPiece(root, i);
                    counts[slot] = original.Frames;
                    pieces[slot] = original.Piece;
                    continue;
                }

                var mp3 = files[i];
                if (string.IsNullOrWhiteSpace(mp3))
                {
                    continue;
                }

                if (!File.Exists(mp3))
                {
                    throw new InvalidOperationException("Missing file: " + mp3);
                }

                var shown = i < titles.Count && !string.IsNullOrWhiteSpace(titles[i]) ? titles[i].Trim() : "song " + (i + 1);
                progress?.Invoke("Encoding " + shown);
                var pcm = ReadPcm(mp3);
                var sample = paired[i];
                if (sample < 0)
                {
                    continue;
                }

                counts[sample] = pcm.Length / 2;
                pieces[sample] = FadpcmCodec.EncodeStereo(pcm);
            }

            for (var i = 0; i < files.Count; i++)
            {
                if (i < titles.Count)
                {
                    newTitles[i] = titles[i];
                }

                if (i < artists.Count)
                {
                    newArtists[i] = artists[i];
                }
            }

            var rebuilt = Rebuild(bank, parsed, pieces, counts);
            Parse(rebuilt);
            var meta = MusicMeta.Rewrite(File.ReadAllBytes(metaSource), newTitles, newArtists);
            var exact = edited ? ChangedSamples(streams, bak) : new bool[parsed.Samples.Count];
            for (var i = 0; i < files.Count; i++)
            {
                if (paired[i] < 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(files[i]) || (restoreOriginal != null && i < restoreOriginal.Count && restoreOriginal[i]))
                {
                    exact[paired[i]] = true;
                }
            }

            progress?.Invoke("Saving the music bank");
            MusicMeta.FitLengths(meta, streamSource, counts, exact);
            Save(rebuilt, streams);
            Save(meta, metaPath);
            AddChosenSongs(root, files, titles, artists, paired, inPlaylist, progress);
            FixWaveIndexes(streams);
            ReleaseSkippedSong(root);
        }

        private static int[] PairRows(int rows, int sampleCount, int[]? map, IReadOnlyList<int>? sampleIndexes)
        {
            if (sampleIndexes != null && sampleIndexes.Count == rows)
            {
                var chosen = new int[rows];
                for (var i = 0; i < rows; i++)
                {
                    chosen[i] = sampleIndexes[i];
                }

                return chosen;
            }

            return Pair(rows, sampleCount, map);
        }

        private static void ApplyVorbis(string root, string streams, string metaPath, string metaBak, string streamSource, byte[] bank, ParsedBank parsed, IReadOnlyList<string?> files, IReadOnlyList<string> titles, IReadOnlyList<string> artists, IReadOnlyList<int>? sampleIndexes, IReadOnlyList<bool>? inPlaylist, Action<string>? progress)
        {
            var count = parsed.Samples.Count;
            int[]? map = null;
            try
            {
                map = MusicMeta.SampleMap(metaBak, streamSource);
            }
            catch (Exception)
            {
            }

            var paired = PairRows(files.Count, count, map, sampleIndexes);
            var fileForSample = new string?[count];
            var titleForSample = new string?[count];
            for (var i = 0; i < files.Count; i++)
            {
                var sample = paired[i];
                if (sample < 0 || sample >= count || string.IsNullOrWhiteSpace(files[i]))
                {
                    continue;
                }

                if (!File.Exists(files[i]!))
                {
                    throw new InvalidOperationException("Missing file: " + files[i]);
                }

                fileForSample[sample] = files[i];
                if (i < titles.Count)
                {
                    titleForSample[sample] = titles[i];
                }
            }

            var pieces = new byte[count][];
            var counts = new int[count];
            var names = new string[count];
            var dataSize = BitConverter.ToInt32(bank, parsed.DataSizeAt);
            var fsbLength = HeaderBytes + parsed.SampleHeaderSize + parsed.NameTableSize + dataSize;
            var keep = new List<int>();
            var keepRates = new List<int>();
            for (var i = 0; i < count; i++)
            {
                var mp3 = fileForSample[i];
                if (string.IsNullOrWhiteSpace(mp3))
                {
                    keep.Add(i);
                    keepRates.Add(SourceRate(parsed.Samples[i].Raw));
                }
            }

            progress?.Invoke("Reading the current songs");
            var kept = keep.Count == 0
                ? Array.Empty<short[]>()
                : FmodPcm.ReadAll(root, bank, parsed.FsbAt, fsbLength, keep.ToArray(), keepRates.ToArray());
            var keptAt = 0;
            for (var i = 0; i < count; i++)
            {
                var mp3 = fileForSample[i];
                if (string.IsNullOrWhiteSpace(mp3))
                {
                    progress?.Invoke("Converting song " + (i + 1) + " of " + count);
                }
                else
                {
                    var shown = i < titles.Count && !string.IsNullOrWhiteSpace(titles[i]) ? titles[i].Trim() : "song " + (i + 1);
                    progress?.Invoke("Encoding " + shown);
                }

                var pcm = string.IsNullOrWhiteSpace(mp3) ? kept[keptAt++] : ReadPcm(mp3!);
                counts[i] = pcm.Length / 2;
                pieces[i] = FadpcmCodec.EncodeStereo(pcm);
                var edited = titleForSample[i] ?? "";
                names[i] = string.IsNullOrWhiteSpace(edited) ? parsed.Samples[i].Name : edited;
            }

            var events = MusicMeta.Read(metaBak);
            var newTitles = new string?[Math.Max(files.Count, events.Count)];
            var newArtists = new string?[newTitles.Length];
            for (var i = 0; i < events.Count && i < titles.Count; i++)
            {
                newTitles[i] = titles[i];
                if (i < artists.Count)
                {
                    newArtists[i] = artists[i];
                }
            }

            var fsb = BuildFadpcm(bank, parsed, pieces, counts, names);
            var rebuilt = SpliceFsb(bank, parsed, fsb);
            Parse(rebuilt);
            var meta = MusicMeta.Rewrite(File.ReadAllBytes(metaBak), newTitles, newArtists);
            var exact = new bool[counts.Length];
            for (var i = 0; i < exact.Length; i++)
            {
                exact[i] = true;
            }

            progress?.Invoke("Saving the music bank");
            MusicMeta.FitLengths(meta, streamSource, counts, exact);
            Save(rebuilt, streams);
            Save(meta, metaPath);
            AddChosenSongs(root, files, titles, artists, paired, inPlaylist, progress);
            FixWaveIndexes(streams);
            ReleaseSkippedSong(root);
        }

        private static int SourceRate(ulong raw)
        {
            var index = (int)((raw >> 1) & 15);
            int[] rates = { 0, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000, 192000, 0, 0 };
            var rate = rates[index];
            return rate == 0 ? 48000 : rate;
        }

        private static byte[] BuildFadpcm(byte[] bank, ParsedBank parsed, byte[][] pieces, int[] counts, string[] names)
        {
            var count = pieces.Length;
            var strings = new List<byte>();
            var offsets = new int[count];
            for (var i = 0; i < count; i++)
            {
                offsets[i] = count * 4 + strings.Count;
                var text = Encoding.UTF8.GetBytes(names[i] ?? "");
                strings.AddRange(text);
                strings.Add(0);
            }

            var nameBytes = new byte[count * 4 + strings.Count];
            for (var i = 0; i < count; i++)
            {
                BitConverter.GetBytes(offsets[i]).CopyTo(nameBytes, i * 4);
            }

            strings.CopyTo(nameBytes, count * 4);
            var headerSize = count * 8;
            var tail = (HeaderBytes + headerSize + nameBytes.Length) & 15;
            if (tail != 0)
            {
                headerSize += 16 - tail;
            }

            var dataSize = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                dataSize += pieces[i].Length;
            }

            var fsb = new byte[HeaderBytes + headerSize + nameBytes.Length + dataSize];
            Buffer.BlockCopy(bank, parsed.FsbAt, fsb, 0, HeaderBytes);
            Encoding.ASCII.GetBytes("FSB5").CopyTo(fsb, 0);
            BitConverter.GetBytes(1).CopyTo(fsb, 4);
            BitConverter.GetBytes(count).CopyTo(fsb, 8);
            BitConverter.GetBytes(headerSize).CopyTo(fsb, 12);
            BitConverter.GetBytes(nameBytes.Length).CopyTo(fsb, 16);
            BitConverter.GetBytes(dataSize).CopyTo(fsb, 20);
            BitConverter.GetBytes(16).CopyTo(fsb, 24);
            nameBytes.CopyTo(fsb, HeaderBytes + headerSize);
            var cursor = 0;
            for (var i = 0; i < count; i++)
            {
                Buffer.BlockCopy(pieces[i], 0, fsb, HeaderBytes + headerSize + nameBytes.Length + cursor, pieces[i].Length);
                ulong raw = 0;
                raw |= 9UL << 1;
                raw |= 1UL << 5;
                raw |= ((ulong)counts[i] & 0x3FFFFFFFUL) << 34;
                raw |= ((ulong)(cursor >> 5) & 0x07FFFFFFUL) << 7;
                BitConverter.GetBytes(raw).CopyTo(fsb, HeaderBytes + i * 8);
                cursor += pieces[i].Length;
            }

            return fsb;
        }

        private static byte[] SpliceFsb(byte[] bank, ParsedBank parsed, byte[] fsb)
        {
            var oldData = BitConverter.ToInt32(bank, parsed.DataSizeAt);
            var oldLength = HeaderBytes + parsed.SampleHeaderSize + parsed.NameTableSize + oldData;
            var tail = bank.Length - (parsed.FsbAt + oldLength);
            if (tail < 0)
            {
                tail = 0;
                oldLength = bank.Length - parsed.FsbAt;
            }

            var output = new byte[parsed.FsbAt + fsb.Length + tail];
            Buffer.BlockCopy(bank, 0, output, 0, parsed.FsbAt);
            Buffer.BlockCopy(fsb, 0, output, parsed.FsbAt, fsb.Length);
            if (tail > 0)
            {
                Buffer.BlockCopy(bank, parsed.FsbAt + oldLength, output, parsed.FsbAt + fsb.Length, tail);
            }

            BitConverter.GetBytes(output.Length - 8).CopyTo(output, 4);
            if (parsed.FsbLengthAt >= 0)
            {
                BitConverter.GetBytes(fsb.Length).CopyTo(output, parsed.FsbLengthAt);
            }

            if (parsed.SndSizeAt >= 0)
            {
                var sndSize = (parsed.FsbAt - parsed.SndPayload) + fsb.Length;
                BitConverter.GetBytes(sndSize).CopyTo(output, parsed.SndSizeAt);
            }

            return output;
        }

        public static void Restore(string root)
        {
            EnsureClosed(root);
            var streams = StreamsPath(root);
            var metaPath = MetaPath(root);
            var bak = BackupOf(streams);
            var metaBak = BackupOf(metaPath);
            if (!File.Exists(bak) || !File.Exists(metaBak))
            {
                throw new InvalidOperationException("There is no music backup to restore.");
            }

            Save(File.ReadAllBytes(bak), streams);
            Save(File.ReadAllBytes(metaBak), metaPath);
            var strings = StringsPath(root);
            var stringsBak = BackupOf(strings);
            if (File.Exists(stringsBak))
            {
                Save(File.ReadAllBytes(stringsBak), strings);
            }
        }

        static void SaveShownNames(string root, IReadOnlyList<string> titles, IReadOnlyList<string> artists, Action<string>? progress)
        {
            progress?.Invoke("Saving titles");
            var metaPath = MetaPath(root);
            if (!File.Exists(metaPath))
            {
                throw new InvalidOperationException("Music bank was not found in that Heat 5 folder.");
            }

            var streams = StreamsPath(root);
            var bak = BackupOf(streams);
            var metaBak = BackupOf(metaPath);
            if (!File.Exists(bak) && File.Exists(streams))
            {
                File.Copy(streams, bak, false);
            }

            if (!File.Exists(metaBak))
            {
                File.Copy(metaPath, metaBak, false);
            }

            var slots = new string?[titles.Count];
            var artistSlots = new string?[titles.Count];
            for (var i = 0; i < titles.Count; i++)
            {
                slots[i] = titles[i] ?? "";
                artistSlots[i] = i < artists.Count ? artists[i] ?? "" : "";
            }

            var meta = MusicMeta.Rewrite(File.ReadAllBytes(metaPath), slots, artistSlots);
            SeparateSharedBuses(ref meta);
            Save(meta, metaPath);
            if (File.Exists(streams))
            {
                FixWaveIndexes(streams);
            }
        }

        static bool Repair(string root)
        {
            EnsureClosed(root);
            var streams = StreamsPath(root);
            var metaPath = MetaPath(root);
            var bak = BackupOf(streams);
            var metaBak = BackupOf(metaPath);
            var parsed = Parse(File.ReadAllBytes(streams));
            var counts = new int[parsed.Samples.Count];
            for (var i = 0; i < counts.Length; i++)
            {
                counts[i] = parsed.Samples[i].Samples;
            }

            var fitted = File.ReadAllBytes(metaPath);
            var changed = File.Exists(bak) ? ChangedSamples(streams, bak) : null;
            var lengths = MusicMeta.FitLengths(fitted, streams, counts, changed);
            var skipped = HasSkippedSong(root);
            if (!lengths && !skipped)
            {
                return false;
            }

            if (!File.Exists(bak))
            {
                File.Copy(streams, bak, false);
            }

            if (!File.Exists(metaBak))
            {
                File.Copy(metaPath, metaBak, false);
            }

            if (lengths)
            {
                Save(fitted, metaPath);
            }

            ReleaseSkippedSong(root);
            return true;
        }

        static bool SameFile(string left, string right)
        {
            FileInfo a;
            FileInfo b;
            try
            {
                a = new FileInfo(left);
                b = new FileInfo(right);
            }
            catch (IOException)
            {
                return false;
            }

            if (!a.Exists || !b.Exists || a.Length != b.Length)
            {
                return false;
            }

            using var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.Read);
            var leftBuf = new byte[1024 * 1024];
            var rightBuf = new byte[1024 * 1024];
            while (true)
            {
                var got = leftStream.Read(leftBuf, 0, leftBuf.Length);
                var filled = 0;
                while (filled < got)
                {
                    var read = rightStream.Read(rightBuf, filled, got - filled);
                    if (read <= 0)
                    {
                        return false;
                    }

                    filled += read;
                }

                if (got == 0)
                {
                    return true;
                }

                for (var i = 0; i < got; i++)
                {
                    if (leftBuf[i] != rightBuf[i])
                    {
                        return false;
                    }
                }
            }
        }

        static bool[] ChangedSamples(string livePath, string savedPath)
        {
            var liveBytes = File.ReadAllBytes(livePath);
            var live = Parse(liveBytes);
            var flags = new bool[live.Samples.Count];
            if (!File.Exists(savedPath))
            {
                return flags;
            }

            try
            {
                var savedBytes = File.ReadAllBytes(savedPath);
                var saved = Parse(savedBytes);
                if (saved.Samples.Count != live.Samples.Count)
                {
                    for (var i = 0; i < flags.Length; i++)
                    {
                        flags[i] = true;
                    }

                    return flags;
                }

                for (var i = 0; i < live.Samples.Count; i++)
                {
                    var current = live.Samples[i];
                    var savedSample = saved.Samples[i];
                    if (current.Samples != savedSample.Samples
                        || current.Length != savedSample.Length
                        || !RangeEqual(liveBytes, current.DataAt, savedBytes, savedSample.DataAt, current.Length))
                    {
                        flags[i] = true;
                    }
                }
            }
            catch (Exception)
            {
                for (var i = 0; i < flags.Length; i++)
                {
                    flags[i] = true;
                }
            }

            return flags;
        }

        static bool RangeEqual(byte[] left, int leftAt, byte[] right, int rightAt, int length)
        {
            if (leftAt < 0 || rightAt < 0 || length < 0 || leftAt + (long)length > left.Length || rightAt + (long)length > right.Length)
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                if (left[leftAt + i] != right[rightAt + i])
                {
                    return false;
                }
            }

            return true;
        }

        static string StringsPath(string root)
        {
            return Path.Combine(root, "NASCARHeat5_Data", "StreamingAssets", "PC", "FMOD", "Master Bank.strings.bank");
        }

        static bool HasSkippedSong(string root)
        {
            var path = StringsPath(root);
            if (!File.Exists(path))
            {
                return false;
            }

            return IndexOf(File.ReadAllBytes(path), Encoding.ASCII.GetBytes("RightThereAllAlong")) >= 0;
        }

        static void ReleaseSkippedSong(string root)
        {
            var path = StringsPath(root);
            if (!File.Exists(path))
            {
                return;
            }

            var data = File.ReadAllBytes(path);
            var oldName = Encoding.ASCII.GetBytes("RightThereAllAlong");
            var newName = Encoding.ASCII.GetBytes("TrackThereAllAlong");
            if (oldName.Length != newName.Length)
            {
                throw new InvalidOperationException("The playlist name could not be kept.");
            }
            var hit = false;
            for (var i = 0; i <= data.Length - oldName.Length; i++)
            {
                var match = true;
                for (var j = 0; j < oldName.Length; j++)
                {
                    if (data[i + j] != oldName[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (!match)
                {
                    continue;
                }

                Buffer.BlockCopy(newName, 0, data, i, newName.Length);
                hit = true;
                i += oldName.Length - 1;
            }

            if (!hit)
            {
                return;
            }

            var bak = BackupOf(path);
            if (!File.Exists(bak))
            {
                File.Copy(path, bak, false);
            }

            Save(data, path);
        }

        public static bool HasBackup(string root)
        {
            return File.Exists(BackupOf(StreamsPath(root))) && File.Exists(BackupOf(MetaPath(root)));
        }

        private static int[] Pair(int rows, int sampleCount, int[]? map)
        {
            var paired = new int[rows];
            var useMap = map != null && map.Length == rows;
            if (useMap)
            {
                for (var i = 0; i < rows; i++)
                {
                    if (map![i] < 0 || map[i] >= sampleCount)
                    {
                        useMap = false;
                        break;
                    }
                }
            }

            for (var i = 0; i < rows; i++)
            {
                paired[i] = useMap ? map![i] : (i < sampleCount ? i : -1);
            }

            return paired;
        }

        private static string MetaPath(string root)
        {
            return Path.Combine(root, "NASCARHeat5_Data", "StreamingAssets", "PC", "FMOD", "Music Bank.bank");
        }

        private static void EnsureClosed(string root)
        {
            if (root.IndexOf("NASCARHeat5_Data", StringComparison.OrdinalIgnoreCase) < 0
                && root.IndexOf("NASCAR Heat 5", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            if (Process.GetProcessesByName("NASCARHeat5").Length > 0)
            {
                throw new InvalidOperationException("Close Heat 5 before changing the music.");
            }
        }

        public static WaveStream OpenFile(string path)
        {
            if (!path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                return new AudioFileReader(path);
            }

            Mp3FileReader? frames = null;
            AudioFileReader? media = null;
            try
            {
                frames = new Mp3FileReader(path);
            }
            catch (Exception)
            {
            }

            try
            {
                media = new AudioFileReader(path);
            }
            catch (Exception)
            {
                if (frames == null)
                {
                    throw new InvalidOperationException("That file has no audio.");
                }

                return frames;
            }

            if (frames == null)
            {
                return media;
            }

            if (media.TotalTime > frames.TotalTime + TimeSpan.FromMilliseconds(250))
            {
                frames.Dispose();
                return media;
            }

            media.Dispose();
            return frames;
        }

        private static short[] ReadPcm(string path)
        {
            var openers = new List<Func<WaveStream>>();
            if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                openers.Add(() => new Mp3FileReader(path));
                openers.Add(() => new AudioFileReader(path));
            }
            else
            {
                openers.Add(() => new AudioFileReader(path));
            }

            short[] best = Array.Empty<short>();
            InvalidOperationException? failed = null;
            foreach (var open in openers)
            {
                try
                {
                    var pcm = DecodePcm(open());
                    if (pcm.Length > best.Length)
                    {
                        best = pcm;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    failed = ex;
                }
                catch (Exception)
                {
                }
            }

            if (best.Length < 48000)
            {
                throw failed ?? new InvalidOperationException("That file has no audio.");
            }

            return best;
        }

        private static short[] DecodePcm(WaveStream reader)
        {
            using (reader)
            {
            ISampleProvider samples = reader.ToSampleProvider();
            if (reader.WaveFormat.Channels == 1)
            {
                samples = new MonoToStereoSampleProvider(samples);
            }
            else if (reader.WaveFormat.Channels != 2)
            {
                throw new InvalidOperationException("Use a mono or stereo mp3.");
            }

            if (samples.WaveFormat.SampleRate != 48000)
            {
                samples = new WdlResamplingSampleProvider(samples, 48000);
            }

            var floats = new List<float>();
            var buf = new float[48000];
            int read;
            while ((read = samples.Read(buf, 0, buf.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    floats.Add(buf[i]);
                }

                if (floats.Count > 48000 * 2 * MaxSeconds)
                {
                    throw new InvalidOperationException("That file is longer than 15 minutes.");
                }
            }

            if (floats.Count < 48000)
            {
                throw new InvalidOperationException("That file is too short.");
            }

            var pcm = new short[floats.Count];
            for (var i = 0; i < pcm.Length; i++)
            {
                var clamped = floats[i];
                if (clamped > 1f)
                {
                    clamped = 1f;
                }

                if (clamped < -1f)
                {
                    clamped = -1f;
                }

                pcm[i] = (short)Math.Round(clamped * 32767f);
            }

            return pcm;
            }
        }

        private sealed class SampleSlot
        {
            public int HeaderAt;
            public int Samples;
            public int Offset;
            public int DataAt;
            public int Length;
            public ulong Raw;
            public string Name = "";
        }

        private sealed class ParsedBank
        {
            public int FsbAt;
            public int DataSizeAt;
            public int SampleHeaderSize;
            public int NameTableSize;
            public int DataAt;
            public int SndSizeAt;
            public int SndPayload;
            public int FsbLengthAt;
            public int Mode;
            public List<SampleSlot> Samples = new List<SampleSlot>();
        }

        static bool AnyRestore(IReadOnlyList<bool>? restoreOriginal)
        {
            if (restoreOriginal == null)
            {
                return false;
            }

            for (var i = 0; i < restoreOriginal.Count; i++)
            {
                if (restoreOriginal[i])
                {
                    return true;
                }
            }

            return false;
        }

        static (byte[] Piece, int Frames) OriginalPiece(string root, int eventIndex)
        {
            var streamsBak = BackupOf(StreamsPath(root));
            var metaBak = BackupOf(MetaPath(root));
            if (!File.Exists(streamsBak) || !File.Exists(metaBak))
            {
                throw new InvalidOperationException("There is no music backup to restore.");
            }

            var map = MusicMeta.SampleMap(metaBak, streamsBak);
            if (eventIndex < 0 || eventIndex >= map.Length || map[eventIndex] < 0)
            {
                throw new InvalidOperationException("That song is not in the restore file.");
            }

            var bank = File.ReadAllBytes(streamsBak);
            var parsed = Parse(bank);
            var sample = parsed.Samples[map[eventIndex]];
            var piece = new byte[sample.Length];
            Buffer.BlockCopy(bank, sample.DataAt, piece, 0, piece.Length);
            return (piece, sample.Samples);
        }

        public static byte[] PreviewPcm(string root, int sampleIndex, string? bankPath = null)
        {
            var path = bankPath ?? StreamsPath(root);
            using var file = File.OpenRead(path);
            var headerLength = (int)Math.Min(1024 * 1024, file.Length);
            var header = new byte[headerLength];
            var got = file.Read(header, 0, header.Length);
            if (got != header.Length)
            {
                Array.Resize(ref header, got);
            }

            var parsed = Parse(header, file.Length);
            if (sampleIndex < 0 || sampleIndex >= parsed.Samples.Count)
            {
                throw new InvalidOperationException("That song has no audio.");
            }

            var slot = parsed.Samples[sampleIndex];
            if (parsed.Mode != 16)
            {
                file.Position = 0;
                var whole = new byte[file.Length];
                var gotAll = 0;
                while (gotAll < whole.Length)
                {
                    var read = file.Read(whole, gotAll, whole.Length - gotAll);
                    if (read <= 0)
                    {
                        throw new InvalidOperationException("That song has no audio.");
                    }

                    gotAll += read;
                }

                var dataSize = BitConverter.ToInt32(whole, parsed.DataSizeAt);
                var fsbLength = HeaderBytes + parsed.SampleHeaderSize + parsed.NameTableSize + dataSize;
                var decoded = FmodPcm.ReadStereo48(root, whole, parsed.FsbAt, fsbLength, sampleIndex, SourceRate(slot.Raw));
                var wave = new byte[decoded.Length * 2];
                Buffer.BlockCopy(decoded, 0, wave, 0, wave.Length);
                return wave;
            }

            var data = new byte[slot.Length];
            file.Position = slot.DataAt;
            var filled = 0;
            while (filled < data.Length)
            {
                var read = file.Read(data, filled, data.Length - filled);
                if (read <= 0)
                {
                    throw new InvalidOperationException("That song has no audio.");
                }

                filled += read;
            }

            var pcm = FadpcmCodec.DecodeStereo(data, slot.Samples);
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static ParsedBank Parse(byte[] bank)
        {
            return Parse(bank, bank.Length);
        }

        private static ParsedBank Parse(byte[] bank, long fileLength)
        {
            var fsb = IndexOf(bank, Encoding.ASCII.GetBytes("FSB5"));
            if (fsb < 0 || Encoding.ASCII.GetString(bank, fsb, 4) != "FSB5")
            {
                throw new InvalidOperationException("Music bank was not recognized.");
            }

            var version = BitConverter.ToInt32(bank, fsb + 4);
            var count = BitConverter.ToInt32(bank, fsb + 8);
            var headerSize = BitConverter.ToInt32(bank, fsb + 12);
            var nameSize = BitConverter.ToInt32(bank, fsb + 16);
            var dataSize = BitConverter.ToInt32(bank, fsb + 20);
            var mode = BitConverter.ToInt32(bank, fsb + 24);
            if (version != 1 || (mode != 16 && mode != 15) || count < 1 || count > 128)
            {
                throw new InvalidOperationException("Music bank was not recognized.");
            }

            var parsed = new ParsedBank
            {
                FsbAt = fsb,
                Mode = mode,
                DataSizeAt = fsb + 20,
                SampleHeaderSize = headerSize,
                NameTableSize = nameSize,
                DataAt = fsb + HeaderBytes + headerSize + nameSize
            };
            var p = fsb + HeaderBytes;
            var headerEnd = fsb + HeaderBytes + headerSize;
            for (var i = 0; i < count; i++)
            {
                if (p + 8 > headerEnd)
                {
                    throw new InvalidOperationException("Music bank was not recognized.");
                }

                var slot = new SampleSlot { HeaderAt = p, Raw = BitConverter.ToUInt64(bank, p) };
                p += 8;
                var next = (slot.Raw & 1) != 0;
                slot.Offset = (int)(((slot.Raw >> 7) & 0x07FFFFFF) << 5);
                slot.Samples = (int)((slot.Raw >> 34) & 0x3FFFFFFF);
                while (next)
                {
                    if (p + 4 > headerEnd)
                    {
                        throw new InvalidOperationException("Music bank was not recognized.");
                    }

                    var flag = BitConverter.ToUInt32(bank, p);
                    next = (flag & 1) != 0;
                    var size = (int)((flag >> 1) & 0xFFFFFF);
                    p += 4 + size;
                }

                parsed.Samples.Add(slot);
            }

            if (p > headerEnd)
            {
                throw new InvalidOperationException("Music bank was not recognized.");
            }

            for (var i = 0; i < parsed.Samples.Count; i++)
            {
                var slot = parsed.Samples[i];
                var end = i + 1 < parsed.Samples.Count ? parsed.Samples[i + 1].Offset : dataSize;
                slot.Length = end - slot.Offset;
                slot.DataAt = parsed.DataAt + slot.Offset;
                if (slot.Offset < 0 || slot.Length <= 0 || slot.DataAt + (long)slot.Length > fileLength || (slot.Offset & 31) != 0)
                {
                    throw new InvalidOperationException("Music bank was not recognized.");
                }
            }

            parsed.SndSizeAt = -1;
            parsed.FsbLengthAt = -1;
            var cursor = 12;
            while (cursor + 8 <= fsb)
            {
                var id = Encoding.ASCII.GetString(bank, cursor, 4);
                var size = BitConverter.ToInt32(bank, cursor + 4);
                if (id == "LIST")
                {
                    cursor += 12;
                    continue;
                }

                if (id == "SNDH" && size >= 12)
                {
                    var payload = cursor + 8;
                    var fsbLength = HeaderBytes + headerSize + nameSize + dataSize;
                    for (var i = 0; i <= size - 4; i += 4)
                    {
                        if (BitConverter.ToInt32(bank, payload + i) == fsbLength)
                        {
                            parsed.FsbLengthAt = payload + i;
                            break;
                        }
                    }
                }

                if (id == "SND ")
                {
                    parsed.SndSizeAt = cursor + 4;
                    parsed.SndPayload = cursor + 8;
                }

                cursor += 8 + size + (size & 1);
            }

            if (parsed.SndPayload > fsb && parsed.SndPayload >= 0)
            {
                throw new InvalidOperationException("Music bank was not recognized.");
            }

            ReadNames(bank, parsed);
            return parsed;
        }

        private static void ReadNames(byte[] bank, ParsedBank parsed)
        {
            if (parsed.NameTableSize < parsed.Samples.Count * 4)
            {
                return;
            }

            var table = parsed.DataAt - parsed.NameTableSize;
            if (table < 0 || table + parsed.Samples.Count * 4 > bank.Length)
            {
                return;
            }

            for (var i = 0; i < parsed.Samples.Count; i++)
            {
                var offset = BitConverter.ToInt32(bank, table + i * 4);
                var start = table + offset;
                if (offset < 0 || start < table || start >= bank.Length)
                {
                    continue;
                }

                var end = start;
                while (end < bank.Length && bank[end] != 0)
                {
                    end++;
                }

                parsed.Samples[i].Name = Encoding.UTF8.GetString(bank, start, end - start);
            }
        }

        private static byte[] Rebuild(byte[] bank, ParsedBank parsed, byte[][] pieces, int[] counts)
        {
            if (parsed.SndSizeAt < 0 || parsed.FsbLengthAt < 0)
            {
                throw new InvalidOperationException("Music bank was not recognized.");
            }

            var dataSize = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                if ((pieces[i].Length & 31) != 0)
                {
                    throw new InvalidOperationException("Music bank was not recognized.");
                }

                dataSize += pieces[i].Length;
            }

            var output = new byte[parsed.DataAt + dataSize];
            Buffer.BlockCopy(bank, 0, output, 0, parsed.DataAt);
            var cursor = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                Buffer.BlockCopy(pieces[i], 0, output, parsed.DataAt + cursor, pieces[i].Length);
                var raw = parsed.Samples[i].Raw;
                raw &= ~(0x3FFFFFFFUL << 34);
                raw &= ~(0x07FFFFFFUL << 7);
                raw &= ~(0xFUL << 1);
                raw |= 9UL << 1;
                raw |= ((ulong)counts[i] & 0x3FFFFFFFUL) << 34;
                raw |= ((ulong)(cursor >> 5) & 0x07FFFFFFUL) << 7;
                var packed = BitConverter.GetBytes(raw);
                Buffer.BlockCopy(packed, 0, output, parsed.Samples[i].HeaderAt, 8);
                cursor += pieces[i].Length;
            }

            Buffer.BlockCopy(BitConverter.GetBytes(dataSize), 0, output, parsed.DataSizeAt, 4);
            var fsbLength = HeaderBytes + parsed.SampleHeaderSize + parsed.NameTableSize + dataSize;
            Buffer.BlockCopy(BitConverter.GetBytes(fsbLength), 0, output, parsed.FsbLengthAt, 4);
            var sndSize = (parsed.FsbAt - parsed.SndPayload) + fsbLength;
            Buffer.BlockCopy(BitConverter.GetBytes(sndSize), 0, output, parsed.SndSizeAt, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(output.Length - 8), 0, output, 4, 4);
            return output;
        }

        private static int IndexOf(byte[] data, byte[] needle)
        {
            for (var i = 0; i <= data.Length - needle.Length; i++)
            {
                var ok = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j])
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void Save(byte[] bytes, string path)
        {
            var tmp = path + ".nh5music.tmp";
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }

            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path))
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                }
            }

            File.Replace(tmp, path, null);
        }
    }
}
