using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Heat5Music
{
    internal static class MusicMeta
    {
        public static List<(string Title, string Artist)> Read(string metaPath)
        {
            var songs = new List<(string Title, string Artist)>();
            var props = Find(Parse(File.ReadAllBytes(metaPath)));
            string artist = "";
            foreach (var prop in props)
            {
                if (prop.Name == "Artist")
                {
                    artist = prop.Value;
                }
                else if (prop.Name == "SongTitle")
                {
                    songs.Add((prop.Value, artist));
                    artist = "";
                }
            }

            return songs;
        }

        public static int[] SampleMap(string metaPath, string streamsPath)
        {
            var meta = File.ReadAllBytes(metaPath);
            var sampleGuids = ReadSampleGuids(streamsPath);
            var events = new List<string>();
            var timelines = new List<(string Timeline, string EventGuid)>();
            var waves = new List<(string Timeline, int Sample)>();
            var pendingSample = -1;
            var cursor = 0;
            while (cursor + 8 <= meta.Length)
            {
                var id = Encoding.ASCII.GetString(meta, cursor, 4);
                var size = BitConverter.ToInt32(meta, cursor + 4);
                if (size < 0 || cursor + 8 + size > meta.Length)
                {
                    break;
                }

                var payload = cursor + 8;
                if (id is "RIFF" or "LIST")
                {
                    cursor += 12;
                    continue;
                }

                if (id == "EVTB" && size >= 16)
                {
                    events.Add(Convert.ToHexString(meta, payload, 16));
                }
                else if (id == "TLNB" && size >= 32)
                {
                    timelines.Add((Convert.ToHexString(meta, payload, 16), Convert.ToHexString(meta, payload + 16, 16)));
                }
                else if (id == "WAIB" && size >= 32)
                {
                    pendingSample = sampleGuids.IndexOf(Convert.ToHexString(meta, payload + 16, 16));
                }
                else if (id == "INST" && size >= 16 && pendingSample >= 0)
                {
                    waves.Add((Convert.ToHexString(meta, payload, 16), pendingSample));
                    pendingSample = -1;
                }

                cursor += 8 + size + (size & 1);
            }

            var map = new int[events.Count];
            for (var i = 0; i < events.Count; i++)
            {
                var timeline = "";
                foreach (var item in timelines)
                {
                    if (item.EventGuid == events[i])
                    {
                        timeline = item.Timeline;
                        break;
                    }
                }

                var sample = -1;
                foreach (var wave in waves)
                {
                    if (wave.Timeline == timeline && timeline.Length > 0)
                    {
                        sample = wave.Sample;
                        break;
                    }
                }

                map[i] = sample;
            }

            return map;
        }

        private static List<string> ReadSampleGuids(string streamsPath)
        {
            var streams = new byte[65536];
            using (var file = File.OpenRead(streamsPath))
            {
                var read = file.Read(streams, 0, streams.Length);
                if (read != streams.Length)
                {
                    Array.Resize(ref streams, read);
                }
            }

            var end = streams.Length;
            for (var i = 0; i < streams.Length - 4; i++)
            {
                if (streams[i] == (byte)'F' && streams[i + 1] == (byte)'S' && streams[i + 2] == (byte)'B' && streams[i + 3] == (byte)'5')
                {
                    end = i;
                    break;
                }
            }

            var sampleGuids = new List<string>();
            for (var i = 0; i < end - 30; i++)
            {
                if (streams[i] != 0x57 || streams[i + 1] != 0x41 || streams[i + 2] != 0x56 || streams[i + 3] != 0x20)
                {
                    continue;
                }

                if (BitConverter.ToInt32(streams, i + 4) != 0x1E)
                {
                    continue;
                }

                sampleGuids.Add(Convert.ToHexString(streams, i + 8, 16));
                i += 8;
            }

            return sampleGuids;
        }

        public static byte[] Rewrite(byte[] bank, IReadOnlyList<string?> titles, IReadOnlyList<string?> artists)
        {
            var root = Parse(bank);
            var props = Find(root);
            var edits = new List<Prop>();
            var song = 0;
            Prop? artist = null;
            foreach (var prop in props)
            {
                if (prop.Name == "Artist")
                {
                    artist = prop;
                }
                else if (prop.Name == "SongTitle")
                {
                    if (song < titles.Count && titles[song] != null)
                    {
                        prop.Value = titles[song] ?? "";
                        edits.Add(prop);
                        if (artist != null && artists[song] != null)
                        {
                            artist.Value = artists[song] ?? "";
                            edits.Add(artist);
                        }
                    }

                    song++;
                    artist = null;
                }
            }

            var chunks = new Dictionary<Chunk, List<Prop>>();
            foreach (var edit in edits)
            {
                if (!chunks.TryGetValue(edit.Owner, out var list))
                {
                    list = new List<Prop>();
                    chunks.Add(edit.Owner, list);
                }

                list.Add(edit);
            }

            foreach (var pair in chunks)
            {
                pair.Value.Sort((a, b) => b.At.CompareTo(a.At));
                foreach (var edit in pair.Value)
                {
                    pair.Key.Payload = ReplaceValue(pair.Key.Payload!, edit.At, edit.Value);
                }
            }

            return Write(root);
        }

        private sealed class Chunk
        {
            public string Id = "";
            public string? ListType;
            public byte[]? Payload;
            public List<Chunk>? Children;
        }

        private sealed class Prop
        {
            public Chunk Owner = null!;
            public int At;
            public string Name = "";
            public string Value = "";
        }

        private static Chunk Parse(byte[] data)
        {
            var p = 0;
            return ReadChunk(data, ref p);
        }

        private static Chunk ReadChunk(byte[] data, ref int p)
        {
            var chunk = new Chunk { Id = Encoding.ASCII.GetString(data, p, 4) };
            var size = BitConverter.ToInt32(data, p + 4);
            p += 8;
            if (chunk.Id is "RIFF" or "LIST")
            {
                chunk.ListType = Encoding.ASCII.GetString(data, p, 4);
                p += 4;
                var end = p + size - 4;
                chunk.Children = new List<Chunk>();
                while (p + 8 <= end)
                {
                    chunk.Children.Add(ReadChunk(data, ref p));
                }

                p = end;
            }
            else
            {
                chunk.Payload = new byte[size];
                Buffer.BlockCopy(data, p, chunk.Payload, 0, size);
                p += size;
            }

            if ((size & 1) == 1 && p < data.Length)
            {
                p++;
            }

            return chunk;
        }

        private static List<Prop> Find(Chunk root)
        {
            var found = new List<Prop>();
            Walk(root, found);
            return found;
        }

        private static void Walk(Chunk chunk, List<Prop> found)
        {
            if (chunk.Children != null)
            {
                foreach (var child in chunk.Children)
                {
                    Walk(child, found);
                }

                return;
            }

            var data = chunk.Payload;
            if (data == null)
            {
                return;
            }

            for (var i = 0; i + 4 < data.Length; i++)
            {
                var size = BitConverter.ToUInt16(data, i);
                var nameLen = BitConverter.ToUInt16(data, i + 2);
                if (nameLen == 0 || nameLen > 32 || i + 4 + nameLen + 2 > data.Length)
                {
                    continue;
                }

                var name = Encoding.ASCII.GetString(data, i + 4, nameLen);
                if (name is not ("SongTitle" or "Artist" or "AlbumTitle" or "Copyright"))
                {
                    continue;
                }

                var valueAt = i + 4 + nameLen;
                var valueLen = BitConverter.ToUInt16(data, valueAt);
                if (valueAt + 2 + valueLen > data.Length)
                {
                    continue;
                }

                if (size != 2 + nameLen + 2 + valueLen)
                {
                    continue;
                }

                found.Add(new Prop
                {
                    Owner = chunk,
                    At = i,
                    Name = name,
                    Value = Encoding.UTF8.GetString(data, valueAt + 2, valueLen)
                });
                i += 1 + size;
            }
        }

        private static byte[] ReplaceValue(byte[] payload, int at, string value)
        {
            var nameLen = BitConverter.ToUInt16(payload, at + 2);
            var valueAt = at + 4 + nameLen;
            var valueLen = BitConverter.ToUInt16(payload, valueAt);
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > 80)
            {
                throw new InvalidOperationException("Keep the song name and artist under 80 characters.");
            }

            var updated = new byte[payload.Length - valueLen + bytes.Length];
            Buffer.BlockCopy(payload, 0, updated, 0, valueAt + 2);
            Buffer.BlockCopy(bytes, 0, updated, valueAt + 2, bytes.Length);
            Buffer.BlockCopy(payload, valueAt + 2 + valueLen, updated, valueAt + 2 + bytes.Length, payload.Length - (valueAt + 2 + valueLen));
            var size = (ushort)(2 + nameLen + 2 + bytes.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(size), 0, updated, at, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)bytes.Length), 0, updated, valueAt, 2);
            return updated;
        }

        private static byte[] Write(Chunk root)
        {
            using var stream = new MemoryStream();
            WriteChunk(stream, root);
            return stream.ToArray();
        }

        private static void WriteChunk(Stream stream, Chunk chunk)
        {
            byte[] body;
            if (chunk.Children != null)
            {
                using var inner = new MemoryStream();
                var type = Encoding.ASCII.GetBytes(chunk.ListType ?? "    ");
                inner.Write(type, 0, 4);
                foreach (var child in chunk.Children)
                {
                    WriteChunk(inner, child);
                }

                body = inner.ToArray();
            }
            else
            {
                body = chunk.Payload ?? Array.Empty<byte>();
            }

            var id = Encoding.ASCII.GetBytes(chunk.Id);
            stream.Write(id, 0, 4);
            var size = BitConverter.GetBytes(body.Length);
            stream.Write(size, 0, 4);
            stream.Write(body, 0, body.Length);
            if ((body.Length & 1) == 1)
            {
                stream.WriteByte(0);
            }
        }
    }
}
