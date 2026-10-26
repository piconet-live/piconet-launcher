using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Launcher.App.Gpu;

/// <summary>
/// A wait of a few milliseconds, kept to within a fraction of one: what a moving 3D view waits between two frames
/// (<see cref="GpuView"/>). The window's own timers and animation ticks are 15 to 30 ms coarse on Windows once nothing
/// is being drawn, which would hold a 60-frames-a-second picture at about 33 on a fast display; Windows' high-resolution
/// waitable timer keeps the time without changing the computer's timer for every program. Elsewhere, and on a Windows
/// too old for it, an ordinary sleep.
/// </summary>
internal static partial class FrameWait
{
    private const uint HighResolution = 0x2;      // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
    private const uint TimerAllAccess = 0x1F0003;

    /// <summary>Runs <paramref name="then"/> on the window's thread about <paramref name="ms"/> from now.</summary>
    public static void After(double ms, Action then)
    {
        if (ms <= 0.2)
        {
            Dispatcher.UIThread.Post(then, DispatcherPriority.Render);
            return;
        }
        Task.Run(() =>
        {
            Wait(ms);
            Dispatcher.UIThread.Post(then, DispatcherPriority.Render);
        });
    }

    private static void Wait(double ms)
    {
        if (OperatingSystem.IsWindows() && CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, HighResolution, TimerAllAccess) is var timer && timer != IntPtr.Zero)
        {
            try
            {
                var due = -(long)(ms * 10_000);   // relative, in 100 ns
                if (SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    _ = WaitForSingleObject(timer, 100);
                    return;
                }
            }
            finally
            {
                CloseHandle(timer);
            }
        }
        Thread.Sleep(TimeSpan.FromMilliseconds(ms));
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr routine, IntPtr argument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
