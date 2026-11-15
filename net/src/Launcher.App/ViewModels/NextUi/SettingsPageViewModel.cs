using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>What the Settings page asks its view for: a file or a folder picked by the system's picker.</summary>
public enum SettingsPick
{
    Disc,
    ReplayFolder,
    OlderCopy,
}

/// <summary>A group of settings as Settings' first step lists it: its name, what is in it, and its rows.</summary>
public sealed record SettingsCategory(string Title, string Line, IReadOnlyList<OptionGroup> Groups);

/// <summary>
/// The redesigned Settings (Views/NextUi/SettingsPageView), in steps as the game's own options are: first the groups
/// (Basics, then Display, Graphics, Sound, Controller, Online, Social, Launcher, Replays), each with what is in it; A
/// opens one, and its option rows show, with what the row the focus is on does on the right; B goes back to the
/// groups, onto the one it came from. Left and right change a value, A opens or switches; every change holds at once,
/// as before. Every setting of the classic pages is in a group, and every row works the classic page's own view model
/// (<see cref="SettingsViewModel"/>), so the two interfaces keep one set of settings.
/// </summary>
public sealed partial class SettingsPageViewModel : ViewModelBase, IDisposable
{
    private readonly MainViewModel _main;
    private readonly ShellViewModel _shell;
    private readonly AppState _state;
    private readonly List<OptionRow> _rows = [];

    public SettingsPageViewModel(MainViewModel main, ShellViewModel shell, AppState state)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(state);
        (_main, _shell, _state) = (main, shell, state);
        var s = main.Settings;
        Basic = [Group("", "Basics",
            Number("Volume", () => s.GameVolume, v => s.GameVolume = v, 0, 100, 5, "%"),
            Choice("Graphics", () => GraphicsPresets.Offered(s), () => GraphicsPresets.Index(s), i => GraphicsPresets.Apply(s, i)),
            Choice("Full screen mode", () => SettingsViewModel.FullScreenChoices, () => s.FullScreenIndex, i => s.FullScreenIndex = i),
            Choice("Input delay", () => DelayWords, () => s.InputDelayIndex, i => s.InputDelayIndex = i),
            Toggle("Rumble", () => s.Rumble, v => s.Rumble = v),
            Act("Controller", () => "Set up", () => main.Current = main.Controller))];
        More =
        [
            Group("Display", "Display",
                Choice("Widescreen", () => SettingsViewModel.WidescreenChoices, () => s.WidescreenIndex, i => s.WidescreenIndex = i),
                Toggle("Vertical sync", () => s.VerticalSyncIndex == 1, v => s.VerticalSyncIndex = v ? 1 : 0),
                Choice("High refresh", () => SettingsViewModel.AuroraHighRefreshChoices, () => s.AuroraHighRefreshIndex, i => s.AuroraHighRefreshIndex = i),
                Toggle("Show frame rate", () => s.ShowFrameRate, v => s.ShowFrameRate = v, () => !s.FrameRateAlwaysShown,
                    () => s.FrameRateAlwaysShown ? "A test build always shows it." : "")),
            Group("Graphics", "Graphics",
                Choice("Resolution", () => SettingsViewModel.AuroraSizeChoices, () => s.AuroraSizeIndex, i => s.AuroraSizeIndex = i),
                Choice("Anti-aliasing", () => SettingsViewModel.AuroraEdgesChoices, () => s.AuroraEdgesIndex, i => s.AuroraEdgesIndex = i),
                Choice("Anisotropic filtering", () => SettingsViewModel.AuroraTexturesChoices, () => s.AuroraTexturesIndex, i => s.AuroraTexturesIndex = i),
                Toggle("Ray tracing", () => s.RayTracing, v => s.RayTracing = v, () => s.RayTracingPossible || s.RayTracing,
                    () => s.RayTracingReason.Length > 0 ? s.RayTracingReason : "Experimental"),
                Choice("Colourblind filter", () => SettingsViewModel.AuroraColourblindChoices, () => s.AuroraColourblindIndex, i => s.AuroraColourblindIndex = i)),
            Group("Sound", "Sound",
                Number("Music and game sounds", () => s.GameVolume, v => s.GameVolume = v, 0, 100, 5, "%"),
                Choice("Other racers' sounds", () => SettingsViewModel.OtherSoundsChoices, () => s.OtherSoundsIndex, i => s.OtherSoundsIndex = i),
                Number("Launcher sounds", () => s.SoundVolume, v => s.SoundVolume = v, 0, 100, 5, "%"),
                Number("Menu sounds", () => s.MenuSoundVolume, v => s.MenuSoundVolume = v, 0, 100, 5, "%",
                    note: () => s.PadNavigation ? "" : "The cursor and back sound only with Controller navigation on."),
                Number("Notifications", () => s.NoticeSoundVolume, v => s.NoticeSoundVolume = v, 0, 100, 5, "%"),
                Number("Other sounds", () => s.OtherSoundVolume, v => s.OtherSoundVolume = v, 0, 100, 5, "%"),
                Toggle("Mute launcher sounds", () => s.SoundMuted, v => s.SoundMuted = v)),
            Group("Controller", "Controller",
                Toggle("Show inputs", () => s.ShowInputs, v => s.ShowInputs = v),
                Toggle("Controller navigation", () => s.PadNavigation, v => s.PadNavigation = v),
                Choice("Button icons", () => SettingsViewModel.ButtonIconsChoices, () => s.ButtonIconsIndex, i => s.ButtonIconsIndex = i),
                Choice("Confirm button", () => SettingsViewModel.ConfirmButtonChoices, () => s.ConfirmButtonIndex, i => s.ConfirmButtonIndex = i)),
            Group("Online", "Online",
                Act("Your name", () => main.Account.Name, TypeName),
                Act("Server", ServerWords, TypeServer, note: () => s.ServerProblem ? "That is no server's address." : ""),
                Act("Use the official server", () => OnOfficial() ? "In use" : "", () => s.UseOfficialServerCommand.Execute(null), () => !OnOfficial()),
                Act("Account", () => "Sign in with Discord · later", () => { }, () => false)),
            Group("Social", "Social",
                Choice("Notices", () => SettingsViewModel.NoticesChoices, () => s.NoticesIndex, i => s.NoticesIndex = i),
                Toggle("Show on Discord", () => s.ShowOnDiscord, v => s.ShowOnDiscord = v),
                Toggle("Hide swear words", () => s.HideSwearWords, v => s.HideSwearWords = v)),
            Group("Launcher", "Launcher",
                Toggle("Experimental UI", () => s.ExperimentalUi, v => s.ExperimentalUi = v, note: () => "Off brings back the classic launcher"),
                Toggle("Developer mode", () => s.DeveloperMode, v => s.DeveloperMode = v),
                Act("Disc image", () => s.DiscPath.Length > 0 ? Path.GetFileName(s.DiscPath) : "None", () => Picking?.Invoke(SettingsPick.Disc),
                    note: () => s.DiscStatusText),
                Act("Game files", () => PrepareSheetViewModel.Words(state), shell.OpenPrepare, () => PrepareSheetViewModel.Wanted(state),
                    () => PrepareSheetViewModel.Wanted(state) ? "Press to prepare them from your disc image." : ""),
                Toggle("Update automatically", () => s.AutoUpdate, v => s.AutoUpdate = v),
                Act("Updates", () => main.Update.ActionText, () => main.Update.ActCommand.Execute(null), () => main.Update.CanAct,
                    () => main.Update.SettingsLine),
                Act("Bring over an older copy", () => s.Move.Moving ? "Copying" : "", () => Picking?.Invoke(SettingsPick.OlderCopy),
                    () => s.Move.Offered && s.Move.Idle, () => s.Move.Text),
                Toggle("Reduced motion", () => s.ReducedMotion, v => s.ReducedMotion = v),
                Number("Interface scale", () => s.PendingScale, v => s.PendingScale = v, LauncherSettings.LeastInterfaceScale,
                    LauncherSettings.MostInterfaceScale, LauncherSettings.InterfaceScaleStep, "%",
                    note: () => s.CanApplyScale ? "Apply it with the row below." : ""),
                Act("Apply the interface scale", () => s.CanApplyScale ? $"{s.PendingScale}%" : "", () => s.ApplyScaleCommand.Execute(null),
                    () => s.CanApplyScale)),
            Group("Replays", "Replays",
                Toggle("Record races", () => s.RecordReplays, v => s.RecordReplays = v),
                Toggle("Record practice", () => s.RecordPractice, v => s.RecordPractice = v),
                Act("Replay folder", () => s.ReplayFolder.Length > 0 ? s.ReplayFolder : "The launcher's own", () => Picking?.Invoke(SettingsPick.ReplayFolder),
                    note: () => s.FolderNotice),
                Act("Open the replay folder", () => "", () => s.OpenReplayFolderCommand.Execute(null))),
        ];
        Categories =
        [
            new SettingsCategory("Basics", "Volume, graphics, full screen, input delay, rumble and the controller", Basic),
            .. More.Select(g => new SettingsCategory(g.Heading, Lines.TryGetValue(g.Heading, out var line) ? line : "", [g with { Heading = "" }])),
        ];
        Focused = Basic[0].Items[0];
        Info = SettingInfo.Of(Categories[0]);
        s.PropertyChanged += OnSettings;
        main.Account.PropertyChanged += OnOther;
        main.Update.PropertyChanged += OnOther;
        s.Move.PropertyChanged += OnOther;
        state.Changed += OnState;
    }

    /// <summary>The input delay's values in words.</summary>
    private static IReadOnlyList<string> DelayWords { get; } = [.. SettingsViewModel.InputDelayChoices.Select(c => c == "1" ? "1 frame" : $"{c} frames")];

    /// <summary>The six rows first.</summary>
    public IReadOnlyList<OptionGroup> Basic { get; }

    /// <summary>Every other setting, in its group.</summary>
    public IReadOnlyList<OptionGroup> More { get; }

    /// <summary>The first step: the groups, the basic rows first.</summary>
    public IReadOnlyList<SettingsCategory> Categories { get; }

    /// <summary>What each group holds, in a line.</summary>
    private static readonly Dictionary<string, string> Lines = new(StringComparer.Ordinal)
    {
        ["Display"] = "Widescreen, vertical sync, high refresh and the frame rate",
        ["Graphics"] = "Resolution, anti-aliasing, filtering, ray tracing, colourblind filter",
        ["Sound"] = "The game's music and sounds; the launcher's menu, notifications and others",
        ["Controller"] = "Inputs on screen, controller navigation, button icons, the confirm button",
        ["Online"] = "Your name, the server, your account",
        ["Social"] = "Notices, Discord and the chat",
        ["Launcher"] = "Disc image, game files, updates, the interface and its scale",
        ["Replays"] = "Recording races and practice, the replay folder",
    };

    /// <summary>The group open (its rows show); null: the groups show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(OpenGroups), nameof(Title))]
    public partial SettingsCategory? Open { get; private set; }

    public bool IsOpen => Open is not null;

    partial void OnOpenChanged(SettingsCategory? value)
    {
        _shell.Stepped();   // (its way back and its hints)
        _onCategory = value is null ? LastOpen : _onCategory;
        Info = value is null ? SettingInfo.Of(LastOpen ?? Categories[0])
            : Focused is { } row ? SettingInfo.Of(row, value.Title) : SettingInfo.Of(value);
    }

    /// <summary>What the window on the right says: of the group or the row the focus is on.</summary>
    [ObservableProperty]
    public partial SettingInfo Info { get; private set; }

    /// <summary>The focus (the pointer's or the pad's) on a group of the first step: the window says what is in it.</summary>
    public void On(SettingsCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        _onCategory = category;
        if (!IsOpen)
        {
            Info = SettingInfo.Of(category);
        }
    }

    private SettingsCategory? _onCategory;   // the group the focus was last on among the groups

    /// <summary>The window says it again (a setting changed: the row's choice marked, a meter, a group's values).</summary>
    private void Reinform() => Info = Open is { } open && Focused is { } row ? SettingInfo.Of(row, open.Title)
        : SettingInfo.Of(_onCategory ?? LastOpen ?? Categories[0]);

    partial void OnFocusedChanged(OptionItem? value)
    {
        if (Open is { } open && value is not null)
        {
            Info = SettingInfo.Of(value, open.Title);
        }
    }

    /// <summary>The rows of the group open.</summary>
    public IReadOnlyList<OptionGroup> OpenGroups => Open?.Groups ?? [];

    /// <summary>The page's title: SETTINGS, or the group open.</summary>
    public string Title => Open?.Title ?? "Settings";

    /// <summary>The group open last (B lands on it among the groups).</summary>
    public SettingsCategory? LastOpen { get; private set; }

    /// <summary>A group pressed: its rows, the focus on the first.</summary>
    [RelayCommand]
    public void OpenCategory(SettingsCategory? category)
    {
        if (category is null)
        {
            return;
        }
        (Open, LastOpen) = (category, category);
        Focused = category.Groups[0].Items[0];
    }

    /// <summary>The group of that name opened ("Launcher" for an update; none of that name: the groups).</summary>
    public void ShowCategory(string title) => OpenCategory(Categories.FirstOrDefault(c => c.Title == title));

    /// <summary>B in a group: the groups again. False: none was open.</summary>
    public bool CloseCategory()
    {
        if (Open is null)
        {
            return false;
        }
        Open = null;
        return true;
    }

    /// <summary>The row the focus is on (the help panel's).</summary>
    [ObservableProperty]
    public partial OptionItem? Focused { get; set; }

    /// <summary>A row asks the view for a file or a folder (the system's picker is a view's).</summary>
    public event Action<SettingsPick>? Picking;

    /// <summary>What the view's picker gave.</summary>
    public async Task PickedAsync(SettingsPick pick, string path)
    {
        var s = _main.Settings;
        switch (pick)
        {
            case SettingsPick.Disc:
                s.DiscPath = path;
                break;
            case SettingsPick.ReplayFolder:
                s.ReplayFolder = path;
                break;
            case SettingsPick.OlderCopy:
                await s.Move.BringOverAsync(path).ConfigureAwait(true);
                break;
        }
        ShowAll();
    }

    public void Dispose()
    {
        _main.Settings.PropertyChanged -= OnSettings;
        _main.Account.PropertyChanged -= OnOther;
        _main.Update.PropertyChanged -= OnOther;
        _main.Settings.Move.PropertyChanged -= OnOther;
        _state.Changed -= OnState;
    }

    /// <summary>"Your name": the keyboard on the screen, the name saved as the game shows it.</summary>
    private void TypeName()
    {
        var account = _main.Account;
        _shell.OpenKeyboard("Your name", account.Name, PlayerNames.MaxLength, false, text =>
        {
            account.Draft = text;
            account.SaveName();
            ShowAll();
        });
    }

    /// <summary>"Server": the keyboard on the screen; Done sets what was typed (blank: no server, or the official one
    /// for a player on it), as Enter in the classic page's box.</summary>
    private void TypeServer()
    {
        var s = _main.Settings;
        _shell.OpenKeyboard("Server address", s.Server, 120, false, text =>
        {
            s.Server = text.Trim();
            s.CommitServerCommand.Execute(null);
            ShowAll();
        });
    }

    /// <summary>The official server is the one set (no address, and a server at all).</summary>
    private bool OnOfficial() => !_main.Settings.NoServer && _main.Settings.Server.Trim().Length == 0;

    private string ServerWords()
    {
        var s = _main.Settings;
        return s.Server.Trim().Length > 0 ? s.Server.Trim() : s.NoServer ? "No server" : "Official server";
    }

    private void OnSettings(object? sender, PropertyChangedEventArgs e) => ShowAll();

    private void OnOther(object? sender, PropertyChangedEventArgs e) => ShowAll();

    /// <summary>The state changed (the game's files prepared, a look found them missing): the rows read again.</summary>
    private void OnState() => OnUi(ShowAll);

    /// <summary>Every row reads its value again (one setting can change another's: the preset, the scale).</summary>
    private void ShowAll()
    {
        foreach (var row in _rows)
        {
            row.Show();
        }
        Reinform();
    }

    private OptionGroup Group(string heading, string eyebrow, params OptionRow[] rows)
    {
        _rows.AddRange(rows);
        foreach (var row in rows)
        {
            row.Show();
        }
        return new OptionGroup(heading, [.. rows.Select(r => new OptionItem(r, eyebrow))]);
    }

    private static ChoiceRow Choice(string label, Func<IReadOnlyList<string>> options, Func<int> get, Action<int> set,
        Func<bool>? enabled = null, Func<string>? note = null) => new(label, options, get, set, enabled, note);

    private static ToggleRow Toggle(string label, Func<bool> get, Action<bool> set, Func<bool>? enabled = null, Func<string>? note = null) =>
        new(label, get, set, enabled, note);

    private static NumberRow Number(string label, Func<int> get, Action<int> set, int least, int most, int step, string unit,
        Func<bool>? enabled = null, Func<string>? note = null) => new(label, get, set, least, most, step, unit, enabled, note);

    private static ActionRow Act(string label, Func<string> value, Action act, Func<bool>? enabled = null, Func<string>? note = null) =>
        new(label, value, act, enabled, note);
}

/// <summary>
/// The Settings page's "Graphics": one choice that sets the resolution, the anti-aliasing and the anisotropic filtering
/// together (owner 2026-11-14): Performance 2x, Off, Off; Balanced Auto, Off, Off (the launcher's default, so a player
/// who never changed them sees Balanced); Quality Auto, 4x MSAA, 16x. Any other mix is "Custom".
/// </summary>
public static class GraphicsPresets
{
    /// <summary>Each preset's places in the three lists (resolution, anti-aliasing, anisotropic filtering).</summary>
    private static readonly (int Size, int Edges, int Textures)[] Sets = [(1, 0, 0), (0, 0, 0), (0, 1, 3)];

    /// <summary>The presets, then Custom.</summary>
    public static IReadOnlyList<string> Words { get; } = ["Performance", "Balanced", "Quality", "Custom"];

    /// <summary>The choices offered: the three presets, and Custom while the settings make none of them.</summary>
    public static IReadOnlyList<string> Offered(SettingsViewModel settings) => Index(settings) < Sets.Length ? [.. Words.Take(Sets.Length)] : Words;

    /// <summary>The preset the three settings make (3: Custom).</summary>
    public static int Index(SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var at = Array.IndexOf(Sets, (settings.AuroraSizeIndex, settings.AuroraEdgesIndex, settings.AuroraTexturesIndex));
        return at < 0 ? Sets.Length : at;
    }

    /// <summary>A preset chosen: its three settings (Custom chosen: nothing changes).</summary>
    public static void Apply(SettingsViewModel settings, int preset)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (preset < 0 || preset >= Sets.Length)
        {
            return;
        }
        var (size, edges, textures) = Sets[preset];
        (settings.AuroraSizeIndex, settings.AuroraEdgesIndex, settings.AuroraTexturesIndex) = (size, edges, textures);
    }
}
