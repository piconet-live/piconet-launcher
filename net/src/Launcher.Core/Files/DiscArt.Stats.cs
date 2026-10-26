using System.Buffers.Binary;
using System.Security.Cryptography;
using Fzgx.GameCube.Compression;

namespace Launcher.Core;

// The machines' stats, read from the disc as its pictures are: through the bounded reader, no file longer than
// LargestFile, none unpacked that names more than LargestUnpacked, and nothing raised on the disc's bytes.
public sealed partial class DiscArt
{
    /// <summary>Every machine's stats from the disc (<see cref="MachineStatFile"/>): the numbers from the car data
    /// file, and the machine select's letters from the main module when it is the pinned disc's. Null when the car
    /// data is missing or is not the game's table; a table without letters when only the main module is not.</summary>
    public MachineStatTable? MachineStats() => MachineStats(DiscPins.Game);

    /// <param name="pins">Which disc's main module the letters are read from: its size and hash.</param>
    public MachineStatTable? MachineStats(DiscPins pins)
    {
        ArgumentNullException.ThrowIfNull(pins);
        var numbers = Read(MachineStatFile.CarData) is { } data ? MachineStatFile.Numbers(data) : null;
        return numbers is null ? null : MachineStatFile.Table(numbers, Ratings(pins));
    }

    /// <summary>Whether every file of the image is the pinned game's: the one digest over all of them that the
    /// preparation holds a disc to (<see cref="DiscPrep.ContentDigest"/>, <see cref="DiscPins.ContentSha256"/>).
    /// The whole image is read (1.4 GB: some seconds). False for an image with a changed file, another region's, or
    /// one that cannot be read to its end.</summary>
    public bool IsUnchanged(DiscPins pins)
    {
        ArgumentNullException.ThrowIfNull(pins);
        lock (_lock)
        {
            try
            {
                var hashes = new List<(string Path, long Size, string Sha256)>(_table.Count);
                var buffer = new byte[1 << 20];
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var file in _table.Values.OrderBy(f => f.Offset))
                {
                    for (long at = 0; at < file.Length;)
                    {
                        var piece = buffer.AsSpan(0, (int)Math.Min(buffer.Length, file.Length - at));
                        _image.Read(file.Offset + at, piece, file.Path);
                        hash.AppendData(piece);
                        at += piece.Length;
                    }
                    hashes.Add((file.Path, file.Length, Convert.ToHexStringLower(hash.GetHashAndReset())));
                }
                return DiscPrep.ContentDigest(hashes).Equals(pins.ContentSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (Unreadable(e))
            {
                return false;
            }
        }
    }

    /// <summary>The disc's car data file, unpacked (<see cref="MachineStatFile"/>): what a stat pack's import
    /// compares a changed file with. Null when the disc has none or it cannot be read.</summary>
    public byte[]? CarData() => Read(MachineStatFile.CarData);

    /// <summary>The four stock colours of each of the 75 parts (<see cref="PartColours"/>), from the main module when
    /// it is the one the pins name; null otherwise, or when the bytes there are not the game's table.</summary>
    public uint[][]? PartColours() => PartColours(DiscPins.Game);

    /// <param name="pins">Which disc's main module the colours are read from: its size and hash.</param>
    public uint[][]? PartColours(DiscPins pins)
    {
        ArgumentNullException.ThrowIfNull(pins);
        return MainModule(pins) is { } module ? Launcher.Core.PartColours.Read(module) : null;
    }

    /// <summary>The letters, from the main module when it is the one the pins name: where they stand is known for
    /// that file only, so another file's bytes there are not taken for them. Null otherwise.</summary>
    private int[][]? Ratings(DiscPins pins) => MainModule(pins) is { } module ? MachineStatFile.Ratings(module) : null;

    /// <summary>The custom parts' naming tables (<see cref="CustomPartFile.Tables"/>), from the main module when it
    /// is the one the pins name, as the letters are read; null otherwise.</summary>
    public CustomPartTables? CustomParts() => CustomParts(DiscPins.Game);

    /// <param name="pins">Which disc's main module the tables are read from: its size and hash.</param>
    public CustomPartTables? CustomParts(DiscPins pins)
    {
        ArgumentNullException.ThrowIfNull(pins);
        return MainModule(pins) is { } module ? CustomPartFile.Tables(module) : null;
    }

    /// <summary>Each custom part's letter (<see cref="CustomPartFile.Grades"/>), from the main module when it is the
    /// one the pins name, as the stock machines' letters are read; null otherwise.</summary>
    public int[]? CustomGrades() => MainModule(DiscPins.Game) is { } module ? CustomPartFile.Grades(module) : null;

    /// <summary>The main module decrypted and unpacked, when the disc's file is the one the pins name: what a stat
    /// pack's import compares a modified module with (<see cref="StatPackImport"/>). Null otherwise.</summary>
    public byte[]? MainModuleOf(DiscPins pins) => MainModule(pins);

    /// <summary>The main module decrypted and unpacked, when the disc's file is the one the pins name (its size and
    /// SHA-256): the places read from it are that file's. Null otherwise, or when it cannot be read.</summary>
    private byte[]? MainModule(DiscPins pins)
    {
        lock (_lock)
        {
            try
            {
                if (!_table.TryGetValue(MachineStatFile.MainModule, out var entry)
                    || !pins.Named.TryGetValue("files/" + MachineStatFile.MainModule, out var pin) || entry.Length != pin.Size)
                {
                    return null;
                }
                var file = ReadFile(MachineStatFile.MainModule);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(file)), pin.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                var packed = MachineStatFile.Decrypted(file);
                return packed.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(4)) is < 0 or > LargestUnpacked
                    ? null : AvLz.Decompress(packed);
            }
            catch (Exception e) when (Unreadable(e))
            {
                return null;
            }
        }
    }
}
