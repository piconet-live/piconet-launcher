using Avalonia.Controls;
using Avalonia.Input;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Controls.NextUi;

/// <summary>An option row of the redesigned interface (<see cref="OptionItem"/> as its data): a row button whose value
/// left and right step (the pad's and the keyboard's, and the mouse's on the row's two arrows: <c>less</c>,
/// <c>more</c>), and which a press works (a switch flips, a choice goes on to its end). It looks as the theme's row
/// buttons do.</summary>
public sealed class OptionButton : Button, IPadValue
{
    /// <summary>The arrow the pointer pressed: -1 the left one, 1 the right one, 0 the row elsewhere (or a key).</summary>
    private int _side;

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _side = 0;
        for (var at = e.Source as Avalonia.Visual; at is not null && at != this; at = Avalonia.VisualTree.VisualExtensions.GetVisualParent(at))
        {
            if (at is Control part && (part.Classes.Contains("less") || part.Classes.Contains("more")))
            {
                _side = part.Classes.Contains("less") ? -1 : 1;
                break;
            }
        }
        base.OnPointerPressed(e);
    }

    public bool StepBy(int by) => DataContext is OptionItem item && item.Step(by);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None && DataContext is OptionItem item)
        {
            item.Step(e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClick()
    {
        base.OnClick();
        var side = _side;
        _side = 0;
        if (side != 0)
        {
            StepBy(side);   // an arrow clicked: that way, to the end and no further
        }
        else
        {
            (DataContext as OptionItem)?.Press();
        }
    }
}
