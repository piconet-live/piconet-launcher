using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>How an option row is worked: stepped with left and right (and pressed), typed, a range of two numbers,
/// only read, or pressed to do something.</summary>
public enum OptionKind
{
    Step,
    Text,
    Range,
    Value,
    Action,
}

/// <summary>
/// One row of the redesigned interface's option pages (a custom lobby's rules, Practice's set-up, every rule of a
/// lobby): one of the classic launcher's rule rows (<see cref="RuleRow"/>, the same object the classic rules panel
/// shows) with its value in words, the line that explains it, and the group it stands in. Left and right step the
/// value, A presses the row (a switch flips, a choice goes round, a fixed course opens its picker); what the row holds
/// is the rule row's alone.
/// </summary>
public sealed partial class OptionItem : ObservableObject
{
    public OptionItem(RuleRow row, string group)
    {
        ArgumentNullException.ThrowIfNull(row);
        Row = row;
        Group = group;
        Kind = row switch
        {
            IOptionRow { IsAction: true } => OptionKind.Action,
            TextRule => OptionKind.Text,
            RangeRule => OptionKind.Range,
            ValueRule => OptionKind.Value,
            _ => OptionKind.Step,
        };
        row.PropertyChanged += (_, _) => Show();
        Show();
    }

    public RuleRow Row { get; }

    /// <summary>The heading of the group the row stands in (the help panel's eyebrow).</summary>
    public string Group { get; }

    public OptionKind Kind { get; }

    public bool IsStep => Kind == OptionKind.Step;

    public bool IsText => Kind == OptionKind.Text;

    public bool IsRange => Kind == OptionKind.Range;

    public bool IsValue => Kind == OptionKind.Value;

    public bool IsAction => Kind == OptionKind.Action;

    /// <summary>The row can be worked now (a row of the Settings page may not be: ray tracing on a card that cannot).</summary>
    public bool Enabled => (Row as IOptionRow)?.Enabled ?? true;

    public string Label => Row.Label;

    /// <summary>Developer mode's key of the row (its launch file's, its rule's); "" none.</summary>
    public string DevKey => DevKeys.For(Row.Label);

    /// <summary>What the row explains in the help panel while the focus is on it.</summary>
    public string Help => RuleHelp.For(Row);

    /// <summary>The value in words: "Grand Prix", "8", "200 ms", "On".</summary>
    [ObservableProperty]
    public partial string ValueText { get; set; } = "";

    /// <summary>A step left goes somewhere (its arrow lit); false: the value is at its first end.</summary>
    [ObservableProperty]
    public partial bool CanLess { get; set; } = true;

    /// <summary>A step right goes somewhere; false: the value is at its last end.</summary>
    [ObservableProperty]
    public partial bool CanMore { get; set; } = true;

    /// <summary>The row's line under it ("": none): why a file was not taken, how a rule differs.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = "";

    /// <summary>The value differs from Quick Play's (a read-only row): in the accent.</summary>
    public bool Marked => Row is ValueRule { Marked: true };

    /// <summary>The help panel offers a file for the row: a mode's ("mode") or a stat pack's ("pack"); "": none.</summary>
    public string FileKind => Row switch
    {
        GameModeRule => "mode",
        PackRule => "pack",
        _ => "",
    };

    public bool TakesFile => FileKind.Length > 0;

    /// <summary>The row's course is chosen in a picker (a fixed course).</summary>
    public bool PicksCourse => Row is CourseRule { IsFixed: true };

    /// <summary>A colour row: its colour's swatch shows beside its word.</summary>
    public bool HasSwatch => Row is SwatchRule;

    /// <summary>The colour row's colour as "#RRGGBB"; null none.</summary>
    public string? SwatchHex => (Row as SwatchRule)?.Hex;

    // a text row's and a range row's own values, for the view's boxes
    public string Text
    {
        get => (Row as TextRule)?.Text ?? "";
        set
        {
            if (Row is TextRule text)
            {
                text.Text = value;
            }
        }
    }

    public string Hint => (Row as TextRule)?.Hint ?? "";

    public int Most => (Row as TextRule)?.Most ?? 0;

    public char Mask => (Row as TextRule)?.Mask ?? '\0';

    public int Low
    {
        get => (Row as RangeRule)?.Low ?? 0;
        set
        {
            if (Row is RangeRule range)
            {
                range.Low = value;
            }
        }
    }

    public int High
    {
        get => (Row as RangeRule)?.High ?? 0;
        set
        {
            if (Row is RangeRule range)
            {
                range.High = value;
            }
        }
    }

    public int Minimum => (Row as RangeRule)?.Minimum ?? 0;

    public int Maximum => (Row as RangeRule)?.Maximum ?? 0;

    /// <summary>Left (-1) or right (1). False: the value is at its end.</summary>
    public bool Step(int by) => OptionSteps.Step(Row, by);

    /// <summary>A (and a click).</summary>
    public void Press() => OptionSteps.Press(Row);

    private void Show()
    {
        ValueText = OptionSteps.Words(Row);
        (CanLess, CanMore) = OptionSteps.Room(Row);
        Note = Row.Note;
        OnPropertyChanged(nameof(PicksCourse));
        OnPropertyChanged(nameof(SwatchHex));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Low));
        OnPropertyChanged(nameof(High));
        OnPropertyChanged(nameof(Help));
    }
}

/// <summary>A group of option rows under its heading ("" none).</summary>
public sealed record OptionGroup(string Heading, IReadOnlyList<OptionItem> Items)
{
    public bool HasHeading => Heading.Length > 0;

    /// <summary>The classic rules panel's groups as option groups (a group without a heading takes
    /// <paramref name="first"/> for its rows' eyebrow).</summary>
    public static IReadOnlyList<OptionGroup> Of(IEnumerable<RuleGroup>? groups, string first = "")
    {
        if (groups is null)
        {
            return [];
        }
        return [.. groups.Select(g => new OptionGroup(g.Heading ?? "", [.. g.Rows.Select(r => new OptionItem(r, g.Heading ?? first))]))];
    }
}

/// <summary>What left, right and A do to each kind of rule row, and its value in words: the one place the
/// redesigned interface works the classic rows.</summary>
public static class OptionSteps
{
    /// <summary>One step of the row's value that way; false when it is at its end (or the row has no steps).</summary>
    public static bool Step(RuleRow row, int by)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row switch
        {
            IOptionRow r => r.StepBy(by),
            ChoiceRule c => Move(c.Selected, by, c.Options.Count, v => c.Selected = v),
            SegmentedRule s => Move(s.Selected, by, s.Options.Count, v => s.Selected = v),
            GameModeRule m => Move(m.Selected, by, m.Options.Count, v => m.Selected = v),
            PackRule p => Move(p.Selected, by, p.Options.Count, v => p.Selected = v),
            CourseRule c => Move(c.Selected, by, c.Options.Count, v => c.Selected = v),
            StepperRule s => Number(s.Value, by * Math.Max(1, s.Step), s.Minimum, s.Maximum, v => s.Value = v),
            LapsRule l => StepLaps(l, by),
            NamedNumberRule n => StepNamed(n, by),
            SliderRule s => Number((int)Math.Round(s.Value), by, s.Minimum, s.Maximum, v => s.Value = v),
            SwitchRule s => Set(s, by > 0),
            SwatchRule s => s.Step(by),
            _ => false,
        };
    }

    /// <summary>A: a switch flips, a choice goes on to its last (never round: the ends are ends), a fixed course's
    /// picker opens, Unlimited and a named value flip, the machine's row opens the Garage, a colour row opens every
    /// colour.</summary>
    public static void Press(RuleRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        switch (row)
        {
            case IOptionRow r:
                r.Press();
                break;
            case SwitchRule s:
                s.On = !s.On;
                break;
            case CourseRule { IsFixed: true } course:
                course.PickCourseCommand.Execute(null);
                break;
            case CourseRule or ChoiceRule or SegmentedRule or GameModeRule or PackRule:
                Step(row, 1);
                break;
            case LapsRule l:
                l.Unlimited = !l.Unlimited;
                break;
            case NamedNumberRule n:
                n.IsNamed = !n.IsNamed;
                break;
            case MachineRule m:
                m.GarageCommand.Execute(null);
                break;
            case SwatchRule s:
                s.Open();
                break;
            default:
                break;
        }
    }

    /// <summary>Whether a step left and a step right go anywhere from the row's value (a switch: on has left, off
    /// right); a row without plain ends (laps, a named number, a colour) has both.</summary>
    public static (bool Less, bool More) Room(RuleRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row switch
        {
            IOptionRow r => r.Room,
            ChoiceRule c => (c.Selected > 0, c.Selected < c.Options.Count - 1),
            SegmentedRule s => (s.Selected > 0, s.Selected < s.Options.Count - 1),
            GameModeRule m => (m.Selected > 0, m.Selected < m.Options.Count - 1),
            PackRule p => (p.Selected > 0, p.Selected < p.Options.Count - 1),
            CourseRule { IsFixed: false } c => (c.Selected > 0, c.Selected < c.Options.Count - 1),
            StepperRule s => (s.Value > s.Minimum, s.Value < s.Maximum),
            SliderRule s => (s.Value > s.Minimum, s.Value < s.Maximum),
            SwitchRule s => (s.On, !s.On),
            _ => (true, true),
        };
    }

    /// <summary>The row's value in words.</summary>
    public static string Words(RuleRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row switch
        {
            IOptionRow r => r.Words,
            ValueRule v => v.Value,
            MachineRule m => m.Value,
            ChoiceRule c => c.Chosen,
            SegmentedRule s => s.Selected >= 0 && s.Selected < s.Options.Count ? s.Options[s.Selected] : "",
            GameModeRule m => m.Chosen?.Name ?? "",
            PackRule p => p.Chosen,
            CourseRule c => c.IsFixed ? $"Fixed · {c.CourseName}" : c.Options[c.Selected],
            StepperRule s => WithUnit(s.Value, s.Unit),
            LapsRule l => l.Unlimited ? "Unlimited" : Whole(l.Value),
            NamedNumberRule n => n.IsNamed ? n.Named : WithUnit(n.Value, n.Unit),
            SliderRule s => Whole(s.Whole),
            SwitchRule s => s.State,
            TextRule t => t.Text,
            RangeRule r => $"{Whole(r.Low)} to {Whole(r.High)}",
            SwatchRule s => s.Chosen,
            _ => "",
        };
    }

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string WithUnit(int value, string unit) => unit.Length > 0 ? $"{Whole(value)} {unit}" : Whole(value);

    private static bool Move(int at, int by, int count, Action<int> set)
    {
        var to = Math.Clamp(at + by, 0, Math.Max(0, count - 1));
        if (count == 0 || to == at)
        {
            return false;
        }
        set(to);
        return true;
    }

    private static bool Number(int at, int by, int least, int most, Action<int> set)
    {
        var to = Math.Clamp(at + by, least, most);
        if (to == at)
        {
            return false;
        }
        set(to);
        return true;
    }

    private static bool Set(SwitchRule row, bool on)
    {
        if (row.On == on)
        {
            return false;
        }
        row.On = on;
        return true;
    }

    /// <summary>Laps: a number; past its most, Unlimited; back from Unlimited, the most again.</summary>
    private static bool StepLaps(LapsRule row, int by)
    {
        if (row.Unlimited)
        {
            if (by > 0)
            {
                return false;
            }
            row.Unlimited = false;
            row.Value = row.Maximum;
            return true;
        }
        if (by > 0 && row.Value >= row.Maximum)
        {
            row.Unlimited = true;
            return true;
        }
        return Number(row.Value, by, 1, row.Maximum, v => row.Value = v);
    }

    /// <summary>A number with a named value beside it: the named value ("Off", "By course") below its least.</summary>
    private static bool StepNamed(NamedNumberRule row, int by)
    {
        if (row.IsNamed)
        {
            if (by < 0)
            {
                return false;
            }
            row.IsNamed = false;
            row.Value = row.Minimum;
            return true;
        }
        if (by < 0 && row.Value <= row.Minimum)
        {
            row.IsNamed = true;
            return true;
        }
        return Number(row.Value, by * Math.Max(1, row.Step), row.Minimum, row.Maximum, v => row.Value = v);
    }
}

/// <summary>The line each rule row explains itself with in the help panel, by its label (the mockup's help, written
/// for the rules the launcher has); a row of a mode's own says what it is in general words.</summary>
public static class RuleHelp
{
    private static readonly Dictionary<string, string> Lines = new(StringComparer.Ordinal)
    {
        ["Name"] = "What others see in the list of lobbies.",
        ["Password"] = "With a password, only people you give it to can join. Off removes it.",
        ["New password"] = "A new password for the lobby. Empty keeps the one it has.",
        ["Racers"] = "The most racers in one race.",
        ["Mode"] = "Grand Prix is a normal race. Modes change how a race is won. A mode can also come from a file.",
        ["Course"] = "How the next course is chosen: the racers vote, it is drawn, or it is always the same.",
        ["Laps"] = "How long one race is.",
        ["CPU racers"] = "Vote lets the racers decide. Always fills the empty seats.",
        ["CPU level"] = "How well the CPU racers drive.",
        ["Boost from the start"] = "A boost for everyone who times the start well.",
        ["Finish limit"] = "How long the others have once the first racer finishes.",
        ["Mines respawn"] = "How fast mines come back after they go off. Off: they stay gone.",
        ["Machine setting"] = "The range of the acceleration and top speed setting racers may use.",
        ["Stats"] = "The machines' stats in this lobby: the disc's own, or a stat pack.",
        ["Stats file"] = "Your own stat pack file, when the mode's stats are a file.",
        ["Mixed packs"] = "Whether racers may bring their own stat packs.",
        ["Vote"] = "How long the course vote lasts.",
        ["Machine choice"] = "How long racers have to pick their machine.",
        ["Voting minimum"] = "How many racers it takes before a vote to start counts.",
        ["Latency limit"] = "Keeps out racers whose connection is too slow.",
        ["Teams"] = "Race in teams instead of each for themselves.",
        ["Machine"] = "The machine you practise with. Press it to change it in the Garage.",
        ["Rewind"] = "How far back you can jump to try a corner again. The mode sets it.",
        ["Restore"] = "Whether your machine is restored after a crash.",
        ["Body"] = "The frame. It decides most of how tough the machine is, and its name.",
        ["Cockpit"] = "Where the pilot sits. It changes the stats a little, and the name.",
        ["Booster"] = "The engines at the back. Bigger boosters are faster but harder to steer.",
        ["Pilot"] = "Who drives it. The pilots come from your disc.",
        ["Body colour"] = "Left and right go through the body's colours. A shows every colour.",
        ["Cockpit colour"] = "Left and right go through the cockpit's colours. A shows every colour.",
        ["Booster colour"] = "Left and right go through the booster's colours. A shows every colour.",
        ["Trail colour"] = "The light behind your machine, on every machine you race. Everyone in the race sees it.",
        ["Volume"] = "How loud the game is. Left and right change it.",
        ["Graphics"] = "One choice for how the game looks: it sets the resolution, anti-aliasing and texture filtering together. Change those one by one in the Graphics group.",
        ["Full screen mode"] = "Borderless switches between windows quickly. Exclusive hands the whole screen to the game.",
        ["Input delay"] = "How many frames your inputs wait in an online race. Change it only if races feel jumpy.",
        ["Rumble"] = "Your controller shakes while you drive.",
        ["Controller"] = "Ports, devices, dead zones and every button.",
        ["Widescreen"] = "Game's is the game's own 16:9. True is 16:9 with the full height of Off.",
        ["Vertical sync"] = "Stops lines tearing across the picture. Can add a little delay.",
        ["High refresh"] = "Pictures between the game's own steps, for a screen above 60 Hz. The race stays at 60 steps a second.",
        ["Show frame rate"] = "A small counter in the corner of the game window.",
        ["Resolution"] = "How sharp the game draws. Higher needs a stronger graphics card.",
        ["Anti-aliasing"] = "Smooths jagged edges.",
        ["Anisotropic filtering"] = "Keeps the track sharp far ahead.",
        ["Ray tracing"] = "Realistic light and reflections, on graphics cards that can do it. The game prepares its graphics once before the first use.",
        ["Colourblind filter"] = "Shifts the game's colours so they are easier to tell apart.",
        ["Other racers' sounds"] = "The engines and crashes of the machines around you.",
        ["Launcher sounds"] = "How loud the launcher's own sounds are, all of them. Menu sounds, notifications and other sounds each go up to this.",
        ["Music and game sounds"] = "How loud the game is: its music and its race sounds. The game mixes them itself, so they are set together, as Volume under Basics.",
        ["Menu sounds"] = "The launcher's menus: the cursor as the focus moves, back, and the confirm of a big button.",
        ["Notifications"] = "A lobby's moments (the course vote, your turn to pick, the start) and urgent notices from the server.",
        ["Other sounds"] = "Everything else the launcher plays: the course draw's ticks and its chime.",
        ["Mute launcher sounds"] = "Turns them off and keeps the volume for later.",
        ["Show inputs"] = "Opens a small window with your controller as the game reads it, whenever the game starts.",
        ["Controller navigation"] = "Lets the controller work the launcher. The game gets the controller back while it runs.",
        ["Button icons"] = "Which buttons the hints show: your controller's own (Auto), or another family's.",
        ["Confirm button"] = "Which face button selects: the bottom one, or the right one as on Nintendo controllers. Auto takes your controller's.",
        ["Your name"] = "The name other racers see. Opens the keyboard on the screen.",
        ["Server"] = "Where Quick Play and custom lobbies live. Keep the official one unless a friend runs their own.",
        ["Use the official server"] = "Back to the official server.",
        ["Account"] = "Signing in with Discord comes in a later version.",
        ["Notices"] = "Silent keeps the server's notices but drops their sound and the flashing taskbar button.",
        ["Show on Discord"] = "Friends on Discord see that you are racing. Off clears it at once.",
        ["Hide swear words"] = "Masks them in the chat as ****.",
        ["Experimental UI"] = "This new interface. Off brings back the classic launcher at once.",
        ["Developer mode"] = "Build, connection, input and file details where they belong.",
        ["Disc image"] = "Your own F-Zero GX (USA) disc image, .iso or .gcm. The launcher prepares the game from it once.",
        ["Update automatically"] = "Downloads updates in the background and puts them in at the next start. Never while a game runs.",
        ["Updates"] = "Where the launcher's own update stands, and the one step that fits.",
        ["Bring over an older copy"] = "Settings, replays and game files of a copy you unzipped before 0.5.0. The old copy stays as it is.",
        ["Reduced motion"] = "Fewer animations, and the background holds still.",
        ["Interface scale"] = "Bigger text and buttons, for a TV or a big screen. Takes effect when you apply it.",
        ["Apply the interface scale"] = "Use the new size now.",
        ["Record races"] = "Keeps a replay of every online race.",
        ["Record practice"] = "Keeps a replay of every practice run.",
        ["Replay folder"] = "Where your replays are kept. A picks another folder.",
        ["Open the replay folder"] = "Shows the folder with your replays.",
        ["Port"] = "Which of the game's four controller ports you set up.",
        ["Input"] = "What is plugged into this port.",
        ["Device"] = "The controller this port reads.",
        ["Test rumble"] = "Shakes the controller once, so you know rumble works.",
        ["Set up in the game"] = "Opens the game's own controller set-up.",
        ["Stick dead zone"] = "How far the stick moves before the game notices. Larger helps a worn stick.",
        ["C-Stick dead zone"] = "The same for the C-Stick.",
        ["Reset the buttons"] = "Puts every button back as the device's standard layout has it.",
        ["Game files"] = "The game's own files, prepared once from your disc image. When they are missing or damaged, press to prepare them again.",
        // the official modes' own rules (their mode files say no more than the label)
        ["Resume countdown"] = "The countdown after you go on from the pause or load your save. 0 s goes on at once.",
        ["Machine setting from"] = "The lowest machine setting racers may use: 0 is all acceleration, 100 all top speed.",
        ["Machine setting to"] = "The highest machine setting racers may use. The same as From fixes everyone's setting.",
        ["Lives"] = "How many wrecks each racer can take. The last one is final; 1 is the game's own rule.",
        ["Race length"] = "How long the race runs before the clock ends it.",
        ["First cut"] = "How long after GO the racer in last place is out for the first time.",
        ["Racers left"] = "The cuts stop and the race ends when this many racers are left.",
        ["Warn from"] = "How many seconds before each cut the warning shows.",
        ["Ghosts on the map"] = "Whether the other racers' ghosts show on the course map.",
        ["Gap"] = "How you see where you stand: the live gap to the ghosts, the gap to the leader at each lap line, both, or none.",
        ["Speed added every 10 s"] = "How fast the speed rises, in km/h every 10 seconds. 120 is about 12 km/h a second.",
        ["Brake energy cost (%)"] = "How much energy braking costs. 0 makes the brake free.",
        ["Boost surge (% above the speed)"] = "How far a boost lifts you above the rising speed.",
        ["Damage"] = "How much energy a hit takes, against the game's own 100%. At 400% a hard hit can end a machine from full.",
        ["Points for a KO"] = "Points for every machine you take out.",
        ["Survival every"] = "How often the racers still in score points for staying in.",
        ["Points for survival"] = "Points each racer still in scores every time.",
        ["Points a life left"] = "Points at the end for every life you still have.",
        ["1st alive"] = "Bonus points at the end for the leader of the racers still in.",
        ["2nd alive"] = "Bonus points at the end for second place of the racers still in.",
        ["3rd alive"] = "Bonus points at the end for third place of the racers still in.",
        ["First crown"] = "How long after GO the crown goes to a racer drawn at random.",
        ["Safe after taking it"] = "How long a new holder cannot be touched: they pass through everyone.",
        ["Speed after taking it (%)"] = "How much faster a new holder runs while they are safe.",
        ["Holder slower by (%)"] = "How much slower the holder runs once they are no longer safe.",
        ["Holder must keep moving (km/h)"] = "The pace the holder has to keep along the course. Slower, and the crown goes to last place.",
    };

    /// <summary>The row's line: its own, else the mode's about for the mode, else a general one.</summary>
    public static string For(RuleRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row is GameModeRule { Chosen: { HasAbout: true } chosen })
        {
            return chosen.About;
        }
        if (row is OptionRow { Help: { } own })
        {
            return own;
        }
        return Lines.TryGetValue(row.Label, out var line) ? line : General;
    }

    /// <summary>The line of a row that has none of its own.</summary>
    public const string General = "A setting of this mode.";
}
