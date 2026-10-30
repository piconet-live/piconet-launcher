using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>One seat of the lobby in the racers' list: its number, the racer (or, empty, who gets it: a CPU, or
/// nobody), the racer's paint, their machine, a tick (has voted, is ready), and the racer's ping ("42 ms"; nothing
/// for an empty seat, a CPU's, and a racer the server has no number for).</summary>
public sealed record SeatRow(string Number, string Name, string ColorHex, string Machine, bool You, bool Open, bool Ticked, string Ping = "",
    string? Tag = null)
{
    /// <summary>The player's id (null: an empty seat).</summary>
    public string? Id { get; init; }

    /// <summary>The custom lobby's host: the host's mark beside the name.</summary>
    public bool Host { get; init; }

    /// <summary>The host may remove this player (the host's own page, another's row).</summary>
    public bool Kickable { get; init; }

    /// <summary>The seat's team (0 on); null: none yet.</summary>
    public int? Team { get; init; }

    /// <summary>The lobby's teams; 0: a lobby without teams.</summary>
    public int Teams { get; init; }

    /// <summary>The player may change this seat's team (their own, or the host's for anyone, while the lobby forms).</summary>
    public bool TeamChanges { get; init; }

    public string TeamText => Team is { } team ? ModeTeams.Word(team) : "No team";

    public bool TeamShown => Teams > 0 && !TeamChanges;

    public bool TeamButton => Teams > 0 && TeamChanges;

    /// <summary>The row with its player's team, in a lobby of so many teams.</summary>
    public SeatRow OnTeam(PlayerInfo player, int teams, bool changes) => this with { Team = player.Team, Teams = teams, TeamChanges = changes };
}

/// <summary>A column of the racers' list (the seats run down the columns).</summary>
public sealed record SeatColumn(IReadOnlyList<SeatRow> Seats);

/// <summary>A line of the lobby's chat: who said it (their paint beside the name) and what. The paint can change
/// while the line stands (the speaker repaints, or leaves): it changes in place, and the line stays the line it is
/// (a selection in it stays). A server's notice is no player's line: it carries its card (<see cref="Notice"/>),
/// which the chat draws instead of a name and a text.</summary>
public sealed partial class ChatLine(string id, string name, string colorHex, string text, bool you, NoticeItem? notice = null, string? tag = null)
    : ObservableObject
{
    /// <summary>A server's notice (null: a player's line).</summary>
    public NoticeItem? Notice { get; } = notice;

    public bool Server => Notice is not null;

    /// <summary>The speaker's player id.</summary>
    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>The speaker's tag, after the name ("NAME #4F2A"); null: none (a server before tags).</summary>
    public string? Tag { get; } = tag;

    /// <summary>The tag as the line shows it after the name: " #4F2A", or nothing.</summary>
    public string TagText => Tag is null ? "" : $" #{Tag}";

    public string Text { get; } = text;

    public bool You { get; } = you;

    [ObservableProperty]
    public partial string ColorHex { get; set; } = colorHex;

    /// <summary>The same line of the chat, whatever its paint.</summary>
    public bool Same(ChatLine other) =>
        other is not null && Id == other.Id && Name == other.Name && Tag == other.Tag && Text == other.Text && You == other.You;
}

/// <summary>A value beside a notice's label: its key, dim, and the value ("allowed" "Blue Falcon only").</summary>
public sealed record NoticeValue(string Key, string Value);

/// <summary>A step of the line across the lobby's top: its name, whether the lobby is at it, or past it.</summary>
public sealed record StepMark(string Name, bool On, bool Passed);

/// <summary>A racer of the race being set up, in the machine stage: their paint, lit when they are ready.</summary>
public sealed record ReadyMark(string Name, string ColorHex, bool Ready, bool You);

/// <summary>One option of the course vote, as a card.</summary>
public sealed partial class CourseOption(int index, string name, int course) : ObservableObject
{
    public int Index { get; } = index;

    /// <summary>The course number (<see cref="CourseVote.RandomCourse"/> for "Random course", until the draw reveals
    /// the course it picked), for its picture.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mark), nameof(ShowsMark), nameof(Place), nameof(PlaceLine), nameof(Cup), nameof(HasGem))]
    public partial int Course { get; set; } = course;

    /// <summary>The course's cup, for its gem beside the venue ("Ruby", "AX", "Story"); "" for the random course
    /// until the draw reveals the course it picked.</summary>
    public string Cup => CourseCups.CupOf(Course);

    public bool HasGem => Cup.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Venue), nameof(Place), nameof(PlaceLine), nameof(Track), nameof(Mark), nameof(ShowsMark))]
    public partial string Name { get; set; } = name;

    /// <summary>The race's laps, from the vote's card (<see cref="VoteCard"/>; 0: unlimited); null: not shown (no
    /// card, the random course before the draw).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceLine))]
    public partial int? Laps { get; set; }

    /// <summary>The badge of a mode that changes the race (a spicy card: "KNOCKOUT"); "" on a normal card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    public partial string Badge { get; set; } = "";

    /// <summary>The spicy mode's line of what it changes, shown when the card is hovered; null on a normal card.</summary>
    [ObservableProperty]
    public partial string? About { get; set; }

    public bool HasBadge => Badge.Length > 0;

    /// <summary>The card as the vote's card says it: its laps, and a spicy mode's badge and line.</summary>
    public void Show(VoteCard? card)
    {
        Laps = card?.Laps;
        Badge = card?.Badge ?? "";
        About = card?.Badge is null ? null : card.About;
    }

    /// <summary>The venue line: the venue and the race's laps when the card has them ("Casino Palace · 6 laps";
    /// unlimited laps say nothing).</summary>
    public string PlaceLine => Laps is > 0 and var laps
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Place} · {laps} {(laps == 1 ? "lap" : "laps")}")
        : Place;

    /// <summary>The venue in capitals, over the course's own name ("MUTE CITY" / "Twist Road").</summary>
    public string Venue => Name.Contains(": ", StringComparison.Ordinal) ? Name[..Name.IndexOf(": ", StringComparison.Ordinal)].ToUpperInvariant() : "ANY VENUE";

    /// <summary>The venue as it is written ("Mute City"; "Any venue" for the random course; "Story · Casino Palace"
    /// for a story chapter shown with a venue's picture).</summary>
    public string Place => !Name.Contains(": ", StringComparison.Ordinal) ? "Any venue"
        : Name.StartsWith("Story: ", StringComparison.Ordinal) && Launcher.Core.DiscArt.SameVenue(Course) is { } cup
            ? $"Story · {Courses.Name(cup)[..Courses.Name(cup).IndexOf(": ", StringComparison.Ordinal)]}"
            : Name[..Name.IndexOf(": ", StringComparison.Ordinal)];

    public string Track => Name.Contains(": ", StringComparison.Ordinal) ? Name[(Name.IndexOf(": ", StringComparison.Ordinal) + 2)..] : Name;

    /// <summary>Its votes as they stand, and its chance of being drawn ("75%": its share of the votes; every
    /// option alike while nobody has voted).</summary>
    [ObservableProperty]
    public partial string Votes { get; set; } = "";

    [ObservableProperty]
    public partial string Chance { get; set; } = "";

    /// <summary>The server has told the votes (an older one tells them only with the result).</summary>
    [ObservableProperty]
    public partial bool Counted { get; set; }

    [ObservableProperty]
    public partial bool IsMine { get; set; }

    /// <summary>It has votes: its chance is shown in full (a course with none, faint).</summary>
    [ObservableProperty]
    public partial bool Voted { get; set; }

    /// <summary>A pip a vote, the player's own first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<VotePip> Pips { get; set; } = [];

    /// <summary>The draw's frame is on it.</summary>
    [ObservableProperty]
    public partial bool IsLit { get; set; }

    /// <summary>The course drawn: its card widens (<see cref="Grow"/>) and lifts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Grow))]
    public partial bool IsWon { get; set; }

    /// <summary>Another course was drawn: the card steps back.</summary>
    [ObservableProperty]
    public partial bool IsLost { get; set; }

    /// <summary>Its share of its row's width: the card drawn takes 2.4 to the others' 1.</summary>
    public double Grow => IsWon ? 2.4 : 1;

    /// <summary>What stands for a picture where there is none: "?" for the random course, else the course's name.</summary>
    public string Mark => Course == CourseVote.RandomCourse ? "?" : Track;

    /// <summary>The card shows its mark instead of a picture: a course without a picture (a story chapter that
    /// races at no cup venue; no disc).</summary>
    public bool ShowsMark => Theme.GameSkin.Venue(Course) is null;
}

/// <summary>A vote on a course, as a pip: the player's own in green; lit while the draw's frame steps on it.</summary>
public sealed record VotePip(bool Mine, bool Lit = false);
