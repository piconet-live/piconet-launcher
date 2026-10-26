using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public sealed partial class ControllerView : UserControl
{
    private Window? _window;

    public ControllerView() => AvaloniaXamlLoader.Load(this);

    /// <summary>Esc ends "press to set" with nothing set, whichever device is listened on.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ControllerViewModel { Capturing: true } vm)
        {
            vm.CancelCapture();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>"Press to set" listens only while the launcher's window is the one in front: a key typed into
    /// another program is not the player's choice of a control (the helper reads the keyboard wherever it is typed).</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.Deactivated += OnWindowLeft;
        }
        TellPage();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        TellPage();
    }

    /// <summary>The page asks the view whether the window is in front before it listens.</summary>
    private void TellPage()
    {
        if (DataContext is ControllerViewModel vm)
        {
            vm.InFront = () => _window is { IsActive: true };
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.Deactivated -= OnWindowLeft;
        }
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowLeft(object? sender, EventArgs e) => (DataContext as ControllerViewModel)?.CancelCapture();
}
