using System.Reflection;
using Launcher.App;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>Where a launcher is and where it keeps its data (<see cref="Install"/>,
/// <see cref="AppState.DataFolderOf(string?, Install)"/>): a player copy is known by the marker beside its libraries,
/// with no command line; a launcher without the marker is as it always was.</summary>
public sealed class InstallTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The launcher's own build folder, as the project file asked it of Launcher.App's project (the
    /// assembly's metadata "LauncherAppFolder"): this build's, wherever its output is.</summary>
    internal static string LauncherAppFolder { get; } = typeof(InstallTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "LauncherAppFolder").Value!;

    /// <summary>A player copy as the package script lays it out: the libraries and the marker in <c>app</c>.</summary>
    private string Copy()
    {
        var copy = Path.Combine(_dir, "Some Copy 0.2.0");
        Directory.CreateDirectory(Path.Combine(copy, Install.ProgramFolder));
        File.WriteAllText(Path.Combine(copy, Install.ProgramFolder, Install.MarkerName), "a player copy");
        return copy;
    }

    [Fact]
    public void APlayerCopyKeepsItsDataInItselfAndFindsTheGameBesideItsProgram()
    {
        var copy = Copy();
        foreach (var libraries in new[] { Path.Combine(copy, "app"), Path.Combine(copy, "app") + Path.DirectorySeparatorChar })
        {
            var install = Install.Of(libraries);                               // as the running launcher names its folder
            Assert.Equal((copy, Path.Combine(copy, "data")), (install.Folder, install.DataFolder));
            Assert.Equal(Path.Combine(copy, "data"), AppState.DataFolderOf(null, install));
            var paths = RecompLaunch.Resolve(null, install.Folder, AppState.DataFolderOf(null, install));
            Assert.Equal(Path.Combine(copy, "recomp", RecompLaunch.RunnerFileName), paths.Runner);
            Assert.Equal(Path.Combine(copy, "recomp", RecompLaunch.ModuleFileName), paths.Module);
            Assert.Equal(Path.Combine(copy, "recomp", "fzgx"), paths.Bundle);
            Assert.Equal(Path.Combine(copy, "data", "recomp-user"), paths.UserFolder);
        }
        Assert.False(Directory.Exists(Path.Combine(copy, "data")));            // asking makes nothing
    }

    /// <summary>An installed launcher (Setup, kept up to date by Velopack, which replaces its folder whole at every
    /// update): the marker the release script writes beside its libraries, the game beside them, the data in the
    /// player's own folder, and the game's files from the prepared folder only, as in a player copy.</summary>
    [Fact]
    public void AnInstalledLauncherKeepsItsDataOutOfItsFolder()
    {
        var folder = Path.Combine(_dir, "current");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Install.InstalledMarkerName), "installed");
        var install = Install.Of(folder);
        Assert.Equal((folder, (string?)null, true, true, false), (install.Folder, install.DataFolder, install.Installed, install.PlayerCopy, install.Portable));
        var players = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fzgx-launcher");
        Assert.Equal(players, AppState.DataFolderOf(null, install));
        var data = Path.Combine(_dir, "a data folder");                       // (not the PC's own, which may hold a game)
        var paths = RecompLaunch.Resolve(null, install, data);
        Assert.Equal(Path.Combine(folder, "recomp", RecompLaunch.RunnerFileName), paths.Runner);
        Assert.Equal(Path.Combine(data, "recomp-user"), paths.UserFolder);
        Assert.True(paths.PlayerCopy && paths.Installed);
        Assert.True(paths.NoGame);                                             // nothing prepared: no recomp/game taken
        Assert.Equal(RecompLaunch.NoProgramInstalled, RecompLaunch.Problem(paths, processorRunsTheGame: true));   // nor any program beside it here
        Assert.Null(install.OlderCopy());                                      // never said in an installed launcher
        // both markers: the installed one wins (the data must never go into a folder an update replaces)
        File.WriteAllText(Path.Combine(folder, Install.MarkerName), "");
        Assert.True(Install.Of(folder).Installed);
    }

    [Fact]
    public void TheDataOptionWinsOverTheMarker()
    {
        var install = Install.Of(Path.Combine(Copy(), "app"));
        var given = Path.Combine(_dir, "elsewhere");
        Assert.Equal(given, AppState.DataFolderOf(given, install));
    }

    [Fact]
    public void WithoutTheMarkerTheLauncherIsWhereItsLibrariesAreAndTheDataIsThePlayers()
    {
        var folder = Path.Combine(_dir, "launcher-dev");
        Directory.CreateDirectory(folder);
        var install = Install.Of(folder);
        Assert.Equal((folder, null), (install.Folder, install.DataFolder));
        var players = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fzgx-launcher");
        Assert.Equal(players, AppState.DataFolderOf(null, install));
        Assert.Equal(Path.Combine(folder, "recomp", RecompLaunch.RunnerFileName), RecompLaunch.Resolve(null, install.Folder, players).Runner);

        // a marker anywhere else than beside the libraries marks nothing: the folder above, a folder below
        File.WriteAllText(Path.Combine(_dir, Install.MarkerName), "");
        Directory.CreateDirectory(Path.Combine(folder, "app"));
        File.WriteAllText(Path.Combine(folder, "app", Install.MarkerName), "");
        Assert.Equal((folder, null), (Install.Of(folder).Folder, Install.Of(folder).DataFolder));
        // and a folder of that name is no marker
        Directory.CreateDirectory(Path.Combine(folder, Install.MarkerName));
        Assert.Null(Install.Of(folder).DataFolder);
    }

    [Fact]
    public void AnOlderCopysFilesInAPlayerCopyAreSaidOnce()
    {
        var copy = Copy();
        var install = Install.Of(Path.Combine(copy, "app"));
        Assert.True(install.PlayerCopy);
        Assert.Null(install.OlderCopy());                                      // a copy as it is unzipped
        Directory.CreateDirectory(Path.Combine(copy, "recomp", "Sys"));
        Directory.CreateDirectory(Path.Combine(copy, "data", "replays"));
        Assert.Null(install.OlderCopy());

        // the first build's copy under it: each of its files that is not in a copy of this layout
        foreach (var name in new[] { "Launcher.App.exe", "Launcher.App.dll", "2 Play.cmd", "1 Prepare disc - drag your ISO onto me.cmd" })
        {
            File.WriteAllText(Path.Combine(copy, name), "");
            Assert.Equal(Install.OlderCopyNotice, install.OlderCopy());
            File.Delete(Path.Combine(copy, name));
        }
        Directory.CreateDirectory(Path.Combine(copy, "recomp", "game"));       // its game folder, carried across
        Assert.Equal(Install.OlderCopyNotice, install.OlderCopy());
        Assert.Contains("move only \"data\"", Install.OlderCopyNotice, StringComparison.Ordinal);

        // a launcher that is no player copy has those very files, and is told nothing
        var flat = Path.Combine(_dir, "launcher-dev");
        Directory.CreateDirectory(Path.Combine(flat, "recomp", "game"));
        File.WriteAllText(Path.Combine(flat, "Launcher.App.exe"), "");
        Assert.Null(Install.Of(flat).OlderCopy());
    }

    /// <summary>The launcher itself, started as a pinned taskbar button or a shortcut starts a copy's program: no
    /// arguments that name a folder, and a working directory that is not the copy's. The launcher's own files are
    /// copied into a made-up copy's <c>app</c> (this build's, 18 MB), with the marker; it lists its replays (a
    /// mode without a window) and must keep everything in the copy's <c>data</c>.</summary>
    [Fact]
    public async Task ACopyStartedWithNoDataOptionFromAnotherWorkingDirectoryKeepsItsDataInTheCopy()
    {
        var cancel = TestContext.Current.CancellationToken;
        if (!OperatingSystem.IsLinux())
        {
            return;   // the libraries' folder of native files is linked, not copied
        }
        var built = LauncherAppFolder;
        Assert.True(File.Exists(Path.Combine(built, "Launcher.App.dll")), $"the launcher is not built: {built}");
        var copy = Copy();
        var app = Path.Combine(copy, Install.ProgramFolder);
        foreach (var file in Directory.GetFiles(built))
        {
            File.Copy(file, Path.Combine(app, Path.GetFileName(file)));
        }
        Directory.CreateSymbolicLink(Path.Combine(app, "runtimes"), Path.Combine(built, "runtimes"));
        var (elsewhere, players) = (Path.Combine(_dir, "elsewhere"), Path.Combine(_dir, "the player's own"));
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(players);

        var host = Environment.ProcessPath is { } running && Path.GetFileNameWithoutExtension(running) == "dotnet" ? running : "dotnet";
        var start = new System.Diagnostics.ProcessStartInfo(host)
        {
            WorkingDirectory = elsewhere, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(app, "Launcher.App.dll"));
        start.ArgumentList.Add("--list-replays");
        // were the marker not found, the launcher would write into the player's own folder: that is this one here
        start.Environment["XDG_DATA_HOME"] = players;
        using var launcher = System.Diagnostics.Process.Start(start)!;
        var (output, error) = (launcher.StandardOutput.ReadToEndAsync(cancel), launcher.StandardError.ReadToEndAsync(cancel));
        try
        {
            await launcher.WaitForExitAsync(cancel).WaitAsync(TimeSpan.FromSeconds(60), cancel);
        }
        catch (TimeoutException)
        {
            launcher.Kill();
            Assert.Fail("the launcher did not end");
        }
        Assert.True(launcher.ExitCode == 0, await output + await error);

        Assert.True(File.Exists(Path.Combine(copy, "data", "settings.json")));   // its settings, in the copy
        Assert.Empty(Directory.GetFileSystemEntries(players));                 // nothing in the player's own folder
        Assert.Empty(Directory.GetFileSystemEntries(elsewhere));               // nor where it was started from
        Assert.Equal(["app", "data"], Directory.GetFileSystemEntries(copy).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void ThisLauncherInATestIsNoPlayerCopy()
    {
        Assert.Null(Install.This.DataFolder);
        Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Install.This.Folder);
    }
}
