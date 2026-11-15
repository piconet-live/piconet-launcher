using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned Garage's builder as an option page in two steps, the parts and the colours: a step forward
/// or back puts the focus on the step's first row.</summary>
public sealed partial class BuilderPageView : UserControl
{
    private BuilderPageViewModel? _page;

    public BuilderPageView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (_page is not null)
            {
                _page.PropertyChanged -= OnPageChanged;
            }
            _page = DataContext as BuilderPageViewModel;
            if (_page is not null)
            {
                _page.PropertyChanged += OnPageChanged;
            }
        };
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BuilderPageViewModel.Step))
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (IsEffectivelyVisible && this.FindControl<Control>("BuilderRows")?.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled) is { } first)
            {
                first.Focus(NavigationMethod.Directional);
                first.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }
}
