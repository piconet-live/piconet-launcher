using Avalonia.Headless;
using Launcher.App.Views;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

public sealed partial class LobbyStageRulesTests
{
    /// <summary>Quick Play's start countdown (<see cref="LobbyState.StartsInMs"/>): the lobby's clock counts it down
    /// while the lobby gathers, from the time the server gave; a state without it (too few players, the rule off)
    /// takes the clock away. With LOBBY_SHOTS naming a folder, the lobby counting down is kept there as a PNG.</summary>
    [Fact]
    public async Task TheGatheringLobbyCountsDownToItsStart()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            Assert.Equal("", lobby.ClockText);                                     // no countdown yet
            Send(server, LedServer.Lobby(LobbyState.Forming) with { StartsInMs = 42_500 });
            PumpTelling(state, () => lobby.ClockText == "0'43\"", $"the clock showed {lobby.ClockText}");
            PumpFor(1_000, state);
            Assert.Matches("^0'4[12]\"$", lobby.ClockText);                       // it runs on the launcher's clock
            if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is { Length: > 0 } folder)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Directory.CreateDirectory(folder);
                window.CaptureRenderedFrame()?.Save(Path.Combine(folder, "lobby-countdown.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            Send(server, LedServer.Lobby(LobbyState.Forming));                     // a player left: below the minimum
            PumpTelling(state, () => lobby.ClockText == "", $"the clock still showed {lobby.ClockText}");
        });
    }
}
