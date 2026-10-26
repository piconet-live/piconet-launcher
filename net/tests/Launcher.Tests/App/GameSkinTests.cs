using System.Buffers.Binary;
using Avalonia;
using Avalonia.Headless;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// The launcher's start with a disc image whose art cannot be read (Launcher.App's own code, run without a window:
/// the application with its theme, the settings, <see cref="GameSkin.Load(string?)"/>, the window's view model and
/// the window, as <c>App.OnFrameworkInitializationCompleted</c> makes them). The image passes the first look, which
/// sees the header only, and the theme reads its files before anything else has: whatever they hold, the launcher
/// opens, with the plain look where there is no art. Made-up images only; no server is set, so nobody is asked.
/// </summary>
// One application at a time: each of these tests starts the launcher's application without a window, and a
// process holds one. In one collection the classes take turns (classes run side by side otherwise).
[Collection(HeadlessApp.Name)]
public sealed class GameSkinTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    public static AppBuilder BuildAvaloniaApp() => HeadlessApp.BuildAvaloniaApp();

    /// <summary>The second review's four files at once, each where the theme reads at the start.</summary>
    private string HostileDisc()
    {
        var good = GarageFiles.Tpl(GarageFiles.Cmpr(8, 8, (_, _) => new Rgb(200, 16, 16)));
        var disc = new SyntheticDisc();
        // the cloud: a compressed file that says it unpacks to 2 GiB
        var packed = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(4), int.MaxValue);
        disc.Files["init/sel.tpl.lz"] = packed;
        // the titles and the setting's labels: a texture in a format that is not decoded
        var format = (byte[])good.Clone();
        format[7] = 8;
        disc.Files["bmp_e/bmp_sel.tpl"] = format;
        // the lettering: a texture that says it is 65,535 by 65,535
        var huge = (byte[])good.Clone();
        huge[7] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(huge.AsSpan(12), 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(huge.AsSpan(14), 0xFFFF);
        disc.Files["bmp_e/bmp_com.tpl"] = huge;
        // a machine: a model without what its reader takes, and paint that is noise
        var model = GarageFiles.Gma(0, 1);
        BinaryPrimitives.WriteInt32BigEndian(model.AsSpan(481), 255);
        BinaryPrimitives.WriteInt32BigEndian(model.AsSpan(259), 1);
        disc.Files["vehicle/rgaze_p.arc"] = GarageFiles.Archive(("rgaze.gma", model), ("rgaze.tpl", good));
        foreach (var k in new[] { "", "_cv1", "_cv2", "_cv3", "_sv0", "_sv1", "_sv2", "_sv3" })
        {
            disc.Files[$"vehicle/rgaze/rgaze{k}.tpl"] = SyntheticDisc.Bytes(40 + k.Length, 300);
        }
        // and one good file, so that a look that works is told from none
        disc.Files["bmp_e/stg/bmp_stg_com.tpl"] = good;                       // "Random course"'s picture
        return disc.Write(_dir, "hostile.iso");
    }

    /// <summary>Runs the test's steps on an application's thread (one application for all of them, as one start
    /// has). What fails there is carried out and raised here. The session is put away without waiting on its own
    /// thread: its results come back on that thread, and waiting there for the thread to end never returns.</summary>
    private static async Task<T> OnTheApplication<T>(Func<T> steps)
    {
        var cancel = TestContext.Current.CancellationToken;
        var session = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            var (result, failure) = await session.Dispatch(() =>
            {
                try
                {
                    return (steps(), (System.Runtime.ExceptionServices.ExceptionDispatchInfo?)null);
                }
#pragma warning disable CA1031 // any failure is the test's result
                catch (Exception e)
#pragma warning restore CA1031
                {
                    return (default(T)!, System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e));
                }
            }, cancel);
            failure?.Throw();
            return result;
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheLauncherStartsOnADiscWhoseArtCannotBeRead()
    {
        var cancel = TestContext.Current.CancellationToken;
        var image = HostileDisc();
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(image));
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            System.Text.Json.JsonSerializer.Serialize(new { discPath = image, noServer = true }));

        var machine = await OnTheApplication(() =>
        {
            // the start, as App makes it: the settings, then the theme's load, which returns
            var resources = Application.Current!.Resources;
            var state = AppState.Load(data);
            Assert.Null(state.Settings.QuickPlayServer);

            GameSkin.Load(state.Settings.DiscPath);

            Assert.Null(GameSkin.Problem);                                     // the readers gave nothing, and raised nothing
            Assert.Equal(true, resources["HasDiscArt"]);
            Assert.False(resources.ContainsKey("CloudImage"));
            Assert.False(resources.ContainsKey("BrandLettering"));
            Assert.False(resources.ContainsKey("AccelerationLabel"));
            Assert.False(resources.ContainsKey($"Title.{MenuTitle.Course}"));
            Assert.NotNull(GameSkin.Venue(Online.Protocol.CourseVote.RandomCourse));   // the one good file is shown
            Assert.Null(GameSkin.Venue(1));
            _ = GameSkin.StockSwatches(0);
            var model = GameSkin.Machine3D(0);

            // and the start goes on: the window's view model and the window, on the launcher's first page
            using (var main = new MainViewModel(state))
            {
                var window = new MainWindow { DataContext = main, Width = 1116, Height = 789 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.IsType<HomeViewModel>(main.Current);
                main.Current = main.Garage;                                    // the page that shows the machines
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Close();
            }

            // the guard itself: art whose opening fails with an error of any kind is the plain look and one line
            var other = new SyntheticDisc().Write(_dir, "other.iso");
            GameSkin.Load(other, _ =>
            {
                GC.KeepAlive(new byte[int.MaxValue]);                          // what the decompressor did: out of memory, at once
                return null;
            });
            Assert.Contains("OutOfMemoryException", GameSkin.Problem, StringComparison.Ordinal);
            Assert.Contains("plain look", GameSkin.Problem, StringComparison.Ordinal);
            Assert.Equal(false, resources["HasDiscArt"]);
            Assert.Equal(true, resources["NoDiscArt"]);
            Assert.Null(GameSkin.Venue(Online.Protocol.CourseVote.RandomCourse));
            var opened = 0;
            GameSkin.Load(other, _ =>
            {
                opened++;
                return null;
            });
            Assert.Equal(0, opened);                                           // not tried again for the same disc
            GameSkin.Load(image);                                              // another disc is
            Assert.Null(GameSkin.Problem);
            Assert.Equal(true, resources["HasDiscArt"]);
            GameSkin.Load(null);
            state.Shutdown();
            return model;
        });
        Assert.Null(await machine.WaitAsync(TimeSpan.FromSeconds(30), cancel));   // no model, and its reading came back
    }
}
