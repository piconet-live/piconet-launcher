using System.Globalization;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>The line under the lobby's steps once the race is known (the card drawn, or a custom lobby's fixed course),
/// kept through the machines, the start and the race: the mode's title, the course and the laps
/// ("KNOCKOUT · Mute City: Twist Road · 3 laps"; a plain race "QUICK PLAY · ..."), and, pointed at, what the mode
/// changes and the rules it changes, a line each, as a vote card's tooltip says them (null: nothing to say); and the
/// race's laps (0: unlimited; null: not known). Badged: the title is a mode's badge (a mode that changes the race), drawn
/// as the vote cards draw it.</summary>
public sealed record RaceLine(string Title, string Place, string? Tip, int? Laps, bool Badged = false)
{
    /// <summary>The whole line: "KNOCKOUT · Mute City: Twist Road · 3 laps".</summary>
    public string Text => $"{Title} · {Place}";

    /// <summary>The title of a race on Quick Play's normal mode, and of one whose mode the server names nowhere.</summary>
    public const string QuickPlay = "QUICK PLAY";

    /// <summary>A mode's title, as the game's results page heads it: its badge (KNOCKOUT, DEATH RACE, OVERDRIVE), else
    /// its name in capitals (Quick Play's normal mode, "Quick Play": QUICK PLAY), else <see cref="QuickPlay"/>.</summary>
    public static string TitleOf(string? badge, string? mode) =>
        badge is { Length: > 0 } ? badge : mode is { Length: > 0 } ? mode.ToUpperInvariant() : QuickPlay;

    /// <summary>The line's words after the title: the course and the laps (unlimited or unknown laps say nothing).</summary>
    public static string Where(int course, int? laps) => laps is > 0 and var n
        ? string.Create(CultureInfo.InvariantCulture, $"{Courses.Name(course)} · {n} {(n == 1 ? "lap" : "laps")}")
        : Courses.Name(course);

    /// <summary>A vote card's tooltip: a spicy mode's line of what it changes, then the rules it changes, a line each;
    /// null for a normal card (Quick Play's own rules).</summary>
    public static string? CardTip(VoteCard? card) => card?.Badge is null ? null : Lines(card.About, card.Rules);

    /// <summary>The line for the card drawn (its mode, its laps; <paramref name="course"/>, the course drawn: a
    /// "Random course" card names none).</summary>
    public static RaceLine OfCard(VoteCard card, int course)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new(TitleOf(card.Badge, card.Mode), Where(course, card.Laps), CardTip(card), card.Laps, card.Badge is { Length: > 0 });
    }

    /// <summary>The line for a race whose mode is a package (a match start's <c>setup.mode</c>): its title, and what it
    /// changes with the rules its setup words change; plain Quick Play's (no badge, no lines) has no tooltip.</summary>
    public static RaceLine OfPackage(ModePackage mode, int course, int? laps)
    {
        ArgumentNullException.ThrowIfNull(mode);
        var rules = ModeRuleLines.Of(mode);
        var tip = mode.Badge.Length == 0 && rules.Count == 0 ? null : Lines(mode.About, rules);
        return new(TitleOf(mode.Badge, mode.Name), Where(course, laps), tip, laps, mode.Badge.Length > 0);
    }

    /// <summary>The line for a custom lobby's race: its mode (none named: Quick Play's), and, pointed at, the mode's line
    /// and the lobby's rules that differ from Quick Play's (<see cref="CustomRules.Summary"/>, a line each).</summary>
    public static RaceLine OfCustom(LobbyRules? rules, LobbyRules? quickPlay, int course, int? laps)
    {
        var mode = rules?.Mode;
        var differing = CustomRules.Summary(rules, quickPlay).Split(" · ", StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(s => mode is not null && s == mode.Name).Where(s => s != Courses.Name(course)).ToList();
        var tip = (mode?.About is not { Length: > 0 }) && differing.Count == 0 ? null : Lines(mode?.About, differing);
        return new(TitleOf(mode?.Badge, mode?.Name), Where(course, laps), tip, laps, mode?.Badge is { Length: > 0 });
    }

    /// <summary>The line of a plain Quick Play race (a server without modes).</summary>
    public static RaceLine Plain(int course, int? laps) => new(QuickPlay, Where(course, laps), null, laps);

    private static string Lines(string? about, IEnumerable<string>? rules) =>
        string.Join('\n', new[] { about }.Concat(rules ?? []).OfType<string>().Where(s => s.Length > 0));
}
