using System;
using System.IO;
using System.Text;

namespace Heat5Music
{
    internal static class MusicTags
    {
        public static (string Title, string Artist) Read(string path)
        {
            var title = Path.GetFileNameWithoutExtension(path);
            var artist = "";
            try
            {
                using var file = File.OpenRead(path);
                var head = new byte[10];
                if (file.Read(head, 0, 10) == 10 && head[0] == (byte)'I' && head[1] == (byte)'D' && head[2] == (byte)'3')
                {
                    var size = Sync(head, 6);
                    var data = new byte[Math.Min(size, 1024 * 256)];
                    var got = file.Read(data, 0, data.Length);
                    var version = head[3];
                    var i = 0;
                    while (i + 10 < got)
                    {
                        var id = Encoding.ASCII.GetString(data, i, 4);
                        if (id[0] == 0)
                        {
                            break;
                        }

                        var frame = version == 4 ? Sync(data, i + 4) : ReadInt(data, i + 4);
                        if (frame < 0 || i + 10 + frame > got)
                        {
                            break;
                        }

                        if (id is "TIT2" or "TPE1")
                        {
                            var text = Decode(data, i + 10, frame);
                            if (id == "TIT2" && !string.IsNullOrWhiteSpace(text))
                            {
                                title = text.Trim();
                            }

                            if (id == "TPE1" && !string.IsNullOrWhiteSpace(text))
                            {
                                artist = text.Trim();
                            }
                        }

                        i += 10 + frame;
                    }
                }
            }
            catch (IOException)
            {
            }

            return (title, artist);
        }

        private static int Sync(byte[] data, int at)
        {
            return (data[at] & 0x7F) << 21 | (data[at + 1] & 0x7F) << 14 | (data[at + 2] & 0x7F) << 7 | (data[at + 3] & 0x7F);
        }

        private static int ReadInt(byte[] data, int at)
        {
            return (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
        }

        private static string Decode(byte[] data, int at, int length)
        {
            if (length <= 1)
            {
                return "";
            }

            var encoding = data[at];
            var start = at + 1;
            var count = length - 1;
            while (count > 0 && data[start + count - 1] == 0)
            {
                count--;
            }

            if (encoding == 0 || encoding == 3)
            {
                return Encoding.UTF8.GetString(data, start, count);
            }

            if (count >= 2 && data[start] == 0xFF && data[start + 1] == 0xFE)
            {
                return Encoding.Unicode.GetString(data, start + 2, count - 2);
            }

            if (count >= 2 && data[start] == 0xFE && data[start + 1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(data, start + 2, count - 2);
            }

            return Encoding.Unicode.GetString(data, start, count - (count & 1));
        }
    }
}
