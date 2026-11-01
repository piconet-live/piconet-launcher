namespace Launcher.App;

/// <summary>
/// Keeps the collector's pauses small. A young collection's pause is about what has survived since the last one, and
/// by itself the runtime waits until tens of MB have: a pause of 40 ms in the middle of a page's rows. While the window
/// is open this asks for one as soon as a few MB have been made, off the window's thread, so that each pause is a few
/// ms. A launcher that makes nothing asks for nothing.
/// </summary>
public sealed class YoungCollector : IDisposable
{
    private const long Allocated = 4 * 1024 * 1024;

    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(100);

    private readonly Timer _timer;
    private long _last = GC.GetTotalAllocatedBytes();

    public YoungCollector() => _timer = new Timer(_ => Look(), null, Every, Every);

    public void Dispose() => _timer.Dispose();

    private void Look()
    {
        var now = GC.GetTotalAllocatedBytes();
        if (now - _last >= Allocated)
        {
            GC.Collect(1, GCCollectionMode.Forced, blocking: true, compacting: false);
            _last = GC.GetTotalAllocatedBytes();
        }
    }
}
