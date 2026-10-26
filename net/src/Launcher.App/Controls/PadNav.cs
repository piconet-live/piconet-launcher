using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Launcher.App.Controls;

/// <summary>
/// What a page tells the pad's navigation (Views/PadNavigator.cs) about its controls: the page's main action, which
/// Start presses (<c>PadNav.Main</c>); a control the pad never stops on (<c>PadNav.Skip</c>: one that needs the
/// keyboard or the mouse); a panel over the page that keeps the pad inside it while it shows (<c>PadNav.Scope</c>: a
/// question). Which controls the pad stops on at all is said here too (<see cref="IsTarget"/>).
/// </summary>
public static class PadNav
{
    public static readonly AttachedProperty<bool> MainProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Main", typeof(PadNav));

    public static readonly AttachedProperty<bool> SkipProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Skip", typeof(PadNav));

    public static readonly AttachedProperty<bool> ScopeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Scope", typeof(PadNav));

    public static bool GetMain(Control control) => control.GetValue(MainProperty);

    public static void SetMain(Control control, bool value) => control.SetValue(MainProperty, value);

    public static bool GetSkip(Control control) => control.GetValue(SkipProperty);

    public static void SetSkip(Control control, bool value) => control.SetValue(SkipProperty, value);

    public static bool GetScope(Control control) => control.GetValue(ScopeProperty);

    public static void SetScope(Control control, bool value) => control.SetValue(ScopeProperty, value);

    /// <summary>A control the pad stops on: a button (a toggle, a check box, a switch), a slider, a stepper, a
    /// drop-down list, a row of choices (<c>ListBox.seg</c>). A text box never is: it needs the keyboard, and
    /// there is no keyboard on the screen. Nor are the window's own buttons (minimise, maximise, close).</summary>
    public static bool IsTarget(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (GetSkip(control))
        {
            return false;
        }
        return control switch
        {
            Button button => !button.Classes.Contains("chrome"),
            Slider or Stepper or ComboBox => true,
            ListBox list => list.Classes.Contains("seg"),
            _ => false,
        };
    }

    /// <summary>A control whose value the pad's left and right change (a press A also steps a choice on).</summary>
    public static bool IsValue(Control control) => control is Slider or Stepper or ComboBox or ToggleSwitch
        || (control is ListBox list && list.Classes.Contains("seg"));

    /// <summary>What the pad's search never goes into: a text box (the keyboard's), and what is not shown.</summary>
    public static bool Closed(Control control) => control is TextBox or SelectableTextBlock || !control.IsVisible
        || control is ScrollBar;
}
