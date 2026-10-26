using Avalonia.Controls;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

// A lobby's stage with a decision always has one obvious way to it (a player in the Garage
// must still reach the course vote when it is called).
public sealed partial class LobbyStageRulesTests
{
    private static readonly int[] Options = [1, 3, 5, CourseVote.RandomCourse];

    /// <summary>The course vote opens while the player is on the Garage place, in a machine's PREVIEW: the launcher
    /// brings them to the lobby's page, its vote showing; the next one opens while they are in the lobby's own Garage
    /// (its button pressed while gathering), in PREVIEW: the vote's face again.</summary>
    [Fact]
    public async Task TheCourseVoteBringsThePlayerToItFromTheGarage()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            // the Garage place, a machine in PREVIEW
            main.Current = main.Garage;
            main.Garage.PreviewMachineCommand.Execute(11);
            PumpFor(100, state);
            Send(server, LedServer.Lobby(LobbyState.Starting), new CourseVote("m1", Options, 60_000));
            PumpTelling(state, () => lobby.VoteShown && main.Current is LobbyViewModel, "the vote did not bring the player to the lobby");
            Assert.True(lobby.LobbyShown);
            Pump(() => window.GetVisualDescendants().OfType<Button>().Count(b => b.DataContext is CourseOption && b.IsEffectivelyVisible) == 4,
                "the vote's cards are not on show");
            Assert.False(main.DecisionShown);

            // back to gathering; the lobby's own Garage, in PREVIEW; the next vote: its face
            Send(server, LedServer.Lobby(LobbyState.Forming));
            PumpTelling(state, () => !lobby.VoteShown);
            Click(window, Named<Button>(window, "Face"));
            Assert.True(lobby.GarageShown);
            main.Garage.PreviewMachineCommand.Execute(12);
            PumpFor(100, state);
            Send(server, LedServer.Lobby(LobbyState.Starting), new CourseVote("m2", Options, 60_000));
            PumpTelling(state, () => lobby.VoteShown && lobby.LobbyShown, "the vote stayed behind the lobby's Garage");
            Assert.Same(main.Lobby, main.Current);
            Pump(() => window.GetVisualDescendants().OfType<Button>().Count(b => b.DataContext is CourseOption && b.IsEffectivelyVisible) == 4);
        });
    }

    /// <summary>A change not saved in the builder is not taken from under the player: the vote opens, the page stays,
    /// and the left column names it, a link that brings them there; it goes when the vote is over.</summary>
    [Fact]
    public async Task MidChangeInTheBuilderTheColumnLinksToTheVote()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            var garage = main.Garage;
            main.Current = garage;
            garage.NewMachineCommand.Execute(null);
            garage.Builder.PickPartCommand.Execute(garage.Builder.Columns[0].Choices[3]);
            Assert.True(garage.MidEdit);
            Send(server, LedServer.Lobby(LobbyState.Starting), new CourseVote("m1", Options, 60_000));
            PumpTelling(state, () => lobby.VoteShown && main.DecisionShown, "the column does not name the vote");
            Assert.Same(garage, main.Current);                                      // the page stays
            Assert.Equal("Course vote", main.DecisionText);
            var link = Named<Button>(window, "Decision");
            Assert.True(link.IsEffectivelyVisible);
            Click(window, link);
            Assert.Same(main.Lobby, main.Current);
            Assert.True(lobby.LobbyShown && lobby.VoteShown);
            Assert.False(main.DecisionShown);
            Assert.True(garage.Builder.Unsaved);                                    // the change kept
        });
    }
}
