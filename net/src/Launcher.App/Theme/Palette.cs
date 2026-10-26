using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Launcher.App.Theme;

/// <summary>A colour of the theme's dictionary (Theme.axaml), for what is drawn in code (PadPicture): the colour
/// is named there once, never again in code. The fallback is for a control made outside an application (a test
/// that draws it alone).</summary>
public static class Palette
{
    public static Color Of(string key, Color fallback) =>
        Application.Current is { } app && app.TryFindResource(key, out var value) && value is Color colour ? colour : fallback;
}
