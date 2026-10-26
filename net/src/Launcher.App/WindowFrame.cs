using System.Runtime.InteropServices;

namespace Launcher.App;

/// <summary>
/// What Windows adds to a window's height beyond the height the toolkit asks for. The toolkit sizes a window as one
/// with Windows' standard title bar (the caption, its frame and padding: 31 px at 100%), and the window's extended
/// client area then covers that bar as well: a window asked for 720 is 751 tall, its least height too. The window
/// takes this off what it asks (<see cref="Views.MainWindow"/>), so that it is 1280 x 720 at least and fits a
/// 1366 x 768 screen with its taskbar. In device-independent pixels, as the toolkit's sizes are; 0 on another system.
/// </summary>
internal static partial class WindowFrame
{
    private const int CaptionHeight = 4;      // SM_CYCAPTION
    private const int SizeFrameHeight = 33;   // SM_CYSIZEFRAME
    private const int PaddedBorder = 92;      // SM_CXPADDEDBORDER

    public static double TitleOverlap()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return 0;
        }
        var height = GetSystemMetricsForDpi(CaptionHeight, 96) + GetSystemMetricsForDpi(SizeFrameHeight, 96)
            + GetSystemMetricsForDpi(PaddedBorder, 96);
        return height is > 0 and < 80 ? height : 0;
    }

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);
}
