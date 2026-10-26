using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Launcher.Core;

/// <summary>
/// The game's own program started as the controller helper (<c>moderngekko-run.exe --controllers USER_FOLDER</c>): no
/// game, only the game's input layer on the game's settings, talking in lines of JSON. The Controller page and the
/// check before a start ask it which devices are plugged in, what the GameCube adapter says, which input the player
/// just moved, and what the game would read from a port; so a name or a reading on the page is the one a race has.
/// It ends when its input closes, and no helper of this launcher is left running when a game starts
/// (<see cref="StopAll"/>): the game must find every device free.
/// </summary>
public sealed class ControllerHelper : IDisposable
{
    private static readonly List<ControllerHelper> Running = [];

    /// <summary>Held while a helper's program is started and counted among the running, and while the running are
    /// counted to be ended: a start in flight is either seen by <see cref="StopAll"/> or has not begun.</summary>
    private static readonly Lock Starting = new();

    /// <summary>The games of this launcher that are starting or running (<see cref="HoldForGame"/>).</summary>
    private static readonly List<GameHold> Holds = [];

    /// <summary>How many lines may wait to be written; one more is dropped (a helper that takes nothing in).</summary>
    private const int MostWaiting = 256;

    private readonly TextWriter _to;
    private readonly TextReader _from;
    private readonly Process? _process;
    private readonly Lock _gate = new();
    private readonly Lock _ending = new();   // held while the helper is ended, until its program has gone
    private readonly Channel<(string? Line, TaskCompletionSource? Mark)> _out = Channel.CreateUnbounded<(string?, TaskCompletionSource?)>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writing;
    private int _waiting;
    private bool _listening;
    private bool _closed;
    private bool _ended;
    private int _captures;

    /// <summary>A helper on any pair of pipes (tests give it a double's).</summary>
    public ControllerHelper(TextWriter to, TextReader from, Process? process = null)
    {
        _to = to;
        _from = from;
        _process = process;
        lock (Running)
        {
            Running.Add(this);
        }
        _writing = Task.Run(WriteAsync);
    }

    /// <summary>A game of this launcher is starting or running: no helper is started (<see cref="Start"/> gives
    /// none), so that the two never contend for a device.</summary>
    public static bool Held
    {
        get
        {
            lock (Holds)
            {
                Holds.RemoveAll(h => h.Over);
                return Holds.Count > 0;
            }
        }
    }

    /// <summary>Some helper of this launcher is running, or ending and not yet gone.</summary>
    public static bool AnyRunning
    {
        get
        {
            lock (Running)
            {
                return Running.Count > 0;
            }
        }
    }

    /// <summary>Something it said, checked (<see cref="ControllerMessages"/>); on the reading thread.</summary>
    public event Action<ControllerMessage>? Received;

    /// <summary>It said its last: it ended, or was told to.</summary>
    public event Action? Ended;

    public static IReadOnlyList<string> Arguments(string userFolder) => ["--controllers", userFolder];

    /// <summary>Starts the helper on the game's user folder; null when the runner is not there or did not start, and
    /// while a game of this launcher is starting or running (<see cref="Held"/>).</summary>
    public static ControllerHelper? Start(RecompPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var info = RecompLaunch.StartInfo(paths, Arguments(paths.UserFolder));
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);   // no mark before the first line
        lock (Starting)
        {
            if (Held)
            {
                return null;
            }
            try
            {
                return Process.Start(info) is { } process ? new ControllerHelper(process.StandardInput, process.StandardOutput, process) : null;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                return null;
            }
        }
    }

    /// <summary>For a game's start: from here on no helper is started, and every helper there is, one whose start
    /// was in flight too, is ended before this comes back. The hold lasts until it is put away, or, once it is given
    /// the game (<see cref="GameHold.Until"/>), until the game has gone.</summary>
    public static GameHold HoldForGame()
    {
        var hold = new GameHold();
        lock (Starting)
        {
            lock (Holds)
            {
                Holds.Add(hold);
            }
        }
        StopAll();
        return hold;
    }

    /// <summary>Every line asked for so far has been written to the helper, or given up (tests wait on it).</summary>
    public Task SentAsync()
    {
        var mark = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_out.Writer.TryWrite((null, mark)))
        {
            mark.TrySetResult();
        }
        return mark.Task;
    }

    /// <summary>Starts taking its lines in (after the handlers are set, so that none is missed).</summary>
    public void Listen()
    {
        lock (_gate)
        {
            if (_listening || _closed)
            {
                return;
            }
            _listening = true;
        }
        _ = Task.Run(ReadAsync);
    }

    /// <summary>The settings on disk changed: read them again.</summary>
    public void Reload() => Send("{\"do\":\"reload\"}");

    /// <summary>Look for the GameCube adapter, as a game start does when a port is set to it. Until this is sent
    /// the helper leaves the adapter alone.</summary>
    public void AskAdapter() => Send("{\"do\":\"adapter\"}");

    /// <summary>Say one port's GameCube controller as it moves; null: none.</summary>
    public void Watch(int? port) =>
        Send(string.Create(CultureInfo.InvariantCulture, $"{{\"do\":\"watch\",\"port\":{port ?? -1}}}"));

    /// <summary>Listen on one device for the next input the player moves, for so long; null: stop listening.</summary>
    /// <returns>This listening's number, which its answer carries (<see cref="HelperCaptured.Id"/>).</returns>
    public int Capture(string? device, int milliseconds = 5000)
    {
        var id = Interlocked.Increment(ref _captures);
        Send(string.Create(CultureInfo.InvariantCulture,
            $"{{\"do\":\"capture\",\"id\":{id},\"device\":{JsonSerializer.Serialize(device ?? "", AsItIs)},\"ms\":{milliseconds}}}"));
        return id;
    }

    /// <summary>A device's name goes to the helper in its own letters (UTF-8), with only what JSON must escape.</summary>
    private static readonly JsonSerializerOptions AsItIs = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Run a port's motor for a moment.</summary>
    public void Rumble(int port, int milliseconds = 300) =>
        Send(string.Create(CultureInfo.InvariantCulture, $"{{\"do\":\"rumble\",\"port\":{port},\"ms\":{milliseconds}}}"));

    /// <summary>Ends the helper: its input is closed, which it takes as the word to go; one that has not gone two
    /// seconds later is ended (this launcher's own child, by its handle). Whoever calls comes back only when the
    /// program has gone: a second caller waits for the first (the page ends its helper off the window's thread, and
    /// a game start at that moment, <see cref="StopAll"/>, must not find the helper still holding its devices).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;   // nothing more is sent
        }
        _out.Writer.TryComplete();
        lock (_ending)
        {
            if (_ended)
            {
                return;
            }
            _ended = true;
            try
            {
                // What was asked for is written first, and then the input is closed; neither is waited for longer
                // than a moment: a helper that takes nothing in (its pipe is full) holds the writer in its write,
                // and is ended below with its program.
                _writing.Wait(TimeSpan.FromMilliseconds(500));
                Task.Run(_to.Dispose).Wait(TimeSpan.FromSeconds(1));
                if (_process is { } process)
                {
                    if (!process.WaitForExit(TimeSpan.FromSeconds(2)))
                    {
                        process.Kill();
                        process.WaitForExit(TimeSpan.FromSeconds(2));
                    }
                    process.Dispose();
                }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException
                                          or AggregateException)
            {
                // it had already gone
            }
            finally
            {
                // it is one of the running until it has gone, so that StopAll waits for it here too
                lock (Running)
                {
                    Running.Remove(this);
                }
            }
        }
    }

    /// <summary>Ends every helper of this launcher: before a game starts, and when the launcher closes.</summary>
    public static void StopAll()
    {
        ControllerHelper[] running;
        lock (Starting)
        {
            lock (Running)
            {
                running = [.. Running];
            }
        }
        foreach (var helper in running)
        {
            helper.Dispose();
        }
    }

    /// <summary>A line for the helper. It is written by a task of its own, in order: whoever asks (the window's
    /// thread) never waits in a write, and a game start (<see cref="StopAll"/>) never waits for one.</summary>
    private void Send(string line)
    {
        lock (_gate)
        {
            if (_closed || _waiting >= MostWaiting)
            {
                return;
            }
            _waiting++;
        }
        _out.Writer.TryWrite((line, null));
    }

    private async Task WriteAsync()
    {
        await foreach (var (line, mark) in _out.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (line is null)
            {
                mark?.TrySetResult();
                continue;
            }
            try
            {
                _to.WriteLine(line);
                _to.Flush();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // it has ended: its reader says so
            }
            lock (_gate)
            {
                _waiting--;
            }
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            while (await NextLineAsync().ConfigureAwait(false) is { } line)
            {
                if (ControllerMessages.Checked(line) is { } message)
                {
                    Received?.Invoke(message);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // the pipe went with the helper
        }
        Ended?.Invoke();
    }

    /// <summary>One line within <see cref="ControllerMessages.MaxLineLength"/> (a longer one is read to its end and
    /// given as too long a text, which is no message); null at the end.</summary>
    private async Task<string?> NextLineAsync()
    {
        var line = new StringBuilder();
        var one = new char[1];
        var any = false;
        while (await _from.ReadAsync(one.AsMemory()).ConfigureAwait(false) == 1)
        {
            any = true;
            if (one[0] == '\n')
            {
                return line.ToString();
            }
            if (line.Length <= ControllerMessages.MaxLineLength && one[0] != '\r')
            {
                line.Append(one[0]);
            }
        }
        return any ? line.ToString() : null;
    }
}

/// <summary>A game of this launcher that is starting or running, while which no controller helper is started
/// (<see cref="ControllerHelper.HoldForGame"/>).</summary>
public sealed class GameHold : IDisposable
{
    private Process? _game;
    private volatile bool _away;

    /// <summary>The hold is over: it was put away, or its game has gone.</summary>
    public bool Over
    {
        get
        {
            try
            {
                return _away || _game is { HasExited: true };
            }
            catch (InvalidOperationException)
            {
                return true;   // the game's process was put away: nobody watches it any more
            }
        }
    }

    /// <summary>The game started: the hold lasts until it has gone.</summary>
    public void Until(Process game) => _game = game;

    /// <summary>The start came to nothing: the hold is over.</summary>
    public void Dispose() => _away = true;
}
