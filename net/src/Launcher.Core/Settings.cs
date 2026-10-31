using System.Text.Json.Serialization;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>Other racers' player-only sounds (boost, lean, the wreck alarm): the game's volume, 0.51 of it, or none
/// (as CPUs make in a single-player race). Engines always play.</summary>
public enum OtherSounds
{
    Full,
    Quieter,
    Off,
}

/// <summary>The game's picture in a race and a replay (the Settings page's "Widescreen"). <see cref="Off"/>: the
/// game's Normal, 4:3. <see cref="Game"/>: the game's own Wide Screen, 16:9, which shows a little more at the sides
/// than Normal and cuts the top and bottom. <see cref="TrueWide"/>: true widescreen, 16:9 with Normal's whole height
/// and the sides widened (the launch file's <c>"trueWidescreen"</c>; port/image/sites_view.py). For the picture
/// only: every choice races the same race.</summary>
public enum WidescreenMode
{
    Off,
    Game,
    TrueWide,
}

/// <summary>The Settings tab's four pages, in the order of their headings; the one last chosen is kept
/// (<see cref="LauncherSettings.SettingsTab"/>).</summary>
public enum SettingsTab
{
    Game,
    Online,
    Controller,
    Launcher,
}

/// <summary>How the player set up one machine in the garage: one of its four stock colours (StockColor 0-3, 0 its own
/// default) or, with StockColor null, <see cref="Color"/> painted on it; and its setting from 0 (acceleration) to 100
/// (top speed), used where the server lets players choose.</summary>
public sealed record MachineLook(int Machine, int? StockColor = 0, Rgb? Color = null, int Setting = 100);

/// <summary>
/// The player's racer: the name the game shows (<see cref="PlayerNames"/>), the machine last raced (0-40, the game's
/// list; the lobby's machine choice starts on it), and each machine's look from the garage (<see cref="Looks"/>;
/// a machine never set up is in its own stock colour). <see cref="Color"/>, <see cref="Setting"/> and
/// <see cref="StockColor"/> are the look of <see cref="Machine"/> as settings saved before the garage kept one per
/// machine; <see cref="LookOf"/> reads both. <see cref="Custom"/>: a garage-built machine the racer races in place
/// of <see cref="Machine"/> (sent with the queue and the machine choice; picked in the Garage's builder); null: the
/// stock machine. <see cref="Customs"/>: the machines saved in the builder, the one raced among them.
/// </summary>
public sealed record RacerProfile(string Name, int Machine, Rgb Color, int Setting = 100, int? StockColor = 0,
    IReadOnlyList<MachineLook>? Looks = null, CustomMachine? Custom = null, IReadOnlyList<CustomBuild>? Customs = null)
{
    /// <summary>The most machines the builder keeps.</summary>
    public const int MostCustoms = 12;

    public static RacerProfile Default { get; } = new("RACER", 0, new Rgb(232, 67, 46));

    /// <summary>The machine raced: <see cref="Custom"/> when set (and one the game can build), else
    /// <see cref="Machine"/>. Every page that names or sends the racer's machine reads this, never
    /// <see cref="Machine"/> alone.</summary>
    [JsonIgnore]
    public RacedMachine Raced => new(Machine, CustomMachine.Checked(Custom));

    /// <summary>A machine's look: from the garage, else (the machine last raced) the older single look, else its own
    /// stock colour with the racer's <see cref="Setting"/>.</summary>
    public MachineLook LookOf(int machine) =>
        Looks?.LastOrDefault(l => l.Machine == machine)
        ?? (machine == Machine ? new MachineLook(machine, StockColor, StockColor is null ? Color : null, Setting)
            : new MachineLook(machine, Setting: Setting));

    /// <summary>The profile with one machine's look set (replacing any earlier one).</summary>
    public RacerProfile WithLook(MachineLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        return this with { Looks = [.. (Looks ?? []).Where(l => l.Machine != look.Machine), look] };
    }

    /// <summary>The profile racing another machine, every machine keeping its look: the older single look stays with the
    /// machine it was for, and the new machine keeps the look it had.</summary>
    public RacerProfile WithMachine(int machine)
    {
        if (machine == Machine)
        {
            return this;
        }
        var next = LookOf(machine);
        var kept = Looks?.Any(l => l.Machine == Machine) == true ? this : WithLook(LookOf(Machine));
        return (kept.Looks!.Any(l => l.Machine == machine) ? kept : kept.WithLook(next)) with { Machine = machine };
    }

    public bool Equals(RacerProfile? other) =>
        other is not null && Name == other.Name && Machine == other.Machine && Color == other.Color &&
        Setting == other.Setting && StockColor == other.StockColor &&
        (Looks ?? []).SequenceEqual(other.Looks ?? []) && SameCustom(Custom, other.Custom) &&
        (Customs ?? []).SequenceEqual(other.Customs ?? []);

    private static bool SameCustom(CustomMachine? a, CustomMachine? b) =>
        a is null ? b is null : b is not null && a with { Colors = null } == b with { Colors = null }
            && (a.Colors ?? []).SequenceEqual(b.Colors ?? []) && (a.Colors is null) == (b.Colors is null);

    public override int GetHashCode() => HashCode.Combine(Name, Machine, Color, Setting, StockColor, Looks?.Count ?? 0);
}

/// <summary>Everything the launcher keeps between runs. Paths are local to this computer.</summary>
public sealed record LauncherSettings
{
    public RacerProfile Racer { get; init; } = RacerProfile.Default;

    /// <summary>The player's own F-Zero GX disc image (GFZE01).</summary>
    public string? DiscPath { get; init; }

    /// <summary>The fork's own user folder (settings, controller), never the player's Dolphin profile.</summary>
    public string? UserFolder { get; init; }

    /// <summary>Where the recompiled game is, for development builds; null: the launcher's own places
    /// (<see cref="RecompLaunch.Resolve"/>).</summary>
    public RecompSettings? Recomp { get; init; }

    /// <summary>Another coordinator for Quick Play (WebSocket address); null (blank in Settings): the official one,
    /// <see cref="OnlineServer"/>, unless there is none (<see cref="NoServer"/>).</summary>
    public string? Server { get; init; }

    /// <summary>The launcher's secret (<see cref="PlayerTags.NewKey"/>), sent in every hello: the server works the
    /// player's tag out from it, so a player never chooses or changes their tag. Made once
    /// (<see cref="WithPlayerKey"/>, when the launcher starts) and kept; never shown. No tag is saved: the one a
    /// server gives is for that connection. Settings saved by a launcher before this had a "tag" instead: it is
    /// passed over.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlayerKey { get; init; }

    /// <summary>These settings with a secret: their own when it is one (<see cref="PlayerTags.IsKey"/>), else a new
    /// one.</summary>
    public LauncherSettings WithPlayerKey() => PlayerTags.IsKey(PlayerKey) ? this : this with { PlayerKey = PlayerTags.NewKey() };

    /// <summary>No server is set: the stored settings, or the server's address in them, could not be read, so the
    /// server the player had is not known and the launcher contacts none (never the official
    /// one in its place). It stays so from start to start, until the player chooses a server on the Settings page (an
    /// address, or the official server) or puts back a settings file that can be read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoServer { get; init; }

    /// <summary>The coordinator Quick Play connects to and the launcher asks for its state: <see cref="Server"/>, or
    /// the official one when it is blank. Null: there is none to contact (<see cref="NoServer"/>).</summary>
    [JsonIgnore]
    public string? QuickPlayServer => ServerOn(StackStamp.Current);

    /// <summary><see cref="QuickPlayServer"/> on a build made for <paramref name="stack"/> (the connection box of a
    /// test names another stack than the build's).</summary>
    public string? ServerOn(StackStamp stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return NoServer ? null : string.IsNullOrWhiteSpace(Server) ? stack.Server : Server.Trim();
    }

    /// <summary>The official coordinator: the one of the server stack this build was made for
    /// (<see cref="StackStamp"/>); null for a build that names none, which has no official server.</summary>
    public static string? OnlineServer => StackStamp.Current.Server;

    /// <summary>Saved values that mean "the official server": the official address itself, and each one a
    /// launcher had before (written by launchers that stored the default: on any later build they still mean "this
    /// build's official server"); it loads as blank. The early local default
    /// (ws://127.0.0.1:47810/ws) is not one of them: a local address must never become the live service without the
    /// player's word, so it stays what the file says, a server of the player's own.</summary>
    public static readonly IReadOnlyList<string> FormerDefaultServers =
        [.. new[] { StackStamp.FirstServer, StackStamp.SecondServer, OnlineServer }.OfType<string>().Distinct()];

    /// <summary>Online races are recorded (by default). Practice runs are <see cref="RecordPractice"/>'s.</summary>
    public bool RecordReplays { get; init; } = true;

    /// <summary>Practice runs are recorded into the replay folder too (optional, off by
    /// default). Off: a practice's launch file names no replay, and the game writes none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RecordPractice { get; init; }

    public string? ReplayFolder { get; init; }

    public OtherSounds OtherSounds { get; init; } = OtherSounds.Quieter;

    /// <summary>The game's picture (<see cref="WidescreenMode"/>): its own 16:9 mode by default.
    /// A file of before true widescreen holds true or false here: true reads as the game's 16:9, false
    /// as Off (<see cref="SettingAsWritten.Widescreen"/>).</summary>
    [JsonConverter(typeof(SettingAsWritten.Widescreen))]
    public WidescreenMode Widescreen { get; init; } = WidescreenMode.Game;

    /// <summary>The game window's strip shows the frame rate (off by default). A test build and
    /// a test game show it whatever this says: <see cref="FrameRateShown"/>.</summary>
    public bool ShowFrameRate { get; init; }

    /// <summary>The controllers rumble in the game (the Settings page's "Rumble", Game; on by default).
    /// Off reaches the game's launch file as <c>"rumble": false</c> (on is the game's default, and a runner
    /// from before the key refuses the member), and the game then holds every motor still.
    /// On, a controller rumbles only while the player drives a seat of their own: never while spectating, in a replay
    /// or once their racer is out or over the line. The game's own option (its controller menu's Controller Rumble,
    /// "Set up in the game") still applies: off there is off too.</summary>
    public bool Rumble { get; init; } = true;

    /// <summary>The chase camera view (1 to 3) the player last raced in: the game says each view chosen with the
    /// camera button on its status board (<see cref="Board.BoardEventKind.Camera"/>), and the next race starts in it
    /// (the launch file's <c>match.camera</c>). 0: none yet, and the game starts in its own. The player's picture
    /// only: no other game of the race sees it.</summary>
    public int RaceCamera { get; init; }

    /// <summary>The view a race starts in: <see cref="RaceCamera"/> when it is one the game takes (1 to 3), else 0
    /// (the game's own, and the launch file holds no <c>match.camera</c>).</summary>
    public int StartCamera => RaceCamera is >= LaunchFormat.MatchCameraLow and <= LaunchFormat.MatchCameraHigh ? RaceCamera : 0;

    /// <summary>Whether the game window's strip shows the frame rate: the player's <see cref="ShowFrameRate"/>, and
    /// always on a test build (<paramref name="build"/>: a review or try copy) and in a test game
    /// (<paramref name="testGame"/>: a start whose launch file has test settings): the frame
    /// rate is shown in all tests. The settings file keeps the player's own value either way.</summary>
    public bool FrameRateShown(StackStamp build, bool testGame)
    {
        ArgumentNullException.ThrowIfNull(build);
        return ShowFrameRate || build.Test || testGame;
    }

    /// <summary>The practice last started (Play's Practice, START PRACTICE): its course and rules, which the page
    /// opens on; null: the first course and Quick Play's rules (<see cref="PracticeSetup.Fresh"/>). Its machine
    /// is not read back: the page's machine is the Garage's.</summary>
    [JsonConverter(typeof(SettingAsWritten.Whole<PracticeSetup>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PracticeSetup? Practice { get; init; }

    /// <summary>The Settings tab's page last chosen (Game, Online, Controller, Launcher): the tab opens on it.</summary>
    public SettingsTab SettingsTab { get; init; } = SettingsTab.Game;

    /// <summary>How large the launcher is drawn, in percent, on top of the system's own display scaling: 100 is the
    /// launcher at its own size. Settings, Launcher, "Interface scale" sets it with
    /// Apply; Ctrl+0, Ctrl+plus and Ctrl+minus anywhere in the launcher set it at once. Anything outside
    /// <see cref="LeastInterfaceScale"/> to <see cref="MostInterfaceScale"/> reads as 100 (<see cref="SettingsCheck"/>).</summary>
    [JsonConverter(typeof(SettingAsWritten.Whole))]
    public int InterfaceScale { get; init; } = DefaultInterfaceScale;

    public const int DefaultInterfaceScale = 100, LeastInterfaceScale = 50, MostInterfaceScale = 250, InterfaceScaleStep = 10;

    /// <summary>The scale the launcher is drawn at: <see cref="InterfaceScale"/> when it is one there is, else 100.</summary>
    [JsonIgnore]
    public int ShownInterfaceScale => InterfaceScale is >= LeastInterfaceScale and <= MostInterfaceScale ? InterfaceScale : DefaultInterfaceScale;

    /// <summary>The scale a step of <paramref name="steps"/> (Ctrl+plus: 1, Ctrl+minus: -1) from the shown one
    /// leads to, on the steps' grid and never past the ends.</summary>
    public int InterfaceScaleStepped(int steps)
    {
        var at = ShownInterfaceScale;
        var on = steps > 0
            ? ((at / InterfaceScaleStep) + 1) * InterfaceScaleStep                          // the next step up
            : ((at + InterfaceScaleStep - 1) / InterfaceScaleStep - 1) * InterfaceScaleStep;  // the next step down
        var to = steps == 0 ? at : on + ((steps - Math.Sign(steps)) * InterfaceScaleStep);
        return Math.Clamp(to, LeastInterfaceScale, MostInterfaceScale);
    }

    public bool AutoUpdate { get; init; } = true;

    /// <summary>The order the Garage shows the machines in: by number, or by a stat (<see cref="MachineStat"/>, written
    /// by its name), rising or falling.</summary>
    public MachineSort GarageSort { get; init; } = MachineSort.ByNumber;

    /// <summary>The machines the player marked with the heart (ids, in the order marked); null: none yet.</summary>
    public IReadOnlyList<int>? FavouriteMachines { get; init; }

    /// <summary>The Garage opens on its Favourites shelf (the marked machines only): the shelf last left there.</summary>
    public bool FavouritesOnly { get; init; }

    /// <summary>The stat pack the Garage's Stats choice shows outside a lobby, by name; null: the disc's own stats.</summary>
    public string? GarageStats { get; init; }

    /// <summary>How loud the launcher's own sounds are (<see cref="LauncherSound"/>: the lobby's, an urgent notice's, the navigation's), 0 to 100.</summary>
    public int SoundVolume { get; init; } = DefaultSoundVolume;

    /// <summary>The launcher's own sounds are off; the volume is kept for when they are on again.</summary>
    public bool SoundMuted { get; init; }

    /// <summary>The game's menu sounds as the pad or the keyboard moves between controls (the cursor) and on the
    /// pad's B (back); never for the mouse (the Settings page's "Navigation sounds", Launcher; off by default:
    /// minimal sounds). Muted and at the volume as the launcher's other sounds.</summary>
    public bool NavigationSounds { get; init; }

    /// <summary>The pad works the launcher's pages (the Settings page's "Controller navigation", Launcher; off by default: a
    /// controller left in the window does nothing to it). Setting a control on the Controller page reads the pad either way.</summary>
    public bool PadNavigation { get; init; }

    /// <summary>The server's notices come without their tone and the taskbar's flash (the Settings page's "Notices":
    /// Silent: players can mute notifications). They still show in the chat, the banner and
    /// the bell's list.</summary>
    public bool NoticesSilent { get; init; }

    /// <summary>Discord's status shows what the player does in the launcher (the Settings page's "Show on Discord",
    /// Online; on unless the player switches it off). Off clears the status at once
    /// (<see cref="DiscordPresence"/>).</summary>
    public bool ShowOnDiscord { get; init; } = true;

    /// <summary>The chat's swear words are masked (****) in the lines this player receives (the Settings page's
    /// "Hide swear words", Online; on unless the player switches it off). The server's
    /// always-blocked words never arrive from a server that filters; they are masked too (ChatMask).</summary>
    public bool HideSwearWords { get; init; } = true;

    public const int DefaultSoundVolume = 50;

    /// <summary>How loud the game is, 0 to 100: the emulator's own volume, written into the game's settings at every
    /// start the launcher makes (<see cref="RecompLaunch.PrepareUserFolder"/>). 100 is the game as it was before
    /// the setting.</summary>
    public int GameVolume { get; init; } = 100;

    /// <summary>This game's input delay in a race, in frames: the player's own choice on the Settings page, 0, 1 or 2
    /// (2 unless the player picks less, never more; nothing picks for the player and nothing
    /// takes a choice back). Anything else in the file reads as 2, and costs that one setting
    /// (<see cref="SettingsCheck"/>, <see cref="SettingAsWritten"/>). It is this game's alone: the other games
    /// of a race are not told and are not changed by it; with a lower delay this game's own inputs act sooner and
    /// its own rollbacks go deeper. The game names it in its log and in the race's summary line (<c>delay=</c>).</summary>
    [JsonConverter(typeof(SettingAsWritten.Whole))]
    public int InputDelay { get; init; } = PlayersInputDelay;

    /// <summary>The input delay a race has unless its player picked less, and the most there is.</summary>
    public const int PlayersInputDelay = 2;

    /// <summary>The input delay a race is started with: <see cref="InputDelay"/> held to 0 to
    /// <see cref="PlayersInputDelay"/>, whoever made these settings.</summary>
    [JsonIgnore]
    public int RaceInputDelay => InputDelay is >= 0 and <= PlayersInputDelay ? InputDelay : PlayersInputDelay;

    /// <summary>When the game hands a picture to the screen (the launch file's <c>"present"</c>); no page shows it.
    /// <c>"console"</c>: at the time a console puts the picture's first line out (every
    /// player's game from 0.2.1 on). The two other ways are kept for comparison and written by hand:
    /// <c>"field"</c>, as it was up to 0.2.0, some milliseconds after a console would; <c>"copy"</c>, when the game
    /// copies the picture out, sooner than a console. Anything else in the file reads as <c>"console"</c>
    /// (<see cref="SettingsCheck"/>). The game names it in its log and in the race's summary line (<c>present=</c>).
    /// 0.2.3's first build gave every game <c>"field"</c>: the runner of 0.2.1 and 0.2.2 waited for a console's
    /// moment inside its GPU thread's pass over the game's commands, which raced with the game's switch of command
    /// buffers ("FIFO: Unknown Opcode", race m23). The runner of 0.2.3 waits after the pass, and the console is back.</summary>
    [JsonConverter(typeof(SettingAsWritten.Words))]
    public string Present { get; init; } = PresentAsAConsole;

    /// <summary>The three ways of showing a picture a settings file can name; the first is every player's.</summary>
    public const string PresentAsAConsole = "console", PresentAsBefore = "field", PresentAtTheCopy = "copy";

    /// <summary>What a game is started with: <see cref="Present"/> when it is one of the two ways kept for
    /// comparison, else null (the launch file then says nothing of it: the game's own way is a console's).</summary>
    [JsonIgnore]
    public string? GamePresent => Present is PresentAsBefore or PresentAtTheCopy ? Present : null;

    /// <summary>Pictures a second in a race or a replay (the launch file's <c>"refreshRate"</c>; the Settings page's "Refresh rate").
    /// <c>"60"</c>: the game's own picture a step, every player's. <c>"120"</c>, <c>"240"</c>, <c>"display"</c> (the
    /// display's own rate): frames drawn between two steps, machines and cameras between where they were and where
    /// they are, each shown at its time; the simulation keeps the console's clock. Anything else reads as
    /// <c>"60"</c> (<see cref="SettingsCheck"/>).</summary>
    [JsonConverter(typeof(SettingAsWritten.Words))]
    public string RefreshRate { get; init; } = RefreshAsTheGame;

    /// <summary>The refresh rates a settings file can name; the first is every player's.</summary>
    public const string RefreshAsTheGame = "60", Refresh120 = "120", Refresh240 = "240", RefreshAsTheDisplay = "display";

    /// <summary>What a game is started with: <see cref="RefreshRate"/> when it draws frames between steps, else null
    /// (the launch file then says nothing of it).</summary>
    [JsonIgnore]
    public string? GameRefreshRate => RefreshRate is Refresh120 or Refresh240 or RefreshAsTheDisplay ? RefreshRate : null;

    /// <summary>The game's pictures wait for the display's refresh (off by default; the Settings page's "Vertical
    /// sync"). Off suits a display with variable refresh and a window the system composes; on is for a display of
    /// a fixed rate in a mode that tears. It reaches the game as its launch file's <c>"verticalSync"</c>.</summary>
    [JsonConverter(typeof(SettingAsWritten.Flag))]
    public bool VerticalSync { get; init; }

    /// <summary>What the game's full screen is (the Settings page's "Full screen mode"; the launch file's
    /// <c>"fullScreen"</c>). <c>"borderless"</c>: the window without a frame, the size of its monitor, as up to 0.2.0.
    /// <c>"exclusive"</c>: on top of that the graphics driver's own full screen while the game has the focus, which
    /// a display with G-Sync or FreeSync follows with the drivers' usual settings; a display of a fixed rate tears
    /// in it unless vertical sync is on. Anything else in the file reads as <see cref="FullScreenDefault"/>.</summary>
    [JsonConverter(typeof(SettingAsWritten.Words))]
    public string FullScreen { get; init; } = FullScreenDefault;

    public const string FullScreenBorderless = "borderless", FullScreenExclusive = "exclusive";

    /// <summary>The full screen a player has until they choose: THE ONE PLACE that says so (the
    /// game itself has no default of its own choosing: a launch file without the key is the borderless window,
    /// as 0.2.0's was, and this launcher always writes the key).</summary>
    public const string FullScreenDefault = FullScreenBorderless;

    /// <summary>What a game is started with: <see cref="FullScreen"/> when it is one of the two, else the default.</summary>
    [JsonIgnore]
    public string GameFullScreen => FullScreen is FullScreenBorderless or FullScreenExclusive ? FullScreen : FullScreenDefault;
}
