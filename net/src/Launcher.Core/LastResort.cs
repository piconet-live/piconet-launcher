using System.Globalization;

namespace Launcher.Core;

/// <summary>
/// The handler of last resort: what the launcher does with an error that nothing caught. It writes the error to the
/// launcher's log file with the time and the build, and tells the player once, in plain words, that something went
/// wrong and where the log is. The launcher's start-up puts it on the routes such an error takes (Launcher.App's
/// Program and App: the window's thread, background tasks and threads, the process itself). Nothing in
/// Launcher.Core installs it: code under test meets its own errors.
/// </summary>
/// <param name="logFile">The launcher's log file. It is made when the first error is written, not before.</param>
/// <param name="build">The launcher's version and build as the player reads them (the sidebar's corner).</param>
/// <param name="clock">The time (tests give their own).</param>
public sealed class LastResort(string logFile, string build, Func<DateTimeOffset>? clock = null)
{
    /// <summary>The errors written in one run of the launcher: an error that comes back at every frame or every
    /// message must not fill the disk.</summary>
    public const int MostEntries = 50;

    /// <summary>The longest text written of one error (an error's message can carry what a server sent).</summary>
    public const int MostEntryLength = 16 * 1024;

    /// <summary>A log longer than this when a run first writes to it is set aside (one earlier log is kept beside
    /// it, ".old") and begun again.</summary>
    public const long MostLogBytes = 1 << 20;

    private static readonly object Mark = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Exception, object> _written = [];
    private readonly Lock _lock = new();
    private int _entries;
    private int _dealt;
    private bool _told;

    public string LogFile { get; } = logFile;

    /// <summary>The errors the handler is done with: written, and the player told where one is told. An error
    /// comes on a thread of its own (a background task's, a background thread's) and its notice follows its entry,
    /// so a log that is there does not say the handler is done; this does, for whoever waits for it.</summary>
    public int Dealt => Volatile.Read(ref _dealt);

    /// <summary>How the player is told (the window: as its other notices are shown; without a window: standard
    /// error). Null: nobody to tell yet; the first error after it is set tells.</summary>
    public Action<string>? Tell { get; set; }

    /// <summary>The folder the log is in, for the window to offer to open (a path is no reading for a notice).</summary>
    public string LogFolder => Path.GetDirectoryName(Path.GetFullPath(LogFile)) ?? ".";

    /// <summary>What the player is told, once in a run: what to do, and the log by its name. Where that is, the
    /// window offers to open (<see cref="LogFolder"/>) and the console is told in full (<see cref="LogFile"/>).</summary>
    public string Notice => $"Launcher error · {Path.GetFileName(LogFile)}";

    /// <summary>An error nothing caught: written to the log, and the player told if they have not been. It never
    /// raises an error of its own (a log that cannot be written is given up).</summary>
    /// <param name="where">Where it was caught, in a few words for the log's reader ("on the window's thread").</param>
    /// <param name="tell">False: only written (the launcher is closing, or the player is told otherwise).</param>
    /// <returns>True, for the routes that ask whether the error is dealt with: the launcher goes on.</returns>
    public bool Caught(string where, Exception? error, bool tell = true)
    {
        Action<string>? teller = null;
        lock (_lock)
        {
            if (error is not null && Written(error))
            {
                return true;
            }
            Write(where, error);
            if (error is not null)
            {
                _written.TryAdd(error, Mark);
            }
            if (tell && !_told && Tell is { } now)
            {
                _told = true;
                teller = now;
            }
        }
        if (teller is not null)
        {
            try
            {
                teller(Notice);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // the screens could not take the notice: said in the log, where this error's reader looks next
                lock (_lock)
                {
                    Write("while the player was told of the error above", e);
                }
            }
        }
        Interlocked.Increment(ref _dealt);
        return true;
    }

    /// <summary>Something the log's reader should know that is no error of the launcher's (a server's message
    /// refused): written as an entry of the run, the player not told.</summary>
    public void Note(string what)
    {
        lock (_lock)
        {
            Write(what, null, note: true);
        }
    }

    /// <summary>Whether this error was written already. One error can come by two routes: the window's thread
    /// keeps a job's error in the job's task, which nobody waits for, so .NET reports it again as a background
    /// task's (wrapped with any others of that task) when the task is collected.</summary>
    private bool Written(Exception error) =>
        _written.TryGetValue(error, out _) || (error is AggregateException { InnerExceptions: { Count: > 0 } inner } && inner.All(Written));

    private void Write(string where, Exception? error, bool note = false)
    {
        if (_entries > MostEntries)
        {
            return;
        }
        var first = _entries++ == 0;
        var head = string.Create(CultureInfo.InvariantCulture, $"==== {(clock?.Invoke() ?? DateTimeOffset.Now):yyyy-MM-dd HH:mm:ss zzz} · {build} · ");
        var text = _entries > MostEntries
            ? head + "more errors came in this run and are not written" + Environment.NewLine
            : note ? head + Cut(where) + Environment.NewLine
            : head + "not caught " + where + Environment.NewLine + Cut(error?.ToString() ?? "(no error was given)") + Environment.NewLine;
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(LogFile)) is { } folder)
            {
                Directory.CreateDirectory(folder);
            }
            if (first)
            {
                LogFiles.SetAsideWhenLong(LogFile, MostLogBytes);
            }
            LogFiles.Append(LogFile, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
                                      or System.Security.SecurityException)
        {
            // no log can be written (the folder is gone, the disk is full): the launcher goes on without one
        }
    }

    private static string Cut(string text) =>
        text.Length <= MostEntryLength ? text : text[..MostEntryLength] + Environment.NewLine + "(cut: the error's text is longer)";
}
