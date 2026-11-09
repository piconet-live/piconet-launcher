using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core;

/// <summary>
/// A replay file handed to the launcher window that is open already on the same data folder (a double-click on a
/// <c>.fzrp</c> while the launcher is open: <see cref="ReplayFileType"/>), so that it plays there and no second
/// window opens. The window listens (<see cref="Listen"/>) on a pipe named after its data folder
/// (<see cref="NameOf"/>), open to the player's own processes only; a start with a replay file sends it the file's
/// full path (<see cref="Send"/>) and ends when the window has taken it, or opens the window itself when no window
/// listens or none takes it.
/// <para>Whether a window listens is known at once, without waiting on the pipe: the listening window holds a named
/// mutex of the same name for as long as it listens (a pipe that is not there would be waited for until the time
/// limit).</para>
/// </summary>
public sealed class ReplayHandOver : IDisposable
{
    /// <summary>The longest path a window takes (a longer line is not read further).</summary>
    private const int LongestPath = 4096;

    /// <summary>A sender's answer: the window took the file.</summary>
    private const string Taken = "taken";

    private readonly string _name;
    private readonly Action<string> _received;
    private readonly Action<string>? _note;
    private readonly Mutex _listening;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private ReplayHandOver(string name, Action<string> received, Action<string>? note)
    {
        (_name, _received, _note) = (name, received, note);
        _listening = new Mutex(false, MutexName(name));
        _loop = Task.Run(LoopAsync);
    }

    /// <summary>The hand-over's name for the data folder <paramref name="dataFolder"/>: the same for every spelling
    /// of one folder (on Windows case does not count), another for another folder.</summary>
    public static string NameOf(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolder));
        if (OperatingSystem.IsWindows())
        {
            full = full.ToUpperInvariant();
        }
        return "piconet-open-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..24];
    }

    private static string MutexName(string name) => @"Local\" + name;

    /// <summary>Listens under <paramref name="name"/> until disposed: each file handed over goes to
    /// <paramref name="received"/>, on the hand-over's own thread, as a full path of a <c>.fzrp</c> file (anything
    /// else that comes is not passed on). <paramref name="note"/>: a line for the launcher's log when listening
    /// fails.</summary>
    public static ReplayHandOver Listen(string name, Action<string> received, Action<string>? note = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(received);
        return new ReplayHandOver(name, received, note);
    }

    /// <summary>Hands <paramref name="file"/> (its full path) to the window that listens under
    /// <paramref name="name"/>, waiting <paramref name="wait"/> at most for it to answer. True: the window took
    /// it; false: no window listens, or none answered.</summary>
    public static bool Send(string name, string file, TimeSpan wait)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (!Mutex.TryOpenExisting(MutexName(name), out var listening))
        {
            return false;
        }
        listening.Dispose();
        using var stop = new CancellationTokenSource(wait);
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.ConnectAsync(stop.Token).GetAwaiter().GetResult();
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine(Path.GetFullPath(file));
            return reader.ReadLineAsync(stop.Token).AsTask().GetAwaiter().GetResult() == Taken;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A file a sender says, taken when it is the full path of a replay file.</summary>
    public static bool Acceptable(string? line) =>
        line is { Length: > 0 and <= LongestPath } && Path.IsPathFullyQualified(line) &&
        line.EndsWith(ReplayFileType.Extension, StringComparison.OrdinalIgnoreCase);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                await TakeAsync(pipe).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is IOException or OperationCanceledException)
            {
                // a sender that went, or never said its line in time: the next one is listened for
            }
            catch (Exception e) when (e is UnauthorizedAccessException or PlatformNotSupportedException)
            {
                _note?.Invoke($"replay hand-over: not listening ({e.GetType().Name}: {e.Message})");
                return;
            }
        }
    }

    /// <summary>One sender's line read (a few seconds at most), passed on, and answered.</summary>
    private async Task TakeAsync(NamedPipeServerStream pipe)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        limit.CancelAfter(TimeSpan.FromSeconds(5));
        var buffer = new char[LongestPath + 2];
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var line = new StringBuilder();
        while (line.Length <= LongestPath)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, LongestPath + 1 - line.Length), limit.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            line.Append(buffer, 0, read);
            if (line.ToString().IndexOf('\n', StringComparison.Ordinal) is var end and >= 0)
            {
                line.Length = end;
                break;
            }
        }
        var file = line.ToString().TrimEnd('\r');
        if (!Acceptable(file))
        {
            return;
        }
        _received(file);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync(Taken.AsMemory(), limit.Token).ConfigureAwait(false);
        await writer.FlushAsync(limit.Token).ConfigureAwait(false);
    }

    private int _disposed;

    /// <summary>Listening stops (a second time does nothing).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // (its errors are its own: it has stopped)
        }
        _listening.Dispose();
        _stop.Dispose();
    }
}
