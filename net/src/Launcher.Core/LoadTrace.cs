using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Launcher.Core;

/// <summary>
/// Where the time of loading a machine goes, off unless
/// a launch file's <c>"loadTrace"</c> is on (<see cref="DebugSettings.LoadTrace"/>): each load (a machine's model, its
/// paint, a custom machine) is a <see cref="Job"/> whose stages (the disc's lock waited for, the disc read, the LZ
/// unpacking, the files' parsing, the textures' decoding, the packing) are timed and written as one line when it ends;
/// the window's own work (putting a model on the card, a still drawn, a machine's first frame) is a line of its own
/// (<see cref="Note"/>). Every line starts with the milliseconds since the launcher's process started.
/// </summary>
public static class LoadTrace
{
    private static readonly AsyncLocal<Job?> Current = new();
    private static readonly long Started = StartOfProcess();
    private static Action<string>? _sink;

    /// <summary>Whether the lines are written.</summary>
    public static bool On => _sink is not null;

    /// <summary>Where the lines go (null: off), set once at the start.</summary>
    public static void Use(Action<string>? sink) => _sink = sink;

    /// <summary>Milliseconds since the process started.</summary>
    public static double Now => Stopwatch.GetElapsedTime(Started).TotalMilliseconds;

    /// <summary>A free line (when on).</summary>
    public static void Note(string line)
    {
        if (_sink is { } sink)
        {
            sink(string.Create(CultureInfo.InvariantCulture, $"+{Now:F0} {line}"));
        }
    }

    /// <summary>Starts a load on this thread and what it awaits: its stages are counted to it until it is disposed,
    /// which writes its line. Null when off.</summary>
    public static Job? Begin(string what)
    {
        if (!On)
        {
            return null;
        }
        var job = new Job(what, Current.Value);
        Current.Value = job;
        return job;
    }

    /// <summary>Times a stage of the current load (disposed when it ends); nothing when off or outside a load.</summary>
    public static Timed Stage(string stage) => Current.Value is { } job ? new Timed(job, stage, Stopwatch.GetTimestamp()) : default;

    /// <summary>A stage being timed.</summary>
    public readonly struct Timed(Job? job, string? stage, long start) : IDisposable
    {
        public void Dispose() => job?.Add(stage!, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>A load: its stages' counts and times, written as one line when disposed.</summary>
    public sealed class Job : IDisposable
    {
        private readonly ConcurrentDictionary<string, (int Count, double Ms)> _stages = new(StringComparer.Ordinal);
        private readonly string _what;
        private readonly Job? _outer;
        private readonly long _start = Stopwatch.GetTimestamp();

        internal Job(string what, Job? outer) => (_what, _outer) = (what, outer);

        internal void Add(string stage, double ms) => _stages.AddOrUpdate(stage, (1, ms), (_, s) => (s.Count + 1, s.Ms + ms));

        public void Dispose()
        {
            Current.Value = _outer;
            var line = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{_what}: {Stopwatch.GetElapsedTime(_start).TotalMilliseconds:F1} ms");
            foreach (var (stage, (count, ms)) in _stages.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                line.Append(CultureInfo.InvariantCulture, $"; {stage} {count}x {ms:F1}");
            }
            Note(line.ToString());
        }
    }

    private static long StartOfProcess()
    {
        try
        {
            var since = DateTime.Now - Process.GetCurrentProcess().StartTime;
            return Stopwatch.GetTimestamp() - (long)(since.TotalSeconds * Stopwatch.Frequency);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return Stopwatch.GetTimestamp();
        }
    }
}
