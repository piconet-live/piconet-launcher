using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned Controller page (its data: the classic page's view model; its rows: the shell's
/// <see cref="ControllerPageViewModel"/>, found once the view is in the window). A button being set listens only while
/// the launcher's window is in front, as on the classic page.</summary>
public sealed partial class ControllerPageView : UserControl, IPadLanding, Controls.NextUi.IDeckPage
{
    public static readonly DirectProperty<ControllerPageView, ControllerPageViewModel?> PageProperty =
        AvaloniaProperty.RegisterDirect<ControllerPageView, ControllerPageViewModel?>(nameof(Page), v => v.Page);

    private ControllerPageViewModel? _page;

    public ControllerPageView() => AvaloniaXamlLoader.Load(this);

    public ControllerPageViewModel? Page
    {
        get => _page;
        private set => SetAndRaise(PageProperty, ref _page, value);
    }

    /// <summary>The pad lands on the first row.</summary>
    public Control? Landing() =>
        this.FindControl<OptionList>("PadRows")?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Page = this.FindAncestorOfType<NextShell>()?.DataContext is MainViewModel main ? main.Shell.ControllerPage : null;
        if (DataContext is ControllerViewModel pad && TopLevel.GetTopLevel(this) is Window window)
        {
            pad.InFront ??= () => window.IsActive;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as ControllerViewModel)?.CancelCapture();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The shell's deck hides the page: a button being set is not set, as when the page is taken out.</summary>
    public void Showing(bool shown)
    {
        if (!shown)
        {
            (DataContext as ControllerViewModel)?.CancelCapture();
        }
    }
}
