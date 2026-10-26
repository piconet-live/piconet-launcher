using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace Launcher.App.Controls;

/// <summary>A small whole number with a button to either side (Theme.axaml: laps, seats): 3 between - and +. With
/// <see cref="Typed"/> the figure can be typed too: kept inside the range when Enter is pressed or the figure is
/// left; anything that is no number gives the value back.</summary>
public sealed class Stepper : UserControl
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<Stepper, int>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> MinimumProperty = AvaloniaProperty.Register<Stepper, int>(nameof(Minimum));

    public static readonly StyledProperty<int> MaximumProperty = AvaloniaProperty.Register<Stepper, int>(nameof(Maximum), 100);

    /// <summary>The figure can be typed.</summary>
    public static readonly StyledProperty<bool> TypedProperty = AvaloniaProperty.Register<Stepper, bool>(nameof(Typed));

    /// <summary>How much a press changes it.</summary>
    public static readonly StyledProperty<int> StepProperty = AvaloniaProperty.Register<Stepper, int>(nameof(Step), 1);

    private readonly Button _less = Side("M0 5.5H10", "Less");
    private readonly Button _more = Side("M0 5.5H10M5 0.5V10.5", "More");
    private readonly TextBlock _figure = new()
    {
        MinWidth = 44, TextAlignment = Avalonia.Media.TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBox _typed = new()
    {
        MinWidth = 44, Width = 52, TextAlignment = Avalonia.Media.TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, MaxLength = 4, IsVisible = false,
    };

    public Stepper()
    {
        _figure.Classes.Add("figure");
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
        _less.Click += (_, _) => Value = Math.Max(Minimum, Value - Step);
        _more.Click += (_, _) => Value = Math.Min(Maximum, Value + Step);
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _less, _figure, _typed, _more } };
        HorizontalAlignment = HorizontalAlignment.Left;
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

    public bool Typed
    {
        get => GetValue(TypedProperty);
        set => SetValue(TypedProperty, value);
    }

    public int Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TypedProperty)
        {
            _typed.IsVisible = Typed;
            _figure.IsVisible = !Typed;
        }
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty)
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

    private void Show()
    {
        _figure.Text = Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _typed.Text = _figure.Text;
        _less.IsEnabled = Value > Minimum;
        _more.IsEnabled = Value < Maximum;
    }

    private static Button Side(string drawing, string name)
    {
        var button = new Button
        {
            Width = 28, Height = 28, Padding = default,
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Avalonia.Media.Geometry.Parse(drawing), Width = 10, Height = 11, StrokeThickness = 1,
                [!Avalonia.Controls.Shapes.Shape.StrokeProperty] = new Binding("Foreground")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) },
                },
            },
        };
        button.Classes.Add("secondary");
        AutomationProperties.SetName(button, name);
        return button;
    }
}
