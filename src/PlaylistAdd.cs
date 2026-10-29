using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Heat5Music
{
    internal static partial class MusicBank
    {
        static void AddChosenSongs(string root, IReadOnlyList<string?> files, IReadOnlyList<string> titles, IReadOnlyList<string> artists, int[] paired, IReadOnlyList<bool>? inPlaylist, Action<string>? progress = null)
        {
            var jobs = new List<SongAdd>();
            for (var i = 0; i < files.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(files[i]))
                {
                    continue;
                }

                var playlist = inPlaylist == null || i >= inPlaylist.Count || inPlaylist[i];
                var sample = i < paired.Length ? paired[i] : -1;
                if (sample >= 0 && playlist)
                {
                    continue;
                }

                if (!File.Exists(files[i]!))
                {
                    throw new InvalidOperationException("Missing file: " + files[i]);
                }

                jobs.Add(new SongAdd
                {
                    File = files[i]!,
                    Title = i < titles.Count ? titles[i] : "",
                    Artist = i < artists.Count ? artists[i] : "",
                    ExistingSample = sample
                });
            }

            if (jobs.Count == 0)
            {
                return;
            }

            var stringsPath = StringsPath(root);
            if (!File.Exists(stringsPath))
            {
                throw new InvalidOperationException("The music name list was not found in that Heat 5 folder.");
            }

            var stringsBak = BackupOf(stringsPath);
            if (!File.Exists(stringsBak))
            {
                File.Copy(stringsPath, stringsBak, false);
            }

            var streams = File.ReadAllBytes(StreamsPath(root));
            var meta = File.ReadAllBytes(MetaPath(root));
            var strings = File.ReadAllBytes(stringsPath);
            var parsed = Parse(streams);
            if (parsed.Mode != 16)
            {
                throw new InvalidOperationException("That music bank has to be saved once before a song can be added.");
            }

            var eventCount = 0;
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id == "EVTB")
                {
                    eventCount++;
                }
            }
            var titleSlots = new string?[eventCount + jobs.Count];
            var artistSlots = new string?[titleSlots.Length];
            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                var token = SongToken(job.Title, eventCount + i + 1);
                var show = string.IsNullOrWhiteSpace(job.Title) ? token : job.Title.Trim();
                if (show.Length > 80)
                {
                    show = show.Substring(0, 80);
                }

                progress?.Invoke(string.IsNullOrWhiteSpace(show) ? "Adding a song" : "Adding " + show);
                byte[] sampleGuid;
                int frames;
                if (job.ExistingSample >= 0)
                {
                    if (job.ExistingSample >= parsed.Samples.Count)
                    {
                        throw new InvalidOperationException("That song has no audio to replace.");
                    }

                    frames = parsed.Samples[job.ExistingSample].Samples;
                    sampleGuid = EnsureSampleGuid(ref streams, job.ExistingSample);
                    parsed = Parse(streams);
                }
                else
                {
                    var pcm = ReadPcm(job.File);
                    frames = pcm.Length / 2;
                    var encoded = FadpcmCodec.EncodeStereo(pcm);
                    streams = AppendSample(streams, encoded, frames, show);
                    parsed = Parse(streams);
                    sampleGuid = EnsureSampleGuid(ref streams, parsed.Samples.Count - 1);
                    parsed = Parse(streams);
                }

                AddEvent(ref meta, frames, sampleGuid);
                AddSongPath(ref strings, token, LastEventGuid(meta));
                titleSlots[eventCount + i] = show;
                artistSlots[eventCount + i] = job.Artist ?? "";
            }

            meta = MusicMeta.Rewrite(meta, titleSlots, artistSlots);
            SeparateSharedBuses(ref meta);
            Save(streams, StreamsPath(root));
            Save(meta, MetaPath(root));
            Save(strings, stringsPath);
        }

        public static void RepairSharedBuses(string root)
        {
            var metaPath = MetaPath(root);
            var meta = File.ReadAllBytes(metaPath);
            if (!SeparateSharedBuses(ref meta))
            {
                return;
            }

            Save(meta, metaPath);
        }

        static bool SeparateSharedBuses(ref byte[] meta)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new List<byte[]>();
            var pendingMaster = new List<byte[]>();
            var pendingGroup = new List<byte[]>();
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id != "EVTB" || chunk.Size < 80)
                {
                    continue;
                }

                var input = BytesAt(meta, chunk.Payload + 48, 16);
                var master = BytesAt(meta, chunk.Payload + 64, 16);
                var key = Convert.ToHexString(input) + Convert.ToHexString(master);
                if (seen.Add(key))
                {
                    continue;
                }

                var group = FindGroupGuid(meta, master);
                var inst = FindInst(meta, BytesAt(meta, chunk.Payload + 32, 16));
                var newInput = NewGuid();
                var newMaster = NewGuid();
                var newGroup = NewGuid();
                ReplaceRange(meta, input, newInput, chunk.At, chunk.End);
                ReplaceRange(meta, master, newMaster, chunk.At, chunk.End);
                ReplaceRange(meta, group, newGroup, inst.At, inst.End);
                var ibus = CopyBus(meta, "IBUS", "IBSB", input);
                var mbus = CopyBus(meta, "MBUS", "MBSB", master);
                var gbus = CopyBus(meta, "GBUS", "GBSB", group);
                Replace(ibus, input, newInput);
                Replace(mbus, master, newMaster);
                Replace(gbus, group, newGroup);
                Replace(gbus, master, newMaster);
                pending.Add(PadEven(ibus));
                pendingMaster.Add(PadEven(mbus));
                pendingGroup.Add(PadEven(gbus));
            }

            if (pending.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < pending.Count; i++)
            {
                BumpCount(meta, "IBSS");
                BumpCount(meta, "GBSS");
                BumpCount(meta, "MBSS");
            }

            InsertBuses(ref meta, "MBSS", Join(pendingMaster));
            InsertBuses(ref meta, "GBSS", Join(pendingGroup));
            InsertBuses(ref meta, "IBSS", Join(pending));
            return true;
        }

        static void InsertBuses(ref byte[] meta, string listType, byte[] extra)
        {
            var list = FindList(meta, listType);
            if (list == null)
            {
                throw new InvalidOperationException("The music list could not be extended.");
            }

            meta = Insert(meta, list.Value.End, extra, list.Value.At);
        }

        static byte[] Join(List<byte[]> parts)
        {
            var length = 0;
            foreach (var part in parts)
            {
                length += part.Length;
            }

            var joined = new byte[length];
            var at = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, joined, at, part.Length);
                at += part.Length;
            }

            return joined;
        }

        static byte[] PadEven(byte[] chunk)
        {
            if ((chunk.Length & 1) == 0)
            {
                return chunk;
            }

            var padded = new byte[chunk.Length + 1];
            Buffer.BlockCopy(chunk, 0, padded, 0, chunk.Length);
            return padded;
        }

        static byte[] CopyBus(byte[] meta, string listType, string id, byte[] guid)
        {
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id == "LIST" && chunk.ListType == listType && GuidEquals(meta, chunk, id, 0, guid))
                {
                    return CopyChunk(meta, chunk);
                }
            }

            throw new InvalidOperationException("The music list could not be extended.");
        }

        static byte[] FindGroupGuid(byte[] meta, byte[] master)
        {
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id != "GBSB" || chunk.Size < 34 || !SameBytes(meta, chunk.Payload + 18, master))
                {
                    continue;
                }

                return BytesAt(meta, chunk.Payload, 16);
            }

            throw new InvalidOperationException("The music list could not be extended.");
        }

        static ChunkRef FindInst(byte[] meta, byte[] timeline)
        {
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id == "INST" && chunk.Size >= 99 && SameBytes(meta, chunk.Payload, timeline))
                {
                    return chunk;
                }
            }

            throw new InvalidOperationException("The music list could not be extended.");
        }

        static byte[] BytesAt(byte[] data, int at, int length)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(data, at, copy, 0, length);
            return copy;
        }

        static bool SameBytes(byte[] data, int at, byte[] guid)
        {
            if (at < 0 || at + guid.Length > data.Length)
            {
                return false;
            }

            for (var i = 0; i < guid.Length; i++)
            {
                if (data[at + i] != guid[i])
                {
                    return false;
                }
            }

            return true;
        }

        static void ReplaceRange(byte[] data, byte[] from, byte[] to, int start, int end)
        {
            for (var i = start; i + 16 <= end; i++)
            {
                if (!SameBytes(data, i, from))
                {
                    continue;
                }

                Buffer.BlockCopy(to, 0, data, i, 16);
                i += 15;
            }
        }

        sealed class SongAdd
        {
            public string File = "";
            public string Title = "";
            public string Artist = "";
            public int ExistingSample = -1;
        }

        static string SongToken(string title, int number)
        {
            var text = new StringBuilder();
            foreach (var letter in title ?? "")
            {
                if (letter < 128 && (char.IsLetterOrDigit(letter) || letter == ' '))
                {
                    text.Append(letter);
                }
            }

            var token = text.ToString().Trim();
            if (token.Length == 0)
            {
                token = "Song " + number;
            }

            if (token.IndexOf("RightThereAllAlong", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                token = "Song " + number;
            }

            return token;
        }

        static byte[] AppendSample(byte[] streams, byte[] piece, int frames, string name)
        {
            var parsed = Parse(streams);
            var count = parsed.Samples.Count;
            var pieces = new byte[count + 1][];
            var counts = new int[count + 1];
            var names = new string[count + 1];
            for (var i = 0; i < count; i++)
            {
                var sample = parsed.Samples[i];
                pieces[i] = new byte[sample.Length];
                Buffer.BlockCopy(streams, sample.DataAt, pieces[i], 0, sample.Length);
                counts[i] = sample.Samples;
                names[i] = sample.Name ?? "";
            }

            pieces[count] = piece;
            counts[count] = frames;
            names[count] = name;
            var fsb = BuildFadpcm(streams, parsed, pieces, counts, names);
            var rebuilt = SpliceFsb(streams, parsed, fsb);
            Parse(rebuilt);
            return rebuilt;
        }

        static byte[] LastEventGuid(byte[] meta)
        {
            byte[]? last = null;
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id != "EVTB" || chunk.Size < 16)
                {
                    continue;
                }

                last = new byte[16];
                Buffer.BlockCopy(meta, chunk.Payload, last, 0, 16);
            }

            if (last == null)
            {
                throw new InvalidOperationException("The music list could not be extended.");
            }

            return last;
        }

        static void AddEvent(ref byte[] meta, int frames, byte[] sampleGuid)
        {
            var chunks = ListChunks(meta);
            ChunkRef? events = null;
            ChunkRef? timelines = null;
            ChunkRef? waves = null;
            ChunkRef? templateEvent = null;
            foreach (var chunk in chunks)
            {
                if (chunk.Id != "LIST")
                {
                    continue;
                }

                if (chunk.ListType == "EVTS")
                {
                    events = chunk;
                }
                else if (chunk.ListType == "TLNS")
                {
                    timelines = chunk;
                }
                else if (chunk.ListType == "WAIS")
                {
                    waves = chunk;
                }
                else if (chunk.ListType == "EVNT")
                {
                    templateEvent = chunk;
                }
            }

            if (events == null || timelines == null || waves == null || templateEvent == null)
            {
                throw new InvalidOperationException("The music list could not be extended.");
            }

            var eventGuid = GuidAt(meta, templateEvent.Value, "EVTB", 0);
            var timelineGuid = GuidAt(meta, templateEvent.Value, "EVTB", 32);
            ChunkRef? templateTimeline = null;
            foreach (var chunk in chunks)
            {
                if (chunk.Id == "LIST" && chunk.ListType == "TMLN" && GuidEquals(meta, chunk, "TLNB", 16, eventGuid))
                {
                    templateTimeline = chunk;
                    break;
                }
            }

            if (templateTimeline == null)
            {
                throw new InvalidOperationException("The music list could not be extended.");
            }

            var waveGuid = GuidAt(meta, templateTimeline.Value, "TLNB", 38);
            ChunkRef? templateWave = null;
            foreach (var chunk in chunks)
            {
                if (chunk.Id == "LIST" && chunk.ListType == "WAIT" && GuidEquals(meta, chunk, "INST", 0, timelineGuid))
                {
                    templateWave = chunk;
                    break;
                }
            }

            if (templateWave == null)
            {
                throw new InvalidOperationException("The music list could not be extended.");
            }

            var newEvent = NewGuid();
            var newTimeline = NewGuid();
            var newWave = NewGuid();
            var eventCopy = CopyChunk(meta, templateEvent.Value);
            var timelineCopy = CopyChunk(meta, templateTimeline.Value);
            var waveCopy = CopyChunk(meta, templateWave.Value);
            Replace(eventCopy, eventGuid, newEvent);
            Replace(eventCopy, timelineGuid, newTimeline);
            Replace(timelineCopy, eventGuid, newEvent);
            Replace(timelineCopy, timelineGuid, newTimeline);
            Replace(timelineCopy, waveGuid, newWave);
            Replace(waveCopy, timelineGuid, newTimeline);
            Replace(waveCopy, waveGuid, newWave);
            Replace(waveCopy, GuidAt(meta, templateWave.Value, "WAIB", 16), sampleGuid);
            WriteLength(timelineCopy, frames);
            BumpCount(meta, "EVTS");
            BumpCount(meta, "TLNS");
            BumpCount(meta, "WAIS");
            BumpBank(meta);
            meta = Insert(meta, waves.Value.End, waveCopy, waves.Value.At);
            meta = Insert(meta, timelines.Value.End, timelineCopy, timelines.Value.At);
            meta = Insert(meta, events.Value.End, eventCopy, events.Value.At);
            InsertHash(ref meta, newEvent);
        }

        internal static void RemovePlaylistEvent(string root, int eventIndex)
        {
            var original = ReadOriginal(root);
            if (original != null && eventIndex < original.Count)
            {
                throw new InvalidOperationException("That song is part of the original soundtrack.");
            }

            var metaPath = MetaPath(root);
            var meta = File.ReadAllBytes(metaPath);
            var events = new List<ChunkRef>();
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id == "EVTB" && chunk.Size >= 48)
                {
                    events.Add(chunk);
                }
            }

            if (eventIndex < 0 || eventIndex >= events.Count)
            {
                throw new InvalidOperationException("That song could not be removed.");
            }

            var evtb = events[eventIndex];
            var eventGuid = GuidBytes(meta, evtb.Payload);
            var timelineGuid = GuidBytes(meta, evtb.Payload + 32);
            var eventList = OwnerList(meta, evtb.At, "EVNT");
            ChunkRef? timeline = null;
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id == "TLNB" && chunk.Size >= 32 && GuidMatch(meta, chunk.Payload + 16, eventGuid))
                {
                    timeline = chunk;
                    break;
                }
            }

            if (timeline == null)
            {
                throw new InvalidOperationException("That song could not be removed.");
            }

            var timelineList = OwnerList(meta, timeline.Value.At, "TMLN");
            ChunkRef? wave = null;
            foreach (var chunk in ListChunks(meta))
            {
                if (chunk.Id == "INST" && chunk.Size >= 16 && GuidMatch(meta, chunk.Payload, timelineGuid))
                {
                    wave = chunk;
                    break;
                }
            }

            if (wave == null)
            {
                throw new InvalidOperationException("That song could not be removed.");
            }

            var waveList = OwnerList(meta, wave.Value.At, "WAIT");
            var hashAt = HashRecord(meta, eventGuid);
            DropCount(meta, "EVTS");
            DropCount(meta, "TLNS");
            DropCount(meta, "WAIS");
            DropBank(meta);
            if (hashAt >= 0)
            {
                DropHashCount(meta);
            }

            var cuts = new List<int> { eventList.At, timelineList.At, waveList.At };
            var spans = new List<int> { SpanOf(eventList), SpanOf(timelineList), SpanOf(waveList) };
            if (hashAt >= 0)
            {
                cuts.Add(hashAt);
                spans.Add(20);
            }

            for (var i = 0; i < cuts.Count; i++)
            {
                var best = i;
                for (var j = i + 1; j < cuts.Count; j++)
                {
                    if (cuts[j] > cuts[best])
                    {
                        best = j;
                    }
                }

                var swapAt = cuts[i];
                var swapSpan = spans[i];
                cuts[i] = cuts[best];
                spans[i] = spans[best];
                cuts[best] = swapAt;
                spans[best] = swapSpan;
            }

            for (var i = 0; i < cuts.Count; i++)
            {
                meta = CutRange(meta, cuts[i], spans[i]);
            }

            Save(meta, metaPath);
        }

        public static int UnusedSampleCount(string root)
        {
            return UnusedIndexes(root).Count;
        }

        internal static void RemoveUnreferencedSamples(string root)
        {
            var drop = UnusedIndexes(root);
            if (drop.Count == 0)
            {
                return;
            }

            var path = StreamsPath(root);
            var streams = File.ReadAllBytes(path);
            var waves = WaveChunks(streams);
            var parsed = Parse(streams);
            if (waves.Count != parsed.Samples.Count)
            {
                throw new InvalidOperationException("That song could not be removed.");
            }

            var cuts = new List<int>();
            var spans = new List<int>();
            var hashes = 0;
            foreach (var index in drop)
            {
                if (index < 0 || index >= waves.Count)
                {
                    throw new InvalidOperationException("That song could not be removed.");
                }

                var wave = waves[index];
                cuts.Add(wave.At);
                spans.Add(SpanOf(wave));
                if (wave.Size >= 16)
                {
                    var guid = new byte[16];
                    Buffer.BlockCopy(streams, wave.Payload, guid, 0, 16);
                    var hashAt = HashRecord(streams, guid);
                    if (hashAt >= 0)
                    {
                        cuts.Add(hashAt);
                        spans.Add(20);
                        hashes++;
                    }
                }
            }

            for (var n = 0; n < drop.Count; n++)
            {
                DropCount(streams, "WAVS");
            }

            for (var n = 0; n < hashes; n++)
            {
                DropHashCount(streams);
            }

            for (var i = 0; i < cuts.Count; i++)
            {
                var best = i;
                for (var j = i + 1; j < cuts.Count; j++)
                {
                    if (cuts[j] > cuts[best])
                    {
                        best = j;
                    }
                }

                var swapAt = cuts[i];
                var swapSpan = spans[i];
                cuts[i] = cuts[best];
                spans[i] = spans[best];
                cuts[best] = swapAt;
                spans[best] = swapSpan;
            }

            for (var i = 0; i < cuts.Count; i++)
            {
                streams = CutRange(streams, cuts[i], spans[i]);
            }

            FixFsbOffset(streams);
            parsed = Parse(streams);
            var keep = new bool[parsed.Samples.Count];
            for (var i = 0; i < keep.Length; i++)
            {
                keep[i] = true;
            }

            foreach (var index in drop)
            {
                if (index < 0 || index >= keep.Length)
                {
                    throw new InvalidOperationException("That song could not be removed.");
                }

                keep[index] = false;
            }

            var rebuilt = SpliceFsb(streams, parsed, OmitSamples(streams, parsed, keep));
            Parse(rebuilt);
            Save(rebuilt, path);
            FixWaveIndexes(path);
        }

        static List<int> UnusedIndexes(string root)
        {
            var result = new List<int>();
            var streamsPath = StreamsPath(root);
            var metaPath = MetaPath(root);
            if (!File.Exists(streamsPath) || !File.Exists(metaPath))
            {
                return result;
            }

            int[] map;
            try
            {
                map = MusicMeta.SampleMap(metaPath, streamsPath);
            }
            catch (Exception)
            {
                return result;
            }

            if (map.Length == 0)
            {
                return result;
            }

            ParsedBank parsed;
            try
            {
                using var file = File.OpenRead(streamsPath);
                var take = (int)Math.Min(file.Length, 1024 * 1024);
                var header = new byte[take];
                var got = file.Read(header, 0, header.Length);
                if (got != header.Length)
                {
                    Array.Resize(ref header, got);
                }

                parsed = Parse(header, file.Length);
            }
            catch (Exception)
            {
                return result;
            }

            var used = new bool[parsed.Samples.Count];
            var referenced = 0;
            for (var i = 0; i < map.Length; i++)
            {
                var sample = map[i];
                if (sample < 0 || sample >= used.Length)
                {
                    continue;
                }

                if (!used[sample])
                {
                    referenced++;
                }

                used[sample] = true;
            }

            if (referenced == 0)
            {
                return result;
            }

            var original = ReadOriginal(root);
            var keepThrough = original == null ? 0 : original.Count;
            for (var i = keepThrough; i < used.Length; i++)
            {
                if (!used[i])
                {
                    result.Add(i);
                }
            }

            return result;
        }

        static List<ChunkRef> WaveChunks(byte[] streams)
        {
            var waves = new List<ChunkRef>();
            foreach (var chunk in ListChunks(streams))
            {
                if (chunk.Id == "WAV ")
                {
                    waves.Add(chunk);
                }
            }

            return waves;
        }

        static void FixFsbOffset(byte[] bank)
        {
            var sound = IndexOf(bank, Encoding.ASCII.GetBytes("FSB5"));
            var header = FindId(bank, "SNDH");
            if (sound <= 0 || header == null || header.Value.Size < 8)
            {
                return;
            }

            var field = header.Value.Payload + 4;
            if (field + 4 <= bank.Length)
            {
                BitConverter.GetBytes(sound).CopyTo(bank, field);
            }
        }

        static byte[] OmitSamples(byte[] bank, ParsedBank parsed, bool[] keep)
        {
            var kept = new List<int>();
            for (var i = 0; i < parsed.Samples.Count && i < keep.Length; i++)
            {
                if (keep[i])
                {
                    kept.Add(i);
                }
            }

            if (kept.Count == 0 || kept.Count == parsed.Samples.Count)
            {
                throw new InvalidOperationException("That song could not be removed.");
            }

            var headerEnd = parsed.FsbAt + HeaderBytes + parsed.SampleHeaderSize;
            var blobs = new byte[kept.Count][];
            var pieces = new byte[kept.Count][];
            var names = new string[kept.Count];
            var headerBytes = 0;
            for (var n = 0; n < kept.Count; n++)
            {
                var slot = parsed.Samples[kept[n]];
                var span = SampleHeaderBytes(bank, slot.HeaderAt, headerEnd);
                blobs[n] = new byte[span];
                Buffer.BlockCopy(bank, slot.HeaderAt, blobs[n], 0, span);
                headerBytes += span;
                if (slot.Length <= 0 || (slot.Length & 31) != 0)
                {
                    throw new InvalidOperationException("That song could not be removed.");
                }

                pieces[n] = new byte[slot.Length];
                Buffer.BlockCopy(bank, slot.DataAt, pieces[n], 0, slot.Length);
                names[n] = slot.Name ?? "";
            }

            var text = new List<byte>();
            var offsets = new int[kept.Count];
            for (var i = 0; i < kept.Count; i++)
            {
                offsets[i] = kept.Count * 4 + text.Count;
                text.AddRange(Encoding.UTF8.GetBytes(names[i]));
                text.Add(0);
            }

            var nameBytes = new byte[kept.Count * 4 + text.Count];
            for (var i = 0; i < kept.Count; i++)
            {
                BitConverter.GetBytes(offsets[i]).CopyTo(nameBytes, i * 4);
            }

            text.CopyTo(nameBytes, kept.Count * 4);
            var headerSize = headerBytes;
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
            BitConverter.GetBytes(kept.Count).CopyTo(fsb, 8);
            BitConverter.GetBytes(headerSize).CopyTo(fsb, 12);
            BitConverter.GetBytes(nameBytes.Length).CopyTo(fsb, 16);
            BitConverter.GetBytes(dataSize).CopyTo(fsb, 20);
            var cursor = HeaderBytes;
            var dataCursor = 0;
            for (var i = 0; i < kept.Count; i++)
            {
                var raw = BitConverter.ToUInt64(blobs[i], 0);
                raw &= ~(0x07FFFFFFUL << 7);
                raw |= ((ulong)(dataCursor >> 5) & 0x07FFFFFFUL) << 7;
                BitConverter.GetBytes(raw).CopyTo(blobs[i], 0);
                Buffer.BlockCopy(blobs[i], 0, fsb, cursor, blobs[i].Length);
                cursor += blobs[i].Length;
                Buffer.BlockCopy(pieces[i], 0, fsb, HeaderBytes + headerSize + nameBytes.Length + dataCursor, pieces[i].Length);
                dataCursor += pieces[i].Length;
            }

            Buffer.BlockCopy(nameBytes, 0, fsb, HeaderBytes + headerSize, nameBytes.Length);
            return fsb;
        }

        static int SampleHeaderBytes(byte[] bank, int at, int limit)
        {
            if (at < 0 || at + 8 > bank.Length || at + 8 > limit)
            {
                throw new InvalidOperationException("That song could not be removed.");
            }

            var next = (BitConverter.ToUInt64(bank, at) & 1) != 0;
            var p = at + 8;
            while (next)
            {
                if (p + 4 > bank.Length || p + 4 > limit)
                {
                    throw new InvalidOperationException("That song could not be removed.");
                }

                var flag = BitConverter.ToUInt32(bank, p);
                next = (flag & 1) != 0;
                var size = (int)((flag >> 1) & 0xFFFFFF);
                p += 4 + size;
                if (p > bank.Length || p > limit)
                {
                    throw new InvalidOperationException("That song could not be removed.");
                }
            }

            return p - at;
        }

        static byte[] GuidBytes(byte[] data, int at)
        {
            var guid = new byte[16];
            Buffer.BlockCopy(data, at, guid, 0, 16);
            return guid;
        }

        static bool GuidMatch(byte[] data, int at, byte[] guid)
        {
            if (at < 0 || at + 16 > data.Length)
            {
                return false;
            }

            for (var i = 0; i < 16; i++)
            {
                if (data[at + i] != guid[i])
                {
                    return false;
                }
            }

            return true;
        }

        static ChunkRef OwnerList(byte[] data, int at, string type)
        {
            ChunkRef? best = null;
            foreach (var chunk in ListChunks(data))
            {
                if (chunk.Id != "LIST" || chunk.ListType != type || chunk.At >= at || at >= chunk.End)
                {
                    continue;
                }

                if (best == null || chunk.At > best.Value.At)
                {
                    best = chunk;
                }
            }

            if (best == null)
            {
                throw new InvalidOperationException("That song could not be removed.");
            }

            return best.Value;
        }

        static int SpanOf(ChunkRef chunk)
        {
            return (chunk.End - chunk.At) + (chunk.Size & 1);
        }

        static int HashRecord(byte[] bank, byte[] guid)
        {
            var hash = FindId(bank, "HASH");
            if (hash == null || hash.Value.Size < 4)
            {
                return -1;
            }

            var payload = hash.Value.Payload;
            var count = BitConverter.ToUInt16(bank, payload) >> 1;
            if (BitConverter.ToUInt16(bank, payload + 2) != 20)
            {
                return -1;
            }

            for (var i = 0; i < count; i++)
            {
                var at = payload + 4 + i * 20;
                if (GuidMatch(bank, at, guid))
                {
                    return at;
                }
            }

            return -1;
        }

        static void DropHashCount(byte[] bank)
        {
            var hash = FindId(bank, "HASH");
            if (hash == null)
            {
                return;
            }

            var count = BitConverter.ToUInt16(bank, hash.Value.Payload) >> 1;
            if (count < 1)
            {
                return;
            }

            BitConverter.GetBytes((ushort)(((count - 1) << 1) | 1)).CopyTo(bank, hash.Value.Payload);
        }

        static void DropCount(byte[] bank, string listType)
        {
            var list = FindList(bank, listType);
            if (list == null)
            {
                return;
            }

            foreach (var chunk in ListChunks(bank))
            {
                if (chunk.Id == "LCNT" && chunk.At > list.Value.At && chunk.At < list.Value.End && chunk.Size >= 4)
                {
                    var count = BitConverter.ToInt32(bank, chunk.Payload);
                    if (count > 0)
                    {
                        BitConverter.GetBytes(count - 1).CopyTo(bank, chunk.Payload);
                    }

                    return;
                }
            }
        }

        static void DropBank(byte[] meta)
        {
            var bank = FindId(meta, "BNKI");
            if (bank == null || bank.Value.Size < 28)
            {
                return;
            }

            var count = BitConverter.ToInt32(meta, bank.Value.Payload + 24);
            if (count > 0)
            {
                BitConverter.GetBytes(count - 1).CopyTo(meta, bank.Value.Payload + 24);
            }
        }

        static byte[] CutRange(byte[] data, int at, int span)
        {
            if (span <= 0 || at < 0 || at + span > data.Length)
            {
                throw new InvalidOperationException("That song could not be removed.");
            }

            var shrunk = new byte[data.Length - span];
            Buffer.BlockCopy(data, 0, shrunk, 0, at);
            Buffer.BlockCopy(data, at + span, shrunk, at, data.Length - (at + span));
            GrowSizes(data, at, -span, shrunk);
            return shrunk;
        }

        static byte[] EnsureSampleGuid(ref byte[] streams, int sampleIndex)
        {
            var guids = SampleGuids(streams);
            while (guids.Count <= sampleIndex)
            {
                var guid = NewGuid();
                var waves = FindList(streams, "WAVS");
                var template = FindId(streams, "WAV ");
                if (waves == null || template == null || template.Value.Size < 16)
                {
                    throw new InvalidOperationException("The music list could not be extended.");
                }

                var copy = CopyChunk(streams, template.Value);
                Buffer.BlockCopy(guid, 0, copy, 8, 16);
                WriteSampleIndex(copy, guids.Count);
                BumpCount(streams, "WAVS");
                streams = Insert(streams, waves.Value.End, copy, waves.Value.At);
                InsertHash(ref streams, guid);
                guids.Add(guid);
            }

            return guids[sampleIndex];
        }

        static void WriteSampleIndex(byte[] chunk, int sampleIndex)
        {
            if (chunk.Length < 36 || sampleIndex < 0)
            {
                return;
            }

            if (BitConverter.ToInt32(chunk, 24) != 12 || BitConverter.ToInt32(chunk, 32) != 131072)
            {
                return;
            }

            BitConverter.GetBytes(sampleIndex << 16).CopyTo(chunk, 28);
        }

        static void FixWaveIndexes(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var length = (int)Math.Min(stream.Length, 65536);
            var head = new byte[length];
            var got = stream.Read(head, 0, head.Length);
            var fsb = got;
            for (var i = 0; i + 4 <= got; i++)
            {
                if (head[i] == (byte)'F' && head[i + 1] == (byte)'S' && head[i + 2] == (byte)'B' && head[i + 3] == (byte)'5')
                {
                    fsb = i;
                    break;
                }
            }

            var index = 0;
            var at = 0;
            while (at + 36 <= fsb)
            {
                if (head[at] != (byte)'W' || head[at + 1] != (byte)'A' || head[at + 2] != (byte)'V' || head[at + 3] != (byte)' ')
                {
                    at++;
                    continue;
                }

                var size = BitConverter.ToInt32(head, at + 4);
                if (size < 28 || at + 8 + size > fsb)
                {
                    at++;
                    continue;
                }

                if (BitConverter.ToInt32(head, at + 24) == 12 && BitConverter.ToInt32(head, at + 32) == 131072)
                {
                    var want = index << 16;
                    if (BitConverter.ToInt32(head, at + 28) != want)
                    {
                        var bytes = BitConverter.GetBytes(want);
                        stream.Position = at + 28;
                        stream.Write(bytes, 0, 4);
                    }
                }

                index++;
                at += 8 + size + (size & 1);
            }
        }

        static List<byte[]> SampleGuids(byte[] streams)
        {
            var guids = new List<byte[]>();
            foreach (var chunk in ListChunks(streams))
            {
                if (chunk.Id != "WAV " || chunk.Size < 16)
                {
                    continue;
                }

                var guid = new byte[16];
                Buffer.BlockCopy(streams, chunk.Payload, guid, 0, 16);
                guids.Add(guid);
            }

            return guids;
        }

        static void AddSongPath(ref byte[] strings, string token, byte[] eventGuid)
        {
            var table = SongPaths.Read(strings);
            var path = "event:/Music/Songs/" + token;
            var suffix = 2;
            while (table.Has(path))
            {
                path = "event:/Music/Songs/" + token + " " + suffix;
                suffix++;
            }

            table.Add(path, eventGuid);
            var payload = table.Write();
            strings = ReplacePayload(strings, "STDT", payload);
        }

        static byte[] ReplacePayload(byte[] file, string id, byte[] payload)
        {
            ChunkRef? chunk = null;
            foreach (var item in ListChunks(file))
            {
                if (item.Id == id)
                {
                    chunk = item;
                }
            }

            if (chunk == null)
            {
                throw new InvalidOperationException("The music name list could not be extended.");
            }

            var oldSpan = chunk.Value.Size + (chunk.Value.Size & 1);
            var newSpan = payload.Length + (payload.Length & 1);
            var grown = new byte[file.Length - oldSpan + newSpan];
            Buffer.BlockCopy(file, 0, grown, 0, chunk.Value.Payload);
            Buffer.BlockCopy(payload, 0, grown, chunk.Value.Payload, payload.Length);
            var rest = chunk.Value.Payload + oldSpan;
            Buffer.BlockCopy(file, rest, grown, chunk.Value.Payload + newSpan, file.Length - rest);
            var delta = newSpan - oldSpan;
            if (delta != 0)
            {
                GrowSizes(file, chunk.Value.Payload, delta, grown);
            }

            BitConverter.GetBytes(payload.Length).CopyTo(grown, chunk.Value.At + 4);
            return grown;
        }

        static void InsertHash(ref byte[] bank, byte[] guid)
        {
            var hash = FindId(bank, "HASH");
            if (hash == null || hash.Value.Size < 4)
            {
                throw new InvalidOperationException("The music list could not be extended.");
            }

            var payload = hash.Value.Payload;
            var raw = BitConverter.ToUInt16(bank, payload);
            var count = raw >> 1;
            var entry = BitConverter.ToUInt16(bank, payload + 2);
            if (entry != 20)
            {
                throw new InvalidOperationException("The music list could not be extended.");
            }

            var place = count;
            for (var i = 0; i < count; i++)
            {
                var at = payload + 4 + i * 20;
                if (CompareGuid(guid, bank, at) < 0)
                {
                    place = i;
                    break;
                }
            }

            var record = new byte[20];
            Buffer.BlockCopy(guid, 0, record, 0, 16);
            BitConverter.GetBytes(HashGuid(guid)).CopyTo(record, 16);
            BitConverter.GetBytes((ushort)(((count + 1) << 1) | 1)).CopyTo(bank, payload);
            bank = Insert(bank, payload + 4 + place * 20, record, hash.Value.At);
        }

        static uint HashGuid(byte[] guid)
        {
            uint hash = 2166136261;
            for (var i = 0; i < guid.Length; i++)
            {
                hash ^= guid[i];
                hash *= 16777619;
            }

            return hash == 0 ? 1u : hash;
        }

        static int CompareGuid(byte[] guid, byte[] data, int at)
        {
            for (var i = 0; i < 16; i++)
            {
                var diff = guid[i] - data[at + i];
                if (diff != 0)
                {
                    return diff;
                }
            }

            return 0;
        }

        static void BumpCount(byte[] bank, string listType)
        {
            var list = FindList(bank, listType);
            if (list == null)
            {
                return;
            }

            foreach (var chunk in ListChunks(bank))
            {
                if (chunk.Id == "LCNT" && chunk.At > list.Value.At && chunk.At < list.Value.End && chunk.Size >= 4)
                {
                    var count = BitConverter.ToInt32(bank, chunk.Payload);
                    BitConverter.GetBytes(count + 1).CopyTo(bank, chunk.Payload);
                    return;
                }
            }
        }

        static void BumpBank(byte[] meta)
        {
            var bank = FindId(meta, "BNKI");
            if (bank == null || bank.Value.Size < 28)
            {
                return;
            }

            var count = BitConverter.ToInt32(meta, bank.Value.Payload + 24);
            BitConverter.GetBytes(count + 1).CopyTo(meta, bank.Value.Payload + 24);
        }

        static void WriteLength(byte[] timeline, int frames)
        {
            foreach (var chunk in ListChunks(timeline))
            {
                if (chunk.Id == "TLNB" && chunk.Size >= 62)
                {
                    BitConverter.GetBytes(frames).CopyTo(timeline, chunk.Payload + 58);
                    return;
                }
            }
        }

        static byte[] NewGuid()
        {
            return Guid.NewGuid().ToByteArray();
        }

        static byte[] GuidAt(byte[] data, ChunkRef chunk, string id, int offset)
        {
            foreach (var child in ListChunks(data))
            {
                if (child.Id == id && child.At >= chunk.At && child.At < chunk.End && child.Size >= offset + 16)
                {
                    var guid = new byte[16];
                    Buffer.BlockCopy(data, child.Payload + offset, guid, 0, 16);
                    return guid;
                }
            }

            throw new InvalidOperationException("The music list could not be extended.");
        }

        static bool GuidEquals(byte[] data, ChunkRef chunk, string id, int offset, byte[] guid)
        {
            foreach (var child in ListChunks(data))
            {
                if (child.Id != id || child.At < chunk.At || child.At >= chunk.End || child.Size < offset + 16)
                {
                    continue;
                }

                for (var i = 0; i < 16; i++)
                {
                    if (data[child.Payload + offset + i] != guid[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        static byte[] CopyChunk(byte[] data, ChunkRef chunk)
        {
            var copy = new byte[chunk.End - chunk.At];
            Buffer.BlockCopy(data, chunk.At, copy, 0, copy.Length);
            return copy;
        }

        static void Replace(byte[] data, byte[] from, byte[] to)
        {
            if (from == null || to == null || from.Length != 16 || to.Length != 16)
            {
                return;
            }

            for (var i = 0; i <= data.Length - 16; i++)
            {
                var match = true;
                for (var j = 0; j < 16; j++)
                {
                    if (data[i + j] != from[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (!match)
                {
                    continue;
                }

                Buffer.BlockCopy(to, 0, data, i, 16);
                i += 15;
            }
        }

        static byte[] Insert(byte[] data, int at, byte[] extra, int ownerAt)
        {
            var span = extra.Length + (extra.Length & 1);
            var grown = new byte[data.Length + span];
            Buffer.BlockCopy(data, 0, grown, 0, at);
            Buffer.BlockCopy(extra, 0, grown, at, extra.Length);
            Buffer.BlockCopy(data, at, grown, at + span, data.Length - at);
            GrowSizes(data, at, span, grown);
            var ownerSize = BitConverter.ToInt32(data, ownerAt + 4);
            if (at == ownerAt + 8 + ownerSize)
            {
                var size = BitConverter.ToInt32(grown, ownerAt + 4);
                BitConverter.GetBytes(size + span).CopyTo(grown, ownerAt + 4);
            }

            // SNDH stores the FSB file offset. Inserting a sample record before that sound
            // moves the FSB, and a stale offset makes the game show the song name with no audio.
            var sound = IndexOf(grown, Encoding.ASCII.GetBytes("FSB5"));
            var header = FindId(grown, "SNDH");
            if (sound > 0 && header != null && header.Value.Size >= 8)
            {
                var field = header.Value.Payload + 4;
                if (field + 4 <= grown.Length)
                {
                    BitConverter.GetBytes(sound).CopyTo(grown, field);
                }
            }

            return grown;
        }

        static void GrowSizes(byte[] original, int at, int delta, byte[] grown)
        {
            foreach (var chunk in ListChunks(original))
            {
                var end = chunk.Payload + chunk.Size;
                if (chunk.At < at && at >= chunk.Payload && at < end)
                {
                    var size = BitConverter.ToInt32(grown, chunk.At + 4);
                    BitConverter.GetBytes(size + delta).CopyTo(grown, chunk.At + 4);
                }
            }
        }

        static ChunkRef? FindList(byte[] data, string type)
        {
            foreach (var chunk in ListChunks(data))
            {
                if (chunk.Id == "LIST" && chunk.ListType == type)
                {
                    return chunk;
                }
            }

            return null;
        }

        static ChunkRef? FindId(byte[] data, string id)
        {
            foreach (var chunk in ListChunks(data))
            {
                if (chunk.Id == id)
                {
                    return chunk;
                }
            }

            return null;
        }

        static List<ChunkRef> ListChunks(byte[] data)
        {
            var found = new List<ChunkRef>();
            WalkChunks(data, 0, data.Length, found);
            return found;
        }

        static void WalkChunks(byte[] data, int start, int end, List<ChunkRef> found)
        {
            var cursor = start;
            while (cursor + 8 <= end && cursor + 8 <= data.Length)
            {
                var id = Encoding.ASCII.GetString(data, cursor, 4);
                var size = BitConverter.ToInt32(data, cursor + 4);
                if (size < 0 || cursor + 8 + size > data.Length)
                {
                    return;
                }

                var payload = cursor + 8;
                var listType = "";
                if (id is "RIFF" or "LIST")
                {
                    listType = Encoding.ASCII.GetString(data, payload, 4);
                }

                var chunk = new ChunkRef
                {
                    At = cursor,
                    Size = size,
                    Payload = payload,
                    End = payload + size,
                    Id = id,
                    ListType = listType
                };
                found.Add(chunk);
                if (id is "RIFF" or "LIST")
                {
                    WalkChunks(data, payload + 4, payload + size, found);
                }

                cursor = payload + size + (size & 1);
            }
        }

        struct ChunkRef
        {
            public int At;
            public int Size;
            public int Payload;
            public int End;
            public string Id;
            public string ListType;
        }

        sealed class SongPaths
        {
            public Node[] Nodes = Array.Empty<Node>();
            public int[] Parents = Array.Empty<int>();
            public byte[][] Guids = Array.Empty<byte[]>();
            public int[] Leaves = Array.Empty<int>();
            public byte[] Blob = Array.Empty<byte>();

            public static SongPaths Read(byte[] file)
            {
                var chunk = FindId(file, "STDT");
                if (chunk == null)
                {
                    throw new InvalidOperationException("The music name list could not be extended.");
                }

                var cursor = chunk.Value.Payload;
                var type = BitConverter.ToUInt32(file, cursor);
                cursor += 4;
                if (type != 1)
                {
                    throw new InvalidOperationException("The music name list could not be extended.");
                }

                var nodeBytes = ReadElems(file, ref cursor, 8);
                var guidBytes = ReadElems(file, ref cursor, 16);
                var blob = ReadBytes(file, ref cursor);
                var leaves = Read24(file, ref cursor);
                var parents = Read24(file, ref cursor);
                var table = new SongPaths
                {
                    Blob = blob,
                    Parents = parents,
                    Leaves = leaves,
                    Guids = new byte[guidBytes.Length / 16][],
                    Nodes = new Node[nodeBytes.Length / 8]
                };
                for (var i = 0; i < table.Guids.Length; i++)
                {
                    table.Guids[i] = new byte[16];
                    Buffer.BlockCopy(guidBytes, i * 16, table.Guids[i], 0, 16);
                }

                for (var i = 0; i < table.Nodes.Length; i++)
                {
                    var key = BitConverter.ToUInt32(nodeBytes, i * 8);
                    var child = BitConverter.ToUInt32(nodeBytes, i * 8 + 4);
                    table.Nodes[i] = new Node
                    {
                        Key = (byte)(key >> 24),
                        Offset = (int)(key & 0xFFFFFF),
                        Count = (int)(child >> 24),
                        First = (int)(child & 0xFFFFFF)
                    };
                }

                return table;
            }

            public bool Has(string path)
            {
                return Find(path) >= 0;
            }

            public void Add(string path, byte[] guid)
            {
                var before = new List<(string Path, byte[] Guid)>();
                for (var i = 0; i < Leaves.Length; i++)
                {
                    before.Add((Walk(Leaves[i]), Guids[i]));
                }

                if (Find(path) < 0)
                {
                    var node = 0;
                    var rest = path;
                    while (rest.Length > 0)
                    {
                        var key = KeyOf(rest[0]);
                        var child = ChildWithKey(node, key);
                        if (child < 0)
                        {
                            InsertChild(node, rest, guid);
                            break;
                        }

                        var edge = Edge(child);
                        var shared = Shared(edge, rest);
                        if (shared == edge.Length)
                        {
                            rest = rest.Substring(shared);
                            if (rest.Length == 0)
                            {
                                break;
                            }

                            node = child;
                            continue;
                        }

                        Split(child, shared, rest.Substring(shared), guid);
                        break;
                    }
                }

            foreach (var item in before)
            {
                var found = Find(item.Path);
                if (found < 0 || !SameGuid(Guids[found], item.Guid))
                {
                    throw new InvalidOperationException("The music name list could not be extended.");
                }
            }

            if (Find(path) < 0)
            {
                throw new InvalidOperationException("The music name list could not be extended.");
            }
        }

        static bool SameGuid(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

            public byte[] Write()
            {
                var nodeBytes = new byte[Nodes.Length * 8];
                for (var i = 0; i < Nodes.Length; i++)
                {
                    var key = ((uint)Nodes[i].Key << 24) | (uint)(Nodes[i].Offset & 0xFFFFFF);
                    var child = ((uint)Nodes[i].Count << 24) | (uint)(Nodes[i].First & 0xFFFFFF);
                    BitConverter.GetBytes(key).CopyTo(nodeBytes, i * 8);
                    BitConverter.GetBytes(child).CopyTo(nodeBytes, i * 8 + 4);
                }

                var guidBytes = new byte[Guids.Length * 16];
                for (var i = 0; i < Guids.Length; i++)
                {
                    Buffer.BlockCopy(Guids[i], 0, guidBytes, i * 16, 16);
                }

                using var stream = new MemoryStream();
                stream.Write(BitConverter.GetBytes(1u), 0, 4);
                WriteElems(stream, nodeBytes, 8);
                WriteElems(stream, guidBytes, 16);
                WriteBytes(stream, Blob);
                Write24(stream, Leaves);
                Write24(stream, Parents);
                return stream.ToArray();
            }

            int Find(string path)
            {
                for (var i = 0; i < Leaves.Length; i++)
                {
                    if (Walk(Leaves[i]) == path)
                    {
                        return i;
                    }
                }

                return -1;
            }

            string Walk(int leaf)
            {
                var parts = new List<string>();
                var node = leaf;
                var guard = 0;
                while (node > 0 && node < Parents.Length && guard < 64)
                {
                    parts.Add(Edge(node));
                    node = Parents[node];
                    guard++;
                }

                parts.Reverse();
                return string.Concat(parts);
            }

            string Edge(int node)
            {
                var item = Nodes[node];
                if (item.Offset == 0xFFFFFF || item.Offset >= Blob.Length)
                {
                    return "";
                }

                var end = item.Offset;
                while (end < Blob.Length && Blob[end] != 0)
                {
                    end++;
                }

                return Encoding.ASCII.GetString(Blob, item.Offset, end - item.Offset);
            }

            int ChildWithKey(int parent, byte key)
            {
                var item = Nodes[parent];
                for (var i = 0; i < item.Count; i++)
                {
                    var child = item.First + i;
                    if (child < Nodes.Length && Nodes[child].Key == key)
                    {
                        return child;
                    }
                }

                return -1;
            }

            void InsertChild(int parent, string edge, byte[] guid)
            {
                var key = KeyOf(edge[0]);
                var first = Nodes[parent].First;
                var count = Nodes[parent].Count;
                var place = 0;
                for (; place < count; place++)
                {
                    if (Nodes[first + place].Key > key)
                    {
                        break;
                    }
                }

                var index = count == 0 ? Nodes.Length : first + place;
                InsertNode(index, parent, edge, key);
                var parentNow = parent >= index ? parent + 1 : parent;
                Nodes[parentNow].Count = count + 1;
                if (count == 0 || place == 0)
                {
                    Nodes[parentNow].First = index;
                }

                AddLeaf(guid, index);
            }

            void Split(int child, int shared, string rest, byte[] guid)
            {
                var edge = Edge(child);
                var oldFirst = Nodes[child].First;
                var oldCount = Nodes[child].Count;
                var suffixIndex = Nodes.Length;
                InsertNode(suffixIndex, child, edge.Substring(shared), KeyOf(edge[shared]));
                Nodes[suffixIndex].First = oldFirst;
                Nodes[suffixIndex].Count = oldCount;
                for (var i = 0; i < oldCount; i++)
                {
                    var kid = oldFirst + i;
                    if (kid >= 0 && kid < Parents.Length && Parents[kid] == child)
                    {
                        Parents[kid] = suffixIndex;
                    }
                }

                for (var i = 0; i < Leaves.Length; i++)
                {
                    if (Leaves[i] == child)
                    {
                        Leaves[i] = suffixIndex;
                    }
                }

                SetEdge(child, edge.Substring(0, shared));
                Nodes[child].First = suffixIndex;
                Nodes[child].Count = 1;
                InsertChild(child, rest, guid);
            }

            void SetEdge(int node, string edge)
            {
                var offset = Blob.Length;
                var bytes = Encoding.ASCII.GetBytes(edge + "\0");
                var grown = new byte[Blob.Length + bytes.Length];
                Buffer.BlockCopy(Blob, 0, grown, 0, Blob.Length);
                Buffer.BlockCopy(bytes, 0, grown, offset, bytes.Length);
                Blob = grown;
                Nodes[node].Offset = offset;
                Nodes[node].Key = KeyOf(edge[0]);
            }

            void InsertNode(int index, int parent, string edge, byte key)
            {
                for (var i = 0; i < Nodes.Length; i++)
                {
                    if (Nodes[i].Count > 0 && Nodes[i].First >= index)
                    {
                        Nodes[i].First++;
                    }
                }

                for (var i = 0; i < Parents.Length; i++)
                {
                    if (Parents[i] >= index && Parents[i] != 0xFFFFFF)
                    {
                        Parents[i]++;
                    }
                }

                for (var i = 0; i < Leaves.Length; i++)
                {
                    if (Leaves[i] >= index && Leaves[i] != 0xFFFFFF)
                    {
                        Leaves[i]++;
                    }
                }

                var parentNow = parent >= index ? parent + 1 : parent;
                var nodes = new Node[Nodes.Length + 1];
                var parents = new int[Parents.Length + 1];
                Array.Copy(Nodes, 0, nodes, 0, index);
                Array.Copy(Nodes, index, nodes, index + 1, Nodes.Length - index);
                Array.Copy(Parents, 0, parents, 0, index);
                Array.Copy(Parents, index, parents, index + 1, Parents.Length - index);
                var offset = Blob.Length;
                var bytes = Encoding.ASCII.GetBytes(edge + "\0");
                var grown = new byte[Blob.Length + bytes.Length];
                Buffer.BlockCopy(Blob, 0, grown, 0, Blob.Length);
                Buffer.BlockCopy(bytes, 0, grown, offset, bytes.Length);
                Blob = grown;
                nodes[index] = new Node { Key = key, Offset = offset, Count = 0, First = 0 };
                parents[index] = parentNow;
                Nodes = nodes;
                Parents = parents;
            }

            void AddLeaf(byte[] guid, int node)
            {
                var place = Guids.Length;
                for (var i = 0; i < Guids.Length; i++)
                {
                    if (CompareGuidValue(guid, Guids[i]) < 0)
                    {
                        place = i;
                        break;
                    }
                }

                var guids = new byte[Guids.Length + 1][];
                var leaves = new int[Leaves.Length + 1];
                Array.Copy(Guids, 0, guids, 0, place);
                Array.Copy(Guids, place, guids, place + 1, Guids.Length - place);
                Array.Copy(Leaves, 0, leaves, 0, place);
                Array.Copy(Leaves, place, leaves, place + 1, Leaves.Length - place);
                guids[place] = guid;
                leaves[place] = node;
                Guids = guids;
                Leaves = leaves;
                for (var i = 0; i < Nodes.Length; i++)
                {
                    if (i != node && Nodes[i].Count == 0 && Nodes[i].First >= place)
                    {
                        Nodes[i].First++;
                    }
                }

                Nodes[node].First = place;
                Nodes[node].Count = 0;
            }

            static int CompareGuidValue(byte[] left, byte[] right)
            {
                return new Guid(left).CompareTo(new Guid(right));
            }

            static int Shared(string edge, string rest)
            {
                var count = 0;
                while (count < edge.Length && count < rest.Length && KeyOf(edge[count]) == KeyOf(rest[count]))
                {
                    count++;
                }

                return count;
            }

            static byte KeyOf(char letter)
            {
                var lower = char.ToLowerInvariant(letter);
                return lower > 255 ? (byte)'?' : (byte)lower;
            }

            static int ReadX16(byte[] data, ref int cursor)
            {
                var raw = BitConverter.ToUInt16(data, cursor);
                cursor += 2;
                if ((raw & 0x8000) != 0)
                {
                    var high = BitConverter.ToUInt16(data, cursor);
                    cursor += 2;
                    return ((raw & 0x7FFF) << 16) | high;
                }

                return raw;
            }

            static byte[] ReadElems(byte[] data, ref int cursor, int size)
            {
                var raw = ReadX16(data, ref cursor);
                var count = raw >> 1;
                var width = BitConverter.ToUInt16(data, cursor);
                cursor += 2;
                if (width != size)
                {
                    throw new InvalidOperationException("The music name list could not be extended.");
                }

                var bytes = new byte[count * size];
                Buffer.BlockCopy(data, cursor, bytes, 0, bytes.Length);
                cursor += bytes.Length;
                return bytes;
            }

            static byte[] ReadBytes(byte[] data, ref int cursor)
            {
                var count = ReadX16(data, ref cursor);
                var bytes = new byte[count];
                Buffer.BlockCopy(data, cursor, bytes, 0, count);
                cursor += count;
                return bytes;
            }

            static int[] Read24(byte[] data, ref int cursor)
            {
                var count = ReadX16(data, ref cursor);
                var values = new int[count];
                for (var i = 0; i < count; i++)
                {
                    values[i] = data[cursor] | (data[cursor + 1] << 8) | (data[cursor + 2] << 16);
                    cursor += 3;
                }

                return values;
            }

            static void WriteX16(Stream stream, int value)
            {
                stream.Write(BitConverter.GetBytes((ushort)value), 0, 2);
            }

            static void WriteElems(Stream stream, byte[] records, int size)
            {
                var count = records.Length / size;
                WriteX16(stream, (count << 1) | 1);
                stream.Write(BitConverter.GetBytes((ushort)size), 0, 2);
                stream.Write(records, 0, records.Length);
            }

            static void WriteBytes(Stream stream, byte[] bytes)
            {
                WriteX16(stream, bytes.Length);
                stream.Write(bytes, 0, bytes.Length);
            }

            static void Write24(Stream stream, int[] values)
            {
                WriteX16(stream, values.Length);
                foreach (var value in values)
                {
                    stream.WriteByte((byte)value);
                    stream.WriteByte((byte)(value >> 8));
                    stream.WriteByte((byte)(value >> 16));
                }
            }
        }

        struct Node
        {
            public byte Key;
            public int Offset;
            public int Count;
            public int First;
        }
    }
}
