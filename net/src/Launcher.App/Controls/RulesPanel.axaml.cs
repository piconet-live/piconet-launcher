using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Controls;

/// <summary>A rules panel: <see cref="Groups"/> of rows (ViewModels/RuleRows.cs) and the action under them
/// (<see cref="Action"/>, <see cref="ActionCommand"/>). It holds no state of its own: what the rows hold is their
/// view model's.</summary>
public sealed partial class RulesPanel : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<RuleGroup>?> GroupsProperty =
        AvaloniaProperty.Register<RulesPanel, IReadOnlyList<RuleGroup>?>(nameof(Groups));

    public static readonly StyledProperty<string> ActionProperty = AvaloniaProperty.Register<RulesPanel, string>(nameof(Action), "");

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<RulesPanel, ICommand?>(nameof(ActionCommand));

    public RulesPanel()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(Button.ClickEvent, OnClick);
    }

    /// <summary>A stat pack's or a mode's row's Choose file: the system's file picker, and the file into the row.</summary>
    private async void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Name: "PackFile", DataContext: PackRule row } button &&
            await Views.StatPackPicker.PickAsync(button).ConfigureAwait(true) is { } path)
        {
            row.Load(path);
        }
        else if (e.Source is Button { Name: "ModeFile", DataContext: GameModeRule mode } modeButton &&
                 await Views.StatPackPicker.PickModeAsync(modeButton).ConfigureAwait(true) is { } modePath)
        {
            mode.Load(modePath);
        }
    }

    public IReadOnlyList<RuleGroup>? Groups
    {
        get => GetValue(GroupsProperty);
        set => SetValue(GroupsProperty, value);
    }

    public string Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }
}
