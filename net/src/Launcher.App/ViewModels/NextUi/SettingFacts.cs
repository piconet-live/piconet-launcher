namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// What the Settings window says of a setting beyond its line (the owner, 2026-11-15: the window was nearly empty):
/// when a change takes effect, what it applies to, and something good to know; and for a group what it is about. By
/// the row's label (a row not named here says nothing more).
/// </summary>
public static class SettingFacts
{
    private const string AtOnce = "At once";
    private const string NextStart = "The game's next start";
    private const string NextRace = "Your next online race";
    private const string Launcher = "The launcher";
    private const string Game = "The game";

    /// <summary>When a change takes effect.</summary>
    private static readonly Dictionary<string, string> When = new(StringComparer.Ordinal)
    {
        ["Volume"] = NextStart,
        ["Graphics"] = NextStart,
        ["Full screen mode"] = NextStart,
        ["Input delay"] = NextRace,
        ["Rumble"] = NextStart,
        ["Controller"] = AtOnce,
        ["Widescreen"] = NextStart,
        ["Vertical sync"] = NextStart,
        ["High refresh"] = NextStart,
        ["Show frame rate"] = NextStart,
        ["Resolution"] = NextStart,
        ["Anti-aliasing"] = NextStart,
        ["Anisotropic filtering"] = NextStart,
        ["Ray tracing"] = NextStart,
        ["Colourblind filter"] = NextStart,
        ["Music and game sounds"] = NextStart,
        ["Other racers' sounds"] = NextRace,
        ["Launcher sounds"] = AtOnce,
        ["Menu sounds"] = AtOnce,
        ["Notifications"] = AtOnce,
        ["Other sounds"] = AtOnce,
        ["Mute launcher sounds"] = AtOnce,
        ["Show inputs"] = NextStart,
        ["Controller navigation"] = AtOnce,
        ["Button icons"] = AtOnce,
        ["Confirm button"] = AtOnce,
        ["Your name"] = AtOnce,
        ["Server"] = "Your next join",
        ["Use the official server"] = "Your next join",
        ["Account"] = "A later version",
        ["Notices"] = AtOnce,
        ["Show on Discord"] = AtOnce,
        ["Hide swear words"] = "The chat's next lines",
        ["Experimental UI"] = AtOnce,
        ["Developer mode"] = AtOnce,
        ["Disc image"] = AtOnce,
        ["Game files"] = AtOnce,
        ["Update automatically"] = "The launcher's next start",
        ["Updates"] = AtOnce,
        ["Bring over an older copy"] = AtOnce,
        ["Reduced motion"] = AtOnce,
        ["Interface scale"] = "Once it is applied",
        ["Apply the interface scale"] = AtOnce,
        ["Record races"] = "Your next race",
        ["Record practice"] = "Your next practice",
        ["Replay folder"] = "The next replay",
        ["Open the replay folder"] = AtOnce,
    };

    /// <summary>What a change applies to: the game, the launcher, online races, a controller.</summary>
    private static readonly Dictionary<string, string> Where = new(StringComparer.Ordinal)
    {
        ["Volume"] = Game,
        ["Graphics"] = Game,
        ["Full screen mode"] = Game,
        ["Input delay"] = "Online races",
        ["Rumble"] = "Your controller in the game",
        ["Controller"] = "Your controller in the game",
        ["Widescreen"] = Game,
        ["Vertical sync"] = Game,
        ["High refresh"] = Game,
        ["Show frame rate"] = Game,
        ["Resolution"] = Game,
        ["Anti-aliasing"] = Game,
        ["Anisotropic filtering"] = Game,
        ["Ray tracing"] = Game,
        ["Colourblind filter"] = Game,
        ["Music and game sounds"] = Game,
        ["Other racers' sounds"] = "Online races and replays",
        ["Launcher sounds"] = Launcher,
        ["Menu sounds"] = Launcher,
        ["Notifications"] = Launcher,
        ["Other sounds"] = Launcher,
        ["Mute launcher sounds"] = Launcher,
        ["Show inputs"] = Game,
        ["Controller navigation"] = Launcher,
        ["Button icons"] = "The launcher's hints",
        ["Confirm button"] = Launcher,
        ["Your name"] = "Online races and the chat",
        ["Server"] = "Quick Play and custom lobbies",
        ["Use the official server"] = "Quick Play and custom lobbies",
        ["Account"] = "Online",
        ["Notices"] = Launcher,
        ["Show on Discord"] = "Your Discord status",
        ["Hide swear words"] = "The chat",
        ["Experimental UI"] = Launcher,
        ["Developer mode"] = Launcher,
        ["Disc image"] = Game,
        ["Game files"] = Game,
        ["Update automatically"] = Launcher,
        ["Updates"] = Launcher,
        ["Bring over an older copy"] = "Settings, replays and game files",
        ["Reduced motion"] = Launcher,
        ["Interface scale"] = Launcher,
        ["Apply the interface scale"] = Launcher,
        ["Record races"] = "Online races",
        ["Record practice"] = "Practice runs",
        ["Replay folder"] = "Replays",
        ["Open the replay folder"] = "Replays",
    };

    /// <summary>Something good to know about the setting.</summary>
    private static readonly Dictionary<string, string> Tips = new(StringComparer.Ordinal)
    {
        ["Volume"] = "The game starts at this volume every time, its music and its race sounds together.",
        ["Graphics"] = "Change resolution, anti-aliasing or filtering one by one in the Graphics group and this says Custom.",
        ["Full screen mode"] = "Borderless suits switching to other windows and streaming. Exclusive gives the screen to the game alone.",
        ["Input delay"] = "Fewer frames feel more direct but need a steady connection. 2 frames is the safe choice.",
        ["Rumble"] = "Needs a controller that can rumble. Test rumble on the Controller page tries it.",
        ["Controller"] = "Set the port, the device and every button there. Your controller works the launcher too, with Controller navigation on.",
        ["Widescreen"] = "Off is the game's own 4:3 picture.",
        ["Vertical sync"] = "Off gives the lowest delay; the picture may tear across.",
        ["High refresh"] = "Only matters on a screen above 60 Hz. The race itself stays at 60 steps a second for everyone.",
        ["Show frame rate"] = "Handy to see what a graphics change costs.",
        ["Resolution"] = "Each step up asks more of your graphics card. Lower it first if races stutter.",
        ["Anti-aliasing"] = "More samples, smoother edges, more work for the graphics card.",
        ["Anisotropic filtering"] = "Costs little on most graphics cards: worth keeping high.",
        ["Ray tracing"] = "The first start with it on takes longer while the game prepares its graphics.",
        ["Colourblind filter"] = "Changes the game's picture only, not the launcher.",
        ["Music and game sounds"] = "The game mixes its music and its sounds itself: the launcher can set them only together. The same as Volume under Basics.",
        ["Other racers' sounds"] = "Quieter keeps the race readable with 30 machines around you.",
        ["Launcher sounds"] = "The launcher plays no music of its own. Mute keeps this volume for later.",
        ["Menu sounds"] = "Pressing a big button (Quick Play, More's cards, a group here) sounds the game's confirm.",
        ["Notifications"] = "Heard even while the game or another window is in front, so you do not miss your turn.",
        ["Other sounds"] = "The draw's ticks are heard only while the launcher's window is in front.",
        ["Mute launcher sounds"] = "The game's own sounds are not touched.",
        ["Show inputs"] = "Useful to check a worn stick or a button that does not answer.",
        ["Controller navigation"] = "The game gets the controller back while it runs.",
        ["Button icons"] = "Auto picks the icons of the controller you hold.",
        ["Confirm button"] = "Nintendo controllers confirm with the right button.",
        ["Your name"] = "Up to the game's own length; other racers see it in the lobby and the race.",
        ["Server"] = "A friend's server is an address such as wss://example.org/ws.",
        ["Use the official server"] = "Your own servers stay on Quick Play's list.",
        ["Account"] = "Until then your name and your replays live on this PC.",
        ["Notices"] = "Silent notices still show in the bell's list.",
        ["Show on Discord"] = "Shows whether you are in a lobby, racing, watching or practising, and the course.",
        ["Hide swear words"] = "Words the server always blocks are masked too.",
        ["Experimental UI"] = "Your settings, replays and machines are shared by both interfaces.",
        ["Developer mode"] = "Adds a DEV chip with the build, the connection and the input.",
        ["Disc image"] = "Your own copy of the game. It stays on this PC.",
        ["Game files"] = "Prepared once, on this PC, from your disc image.",
        ["Update automatically"] = "Never while a game runs.",
        ["Updates"] = "Release notes are under More, About.",
        ["Bring over an older copy"] = "The old copy stays as it is.",
        ["Reduced motion"] = "Also stills the background flag and the machine on the hub.",
        ["Interface scale"] = "Bigger text and buttons for a TV across the room.",
        ["Apply the interface scale"] = "The window lays itself out again at the new size.",
        ["Record races"] = "Every online race is kept, with its results, under Replays.",
        ["Record practice"] = "Practice replays show under Replays, Practice.",
        ["Replay folder"] = "Replays already there stay where they are.",
        ["Open the replay folder"] = "Opens it in the system's file browser.",
    };

    /// <summary>What a group is about, beside its rows.</summary>
    private static readonly Dictionary<string, string> GroupTips = new(StringComparer.Ordinal)
    {
        ["Basics"] = "The six settings most players change. Everything else is in the groups below.",
        ["Display"] = "How the game's picture fits your screen.",
        ["Graphics"] = "How sharp and smooth the game draws. Graphics under Basics sets three of these at once.",
        ["Sound"] = "The game's music and sounds, and the launcher's own sounds by kind.",
        ["Controller"] = "How the launcher shows and answers your controller.",
        ["Online"] = "Who you are online, and where you race.",
        ["Social"] = "Notices, Discord and the chat.",
        ["Launcher"] = "The disc image, the game's files, updates and the interface itself.",
        ["Replays"] = "What is recorded, and where it is kept.",
    };

    /// <summary>When a change of the row takes effect; "" not said.</summary>
    public static string WhenOf(string label) => When.GetValueOrDefault(label, "");

    /// <summary>What the row applies to; "" not said.</summary>
    public static string WhereOf(string label) => Where.GetValueOrDefault(label, "");

    /// <summary>Something good to know of the row; "" none.</summary>
    public static string TipOf(string label) => Tips.GetValueOrDefault(label, "");

    /// <summary>What the group is about; "" none.</summary>
    public static string GroupTipOf(string group) => GroupTips.GetValueOrDefault(group, "");
}
