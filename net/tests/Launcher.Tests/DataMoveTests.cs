using Launcher.Core;

namespace Launcher.Tests;

/// <summary>An older player copy's data taken into an installed launcher's data folder (<see cref="DataMove"/>):
/// what is taken and what is left, the settings last with the data folder's own kept beside them, files the data
/// folder has kept, links never followed, and the older copy unchanged.</summary>
public sealed class DataMoveTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>An older copy as players had it: the program's folders and its data folder.</summary>
    private string OlderCopy()
    {
        var copy = Path.Combine(_dir, "Piconet-0.4.0-abcdef12");
        var data = Path.Combine(copy, Install.DataName);
        Write(copy, "recomp/moderngekko-run.exe", "a program");
        Write(data, "settings.json", "{\"playerKey\":\"old\"}");
        Write(data, "replays/race 1.fzrp", "a replay");
        Write(data, "recomp-user/GC/card.raw", "a memory card");
        Write(data, "recomp-game/prepared.json", "{}");
        Write(data, "recomp-game/GFZE01/sys/main.dol", "the game");
        Write(data, "recomp-game/preparing.lock", "");
        Write(data, "recomp-game/GFZE01.preparing/sys/main.dol", "half a game");
        Write(data, "notices.json", "[]");
        Write(data, "launcher.log", "a log");
        Write(data, "cache/courses/x.png", "a still");
        Write(data, "boards/board-1", "a board");
        return copy;
    }

    [Fact]
    public void TheCopysFolderOrItsDataFolderNameTheData()
    {
        var copy = OlderCopy();
        var data = Path.Combine(copy, Install.DataName);
        Assert.Equal(data, DataMove.DataOf(copy));
        Assert.Equal(data, DataMove.DataOf(data + Path.DirectorySeparatorChar));
        Assert.Null(DataMove.DataOf(Path.Combine(copy, "recomp")));            // no settings there
    }

    [Fact]
    public void WhatIsThePlayersIsCopiedAndCheckedAndTheRestIsLeft()
    {
        var copy = OlderCopy();
        var from = Path.Combine(copy, Install.DataName);
        var to = Path.Combine(_dir, "fzgx-launcher");
        Write(to, "settings.json", "{\"playerKey\":\"new\"}");                // the installed launcher's first start
        Write(to, "replays/race 1.fzrp", "another replay of that name");      // kept as it is
        Write(to, "notices.json", "[]");                                        // the same: left
        var before = Directory.GetFiles(copy, "*", SearchOption.AllDirectories).Select(f => (f, File.ReadAllText(f))).ToList();

        var seen = new List<DataMoveProgress>();
        var result = DataMove.Copy(from, to, new Recorded(seen), TestContext.Current.CancellationToken);

        Assert.True(result.Done, result.Message);
        Assert.Equal((4, 1, 1, 0), (result.Copied, result.Same, result.Kept, result.Links));
        Assert.Equal("{\"playerKey\":\"old\"}", File.ReadAllText(Path.Combine(to, "settings.json")));
        Assert.Equal("{\"playerKey\":\"new\"}", File.ReadAllText(result.SettingsBackup!));
        Assert.Equal("another replay of that name", File.ReadAllText(Path.Combine(to, "replays", "race 1.fzrp")));
        Assert.Equal("a memory card", File.ReadAllText(Path.Combine(to, "recomp-user", "GC", "card.raw")));
        Assert.Equal("the game", File.ReadAllText(Path.Combine(to, "recomp-game", "GFZE01", "sys", "main.dol")));
        Assert.True(File.Exists(Path.Combine(to, "recomp-game", "prepared.json")));
        // a preparation's lock and unfinished copy, the log, the caches, the boards and the program are not taken
        foreach (var left in new[] { "recomp-game/preparing.lock", "recomp-game/GFZE01.preparing", "launcher.log", "cache", "boards", "recomp" })
        {
            Assert.False(Path.Exists(Path.Combine(to, left)), left);
        }
        Assert.Empty(Directory.GetFiles(to, "*.copying", SearchOption.AllDirectories));
        // the settings came last, and the older copy is as it was
        Assert.Equal(6, seen.Count);
        Assert.Equal(seen[^1].BytesTotal, seen[^1].BytesDone);
        Assert.Equal(before, Directory.GetFiles(copy, "*", SearchOption.AllDirectories).Select(f => (f, File.ReadAllText(f))).ToList());
        Assert.Contains("still there", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkIsNeverFollowed()
    {
        var copy = OlderCopy();
        var from = Path.Combine(copy, Install.DataName);
        var disc = Path.Combine(_dir, "extracted");
        Write(disc, "sys/main.dol", "the disc's own");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(from, "recomp-game", "GFZE01-linked"), disc);
            Directory.CreateSymbolicLink(Path.Combine(from, DataFolders.ReplaysName + "-unused"), disc);   // not taken anyway
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("this system makes no links for a test: " + e.Message);
        }
        var to = Path.Combine(_dir, "data here");
        var result = DataMove.Copy(from, to, cancel: TestContext.Current.CancellationToken);
        Assert.True(result.Done, result.Message);
        Assert.Equal(1, result.Links);
        Assert.False(Path.Exists(Path.Combine(to, "recomp-game", "GFZE01-linked")));
        Assert.Contains("1 link left out", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThisLaunchersOwnFolderOrAFolderWithoutSettingsIsRefused()
    {
        var to = Path.Combine(_dir, "fzgx-launcher");
        Write(to, "settings.json", "{}");
        Assert.Equal(DataMove.SameFolderText, DataMove.Copy(to, to + Path.DirectorySeparatorChar, cancel: TestContext.Current.CancellationToken).Message);
        var empty = Path.Combine(_dir, "nothing");
        Directory.CreateDirectory(empty);
        var refused = DataMove.Copy(empty, to, cancel: TestContext.Current.CancellationToken);
        Assert.Equal((false, DataMove.NoDataText), (refused.Done, refused.Message));
    }

    /// <summary>Progress as it is reported, on the reporting thread (not <see cref="Progress{T}"/>, which posts).</summary>
    private sealed class Recorded(List<DataMoveProgress> into) : IProgress<DataMoveProgress>
    {
        public void Report(DataMoveProgress value) => into.Add(value);
    }
}
