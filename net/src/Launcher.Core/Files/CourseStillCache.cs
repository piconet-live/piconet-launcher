using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core;

/// <summary>
/// Where the tiles' stills are kept: <c>cache/courses/DISC/NN.png</c> in the data folder, DISC the disc's key
/// (<see cref="DiscKey"/>), NN the course's number. A still is drawn once per course and disc; another look of the
/// stills (<see cref="Recipe"/>) or another size is another key, so an old picture is never shown for a new look.
/// </summary>
public static class CourseStillCache
{
    /// <summary>The stills' look: raised whenever the camera, the sky or the tint changes.</summary>
    public const int Recipe = 1;

    /// <summary>The folder for the disc's stills.</summary>
    public static string Folder(string dataFolder, string discKey) => Path.Combine(dataFolder, "cache", "courses", discKey);

    /// <summary>A course's still in that folder: <c>NN.png</c>.</summary>
    public static string File(string folder, int course) =>
        Path.Combine(folder, course.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ".png");

    /// <summary>The disc's key: 16 hexadecimal characters of a SHA-256 over the course models' files as the disc
    /// holds them (<paramref name="files"/>: name and bytes, in any order), the <see cref="Recipe"/> and the still's
    /// size. The same disc gives the same key whatever image file it is read from.</summary>
    public static string DiscKey(IEnumerable<(string Name, byte[] Bytes)> files, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes($"course stills {Recipe} {width}x{height}\n"));
        foreach (var (name, bytes) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.ASCII.GetBytes($"{name} {bytes.Length}\n"));
            hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    /// <summary>The largest still read back: 4 MiB (one of 320 by 144 pixels is tens of kilobytes).</summary>
    private const int Largest = 4 << 20;

    /// <summary>A kept still's bytes; null when there is none, or it cannot be read, or it is not of a still's size
    /// (then it is drawn again).</summary>
    public static byte[]? Read(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists && info.Length is > 0 and <= Largest ? System.IO.File.ReadAllBytes(file) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps a still's PNG bytes: written beside it, then moved in, so that a still is whole or not there.
    /// One that cannot be written is drawn again next time.</summary>
    public static void Keep(string file, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Online.Protocol.AtomicFile.WriteAllBytes(file, png);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // drawn again next time
        }
    }
}
