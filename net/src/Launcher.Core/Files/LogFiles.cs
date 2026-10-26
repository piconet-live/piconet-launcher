using System.Text;

namespace Launcher.Core;

/// <summary>The launcher's own logs (<c>launcher.log</c>; the 3D views' <c>gpu.log</c>): text added at the end of a
/// file the launcher writes and never reads. What a disk that takes no more raises is the caller's to catch: each
/// log has its own answer to it.</summary>
public static class LogFiles
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Adds the text at the log's end (UTF-8); the log is made when it is not there, its folder is not.</summary>
    public static void Append(string file, string text) => File.AppendAllText(file, text, Utf8);

    /// <summary>A log longer than <paramref name="most"/> bytes is set aside (".old": one earlier log is kept beside
    /// it), so that the next text begins a new one.</summary>
    public static void SetAsideWhenLong(string file, long most)
    {
        if (File.Exists(file) && new FileInfo(file).Length > most)
        {
            File.Move(file, file + ".old", overwrite: true);
        }
    }
}
