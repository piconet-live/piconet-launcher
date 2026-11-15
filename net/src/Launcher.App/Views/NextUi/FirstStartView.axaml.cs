using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned first start (its data: the classic first screen's view model; its steps: the shell's
/// <see cref="FirstStartViewModel"/>, found once the view is in the window). The view's own part: the system's pickers
/// of the disc image and of an older copy's folder.</summary>
public sealed partial class FirstStartView : UserControl, IPadLanding
{
    public static readonly DirectProperty<FirstStartView, FirstStartViewModel?> PageProperty =
        AvaloniaProperty.RegisterDirect<FirstStartView, FirstStartViewModel?>(nameof(Page), v => v.Page);

    private FirstStartViewModel? _page;

    public FirstStartView() => AvaloniaXamlLoader.Load(this);

    public FirstStartViewModel? Page
    {
        get => _page;
        private set => SetAndRaise(PageProperty, ref _page, value);
    }

    /// <summary>The pad lands on Next when it can be pressed, else on the disc's button.</summary>
    public Control? Landing() => Page is { CanNext: true } ? this.FindControl<Button>("Next") : this.FindControl<Button>("ChooseDisc");

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Page = this.FindAncestorOfType<NextShell>()?.DataContext is MainViewModel main ? main.Shell.FirstStart : null;
        Page?.Shown();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Page?.Left();
        base.OnDetachedFromVisualTree(e);
    }

    private async void OnChooseDisc(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiscViewModel vm && await DiscPicker.PickAsync(this).ConfigureAwait(true) is { } path)
        {
            vm.Path = path;
            // the next step: the image taken, Next (when it is the game's; a wrong one keeps the focus on the choice)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (this.FindControl<Button>("Next") is { IsEffectivelyEnabled: true } next)
                {
                    next.Focus(Avalonia.Input.NavigationMethod.Directional);
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private async void OnBringOver(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiscViewModel vm && await FolderPicker.PickAsync(this, FolderPicker.OlderCopyTitle).ConfigureAwait(true) is { } folder)
        {
            await vm.Move.BringOverAsync(folder).ConfigureAwait(true);
        }
    }
}
