using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>Settings, three of the Settings tab's four pages (<see cref="Tab"/>; the fourth, Controller, is its own
/// view model). Game: the screen, full screen, vertical sync, the frame rate, the game's volume, other racers' sounds.
/// Online: the input delay, the server, the account (Discord sign-in, shown as coming later), notices, Discord's status, the chat's swear words. Launcher: the disc,
/// the launcher's own sounds, updates, replays. Every change is saved at once, but the server: the box is a draft, set when the player
/// commits it (Enter, or the button under it), never while they type or by leaving the box, and a blank box is the
/// official server only for a player already on it (<see cref="ServerChoice"/>: the launcher asks a changed server at once, so a server that is set is
/// a server that is contacted).</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly bool _loading;

    /// <summary>The launcher's update, as the Launcher tab shows it (the main page's); null: none (a test's page).</summary>
    public UpdateViewModel? Update { get; init; }

    /// <summary>"Bring over an older copy", the Launcher tab's (<see cref="DataMoveViewModel"/>).</summary>
    public DataMoveViewModel Move { get; }

    public SettingsViewModel(AppState state)
    {
        _state = state;
        Move = new DataMoveViewModel(state);
        _loading = true;
        var s = state.Settings;
        Tab = s.SettingsTab == SettingsTab.Controller ? SettingsTab.Game : s.SettingsTab;
        DiscPath = s.DiscPath ?? "";
        Server = s.Server ?? "";
        NoServer = s.NoServer;
        RecordReplays = s.RecordReplays;
        RecordPractice = s.RecordPractice;
        ReplayFolder = s.ReplayFolder ?? "";
        OtherSoundsIndex = Math.Clamp((int)s.OtherSounds, 0, OtherSoundsChoices.Count - 1);
        NoticesIndex = s.NoticesSilent ? 1 : 0;
        ShowOnDiscord = s.ShowOnDiscord;
        HideSwearWords = s.HideSwearWords;
        WidescreenIndex = Enum.IsDefined(s.Widescreen) ? (int)s.Widescreen : (int)WidescreenMode.Game;
        ShowFrameRate = s.ShowFrameRate || FrameRateAlwaysShown;
        ShowInputs = s.ShowInputs;
        Rumble = s.Rumble;
        InputDelayIndex = s.RaceInputDelay;
        VerticalSyncIndex = s.VerticalSync ? 1 : 0;
        FullScreenIndex = s.GameFullScreen == LauncherSettings.FullScreenExclusive ? 1 : 0;
        RefreshRateIndex = Math.Max(0, RefreshRates.IndexOf(s.RefreshRate));
        AutoUpdate = s.AutoUpdate;
        GameVolume = s.GameVolume;
        SoundVolume = s.SoundVolume;
        SoundMuted = s.SoundMuted;
        NavigationSounds = s.NavigationSounds;
        PadNavigation = s.PadNavigation;
        PendingScale = _scale = s.ShownInterfaceScale;
        _loading = false;
        // the first screen can set the disc after this page was made
        state.Changed += () => OnUi(() =>
        {
            if (_state.Settings.DiscPath is { } disc && disc != DiscPath.Trim())
            {
                DiscPath = disc;
            }
            ShowServer();
            if (_state.Settings.ShownInterfaceScale != _scale)
            {
                // set elsewhere (the keys): the slider shows it, and what was pending there is dropped
                PendingScale = _scale = _state.Settings.ShownInterfaceScale;
                OnPropertyChanged(nameof(CanApplyScale));
            }
        });
    }

    /// <summary>The interface scale in use, in percent.</summary>
    private int _scale;

    /// <summary>The interface scale on the slider, in percent: nothing changes until Apply (a scale applied while
    /// the slider is dragged would move the slider under the pointer).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingScaleText), nameof(CanApplyScale))]
    public partial int PendingScale { get; set; }

    public string PendingScaleText => $"{PendingScale}%";

    public bool CanApplyScale => Applied(PendingScale) != _scale;

    /// <summary>Apply: the slider's scale, at once and kept.</summary>
    [RelayCommand]
    private void ApplyScale()
    {
        var to = Applied(PendingScale);
        _state.Update(s => s with { InterfaceScale = to });
        PendingScale = _scale = to;
        OnPropertyChanged(nameof(CanApplyScale));
    }

    /// <summary>A slider's value as a scale there is: on the steps' grid, within the ends.</summary>
    private static int Applied(int value) =>
        Math.Clamp((int)Math.Round(value / (double)LauncherSettings.InterfaceScaleStep) * LauncherSettings.InterfaceScaleStep,
            LauncherSettings.LeastInterfaceScale, LauncherSettings.MostInterfaceScale);

    public static double LeastScale => LauncherSettings.LeastInterfaceScale;

    public static double MostScale => LauncherSettings.MostInterfaceScale;

    public static double ScaleStep => LauncherSettings.InterfaceScaleStep;

    /// <summary>The page showing: Game, Online or Launcher (the window sets it; Controller is another page).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnGame), nameof(OnOnline), nameof(OnLauncher))]
    public partial SettingsTab Tab { get; set; }

    public bool OnGame => Tab == SettingsTab.Game;

    public bool OnOnline => Tab == SettingsTab.Online;

    public bool OnLauncher => Tab == SettingsTab.Launcher;

    public static IReadOnlyList<string> OtherSoundsChoices { get; } = ["Full", "Quieter", "Off"];

    /// <summary>The picture (<see cref="WidescreenMode"/>, in its order): the game's Normal, its own Wide Screen, true
    /// widescreen.</summary>
    public static IReadOnlyList<string> WidescreenChoices { get; } = ["Off", "Game's", "True"];

    /// <summary>The server's notices: with their tone and flash, or silent (shown all the same).</summary>
    public static IReadOnlyList<string> NoticesChoices { get; } = ["Alert", "Silent"];

    /// <summary>The input delay's values, in frames: each is its own place in the list.</summary>
    public static IReadOnlyList<string> InputDelayChoices { get; } = ["0", "1", "2"];

    public static IReadOnlyList<string> VerticalSyncChoices { get; } = ["Off", "On"];

    /// <summary>The two kinds of full screen, in the order of <see cref="FullScreenIndex"/>.</summary>
    public static IReadOnlyList<string> FullScreenChoices { get; } = ["Borderless", "Exclusive"];

    /// <summary>The refresh rates, in the order of <see cref="RefreshRateIndex"/>, as the settings file names them
    /// (<see cref="RefreshRates"/>) and as the page shows them. No choice adds input delay: the game's
    /// own picture comes at its 60 Hz moment and the pictures after it are guessed forward (stage 5).</summary>
    public static IReadOnlyList<string> RefreshRateChoices { get; } =
        ["60 Hz", "120 Hz", "240 Hz", "Display"];

    private static readonly List<string> RefreshRates =
        [LauncherSettings.RefreshAsTheGame, LauncherSettings.Refresh120, LauncherSettings.Refresh240, LauncherSettings.RefreshAsTheDisplay];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscStatusText), nameof(DiscProblem))]
    public partial string DiscPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerProblem), nameof(ServerWaiting))]
    public partial string Server { get; set; }

    /// <summary>No server is set (<see cref="LauncherSettings.NoServer"/>): the line under the box says so, with a
    /// button for the official one.</summary>
    [ObservableProperty]
    public partial bool NoServer { get; set; }

    /// <summary>What a blank server box stands for, as the settings are (<see cref="ServerChoice.BlankIsOfficial"/>).</summary>
    public string ServerPlaceholder => ServerChoice.BlankIsOfficial(_state.Settings) ? "Official server" : "No server";

    [ObservableProperty]
    public partial bool RecordReplays { get; set; }

    /// <summary>"Record practice", beside "Record my races" (<see cref="LauncherSettings.RecordPractice"/>).</summary>
    [ObservableProperty]
    public partial bool RecordPractice { get; set; }

    [ObservableProperty]
    public partial string ReplayFolder { get; set; }

    [ObservableProperty]
    public partial int OtherSoundsIndex { get; set; }

    [ObservableProperty]
    public partial int NoticesIndex { get; set; }

    [ObservableProperty]
    public partial bool AutoUpdate { get; set; }

    /// <summary>Discord's status shows what the player does (<see cref="LauncherSettings.ShowOnDiscord"/>).</summary>
    [ObservableProperty]
    public partial bool ShowOnDiscord { get; set; }

    /// <summary>The chat's swear words are masked (<see cref="LauncherSettings.HideSwearWords"/>).</summary>
    [ObservableProperty]
    public partial bool HideSwearWords { get; set; }

    [ObservableProperty]
    public partial int WidescreenIndex { get; set; }

    /// <summary>The game window's strip shows the frame rate.</summary>
    [ObservableProperty]
    public partial bool ShowFrameRate { get; set; }

    [ObservableProperty]
    public partial bool ShowInputs { get; set; }

    /// <summary>A test build shows the frame rate whatever the setting says (<see cref="LauncherSettings.FrameRateShown"/>):
    /// its box is ticked and cannot be changed, and the file keeps the player's own value.</summary>
    public bool FrameRateAlwaysShown { get; } = StackStamp.Current.Test;

    /// <summary>The controllers rumble in the game (<see cref="LauncherSettings.Rumble"/>).</summary>
    [ObservableProperty]
    public partial bool Rumble { get; set; }

    /// <summary>This game's input delay in a race, in frames (<see cref="LauncherSettings.InputDelay"/>): the place
    /// in <see cref="InputDelayChoices"/> is the value.</summary>
    [ObservableProperty]
    public partial int InputDelayIndex { get; set; }

    /// <summary>Vertical sync (<see cref="LauncherSettings.VerticalSync"/>): 0 off, 1 on.</summary>
    [ObservableProperty]
    public partial int VerticalSyncIndex { get; set; }

    /// <summary>Full screen (<see cref="LauncherSettings.FullScreen"/>): 0 borderless, 1 exclusive.</summary>
    [ObservableProperty]
    public partial int FullScreenIndex { get; set; }

    /// <summary>Refresh rate (<see cref="LauncherSettings.RefreshRate"/>): 0 the game's 60, 1 120, 2 240, 3 the
    /// display's own.</summary>
    [ObservableProperty]
    public partial int RefreshRateIndex { get; set; }

    /// <summary>How loud the game is, 0 to 100: it starts at this volume in every start the launcher makes.</summary>
    [ObservableProperty]
    public partial int GameVolume { get; set; }

    /// <summary>How loud the launcher's own sounds are (the lobby's three), 0 to 100, and whether they are off.</summary>
    [ObservableProperty]
    public partial int SoundVolume { get; set; }

    [ObservableProperty]
    public partial bool SoundMuted { get; set; }

    /// <summary>The game's cursor and cancel as the pad or the keyboard moves (<see cref="LauncherSettings.NavigationSounds"/>).</summary>
    [ObservableProperty]
    public partial bool NavigationSounds { get; set; }

    /// <summary>The pad works the launcher's pages (<see cref="LauncherSettings.PadNavigation"/>).</summary>
    [ObservableProperty]
    public partial bool PadNavigation { get; set; }

    /// <summary>The settings as they are, for the launcher's own sounds.</summary>
    public LauncherSettings Sounds => _state.Settings;

    /// <summary>The launcher's volume was set: the page lets it be heard (a moment after the last change).</summary>
    public event Action? SoundSet;

    private int _soundChanges;

    /// <summary>Why the replay folder did not open, when it did not.</summary>
    [ObservableProperty]
    public partial string FolderNotice { get; set; } = "";

    /// <summary>The disc image is not the game's (or there is none): the line under it is a notice.</summary>
    public bool DiscProblem => DiscCheck.Check(DiscPath.Trim()) != DiscStatus.Ok;

    public string DiscStatusText => DiscWords.Of(DiscPath);

    /// <summary>What is typed is no server address: it is not saved, and the line under it says what one is.</summary>
    public bool ServerProblem => Server.Trim().Length > 0 && !SettingsCheck.IsServer(Server.Trim());

    partial void OnDiscPathChanged(string value) => Save();

    /// <summary>The box holds something that is not set yet (an address, or a blank that would mean no server):
    /// the row under it has the button that sets it.</summary>
    public bool ServerWaiting => ServerChoice.Waiting(_state.Settings, Server);

    /// <summary>That button's word.</summary>
    public string ServerCommitText => Server.Trim().Length == 0 ? "Use no server" : "Use this server";

    partial void OnServerChanged(string value) => Did(ServerAct.Typing);

    /// <summary>Enter in the server's box, or the button under it: what the box holds is the player's choice.</summary>
    [RelayCommand]
    private void CommitServer() => Did(ServerAct.Commit);

    /// <summary>The box left: nothing is set (<see cref="ServerChoice"/>).</summary>
    public void LeftServer() => Did(ServerAct.Leaving);

    private void Did(ServerAct act)
    {
        if (!_loading && ServerChoice.After(_state.Settings, Server, act) != _state.Settings)
        {
            _state.Update(s => ServerChoice.After(s, Server, act));
        }
        ShowServer();
    }

    private void ShowServer()
    {
        NoServer = _state.Settings.NoServer;
        OnPropertyChanged(nameof(ServerWaiting));
        OnPropertyChanged(nameof(ServerCommitText));
        OnPropertyChanged(nameof(ServerPlaceholder));
    }

    partial void OnRecordReplaysChanged(bool value) => Save();

    partial void OnRecordPracticeChanged(bool value) => Save();

    partial void OnReplayFolderChanged(string value)
    {
        FolderNotice = "";
        Save();
    }

    partial void OnOtherSoundsIndexChanged(int value) => Save();

    partial void OnNoticesIndexChanged(int value) => Save();

    partial void OnAutoUpdateChanged(bool value)
    {
        Save();
        if (value && !_loading)
        {
            Update?.Start();   // switched on: looked for (and downloaded) now, not only at the next start
        }
    }

    partial void OnShowOnDiscordChanged(bool value) => Save();

    partial void OnHideSwearWordsChanged(bool value) => Save();

    partial void OnWidescreenIndexChanged(int value) => Save();

    partial void OnShowFrameRateChanged(bool value) => Save();

    partial void OnShowInputsChanged(bool value) => Save();

    partial void OnRumbleChanged(bool value) => Save();

    partial void OnInputDelayIndexChanged(int value) => Save();

    partial void OnVerticalSyncIndexChanged(int value) => Save();

    partial void OnFullScreenIndexChanged(int value) => Save();

    partial void OnRefreshRateIndexChanged(int value) => Save();

    partial void OnGameVolumeChanged(int value) => Save();

    partial void OnSoundVolumeChanged(int value)
    {
        Save();
        _ = HearSoonAsync(++_soundChanges);
    }

    partial void OnSoundMutedChanged(bool value)
    {
        Save();
        _ = HearSoonAsync(++_soundChanges);
    }

    partial void OnNavigationSoundsChanged(bool value) => Save();

    partial void OnPadNavigationChanged(bool value) => Save();

    private async Task HearSoonAsync(int change)
    {
        if (_loading)
        {
            return;
        }
        await Task.Delay(250).ConfigureAwait(true);
        if (change == _soundChanges)
        {
            SoundSet?.Invoke();
        }
    }

    [RelayCommand]
    private void OpenReplayFolder() =>
        FolderNotice = DolphinTools.TryOpenFolder(_state.Settings.ReplayFolder) ? "" : "Folder could not be opened";

    /// <summary>The button under the server's box while no server is set: the official server, which a blank box
    /// stands for from then on.</summary>
    [RelayCommand]
    private void UseOfficialServer()
    {
        _state.Update(s => ServerChoice.After(s, Server, ServerAct.Official));
        Server = "";
        ShowServer();
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }
        // (the server is not saved here: SetServer)
        _state.Update(s => s with
        {
            DiscPath = Kept(DiscPath, SettingsCheck.MostPath),
            RecordReplays = RecordReplays,
            RecordPractice = RecordPractice,
            ReplayFolder = Kept(ReplayFolder, SettingsCheck.MostPath),
            OtherSounds = (OtherSounds)Math.Clamp(OtherSoundsIndex, 0, OtherSoundsChoices.Count - 1),
            // (a list with nothing selected says -1: the setting stays what it is)
            NoticesSilent = NoticesIndex is 0 or 1 ? NoticesIndex == 1 : s.NoticesSilent,
            AutoUpdate = AutoUpdate,
            ShowOnDiscord = ShowOnDiscord,
            HideSwearWords = HideSwearWords,
            Widescreen = (WidescreenMode)Math.Clamp(WidescreenIndex, 0, WidescreenChoices.Count - 1),
            ShowFrameRate = FrameRateAlwaysShown ? s.ShowFrameRate : ShowFrameRate,
            ShowInputs = ShowInputs,
            Rumble = Rumble,
            // (a list with nothing selected says -1: the setting stays what it is)
            InputDelay = InputDelayIndex is >= 0 and <= LauncherSettings.PlayersInputDelay ? InputDelayIndex : s.RaceInputDelay,
            VerticalSync = VerticalSyncIndex is 0 or 1 ? VerticalSyncIndex == 1 : s.VerticalSync,
            FullScreen = FullScreenIndex switch
            {
                0 => LauncherSettings.FullScreenBorderless,
                1 => LauncherSettings.FullScreenExclusive,
                _ => s.GameFullScreen,
            },
            RefreshRate = RefreshRateIndex >= 0 && RefreshRateIndex < RefreshRates.Count ? RefreshRates[RefreshRateIndex] : s.RefreshRate,
            GameVolume = Math.Clamp(GameVolume, 0, 100),
            SoundVolume = Math.Clamp(SoundVolume, 0, 100),
            SoundMuted = SoundMuted,
            NavigationSounds = NavigationSounds,
            PadNavigation = PadNavigation,
        });
    }

    /// <summary>What is typed as it is kept: trimmed, none when blank or longer than the settings hold.</summary>
    private static string? Kept(string value, int most) =>
        value.Trim() is { Length: > 0 } text && text.Length <= most && !text.Any(char.IsControl) ? text : null;
}

/// <summary>What the launcher says of a disc image, in one place for the first screen and the Settings page: a
/// label.</summary>
public static class DiscWords
{
    /// <param name="path">The image's path as typed or chosen.</param>
    public static string Of(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return DiscCheck.Check(path.Trim()) switch
        {
            DiscStatus.Ok => "Verified: F-Zero GX (USA)",
            DiscStatus.WrongGame => "Not F-Zero GX (USA)",
            DiscStatus.Unsupported => "Not a plain .iso or .gcm",
            _ => path.Trim().Length == 0 ? "No disc image" : "No file there",
        };
    }

    /// <summary>How a preparation that did not end ready is shown: a label, with a value where one helps (the file,
    /// the space needed). The preparation's whole sentence stays its tooltip and the launcher's notice.</summary>
    public static string Said(DiscPrepResult result, string path)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            DiscPrepOutcome.Cancelled => "Stopped · nothing kept",
            DiscPrepOutcome.ImageMissing => "No file there",
            DiscPrepOutcome.NotADisc => "Not a GameCube disc image",
            DiscPrepOutcome.UnsupportedFormat => Kind(path) == DiscImageKind.NKit ? "NKit image: not a full ISO" : "Not a plain .iso or .gcm",
            DiscPrepOutcome.WrongGame => "Not F-Zero GX (USA)",
            DiscPrepOutcome.WrongVersion => "Another revision of F-Zero GX (USA)",
            DiscPrepOutcome.ModifiedFile => result.File is { Length: > 0 } file ? $"Modified disc image · {file}" : "Modified disc image",
            DiscPrepOutcome.DamagedImage => "Disc image damaged",
            DiscPrepOutcome.NotEnoughSpace => $"Not enough space · {DiscPrep.Size(result.BytesNeeded)} needed",
            DiscPrepOutcome.Busy => "Being prepared in another window",
            DiscPrepOutcome.Failed => "Game files not written",
            _ => "",
        };
    }

    /// <summary>Game files found at the start that cannot be used as they are.</summary>
    public static string Said(PreparedStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.State switch
        {
            PreparedState.Damaged => status.File is { Length: > 0 } file ? $"Game files damaged · {file}" : "Game files damaged",
            PreparedState.Stale => "Game files from another version",
            _ => "",
        };
    }

    private static DiscImageKind Kind(string path)
    {
        try
        {
            return DiscImage.Kind(path.Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return DiscImageKind.Unknown;
        }
    }
}
