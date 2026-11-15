namespace Launcher.App.ViewModels.NextUi;

/// <summary>A line of the Settings window's facts: what is said ("Takes effect") and what it is ("At once").</summary>
public sealed record InfoFact(string Label, string Value);

/// <summary>One of a row's choices in the Settings window, the one chosen marked.</summary>
public sealed record InfoChoice(string Text, bool On);

/// <summary>A row of a group in the Settings window: its name and its value now.</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>
/// What the Settings page's window on the right says (the owner, 2026-11-15, twice: it said too little): of the group or
/// the row the focus is on, the pointer's or the pad's alike. Its group's drawing (<see cref="Icon"/>, also large behind
/// it), its name, what it does; for a row its value now (<see cref="Item"/>'s own, so that a change shows at once) and
/// its note, every choice it has with the one chosen marked or, for a number, where it stands between its ends; when a
/// change takes effect and what it applies to (<see cref="SettingFacts"/>); something good to know; how it is changed.
/// For a group: every row in it with its value now, and what the group is about. Made again whenever a setting
/// changes (<see cref="SettingsPageViewModel"/>).
/// </summary>
public sealed class SettingInfo
{
    private SettingInfo(string group, string title, string text, string howTo, OptionItem? item)
    {
        Group = group;
        Title = title;
        Text = text;
        HowTo = howTo;
        Item = item;
        Icon = IconOf(group);
    }

    /// <summary>The group's name (over the title).</summary>
    public string Group { get; }

    public string Title { get; }

    /// <summary>What it does, in a sentence or two.</summary>
    public string Text { get; }

    /// <summary>How it is changed, by the pad, the keys and the pointer alike.</summary>
    public string HowTo { get; }

    /// <summary>A group's settings, each with its value now; none for a row.</summary>
    public IReadOnlyList<InfoRow> Rows { get; private init; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>The row (its value and its note as they are now); null for a group.</summary>
    public OptionItem? Item { get; }

    public bool HasItem => Item is not null;

    /// <summary>A row's choices (a list's, or Off and On), the one chosen marked; none for any other row.</summary>
    public IReadOnlyList<InfoChoice> Choices { get; private init; } = [];

    public bool HasChoices => Choices.Count > 0;

    /// <summary>A number's place between its ends, 0 to 1 (the meter); null for any other row.</summary>
    public double? Share { get; private init; }

    public bool HasMeter => Share is not null;

    /// <summary>The meter's width at that place, of <see cref="MeterWidth"/>.</summary>
    public double MeterFill => (Share ?? 0) * MeterWidth;

    /// <summary>The meter's whole width in the window.</summary>
    public const double MeterWidth = 360;

    /// <summary>The meter's ends in words ("0%", "100%").</summary>
    public string Low { get; private init; } = "";

    public string High { get; private init; } = "";

    /// <summary>When a change takes effect, what it applies to, a number's range; for a group how many it holds.</summary>
    public IReadOnlyList<InfoFact> Facts { get; private init; } = [];

    public bool HasFacts => Facts.Count > 0;

    /// <summary>Something good to know; "" none.</summary>
    public string Tip { get; private init; } = "";

    public bool HasTip => Tip.Length > 0;

    /// <summary>The group's drawing: a path in 24 by 24, drawn as a line.</summary>
    public string Icon { get; }

    /// <summary>A group of the first step.</summary>
    public static SettingInfo Of(SettingsCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        var items = category.Groups.SelectMany(g => g.Items).ToList();
        var when = items.Select(i => SettingFacts.WhenOf(i.Label)).Where(w => w.Length > 0).Distinct().ToList();
        List<InfoFact> facts = [new("Settings", items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        if (when.Count == 1)
        {
            facts.Add(new InfoFact("Takes effect", when[0]));
        }
        return new SettingInfo(category.Title, category.Title, category.Line,
            "A opens the group (a click too); B comes back to the groups.", null)
        {
            Rows = [.. items.Select(i => new InfoRow(i.Label, i.ValueText))],
            Facts = facts,
            Tip = SettingFacts.GroupTipOf(category.Title),
        };
    }

    /// <summary>A row of the group open.</summary>
    public static SettingInfo Of(OptionItem item, string group)
    {
        ArgumentNullException.ThrowIfNull(item);
        var facts = new List<InfoFact>();
        if (SettingFacts.WhenOf(item.Label) is { Length: > 0 } when)
        {
            facts.Add(new InfoFact("Takes effect", when));
        }
        if (SettingFacts.WhereOf(item.Label) is { Length: > 0 } where)
        {
            facts.Add(new InfoFact("Applies to", where));
        }
        var number = item.Row as NumberRow;
        if (number is not null)
        {
            facts.Add(new InfoFact("Range", number.Span));
        }
        return new SettingInfo(group, item.Label, item.Help, HowToOf(item), item)
        {
            Choices = ChoicesOf(item),
            Share = number?.Share,
            Low = number?.LeastWords ?? "",
            High = number?.MostWords ?? "",
            Facts = facts,
            Tip = SettingFacts.TipOf(item.Label),
        };
    }

    /// <summary>A row's choices with the one chosen marked: a list's, Off and On for a switch; none for any other.</summary>
    private static IReadOnlyList<InfoChoice> ChoicesOf(OptionItem item) => item.Row switch
    {
        ChoiceRow choice => [.. choice.Options.Select(o => new InfoChoice(o, o == choice.Words))],
        ToggleRow toggle => [new InfoChoice("Off", toggle.Words == "Off"), new InfoChoice("On", toggle.Words == "On")],
        _ => [],
    };

    /// <summary>How a row of this kind is changed.</summary>
    public static string HowToOf(OptionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.Enabled)
        {
            return "It cannot be changed now.";
        }
        return item.Kind switch
        {
            OptionKind.Step => "Left and right change it: the D-pad or the stick, the arrow keys, or the arrows beside the value. It holds at once.",
            OptionKind.Action => "A opens it, or a click.",
            OptionKind.Text => "A types in it, or a click; with the pad the keyboard comes up on the screen.",
            OptionKind.Range => "Two numbers: the lowest and the highest. Type them, or step them.",
            _ => "It says what it is; there is nothing to change here.",
        };
    }

    /// <summary>A group's drawing (a line in 24 by 24): sliders for the basics, a screen, a cube, a loudspeaker, a pad,
    /// a globe, two people, a window, a film; a gear for any other.</summary>
    public static string IconOf(string group) => group switch
    {
        "Basics" => "M4 6H14 M18 6H20 M4 12H7 M11 12H20 M4 18H15 M19 18H20 M16 4V8 M9 10V14 M17 16V20",
        "Display" => "M3 4H21V16H3Z M8 20H16 M12 16V20",
        "Graphics" => "M12 3L20 7.5V16.5L12 21L4 16.5V7.5Z M12 12L20 7.5 M12 12L4 7.5 M12 12V21",
        "Sound" => "M4 9H8L13 5V19L8 15H4Z M16 9.5A3.5 3.5 0 0 1 16 14.5 M18.5 7A7 7 0 0 1 18.5 17",
        "Controller" => "M6.5 8H17.5A4.5 4.5 0 0 1 22 12.5A3.5 3.5 0 0 1 15.5 15L14.5 14H9.5L8.5 15A3.5 3.5 0 0 1 2 12.5A4.5 4.5 0 0 1 6.5 8Z M7 10.5V14 M5.25 12.25H8.75 M16 11H16.01 M18 13H18.01",
        "Online" => "M12 3A9 9 0 1 0 12.01 3Z M3 12H21 M12 3C15.5 6.5 15.5 17.5 12 21 M12 3C8.5 6.5 8.5 17.5 12 21",
        "Social" => "M9 4A3.5 3.5 0 1 1 8.99 4Z M2.5 20C2.5 15.6 5.4 13.5 9 13.5C12.6 13.5 15.5 15.6 15.5 20 M16 4.5A3 3 0 0 1 16 10.5 M18 13.8C20.2 14.6 21.5 16.6 21.5 20",
        "Launcher" => "M3 5H21V19H3Z M3 9H21 M6 7H6.01 M9 7H9.01",
        "Replays" => "M4 5H20V19H4Z M10 9L15 12L10 15Z",
        _ => "M12 8.5A3.5 3.5 0 1 1 11.99 8.5Z M12 2V5 M12 19V22 M2 12H5 M19 12H22 M4.9 4.9L7 7 M17 17L19.1 19.1 M4.9 19.1L7 17 M17 7L19.1 4.9",
    };
}
