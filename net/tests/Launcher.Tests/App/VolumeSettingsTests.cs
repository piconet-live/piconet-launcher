using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
/// The Settings page's two volumes, used with the headless window's mouse and keyboard. The game's (the game
/// is loud): the slider moved, the number beside it, the setting kept, and the value where the game reads it when the launcher starts
/// the game (a script here that stands for the runner): the emulator's own volume in the game's settings. The
/// launcher's own sounds: their slider, heard a moment after it is set, and Mute, after which nothing sounds.
/// Beside nothing else: a start of the game ends every controller helper of the process.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class VolumeSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The launcher's state with a game that is "installed": a script that writes down that it was started.</summary>
    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), """{"noServer": true}""");
        var state = AppState.Load(data);
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{Started}.part'\nmv '{Started}.part' '{Started}'\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var module = Path.Combine(_dir, "m.dll");
        File.WriteAllText(module, "");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: Path.Combine(_dir, "game")) });
        Assert.Null(RecompLaunch.Problem(state.GamePaths));
        return state;
    }

    private string Started => Path.Combine(_dir, "started.txt");

    [Fact]
    public async Task TheGamesVolumeSetOnTheSettingsPageIsWhereTheGameReadsItAtAStart()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the game is a shell script here");
        var cancel = TestContext.Current.CancellationToken;
        var state = State();
        var heard = new List<(LobbyMoment Moment, int Volume)>();
        void Hear(LobbyMoment moment, int volume) => heard.Add((moment, volume));
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                Attention.Played += Hear;
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                window.Show();
                main.ShowSettings(SettingsTab.Game);                                    // the game's volume is on Game
                Dispatcher.UIThread.RunJobs();
                var view = window.GetVisualDescendants().OfType<SettingsView>().Single();

                // the game's volume: 100 as the game was, the number beside the slider
                var volume = Named<Slider>(view, "GameVolume");
                var number = Named<TextBlock>(view, "GameVolumeNumber");
                Assert.Equal((100, "100", 100), (volume.Value, number.Text, state.Settings.GameVolume));
                // pressed in its left half with the mouse, then finer with the arrow keys
                var at = volume.TranslatePoint(new Point(volume.Bounds.Width * 0.25, volume.Bounds.Height / 2), window)!.Value;
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.True(volume.Value < 100, "the press did not move the slider");
                Assert.Equal((int)volume.Value, state.Settings.GameVolume);
                volume.Focus();
                for (var i = 0; i < 40 && volume.Value != 35; i++)
                {
                    if (volume.Value > 35)
                    {
                        Press(window, Key.Left, PhysicalKey.ArrowLeft);
                    }
                    else
                    {
                        Press(window, Key.Right, PhysicalKey.ArrowRight);
                    }
                }
                Assert.Equal((35, "35", 35), (volume.Value, number.Text, state.Settings.GameVolume));
                Assert.Empty(heard);                                                    // (the game's volume makes no sound here)

                // a start of the game: the emulator's own volume in the game's settings is the slider's
                Assert.Null(state.PlayOffline());
                Pump(() => File.Exists(Started), "the game was not started");
                var ini = File.ReadAllLines(Path.Combine(state.GamePaths.UserFolder, "Config", "Dolphin.ini"));
                Assert.Equal("35", RecompLaunch.Setting(ini, "DSP", "Volume"));
                Assert.Equal("6.0", RecompLaunch.Setting(ini, "Core", "Overclock"));     // with what the game needs beside it
                Pump(() => !state.Racing);

                AStartWithSettingsThatCannotBeWrittenSaysSoAndStarts(window, state);

                // the launcher's own sounds, on Launcher: the slider is heard a moment after it is set, at what was set
                main.ShowSettings(SettingsTab.Launcher);
                Dispatcher.UIThread.RunJobs();
                var sounds = Named<Slider>(view, "SoundVolume");
                Assert.True(sounds.IsEffectivelyVisible);
                Assert.Equal((50, 50), (sounds.Value, state.Settings.SoundVolume));
                sounds.Focus();
                Press(window, Key.Right, PhysicalKey.ArrowRight);
                Press(window, Key.Right, PhysicalKey.ArrowRight);
                Assert.Equal((60, 60), (sounds.Value, state.Settings.SoundVolume));
                Pump(() => heard.Count == 1, "the launcher's volume was not heard when set");
                Assert.Equal((LobbyMoment.CourseVote, 60), heard[0]);
                // Mute: nothing sounds, the volume is kept, and the slider rests
                Click(window, Named<CheckBox>(view, "SoundMuted"));
                Assert.True(state.Settings.SoundMuted);
                Assert.Equal(60, state.Settings.SoundVolume);
                Assert.False(sounds.IsEnabled);
                PumpFor(400);
                Attention.Sound(LobbyMoment.RaceStart, state.Settings);
                Assert.Single(heard);
                window.Close();
                main.Dispose();
            }, cancel);

            // kept for the next start
            var again = AppState.Load(Path.Combine(_dir, "data"));
            Assert.Equal((35, 60, true), (again.Settings.GameVolume, again.Settings.SoundVolume, again.Settings.SoundMuted));
        }
        finally
        {
            await ui.Dispatch(() => Attention.Played -= Hear, CancellationToken.None);
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    /// <summary>The game's settings cannot be written with another volume: the game starts all the same, and the
    /// left column says the volume could not be set.</summary>
    private void AStartWithSettingsThatCannotBeWrittenSaysSoAndStarts(MainWindow window, AppState state)
    {
        var config = Path.Combine(state.GamePaths.UserFolder, "Config");
        File.Delete(Started);
        Close(config, true);
        try
        {
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal(30, state.Settings.GameVolume);
            Assert.Null(state.PlayOffline());
            Pump(() => File.Exists(Started), "the game was not started with settings that could not be written");
            Pump(() => state.Problem == AppState.VolumeNotSetText, "nothing said that the volume could not be set");
            Assert.Equal("35", RecompLaunch.Setting(File.ReadAllLines(Path.Combine(config, "Dolphin.ini")), "DSP", "Volume"));
        }
        finally
        {
            Close(config, false);
        }
        Pump(() => !state.Racing);
        Press(window, Key.Right, PhysicalKey.ArrowRight);
    }

    /// <summary>A folder that takes no new file (and so no write of a file in it, which goes beside the file first),
    /// or takes them again.</summary>
    private static void Close(string folder, bool closed)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(folder, closed ? UnixFileMode.UserRead | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
