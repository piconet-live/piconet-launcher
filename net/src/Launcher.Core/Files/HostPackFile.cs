using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// A custom lobby host's own stat pack: a <c>.fzstats</c> file they chose, its pack's own name and the file's text
/// whole. The text goes to the server with the lobby's rules (<see cref="LobbyRules.PackFile"/>), which reads and
/// checks it as it would a pack file of its own, and hands the pack to every launcher with the race. The launcher
/// reads it first with the same reader (<see cref="StatPackText.Read"/>), so that a file that is no pack is said at
/// once, beside the rule.
/// </summary>
public sealed record HostPackFile(string Name, string Text)
{
    /// <summary>The file's pack; or, in a line for the player, why it is none.</summary>
    public static (HostPackFile? File, string? Problem) Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string text;
        try
        {
            // (a character is four bytes at most: a longer file is too long, and is not read to find that out)
            if (new FileInfo(path).Length > LobbyRules.MostPackFile * 4L)
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

    /// <summary>A file's text as a host's pack; or, in a line for the player, why it is none.</summary>
    public static (HostPackFile? File, string? Problem) Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > LobbyRules.MostPackFile)
        {
            return (null, TooLong);
        }
        var (pack, refusal) = StatPackText.Read(text);
        return pack is null ? (null, $"Not a stat pack: {refusal}") : (new HostPackFile(pack.Name, text), null);
    }

    private const string TooLong = "That file is too big for a stat pack.";
}
