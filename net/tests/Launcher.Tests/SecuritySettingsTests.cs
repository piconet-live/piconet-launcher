using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The settings file is on the player's disk and every page reads it when the launcher opens: whatever is
/// in it, loading gives settings the pages can read (the defaults where the file gives none), does not throw, and
/// says once that it had to.</summary>
public sealed partial class SecuritySettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SettingsFile => Path.Combine(_dir, "settings.json");

    private SettingsStore Stored(string text)
    {
        File.WriteAllText(SettingsFile, text);
        return new SettingsStore(SettingsFile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"settings\"")]
    [InlineData("{\"racer\": 5}")]
    [InlineData("{\"racer\": {\"name\": \"A\", \"machine\": \"six\"}}")]
    [InlineData("{\"racer\": {\"name\": \"A\", \"machine\": 99999999999999999999}}")]
    [InlineData("{\"racer\": {\"name\": \"A\", \"machine\": 1, \"color\": {\"r\": 256, \"g\": 0, \"b\": 0}}}")]
    [InlineData("{\"racer\": {\"name\": [\"A\"]}}")]
    [InlineData("{\"recordReplays\": \"yes\"}")]
    [InlineData("\u0000\u0001\u0002 not text at all")]
    public void AFileThatHoldsNoSettingsGivesTheDefaultsAndSaysSo(string text)
    {
        var store = Stored(text);

        // the defaults, but for the server: none, where the defaults' own is the official one (NoServerTests)
        var loaded = store.Load();
        Assert.Equal(new LauncherSettings { NoServer = true }, loaded);

        Assert.Equal(SettingsRead.Unreadable, store.LastRead);
        Assert.Equal(SettingsStore.UnreadableNotice, store.Notice);
        // what was there is kept beside it, and the launcher's own settings go over it
        Assert.Equal(text, File.ReadAllText(store.DamagedPath));
        Assert.True(store.TrySave(loaded));
        // said once: the next start reads the launcher's own file, and still has no server
        var next = new SettingsStore(SettingsFile);
        Assert.Equal(new LauncherSettings { NoServer = true }, next.Load());
        Assert.Equal(SettingsRead.Fine, next.LastRead);
        Assert.Null(next.Notice);
    }

    [Fact]
    public void AFileNestedDeeperThanSettingsAreIsNotRead()
    {
        var store = Stored(new string('[', 5000) + new string(']', 5000));

        Assert.Equal(new LauncherSettings { NoServer = true }, store.Load());
        Assert.Equal(SettingsRead.Unreadable, store.LastRead);

        store = Stored("{\"racer\":" + string.Concat(Enumerable.Repeat("{\"looks\":[", 3000)) + "}");
        Assert.Equal(new LauncherSettings { NoServer = true }, store.Load());
        Assert.Equal(SettingsRead.Unreadable, store.LastRead);
    }

    [Fact]
    public void AFileLongerThanSettingsAreIsNotRead()
    {
        // sound JSON, and a megabyte of it
        var store = Stored("{\"discPath\": \"" + new string('a', (int)SettingsStore.MostBytes) + "\"}");

        Assert.Equal(new LauncherSettings { NoServer = true }, store.Load());
        Assert.Equal(SettingsRead.Unreadable, store.LastRead);
    }

    private static readonly LauncherSettings Mine = new()
    {
        Racer = RacerProfile.Default with { Name = "ANA", Machine = 7 }, DiscPath = "C:/games/gx.iso", Server = "ws://10.0.0.5:47810/ws",
    };

    /// <summary>The review's case: another program holds the settings file while the launcher starts. The file is
    /// whole; read as "unreadable" it was written over with the defaults at that same start (every start saves),
    /// with no copy kept: the name, the disc and the server were gone for good. Now the launcher runs on its
    /// defaults with no server for this run, says so, and leaves the file alone.</summary>
    [Fact]
    public void AFileThatCannotBeOpenedIsLeftAsItIs()
    {
        new SettingsStore(SettingsFile).Save(Mine);
        var stood = File.ReadAllBytes(SettingsFile);
        var store = new SettingsStore(SettingsFile);

        LauncherSettings loaded;
        using (new FileStream(SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            loaded = store.Load();
        }

        Assert.Equal(new LauncherSettings { NoServer = true }, loaded);
        Assert.Equal(SettingsRead.Unreadable, store.LastRead);
        Assert.Equal(SettingsStore.NotOpenedNotice, store.Notice);
        Assert.True(store.LeftAlone);
        Assert.Null(store.KeptAt);
        Assert.False(File.Exists(store.DamagedPath));                        // nothing says it is damaged
        // the save every start makes (AppState.Load), and any later one of this run
        Assert.False(store.TrySave(loaded with { UserFolder = "x" }));
        Assert.Throws<IOException>(() => store.Save(loaded));
        Assert.Equal(stood, File.ReadAllBytes(SettingsFile));
        // the next start has everything the player had set
        var next = new SettingsStore(SettingsFile);
        Assert.Equal(Mine, next.Load());
        Assert.Equal(SettingsRead.Fine, next.LastRead);
        Assert.False(next.LeftAlone);
        Assert.True(next.TrySave(Mine));
    }

    /// <summary>A file held for a moment (a scanner, a backup) is read once it is let go.</summary>
    [Fact]
    public async Task AFileHeldForAMomentIsReadWhenItIsLetGo()
    {
        new SettingsStore(SettingsFile).Save(Mine);
        var store = new SettingsStore(SettingsFile);
        var held = new FileStream(SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var letGo = Task.Run(async () =>
        {
            await Task.Delay(SettingsStore.OpenWait * 1.5);
            await held.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        var loaded = store.Load();
        await letGo;

        Assert.Equal(Mine, loaded);
        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Null(store.Notice);
        Assert.False(store.LeftAlone);
    }

    /// <summary>The copy of a file that was not read as it stood is the player's: a second such file does not go
    /// over the first one's copy.</summary>
    [Fact]
    public void ASecondDamagedFileDoesNotGoOverTheCopyOfTheFirst()
    {
        const string first = "{ \"racer\": {\"name\": \"ANA\"}, first, not json";
        const string second = "{ second, not json";
        var store = Stored(first);
        Assert.True(store.TrySave(store.Load()));
        Assert.Equal(store.DamagedPath, store.KeptAt);

        store = Stored(second);
        store.Load();

        Assert.Equal(first, File.ReadAllText(store.DamagedPath));
        Assert.Equal(store.DamagedPath + ".2", store.KeptAt);
        Assert.Equal(second, File.ReadAllText(store.KeptAt!));
        Assert.False(store.LeftAlone);
        // a file with unsound parts is kept the same way
        store = Stored("{\"racer\": {\"name\": \"ANA\", \"machine\": 99}}");
        store.Load();
        Assert.Equal(SettingsRead.Repaired, store.LastRead);
        Assert.Equal(store.DamagedPath + ".3", store.KeptAt);
        Assert.Equal(first, File.ReadAllText(store.DamagedPath));
    }

    /// <summary>No copy, no writing over: with every place for a copy taken, the file that was not read stays as it
    /// is, and the launcher says that its changes last for this run.</summary>
    [Theory]
    [InlineData("{ not json", SettingsRead.Unreadable)]
    [InlineData("{\"racer\": {\"name\": \"ANA\", \"machine\": 99}}", SettingsRead.Repaired)]
    public void AFileThatWasNotReadAsItStoodIsWrittenOverOnlyOnceACopyIsKept(string text, SettingsRead read)
    {
        var store = Stored(text);
        for (var n = 1; n <= SettingsStore.MostDamaged; n++)
        {
            Directory.CreateDirectory(n == 1 ? store.DamagedPath : $"{store.DamagedPath}.{n}");   // a place that takes no copy
        }

        var loaded = store.Load();

        Assert.Equal(read, store.LastRead);
        Assert.True(store.LeftAlone);
        Assert.Null(store.KeptAt);
        Assert.True(store.CopiesFull);
        Assert.Equal(store.CopiesFullNotice, store.Notice);
        Assert.False(store.TrySave(loaded));
        Assert.Equal(text, File.ReadAllText(SettingsFile));
    }

    /// <summary>The second review's case: nine unreadable settings files are kept, and the tenth is left alone, at
    /// this start and at every later one: defaults, no server, nothing saved, and the notice said only that no
    /// copy could be kept. It says now that the copies are full, where they are, and that deleting old ones ends
    /// it; and that is what ends it. (The oldest copy is not dropped to make room: it is the likeliest to hold what
    /// the player had set.)</summary>
    [Fact]
    public void WithNineCopiesKeptTheNoticeSaysSoAndDeletingOldOnesEndsIt()
    {
        for (var n = 1; n <= SettingsStore.MostDamaged; n++)
        {
            var earlier = Stored($"{{ unreadable file {n}");
            Assert.True(earlier.TrySave(earlier.Load()));
            Assert.False(earlier.CopiesFull);
        }
        const string tenth = "{ unreadable file 10";
        for (var start = 0; start < 3; start++)                                // the same at every start
        {
            var store = start == 0 ? Stored(tenth) : new SettingsStore(SettingsFile);
            var loaded = store.Load();
            Assert.True(store.LeftAlone);
            Assert.True(store.CopiesFull);
            Assert.Null(loaded.QuickPlayServer);
            Assert.False(store.TrySave(loaded));
            Assert.Equal(tenth, File.ReadAllText(SettingsFile));
            Assert.Equal(_dir, store.Folder);
            var folder = Path.GetFileName(_dir);
            Assert.Equal($"Settings unreadable · 9 damaged copies kept, nothing saved · delete old .damaged files in {folder}", store.Notice);
        }
        Assert.Equal("{ unreadable file 1", File.ReadAllText(SettingsFile + ".damaged"));   // the first copy is still the first file

        // the player deletes two old copies: the next start keeps the tenth file and writes its own settings
        File.Delete(SettingsFile + ".damaged.4");
        File.Delete(SettingsFile + ".damaged.7");
        var next = new SettingsStore(SettingsFile);
        var settings = next.Load();
        Assert.False(next.LeftAlone);
        Assert.False(next.CopiesFull);
        Assert.Equal(SettingsFile + ".damaged.4", next.KeptAt);
        Assert.Equal(tenth, File.ReadAllText(next.KeptAt!));
        Assert.Equal(SettingsStore.UnreadableNotice, next.Notice);
        Assert.True(next.TrySave(settings with { Server = "ws://10.0.0.5:47810/ws", NoServer = false }));
        Assert.Equal("ws://10.0.0.5:47810/ws", new SettingsStore(SettingsFile).Load().QuickPlayServer);
    }
}
