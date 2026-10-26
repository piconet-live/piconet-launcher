using Avalonia;
using Avalonia.Headless;

namespace Launcher.Tests;

/// <summary>The launcher's application without a window, for the tests that start it, and the collection they run in
/// (compiled into the service's Integration.Tests too).</summary>
internal static class HeadlessApp
{
    public const string Name = "the application without a window";

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Launcher.App.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
