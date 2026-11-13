using System.Collections.Concurrent;
using System.Diagnostics;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>A replay file handed to the window that is open on the same data folder (<see cref="ReplayHandOver"/>):
/// it arrives as its full path, only at the window of that data folder; with no window listening the sender knows
/// at once and opens its own; a line that is no replay file's path is not passed on.</summary>
public sealed class ReplayHandOverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A name no other test (nor a launcher on this PC) listens under.</summary>
    private string Name(string folder) => ReplayHandOver.NameOf(Path.Combine(_dir, folder));

    [Fact]
    public void TheWindowOfTheDataFolderGetsTheFile()
    {
        var got = new BlockingCollection<string>();
        using var window = ReplayHandOver.Listen(Name("data"), got.Add);
        var file = Path.Combine(_dir, "20261108-201500-m1.fzrp");

        Assert.True(ReplayHandOver.Send(Name("data"), file, TimeSpan.FromSeconds(10)));
        Assert.True(got.TryTake(out var path, TimeSpan.FromSeconds(10)));
        Assert.Equal(file, path);

        // a second file, to the same window: it listens on after the first
        var second = Path.Combine(_dir, "b.FZRP");
        Assert.True(ReplayHandOver.Send(Name("data"), second, TimeSpan.FromSeconds(10)));
        Assert.True(got.TryTake(out path, TimeSpan.FromSeconds(10)));
        Assert.Equal(second, path);
    }

    /// <summary>A relative path goes as the full one, from the sender's own folder.</summary>
    [Fact]
    public void ARelativePathArrivesInFull()
    {
        var got = new BlockingCollection<string>();
        using var window = ReplayHandOver.Listen(Name("data"), got.Add);
        Assert.True(ReplayHandOver.Send(Name("data"), "race.fzrp", TimeSpan.FromSeconds(10)));
        Assert.True(got.TryTake(out var path, TimeSpan.FromSeconds(10)));
        Assert.Equal(Path.GetFullPath("race.fzrp"), path);
    }

    /// <summary>No window on that data folder: the sender knows at once (no wait for a pipe that is not there),
    /// and the window of another data folder gets nothing.</summary>
    [Fact]
    public void WithNoWindowOnTheDataFolderTheSenderOpensItsOwn()
    {
        var got = new BlockingCollection<string>();
        using var other = ReplayHandOver.Listen(Name("other"), got.Add);
        var time = Stopwatch.StartNew();

        Assert.False(ReplayHandOver.Send(Name("data"), Path.Combine(_dir, "a.fzrp"), TimeSpan.FromSeconds(10)));
        Assert.True(time.Elapsed < TimeSpan.FromSeconds(2), $"the sender waited {time.Elapsed}");
        Assert.Empty(got);
    }

    /// <summary>A window closed stops listening: the next start opens its own.</summary>
    [Fact]
    public void AClosedWindowListensNoMore()
    {
        var got = new BlockingCollection<string>();
        var window = ReplayHandOver.Listen(Name("data"), got.Add);
        window.Dispose();
        window.Dispose();   // (twice does nothing)

        Assert.False(ReplayHandOver.Send(Name("data"), Path.Combine(_dir, "a.fzrp"), TimeSpan.FromSeconds(2)));
        Assert.Empty(got);
    }

    /// <summary>The name is the data folder's, whatever its spelling; another folder's is another.</summary>
    [Fact]
    public void TheNameIsTheDataFolders()
    {
        var data = Path.Combine(_dir, "data");
        Assert.Equal(ReplayHandOver.NameOf(data), ReplayHandOver.NameOf(data + Path.DirectorySeparatorChar));
        Assert.Equal(ReplayHandOver.NameOf(data), ReplayHandOver.NameOf(Path.Combine(_dir, "x", "..", "data")));
        Assert.NotEqual(ReplayHandOver.NameOf(data), ReplayHandOver.NameOf(Path.Combine(_dir, "data2")));
        Assert.StartsWith("piconet-open-", ReplayHandOver.NameOf(data), StringComparison.Ordinal);
    }

    /// <summary>What the window takes from a sender: a replay file's full path, nothing else.</summary>
    [Fact]
    public void OnlyAReplayFilesFullPathIsTaken()
    {
        Assert.True(ReplayHandOver.Acceptable(Path.Combine(_dir, "a.fzrp")));
        Assert.False(ReplayHandOver.Acceptable("a.fzrp"));                                   // not a full path
        Assert.False(ReplayHandOver.Acceptable(Path.Combine(_dir, "a.json")));
        Assert.False(ReplayHandOver.Acceptable(""));
        Assert.False(ReplayHandOver.Acceptable(null));
        Assert.False(ReplayHandOver.Acceptable(Path.Combine(_dir, new string('a', 5000) + ".fzrp")));
    }
}
