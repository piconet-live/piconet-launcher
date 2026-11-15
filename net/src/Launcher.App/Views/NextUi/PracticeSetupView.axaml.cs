using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's Practice in two steps: the course, then its set-up and Start practice. A step
/// forward or back takes the focus with it: to START PRACTICE, or back to the course chosen.</summary>
public sealed partial class PracticeSetupView : UserControl, IPadLanding
{
    private PracticeViewModel? _page;

    public PracticeSetupView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (_page is not null)
            {
                _page.PropertyChanged -= OnPageChanged;
            }
            _page = DataContext as PracticeViewModel;
            if (_page is not null)
            {
                _page.PropertyChanged += OnPageChanged;
            }
        };
    }

    /// <summary>The set-up: START PRACTICE (else its first row); the courses: the course chosen, else the first.</summary>
    public Control? Landing()
    {
        if (_page is { SettingUp: true })
        {
            return this.FindControl<Button>("Start") is { IsEffectivelyEnabled: true } start ? start
                : this.FindControl<Control>("Rows")?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible);
        }
        var cards = this.FindControl<ItemsControl>("Courses")?.GetVisualDescendants().OfType<Button>().ToList() ?? [];
        return cards.FirstOrDefault(b => b.Classes.Contains("picked")) ?? cards.FirstOrDefault();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PracticeViewModel.SettingUp))
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            if (IsEffectivelyVisible && (focused is null || !focused.IsEffectivelyVisible || !this.IsVisualAncestorOf(focused))
                && Landing() is { } to)
            {
                to.Focus(NavigationMethod.Directional);
                to.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }
}
