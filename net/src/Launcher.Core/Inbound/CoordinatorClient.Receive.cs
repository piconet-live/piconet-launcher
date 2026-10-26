using System.Net.WebSockets;
using System.Text;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>The connection's receiving side: every message of the coordinator is read here, whole and within its
/// length, and checked (<see cref="ServerMessages"/>) before anything of it is kept or passed on.</summary>
/// <remarks>The messages are taken in their order, one at a time; a relay measurement takes its seconds, and what
/// comes meanwhile waits for its answer, as it always did. One message does not wait: the server's ping, which it
/// times (each player's ping in the lobby), is answered the moment it is read, also while a measurement runs.</remarks>
public sealed partial class CoordinatorClient
{
    /// <summary>What was read: a message (null: one that cannot be used), or how the reading ended.</summary>
    private readonly record struct Read(ServerMessage? Message, string? End = null, bool ServerClosed = false);

    private async Task ReceiveLoopAsync(CancellationToken stop)
    {
        var reason = "closed";
        Task<Read>? reading = null;
        (string MatchId, Task<IReadOnlyList<RelayRtt>> Result)? measuring = null;
        try
        {
            await _listened.Task.WaitAsync(stop).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            var held = new Queue<Read>();   // what came while a measurement ran, in its order
            var ended = false;              // the reading's end is held: nothing more is read
            while (!stop.IsCancellationRequested)
            {
                Read next;
                if (measuring is null && held.Count > 0)
                {
                    next = held.Dequeue();
                }
                else
                {
                    reading ??= ended ? null : ReadAsync(buffer, stop);
                    if (measuring is { } m && (reading is null ||
                        await Task.WhenAny(reading, m.Result).ConfigureAwait(false) == m.Result))
                    {
                        await SendAsync(new RelayLatency(m.MatchId, await m.Result.ConfigureAwait(false)), stop).ConfigureAwait(false);
                        measuring = null;
                        continue;
                    }
                    next = await reading!.ConfigureAwait(false);
                    reading = null;
                    if (next.Message is LinkPing ping)
                    {
                        // the server times this answer: at once, whatever else is in hand, and nothing else
                        await SendAsync(new LinkPong(ping.Id), stop).ConfigureAwait(false);
                        continue;
                    }
                    if (measuring is not null)
                    {
                        ended = next.End is not null;
                        held.Enqueue(next);
                        continue;
                    }
                }
                if (next.End is not null)
                {
                    if (next.ServerClosed)
                    {
                        // the server closed its side (after its last word, when it ended the connection for a
                        // reason): answered, so that it has nothing to wait for
                        await AnswerCloseAsync().ConfigureAwait(false);
                    }
                    reason = next.End;
                    return;
                }
                if (next.Message is MeasureRelays measure)
                {
                    measuring = (measure.MatchId, RelayMeasure.MeasureAsync(measure.Relays, RelayHost, stop));
                }
                else if (next.Message is not null)
                {
                    _received?.Invoke(next.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = Stopped;
        }
        catch (WebSocketException e)
        {
            reason = e.Message;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // anything else raised while a message was taken in (ServerMessages refuses what is known to break the
            // launcher; this is what is not known): the connection ends here and the player reads so now. Left to
            // end this task, the error stayed unseen until the next Join waited for the task (DisposeAsync).
            Fault = e;
            reason = FaultText;
        }
        finally
        {
            // (what is left in hand ends with the connection: its error is this end, said above or not at all)
            Forget(reading);
            Forget(measuring?.Result);
            Closed?.Invoke(reason);
        }
    }

    private static void Forget(Task? left) =>
        _ = left?.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <summary>The next message of the server, whole and within its length and checked before anything is kept or
    /// shown (<see cref="ServerMessages"/>; what cannot be used is left out), or the reading's end.</summary>
    private async Task<Read> ReadAsync(byte[] buffer, CancellationToken stop)
    {
        using var text = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await _socket.ReceiveAsync(buffer, stop).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close)
            {
                return new Read(null, "closed", ServerClosed: true);
            }
            if (text.Length + r.Count > ServerMessages.MaxBytes)
            {
                // no message is this long: not kept in memory until it ends
                return new Read(null, "the server sent a message too long to be one");
            }
            text.Write(buffer, 0, r.Count);
        }
        while (!r.EndOfMessage);
        return new Read(ServerMessages.Checked(Encoding.UTF8.GetString(text.ToArray())));
    }

    /// <summary>The launcher's close frame after the server's: the connection then ends by both sides' word, with
    /// everything the server sent read. It waits a second at most and raises nothing.</summary>
    private async Task AnswerCloseAsync()
    {
        try
        {
            using var limit = new CancellationTokenSource(1000);
            await _sending.WaitAsync(limit.Token).ConfigureAwait(false);
            try
            {
                if (_socket.State == WebSocketState.CloseReceived)
                {
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, limit.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                _sending.Release();
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
        {
            // the server is gone already, or the client is being put away
        }
    }
}
