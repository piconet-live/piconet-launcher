using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Launcher.Core;

/// <summary>
/// Finds where the window's thread stops answering, off unless it is asked for (<see cref="EnvironmentName"/>): a
/// thread of its own posts a beat to the window's thread at the highest priority, so the beat's wait is how long the
/// job that was running held the thread. A wait over the limit is one line: how long it was, what the launcher said
/// it was doing (<see cref="Doing"/>, the jobs of the view models and the tabs say) and the toolkit's own long
/// activities in that time (a layout, a click's handlers, styles). It cannot see the thread's stack; the line names
/// the job, not the line of code.
/// </summary>
public sealed class StallWatch : IDisposable
{
    /// <summary>Set to 1: the launcher writes <see cref="LogFileName"/> in its data folder. Set to 2: the lines also
    /// name the toolkit's work (<see cref="AskToolkit"/>).</summary>
    public const string EnvironmentName = "PICONET_STALL_LOG";

    public const string LogFileName = "ui-stalls.log";

    /// <summary>The longest wait that is no stall, in milliseconds.</summary>
    public const double DefaultLimitMs = 8;

    /// <summary>The toolkit's source of activities (layout, styles, routed events).</summary>
    private const string ToolkitSource = "Avalonia.Diagnostic.Source";

    /// <summary>A toolkit activity shorter than this is left out of a line.</summary>
    private const double ActivityFloorMs = 2;

    private static volatile string _doing = "";
    private static StallWatch? _running;

    private readonly Action<Action> _post;
    private readonly Action<string> _write;
    private readonly double _limitMs;
    private readonly ManualResetEventSlim _beat = new();
    private readonly Thread _thread;
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<(long Ended, double Ms, string What)> _activities = new();
    private readonly Lock _gate = new();
    private readonly List<StallSeen> _seen = [];
    private volatile bool _over;

    /// <param name="post">Puts a job on the window's thread, ahead of everything else waiting there.</param>
    /// <param name="write">Takes each stall's line; on the watch's own thread.</param>
    public StallWatch(Action<Action> post, Action<string> write, double limitMs = DefaultLimitMs)
    {
        _post = post;
        _write = write;
        _limitMs = limitMs;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ToolkitSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = Stopped,
        };
        ActivitySource.AddActivityListener(_listener);
        _running = this;
        FineTimers(on: true);
        _thread = new Thread(Watch) { IsBackground = true, Name = "stall watch", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Before the toolkit starts: its activities are on (they name layouts and handlers in the lines, and
    /// make layouts two to four times slower while they do: for finding what a stall is, not how long).</summary>
    public static void AskToolkit() => AppContext.SetSwitch("Avalonia.Diagnostics.Diagnostic.IsEnabled", true);

    /// <summary>Whether a watch runs (what is said to <see cref="Doing"/> costs nothing otherwise).</summary>
    public static bool On => _running is not null;

    /// <summary>The window's thread says what it starts on; the words stand until the next.</summary>
    public static void Doing(string what)
    {
        if (On)
        {
            _doing = what;
        }
    }

    /// <summary>Every stall so far (a test's walk reads them).</summary>
    public IReadOnlyList<StallSeen> Seen
    {
        get
        {
            lock (_gate)
            {
                return [.. _seen];
            }
        }
    }

    /// <summary>The stalls so far taken away, for a test's walk to read them step by step.</summary>
    public IReadOnlyList<StallSeen> Take()
    {
        lock (_gate)
        {
            var all = _seen.ToList();
            _seen.Clear();
            return all;
        }
    }

    public void Dispose()
    {
        _over = true;
        _beat.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _listener.Dispose();
        FineTimers(on: false);
        _running = null;
    }

    private void Stopped(Activity activity)
    {
        var ms = activity.Duration.TotalMilliseconds;
        if (ms >= ActivityFloorMs)
        {
            _activities.Enqueue((Stopwatch.GetTimestamp(), ms, $"{activity.OperationName.Replace("Avalonia.", "", StringComparison.Ordinal)} {Tags(activity)}".TrimEnd()));
        }
    }

    private static string Tags(Activity activity) => string.Join(' ', activity.TagObjects.Select(t => t.Value?.ToString()).Where(v => v is { Length: > 0 and < 60 }));

    private void Watch()
    {
        while (!_over)
        {
            _beat.Reset();
            var asked = Stopwatch.GetTimestamp();
            var doing = _doing;
            _post(_beat.Set);
            _beat.Wait();
            if (_over)
            {
                return;
            }
            var ms = Stopwatch.GetElapsedTime(asked).TotalMilliseconds;
            if (ms > _limitMs)
            {
                Tell(ms, asked, doing);
            }
            // (with the fine timers about 1 ms: the beats are close enough to see 8 ms; the thread is not kept busy)
            Thread.Sleep(1);
        }
    }

    private void Tell(double ms, long asked, string doing)
    {
        var inIt = new List<(double Ms, string What)>();
        while (_activities.TryDequeue(out var a))
        {
            if (a.Ended >= asked)
            {
                inIt.Add((a.Ms, a.What));
            }
        }
        var now = _doing;
        var what = doing == now || now.Length == 0 ? doing : doing.Length == 0 ? now : $"{doing} > {now}";
        var seen = new StallSeen(ms, what, [.. inIt.OrderByDescending(a => a.Ms).Take(4).Select(a => a.What + string.Create(CultureInfo.InvariantCulture, $" {a.Ms:F0} ms"))]);
        lock (_gate)
        {
            _seen.Add(seen);
        }
        _write(seen.ToString());
    }

    // Windows's sleeps last about 15 ms unless a program asks for finer ones: asked for while the watch runs.
    private static void FineTimers(bool on)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            _ = on ? NativeMethods.timeBeginPeriod(1) : NativeMethods.timeEndPeriod(1);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // the watch sees less then
        }
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint timeEndPeriod(uint period);
    }
}

/// <summary>One time the window's thread did not answer: how long, and what it was on.</summary>
public sealed record StallSeen(double Ms, string Doing, IReadOnlyList<string> Toolkit)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Ms,6:F1} ms  {(Doing.Length == 0 ? "?" : Doing)}{(Toolkit.Count == 0 ? "" : "  [" + string.Join("; ", Toolkit) + "]")}");
}
