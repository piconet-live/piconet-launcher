using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>A custom lobby's rules as an option page in steps: "From a file…" opens the system's file picker for the
/// row the focus is on (a mode file, a stat pack file), and "Choose the course" the fixed course's picker; a step
/// forward or back puts the focus on the step's first row.</summary>
public sealed partial class RulesPageView : UserControl, IPadLanding
{
    private RulesPageViewModel? _page;

    public RulesPageView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (_page is not null)
            {
                _page.PropertyChanged -= OnPageChanged;
            }
            _page = DataContext as RulesPageViewModel;
            if (_page is not null)
            {
                _page.PropertyChanged += OnPageChanged;
            }
        };
    }

    /// <summary>The step's first row.</summary>
    public Control? Landing() =>
        this.FindControl<Control>("StepRows")?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled);

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RulesPageViewModel.Step))
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (IsEffectivelyVisible && Landing() is { } first)
            {
                first.Focus(NavigationMethod.Directional);
                first.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }

    private async void OnFromFile(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not RulesPageViewModel { Focused: { } item })
        {
            return;
        }
        switch (item.Row)
        {
            case GameModeRule mode when await StatPackPicker.PickModeAsync(button).ConfigureAwait(true) is { } modePath:
                mode.Load(modePath);
                break;
            case PackRule pack when await StatPackPicker.PickAsync(button).ConfigureAwait(true) is { } packPath:
                pack.Load(packPath);
                break;
            default:
                break;
        }
    }

    private void OnChooseCourse(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RulesPageViewModel { Focused.Row: CourseRule course })
        {
            course.PickCourseCommand.Execute(null);
        }
    }
}
