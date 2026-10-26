using Launcher.App;

namespace Launcher.Tests.App;

/// <summary>What a replay's or a practice's game that stopped with an error says (<see cref="AppState.StoppedText"/>):
/// its exit code, a small one as a number and a Windows status in hexadecimal.</summary>
public sealed class GameStoppedTests
{
    [Theory]
    [InlineData(86, "The game stopped because of an error (code 86).")]                 // the lanes' Fatal
    [InlineData(1, "The game stopped because of an error (code 1).")]
    [InlineData(-1073740791, "The game stopped because of an error (code 0xC0000409).")] // an abort (crash-233532)
    [InlineData(-1073741819, "The game stopped because of an error (code 0xC0000005).")] // an access violation
    public void TheTextNamesTheExitCode(int code, string text) => Assert.Equal(text, AppState.StoppedText(code));
}
