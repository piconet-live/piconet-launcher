using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace Launcher.App.Controls;

/// <summary>Every number the player sets can be typed: a whole number kept inside its range when Enter is pressed or
/// the box is left; anything that is no number gives the value back.</summary>
public sealed class NumberBox : UserControl
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<NumberBox, int>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> MinimumProperty = AvaloniaProperty.Register<NumberBox, int>(nameof(Minimum));

    public static readonly StyledProperty<int> MaximumProperty = AvaloniaProperty.Register<NumberBox, int>(nameof(Maximum), 100);

    private readonly TextBox _typed = new()
    {
        MinWidth = 44, Width = 52, TextAlignment = Avalonia.Media.TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, MaxLength = 4,
    };

    public NumberBox()
    {
        _typed.Classes.Add("field");
        _typed.Classes.Add("figure");
        AutomationProperties.SetName(_typed, "Typed");
        _typed.LostFocus += (_, _) => Take();
        _typed.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Take();
                e.Handled = true;
            }
        };
        Content = _typed;
        VerticalAlignment = VerticalAlignment.Center;
        Show();
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            Show();
        }
    }

    // the typed figure, kept inside the range; no number: the value as it was
    private void Take()
    {
        if (Launcher.Core.TypedNumber.Whole(_typed.Text, Minimum, Maximum) is { } typed)
        {
            Value = typed;
        }
        Show();
    }

    private void Show() => _typed.Text = Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
