using System.Diagnostics;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The handler of last resort (<see cref="LastResort"/>): an error nothing caught is written to the
/// launcher's log with the time and the build, and the player is told once, in plain words. The routes such an
/// error takes are put on it by the launcher's start-up, so they are tried in the launcher itself, started as a
/// process of the test's own.</summary>
public sealed class LastResortTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Log => Path.Combine(_dir, "data", "launcher.log");

    private static InvalidOperationException Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    private static int Entries(string log) => log.Split('\n').Count(line => line.StartsWith("==== ", StringComparison.Ordinal));

    [Fact]
    public void AnErrorIsWrittenWithTheTimeAndTheBuildAndThePlayerIsToldOnce()
    {
        var told = new List<string>();
        var faults = new LastResort(Log, "Version 0.1.0 · build abc1234", () => new DateTimeOffset(2026, 10, 6, 15, 4, 5, TimeSpan.FromHours(-4))) { Tell = told.Add };
        Assert.False(File.Exists(Log));                                        // nothing is made before an error

        Assert.True(faults.Caught("on the window's thread", Thrown("the first")));
        Assert.True(faults.Caught("in a background task", Thrown("the second")));

        var log = File.ReadAllText(Log);
        Assert.Equal(2, Entries(log));
        Assert.StartsWith("==== 2026-10-06 15:04:05 -04:00 · Version 0.1.0 · build abc1234 · not caught on the window's thread", log, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: the first", log, StringComparison.Ordinal);
        Assert.Contains("at Launcher.Tests.LastResortTests.Thrown", log, StringComparison.Ordinal);   // where it was thrown
        Assert.Contains("· not caught in a background task", log, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: the second", log, StringComparison.Ordinal);
        // told once, in plain words, with the log by its name: where it is is offered beside the notice (a path
        // is no reading for a notice, and wrapped badly in the launcher's left column)
        var notice = Assert.Single(told);
        Assert.Equal(faults.Notice, notice);
        Assert.Contains(Path.GetFileName(Log), notice, StringComparison.Ordinal);
        Assert.DoesNotContain(_dir, notice, StringComparison.Ordinal);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(Log)), faults.LogFolder);
        Assert.True(notice.Length < 120);
        Assert.DoesNotContain("Exception", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", notice, StringComparison.Ordinal);
    }

    /// <summary>What is no error of the launcher's but belongs in its log (a server's match start refused): one
    /// entry with the time and the build, not called an error, and the player is not told.</summary>
    [Fact]
    public void ANoteIsWrittenAsAnEntryAndNobodyIsTold()
    {
        var told = new List<string>();
        var faults = new LastResort(Log, "b", () => new DateTimeOffset(2026, 10, 7, 0, 1, 2, TimeSpan.Zero)) { Tell = told.Add };

        faults.Note("a match start for a player in no lobby was refused");

        var log = File.ReadAllText(Log);
        Assert.Equal("==== 2026-10-07 00:01:02 +00:00 · b · a match start for a player in no lobby was refused" + Environment.NewLine, log);
        Assert.Empty(told);
        // a note is an entry of the run like any other: notes without end do not fill the log either
        for (var i = 0; i < 500; i++)
        {
            faults.Note($"note {i}");
        }
        Assert.Equal(LastResort.MostEntries + 1, Entries(File.ReadAllText(Log)));
    }

    [Fact]
    public void ThePlayerIsToldOfTheFirstErrorThereIsSomebodyToTell()
    {
        var told = new List<string>();
        var faults = new LastResort(Log, "b");
        faults.Caught("at the start", Thrown("before any screen"));
        faults.Tell = told.Add;
        faults.Caught("while the launcher closes", Thrown("written only"), tell: false);
        Assert.Empty(told);
        faults.Caught("later", Thrown("with a screen"));
        faults.Caught("later", Thrown("again"));
        Assert.Single(told);
        Assert.Equal(4, Entries(File.ReadAllText(Log)));
    }

    /// <summary>An error in a job of the window's thread comes a second time, as the error of the job's task that
    /// nobody waited for: it is written once.</summary>
    [Fact]
    public void AnErrorThatComesByTwoRoutesIsWrittenOnce()
    {
        var told = new List<string>();
        var faults = new LastResort(Log, "b") { Tell = told.Add };
        var error = Thrown("in a job of the window's thread");
        Assert.True(faults.Caught("on the window's thread", error));
        Assert.True(faults.Caught("in a background task", new AggregateException(error)));
        Assert.True(faults.Caught("on the window's thread", error));
        Assert.Equal(1, Entries(File.ReadAllText(Log)));
        // another task's errors, one of them new, are written
        Assert.True(faults.Caught("in a background task", new AggregateException(error, Thrown("another"))));
        Assert.Equal(2, Entries(File.ReadAllText(Log)));
        Assert.Single(told);
    }

    [Fact]
    public void ErrorsWithoutEndDoNotFillTheLog()
    {
        var faults = new LastResort(Log, "b");
        for (var i = 0; i < 500; i++)
        {
            faults.Caught("at every frame", new InvalidOperationException(new string('x', 200_000)));
        }
        var log = File.ReadAllText(Log);
        Assert.Equal(LastResort.MostEntries + 1, Entries(log));
        Assert.Contains("more errors came in this run and are not written", log, StringComparison.Ordinal);
        Assert.Contains("(cut: the error's text is longer)", log, StringComparison.Ordinal);
        Assert.InRange(log.Length, 1, LastResort.MostEntries * (LastResort.MostEntryLength + 400));
    }

    [Fact]
    public void ALongLogIsSetAsideWhenARunFirstWritesToIt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, new string('o', (int)LastResort.MostLogBytes + 1));
        var faults = new LastResort(Log, "b");
        faults.Caught("here", Thrown("one"));
        faults.Caught("here", Thrown("two"));
        Assert.Equal(LastResort.MostLogBytes + 1, new FileInfo(Log + ".old").Length);
        Assert.Equal(2, Entries(File.ReadAllText(Log)));
        Assert.DoesNotContain("ooo", File.ReadAllText(Log), StringComparison.Ordinal);
    }

    [Fact]
    public void ALogThatCannotBeWrittenAndAScreenThatCannotBeToldRaiseNothing()
    {
        // the log's folder is a file: nothing can be written there
        File.WriteAllText(Path.Combine(_dir, "taken"), "");
        var mute = new LastResort(Path.Combine(_dir, "taken", "launcher.log"), "b") { Tell = _ => throw new InvalidOperationException("no screen") };
        Assert.True(mute.Caught("anywhere", Thrown("one")));
        Assert.True(mute.Caught("anywhere", null));

        var faults = new LastResort(Log, "b") { Tell = _ => throw new InvalidOperationException("no screen") };
        Assert.True(faults.Caught("anywhere", Thrown("one")));
        var log = File.ReadAllText(Log);
        Assert.Contains("not caught while the player was told of the error above", log, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: no screen", log, StringComparison.Ordinal);
    }

    /// <summary>The handler is done with an error (<see cref="LastResort.Dealt"/>) only when the player was told,
    /// which comes after the log's entry: the launcher's test door waits for that before it ends the launcher (it
    /// waited for the log, and under load ended before the notice was written). An error that was written already
    /// is not counted again; one that nobody is told of is.</summary>
    [Fact]
    public void TheHandlerIsDoneWithAnErrorOnlyOnceThePlayerIsTold()
    {
        var faults = new LastResort(Log, "Version 0.1.0 · build abc1234");
        var whenTold = new List<(bool LogThere, int Dealt)>();
        faults.Tell = _ => whenTold.Add((File.Exists(Log), faults.Dealt));
        Assert.Equal(0, faults.Dealt);

        var first = Thrown("the first");
        faults.Caught("on a background thread", first);
        Assert.Equal([(true, 0)], whenTold);                                   // written, told, and only then done
        Assert.Equal(1, faults.Dealt);

        faults.Caught("in a background task", first);                          // the same error by a second route
        Assert.Equal(1, faults.Dealt);
        faults.Caught("by anything: the launcher closes", Thrown("the second"), tell: false);
        Assert.Equal(2, faults.Dealt);
        Assert.Single(whenTold);
    }

    /// <summary>The launcher itself (this build's, beside the tests'), without its window, on a data folder of the
    /// test's own: its exit status, standard output and standard error. A launcher still running after a minute is
    /// ended, by the test's own handle of it.</summary>
    private (int Status, string Output, string Error) Launcher(params string[] arguments)
    {
        var app = Path.Combine(InstallTests.LauncherAppFolder, "Launcher.App.dll");
        Assert.True(File.Exists(app), $"the launcher is not built: {app}");
        var host = Environment.ProcessPath is { } running && Path.GetFileNameWithoutExtension(running) == "dotnet" ? running : "dotnet";
        var start = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in (string[])[app, "--data", Path.Combine(_dir, "data"), .. arguments])
        {
            start.ArgumentList.Add(argument);
        }
        using var launcher = Process.Start(start)!;
        var output = launcher.StandardOutput.ReadToEndAsync();
        var error = launcher.StandardError.ReadToEndAsync();
        if (!launcher.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            launcher.Kill();
            Assert.Fail("the launcher did not end");
        }
        launcher.WaitForExit();
        return (launcher.ExitCode, output.Result, error.Result);
    }

    /// <summary>An error nothing catches on each route (the launcher's own test door raises one, a launch file's
    /// <c>"fault"</c>): the launcher's start-up has put the handler there, and
    /// the error is in the log with the time, the build and the route. On the routes that can go on, the player is
    /// told once (on standard error without the window) and the launcher is still running after it. The last
    /// route is the one that ends the process: its event is raised here without the end (a process that ends so
    /// aborts, which leaves a crash dump on this computer), so only what is written is shown, and that nobody is
    /// told by a launcher that is closing.</summary>
    [Theory]
    [InlineData("window", "on the window's thread (the launcher goes on)", true)]
    [InlineData("task", "in a background task (the launcher goes on)", true)]
    [InlineData("thread", "on a background thread (that thread ended, the launcher goes on)", true)]
    [InlineData("process", "by anything: the launcher closes", false)]
    public void TheLauncherWritesAnErrorNothingCaught(string route, string where, bool goesOn)
    {
        var file = Path.Combine(_dir, "fault.json");
        File.WriteAllText(file, """{"about": "an error on one route, without the window", "debug": {"fault": "ROUTE", "faultWithoutWindow": true}}"""
            .Replace("ROUTE", route, StringComparison.Ordinal));
        var (status, output, error) = Launcher("--config", file);

        Assert.True(File.Exists(Log), $"no log was written; the launcher said: {output}{error}");
        var log = File.ReadAllText(Log);
        Assert.Equal(1, Entries(log));
        Assert.Matches(@"^==== \d{4}-\d\d-\d\d \d\d:\d\d:\d\d [+-]\d\d:\d\d · Version \d+\.\d+\.\d+", log);
        Assert.Contains($"· not caught {where}", log, StringComparison.Ordinal);
        Assert.Contains($"System.InvalidOperationException: test fault: {route}", log, StringComparison.Ordinal);
        Assert.Contains("still running", output, StringComparison.Ordinal);
        Assert.Equal(0, status);
        if (goesOn)
        {
            // on the console the notice is followed by where the log is
            Assert.Equal($"Launcher error · launcher.log ({Log})", error.Trim());
        }
        else
        {
            Assert.Equal("", error);
        }
    }

    /// <summary>Without an error the launcher writes no log (here: the list of an empty replay folder).</summary>
    [Fact]
    public void ALauncherWithoutAnErrorWritesNoLog()
    {
        var (status, _, error) = Launcher("--list-replays");
        Assert.Equal(0, status);
        Assert.Equal("", error);
        Assert.False(File.Exists(Log));
    }

    /// <summary>An option the launcher does not know starts nothing: the test settings that were options once
    /// (<c>--quick-play</c>, <c>--autopilot</c>) are a launch file's now, and a script that still passes one must
    /// not get the window, with the player's own server, in its place.</summary>
    [Theory]
    [InlineData("--quick-play")]
    [InlineData("--autopilot")]
    [InlineData("--no-such-option")]
    public void AnOptionTheLauncherDoesNotKnowIsRefusedAndNothingStarts(string option)
    {
        var (status, output, error) = Launcher(option);
        Assert.Equal(64, status);
        Assert.Equal("", output);
        Assert.Equal($"{option} is not an option of the launcher. Test settings are named in a launch file (--config FILE).", error.Trim());
        Assert.False(File.Exists(Log));
    }

    /// <summary>A launch file with a setting the launcher does not know is refused whole, with the setting's name:
    /// read as "off", a mistyped test setting would run the test without it.</summary>
    [Fact]
    public void ALaunchFileWithASettingTheLauncherDoesNotKnowIsRefused()
    {
        var file = Path.Combine(_dir, "odd.json");
        File.WriteAllText(file, """{"about": "a mistyped setting", "debug": {"autoPilot": true}}""");
        var (status, _, error) = Launcher("--config", file, "--list-replays");
        Assert.Equal(64, status);
        Assert.Equal($"The launch file is refused: \"debug.autoPilot\" is not a setting of a launch file ({file})", error.Trim());
    }
}
