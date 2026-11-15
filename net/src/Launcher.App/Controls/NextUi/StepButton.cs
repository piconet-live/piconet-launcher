using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Launcher.App.Controls.NextUi;

/// <summary>A button whose value left and right step (the pad's and the keyboard's), as an option row's, worked by a
/// command of its own: <see cref="StepCommand"/> is given -1 or 1, and says by CanExecute whether that way goes
/// anywhere (the Garage's machine setting). A press is the button's own command. It looks as the theme's buttons do.</summary>
public sealed class StepButton : Button, IPadValue
{
    public static readonly StyledProperty<ICommand?> StepCommandProperty =
        AvaloniaProperty.Register<StepButton, ICommand?>(nameof(StepCommand));

    public ICommand? StepCommand
    {
        get => GetValue(StepCommandProperty);
        set => SetValue(StepCommandProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    public bool StepBy(int by)
    {
        if (StepCommand is { } step && step.CanExecute(by))
        {
            step.Execute(by);
            return true;
        }
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None)
        {
            StepBy(e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
