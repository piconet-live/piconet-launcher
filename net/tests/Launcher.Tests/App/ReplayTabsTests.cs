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
/// results to open. A practice run is a replay whose header says <c>"practice": true</c>.
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
    private void Write(string name, string recorded, string? practice)
    {
        var header = $$$"""
            {"match": {"matchId": "x", "seat": 0, "setup": {"course": 5, "laps": 3, "seats": [
              {"seat": 0, "playerId": "p1", "name": "ANA", "machine": 1}]}},
             "seat": 0, "mask": 1, "delay": 2, "recorded": "{{{recorded}}}"{{{(practice is null ? "" : ", \"practice\": " + practice)}}}}
            """;
        Directory.CreateDirectory(Replays);
        var json = Encoding.UTF8.GetBytes(header);
        using var f = File.Create(Path.Combine(Replays, name));
        var w = new BinaryWriter(f);
        Span<byte> u32 = stackalloc byte[4];
        w.Write("FZRP"u8);
        w.Write((byte)0);
        w.Write((byte)2);
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
                Dispatcher.UIThread.RunJobs();
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
                    Dispatcher.UIThread.RunJobs();
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
}
