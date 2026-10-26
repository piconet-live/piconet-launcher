using System.Globalization;

namespace Launcher.Core;

/// <summary>A whole number the player typed into a field (a stepper's figure): digits only, kept inside its range.</summary>
public static class TypedNumber
{
    /// <summary>The number inside [<paramref name="minimum"/>, <paramref name="maximum"/>] (the nearest end when
    /// outside); null when the text is no number of digits only (a sign, a space, a letter, nothing).</summary>
    public static int? Whole(string? text, int minimum, int maximum)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 9)
        {
            return text is { Length: > 9 } && text.All(char.IsAsciiDigit) ? maximum : null;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : null;
    }
}
