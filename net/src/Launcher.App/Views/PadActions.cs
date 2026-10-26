using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Launcher.App.Controls;
using Launcher.Core.Pads;

namespace Launcher.App.Views;

/// <summary>What the pad does to the control it is on: A presses it (as a click does: its handlers, then its
/// command), and left and right change a value (a slider, a stepper, a row of choices, a drop-down list, a switch).</summary>
public static class PadActions
{
    /// <summary>A: a button is pressed (a toggle toggled), through the control's own automation (the same path as
    /// a click: trap 14); a row of choices or a drop-down list goes to its next choice, round to the first; a
    /// switch flips. False: nothing to press (a slider, a stepper).</summary>
    public static bool Press(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!control.IsEffectivelyEnabled)
        {
            return false;
        }
        switch (control)
        {
            case SelectingItemsControl choices when PadNav.IsValue(control):
                var count = choices.ItemCount;
                if (count > 0)
                {
                    choices.SelectedIndex = (choices.SelectedIndex + 1 + count) % count;
                }
                return count > 0;
            case Slider or Stepper:
                return false;
            default:
                break;
        }
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        if (peer.GetProvider<IToggleProvider>() is { } toggle)
        {
            toggle.Toggle();
            return true;
        }
        if (peer.GetProvider<IInvokeProvider>() is { } invoke)
        {
            invoke.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>Left or right on a value: one step that way. True: the step was taken, or the value is at its end
    /// and the move is a repeat (a held direction never leaves the control it is changing); false: no value here,
    /// or a fresh press at its end, which moves the focus on.</summary>
    public static bool Step(Control control, PadMove move, bool repeat)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (move is not (PadMove.Left or PadMove.Right) || !PadNav.IsValue(control) || !control.IsEffectivelyEnabled)
        {
            return false;
        }
        var by = move == PadMove.Right ? 1 : -1;
        var moved = control switch
        {
            Slider slider => StepSlider(slider, by),
            Stepper stepper => StepStepper(stepper, by),
            ToggleSwitch toggle => Set(toggle, by > 0),
            SelectingItemsControl choices => StepChoice(choices, by),
            _ => false,
        };
        return moved || repeat;
    }

    private static bool StepSlider(Slider slider, int by)
    {
        var step = slider.TickFrequency > 0 ? slider.TickFrequency : slider.SmallChange > 0 ? slider.SmallChange : 1;
        var to = Math.Clamp(slider.Value + (by * step), slider.Minimum, slider.Maximum);
        if (to == slider.Value)
        {
            return false;
        }
        slider.Value = to;
        return true;
    }

    private static bool StepStepper(Stepper stepper, int by)
    {
        var to = Math.Clamp(stepper.Value + (by * Math.Max(1, stepper.Step)), stepper.Minimum, stepper.Maximum);
        if (to == stepper.Value)
        {
            return false;
        }
        stepper.Value = to;
        return true;
    }

    private static bool Set(ToggleSwitch toggle, bool on)
    {
        if (toggle.IsChecked == on)
        {
            return false;
        }
        toggle.IsChecked = on;
        return true;
    }

    private static bool StepChoice(SelectingItemsControl choices, int by)
    {
        var to = Math.Clamp(choices.SelectedIndex + by, 0, choices.ItemCount - 1);
        if (to == choices.SelectedIndex || to < 0)
        {
            return false;
        }
        choices.SelectedIndex = to;
        return true;
    }
}
