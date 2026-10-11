using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Heat5Music;

internal static class FmodPcm
{
    const int Ok = 0;
    const uint OpenMemory = 0x00000800;
    const uint CreateStream = 0x00000080;
    const uint AccurateTime = 0x00004000;
    const uint TimePcm = 0x00000002;

    const int FormatPcm16 = 2;
    const int FormatPcmFloat = 5;

    delegate int CreateSystem(out IntPtr system);
    delegate int SystemInit(IntPtr system, int channels, uint flags, IntPtr extra);
    delegate int SystemClose(IntPtr system);
    delegate int SystemRelease(IntPtr system);
    delegate int CreateSound(IntPtr system, IntPtr data, uint mode, IntPtr exinfo, out IntPtr sound);
    delegate int GetNumSubSounds(IntPtr sound, out int count);
    delegate int GetSubSound(IntPtr sound, int index, out IntPtr sub);
    delegate int GetLength(IntPtr sound, out uint length, uint unit);
    delegate int GetFormat(IntPtr sound, IntPtr type, out int format, out int channels, out int bits);
    delegate int SeekData(IntPtr sound, uint pcm);
    delegate int ReadData(IntPtr sound, IntPtr buffer, uint length, out uint read);
    delegate int ReleaseSound(IntPtr sound);

    static IntPtr _lib;
    static CreateSystem? _create;
    static SystemInit? _init;
    static SystemClose? _close;
    static SystemRelease? _releaseSystem;
    static CreateSound? _createSound;
    static GetNumSubSounds? _numSubs;
    static GetSubSound? _subSound;
    static GetLength? _length;
    static GetFormat? _format;
    static SeekData? _seek;
    static ReadData? _read;
    static ReleaseSound? _releaseSound;

    public static short[] ReadStereo48(string gameRoot, byte[] bank, int fsbAt, int fsbLength, int sampleIndex, int sourceRate)
    {
        var all = ReadAll(gameRoot, bank, fsbAt, fsbLength, new[] { sampleIndex }, new[] { sourceRate });
        return all[0];
    }

    public static short[][] ReadAll(string gameRoot, byte[] bank, int fsbAt, int fsbLength, int[] indexes, int[] sourceRates)
    {
        Ensure(gameRoot);
        if (fsbAt < 0 || fsbLength <= 0 || fsbAt + fsbLength > bank.Length)
        {
            throw new InvalidOperationException("Music bank was not recognized.");
        }

        var slice = new byte[fsbLength];
        Buffer.BlockCopy(bank, fsbAt, slice, 0, fsbLength);
        var pin = GCHandle.Alloc(slice, GCHandleType.Pinned);
        IntPtr system = 0;
        IntPtr sound = 0;
        try
        {
            Check(_create!(out system), "Heat 5's audio library did not start.");
            Check(_init!(system, 32, 0, 0), "Heat 5's audio library did not start.");
            var info = new byte[8];
            BitConverter.GetBytes(8).CopyTo(info, 0);
            BitConverter.GetBytes(slice.Length).CopyTo(info, 4);
            var infoPin = GCHandle.Alloc(info, GCHandleType.Pinned);
            try
            {
                var mode = OpenMemory | CreateStream | AccurateTime;
                Check(_createSound!(system, pin.AddrOfPinnedObject(), mode, infoPin.AddrOfPinnedObject(), out sound), "That soundtrack could not be opened.");
            }
            finally
            {
                infoPin.Free();
            }

            Check(_numSubs!(sound, out var count), "That soundtrack could not be opened.");
            var songs = new short[indexes.Length][];
            for (var n = 0; n < indexes.Length; n++)
            {
                var sampleIndex = indexes[n];
                var target = sound;
                if (count > 0)
                {
                    if (sampleIndex < 0 || sampleIndex >= count)
                    {
                        throw new InvalidOperationException("That song has no audio.");
                    }

                    Check(_subSound!(sound, sampleIndex, out target), "That song has no audio.");
                }

                songs[n] = ReadTarget(target, sourceRates[n]);
            }

            return songs;
        }
        finally
        {
            if (sound != 0)
            {
                _releaseSound!(sound);
            }

            if (system != 0)
            {
                _close!(system);
                _releaseSystem!(system);
            }

            pin.Free();
        }
    }

    static short[] ReadTarget(IntPtr target, int sourceRate)
    {
        Check(_format!(target, 0, out var format, out var channels, out _), "That song has no audio.");
        Check(_length!(target, out var frames, TimePcm), "That song has no audio.");
        if (frames == 0 || channels < 1)
        {
            throw new InvalidOperationException("That song has no audio.");
        }

        Check(_seek!(target, 0), "That song has no audio.");
        var bytesPerFrame = format == FormatPcmFloat ? channels * 4 : channels * 2;
        var raw = new byte[frames * bytesPerFrame];
        var filled = 0;
        var rawPin = GCHandle.Alloc(raw, GCHandleType.Pinned);
        try
        {
            while (filled < raw.Length)
            {
                var want = (uint)Math.Min(raw.Length - filled, 256 * 1024);
                Check(_read!(target, rawPin.AddrOfPinnedObject() + filled, want, out var got), "That song has no audio.");
                if (got == 0)
                {
                    break;
                }

                filled += (int)got;
            }
        }
        finally
        {
            rawPin.Free();
        }

        if (filled < raw.Length)
        {
            Array.Resize(ref raw, filled);
        }

        return ToStereo48(raw, format, channels, sourceRate);
    }

    static short[] ToStereo48(byte[] raw, int format, int channels, int rate)
    {
        short[] interleaved;
        if (format == FormatPcmFloat)
        {
            var floats = raw.Length / 4;
            interleaved = new short[floats];
            for (var i = 0; i < floats; i++)
            {
                var sample = BitConverter.ToSingle(raw, i * 4);
                if (sample > 1f) sample = 1f;
                if (sample < -1f) sample = -1f;
                interleaved[i] = (short)Math.Round(sample * 32767f);
            }
        }
        else if (format == FormatPcm16)
        {
            interleaved = new short[raw.Length / 2];
            Buffer.BlockCopy(raw, 0, interleaved, 0, interleaved.Length * 2);
        }
        else
        {
            throw new InvalidOperationException("That song has no audio.");
        }

        if (channels == 1)
        {
            var stereo = new short[interleaved.Length * 2];
            for (var i = 0; i < interleaved.Length; i++)
            {
                stereo[i * 2] = interleaved[i];
                stereo[i * 2 + 1] = interleaved[i];
            }

            interleaved = stereo;
            channels = 2;
        }

        if (channels != 2)
        {
            throw new InvalidOperationException("That song has no audio.");
        }

        if (rate == 48000)
        {
            return interleaved;
        }

        var bytes = new byte[interleaved.Length * 2];
        Buffer.BlockCopy(interleaved, 0, bytes, 0, bytes.Length);
        using var source = new RawSourceWaveStream(new MemoryStream(bytes), new WaveFormat(rate, 16, 2));
        ISampleProvider samples = source.ToSampleProvider();
        samples = new WdlResamplingSampleProvider(samples, 48000);
        var floatsOut = new List<float>();
        var buf = new float[48000];
        int read;
        while ((read = samples.Read(buf, 0, buf.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                floatsOut.Add(buf[i]);
            }
        }

        var pcm = new short[floatsOut.Count];
        for (var i = 0; i < pcm.Length; i++)
        {
            var sample = floatsOut[i];
            if (sample > 1f) sample = 1f;
            if (sample < -1f) sample = -1f;
            pcm[i] = (short)Math.Round(sample * 32767f);
        }

        return pcm;
    }

    static void Ensure(string gameRoot)
    {
        if (_create != null)
        {
            return;
        }

        var path = Path.Combine(gameRoot, "NASCARHeat5_Data", "Plugins", "fmodstudio.dll");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Heat 5's audio library was not found in that folder.");
        }

        _lib = NativeLibrary.Load(path);
        _create = Marshal.GetDelegateForFunctionPointer<CreateSystem>(NativeLibrary.GetExport(_lib, "FMOD_System_Create"));
        _init = Marshal.GetDelegateForFunctionPointer<SystemInit>(NativeLibrary.GetExport(_lib, "FMOD_System_Init"));
        _close = Marshal.GetDelegateForFunctionPointer<SystemClose>(NativeLibrary.GetExport(_lib, "FMOD_System_Close"));
        _releaseSystem = Marshal.GetDelegateForFunctionPointer<SystemRelease>(NativeLibrary.GetExport(_lib, "FMOD_System_Release"));
        _createSound = Marshal.GetDelegateForFunctionPointer<CreateSound>(NativeLibrary.GetExport(_lib, "FMOD_System_CreateSound"));
        _numSubs = Marshal.GetDelegateForFunctionPointer<GetNumSubSounds>(NativeLibrary.GetExport(_lib, "FMOD_Sound_GetNumSubSounds"));
        _subSound = Marshal.GetDelegateForFunctionPointer<GetSubSound>(NativeLibrary.GetExport(_lib, "FMOD_Sound_GetSubSound"));
        _length = Marshal.GetDelegateForFunctionPointer<GetLength>(NativeLibrary.GetExport(_lib, "FMOD_Sound_GetLength"));
        _format = Marshal.GetDelegateForFunctionPointer<GetFormat>(NativeLibrary.GetExport(_lib, "FMOD_Sound_GetFormat"));
        _seek = Marshal.GetDelegateForFunctionPointer<SeekData>(NativeLibrary.GetExport(_lib, "FMOD_Sound_SeekData"));
        _read = Marshal.GetDelegateForFunctionPointer<ReadData>(NativeLibrary.GetExport(_lib, "FMOD_Sound_ReadData"));
        _releaseSound = Marshal.GetDelegateForFunctionPointer<ReleaseSound>(NativeLibrary.GetExport(_lib, "FMOD_Sound_Release"));
    }

    static void Check(int result, string message)
    {
        if (result != Ok)
        {
            throw new InvalidOperationException(message + " (" + result + ")");
        }
    }
}
