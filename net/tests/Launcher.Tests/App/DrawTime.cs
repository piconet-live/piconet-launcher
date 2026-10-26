using Avalonia.Threading;
using Launcher.App;
using Launcher.App.ViewModels;

namespace Launcher.Tests;

/// <summary>The course draw's clock (<see cref="LobbyViewModel.DrawClock"/>) in a test's hands: the draw runs a frame
/// (16 ms of this clock) each time the test says, so the test sees every step of the frame however slowly the
/// window's thread is let work. On the system's clock a busy PC gave the thread a turn only now and then, and the draw,
/// which goes where the time says, passed over steps unseen. The draw waits on one timer at a time, its next frame's.
/// (Compiled into the service's Integration.Tests too.)</summary>
internal sealed class DrawTime : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(20_000);
    private (TimerCallback Callback, object? State)? _next;

    /// <summary>The draw waits for its next frame.</summary>
    public bool Waiting
    {
        get
        {
            lock (_gate)
            {
                return _next is not null;
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            _next = (callback, state);
        }
        return new Once();
    }

    /// <summary>Moves the clock on a frame and lets the draw's wait end.</summary>
    public void Frame()
    {
        (TimerCallback Callback, object? State)? next;
        lock (_gate)
        {
            _now += TimeSpan.FromMilliseconds(16);
            (next, _next) = (_next, null);
        }
        next?.Callback(next.Value.State);
    }

    /// <summary>On the window's thread: tells the page of <paramref name="state"/>'s changes until the draw has begun
    /// (the result reaches the page so), then runs it a frame at a time until <paramref name="finished"/>, with
    /// <paramref name="eachFrame"/> after each; thirty seconds at most.</summary>
    public void Run(AppState state, Func<bool> finished, Action? eachFrame = null)
    {
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (!finished() && DateTime.UtcNow < limit)
        {
            if (Waiting)
            {
                Frame();
            }
            else
            {
                Thread.Sleep(5);
                state.Update(s => s);
            }
            Dispatcher.UIThread.RunJobs();
            eachFrame?.Invoke();
        }
        Assert.True(finished(), "the draw did not end");
    }

    private sealed class Once : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
