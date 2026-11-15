using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Launcher.App.Controls;

/// <summary>
/// What a page tells the pad's navigation (Views/PadNavigator.cs) about its controls: the page's main action, which
/// Start presses (<c>PadNav.Main</c>); a control the pad never stops on (<c>PadNav.Skip</c>: one that needs the
/// keyboard or the mouse); a panel over the page that keeps the pad inside it while it shows (<c>PadNav.Scope</c>: a
/// question); the page's own list, which B and Escape bring the focus back to before they leave the page
/// (<c>PadNav.Home</c>: the Garage's machines, a list of lobbies). Which controls the pad stops on at all is said here
/// too (<see cref="IsTarget"/>).
/// </summary>
public static class PadNav
{
    public static readonly AttachedProperty<bool> MainProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Main", typeof(PadNav));

    public static readonly AttachedProperty<bool> SkipProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Skip", typeof(PadNav));

    public static readonly AttachedProperty<bool> ScopeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Scope", typeof(PadNav));

    public static readonly AttachedProperty<bool> HomeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Home", typeof(PadNav));

    public static bool GetHome(Control control) => control.GetValue(HomeProperty);

    /// <summary>Where the focus goes once a control is pressed: the name of the page's control that is the next step
    /// (<c>Views/PadFollow.cs</c>: a course picked, START PRACTICE).</summary>
    public static readonly AttachedProperty<string?> NextProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Next", typeof(PadNav));

    public static string? GetNext(Control control) => control.GetValue(NextProperty);

    /// <summary>A row of sub-tabs (chips, the one on lit: <c>go</c>): R1 and Ctrl+arrows step through them
    /// (<c>Views/PadNavigator.cs</c>, <c>StepTab</c>), as L2 and R2 step through the areas.</summary>
    public static readonly AttachedProperty<bool> TabsProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Tabs", typeof(PadNav));

    public static bool GetTabs(Control control) => control.GetValue(TabsProperty);

    public static void SetTabs(Control control, bool value) => control.SetValue(TabsProperty, value);

    public static void SetNext(Control control, string? value) => control.SetValue(NextProperty, value);

    public static void SetHome(Control control, bool value) => control.SetValue(HomeProperty, value);

    public static bool GetMain(Control control) => control.GetValue(MainProperty);

    public static void SetMain(Control control, bool value) => control.SetValue(MainProperty, value);

    public static bool GetSkip(Control control) => control.GetValue(SkipProperty);

    public static void SetSkip(Control control, bool value) => control.SetValue(SkipProperty, value);

    public static bool GetScope(Control control) => control.GetValue(ScopeProperty);

    public static void SetScope(Control control, bool value) => control.SetValue(ScopeProperty, value);

    /// <summary>What a text box the pad presses opens: the redesigned interface's keyboard on the screen (its shell
    /// sets it while it shows); null: no keyboard on the screen, and the pad stops on no text box.</summary>
    public static Func<TextBox, bool>? Typing { get; set; }

    /// <summary>A control the pad stops on: a button (a toggle, a check box, a switch), a slider, a stepper, a
    /// drop-down list, a row of choices (<c>ListBox.seg</c>). A text box only where a keyboard on the screen types into
    /// it (<see cref="Typing"/>: the redesigned interface): the classic one has none. Nor are the window's own buttons
    /// (minimise, maximise, close).</summary>
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
            TextBox box => Typing is not null && !box.IsReadOnly,
            ListBox list => list.Classes.Contains("seg"),
            _ => false,
        };
    }

    /// <summary>A control whose value the pad's left and right change (a press A also steps a choice on).</summary>
    public static bool IsValue(Control control) => control is Slider or Stepper or ComboBox or ToggleSwitch or IPadValue
        || (control is ListBox list && list.Classes.Contains("seg"));

    /// <summary>What the pad's search never goes into: a text box (the keyboard's; one the keyboard on the screen types
    /// into is a target itself), and what is not shown.</summary>
    public static bool Closed(Control control) => (control is TextBox && !IsTarget(control)) || control is SelectableTextBlock || !control.IsVisible
        || control is ScrollBar;
}
