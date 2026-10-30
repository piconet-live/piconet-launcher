using System.Collections.Concurrent;
using Launcher.Core;
using Online.Protocol;
using TestSupport;

namespace Launcher.Tests;

/// <summary>The stand-in service races Quick Play's rotation as the service does (<see cref="QuickPlayRotation"/>):
/// on the protocol's rotation of today's words, its vote has five cards, one spicy with its badge, and Random; the
/// draw's card names what the course drawn races. The official rotation does not load yet: the stand-in then votes on
/// courses alone and says why. A custom lobby's host mode file is compiled as the service compiles it: a file that
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
    public async Task TheOfficialRotationWaitsAndTheStandInSaysWhy()
    {
        StandInService.UseQuickPlay(RepositoryFolder.Protocol("protocol", "quickplay"));
        try
        {
            Assert.StartsWith("knockout.fzmode line 15: ", StandInService.RotationRefusal, StringComparison.Ordinal);
            var (link, got) = await Connected();
            await using (link)
            {
                var said = await Next<ChatMessage>(got, c => c.NoticeId == "stand-in-modes");
                Assert.Contains("knockout.fzmode line 15", said.Text, StringComparison.Ordinal);
                await link.EnterQuickPlayAsync(6, new Rgb(1, 2, 3), cancel: TestContext.Current.CancellationToken);
                await link.VoteStartAsync(true, TestContext.Current.CancellationToken);
                Assert.Null((await Next<CourseVote>(got)).Cards);
            }
        }
        finally
        {
            StandInService.UseQuickPlay(Vectors);
        }
    }

    [Fact]
    public void AHostsModeFileIsCompiledAsTheServiceDoes()
    {
        var good = File.ReadAllText(Path.Combine(Vectors, "overdrive.fzmode"));
        var rules = new LobbyRules(new Dictionary<string, int>(), ModeFile: good);
        var (mode, refusal) = StandInService.ModeOf(rules, null);
        Assert.Null(refusal);
        Assert.Equal(("Overdrive", "OVERDRIVE", ModeCompiler.Compile(good).Hash), (mode!.Name, mode.Badge, mode.Hash));
        // named again without the file: kept; another hash: refused; none: no mode
        Assert.Equal((mode, (string?)null), StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(), Mode: mode), mode));
        Assert.Equal("the lobby holds no such mode", StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(), Mode: mode), null).Refusal);
        Assert.Equal((null, (string?)null), StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>()), mode));
        Assert.StartsWith("line 5: ", StandInService.ModeOf(new LobbyRules(new Dictionary<string, int>(),
            ModeFile: "mode \"Broken\" {\n  api 1;\n}\n\nlaps 100;\n"), null).Refusal, StringComparison.Ordinal);
    }
}
