using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The Local network tab's Join needs what a race needs, by the rule RACE reads too
/// (<see cref="AppState.WhyNotJoin"/>): the disc, and the game with its online part. Without them the launcher
/// joined all the same, and at the race's start, with no game to open, the seat went to the AI.</summary>
public sealed class LanJoinTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new { noServer = true }));
        return AppState.Load(data);
    }

    /// <summary>What the page says after Join by address, and whether anything was joined or shown.</summary>
    private static async Task<(string Notice, bool Joined, bool LobbyShown)> Join(AppState state)
    {
        var shown = false;
        using var page = new LanViewModel(state, () => shown = true) { Address = "127.0.0.1:9" };   // nothing listens there
        await page.JoinAddressCommand.ExecuteAsync(null);
        return (page.Notice, state.Session is not null, shown);
    }

    [Fact]
    public async Task WithoutADiscOrWithoutTheGameNothingIsJoinedAndThePageSaysWhy()
    {
        var state = State();
        try
        {
            Assert.Equal(("No disc image", false, false), await Join(state));

            state.Update(s => s with { DiscPath = new SyntheticDisc().Write(_dir, "gx.iso") });
            Assert.Equal(RecompLaunch.NoProgram, state.RaceProblem);
            Assert.Equal((RecompLaunch.NoProgram, false, false), await Join(state));

            // the game, without its online part: offline play would start, a race would not
            var (runner, module, game) = (Path.Combine(_dir, "runner.sh"), Path.Combine(_dir, "m.dll"), Path.Combine(_dir, "game"));
            File.WriteAllText(runner, "#!/bin/sh\nexit 0\n");
            File.WriteAllText(module, "");
            Directory.CreateDirectory(Path.Combine(game, "sys"));
            File.WriteAllText(Path.Combine(game, "sys", "main.dol"), "");
            state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: game) });
            await state.LookForTheGame();                              // as after a preparation: the game is there now
            Assert.Null(state.GameProblem);
            Assert.Equal((RecompLaunch.NoOnlinePart, false, false), await Join(state));
            Assert.Equal(RecompLaunch.NoOnlinePart, state.WhyNotJoin);   // and RACE reads the same answer
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>With the disc and the whole game the join is tried: here the server is not there, and the page says so.</summary>
    [Fact]
    public async Task WithTheDiscAndTheGameTheJoinIsTried()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var state = State();
        try
        {
            var (runner, module, game, bundle) = (Path.Combine(_dir, "runner.sh"), Path.Combine(_dir, "m.dll"), Path.Combine(_dir, "game"), Path.Combine(_dir, "fzgx"));
            // the controllers' check asks the game's own program: it answers as that does, and ends
            File.WriteAllText(runner, """
                #!/bin/sh
                echo '{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[]}'
                echo '{"is":"ports","ports":[{"kind":0,"device":"","connected":false}]}'
                echo '{"is":"ready","version":1}'
                cat > /dev/null

                """.Replace("                ", "", StringComparison.Ordinal));
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(module, "");
            Directory.CreateDirectory(Path.Combine(game, "sys"));
            File.WriteAllText(Path.Combine(game, "sys", "main.dol"), "");
            Directory.CreateDirectory(bundle);
            File.WriteAllText(Path.Combine(bundle, "mod.bin"), "");
            state.Update(s => s with
            {
                DiscPath = new SyntheticDisc().Write(_dir, "gx.iso"),
                Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: game, Bundle: bundle),
            });
            Assert.Null((await state.LookForTheGame()).RaceProblem);
            Assert.Null(state.WhyNotJoin);

            var (notice, _, shown) = await Join(state);

            Assert.StartsWith("Could not join: ", notice, StringComparison.Ordinal);
            Assert.False(shown);
        }
        finally
        {
            // (the join asked for the controllers' check and failed before the check had its answer: the shutdown
            // ends the check, which else went on writing into this test's folder while it was taken away)
            state.Shutdown();
        }
    }
}
