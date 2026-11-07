using Avalonia;
using Launcher.Core;

namespace Launcher.App;

/// <summary>
/// How the launcher's windows are drawn on Windows: onto the window's own surface (the redirection surface), not
/// through Windows' composition (WinUI, else DirectComposition) as Avalonia draws by default. A capture that reads
/// the window's surface (OBS's Window Capture on its Automatic way, BitBlt) gets nothing of a composed window, and
/// players stream the Inputs window, as they do M'Overlay's. Avalonia takes the choice for every window of the
/// process or for none (<see cref="Win32PlatformOptions.CompositionMode"/>, read once as the platform starts: no
/// window has a way of its own). The launch file's <c>"windowComposition"</c> goes back to Avalonia's default, to
/// compare the two.
/// What it costs, by Avalonia's own notes: the composed ways allow a window's acrylic and blur effects and drawing at
/// a high refresh rate; on the surface "some Avalonia features might not work" (Avalonia keeps it for older
/// systems). The launcher uses no acrylic or blur; the see-through corners of a menu or a tooltip can take their
/// fallback colour instead.
/// </summary>
public static class WindowsDrawing
{
    /// <summary>The Windows options of the app's builder (Program.BuildAvaloniaApp).</summary>
    public static Win32PlatformOptions Options(DebugSettings debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        return debug.WindowComposition
            ? new Win32PlatformOptions()
            : new Win32PlatformOptions { CompositionMode = [Win32CompositionMode.RedirectionSurface] };
    }
}
