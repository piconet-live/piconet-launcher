using Online.Protocol.Chat;

namespace Launcher.Core;

/// <summary>
/// A chat line's text with the swear words masked (****), for a player who hides them
/// (<see cref="LauncherSettings.HideSwearWords"/>): the words of <see cref="ChatWords.Masked"/>, found as the server
/// finds its blocked words (Online.Protocol/Chat/WordList.cs). The lobby shows its last lines again many times a
/// second while a race runs, so each text's mask is kept (the last few hundred).
/// </summary>
public static class ChatMask
{
    private const int Kept = 500;

    private static readonly Dictionary<string, string> Masked = new(StringComparer.Ordinal);

    private static readonly Lock Guard = new();

    /// <summary>The text with its swear words masked.</summary>
    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (Guard)
        {
            if (Masked.TryGetValue(text, out var known))
            {
                return known;
            }
        }
        var masked = ChatWords.Masked.Masked(text);
        lock (Guard)
        {
            if (Masked.Count >= Kept)
            {
                Masked.Clear();
            }
            Masked[text] = masked;
        }
        return masked;
    }
}
