using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The lobby page's states, from the lobby's stage.</summary>
public sealed class LobbyTests
{
    [Theory]
    [InlineData(LobbyState.Forming, LobbyScreen.Gathering)]
    [InlineData(LobbyState.Starting, LobbyScreen.Starting)]
    [InlineData(LobbyState.Choosing, LobbyScreen.Choosing)]
    [InlineData(LobbyState.Racing, LobbyScreen.Race)]
    public void TheLobbysStageGivesItsScreen(string stage, LobbyScreen screen) =>
        Assert.Equal(screen, LobbyScreens.Of(new LobbyState("l1", [], 30, 2, false, 0, 0, stage)));

    [Fact]
    public void QueueingShowsTheGathering() => Assert.Equal(LobbyScreen.Gathering, LobbyScreens.Of(null));

    [Fact]
    public void ALobbyWaitingForItsRaceServerSaysSo()
    {
        var choosing = new LobbyState("l1", [], 30, 2, false, 0, 0, LobbyState.Choosing);
        Assert.Equal("Machines", QuickPlaySession.LobbyText(choosing, "p1"));
        var waiting = choosing with { Waiting = LobbyState.WaitingForRelay };   // machines chosen, the relay still starting
        Assert.Equal("Starting race server…", QuickPlaySession.LobbyText(waiting, "p1"));
        Assert.Equal(LobbyScreen.Choosing, LobbyScreens.Of(waiting));           // still on the machine choice's page
    }

    [Theory]
    [InlineData(LobbyScreen.Gathering, LobbyScreen.Starting, false, true)]    // the vote: from any page
    [InlineData(LobbyScreen.Gathering, LobbyScreen.Starting, true, true)]
    [InlineData(LobbyScreen.Starting, LobbyScreen.Choosing, false, true)]    // the machine choice: from any page
    [InlineData(LobbyScreen.Starting, LobbyScreen.Choosing, true, true)]
    [InlineData(LobbyScreen.Choosing, LobbyScreen.Race, true, true)]          // the race: from the home page
    [InlineData(LobbyScreen.Choosing, LobbyScreen.Race, false, false)]        // not from settings or replays
    [InlineData(LobbyScreen.Starting, LobbyScreen.Race, true, true)]
    [InlineData(LobbyScreen.Starting, LobbyScreen.Race, false, false)]
    [InlineData(LobbyScreen.Race, LobbyScreen.Gathering, true, false)]        // the lobby again: the player stays put
    [InlineData(LobbyScreen.Starting, LobbyScreen.Starting, false, false)]    // no change, no move
    [InlineData(LobbyScreen.Choosing, LobbyScreen.Choosing, false, false)]
    [InlineData(LobbyScreen.Race, LobbyScreen.Race, true, false)]
    public void TheVoteAndTheRaceBringThePlayerToTheLobby(LobbyScreen before, LobbyScreen now, bool onHome, bool brings) =>
        Assert.Equal(brings, LobbyScreens.BringsToLobby(before, now, onHome));

    [Theory]
    [InlineData(3, null, new[] { 1, 3, 5 }, 3)]     // the racer's machine when allowed
    [InlineData(2, 5, new[] { 1, 3, 5 }, 5)]        // else the one the session races (what the server keeps)
    [InlineData(2, 4, new[] { 1, 3, 5 }, 1)]        // else the first allowed
    public void TheMachineChoiceStartsOnTheRacersMachine(int racer, int? session, int[] allowed, int start) =>
        Assert.Equal(start, QuickPlaySession.DefaultChoice(racer, session, allowed));
}
