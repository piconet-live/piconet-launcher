using System.Collections.Concurrent;
using Launcher.Core;
using Online.Protocol;
using TestSupport;

namespace Launcher.Tests;

/// <summary>The stand-in service races Quick Play's rotation as the service does (<see cref="QuickPlayRotation"/>):
/// on the protocol's rotation of today's words, its vote has five cards, one spicy with its badge, and Random; the
/// draw's card names what the course drawn races. The official rotation loads, with no notice. A rotation that does not
/// load: the stand-in votes on courses alone and says why. A custom lobby's host mode file is compiled as the service compiles it: a file that
/// does not compile is refused with its line (bad_mode), one that does names the lobby's mode.</summary>
[Collection(StandInModesTests.Name)]
public sealed class StandInModesTests
{
    /// <summary>The stand-in's rotation is the process's: these tests take turns.</summary>
    public const string Name = "stand-in rotation";

    private static string Vectors => RepositoryFolder.Protocol("protocol", "tests", "vectors", "quickplay");

    private static async Task<(ICoordinatorConnection Link, ConcurrentQueue<ServerMessage> Got)> Connected()
    {
        var link = await StandInService.Connect(new Uri(StandInService.Address), "ANA", null, false, null, TestContext.Current.CancellationToken);
        var got = new ConcurrentQueue<ServerMessage>();
        link.Received += got.Enqueue;
        return (link, got);
    }

    private static async Task<T> Next<T>(ConcurrentQueue<ServerMessage> got, Func<T, bool>? which = null) where T : ServerMessage
    {
        for (var waited = 0; waited < 20_000; waited += 50)
        {
            if (got.OfType<T>().FirstOrDefault(m => which?.Invoke(m) ?? true) is { } message)
            {
                return message;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"no {typeof(T).Name} came");
    }

    [Fact]
    public async Task TheStandInsVoteIsTheRotationsCards()
    {
        StandInService.UseQuickPlay(Vectors);
        Assert.Null(StandInService.RotationRefusal);
        var (link, got) = await Connected();
        await using (link)
        {
            await link.EnterQuickPlayAsync(6, new Rgb(1, 2, 3), cancel: TestContext.Current.CancellationToken);
            var lobby = await Next<LobbyState>(got);
            Assert.False(lobby.NoCpuVote);   // (the rotation's modes allow CPUs)
            await link.VoteStartAsync(true, TestContext.Current.CancellationToken);
            var vote = await Next<CourseVote>(got);
            Assert.Equal(6, vote.Options.Count);
            Assert.Equal(6, vote.Cards!.Count);
            var spicy = Assert.Single(vote.Cards, c => c.Badge is not null);
            Assert.Equal(("OVERDRIVE", 0, 8), (spicy.Badge, spicy.Laps, vote.Options[vote.Cards.ToList().IndexOf(spicy)]));
            Assert.All(vote.Cards.Take(5).Where(c => c.Badge is null), c => Assert.Equal(3, c.Laps));
            Assert.Null(vote.Cards[^1].Laps);
            var chosen = await Next<CourseChosen>(got);
            Assert.NotNull(chosen.Card);
            Assert.Equal(chosen.Card!.Badge is null ? 3 : 0, chosen.Card.Laps);
        }
    }

    [Fact]
    public async Task TheOfficialRotationIsTheStandInsVote()
    {
        StandInService.UseQuickPlay(RepositoryFolder.Protocol("protocol", "quickplay"));
        try
        {
            Assert.Null(StandInService.RotationRefusal);
            var (link, got) = await Connected();
            await using (link)
            {
                await link.EnterQuickPlayAsync(6, new Rgb(1, 2, 3), cancel: TestContext.Current.CancellationToken);
                await link.VoteStartAsync(true, TestContext.Current.CancellationToken);
                var vote = await Next<CourseVote>(got);
                var cards = vote.Cards!;
                var spicy = Assert.Single(cards, c => c.Badge is not null);
                Assert.Equal(("KNOCKOUT", 0, 8), (spicy.Badge, spicy.Laps, vote.Options[cards.ToList().IndexOf(spicy)]));
                var normal = StandInService.Rotation!.Normal;
                for (var i = 0; i < 5; i++)
                {
                    if (cards[i].Badge is null)
                    {
                        Assert.Equal(normal.Laps(vote.Options[i]), cards[i].Laps);
                    }
                }
                Assert.DoesNotContain(got.OfType<ChatMessage>(), c => c.NoticeId == "stand-in-modes");
            }
        }
        finally
        {
            StandInService.UseQuickPlay(Vectors);
        }
    }

    [Fact]
    public async Task ARotationThatDoesNotLoadIsSaidAndTheVoteIsOfCourses()
    {
        var folder = Directory.CreateTempSubdirectory("rotation-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, QuickPlayRotation.FileName), "spicy_cards = 0\n[normal]\nmode = \"broken.fzmode\"");
            File.WriteAllText(Path.Combine(folder, "broken.fzmode"), "mode \"Broken\" {\n  api 1;\n}\n\nlaps 100;\n");
            StandInService.UseQuickPlay(folder);
            Assert.StartsWith("broken.fzmode line 5: ", StandInService.RotationRefusal, StringComparison.Ordinal);
            var (link, got) = await Connected();
            await using (link)
            {
                var said = await Next<ChatMessage>(got, c => c.NoticeId == "stand-in-modes");
                Assert.Contains("broken.fzmode line 5", said.Text, StringComparison.Ordinal);
                await link.EnterQuickPlayAsync(6, new Rgb(1, 2, 3), cancel: TestContext.Current.CancellationToken);
                await link.VoteStartAsync(true, TestContext.Current.CancellationToken);
                Assert.Null((await Next<CourseVote>(got)).Cards);
            }
        }
        finally
        {
            StandInService.UseQuickPlay(Vectors);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AHostsModeFileIsCompiledAsTheServiceDoes()
    {
        var good = File.ReadAllText(Path.Combine(Vectors, "overdrive.fzmode"));
        var rules = new LobbyRules(new Dictionary<string, int>(), ModeFile: good);
        var (mode, _, refusal) = StandInService.ModeOf(rules, null, null);
        Assert.Null(refusal);
        Assert.Equal(("Overdrive", "OVERDRIVE", ModeCompiler.Compile(good).Hash), (mode!.Name, mode.Badge, mode.Hash));
        // named again without the file: kept; another hash: refused; none: no mode
        Assert.Same(mode, StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(), Mode: mode), mode, null).Mode);
        Assert.Equal("the lobby holds no such mode", StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(), Mode: mode), null, null).Refusal);
        Assert.Null(StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>()), mode, null).Mode);
        // the host's values: checked against the package, kept, and over the words
        const string WithParam = "mode \"P\" { api 1; }\nparam n \"Laps\" number 2..10 step 2 default 4;\nlaps n;\n";
        Assert.StartsWith("Laps:", StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(), ModeFile: WithParam, ModeValues: [3]), null, null).Refusal, StringComparison.Ordinal);
        var (first, firstTerms, _) = StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(), ModeFile: WithParam), null, null);
        Assert.Equal([4], first!.Values!);
        Assert.Equal(4, first.Terms!["laps"]);
        var (moved, movedTerms, _) = StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(), Mode: first, ModeValues: [8]), first, firstTerms);
        Assert.Equal([8], moved!.Values!);
        Assert.Equal((8, 8), (moved.Terms!["laps"], movedTerms!.Laps(1)));
        Assert.StartsWith("Laps:", StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(), Mode: moved, ModeValues: [20]), moved, movedTerms).Refusal, StringComparison.Ordinal);
        Assert.StartsWith("line 5: ", StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(),
            ModeFile: "mode \"Broken\" {\n  api 1;\n}\n\nlaps 100;\n"), null, null).Refusal, StringComparison.Ordinal);
    }

    // the overhaul: the stand-in takes a mode's words and a lobby's teams as the service does (the service's GameModeTests)

    private const string Head = "mode \"M\" { api 1; }\n";

    private static readonly Rgb Paint = new(1, 2, 3);

    private static async Task<LobbyState> Created((ICoordinatorConnection Link, ConcurrentQueue<ServerMessage> Got) c, string body, Dictionary<string, int>? values = null)
    {
        await c.Link.CreateLobbyAsync(new CreateLobby("F", new LobbyRules(values ?? new Dictionary<string, int>(), ModeFile: Head + body), null, 6, Paint), TestContext.Current.CancellationToken);
        return await Next<LobbyState>(c.Got, s => s.Custom);
    }

    [Fact]
    public async Task RestoreIsRefusedAsTheServiceRefusesIt()
    {
        var c = await Connected();
        await using (c.Link)
        {
            await c.Link.CreateLobbyAsync(new CreateLobby("F", new LobbyRules(new Dictionary<string, int> { ["restore"] = 1 }), null, 6, Paint), TestContext.Current.CancellationToken);
            var refused = await Next<ErrorMessage>(c.Got);
            Assert.Equal((CustomLobby.BadRules, "no rule restore"), (refused.Code, refused.Message));
        }
    }

    [Fact]
    public async Task AModesWordsSetTheLobbyAsOnTheService()
    {
        var c = await Connected();
        await using (c.Link)
        {
            var state = await Created(c, "racers 2..6;\nlaps 2;");
            Assert.Equal(6, state.Capacity);
            Assert.True(state.NoCpuVote);                        // (no `cpus on`)
            Assert.Equal(0, state.Rules!.Mode!.Terms!["finish-limit"]);
            Assert.Equal(2, state.Rules.Mode.Terms["laps"]);
            Assert.Equal((int)CpuFillMode.Never, state.Rules.Mode.Terms["cpu-fill"]);
        }
        var d = await Connected();
        await using (d.Link)
        {
            Assert.False((await Created(d, "cpus on;")).NoCpuVote);
        }
    }

    [Fact]
    public async Task AWordTheStandInCannotHonourRefusesTheMode()
    {
        var c = await Connected();
        await using (c.Link)
        {
            await c.Link.CreateLobbyAsync(new CreateLobby("F", new LobbyRules(new Dictionary<string, int>(), ModeFile: Head + "stats \"Nope\";"), null, 6, Paint), TestContext.Current.CancellationToken);
            Assert.Equal(CustomLobby.BadMode, (await Next<ErrorMessage>(c.Got)).Code);
        }
    }

    [Fact]
    public async Task APinnedCourseHasNoVoteAndTheModesMachinesAreOffered()
    {
        var c = await Connected();
        await using (c.Link)
        {
            await Created(c, "course \"Lightning: Loop Cross\";\nmachines [\"Blue Falcon\"];");
            await c.Link.StartLobbyRaceAsync(TestContext.Current.CancellationToken);
            var choose = await Next<ChooseMachine>(c.Got);
            Assert.Equal((8, 6), (choose.Course, Assert.Single(choose.Machines)));
            Assert.Empty(c.Got.OfType<CourseVote>());
        }
    }

    [Theory]
    [InlineData("customs off;", true)]
    [InlineData("machines [\"Blue Falcon\"];\ncustoms off;", true)]
    [InlineData("machines [\"Blue Falcon\"];", false)]
    [InlineData("", false)]
    public async Task TheChoiceSaysWhenTheModeTakesNoCustoms(string words, bool off)
    {
        var c = await Connected();
        await using (c.Link)
        {
            await Created(c, words);
            await c.Link.StartLobbyRaceAsync(TestContext.Current.CancellationToken);
            Assert.Equal(off, (await Next<ChooseMachine>(c.Got)).CustomsOff);
        }
    }

    [Fact]
    public async Task BelowItsRacersAModeRefusesTheRace()
    {
        var c = await Connected();
        await using (c.Link)
        {
            await Created(c, "racers 20..30;");
            await c.Link.StartLobbyRaceAsync(TestContext.Current.CancellationToken);
            var refused = await Next<ErrorMessage>(c.Got);
            Assert.Equal(CustomLobby.ModeRefuses, refused.Code);
            Assert.Contains("at least 20 racers", refused.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TeamsAreChosenAndCheckedAsOnTheService()
    {
        var c = await Connected();
        await using (c.Link)
        {
            var state = await Created(c, "teams 2..3;", new Dictionary<string, int> { [LobbyRules.TeamsKey] = 9 });
            Assert.Equal(3, state.Rules!.Value(LobbyRules.TeamsKey));   // (within the mode's word)
            await c.Link.ChooseTeamAsync(1, null, TestContext.Current.CancellationToken);
            var mine = await Next<LobbyState>(c.Got, s => s.Players.Any(p => p.Team == 1));
            Assert.Single(mine.Players, p => p.Team == 1);
            await c.Link.ChooseTeamAsync(3, null, TestContext.Current.CancellationToken);
            Assert.Equal(CustomLobby.BadRules, (await Next<ErrorMessage>(c.Got)).Code);
        }
    }
}
