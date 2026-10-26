using System.Reflection;
using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

// The coordinator's messages (FuzzTests): one of every message, mutated, through ServerMessages.Checked, and what the
// launcher then reads of each.
public sealed partial class FuzzTests
{
    /// <summary>A pack for the messages that carry one (declared before them: the fields are made in order).</summary>
    private static readonly StatPack FuzzPack = StatPack.Of("GX Fuzz", [StatEntry.Of(6, 4, 2520), new StatEntry(41 + 27, 0x48, 0x05000000)]).Pack!;

    /// <summary>One of every message a coordinator sends, whole.</summary>
    private static readonly ServerMessage[] Messages =
    [
        new Welcome("p1", "Den", ControlProtocol.Version, new ServerRules(3, 30, CpuFillMode.Vote, CourseSelectMode.Vote, 21,
            [0, 1, 2, 29], new SettingRange(80, 100), new RaceRules())),
        new ErrorMessage("bad_name", "no"),
        new LobbyState("l1", [new PlayerInfo("p1", "ANA", 3, new Rgb(1, 2, 3), 2, true, true), new PlayerInfo("p2", "BEN", 0, new Rgb(4, 5, 6))],
            30, 2, true, 1, 0, LobbyState.Racing,
            new LobbyRace("m7", 21, 3, [new SeatSetup(0, "p1", "ANA", 3, new Rgb(1, 2, 3)), new SeatSetup(1, null, "CPU", 7, null)], DateTimeOffset.UnixEpoch),
            LobbyState.WaitingForRelay, [new OtherBuild("a/b/c", 2), new OtherBuild(null, 1)]),
        new LeftLobby("l1"),
        new ChatMessage(ControlProtocol.LobbyChannel, "p2", "BEN", "hello", DateTimeOffset.UnixEpoch),
        new MeasureRelays("m7", [new RelayEndpoint("r1", "", 47811), new RelayEndpoint("r2", "relay.example", 47811)], 1500),
        new MatchStart("m7", new RelayEndpoint("r1", "", 47811), "1f2e3d4c5b6a7988", 1,
            new RaceSetup(21, 3, [new SeatSetup(0, "p1", "ANA", 3, new Rgb(1, 2, 3), 70, 2), new SeatSetup(1, "p2", "BEN", 0, null)], 4660, 305419896,
                new RaceRules(2, true, false)), true),
        new Requeued("m7", "latency"),
        new QuickPlayEnded("m7", "at_capacity"),
        new CourseVote("m7", [1, 21, 16, 8, 36, CourseVote.RandomCourse], 10000),
        new CourseChosen("m7", [1, 21, 16, 8, 36, CourseVote.RandomCourse], [0, 2, 0, 1, 0, 0], 1, 21),
        new ChooseMachine("m7", 21, [0, 1, 2, 3], 15000),
        new LiveRace("m7", new RaceSnapshot(600, 1, [new RacerLive(0, 1, 1, 2000, 62000, 60000, 1200, 80, 100, 1, 1500, RacerLive.Racing, 1, 2, 3, 4000),
            new RacerLive(1, 0, 2, 100, 125000, 61000, 0, 0, 100, 0, 1400, RacerLive.Finished, 5, 6, 7, 9000)])),
        new RaceDesynced("m7"),
        // since 0.2.0: the rules live, notices, the server's pack, tags, spectators, the vote's counts, pings,
        // a lobby with packs a seat, a race with a pack, packs a seat and a custom machine
        new RulesChanged(new ServerRules(3, 30, CpuFillMode.Always, CourseSelectMode.Fixed, 21, [0, 1, 40], new SettingRange(0, 100),
            new RaceRules(1, true, true, 30, 200))),
        new RecentNotices([new RecentNotice("n1", Notices.Warning, "servers restart", DateTimeOffset.UnixEpoch)]),
        new ChatMessage(ControlProtocol.GlobalChannel, Notices.ServerId, "Server", "[URGENT] now", DateTimeOffset.UnixEpoch, Notices.Urgent, "n2"),
        new ChatMessage(ControlProtocol.LobbyChannel, "p2", "BEN", "hi", DateTimeOffset.UnixEpoch, FromTag: "AB23"),
        new ErrorMessage("muted", "you are muted", 60),
        new QuickPlayEnded("m7", "retired", "This server is retired: download the new build."),
        new StatPackValues(FuzzPack.ToWire()),
        new TagChanged("AB23"),
        new CourseVotes("m7", [0, 2, 0, 1, 0, 0], ["p1", "p2"]),
        new LobbyPings("l1", [new PlayerPing("p1", 40), new PlayerPing("p2", 120)]),
        new LobbyState("l1", [new PlayerInfo("p1", "ANA", 3, new Rgb(1, 2, 3), Tag: "AB23", Pack: "GX Fuzz"), new PlayerInfo("p2", "BEN", 0, new Rgb(4, 5, 6), Watching: true)],
            30, 2, true, 1, 0, LobbyState.Choosing, StatPack: new StatPackName(FuzzPack.Name, FuzzPack.Fingerprint, "someone"),
            SeatPacks: [new StatPackName(FuzzPack.Name, FuzzPack.Fingerprint)]),
        new MatchStart("m8", new RelayEndpoint("r1", "relay.example", 47811), "1f2e3d4c5b6a7988", 0,
            new RaceSetup(21, 3, [new SeatSetup(0, "p1", "ANA", 3, new Rgb(1, 2, 3), 70, null, new CustomMachine(3, 30, 60, 6, [0xFF000001, 0, 0x00FF0001]), 1),
                new SeatSetup(1, null, "CPU", 7, null)], 4660, 305419896, new RaceRules(3, false, true, 30, 200), FuzzPack.ToWire(), [FuzzPack.ToWire()])),
        new WatchStart("m8", new RelayEndpoint("r1", "", 47811), "1f2e3d4c5b6a7988", 1,
            new RaceSetup(21, 3, [new SeatSetup(0, "p1", "ANA", 3, null), new SeatSetup(1, "p2", "BEN", 0, null)], 1, 2)),
    ];


    /// <summary>Every property the code may read without a null check (its type says it is never null) holds
    /// something, all the way down.</summary>
    private static void NothingMissing(object? value, string path, NullabilityInfoContext context)
    {
        if (value is null || value is string || value.GetType().IsPrimitive || value is DateTimeOffset || value is Enum)
        {
            return;
        }
        if (value is System.Collections.IEnumerable list)
        {
            var i = 0;
            foreach (var item in list)
            {
                Assert.True(item is not null, $"{path}[{i}] is null");
                NothingMissing(item, $"{path}[{i++}]", context);
            }
            return;
        }
        foreach (var p in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0 || p.Name == "EqualityContract")
            {
                continue;
            }
            var v = p.GetValue(value);
            if (!p.PropertyType.IsValueType && context.Create(p).ReadState == NullabilityState.NotNull)
            {
                Assert.True(v is not null, $"{path}.{p.Name} is null");
            }
            NothingMissing(v, $"{path}.{p.Name}", context);
        }
    }

    [Fact]
    public void NoCoordinatorMessageReachesTheLauncherWithAPartMissing()
    {
        var seeds = Messages.Select(ControlProtocol.Encode).ToArray();
        var context = new NullabilityInfoContext();
        Loop("message", 6000, random =>
        {
            var json = MutateJson(seeds[random.Next(seeds.Length)], random);
            var data = Encoding.UTF8.GetBytes(json);
            if (random.Next(10) == 0)
            {
                Bytes(data, random);
            }
            return data;
        }, data =>
        {
            if (ServerMessages.Checked(Encoding.UTF8.GetString(data)) is not { } message)
            {
                return;
            }
            NothingMissing(message, message.GetType().Name, context);
            switch (message)
            {
                case LiveRace live:
                    Assert.All(live.Snapshot.Racers, r => Assert.InRange(r.Seat, 0, 29));
                    _ = RaceStandings.Places(live.Snapshot.Racers);
                    break;
                case LobbyState lobby:
                    _ = QuickPlaySession.LobbyText(lobby, "p1", "a/b/c");
                    _ = LobbyScreens.Of(lobby);
                    break;
                case Welcome { Rules: { } rules }:
                    Assert.All(rules.AllowedMachines, m => Assert.InRange(m, 0, Machines.Names.Count - 1));
                    _ = Machines.Names[QuickPlaySession.QueueMachine(0, rules)];
                    break;
                case MatchStart start:
                    Assert.Equal(Path.GetFileName(RecompLaunch.ReplayFileName(DateTimeOffset.UnixEpoch, start.MatchId)),
                        RecompLaunch.ReplayFileName(DateTimeOffset.UnixEpoch, start.MatchId));
                    Assert.InRange(start.Seat, 0, 29);
                    Assert.All(start.Setup.Seats, s => Assert.InRange(s.Seat, 0, 29));
                    _ = MatchFile.Build(start, new RaceOptions("127.0.0.1", OtherSounds.Quieter));
                    break;
                case MeasureRelays measure:
                    Assert.InRange(measure.Relays.Count, 0, ServerMessages.MaxRelays);
                    Assert.All(measure.Relays, r => Assert.InRange(r.Port, 1, 65535));
                    break;
                case CourseChosen chosen:
                    var parts = CourseDraw.Parts(chosen.Votes);
                    Assert.InRange(CourseDraw.Position(CourseDraw.Landing(parts, chosen.Winner, chosen.Course), 0.5), 0, 1);
                    break;
                case StatPackValues values:
                    _ = StatPack.FromWire(values.Pack);
                    break;
                case ChatMessage chat when ServerNotice.Of(chat) is { } notice:
                    _ = ServerNotice.WordOf(notice.Level) + notice.Text;
                    break;
                case RecentNotices recent:
                    Assert.InRange(recent.Notices.Count, 0, NoticeLog.Most);
                    foreach (var n in recent.Notices)
                    {
                        _ = ServerNotice.Of(n);
                    }
                    break;
                case WatchStart watch:
                    Assert.InRange(watch.Seat, 0, 29);
                    break;
                case RulesChanged changed:
                    Assert.All(changed.Rules.AllowedMachines, m => Assert.InRange(m, 0, Machines.Names.Count - 1));
                    break;
            }
            // what Discord's status is made of (PresenceWords): a lobby, a race and its live race
            _ = PresenceWords.Of(message switch
            {
                LobbyState lobby => new PresenceInput(Lobby: lobby),
                MatchStart start => new PresenceInput(GameRun.Race, Race: start.Setup),
                LiveRace live => new PresenceInput(GameRun.Race, Live: live.Snapshot),
                _ => new PresenceInput(),
            });
            if (message is MatchStart { Setup.Stats: { } stats })
            {
                _ = StatPack.FromWire(stats);
            }
        });
    }
}
