using Launcher.App;
using Launcher.Core.Board;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>A racer the lobby's finish limit disqualified has their game closed (<see cref="AppState.Disqualified"/>):
/// only once the game says the race is over, only with a finish limit, and only while their own racer still races.</summary>
public sealed class DisqualifiedTests
{
    private static RacerLive Racer(int seat, string state) =>
        new(seat, 1, 2, 0, 90_000, null, 0, 100, 100, 0, 0, state, 0, 0, 0);

    private static RaceSnapshot Race(string mine, string other = RacerLive.Finished) =>
        new(600, 1, [Racer(0, other), Racer(1, mine)]);

    private static readonly RaceRules Limit = new(FinishLimitSeconds: 90);

    [Theory]
    [InlineData(BoardPhase.Finished, RacerLive.Racing, 90, true)]     // the limit ran out while the player raced
    [InlineData(BoardPhase.Racing, RacerLive.Racing, 90, false)]      // the race goes on
    [InlineData(BoardPhase.Finished, RacerLive.Finished, 90, false)]  // over the line: the results are theirs
    [InlineData(BoardPhase.Finished, RacerLive.Out, 90, false)]       // knocked out
    [InlineData(BoardPhase.Finished, RacerLive.Racing, 0, false)]     // no finish limit
    public void OnlyARacerStillRacingWhenTheLimitEndedTheRaceIsDisqualified(BoardPhase phase, string mine, int limit, bool disqualified) =>
        Assert.Equal(disqualified, AppState.Disqualified(phase, Race(mine), new RaceRules(FinishLimitSeconds: limit)));

    [Fact]
    public void ARaceWithoutRulesDisqualifiesNoOne() =>
        Assert.False(AppState.Disqualified(BoardPhase.Finished, Race(RacerLive.Racing), null));

    [Fact]
    public void AnotherRacerStillRacingIsNotThePlayers() =>
        Assert.False(AppState.Disqualified(BoardPhase.Finished, Race(RacerLive.Finished, other: RacerLive.Racing), Limit));
}
