using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>
/// The menu that changes the player's name. Whoever shows it (a button's flyout) calls <see cref="Begin"/> when it
/// opens and closes it at <see cref="Done"/>. The steps are in this order on purpose: the name is saved here, then
/// the menu is closed. (A Save button that closed the menu in its Click handler and left the saving to its command,
/// which a button reads only afterwards, would save nothing: closed, the button has no command. Only Enter would.)
/// </summary>
public sealed partial class NameMenu : UserControl
{
    public NameMenu() => AvaloniaXamlLoader.Load(this);

    /// <summary>The menu is finished with, saved or not: close it.</summary>
    public event EventHandler? Done;

    /// <summary>The menu opened: the box holds the saved name, selected, so that typing replaces it. (The name is put
    /// into the box first: selected before, the box's own text was selected, which at a first opening was none, and
    /// what the player typed went in front of the old name.)</summary>
    public void Begin()
    {
        (DataContext as AccountViewModel)?.EditName();
        if (this.FindControl<TextBox>("NameBox") is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    /// <summary>Enter saves, as the Save button.</summary>
    private void OnNameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Save();
            e.Handled = true;
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Save();

    private void OnCancel(object? sender, RoutedEventArgs e) => Done?.Invoke(this, EventArgs.Empty);

    /// <summary>Saves the name, then closes; a box that holds no name stays open.</summary>
    private void Save()
    {
        if (DataContext is AccountViewModel vm && vm.SaveName())
        {
            Done?.Invoke(this, EventArgs.Empty);
        }
    }
}
