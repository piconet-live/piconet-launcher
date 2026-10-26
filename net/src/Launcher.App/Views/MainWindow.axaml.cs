using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Huskui.Avalonia.Controls;

namespace Launcher.App.Views;

/// <summary>The window: its title bar, the left column, the tabs and the page showing.</summary>
public sealed partial class MainWindow : AppWindow
{
    /// <summary>The machine's height in the left column: the window's least height (720) has room for it beside
    /// the column's other parts (the name, the play control with its lines, "Play F-Zero GX", Settings, the
    /// connection).</summary>
    private const double MachineHeight = 176;

    /// <summary>The machine's least height while the column has more to say than that (a notice under the play
    /// control): the machine gives way before a button is cut off.</summary>
    private const double MachineTight = 72;

    private readonly Border? _machine;
    private readonly ScrollViewer? _column;
    private readonly StackPanel? _parts;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Height -= _overlap;
        _machine = this.FindControl<Border>("MachineWell");
        _column = this.FindControl<ScrollViewer>("ColumnScroll");
        _parts = this.FindControl<StackPanel>("ColumnParts");
        if (_parts is not null)
        {
            // the column's parts grew or shrank (a notice came or went)
            _parts.LayoutUpdated += (_, _) => Fold();
        }
        Fold();
        DataContextChanged += (_, _) => Hear();
        // the interface scale's keys, wherever the keyboard is in the window (a text box too)
        AddHandler(KeyDownEvent, OnScaleKey, RoutingStrategies.Tunnel);
    }

    /// <summary>The window's least size at 100% (1280 x 720), and its
    /// title bar's height. Its size at the first start is the least (MainWindow.axaml): it fits a 1366 x 768 screen
    /// with the taskbar.</summary>
    private const double LeastWidth = 1280, LeastHeight = 720, TitleBar = 32;

    /// <summary>The interface scale the window is drawn at (1: 100%).</summary>
    private double Scale => _heard?.Scale is > 0 and var scale ? scale : 1;

    /// <summary>Ctrl+0: 100%; Ctrl+plus and Ctrl+minus (the main keys or the number pad's): a step larger or
    /// smaller. At once and kept: the way back from a scale the player cannot use.</summary>
    private void OnScaleKey(object? sender, KeyEventArgs e)
    {
        if (_heard is not { } main || (e.KeyModifiers & KeyModifiers.Control) == 0 || (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            return;
        }
        int? steps = e.Key switch
        {
            Key.D0 or Key.NumPad0 => 0,
            Key.OemPlus or Key.Add => 1,
            Key.OemMinus or Key.Subtract => -1,
            _ => null,
        };
        if (steps is { } by)
        {
            main.StepScale(by);
            e.Handled = true;
        }
    }

    /// <summary>A new interface scale: the window's least size grows with it, as far as the screen it is on has
    /// room (beyond that its content scrolls: Controls.ScaledRoot), and so does the title bar the system drags by.</summary>
    private void Rescaled()
    {
        var scale = Scale;
        var room = Screens?.ScreenFromWindow(this) is { } screen ? screen.WorkingArea.Size.ToSize(screen.Scaling) : (Size?)null;
        MinWidth = Math.Min(LeastWidth * scale, room?.Width ?? double.MaxValue);
        MinHeight = Math.Min(LeastHeight * scale, room?.Height ?? double.MaxValue) - _overlap;
        ExtendClientAreaTitleBarHeightHint = TitleBar * scale;
        Fold();
    }

    private void OnMainChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.Scale))
        {
            Rescaled();
        }
    }

    private ViewModels.MainViewModel? _heard;

    /// <summary>The pad that works the window (made with the window's pages).</summary>
    public PadNavigator? Pad { get; private set; }

    /// <summary>The lobby's three moments (the course vote opens, the machine stage opens, the race starts): a sound
    /// at the settings' volume, and the taskbar's button flashing while the window is not in front; the draw's
    /// sounds (<see cref="ViewModels.LobbyViewModel.Sounded"/>) without the flash.</summary>
    private void Hear()
    {
        if (_heard is not null)
        {
            _heard.Lobby.Moment -= OnMoment;
            _heard.Lobby.Sounded -= OnLobbySound;
            _heard.Garage.Confirmed -= OnConfirmed;
            _heard.Notices.Alert -= OnNotice;
            _heard.PropertyChanged -= OnMainChanged;
        }
        _heard = DataContext as ViewModels.MainViewModel;
        if (_heard is not null)
        {
            if (this.FindControl<Controls.PadMark>("PadMark") is { } mark)
            {
                Pad = new PadNavigator(this, _heard, mark, this.FindControl<ContentControl>("PageHost"));
            }
            _heard.Lobby.Moment += OnMoment;
            _heard.Lobby.Sounded += OnLobbySound;
            _heard.Garage.Confirmed += OnConfirmed;
            _heard.Notices.Alert += OnNotice;
            _heard.PropertyChanged += OnMainChanged;
        }
        Rescaled();
    }

    /// <summary>A server's notice of the two highest tiers: the taskbar's button flashes (warning and urgent), and
    /// urgent's own tone sounds, minimised or not, at the launcher's volume unless its sounds are muted.</summary>
    private void OnNotice(ViewModels.NoticeAlert alert)
    {
        if (_heard is not { } main)
        {
            return;
        }
        if (alert == ViewModels.NoticeAlert.Sound)
        {
            Attention.Sound(global::Launcher.Core.LobbyMoment.UrgentNotice, main.SoundSettings);
        }
        else
        {
            Attention.Flash(this);
        }
    }

    private void OnMoment(Launcher.Core.LobbyMoment moment)
    {
        if (_heard is { } main)
        {
            Attention.Sound(moment, main.SoundSettings);
            Attention.Flash(this);
        }
    }

    /// <summary>A machine confirmed in the Garage (USE THIS MACHINE, Save, READY): the game's confirm, no flash.</summary>
    private void OnConfirmed() => OnLobbySound(global::Launcher.Core.LauncherSound.Confirm);

    /// <summary>The draw's blip on a hop and its chime on the course drawn: a sound, no flash.</summary>
    private void OnLobbySound(global::Launcher.Core.LauncherSound sound)
    {
        if (_heard is { } main)
        {
            Attention.Sound(sound, main.SoundSettings);
        }
    }

    /// <summary>What Windows adds to the height the window asks for (<see cref="WindowFrame"/>): taken off the height
    /// and the least height asked, so that the window is 1280 x 720 as it opens and at least.</summary>
    private readonly double _overlap = WindowFrame.TitleOverlap();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (global::Launcher.Core.LoadTrace.On)
        {
            global::Launcher.Core.LoadTrace.Note("window opened");
            RequestAnimationFrame(_ => global::Launcher.Core.LoadTrace.Note("window's first frame"));
        }
        if (TryGetPlatformHandle() is { } handle)
        {
            Taskbar.Mark(handle.Handle);
        }
        Rescaled();   // the screen it opened on is known now
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty)
        {
            Fold();
        }
    }

    /// <summary>A column with more to say than its height holds: the machine shrinks, so that the name and the play
    /// control stay in sight.</summary>
    private void Fold()
    {
        // the window's size is first set before its content exists
        if (_machine is { } well)
        {
            var wanted = MachineHeight;
            if (_column is { Viewport.Height: > 0 } column && _parts is { } parts)
            {
                // what the parts need against the room they would have with the machine at that height
                var room = column.Viewport.Height + (well.Height - wanted);
                var lacking = Math.Ceiling(parts.DesiredSize.Height - room);
                if (lacking > 0)
                {
                    wanted = Math.Max(MachineTight, wanted - lacking);
                }
            }
            if (well.Height != wanted)
            {
                well.Height = wanted;
            }
        }
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
