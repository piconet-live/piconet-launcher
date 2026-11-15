using Launcher.App.ViewModels;
using Launcher.Core;

namespace Launcher.App.Views.NextUi;

/// <summary>The lobby's call back from a practice as the player hears it, in the game too (where the launcher's band
/// does not show): a call when it begins, and a tick on each of its last three seconds.</summary>
internal static class RecallSounds
{
    public static void Begun(MainViewModel main) => Attention.Sound(LobbyMoment.CourseVote, main.SoundSettings);

    public static void Second(MainViewModel main, int left)
    {
        if (left is >= 1 and <= 3)
        {
            Attention.Sound(LauncherSound.DrawHop, main.SoundSettings);
        }
    }
}
