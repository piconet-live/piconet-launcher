using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Launcher.App.Controls;
using Launcher.Core;
using Launcher.Core.Board;

namespace Launcher.App.Views;

/// <summary>
/// The Inputs window ("Show inputs", <see cref="InputsKeeper"/>): the player's controller drawn live from what the game
/// last wrote on its status board (<see cref="GameBoard.Pad"/>), looked at 60 times a second while the window is open
/// (the board itself is looked at every 15 ms) and drawn again only when it changed; at rest while there is none.
/// Nothing here waits: a look is one read of a reference.
/// </summary>
public sealed partial class InputsWindow : Window
{
    /// <summary>The least size kept from a settings file, and the most.</summary>
    private const int Least = 120, Most = 8192;

    private readonly Func<PadReading?> _read;
    private readonly PadOverlay _pad;
    private readonly DispatcherTimer _timer;
    private PadReading? _shown;

    /// <summary>For the designer only.</summary>
    public InputsWindow()
        : this(() => null, null)
    {
    }

    /// <param name="read">The pad now (any thread's writes: the board's look sets it whole); null: none.</param>
    /// <param name="place">Where the window was last; null, or a place on no screen: its own size, centred.</param>
    public InputsWindow(Func<PadReading?> read, InputsPlace? place)
    {
        _read = read;
        AvaloniaXamlLoader.Load(this);
        _pad = this.FindControl<PadOverlay>("Pad") ?? throw new InvalidOperationException("the Inputs window has no pad");
        Place(place);
        _timer = new DispatcherTimer(Every, DispatcherPriority.Render, (_, _) => Look());
        Opened += (_, _) =>
        {
            Look();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>How often the pad is looked at: a refresh of a 60 Hz screen, the game's own rate of steps.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(1 / 60.0);

    /// <summary>Where the window stands and its size, as kept: only while it stands as a window (not minimised,
    /// maximised or full screen).</summary>
    public InputsPlace? Kept => WindowState == WindowState.Normal
        ? new InputsPlace(Position.X, Position.Y, (int)Math.Round(ClientSize.Width), (int)Math.Round(ClientSize.Height))
        : null;

    /// <summary>The pad as drawn now (tests).</summary>
    public PadPicture? Shown => _pad.Pad;

    /// <summary>One look at the game's pad: drawn again only when it is another one.</summary>
    public void Look()
    {
        var now = _read();
        if (!Equals(now, _shown))
        {
            _shown = now;
            _pad.Pad = PadPicture.Of(now);
        }
    }

    private void Place(InputsPlace? place)
    {
        if (place is { Width: >= Least and <= Most, Height: >= Least / 2 and <= Most } size)
        {
            Width = size.Width;
            Height = size.Height;
        }
        var corner = place is null ? (PixelPoint?)null : new PixelPoint(place.X, place.Y);
        if (corner is { } at && Screens?.All.Any(s => s.WorkingArea.Contains(at + new PixelPoint(24, 24))) == true)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = at;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
