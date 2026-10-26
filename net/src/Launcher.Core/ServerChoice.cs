namespace Launcher.Core;

/// <summary>What the player did at the Settings page's Server box.</summary>
public enum ServerAct
{
    /// <summary>A letter typed or deleted, or a text pasted.</summary>
    Typing,

    /// <summary>The box left for another part of the page (a click elsewhere, the Tab key).</summary>
    Leaving,

    /// <summary>Enter in the box, or the button under it that says so ("Use this server", "Use no server").</summary>
    Commit,

    /// <summary>The button "Use the official server".</summary>
    Official,
}

/// <summary>
/// The Settings page's Server box as a rule (the launcher contacts no server the player did
/// not choose). The box is a draft. A server is set when the player says so (<see cref="ServerAct.Commit"/>,
/// <see cref="ServerAct.Official"/>), never while they type and never by leaving the box: every prefix of an address
/// is an address too, of somebody else's computer, and the launcher asks a changed server at once
/// (<see cref="ServerHealth"/>, <see cref="ServerStatusFeed"/>); and a click on a button leaves the box first, so
/// that leaving set, and asked, the typed address a moment before the button's own choice. A blank box stands for
/// the official server only for a player who is on it already: committed by a player on a server of their own, or
/// while no server is set, it means no server (<see cref="LauncherSettings.NoServer"/>).
/// </summary>
public static class ServerChoice
{
    /// <summary>The settings after the player did <paramref name="act"/> with the box holding
    /// <paramref name="typed"/>.</summary>
    public static LauncherSettings After(LauncherSettings settings, string? typed, ServerAct act)
    {
        ArgumentNullException.ThrowIfNull(settings);
        switch (act)
        {
            case ServerAct.Official:
                // whatever the box holds: the button's choice, and the box is emptied (a blank box stands for it)
                return settings with { Server = null, NoServer = false };
            case ServerAct.Commit:
                var text = typed?.Trim() ?? "";
                if (text.Length == 0)
                {
                    // the official server stays the official server and no server stays none; a server of the
                    // player's own, cleared, is not replaced by the official one
                    return settings.Server is null ? settings : settings with { Server = null, NoServer = true };
                }
                // half an address, or none: what is set stays
                return SettingsCheck.IsServer(text) ? settings with { Server = text, NoServer = false } : settings;
            default:
                return settings;   // typing, or leaving the box: a draft
        }
    }

    /// <summary>What a blank box, committed, would mean for these settings: the official server (true), or no
    /// server. The box's placeholder says so.</summary>
    public static bool BlankIsOfficial(LauncherSettings settings) =>
        LauncherSettings.OnlineServer is { } official && After(settings, "", ServerAct.Commit).QuickPlayServer == official;

    /// <summary>The box holds something that is not what is set and would be if committed (an address, or a blank
    /// that means no server): the page says how to commit it.</summary>
    public static bool Waiting(LauncherSettings settings, string? typed) => After(settings, typed, ServerAct.Commit) != settings;

    /// <summary>The server the launcher asks by itself, nobody having pressed anything (the status feed, the
    /// connection dot): the one set, once the first screen (choose your disc) is passed, and none before. A first
    /// start has the official server set, and contacted it while that screen was still showing.</summary>
    /// <param name="pastFirstScreen">The player's disc is verified and the launcher's pages show.</param>
    public static string? AskedUnprompted(LauncherSettings settings, bool pastFirstScreen)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return pastFirstScreen ? settings.QuickPlayServer : null;
    }
}
