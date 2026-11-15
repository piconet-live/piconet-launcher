using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Launcher.App.Controls.NextUi;

/// <summary>
/// Where the player is on a page that is gone through one step at a time (F-Zero GX's way: one choice a screen, the
/// next on a screen of its own; B a step back): the steps' names in a row, numbered, the one they are on lit, those
/// behind them done. Only shows: the page's own buttons and B move between the steps.
/// </summary>
public sealed class StepTrail : StackPanel
{
    /// <summary>The steps' names, in their order, between commas ("Course,Set up").</summary>
    public static readonly StyledProperty<string?> StepsProperty =
        AvaloniaProperty.Register<StepTrail, string?>(nameof(Steps));

    /// <summary>The step the player is on (0 the first).</summary>
    public static readonly StyledProperty<int> AtProperty =
        AvaloniaProperty.Register<StepTrail, int>(nameof(At));

    public StepTrail()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 10;
        IsHitTestVisible = false;
        Focusable = false;
    }

    public string? Steps
    {
        get => GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public int At
    {
        get => GetValue(AtProperty);
        set => SetValue(AtProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StepsProperty || change.Property == AtProperty)
        {
            Build();
        }
    }

    private void Build()
    {
        Children.Clear();
        var names = (Steps ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < names.Length; i++)
        {
            var (on, done) = (i == At, i < At);
            if (i > 0)
            {
                var link = new Border { Classes = { "link" }, VerticalAlignment = VerticalAlignment.Center };
                link.Classes.Set("done", i <= At);
                Children.Add(link);
            }
            var number = new Border { Classes = { "number" }, Child = new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture) } };
            var step = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Classes = { "step" },
                Children = { number, new TextBlock { Text = names[i].ToUpperInvariant(), VerticalAlignment = VerticalAlignment.Center } },
            };
            step.Classes.Set("on", on);
            step.Classes.Set("done", done);
            Children.Add(step);
        }
    }
}
