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

    /// <summary>Why the service would refuse a host's rules: a key it takes no value under (restore among them), or a rule
    /// of the race that is the mode's (<see cref="LobbyRules.OutsideTheMode"/>); null: none.</summary>
    public static string? BadRule(LobbyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Values.Keys.FirstOrDefault(k => !LobbyRules.Keys.Contains(k)) is { } key ? $"no rule {key}" : rules.OutsideTheMode();
    }

    /// <summary>The stat packs the stand-in names as a server's, by name: the packs that came with the launcher
    /// (<see cref="PracticePacks"/>, the protocol's, as the service's image has them). Its races name the pack; they do
    /// not carry it.</summary>
    public static IReadOnlySet<string> PackNames => Packs.Value;

    private static readonly Lazy<IReadOnlySet<string>> Packs =
        new(() => PracticePacks.Read(PracticePacks.Folder).Select(p => p.Name).ToHashSet(StringComparer.Ordinal));

    /// <summary>Why the stand-in cannot race a mode's stats ("this mode's stats: ..."); null: it can (the disc's own, the
    /// host's file, or a pack it names).</summary>
    private static string? Unraceable(ModeTerms terms) =>
        terms.MixedPacks == true || terms.Pack is { Length: > 0 } pack && !PackNames.Contains(pack)
            ? "this mode's stats: this server has no such stat pack" : null;

    /// <summary>The official modes the stand-in offers a host, as the service lists them (<see cref="LobbyList.Modes"/>):
    /// its rotation's lobby modes this "server" can race on their defaults. Empty: no rotation.</summary>
    public static IReadOnlyList<LobbyMode> OfficialModes =>
        [.. (Rotation?.LobbyModes ?? []).Where(m => Unraceable(m.Terms) is null).Select(m => m.Named())];

    /// <summary>Why the host's rules and the lobby's mode do not go together, as the service refuses them
    /// (<see cref="CustomLobby.BadRules"/>): a pack file the mode's stats choice is not, that choice without a file, a
    /// fixed course outside the mode's list. Null: they go together.</summary>
    public static string? Against(LobbyRules rules, ModeTerms? terms)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return terms switch
        {
            null => null,
            { PackFromFile: false } when rules.PackFile is not null => "the mode sets the stats now: choose a mode whose stats are your own file",
            { PackFromFile: true } when rules.PackFile is null => "this mode races your own stat pack: choose its file",
            { Course: null, Courses: { Count: > 0 } courses } when rules.Value(HostRules.CourseSelect) == (int)CourseSelectMode.Fixed
                && rules.Value(LobbyRules.FixedCourseKey) is { } course && !courses.Contains(course) =>
                $"{CourseNames.Name(course)} is not one of this mode's courses: choose one of them",
            _ => null,
        };
    }

    /// <summary>One of the official modes by its hash, as the service takes it from its catalog: on the host's values;
    /// another hash is no mode the lobby holds.</summary>
    private static (LobbyMode? Mode, ModeTerms? Terms, string? Refusal) Official(string hash, IReadOnlyList<int>? values)
    {
        if (Rotation?.LobbyModes.FirstOrDefault(m => m.Hash == hash) is not { } official)
        {
            return (null, null, "the lobby holds no such mode");
        }
        if (values is not null && official.Package.ValuesRefusal(values) is { } refused)
        {
            return (null, null, refused);
        }
        Packages[official.Hash] = official.Package;
        var raced = official with { Values = values };
        if (Unraceable(raced.Terms) is { } stats)
        {
            return (null, null, stats);
        }
        return (raced.Named(), raced.Terms, null);
    }

    /// <summary>The modes the stand-in's lobbies have compiled, by hash: a host's new values go over them, as the
    /// service's lobby holds its package.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ModePackage> Packages = new(StringComparer.Ordinal);

    /// <summary>The mode a host's rules give a stand-in lobby, as the service takes it: a mode file compiled (its first error
    /// refused, <see cref="CustomLobby.BadMode"/>, as a word this "server" cannot honour: a pack it does not name), the mode
    /// the lobby has named again (kept), an official one by its hash, or none named: Quick Play's normal mode.</summary>
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
            if (Unraceable(terms) is { } stats)
            {
                return (null, null, stats);
            }
            return (new LobbyMode(compiled.Hash!, compiled.ShareCode!, m.Name, m.Api, m.Badge, m.About, terms.Rules(), terms.Pack,
                m.Params.Count > 0 ? m.Params : null, m.Params.Count > 0 ? rules.ModeValues ?? [.. m.Params.Select(p => p.Default)] : null,
                terms.Machines is { Count: > 0 } machines ? machines : null, terms.CustomsOff,
                terms.Courses is { Count: > 0 } courses ? courses : null, terms.PackParam), terms, null);
        }
        if (rules.Mode is null)
        {
            // none named: Quick Play's normal mode, by its hash (the owner, 2026-11-05); no rotation: none
            return Rotation?.Normal is { } normal ? Official(normal.Hash, null) : (null, null, null);
        }
        if (rules.Mode.Hash != had?.Hash)
        {
            return Official(rules.Mode.Hash, rules.ModeValues);
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
        return Unraceable(now) is { } unraceable ? (null, null, unraceable)
            : (had with { Terms = now.Rules(), Pack = now.Pack, Values = values }, now, null);
    }
}
