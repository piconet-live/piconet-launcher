using Avalonia.Controls;

namespace Launcher.App.Views;

/// <summary>A page that says where the pad lands when it shows (the server last used, not the page's first control);
/// <see cref="PadNavigator"/> asks it.</summary>
public interface IPadLanding
{
    /// <summary>The control to land on; null: the page's first.</summary>
    Control? Landing();
}
