using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using Launcher.Core.Board;

namespace Launcher.Core;

/// <summary>The status board's file on the disk (<see cref="GameBoard"/>; protocol/formats.md section 8): made in
/// the launcher's own data folder, shared for reading and writing (the game opens it so), removed with its board, and
/// the boards whose launcher is gone removed when the next is made.</summary>
public static class BoardFile
{
    /// <summary>A new board file of <see cref="BoardLayout.Size"/> zeros in <paramref name="folder"/> (made if missing),
    /// mapped for reading and writing; the boards of launchers that are gone are removed first. The mapping is made
    /// from the file as opened here, shared: one made from the path would keep the game out.</summary>
    /// <exception cref="IOException">The folder or the file cannot be written.</exception>
    public static MemoryMappedFile Make(string folder, out string path)
    {
        ArgumentNullException.ThrowIfNull(folder);
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder, "*.board"))
        {
            try
            {
                if (!LauncherLives(old))
                {
                    File.Delete(old);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a game still has it open, or it cannot be read: it stays
            }
        }
        path = Path.Combine(folder, $"game-{Guid.NewGuid():N}.board");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        file.SetLength(BoardLayout.Size);
        return MemoryMappedFile.CreateFromFile(file, null, BoardLayout.Size, MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None, leaveOpen: false);
    }

    /// <summary>Removes a board's file; one a game still has open stays, for the next <see cref="Make"/>.</summary>
    public static void Remove(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the game still has it
        }
    }

    /// <summary>Whether the launcher that made a board still runs (by the process id its header holds).</summary>
    private static bool LauncherLives(string path)
    {
        var head = new byte[16];
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length)
            {
                return false;
            }
        }
        var pid = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(BoardLayout.HeaderLauncherPid));
        if (pid <= 0)
        {
            return false;
        }
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;   // no such process
        }
    }
}
