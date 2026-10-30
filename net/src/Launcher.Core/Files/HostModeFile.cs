using System.Text;
using System.Text.Json;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// A custom lobby host's own game mode: a <c>.fzmode</c> file they chose (piconet-docs research/game-modes/), the
/// mode's name and badge as its header says them, and the file's text whole. The text goes to the server with the
/// lobby's rules (<see cref="LobbyRules.ModeFile"/>), which compiles it with the same compiler and sends the package
/// to every member with the race. The launcher compiles it first (<see cref="ModeCompiler"/>), so a file that does not
/// compile is said at once, beside the rule, with its line.
/// </summary>
public sealed record HostModeFile(string Name, string Badge, string Text, ModeTerms Terms)
{
    /// <summary>The extension of a mode file.</summary>
    public const string Extension = ".fzmode";

    /// <summary>The file's mode; or, in a line for the player, why it is none.</summary>
    public static (HostModeFile? File, string? Problem) Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string text;
        try
        {
            // (a character is four bytes at most: a longer file is too long, and is not read to find that out)
            if (new FileInfo(path).Length > LobbyRules.MostModeFile * 4L)
            {
                return (null, TooLong);
            }
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or System.Security.SecurityException)
        {
            return (null, "That file can't be read.");
        }
        return Of(text);
    }

    /// <summary>A file's text as a host's mode; or, in a line for the player, why it is none: too long, or the
    /// compiler's first error ("line 5: ...").</summary>
    public static (HostModeFile? File, string? Problem) Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > LobbyRules.MostModeFile)
        {
            return (null, TooLong);
        }
        // (as the rules' JSON a character outside ASCII and a few others are six bytes each: as a pack file, the
        // server takes at most twice the longest, HostPackFile.MostSentBytes)
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(text, ControlProtocol.Json)) > HostPackFile.MostSentBytes)
        {
            return (null, TooLong);
        }
        var compiled = ModeCompiler.Compile(text);
        return compiled.Mode is { } mode
            ? (new HostModeFile(mode.Name, mode.Badge, text, ModeTerms.Of(mode)), null)
            : (null, $"Not a mode: {compiled.Errors[0]}");
    }

    private const string TooLong = "That file is too big for a mode.";
}
