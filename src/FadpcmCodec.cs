using System;

namespace Heat5Music
{
    internal static class FadpcmCodec
    {
        public const int FrameBytes = 0x8C;
        public const int SamplesPerFrame = 256;

        private static readonly short[,] Coefs =
        {
            { 0, 0 },
            { 60, 0 },
            { 122, 60 },
            { 115, 52 },
            { 98, 55 },
            { 0, 0 },
            { 0, 0 }
        };

        public static byte[] EncodeStereo(short[] interleaved)
        {
            if (interleaved.Length < 2 || (interleaved.Length & 1) != 0)
            {
                throw new InvalidOperationException("That file has no audio.");
            }

            var count = interleaved.Length / 2;
            var left = new short[count];
            var right = new short[count];
            for (var i = 0; i < count; i++)
            {
                left[i] = interleaved[i * 2];
                right[i] = interleaved[(i * 2) + 1];
            }

            var a = EncodeChannel(left);
            var b = EncodeChannel(right);
            var frames = a.Length / FrameBytes;
            var mixed = new byte[frames * FrameBytes * 2];
            for (var f = 0; f < frames; f++)
            {
                Buffer.BlockCopy(a, f * FrameBytes, mixed, f * FrameBytes * 2, FrameBytes);
                Buffer.BlockCopy(b, f * FrameBytes, mixed, (f * FrameBytes * 2) + FrameBytes, FrameBytes);
            }

            var padded = (mixed.Length + 31) & ~31;
            if (padded != mixed.Length)
            {
                Array.Resize(ref mixed, padded);
            }

            return mixed;
        }

        public static short[] DecodeStereo(byte[] data, int samples)
        {
            var frames = (samples + (SamplesPerFrame - 1)) / SamplesPerFrame;
            var left = DecodeChannel(data, 0, frames, samples);
            var right = DecodeChannel(data, FrameBytes, frames, samples);
            var mixed = new short[samples * 2];
            for (var i = 0; i < samples; i++)
            {
                mixed[i * 2] = left[i];
                mixed[(i * 2) + 1] = right[i];
            }

            return mixed;
        }

        private static byte[] EncodeChannel(short[] pcm)
        {
            var frames = Math.Max(1, (pcm.Length + (SamplesPerFrame - 1)) / SamplesPerFrame);
            var output = new byte[frames * FrameBytes];
            var hist1 = 0;
            var hist2 = 0;
            var pos = 0;
            for (var f = 0; f < frames; f++)
            {
                var at = f * FrameBytes;
                WriteInt16(output, at + 8, hist1);
                WriteInt16(output, at + 10, hist2);
                var coefBits = 0;
                var shiftBits = 0;
                for (var g = 0; g < 8; g++)
                {
                    var take = Math.Max(0, Math.Min(32, pcm.Length - pos));
                    var block = new short[32];
                    for (var i = 0; i < take; i++)
                    {
                        block[i] = pcm[pos + i];
                    }

                    var bestCoef = 0;
                    var bestShift = 0;
                    long bestError = long.MaxValue;
                    for (var coef = 0; coef < 5; coef++)
                    {
                        var open = OpenLoopShift(block, take, hist1, hist2, coef);
                        var from = Math.Max(0, open - 1);
                        var to = Math.Min(12, open + 2);
                        for (var shift = from; shift <= to; shift++)
                        {
                            var error = Error(block, take, hist1, hist2, coef, shift, bestError);
                            if (error < bestError)
                            {
                                bestError = error;
                                bestCoef = coef;
                                bestShift = shift;
                            }
                        }
                    }

                    coefBits |= bestCoef << (g * 4);
                    shiftBits |= bestShift << (g * 4);
                    hist1 = WriteGroup(output, at + 0x0C + (0x10 * g), block, hist1, hist2, bestCoef, bestShift, out hist2);
                    pos += 32;
                }

                WriteInt32(output, at, coefBits);
                WriteInt32(output, at + 4, shiftBits);
            }

            return output;
        }

        private static int OpenLoopShift(short[] block, int take, int hist1, int hist2, int coef)
        {
            var c1 = Coefs[coef, 0];
            var c2 = Coefs[coef, 1];
            var max = 1;
            for (var i = 0; i < take; i++)
            {
                var predict = ((hist1 * c1) - (hist2 * c2)) >> 6;
                var residual = block[i] - predict;
                var abs = residual < 0 ? -residual : residual;
                if (abs > max)
                {
                    max = abs;
                }

                hist2 = hist1;
                hist1 = block[i];
            }

            var shift = 0;
            while (shift < 12 && (7 << shift) < max)
            {
                shift++;
            }

            return shift;
        }

        private static long Error(short[] block, int take, int hist1, int hist2, int coef, int shift, long limit)
        {
            long error = 0;
            var c1 = Coefs[coef, 0];
            var c2 = Coefs[coef, 1];
            for (var i = 0; i < take; i++)
            {
                BestNibble(block[i], hist1, hist2, c1, c2, shift, out var decoded);
                var diff = decoded - block[i];
                error += (long)diff * diff;
                if (error >= limit)
                {
                    return error;
                }

                hist2 = hist1;
                hist1 = decoded;
            }

            return error;
        }

        private static int WriteGroup(byte[] output, int at, short[] block, int hist1, int hist2, int coef, int shift, out int newHist2)
        {
            var c1 = Coefs[coef, 0];
            var c2 = Coefs[coef, 1];
            for (var j = 0; j < 4; j++)
            {
                var word = 0;
                for (var k = 0; k < 8; k++)
                {
                    var sample = block[(j * 8) + k];
                    var nibble = BestNibble(sample, hist1, hist2, c1, c2, shift, out var decoded);
                    word |= nibble << (k * 4);
                    hist2 = hist1;
                    hist1 = decoded;
                }

                WriteInt32(output, at + (j * 4), word);
            }

            newHist2 = hist2;
            return hist1;
        }

        private static int BestNibble(int target, int hist1, int hist2, int c1, int c2, int shiftNibble, out int decoded)
        {
            var shift = 22 - shiftNibble;
            var predict = ((hist1 * c1) - (hist2 * c2)) >> 6;
            var guess = target - predict;
            var n = shiftNibble <= 0
                ? guess
                : guess >= 0
                    ? (guess + (1 << (shiftNibble - 1))) >> shiftNibble
                    : -(((-guess) + (1 << (shiftNibble - 1))) >> shiftNibble);
            if (n > 7)
            {
                n = 7;
            }

            if (n < -8)
            {
                n = -8;
            }

            var best = 0;
            var bestErr = int.MaxValue;
            decoded = 0;
            for (var d = -1; d <= 1; d++)
            {
                var t = n + d;
                if (t < -8 || t > 7)
                {
                    continue;
                }

                var stored = t & 0xF;
                var scaled = (stored << 28) >> shift;
                var sample = (scaled - (hist2 * c2) + (hist1 * c1)) >> 6;
                if (sample > 32767)
                {
                    sample = 32767;
                }

                if (sample < -32768)
                {
                    sample = -32768;
                }

                var err = Math.Abs(sample - target);
                if (err < bestErr)
                {
                    bestErr = err;
                    best = stored;
                    decoded = sample;
                }
            }

            return best;
        }

        private static short[] DecodeChannel(byte[] data, int channelOffset, int frames, int samples)
        {
            var pcm = new short[samples];
            var written = 0;
            for (var f = 0; f < frames && written < samples; f++)
            {
                var at = channelOffset + (f * FrameBytes * 2);
                var coefs = BitConverter.ToInt32(data, at);
                var shifts = BitConverter.ToInt32(data, at + 4);
                var hist1 = (int)BitConverter.ToInt16(data, at + 8);
                var hist2 = (int)BitConverter.ToInt16(data, at + 10);
                for (var g = 0; g < 8 && written < samples; g++)
                {
                    var index = ((coefs >> (g * 4)) & 0x0F) % 7;
                    var shiftNibble = (shifts >> (g * 4)) & 0x0F;
                    var shift = 22 - shiftNibble;
                    var c1 = Coefs[index, 0];
                    var c2 = Coefs[index, 1];
                    for (var j = 0; j < 4 && written < samples; j++)
                    {
                        var nibbles = BitConverter.ToUInt32(data, at + 0x0C + (0x10 * g) + (4 * j));
                        for (var k = 0; k < 8 && written < samples; k++)
                        {
                            var nibble = (int)((nibbles >> (k * 4)) & 0x0F);
                            var sample = (nibble << 28) >> shift;
                            sample = (sample - (hist2 * c2) + (hist1 * c1)) >> 6;
                            if (sample > 32767)
                            {
                                sample = 32767;
                            }

                            if (sample < -32768)
                            {
                                sample = -32768;
                            }

                            pcm[written++] = (short)sample;
                            hist2 = hist1;
                            hist1 = sample;
                        }
                    }
                }
            }

            return pcm;
        }

        private static void WriteInt16(byte[] data, int at, int value)
        {
            var bytes = BitConverter.GetBytes((short)value);
            data[at] = bytes[0];
            data[at + 1] = bytes[1];
        }

        private static void WriteInt32(byte[] data, int at, int value)
        {
            var bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, data, at, 4);
        }
    }
}
