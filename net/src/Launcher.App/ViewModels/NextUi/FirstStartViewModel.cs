using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// The redesigned interface's first start (Views/NextUi/FirstStartView), the classic first screen in three steps: 1
/// "Welcome to Piconet" (the disc image picked, checked and the game prepared from it once; "Played before? Bring over
/// an older copy"), 2 "Your name" (the keyboard on the screen), 3 "Your controller" (buttons pressed light up). Next and
/// Back between them, "Let's race" at the end opens the launcher. Everything is the classic first screen's
/// (<see cref="DiscViewModel"/>: its checks, its preparation and every sentence it says when that does not end ready),
/// the name the left column's (<see cref="AccountViewModel"/>), the controller the Controller page's
/// (<see cref="ControllerViewModel"/>).
/// </summary>
public sealed partial class FirstStartViewModel : ViewModelBase, IDisposable
{
    public const int Steps = 3;

    private readonly MainViewModel _main;
    private readonly ShellViewModel _shell;

    public FirstStartViewModel(MainViewModel main, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(shell);
        (_main, _shell) = (main, shell);
        main.Disc.PropertyChanged += OnDisc;
        Show();
    }

    public DiscViewModel Disc => _main.Disc;

    public AccountViewModel Account => _main.Account;

    public ControllerViewModel Controller => _main.Controller;

    /// <summary>The step showing, 1 to 3.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnDiscStep), nameof(OnNameStep), nameof(OnPadStep), nameof(CanBack), nameof(Eyebrow), nameof(Title), nameof(Line),
        nameof(NextText), nameof(CanNext), nameof(Marks))]
    public partial int Step { get; private set; } = 1;

    public bool OnDiscStep => Step == 1;

    public bool OnNameStep => Step == 2;

    public bool OnPadStep => Step == 3;

    public bool CanBack => Step > 1;

    /// <summary>The three marks over the page, those of the steps done and showing lit.</summary>
    public IReadOnlyList<bool> Marks => [.. Enumerable.Range(1, Steps).Select(n => n <= Step)];

    public string Eyebrow => $"Step {Step} of {Steps}";

    public string Title => Step switch
    {
        1 => "Welcome to Piconet",
        2 => "Your name",
        _ => "Your controller",
    };

    public string Line => Step switch
    {
        1 => "Race F-Zero GX online. First the game itself: pick your own disc image, and the launcher prepares it once.",
        2 => "This is what other racers see. You can change it later on your profile.",
        _ => "Press a few buttons. Each one lights up when the launcher sees it. Ports and buttons are in Settings later.",
    };

    /// <summary>The main button's word: Next, and at the end Let's race.</summary>
    public string NextText => Step == Steps ? "LET'S RACE" : "NEXT";

    /// <summary>Next can be pressed: on the first step once the disc image is the game's and nothing is being prepared.</summary>
    public bool CanNext => Step != 1 || (Disc.Verified && Disc.Idle);

    /// <summary>The game is ready (the first step done): said under the disc.</summary>
    public bool GameReady => Disc.Ready;

    /// <summary>Next: the first step prepares the game (when it needs it) and goes on once it is ready; the last opens
    /// the launcher.</summary>
    [RelayCommand]
    private async Task NextAsync()
    {
        switch (Step)
        {
            case 1 when Disc.Ready:
                Step = 2;
                break;
            case 1:
                if (CanNext)
                {
                    await Disc.ContinueCommand.ExecuteAsync(null).ConfigureAwait(true);   // (ready: the next step follows)
                }
                break;
            case 2:
                Step = 3;
                _main.Controller.Shown();   // (the controller's test: the launcher's helper watches its port)
                break;
            default:
                _main.Controller.Hidden();
                Disc.Open();
                break;
        }
    }

    /// <summary>Back: the step before.</summary>
    [RelayCommand]
    public void Back()
    {
        if (Step == 3)
        {
            _main.Controller.Hidden();
        }
        if (Step > 1)
        {
            Step--;
        }
    }

    /// <summary>The name: the keyboard on the screen; Done keeps it as the game shows it.</summary>
    [RelayCommand]
    private void ChangeName()
    {
        var account = Account;
        _shell.OpenKeyboard("Your name", account.Name, Online.Protocol.PlayerNames.MaxLength, false, text =>
        {
            account.Draft = text;
            account.SaveName();
        });
    }

    /// <summary>The view shows the first start: the classic screen's CONTINUE does not open the launcher here (the
    /// steps do).</summary>
    public void Shown()
    {
        Disc.OpensAtOnce = false;
        Step = Disc.Ready ? Math.Max(Step, 1) : 1;
    }

    /// <summary>The view is gone (the launcher opened, or the classic interface came back): the classic screen opens
    /// it again by itself.</summary>
    public void Left()
    {
        if (Step == 3)
        {
            _main.Controller.Hidden();
        }
        Disc.OpensAtOnce = true;
    }

    public void Dispose() => _main.Disc.PropertyChanged -= OnDisc;

    private void OnDisc(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiscViewModel.Ready) && Disc.Ready && Step == 1)
        {
            Step = 2;   // prepared: the name next
        }
        Show();
    }

    private void Show()
    {
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(GameReady));
    }
}
