using Avalonia.Threading;
using Launcher.App.Views;
using Launcher.Core;
using Launcher.Core.Board;

namespace Launcher.App;

/// <summary>
/// "Show inputs" (Settings, Game): the Inputs window, a window of the launcher's own beside the game's, as M'Overlay is
/// beside Melee. On, it opens with each game start (and at once when it is switched on while a game runs); it stays
/// open between races, at rest, so a capture of it keeps its window; it closes when the setting is switched off, when
/// the player closes it (the next start opens it again) and with the launcher. Its place and size are kept for the next
/// time. It reads the game's pad from the status board only (<see cref="GameBoard.Pad"/>): nothing of the game's race,
/// replay or online exactness changes.
/// </summary>
public sealed class InputsKeeper : IDisposable
{
    private readonly AppState _state;
    private readonly Func<PadReading?> _read;
    private bool _on;
    private bool _disposed;

    /// <param name="read">The pad now; null: the running game's board's (<see cref="AppState.Board"/>).</param>
    public InputsKeeper(AppState state, Func<PadReading?>? read = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _read = read ?? (() => state.Board?.Pad);
        _on = state.Settings.ShowInputs;
        state.BoardMade += OnBoardMade;
        state.Changed += OnChanged;
    }

    /// <summary>The window while it is open.</summary>
    public InputsWindow? Window { get; private set; }

    /// <summary>A game starts: the window opens if the setting is on (the window's thread).</summary>
    public void GameStarted()
    {
        if (!_disposed && _state.Settings.ShowInputs)
        {
            Open();
        }
    }

    private void OnBoardMade(GameBoard board) => OnWindowsThread(GameStarted);

    private void OnChanged() => OnWindowsThread(() =>
    {
        var on = _state.Settings.ShowInputs;
        if (_disposed || on == _on)
        {
            return;
        }
        _on = on;
        if (!on)
        {
            Window?.Close();
        }
        else if (_state.Racing)
        {
            Open();
        }
    });

    /// <summary>At once on the window's thread, else posted to it.</summary>
    private static void OnWindowsThread(Action act)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            act();
        }
        else
        {
            Dispatcher.UIThread.Post(act);
        }
    }

    private void Open()
    {
        if (Window is not null)
        {
            return;
        }
        var window = new InputsWindow(_read, _state.Settings.InputsWindow);
        window.Closing += (_, _) =>
        {
            if (window.Kept is { } place && place != _state.Settings.InputsWindow)
            {
                _state.Update(s => s with { InputsWindow = place });
            }
        };
        window.Closed += (_, _) =>
        {
            if (Window == window)
            {
                Window = null;
            }
        };
        Window = window;
        window.Show();
    }

    /// <summary>The launcher's window closed: the Inputs window goes with it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _state.BoardMade -= OnBoardMade;
        _state.Changed -= OnChanged;
        Window?.Close();
    }
}
