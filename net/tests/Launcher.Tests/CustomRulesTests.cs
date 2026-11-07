using Launcher.App.ViewModels;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// A custom lobby's rules as the launcher says them: the summary line names only what differs from Quick Play's (a
/// pack only when it is not Quick Play's own; nothing for a lobby on Quick Play's rules), in the rules' order; a
/// server that names no Quick Play rules is compared with the rules' defaults; the whole list marks what differs.
/// The host picks a mode (the server's official ones, or a file), then its settings; beside it they send only the lobby
/// settings that differ, never a rule of the race the mode owns, and a pack only as their own file. A refusal and a
/// leaving each have their line; a locked row asks first.
/// </summary>
public sealed class CustomRulesTests
{
    /// <summary>Quick Play's rules whole, with a pack, as a server's list carries them.</summary>
    private static readonly LobbyRules QuickPlay = new(CustomRules.Defaults.Values, "GX Unleashed");

    private static LobbyRules Whole(Dictionary<string, int> changes, string? pack = "GX Unleashed")
    {
        var values = QuickPlay.Values.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (key, value) in changes)
        {
            values[key] = value;
        }
        return new LobbyRules(values, pack);
    }

    [Fact]
    public void TheSummaryNamesOnlyWhatDiffersInTheRulesOrder()
    {
        var rules = Whole(new() { ["latency-limit"] = 150, ["racers"] = 12 }, "GX+");

        Assert.Equal("12 racers · Latency limit 150 ms", CustomRules.Summary(rules, QuickPlay));
        Assert.Equal("", CustomRules.Summary(Whole([]), QuickPlay));                          // Quick Play's own
        // (the race's rules are the mode's, said with its name: the lobby's summary names none of them)
        Assert.Equal("", CustomRules.Summary(Whole(new() { ["laps"] = 1, ["cpu-fill"] = (int)CpuFillMode.Never }, pack: null), QuickPlay));
        Assert.Equal("Course vote 20 s · Voting minimum 4", CustomRules.Summary(
            Whole(new() { ["vote-seconds"] = 20, ["voting-minimum"] = 4, ["mine-regen"] = 0 }), QuickPlay));
    }

    [Fact]
    public void WithoutTheServersQuickPlayRulesTheDefaultsStandIn()
    {
        var rules = new LobbyRules(CustomRules.Defaults.Values.ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal("", CustomRules.Summary(rules, null));
        var twelve = rules with { Values = rules.Values.ToDictionary(p => p.Key, p => p.Key == "racers" ? 12 : p.Value) };
        Assert.Equal("12 racers", CustomRules.Summary(twelve, null));
        Assert.Null(rules.StatPack);   // (no word from a server: no pack, nothing of the launcher's own)
        Assert.Equal(HostRules.LatencyLimit.Default, CustomRules.Value(null, "latency-limit", null));
    }

    /// <summary>The server's official modes as its list names them: Quick Play's rules, those with GX+ stats, Knockout with
    /// its settings.</summary>
    private static readonly LobbyMode Plain = new(new string('f', 64), "FFFFFFFF", "Quick Play", 1, About: "Quick Play's rules: laps by course, GX Unleashed stats.",
        Terms: new Dictionary<string, int> { ["finish-limit"] = 90 });

    private static readonly LobbyMode PlusStats = new(new string('b', 64), "BBBBBBBB", "Quick Play with GX+ stats", 1,
        Terms: new Dictionary<string, int> { ["finish-limit"] = 90 }, Pack: "GX+");

    private static readonly LobbyMode Knockout = new(new string('c', 64), "CCCCCCCC", "Knockout", 1, "KNOCKOUT", "Out at every cut.",
        Terms: new Dictionary<string, int> { ["laps"] = 0, ["finish-limit"] = 0 },
        Params: [new ModeParam("keep", "Racers left", ModeParamKind.Number, ModeType.Int, 1, 29, 1, 1, [])], Values: [1], Courses: [8]);

    private static readonly IReadOnlyList<LobbyMode> Official = [Plain, PlusStats, Knockout];

    /// <summary>The stats are the mode's: no stats row beside it. Its stats parameter says stock and file in words; on
    /// the file it asks for the host's own pack file (Choose file…), sent whole with its name; another choice sends none.
    /// What a lobby races is said in its whole list.</summary>
    [Fact]
    public void TheStatsAreTheModesParameterAndItsFileChoiceAsksForTheFile()
    {
        var dir = Directory.CreateTempSubdirectory("host-pack").FullName;
        try
        {
            var (good, bad) = (Path.Combine(dir, "mine.fzstats"), Path.Combine(dir, "bad.fzstats"));
            File.WriteAllText(good, MyPack);
            File.WriteAllText(bad, "fzstats 1\nname Bad\nmachine 6 Weight -1e30\n");
            var editor = new LobbyRulesEditor(null, QuickPlay, [WithStats], "Friday");
            Assert.DoesNotContain(HostRules.StatPack.Label, editor.RuleGroups.SelectMany(g => g.Rows).Select(r => r.Label));
            var stats = Assert.IsType<ChoiceRule>(editor.ParamRows.Single(r => r.Stats).Row);
            Assert.Equal(["GX Unleashed", ModeParamRow.StockWords, ModeParamRow.FileWords], stats.Options);
            Assert.False(editor.PackFileShown);
            Assert.Equal((null, null), (editor.Rules.StatPack, editor.Rules.PackFile));

            stats.Selected = 2;                                                              // the host's own file
            Assert.True(editor.PackFileShown);
            Assert.Contains(editor.PackFileRow, editor.CreateGroups.SelectMany(g => g.Rows));
            editor.PackFileRow.Load(bad);                                                    // no pack: said, nothing sent
            Assert.StartsWith("Not a stat pack: ", editor.PackFileRow.Note, StringComparison.Ordinal);
            Assert.Equal((null, null), (editor.Rules.StatPack, editor.Rules.PackFile));
            editor.PackFileRow.Load(good);
            Assert.Equal(("My Pack", MyPack), (editor.Rules.StatPack, editor.Rules.PackFile));
            Assert.Null(editor.Rules.OutsideTheMode());
            stats.Selected = 1;                                                              // stock: no file sent
            Assert.False(editor.PackFileShown);
            Assert.Equal((null, null), (editor.Rules.StatPack, editor.Rules.PackFile));
            Assert.Equal("Quick Play · Stats Stock", editor.Summary);

            // the host's lobby on the file: the panel opens on it
            var again = new LobbyRulesEditor(new LobbyRules(QuickPlay.Values, "My Pack", Mode: WithStats with { Values = [2], Pack = null }), QuickPlay,
                [WithStats], "Friday", editing: true, file: new HostPackFile("My Pack", MyPack));
            Assert.True(again.PackFileShown);
            Assert.Equal(("My Pack", MyPack), (again.Rules.StatPack, again.Rules.PackFile));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        LobbyMode Mode(string? pack) => new(new string('d', 64), "DDDDDDDD", "M", 1, Pack: pack, Machines: [6]);
        foreach (var (pack, raced) in new[] { ("", HostRuleRows.Stock), ("GX+", "GX+") })
        {
            var rules = new LobbyRules(new Dictionary<string, int>(), Mode: Mode(pack));
            var all = CustomRules.AllRules(rules, null).SelectMany(g => g.Rows).OfType<ValueRule>().ToList();
            Assert.Equal(raced, all.Single(r => r.Label == HostRules.StatPack.Label).Value);
            Assert.Equal("Blue Falcon only", all.Single(r => r.Label == "Machines").Value);
        }
    }

    /// <summary>Quick Play's normal mode with its stats parameter, as a server's list names it.</summary>
    private static readonly LobbyMode WithStats = new(new string('e', 64), "EEEEEEEE", "Quick Play", 1,
        Terms: new Dictionary<string, int> { ["finish-limit"] = 90 }, Pack: "GX Unleashed",
        Params: [new ModeParam("pack", "Stats", ModeParamKind.Choice, ModeType.Int, 0, 2, 1, 0, ["GX Unleashed", "stock", "file"])], Values: [0],
        PackParam: 0);

    /// <summary>An optional parameter's row: a whole number beside its named value ("By course"), on it by default; a
    /// number moves it off, and the value sent is the number, or the named value's cell.</summary>
    [Fact]
    public void AnOptionalParameterIsANamedValueBesideItsNumber()
    {
        var laps = new ModeParam("lap_count", "Laps", ModeParamKind.Number, ModeType.Int, 1, 100, 1, 100, [], "By course");
        var mode = new LobbyMode(new string('9', 64), "99999999", "Quick Play", 1, Params: [laps], Values: [100]);
        var editor = new LobbyRulesEditor(null, QuickPlay, [mode], "Friday");
        var row = Assert.IsType<NamedNumberRule>(editor.ParamRows[0].Row);
        Assert.Equal(("By course", true, 1, 99), (row.Named, row.IsNamed, row.Minimum, row.Maximum));
        Assert.Equal([100], editor.Rules.ModeValues!);
        Assert.Equal("Quick Play", editor.Summary);
        row.IsNamed = false;
        row.Value = 5;
        Assert.Equal([5], editor.Rules.ModeValues!);
        Assert.Equal("Quick Play · Laps 5", editor.Summary);
        Assert.Equal("By course", ModeParamRow.Text(laps, 100));
    }

    [Fact]
    public void AFreshPracticeStartsOnStockAndASavedOneKeepsItsChoice()
    {
        Assert.Null(PracticeSetup.Default.StatPack);
        Assert.Equal("F-Zero GX+", (PracticeSetup.Default with { StatPack = "F-Zero GX+" }).Kept().StatPack);
        Assert.Null(PracticeSetup.Default.Kept().StatPack);          // (a saved Stock stays Stock)
    }

    [Fact]
    public void TheDefaultsAreTheServicesQuickPlay()
    {
        // the race's rules and the machine setting are rules.toml's defaults, as the service's; the lobby's times,
        // which rules.toml gives none, are the service's own (not each rule's least)
        var rules = CustomRules.Defaults;
        Assert.Equal(RaceRules.QuickPlay, QuickPlayDefaults.Rules.Race);
        Assert.Equal(SettingRange.QuickPlay, QuickPlayDefaults.Rules.MachineSetting);
        Assert.All(HostRules.Race, rule => Assert.Equal(RaceRules.QuickPlay.Value(rule), rules.Value(rule)));
        Assert.Equal((10, 40, 10), (rules.Value(HostRules.VoteSeconds), rules.Value(HostRules.ChooseSeconds),
            rules.Value(HostRules.VotingMinimum)));
        Assert.Equal("", CustomRules.Summary(new(new Dictionary<string, int> { ["vote-seconds"] = 10, ["choose-seconds"] = 40 }), null));
    }

    [Fact]
    public void TheWholeListMarksWhatDiffers()
    {
        var groups = CustomRules.AllRules(Whole(new() { ["laps"] = 5 }), QuickPlay);

        Assert.Equal(["Race", "Machines", "Lobby", "Connection"], groups.Select(g => g.Heading));
        var rows = groups.SelectMany(g => g.Rows).OfType<ValueRule>().ToList();
        Assert.Equal(LobbyRules.Settable.Count, rows.Count);                                  // every rule a host sets
        Assert.Equal([("Laps", "5")], rows.Where(r => r.Marked).Select(r => (r.Label, r.Value)));
        Assert.Equal("GX Unleashed", rows.Single(r => r.Label == "Stats").Value);
    }

    /// <summary>Pick a mode, then its settings: a new lobby opens on the server's first official mode (Quick Play's rules),
    /// sent by its hash with no values of the race; a lobby setting changed is sent; another mode brings its settings on
    /// their defaults; nothing the mode owns is ever sent, so the server never refuses the rules for it.</summary>
    [Fact]
    public void AHostPicksAModeThenItsSettings()
    {
        var editor = new LobbyRulesEditor(null, QuickPlay, Official, CustomLobbiesViewModel.DefaultName("ANA"));

        Assert.Equal("ANA's lobby", editor.Name);
        Assert.Equal(["Quick Play", "Quick Play with GX+ stats", "Knockout"], editor.ModeRow.Choices.Select(c => c.Name));
        Assert.Equal("Quick Play's rules: laps by course, GX Unleashed stats.", editor.ModeRow.Chosen!.About);
        Assert.Empty(editor.Rules.Values);
        Assert.Equal((Plain.Hash, null), (editor.Rules.Mode?.Hash, editor.Rules.ModeFile));
        Assert.Null(editor.Rules.OutsideTheMode());
        Assert.Equal("Quick Play", editor.Summary);
        Assert.Empty(editor.ParamRows);
        Assert.Equal(["Name", "Password"], editor.CreateGroups[0].Rows.Select(r => r.Label));
        Assert.Equal(("Quick Play", "Settings"), (editor.CreateGroups[1].Heading, editor.CreateGroups[1].Rows[0].Label));   // (this mode has none)

        // only the lobby's own settings are the host's, in their groups: no CPU racers, teams or stats (the mode's)
        Assert.Equal(["Lobby", "Connection", "Course"], editor.RuleGroups.Select(g => g.Heading));
        Assert.Equal(["Racers", "Course vote", "Machine choice", "Voting minimum", "Latency limit", "Course"],
            editor.RuleGroups.SelectMany(g => g.Rows).Select(r => r.Label));
        ((StepperRule)editor.Rule("racers")).Value = 12;
        ((StepperRule)editor.Rule("latency-limit")).Value = 150;
        Assert.Equal(new Dictionary<string, int> { ["racers"] = 12, ["latency-limit"] = 150 }, editor.Rules.Values.ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal("Quick Play · 12 racers · Latency limit 150 ms", editor.Summary);

        // another mode: its settings on their defaults, its courses; the lobby's settings stay
        editor.ModeRow.Selected = 2;
        Assert.Equal(["Racers left"], editor.ParamRows.Select(p => p.Row.Label));
        Assert.Equal("Knockout", editor.Rules.Mode?.Name);
        Assert.Equal([1], editor.Rules.ModeValues!);
        Assert.Equal([8], editor.CourseRow.Picker.Cards.Select(c => c.Course));
        ((StepperRule)editor.ParamRows[0].Row).Value = 3;
        Assert.Equal([3], editor.Rules.Mode!.Values!);
        Assert.Equal(12, editor.Rules.Value("racers"));
        Assert.Null(editor.Rules.OutsideTheMode());
        Assert.Equal("Knockout", editor.CreateGroups[1].Heading);
    }

    /// <summary>The host's lobby on one of the official modes opens on it with the lobby's values; on the host's own
    /// file the server holds, that file is a choice of its own after the official ones.</summary>
    [Fact]
    public void TheHostsLobbyOpensOnItsMode()
    {
        var onKnockout = new LobbyRules(new Dictionary<string, int>(), Mode: Knockout with { Values = [5] });
        var editor = new LobbyRulesEditor(onKnockout, QuickPlay, Official, "Friday", editing: true);
        Assert.Equal((2, 3), (editor.ModeRow.Selected, editor.ModeRow.Options.Count));
        Assert.Equal(5, editor.ParamRows[0].Value);
        Assert.Contains(editor.ModeRow, editor.LobbyGroups.SelectMany(g => g.Rows));

        var own = new LobbyMode(new string('e', 64), "EEEEEEEE", "Overdrive", 1);
        var mine = new LobbyRulesEditor(new LobbyRules(new Dictionary<string, int>(), Mode: own), QuickPlay, Official, "Friday", editing: true);
        Assert.Equal(["Quick Play", "Quick Play with GX+ stats", "Knockout", "Overdrive"], mine.ModeRow.Options);
        Assert.Equal((3, own.Hash), (mine.ModeRow.Selected, mine.Rules.Mode?.Hash));
        mine.ModeRow.Selected = 0;
        Assert.Equal(Plain.Hash, mine.Rules.Mode?.Hash);

        // a server that lists no modes (one from before them): Quick Play's rules, no mode
        var bare = new LobbyRulesEditor(null, QuickPlay, [], "Friday");
        Assert.Equal([GameModeRule.NoMode], bare.ModeRow.Options);
        Assert.Null(bare.Rules.Mode);
    }

    private const string MyPack = "fzstats 1\nname My Pack\n# heavier\nmachine 6 Weight 2520\n";

    [Fact]
    public void APackFileTooLongAsJsonIsSaidAndNotSent()
    {
        // within the characters a pack file may be, but outside ASCII: six bytes a character as JSON, past what the
        // server reads
        var accented = MyPack + "# " + new string('\u00e9', (HostPackFile.MostSentBytes / 6) + 1) + "\n";
        Assert.True(accented.Length <= LobbyRules.MostPackFile);
        Assert.Equal((null, "That file is too big to send: shorten its comments."), HostPackFile.Of(accented));
        // the same length in plain letters is sent
        var plain = MyPack + "# " + new string('e', (HostPackFile.MostSentBytes / 6) + 1) + "\n";
        Assert.Equal("My Pack", HostPackFile.Of(plain).File?.Name);
    }

    [Fact]
    public void ALobbysOwnRulesOpenTheHostsPanel()
    {
        var rules = Whole(new() { ["racers"] = 12, ["course-select"] = (int)CourseSelectMode.Fixed,
            [LobbyRules.FixedCourseKey] = 1, ["voting-minimum"] = 4, ["cpu-fill"] = (int)CpuFillMode.Always });
        var editor = new LobbyRulesEditor(rules, QuickPlay, [], "Friday", locked: true, editing: true);

        Assert.Equal(12, ((StepperRule)editor.Rule("racers")).Value);
        Assert.Equal((2, 1, false), editor.CourseRow is var course ? (course.Selected, course.Course, course.Picking) : default);
        Assert.True(editor.LockedRow.On);
        // (the CPUs are the mode's: an old lobby's cpu-fill is no row and goes with no rules)
        Assert.Equal(new Dictionary<string, int>
        {
            ["racers"] = 12, ["course-select"] = 2, [LobbyRules.FixedCourseKey] = 1, ["voting-minimum"] = 4,
        }, editor.Rules.Values.ToDictionary(p => p.Key, p => p.Value));
        Assert.Null(editor.Rules.OutsideTheMode());   // (the lobby's own settings alone)
    }

    [Fact]
    public void AFixedCourseIsPickedInTheOneCoursePickerAndNamedInTheSummary()
    {
        var editor = new LobbyRulesEditor(null, QuickPlay, [], "Friday");
        var course = editor.CourseRow;
        Assert.Equal(["Vote", "Random", "Fixed"], course.Options);
        Assert.False(editor.Picking);

        course.Selected = CourseRule.Fixed;                                                   // Fixed opens the picker
        Assert.True(editor.Picking);
        Assert.Equal(PickMode.One, course.Picker.Mode);
        course.Picker.Pick(course.Picker.Cards.Single(c => c.Course == 16));               // a course pressed: the lobby's
        Assert.False(editor.Picking);
        Assert.Equal((int)CourseSelectMode.Fixed, editor.Rules.Value(HostRules.CourseSelect));
        Assert.Equal(16, editor.Rules.Value(LobbyRules.FixedCourseKey));
        Assert.Equal(Launcher.Core.Courses.Name(16), editor.Summary);

        course.Selected = (int)CourseSelectMode.Random;
        Assert.Null(editor.Rules.Value(LobbyRules.FixedCourseKey));
        Assert.Equal("Course Random", editor.Summary);
    }

    private static LobbyRules ModeRules(string words)
    {
        var compiled = ModeCompiler.Compile("mode \"M\" { api 1; }\n" + words + "\n");
        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Errors));
        var terms = ModeTerms.Of(compiled.Mode!);
        return new LobbyRules(new Dictionary<string, int>(), Mode: new LobbyMode(new string('a', 64), "AAAAAAAA", "M", 1, Terms: terms.Rules(),
            Courses: terms.Courses is { Count: > 0 } courses ? courses : null));
    }

    /// <summary>A mode that lists courses: the row stays (Vote, Random, Fixed), its picker offers the list alone, and
    /// the lobby's line and whole list say so; a mode that pins one has no row; neither word, every course.</summary>
    [Fact]
    public void TheCourseRowStaysWithinTheModesCourses()
    {
        var listed = new LobbyRulesEditor(ModeRules("courses [\"Mute City: Sonic Oval\", \"Casino Palace: Split Oval\"];"), QuickPlay, [], "Friday", editing: true);
        Assert.Contains(listed.RuleGroups.SelectMany(g => g.Rows), r => r is CourseRule);
        Assert.Equal([16, 36], listed.CourseRow.Picker.Cards.Select(c => c.Course).Order());
        listed.CourseRow.Selected = CourseRule.Fixed;
        Assert.True(listed.CourseRow.Course is 16 or 36);
        Assert.True(listed.Rules.Value(LobbyRules.FixedCourseKey) is 16 or 36);
        Assert.Contains("2 courses", CustomRules.Summary(listed.Rules, QuickPlay), StringComparison.Ordinal);
        Assert.Equal("2 courses", CustomRules.AllRules(listed.Rules, QuickPlay).SelectMany(g => g.Rows).OfType<ValueRule>().Single(r => r.Label == "Courses").Value);

        var one = new LobbyRulesEditor(ModeRules("courses [\"Mute City: Sonic Oval\"];"), QuickPlay, [], "Friday", editing: true);
        Assert.Equal(36, Assert.Single(one.CourseRow.Picker.Cards).Course);
        Assert.Equal(CustomRules.CoursesLine([36]), Launcher.Core.Courses.Name(36) + " only");

        var pinned = new LobbyRulesEditor(ModeRules("course \"Mute City: Sonic Oval\";"), QuickPlay, [], "Friday", editing: true);
        Assert.DoesNotContain(pinned.RuleGroups.SelectMany(g => g.Rows), r => r is CourseRule);

        foreach (var words in new[] { "", "courses all;" })
        {
            var free = new LobbyRulesEditor(ModeRules(words), QuickPlay, [], "Friday", editing: true);
            Assert.Contains(free.RuleGroups.SelectMany(g => g.Rows), r => r is CourseRule);
            Assert.Equal(CourseCups.All.Count, free.CourseRow.Picker.Cards.Count);
        }
    }

    /// <summary>A fixed course from before the mode moves into its list; taking the mode off opens the row again.</summary>
    [Fact]
    public void AFixedCourseFollowsTheListWhenTheModeComesAndGoes()
    {
        var start = ModeRules("courses [\"Mute City: Sonic Oval\"];");
        var rules = start with { Values = new Dictionary<string, int> { ["course-select"] = (int)CourseSelectMode.Fixed, [LobbyRules.FixedCourseKey] = 16 } };
        var editor = new LobbyRulesEditor(rules, QuickPlay, [Plain], "Friday", editing: true);
        Assert.Equal(36, editor.CourseRow.Course);          // (16 is outside the list)
        Assert.Equal(36, editor.Rules.Value(LobbyRules.FixedCourseKey));
        editor.ModeRow.Selected = 0;                        // Quick Play's rules, no list
        Assert.Equal(CourseCups.All.Count, editor.CourseRow.Picker.Cards.Count);
        Assert.Contains(editor.RuleGroups.SelectMany(g => g.Rows), r => r == editor.CourseRow);
        Assert.Equal(36, editor.Rules.Value(LobbyRules.FixedCourseKey));
    }

    [Theory]
    [InlineData(CustomLobby.BadPassword, "Wrong password")]
    [InlineData(CustomLobby.LobbyFull, "Lobby full")]
    [InlineData(CustomLobby.LobbyClosed, "Lobby closed to new players")]
    [InlineData(CustomLobby.NoSuchLobby, "Lobby gone")]
    [InlineData(CustomLobby.NotHost, "Only the host can do that")]
    [InlineData(CustomLobby.BadRules, "Rules not taken")]
    [InlineData(CustomLobby.RulesLocked, "Rules change only while the lobby waits")]
    [InlineData(CustomLobby.BadName, "Lobby name not taken")]
    public void EachRefusalHasItsLine(string code, string line)
    {
        Assert.Equal(line, CustomLobbiesViewModel.RefusalText(code));
        Assert.Contains(code, QuickPlaySession.CustomLobbyRefusals);
    }

    [Fact]
    public void ALeavingThatWasNotThePlayersHasItsLine()
    {
        Assert.Equal("Removed from the lobby by the host", CustomLobbiesViewModel.LeftText(CustomLobby.Kicked));
        Assert.Equal("Lobby closed", CustomLobbiesViewModel.LeftText(CustomLobby.Closed));
    }

    [Fact]
    public void ARowSaysItsLobby()
    {
        var row = CustomLobbiesViewModel.Row(new LobbySummary("l9", "Friday cup", "ZOE", 6, 12, true,
            Whole(new() { ["racers"] = 12 }), LobbyState.Racing), QuickPlay);

        Assert.Equal(new CustomLobbyRow("l9", "Friday cup", "ZOE", "6/12", true, "Racing", true, "12 racers"), row);
        Assert.Equal("Waiting", CustomLobbiesViewModel.StageText(LobbyState.Forming));
        Assert.Equal("Starting", CustomLobbiesViewModel.StageText(LobbyState.Choosing));
        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWX's lobby", CustomLobbiesViewModel.DefaultName("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
    }
}
