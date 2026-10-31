using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace Launcher.App.Controls;

/// <summary>A small whole number with a button to either side (Theme.axaml: laps, seats): 3 between - and +, and the
/// figure can be typed (<see cref="NumberBox"/>).</summary>
public sealed class Stepper : UserControl
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<Stepper, int>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> MinimumProperty = AvaloniaProperty.Register<Stepper, int>(nameof(Minimum));

    public static readonly StyledProperty<int> MaximumProperty = AvaloniaProperty.Register<Stepper, int>(nameof(Maximum), 100);

    /// <summary>How much a press changes it.</summary>
    public static readonly StyledProperty<int> StepProperty = AvaloniaProperty.Register<Stepper, int>(nameof(Step), 1);

    private readonly Button _less = Side("M0 5.5H10", "Less");
    private readonly Button _more = Side("M0 5.5H10M5 0.5V10.5", "More");
    private readonly NumberBox _typed = new();

    public Stepper()
    {
        _typed.Bind(NumberBox.ValueProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });
        _typed.Bind(NumberBox.MinimumProperty, new Binding(nameof(Minimum)) { Source = this });
        _typed.Bind(NumberBox.MaximumProperty, new Binding(nameof(Maximum)) { Source = this });
        _less.Click += (_, _) => Value = Math.Max(Minimum, Value - Step);
        _more.Click += (_, _) => Value = Math.Min(Maximum, Value + Step);
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _less, _typed, _more } };
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

    public int Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty)
        {
            Show();
        }
    }

    private void Show()
    {
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
