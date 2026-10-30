using Launcher.App.ViewModels;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// A custom lobby's rules as the launcher says them: the summary line names only what differs from Quick Play's (a
/// pack only when it is not Quick Play's own; nothing for a lobby on Quick Play's rules), in the rules' order; a
/// server that names no Quick Play rules is compared with the rules' defaults; the whole list marks what differs.
/// The host's settings send only what differs, and the stat pack always (none is the disc's own stats); the Quick
/// Play preset takes Quick Play's pack. A refusal and a leaving each have their line; a locked row asks first.
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
        var rules = Whole(new() { ["latency-limit"] = 150, ["laps"] = 5 }, "GX+");

        Assert.Equal("5 laps · GX+ · ping limit 150 ms", CustomRules.Summary(rules, QuickPlay));
        Assert.Equal("", CustomRules.Summary(Whole([]), QuickPlay));                          // Quick Play's own
        Assert.Equal("1 lap · no CPUs · stock stats",
            CustomRules.Summary(Whole(new() { ["laps"] = 1, ["cpu-fill"] = (int)CpuFillMode.Never }, pack: null), QuickPlay));
        Assert.Equal("mines stay gone · setting 80-100", CustomRules.Summary(
            Whole(new() { ["machine-setting"] = 80, [LobbyRules.SettingHighKey] = 100, ["mine-regen"] = 0 }), QuickPlay));
    }

    [Fact]
    public void WithoutTheServersQuickPlayRulesTheDefaultsStandIn()
    {
        var rules = new LobbyRules(CustomRules.Defaults.Values.ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal("", CustomRules.Summary(rules, null));
        var five = rules with { Values = rules.Values.ToDictionary(p => p.Key, p => p.Key == "laps" ? 5 : p.Value) };
        Assert.Equal("5 laps", CustomRules.Summary(five, null));
        Assert.Null(rules.StatPack);   // (no word from a server: no pack, nothing of the launcher's own)
        Assert.Equal("GX Unleashed", CustomRules.Summary(rules with { StatPack = "GX Unleashed" }, null));
        Assert.Equal(HostRules.LatencyLimit.Default, CustomRules.Value(null, "latency-limit", null));
    }

    /// <summary>The stats row starts on the server's own value, as the lobby list carries it, and Stock is a choice; a bare
    /// server's is Stock; a mode that states stats, either way, takes the row away and says what is raced; a list of
    /// machines is said.</summary>
    [Fact]
    public void TheStatsRowStartsOnTheServersPackAndAModeThatStatesItTakesTheRow()
    {
        string[] Shown(LobbyRulesEditor e) => [.. e.RuleGroups.SelectMany(g => g.Rows).Select(r => r.Label)];
        var bare = new LobbyRulesEditor(null, new LobbyRules(CustomRules.Defaults.Values), ["GX Unleashed"], "Friday");
        Assert.Null(bare.Rules.StatPack);
        Assert.Equal(HostRuleRows.Stock, Assert.IsType<PackRule>(bare.Rule(HostRules.StatPack.Name)).Chosen);
        Assert.Null(new LobbyRulesEditor(null, null, ["GX Unleashed"], "Friday").Rules.StatPack);   // (no word yet: never the launcher's own)

        var start = new LobbyRulesEditor(null, QuickPlay, [], "Friday");
        Assert.Equal("GX Unleashed", start.Rules.StatPack);
        Assert.Equal("Same as Quick Play", start.SummaryShown);
        var row = Assert.IsType<PackRule>(start.Rule(HostRules.StatPack.Name));
        row.Selected = 0;
        Assert.Null(start.Rules.StatPack);
        Assert.Equal("stock stats", start.Summary);

        LobbyMode Mode(string? pack) => new(new string('a', 64), "AAAAAAAA", "M", 1, Pack: pack, Machines: [6]);
        foreach (var (pack, raced) in new[] { ("", HostRuleRows.Stock), ("GX+", "GX+") })
        {
            var rules = new LobbyRules(new Dictionary<string, int>(), "GX Unleashed", Mode: Mode(pack));
            Assert.DoesNotContain(HostRules.StatPack.Label, Shown(new LobbyRulesEditor(rules, null, [], "Friday", editing: true)));
            var all = CustomRules.AllRules(rules, null).SelectMany(g => g.Rows).OfType<ValueRule>().ToList();
            Assert.Equal(raced, all.Single(r => r.Label == HostRules.StatPack.Label).Value);
            Assert.Equal("Blue Falcon only", all.Single(r => r.Label == "Machines").Value);
        }
        var open = new LobbyRules(new Dictionary<string, int>(), Mode: Mode(null));
        Assert.Contains(HostRules.StatPack.Label, Shown(new LobbyRulesEditor(open, null, [], "Friday", editing: true)));
        Assert.Equal("M · Blue Falcon only", CustomRules.Summary(open, null));
        Assert.Equal("", CustomRules.MachinesLine(null));
        Assert.Equal("", CustomRules.MachinesLine([.. Enumerable.Range(0, Machines.Names.Count)]));
        Assert.Equal("2 machines", CustomRules.MachinesLine([2, 6]));
    }

    [Fact]
    public void AFreshPracticeStartsOnItsPackIfHeldAndASavedOneKeepsItsChoice()
    {
        Assert.Equal(PracticeSetup.FreshStatPack, PracticeSetup.Fresh(["F-Zero GX+", PracticeSetup.FreshStatPack]).StatPack);
        Assert.Null(PracticeSetup.Fresh(["F-Zero GX+"]).StatPack);   // (its file absent: Stock)
        Assert.Null(PracticeSetup.Fresh([]).StatPack);
        Assert.Null(PracticeSetup.Default.StatPack);
        Assert.Equal("F-Zero GX+", (PracticeSetup.Default with { StatPack = "F-Zero GX+" }).Kept().StatPack);
        Assert.Null(PracticeSetup.Default.Kept().StatPack);          // (a saved Stock stays Stock)

        // the name is a pack of the repository's, by its own name
        var names = Directory.EnumerateFiles(TestSupport.RepositoryFolder.Protocol("protocol", "packs"), "*" + StatPackText.Extension)
            .Select(f => StatPackText.Read(File.ReadAllText(f)).Pack?.Name);
        Assert.Contains(PracticeSetup.FreshStatPack, names);
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

    [Fact]
    public void TheHostsSettingsSendWhatDiffersAndThePackAlways()
    {
        var editor = new LobbyRulesEditor(null, QuickPlay, ["GX+"], CustomLobbiesViewModel.DefaultName("ANA"));

        Assert.Equal("ANA's lobby", editor.Name);
        Assert.Empty(editor.Rules.Values);
        Assert.Equal("GX Unleashed", editor.Rules.StatPack);                                  // the Quick Play preset: its pack
        Assert.Equal("Same as Quick Play", editor.SummaryShown);

        ((LapsRule)editor.Rule("laps")).Value = 5;
        ((RangeRule)editor.Rule("machine-setting")).Low = 80;
        Assert.Equal(new Dictionary<string, int> { ["laps"] = 5, ["machine-setting"] = 80, [LobbyRules.SettingHighKey] = 100 },
            editor.Rules.Values.ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal("5 laps · setting 80-100", editor.Summary);

        // a preset: every rule back to Quick Play's, with its pack
        editor.PresetRow.Selected = editor.Presets.ToList().IndexOf("GX+ stats");
        Assert.Empty(editor.Rules.Values);
        Assert.Equal("GX+", editor.Rules.StatPack);
        Assert.Equal("GX+", editor.Summary);
        editor.PresetRow.Selected = 0;
        Assert.Equal("GX Unleashed", editor.Rules.StatPack);
    }

    private const string MyPack = "fzstats 1\nname My Pack\n# heavier\nmachine 6 Weight 2520\n";

    [Fact]
    public void AHostsOwnPackFileIsOfferedByItsNameAndSentWhole()
    {
        var dir = Directory.CreateTempSubdirectory("host-pack").FullName;
        try
        {
            var (good, bad) = (Path.Combine(dir, "mine.fzstats"), Path.Combine(dir, "bad.fzstats"));
            File.WriteAllText(good, MyPack);
            File.WriteAllText(bad, "fzstats 1\nname Bad\nmachine 6 Weight -1e30\n");
            var editor = new LobbyRulesEditor(null, QuickPlay, ["GX+"], "Friday");
            var row = Assert.IsType<PackRule>(editor.Rule(HostRules.StatPack.Name));
            Assert.Equal(["Stock", "GX Unleashed", "GX+"], row.Options);
            Assert.Null(editor.Rules.PackFile);

            row.Load(bad);                                                                    // no pack: said, nothing chosen
            Assert.StartsWith("Not a stat pack: ", row.Note, StringComparison.Ordinal);
            Assert.Equal(("GX Unleashed", null), (editor.Rules.StatPack, editor.Rules.PackFile));

            row.Load(good);                                                                   // the file: its pack's name, last
            Assert.Equal("", row.Note);
            Assert.Equal(["Stock", "GX Unleashed", "GX+", "My Pack"], row.Options);
            Assert.Equal(("My Pack", MyPack), (editor.Rules.StatPack, editor.Rules.PackFile));
            Assert.Equal("My Pack", editor.Summary);
            row.Selected = 2;                                                                 // a server's pack again
            Assert.Equal(("GX+", null), (editor.Rules.StatPack, editor.Rules.PackFile));
            editor.PresetRow.Selected = 0;                                                    // a preset: the file stays offered
            Assert.Equal("My Pack", Assert.IsType<PackRule>(editor.Rule(HostRules.StatPack.Name)).Options[^1]);

            // the host's lobby, its rules naming the pack the host sent: the panel opens on the file
            var again = new LobbyRulesEditor(new LobbyRules(QuickPlay.Values, "My Pack"), QuickPlay, ["GX+"], "Friday", editing: true,
                file: new HostPackFile("My Pack", MyPack));
            Assert.True(Assert.IsType<PackRule>(again.Rule(HostRules.StatPack.Name)).FileChosen);
            Assert.Equal(("My Pack", MyPack), (again.Rules.StatPack, again.Rules.PackFile));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

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
        var rules = Whole(new() { ["cpu-fill"] = (int)CpuFillMode.Always, ["course-select"] = (int)CourseSelectMode.Fixed,
            [LobbyRules.FixedCourseKey] = 1, ["boost-start"] = 1 });
        var editor = new LobbyRulesEditor(rules, QuickPlay, [], "Friday", locked: true, editing: true);

        Assert.Equal(1, ((SegmentedRule)editor.Rule("cpu-fill")).Selected);
        Assert.Equal((2, 1, false), editor.CourseRow is var course ? (course.Selected, course.Course, course.Picking) : default);
        Assert.True(editor.LockedRow.On);
        Assert.Equal(new Dictionary<string, int>
        {
            ["cpu-fill"] = 1, ["course-select"] = 2, [LobbyRules.FixedCourseKey] = 1, ["boost-start"] = 1,
        }, editor.Rules.Values.ToDictionary(p => p.Key, p => p.Value));
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
        Assert.Equal(Launcher.Core.Courses.Name(16), editor.PresetRow.Note);

        course.Selected = (int)CourseSelectMode.Random;
        Assert.Null(editor.Rules.Value(LobbyRules.FixedCourseKey));
        Assert.Equal("random course", editor.Summary);
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
