using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>A key of the keyboard on the screen: what it types.</summary>
public sealed record KeyCap(string Label);

/// <summary>A row of the keyboard's keys.</summary>
public sealed record KeyRow(IReadOnlyList<KeyCap> Keys);

/// <summary>
/// The redesigned interface's keyboard on the screen (a text box the pad presses: a name, a server, a lobby's name, a
/// password, a search, a colour): what is typed (a password's as dots), the keys in four rows, Shift for one capital,
/// Space, Delete, Done and Cancel. A real keyboard types into it too. Done gives the text to whoever opened it; Cancel
/// (and B) closes it with nothing changed.
/// </summary>
public sealed partial class KeyboardViewModel : ViewModelBase
{
    /// <summary>The keys, row by row (as the mockup has them).</summary>
    public static IReadOnlyList<string> Layout { get; } = ["1234567890", "QWERTYUIOP", "ASDFGHJKL'", "ZXCVBNM-_."];

    private readonly Action<string> _done;
    private readonly Action _closed;

    /// <param name="most">The most characters it takes (0: 64).</param>
    public KeyboardViewModel(string title, string text, int most, bool masked, Action<string> done, Action closed)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentNullException.ThrowIfNull(closed);
        (Title, Most, Masked, _done, _closed) = (title, most > 0 ? most : 64, masked, done, closed);
        Text = text ?? "";
        ShowKeys();
    }

    /// <summary>What is typed for ("Your name", "Server address").</summary>
    public string Title { get; }

    public int Most { get; }

    /// <summary>A password: shown as dots.</summary>
    public bool Masked { get; }

    public char Mask => Masked ? '•' : '\0';

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>The next letter is a capital.</summary>
    [ObservableProperty]
    public partial bool Shift { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<KeyRow> Rows { get; private set; } = [];

    partial void OnShiftChanged(bool value) => ShowKeys();

    partial void OnTextChanged(string value)
    {
        if (value.Length > Most)
        {
            Text = value[..Most];
        }
    }

    /// <summary>A key: its character typed (a capital once after Shift).</summary>
    [RelayCommand]
    private void Type(KeyCap? key)
    {
        if (key is not null && Text.Length < Most)
        {
            Text += key.Label;
            Shift = false;
        }
    }

    [RelayCommand]
    private void Space()
    {
        if (Text.Length < Most)
        {
            Text += " ";
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Text.Length > 0)
        {
            Text = Text[..^1];
        }
    }

    [RelayCommand]
    private void ToggleShift() => Shift = !Shift;

    /// <summary>Done: the text given, the keyboard closed.</summary>
    [RelayCommand]
    private void Done()
    {
        _closed();
        _done(Text);
    }

    /// <summary>Cancel: closed, nothing changed.</summary>
    [RelayCommand]
    public void Cancel() => _closed();

    private void ShowKeys() =>
        Rows = [.. Layout.Select(row => new KeyRow([.. row.Select(c => new KeyCap(Shift ? c.ToString() : char.ToLowerInvariant(c).ToString()))]))];
}
