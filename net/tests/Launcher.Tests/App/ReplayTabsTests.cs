using System.Buffers.Binary;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The Replays page's tabs: All, Races and Practice with their
/// counts, pressed with the headless window's mouse; a practice run's row says "Practice" with the time, and has no
/// results to open. A practice run is a replay whose header says <c>"practice": true</c>. A row's "•••" actions
/// are open on one row at a time, and a row deleted leaves the others as they were.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class ReplayTabsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Replays => Path.Combine(_dir, "data", "replays");

    private const string Results = """
        {"results":[{"seat":0,"rank":1,"finished":true,"out":false,"laps":3,"timeMs":131930,"bestLapMs":43001}]}
        """;

    /// <summary>A replay of one racer, with results; <paramref name="practice"/>: the header's word, as written.</summary>
    private void Write(string name, string recorded, string? practice, string mode = "", byte version = 2)
    {
        var header = $$$"""
            {"match": {"matchId": "x", "seat": 0, "setup": {"course": 5, "laps": 3, "seats": [
              {"seat": 0, "playerId": "p1", "name": "ANA", "machine": 1}]{{{mode}}}}},
             "seat": 0, "mask": 1, "delay": 2, "recorded": "{{{recorded}}}"{{{(practice is null ? "" : ", \"practice\": " + practice)}}}}
            """;
        Directory.CreateDirectory(Replays);
        var json = Encoding.UTF8.GetBytes(header);
        using var f = File.Create(Path.Combine(Replays, name));
        var w = new BinaryWriter(f);
        Span<byte> u32 = stackalloc byte[4];
        w.Write("FZRP"u8);
        w.Write((byte)0);
        w.Write(version);
        BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)json.Length);
        w.Write(u32);
        w.Write(json);
        w.Write("FZRF"u8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 1);
        w.Write(u32);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 0);
        w.Write(u32);
        w.Write("FZRL"u8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 122);
        w.Write(u32);
        w.Write(new byte[122]);
        w.Write(new byte[600 * 10]);
        var block = Encoding.UTF8.GetBytes(Results);
        w.Write(block);
        BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)block.Length);
        w.Write(u32);
        w.Write("FZRS"u8);
    }

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), """{"noServer": true, "soundMuted": true}""");
        return AppState.Load(data);
    }

    /// <summary>The page's read of the folder done (it reads off the window's thread) and shown.</summary>
    private static void Loaded(ReplaysViewModel page)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (page.Loading && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.False(page.Loading, "the replays were not read in 10 s");
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A mode's race with teams: each racer's team in the results; anything but a list of teams: none.</summary>
    [Theory]
    [InlineData(", \"mode\": {\"teams\": [1, 0]}", "Team 2")]
    [InlineData(", \"mode\": {\"teams\": []}", "Team 1")]   // (a seat past the list: its own number)
    [InlineData(", \"mode\": {\"teams\": [\"x\"]}", "")]
    [InlineData(", \"mode\": {\"teams\": 3}", "")]
    [InlineData(", \"mode\": {}", "")]
    [InlineData("", "")]
    public void AReplaysResultsSayEachRacersTeam(string mode, string says)
    {
        Write("t.fzrp", "2026-10-18T10:00:00+00:00", null, mode);
        Assert.Equal(says, ReplayWords.Results(Assert.Single(ReplayFile.List(Replays))).Single().Team);
    }

    /// <summary>The Results of a row, drawn with the racer's team. LOBBY_SHOTS keeps it.</summary>
    [Fact]
    public async Task TheResultsShowTheTeam()
    {
        Write("t.fzrp", DateTimeOffset.Now.ToString("o"), null, ", \"mode\": {\"teams\": [1]}");
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var page = new ReplaysViewModel(state, () => DateTimeOffset.Now);
                var window = new Window { Content = new ReplaysView { DataContext = page }, Width = 1100, Height = 700 };
                window.Show();
                try
                {
                    Loaded(page);
                    Click(window, window.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == "Results" && b.IsEffectivelyVisible));
                    Dispatcher.UIThread.RunJobs();
                    Assert.Contains("Team 2", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
                    if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is { Length: > 0 } folder)
                    {
                        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, "replay-results-teams.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
                }
                finally
                {
                    window.Close();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    /// <summary>A mode's race (version 9, the package in its header): "•••" offers Show mode file, which opens the file
    /// under the row as it was raced, its name with its share code, its settings' values and its text (language 1); a
    /// row without a mode offers none. LOBBY_SHOTS keeps the offer and the file.</summary>
    [Fact]
    public async Task AModesReplayShowsItsModeFile()
    {
        var text = File.ReadAllText(TestSupport.RepositoryFolder.Protocol("protocol", "quickplay", "ghost_race.fzmode"));
        var start = Online.Protocol.ModeCompiler.Compile(text).Mode!.Start();
        Write("g.fzrp", DateTimeOffset.Now.ToString("o"), null,
            $$""", "mode": {"package": "{{start.Package}}", "hash": "{{start.Hash}}", "api": {{start.Api}}, "params": [1, 0, 0]}""", version: 9);
        Write("n.fzrp", DateTimeOffset.Now.AddMinutes(-1).ToString("o"), null);
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                // (the launcher's own window, so the page is drawn on its theme as a player sees it)
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
                window.Show();
                main.Current = main.Replays;
                var page = main.Replays;
                try
                {
                    Loaded(page);
                    var (mode, none) = (page.Items[0], page.Items[1]);
                    Assert.Equal((true, false), (mode.HasModeFile, none.HasModeFile));
                    page.ToggleMoreCommand.Execute(mode);
                    Dispatcher.UIThread.RunJobs();
                    var offers = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "ModeFileButton").ToList();
                    Assert.Equal([true, false], offers.Select(b => b.IsEffectivelyVisible));
                    // the file's actions take the result's room, and the row's columns stay where the others' are
                    Assert.Equal((false, true), (mode.ResultShown, none.ResultShown));
                    var racers = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == "ANA" && t.IsEffectivelyVisible)
                        .Select(t => Math.Round(Avalonia.VisualExtensions.TranslatePoint(t, default, window)!.Value.X)).ToList();
                    Assert.Equal(2, racers.Count);
                    Assert.Equal(racers[0], racers[1]);
                    Shot(window, "replay-mode-file-offer");
                    Click(window, offers[0]);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(mode.ShowingMode);
                    Assert.True(mode.Acting);
                    Assert.Equal($"Ghost race · code {Online.Protocol.ModePackage.ShareCode(start.Hash)}", mode.ModeHeading);
                    Assert.Equal("Laps 1 · Ghosts on the map Show · Gap Live", mode.ModeSettings);
                    var source = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Name == "ModeSource" && t.IsEffectivelyVisible);
                    Assert.Equal(text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), source.Text);
                    Assert.Contains("preset \"Sprint\" { lap_count = 1; }", source.Text, StringComparison.Ordinal);
                    Shot(window, "replay-mode-file");

                    // the results open in its place, and Close puts the file away
                    page.ToggleStatsCommand.Execute(mode);
                    Assert.Equal((true, false), (mode.ShowingStats, mode.ShowingMode));
                    page.ShowModeFileCommand.Execute(mode);
                    Assert.Equal((false, true), (mode.ShowingStats, mode.ShowingMode));
                    page.HideModeFileCommand.Execute(mode);
                    Assert.False(mode.ShowingMode);
                }
                finally
                {
                    window.Close();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is { Length: > 0 } folder)
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Directory.CreateDirectory(folder);
            window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    [Fact]
    public void APracticeRunIsTheHeadersWord()
    {
        Write("p.fzrp", "2026-10-18T10:00:00+00:00", "true");
        Write("r.fzrp", "2026-10-18T09:00:00+00:00", null);
        Write("f.fzrp", "2026-10-18T08:00:00+00:00", "false");
        Write("s.fzrp", "2026-10-18T07:00:00+00:00", "\"yes\"");   // not the word true: a race
        var all = ReplayFile.List(Replays).ToList();
        Assert.Equal([true, false, false, false], all.Select(r => r.Practice));
        Assert.Equal((4, 3, 1), (ReplayKind.All.Count(all), ReplayKind.Races.Count(all), ReplayKind.Practice.Count(all)));
    }

    [Fact]
    public async Task ReplaysAreListedUnderTheirDaysAndMonths()
    {
        var now = DateTimeOffset.Now;
        Write("a.fzrp", now.AddSeconds(-2).ToString("o"), null);
        Write("b.fzrp", now.AddSeconds(-3).ToString("o"), "true");
        Write("c.fzrp", "2020-01-15T12:00:00+00:00", null);
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var page = new ReplaysViewModel(state, () => now);
                var window = new Window { Content = new ReplaysView { DataContext = page }, Width = 1100, Height = 700 };
                window.Show();
                Loaded(page);
                try
                {
                    Assert.Equal(["Today 2", "a.fzrp", "b.fzrp", "January 2020 1", "c.fzrp"],
                        page.Rows.Select(r => r switch
                        {
                            ReplayGroup g => $"{g.Title} {g.Count}",
                            ReplayItem i => i.FileName,
                            _ => "?",
                        }));
                    var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                    Assert.Contains(texts, t => string.Equals(t, "Today", StringComparison.OrdinalIgnoreCase));   // the heading drawn

                    Click(window, window.GetVisualDescendants().OfType<Button>()
                        .Single(b => AutomationProperties.GetName(b) == "Races" && b.Classes.Contains("section")));
                    Assert.Equal(["Today 1", "January 2020 1"], page.Rows.OfType<ReplayGroup>().Select(g => $"{g.Title} {g.Count}"));
                }
                finally
                {
                    window.Close();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    [Fact]
    public async Task TheTabsShowTheRacesOrThePracticeRuns()
    {
        Write("a.fzrp", "2026-10-18T10:00:00+00:00", "true");
        Write("b.fzrp", "2026-10-18T09:00:00+00:00", null);
        Write("c.fzrp", "2026-10-18T08:00:00+00:00", null);
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var page = new ReplaysViewModel(state);
                var window = new Window { Content = new ReplaysView { DataContext = page }, Width = 1100, Height = 700 };
                window.Show();
                Loaded(page);
                try
                {
                    Button Tab(string label) => window.GetVisualDescendants().OfType<Button>()
                        .Single(b => AutomationProperties.GetName(b) == label && b.Classes.Contains("section"));
                    string Count(string label) => Tab(label).GetVisualDescendants().OfType<TextBlock>().Last().Text ?? "";
                    Assert.Equal(("3", "2", "1"), (Count("All"), Count("Races"), Count("Practice")));
                    Assert.Contains("on", Tab("All").Classes);
                    Assert.Equal(3, page.Items.Count);

                    Click(window, Tab("Practice"));
                    Assert.Equal(ReplayKind.Practice, page.Kind);
                    var row = Assert.Single(page.Items);
                    Assert.True(row.IsPractice);
                    Assert.False(row.HasStats);   // it has results, and no Results to open
                    var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                    Assert.Contains("Practice", texts.Skip(3));   // the Result column (after the three tabs' words)
                    Assert.Contains(row.Time, texts);
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                        b => AutomationProperties.GetName(b) == "Results" && b.IsEffectivelyVisible);
                    Assert.Contains("on", Tab("Practice").Classes);
                    Assert.DoesNotContain("on", Tab("All").Classes);

                    Click(window, Tab("Races"));
                    Assert.Equal(2, page.Items.Count);
                    Assert.All(page.Items, r => Assert.True(r.IsRace && r.HasStats));
                    Assert.Equal(2, window.GetVisualDescendants().OfType<Button>()
                        .Count(b => AutomationProperties.GetName(b) == "Results" && b.IsEffectivelyVisible));

                    // a row deleted: the counts follow, the tab stays
                    File.Delete(Path.Combine(Replays, "b.fzrp"));
                    page.Refresh();
                    Loaded(page);
                    Assert.Equal(("2", "1", "1"), (Count("All"), Count("Races"), Count("Practice")));
                    Assert.Single(page.Items);
                }
                finally
                {
                    window.Close();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    [Fact]
    public async Task OneRowOffersItsFileActionsAtATimeAndADeleteLeavesTheOthers()
    {
        Write("a.fzrp", "2026-10-18T10:00:00+00:00", null);
        Write("b.fzrp", "2026-10-18T09:00:00+00:00", null);
        Write("c.fzrp", "2026-10-18T08:00:00+00:00", null);
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var page = new ReplaysViewModel(state);
                Loaded(page);
                var (a, b, c) = (page.Items[0], page.Items[1], page.Items[2]);

                page.ToggleMoreCommand.Execute(a);
                Assert.True(a.More);
                page.ToggleMoreCommand.Execute(b);   // another row's "•••": the first goes back to its usual actions
                Assert.Equal((true, true), (a.Acting, b.More));
                page.ToggleMoreCommand.Execute(b);
                Assert.True(b.Acting);

                // c's results open; a deleted: c keeps its results open, the counts follow
                page.ToggleStatsCommand.Execute(c);
                page.AskDeleteCommand.Execute(a);
                page.DeleteCommand.Execute(a);
                Assert.Equal([b, c], page.Items);
                Assert.True(c.ShowingStats);
                Assert.False(File.Exists(Path.Combine(Replays, "a.fzrp")));
                Assert.Equal(2, page.Tabs[0].Count);
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }
}
