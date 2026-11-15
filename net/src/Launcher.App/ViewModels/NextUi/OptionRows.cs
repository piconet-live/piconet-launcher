using System.Globalization;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// An option row that works a value held elsewhere (a setting of the Settings page, a port of the Controller page),
/// by the getter and setter it is given: left and right step it, A presses it, and it says its value in words. It reads
/// the value again when told (<see cref="Show"/>): the page tells its rows when what they show may have changed.
/// </summary>
public interface IOptionRow
{
    /// <summary>A step that way (-1 left, 1 right); false: at its end, or nothing to step.</summary>
    bool StepBy(int by);

    /// <summary>A (and a click).</summary>
    void Press();

    /// <summary>The value in words.</summary>
    string Words { get; }

    /// <summary>The row is pressed to do something (it has no value to step): an arrow at its end, not two.</summary>
    bool IsAction { get; }

    /// <summary>The row can be worked now.</summary>
    bool Enabled { get; }

    /// <summary>Whether a step left (<c>Less</c>) and a step right (<c>More</c>) go anywhere: the arrows of a value at
    /// its end are faint.</summary>
    (bool Less, bool More) Room { get; }

    /// <summary>The value read again, and the row's line.</summary>
    void Show();
}

/// <summary>A row of the option rows (<see cref="IOptionRow"/>): its label, whether it can be worked (a getter), and
/// its line under it (a getter: a reason, a state).</summary>
public abstract class OptionRow(string label, Func<bool>? enabled, Func<string>? note) : RuleRow(label), IOptionRow
{
    /// <summary>The row's own line in the help panel; null: the line its label has (<see cref="RuleHelp"/>).</summary>
    public string? Help { get; init; }

    public bool Enabled => enabled?.Invoke() ?? true;

    public virtual bool IsAction => false;

    public virtual (bool Less, bool More) Room => (true, true);

    public abstract string Words { get; }

    public abstract bool StepBy(int by);

    public virtual void Press() => StepBy(1);

    public void Show()
    {
        Note = note?.Invoke() ?? "";
        OnPropertyChanged(nameof(Words));
        OnPropertyChanged(nameof(Enabled));
    }
}

/// <summary>One of a list: left and right go along it, and A on, to its ends (never round: the first and the last are
/// ends one sees).</summary>
public sealed class ChoiceRow(string label, Func<IReadOnlyList<string>> options, Func<int> get, Action<int> set,
    Func<bool>? enabled = null, Func<string>? note = null) : OptionRow(label, enabled, note)
{
    public override string Words => get() is var at && at >= 0 && at < options().Count ? options()[at] : "";

    /// <summary>Every choice, in order (the Settings window lists them, the one chosen marked).</summary>
    public IReadOnlyList<string> Options => options();

    public override bool StepBy(int by)
    {
        var count = options().Count;
        var at = get();
        var to = Math.Clamp(at + by, 0, Math.Max(0, count - 1));
        if (!Enabled || count == 0 || to == at)
        {
            return false;
        }
        set(to);
        Show();
        return true;
    }

    public override void Press() => StepBy(1);

    public override (bool Less, bool More) Room => (get() > 0, get() < options().Count - 1);
}

/// <summary>On or off: right on, left off, A flips it.</summary>
public sealed class ToggleRow(string label, Func<bool> get, Action<bool> set, Func<bool>? enabled = null, Func<string>? note = null)
    : OptionRow(label, enabled, note)
{
    public override string Words => get() ? "On" : "Off";

    public override bool StepBy(int by)
    {
        var on = by > 0;
        if (!Enabled || get() == on)
        {
            return false;
        }
        set(on);
        Show();
        return true;
    }

    public override void Press()
    {
        if (Enabled)
        {
            set(!get());
            Show();
        }
    }

    public override (bool Less, bool More) Room => (get(), !get());
}

/// <summary>A whole number between its ends, stepped by its step; its unit after it ("%", "ms").</summary>
public sealed class NumberRow(string label, Func<int> get, Action<int> set, int least, int most, int step, string unit = "",
    Func<bool>? enabled = null, Func<string>? note = null) : OptionRow(label, enabled, note)
{
    public override string Words => get().ToString(CultureInfo.InvariantCulture) + unit;

    /// <summary>Where the value stands between its ends, 0 to 1 (the Settings window's meter).</summary>
    public double Share => most > least ? Math.Clamp((get() - least) / (double)(most - least), 0, 1) : 0;

    /// <summary>Its ends and its step in words: "0% to 100%, in steps of 5".</summary>
    public string Span => string.Create(CultureInfo.InvariantCulture, $"{least}{unit} to {most}{unit}, in steps of {step}");

    public string LeastWords => least.ToString(CultureInfo.InvariantCulture) + unit;

    public string MostWords => most.ToString(CultureInfo.InvariantCulture) + unit;

    public override bool StepBy(int by)
    {
        var at = get();
        var to = Math.Clamp((int)Math.Round((at + (Math.Sign(by) * step) - least) / (double)step) * step + least, least, most);
        if (!Enabled || to == at)
        {
            return false;
        }
        set(to);
        Show();
        return true;
    }

    public override void Press()
    {
    }

    public override (bool Less, bool More) Room => (get() > least, get() < most);
}

/// <summary>A row pressed to do something (open a page, the keyboard, a picker), its value beside it (a getter);
/// left and right do nothing.</summary>
public sealed class ActionRow(string label, Func<string> value, Action act, Func<bool>? enabled = null, Func<string>? note = null)
    : OptionRow(label, enabled, note)
{
    public override bool IsAction => true;

    public override string Words => value();

    public override bool StepBy(int by) => false;

    public override (bool Less, bool More) Room => (false, false);

    public override void Press()
    {
        if (Enabled)
        {
            act();
            Show();
        }
    }
}
