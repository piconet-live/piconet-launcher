using System.Globalization;
using Avalonia.Data.Converters;

namespace Launcher.App.Views.NextUi;

/// <summary>A grade of the game's machine select (A the best, E the least) as five parts, as many lit as it is
/// worth: A five, E one; anything else none.</summary>
public sealed class GradeParts : IValueConverter
{
    public static GradeParts Instance { get; } = new();

    /// <summary>How many of the five parts a grade lights.</summary>
    public static int Worth(string? grade) => grade?.Trim() switch
    {
        "A" => 5,
        "B" => 4,
        "C" => 3,
        "D" => 2,
        "E" => 1,
        _ => 0,
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var lit = Worth(value as string);
        return Enumerable.Range(0, 5).Select(i => i < lit).ToList();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A name's first letter, in capitals (a racer's mark).</summary>
public sealed class Initial : IValueConverter
{
    public static Initial Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } name ? name[..1].ToUpperInvariant() : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The classic rules panel's groups (a page's <c>Groups</c>) as the redesigned interface's option groups;
/// the parameter names the first group when it has no heading of its own.</summary>
public sealed class ToOptions : IValueConverter
{
    public static ToOptions Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ViewModels.NextUi.OptionGroup.Of(value as IEnumerable<ViewModels.RuleGroup>, parameter as string ?? "");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The disc image's button: its file's name once one is picked, else what to do.</summary>
public sealed class DiscWordsOf : IValueConverter
{
    public static DiscWordsOf Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path && path.Trim().Length > 0 ? System.IO.Path.GetFileName(path.Trim()) : "Choose disc image…";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Dimmed (true): a faint opacity; else whole.</summary>
public sealed class DimOpacity : IValueConverter
{
    public static DimOpacity Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 0.4 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A page's step is the one its parameter names (a page gone through in steps shows one step at a time).</summary>
public sealed class StepIs : IValueConverter
{
    public static StepIs Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int step && int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shown) && step == shown;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A place that is a win ("1st"): in gold.</summary>
public sealed class IsFirst : IValueConverter
{
    public static IsFirst Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string place && place == Launcher.Core.ReplayWords.Ordinal(1);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The page showing is the profile (its chip in the top bar lit).</summary>
public sealed class IsProfile : IValueConverter
{
    public static IsProfile Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is ViewModels.NextUi.ProfileViewModel;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A colour's swatch in words, for a screen reader: "Its own", "Colour 2", "Your paint".</summary>
public sealed class SwatchWords : IValueConverter
{
    public static SwatchWords Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ViewModels.StockSwatch swatch ? ViewModels.NextUi.SwatchRule.Words(swatch) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A page that fits the room the shell gives it (it scrolls its own lists): the shell's page area does not
/// scroll it (true: Disabled; false: Auto).</summary>
public sealed class FitsToScroll : IValueConverter
{
    public static FitsToScroll Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Settings' eyebrow: LAUNCHER over the groups, SETTINGS over the rows of a group open.</summary>
public sealed class SettingsEyebrow : IValueConverter
{
    public static readonly SettingsEyebrow Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "SETTINGS" : "LAUNCHER";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A path's data as text (SettingInfo's drawing of a group) as the geometry it draws; none for no text.</summary>
public sealed class PathData : IValueConverter
{
    public static readonly PathData Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } data ? Avalonia.Media.Geometry.Parse(data) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
