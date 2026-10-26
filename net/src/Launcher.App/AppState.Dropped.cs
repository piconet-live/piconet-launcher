using Launcher.Core;

namespace Launcher.App;

// A Quick Play connection that ended without the player's doing.
public sealed partial class AppState
{
    /// <summary>The session, listened to for its connection ending without the player's doing (the server closed
    /// it, with or without a reason, or it was lost): said in the left column, where a server that cannot be reached
    /// is said. Not after a leave, which has its own word and no session any more.</summary>
    private QuickPlaySession HeardWhenDropped(QuickPlaySession session)
    {
        session.Dropped += text =>
        {
            if (Session == session)
            {
                Problem = text;
                Changed?.Invoke();
            }
        };
        return session;
    }
}
