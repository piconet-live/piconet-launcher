using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Launcher.App.Views;

/// <summary>A "#RRGGBB" colour from a view model to a brush (view models carry no UI types).</summary>
public sealed class HexToBrush : IValueConverter
{
    public static HexToBrush Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Color.TryParse(s, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A share from 0 to 1 as a bar's length: the bar is drawn full and scaled across from its left end.</summary>
public sealed class ShareToScale : IValueConverter
{
    public static ShareToScale Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new ScaleTransform(value is double share && double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0, 1);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A game picture for a course number, from the player's disc (Theme/GameSkin): null without the
/// disc, so a view can show the name instead.</summary>
public sealed class GamePicture(Func<int, IImage?> picture) : IValueConverter
{
    public static GamePicture Venue { get; } = new(Theme.GameSkin.Venue);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n ? picture(n) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the value's text equals the parameter (the navigation's current page).</summary>
public sealed class Equal : IValueConverter
{
    public static Equal Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A machine's name by its id.</summary>
public sealed class MachineNameText : IValueConverter
{
    public static MachineNameText Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n >= 0 && n < Launcher.Core.Machines.Names.Count ? Launcher.Core.Machines.Names[n] : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A text in capitals (a step of the lobby's line, a venue over its course).</summary>
public sealed class UpperText : IValueConverter
{
    public static UpperText Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpperInvariant() ?? "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A word too long for the big button's full size ("CHOOSE YOUR DISC"): the button sets it smaller.</summary>
public sealed class LongWord : IValueConverter
{
    public static LongWord Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is string { Length: > 6 };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A player's tag while the pointer is over its line (a tag shows only on the pointer)
/// or while part of the line is selected (its text must not change under a selection), else nothing: values [the
/// tag's text, the pointer is over, the selected text].</summary>
public sealed class TagWhilePointed : IMultiValueConverter
{
    public static TagWhilePointed Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [string tag, bool over, var selected] && (over || selected is string { Length: > 0 }) ? tag : "";
}

/// <summary>A cup's gem colour (Theme.axaml, Gem...Brush) by the cup's name; none for "All".</summary>
public sealed class CupGem : IValueConverter
{
    public static CupGem Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string cup && Avalonia.Application.Current is { } app && app.TryFindResource($"Gem{cup}Brush", out var brush)
            ? brush : Avalonia.Media.Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A cup's name as the hero writes it: "Ruby Cup", "AX Cup"; the story's chapters "Story".</summary>
public sealed class CupName : IValueConverter
{
    public static CupName Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string cup ? cup == "Story" ? cup : $"{cup} Cup" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
