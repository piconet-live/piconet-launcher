using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The stand-in service: the launcher's dev mode (<c>--dev-service</c>), which runs the launcher against a service in
/// the launcher's own process, so that a contributor can click through Quick Play's pages without the real server,
/// a server stack or keys (as Slippi's launcher mocks its services). A lobby with a few racers, chat, the start vote,
/// the course vote and its draw, the machine stage; then no race: the stand-in has no race server, and says so. The
/// connection is <see cref="StandInCoordinator"/>, the status page's answers <see cref="StandInStatus"/>. Never on in
/// a player's launcher: only that command-line switch turns it on.
/// </summary>
public static class StandInService
{
    /// <summary>The address the launcher is pointed at while the stand-in runs. Nothing is ever sent to it: the
    /// connection and the status answers are made in the launcher (a name under <c>.invalid</c> is nobody's).</summary>
    public const string Address = "ws://stand-in.invalid/ws";

    /// <summary>The server's name, as the pages show it.</summary>
    public const string Name = "Stand-in service";

    /// <summary>What the player is told where a race would start.</summary>
    public const string NoRacesText = "No races on the stand-in service";

    /// <summary>How the launcher's sessions reach the stand-in (in place of <see cref="CoordinatorClient.ConnectionAsync"/>).</summary>
    public static CoordinatorConnect Connect { get; } = StandInCoordinator.ConnectAsync;

    /// <summary>The answers of the stand-in's status page and health check, for one asker's HTTP client.</summary>
    public static HttpMessageHandler Answers() => new StandInStatus();

    /// <summary>The folder of Quick Play's rotation the stand-in races (<see cref="QuickPlayRotation"/>): the
    /// protocol's protocol/quickplay/, which the build puts beside the program, as the service's image has it.</summary>
    public static string QuickPlayFolder { get; private set; } = Path.Combine(AppContext.BaseDirectory, "quickplay");

    private static readonly Lock RotationGate = new();
    private static (string Folder, QuickPlayRotation? Rotation, string? Refusal)? _rotation;

    /// <summary>The rotation the stand-in's Quick Play votes on, read once a folder; null when the folder's does not
    /// load (<see cref="RotationRefusal"/>): the stand-in then votes on courses alone, the vote of before modes,
    /// where the service would refuse to start.</summary>
    public static QuickPlayRotation? Rotation => Rotated().Rotation;

    /// <summary>Why the rotation did not load (the file, and the compiler's line); null when it did.</summary>
    public static string? RotationRefusal => Rotated().Refusal;

    /// <summary>The stand-in races the rotation in <paramref name="folder"/> from its next lobby on (a test's own).</summary>
    public static void UseQuickPlay(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        lock (RotationGate)
        {
            QuickPlayFolder = folder;
            _rotation = null;
        }
    }

    private static (string Folder, QuickPlayRotation? Rotation, string? Refusal) Rotated()
    {
        lock (RotationGate)
        {
            if (_rotation is not { } read || read.Folder != QuickPlayFolder)
            {
                try
                {
                    read = (QuickPlayFolder, QuickPlayRotation.Read(QuickPlayFolder), null);
                }
                catch (InvalidDataException e)
                {
                    read = (QuickPlayFolder, null, e.Message);
                }
                _rotation = read;
            }
            return read;
        }
    }

    /// <summary>Why the service would refuse a host's rules for a key it takes no value under (restore among them); null: none.</summary>
    public static string? BadRule(LobbyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Values.Keys.FirstOrDefault(k => !LobbyRules.Keys.Contains(k)) is { } key ? $"no rule {key}" : null;
    }

    /// <summary>The modes the stand-in's lobbies have compiled, by hash: a host's new values go over them, as the
    /// service's lobby holds its package.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ModePackage> Packages = new(StringComparer.Ordinal);

    /// <summary>The mode a host's rules give a stand-in lobby, as the service takes it: a mode file compiled (its first error
    /// refused, <see cref="CustomLobby.BadMode"/>, as a word this "server" cannot honour: it has no stat packs), the mode
    /// the lobby has named again (kept), or none.</summary>
    public static (LobbyMode? Mode, ModeTerms? Terms, string? Refusal) ModeOf(LobbyRules rules, LobbyMode? had, ModeTerms? hadTerms)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.ModeFile is { } text)
        {
            var compiled = text.Length > LobbyRules.MostModeFile ? null : ModeCompiler.Compile(text);
            if (compiled?.Mode is not { } m)
            {
                return (null, null, compiled?.Errors[0] ?? "the mode file is too long");
            }
            if (rules.ModeValues is { } chosen && m.ValuesRefusal(chosen) is { } outside)
            {
                return (null, null, outside);
            }
            var terms = ModeTerms.Of(m, rules.ModeValues);
            Packages[compiled.Hash!] = m;
            if (terms.Pack is { Length: > 0 } || terms.MixedPacks == true)
            {
                return (null, null, "this mode's stats: this server has no stat packs");
            }
            return (new LobbyMode(compiled.Hash!, compiled.ShareCode!, m.Name, m.Api, m.Badge, m.About, terms.Rules(), terms.Pack,
                m.Params.Count > 0 ? m.Params : null, m.Params.Count > 0 ? rules.ModeValues ?? [.. m.Params.Select(p => p.Default)] : null), terms, null);
        }
        if (rules.Mode is null)
        {
            return (null, null, null);
        }
        if (rules.Mode.Hash != had?.Hash)
        {
            return (null, null, "the lobby holds no such mode");
        }
        if (rules.ModeValues is not { } values || !Packages.TryGetValue(had.Hash, out var kept))
        {
            return (had, hadTerms, null);
        }
        if (kept.ValuesRefusal(values) is { } refused)
        {
            return (null, null, refused);
        }
        var now = ModeTerms.Of(kept, values);
        return (had with { Terms = now.Rules(), Pack = now.Pack, Values = values }, now, null);
    }
}
