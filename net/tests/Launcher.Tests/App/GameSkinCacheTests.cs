using Launcher.App.Theme;

namespace Launcher.Tests;

/// <summary>The disc art's model caches are filled from worker threads as well as the window's (the preload, the
/// one-hop loads, the custom stills): asked from eight threads at once, for the same keys and different ones, nothing
/// throws (a log showed "concurrent update corrupted its state" under GameSkin.Custom3D) and every key
/// has one task, the same for every caller.</summary>
[Collection(HeadlessApp.Name)]
public sealed class GameSkinCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose()
    {
        GameSkin.Load(null);
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>The race page's question: never read on the asker's thread, the same read for two askers, and known after.</summary>
    [Fact]
    public async Task ACoursesModelIsReadOnAWorkerAndKept()
    {
        GameSkin.Load(new SyntheticDisc().Write(_dir, "gx.iso"));
        var read = GameSkin.CourseAsync(8);
        Assert.Same(read, GameSkin.CourseAsync(8));
        Assert.Null(await read);   // (the synthetic disc has no models)
        Assert.True(GameSkin.CourseAsync(8).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ManyThreadsAskingAtOnceGetOneTaskAKey()
    {
        GameSkin.Load(new SyntheticDisc().Write(_dir, "gx.iso"));
        const int threads = 8, rounds = 200;
        using var start = new Barrier(threads);
        var machines = new System.Collections.Concurrent.ConcurrentBag<(int Key, Task Task)>();
        var customs = new System.Collections.Concurrent.ConcurrentBag<(int Key, Task Task)>();
        var courses = new System.Collections.Concurrent.ConcurrentBag<(int Key, Task Task)>();
        var workers = Enumerable.Range(0, threads).Select(t => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait(TestContext.Current.CancellationToken);
            for (var i = 0; i < rounds; i++)
            {
                // the same keys on every thread, and some of each thread's own
                var key = (i % 4) + ((i % 3 == 0) ? 0 : t * 4);
                machines.Add((key, GameSkin.Machine3D(key % 41)));
                customs.Add((key, GameSkin.Custom3D(key % 25, 25 + (key % 25), 50 + (key % 25))));
                courses.Add((key, GameSkin.CourseLookAsync(key % 40)));
            }
        }, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        await Task.WhenAll(workers);
        foreach (var (name, bag) in new[] { ("Machine3D", machines), ("Custom3D", customs), ("CourseLookAsync", courses) })
        {
            Assert.Equal(threads * rounds, bag.Count);
            foreach (var key in bag.GroupBy(e => e.Key))
            {
                Assert.True(key.Select(e => e.Task).Distinct().Count() == 1, $"{name}: key {key.Key} has more than one task");
            }
        }
        // (and what they load finishes without an exception: the synthetic disc has no models, so null)
        await Task.WhenAll(machines.Select(e => e.Task).Concat(customs.Select(e => e.Task)).Concat(courses.Select(e => e.Task)).Distinct());
    }
}
