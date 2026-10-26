using System.Buffers.Binary;

namespace Launcher.Core;

/// <summary>
/// The launcher's sounds as wave files (RIFF, PCM, mono, 16 bit): what the system plays from memory, and what the
/// sounds' cache keeps (<see cref="GameSounds"/>). <see cref="Read"/> takes back only what <see cref="Make"/> writes.
/// </summary>
public static class WaveFile
{
    /// <summary>The samples as a wave file's bytes at <paramref name="rate"/> samples a second.</summary>
    public static byte[] Make(ReadOnlySpan<short> samples, int rate)
    {
        var data = samples.Length * 2;
        var file = new byte[44 + data];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 36 + data);
        "WAVEfmt "u8.CopyTo(file.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), 16);          // the format's length
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(20), 1);           // PCM
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(22), 1);           // mono
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(28), rate * 2);    // bytes a second
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(32), 2);           // bytes a sample
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(34), 16);
        "data"u8.CopyTo(file.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(40), data);
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(44 + (2 * i)), samples[i]);
        }
        return file;
    }

    /// <summary>A wave file as <see cref="Make"/> writes it: its rate and samples; null for anything else (another
    /// layout, a file cut short, a rate no sound of the launcher has).</summary>
    public static (int Rate, short[] Samples)? Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 44 || !file[..4].SequenceEqual("RIFF"u8) || !file[8..16].SequenceEqual("WAVEfmt "u8)
            || !file[36..40].SequenceEqual("data"u8)
            || BinaryPrimitives.ReadInt32LittleEndian(file[16..]) != 16 || BinaryPrimitives.ReadInt16LittleEndian(file[20..]) != 1
            || BinaryPrimitives.ReadInt16LittleEndian(file[22..]) != 1 || BinaryPrimitives.ReadInt16LittleEndian(file[34..]) != 16)
        {
            return null;
        }
        var rate = BinaryPrimitives.ReadInt32LittleEndian(file[24..]);
        var data = BinaryPrimitives.ReadInt32LittleEndian(file[40..]);
        if (rate is < 1000 or > 96000 || data < 0 || data % 2 != 0 || data != file.Length - 44)
        {
            return null;
        }
        var samples = new short[data / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(file[(44 + (2 * i))..]);
        }
        return (rate, samples);
    }
}
