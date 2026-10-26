using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.App.Views;

namespace Launcher.Tests;

/// <summary>
/// The menu that changes the player's name (<see cref="NameMenu"/>, which the left column's name opens), used as a
/// player uses it: it opens, the player types, and presses Save or Enter. Run without a
/// window (Avalonia.Headless), with the mouse and the keyboard of the headless window, so the Save button's own
/// click is what is tested: a click on Save could do nothing when the
/// button closed the menu and saved nothing, while Enter saved. Here the menu is shown by itself, in a window;
/// <see cref="NameInTheColumnTests"/> opens it from the name, as a flyout.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NameMenuTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A launcher's state in a data folder of the test's own, with no server to ask.</summary>
    private AppState State(string? name = null)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), name is null
            ? System.Text.Json.JsonSerializer.Serialize(new { noServer = true })
            : System.Text.Json.JsonSerializer.Serialize(new { noServer = true, racer = new { name, machine = 0, color = new { r = 1, g = 2, b = 3 } } }));
        return AppState.Load(data);
    }

    private string SavedName() =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "data", "settings.json")))
            .RootElement.GetProperty("racer").GetProperty("name").GetString()!;

    /// <summary>The menu in a window, opened as a flyout opens it; what the steps return, from the application's
    /// thread.</summary>
    private static async Task<T> WithTheMenu<T>(AppState state, Func<Window, NameMenu, AccountViewModel, Func<int>, T> steps)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            return await session.Dispatch(() =>
            {
                using var account = new AccountViewModel(state, () => { }, () => false);
                var menu = new NameMenu { DataContext = account };
                var done = 0;
                menu.Done += (_, _) => done++;
                var window = new Window { Content = menu, Width = 400, Height = 300 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                menu.Begin();
                Dispatcher.UIThread.RunJobs();
                try
                {
                    return steps(window, menu, account, () => done);
                }
                finally
                {
                    window.Close();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static void Click(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task TheSaveButtonSavesTheTypedName()
    {
        var state = State();
        Assert.Equal("RACER", state.Settings.Racer.Name);                      // a new player's name
        var (opened, typed, closed, shown) = await WithTheMenu(state, (window, menu, account, done) =>
        {
            var box = menu.FindControl<TextBox>("NameBox")!;
            // it opens on the saved name, selected: what is typed replaces it
            var opened = (box.Text, box.IsFocused, Math.Min(box.SelectionStart, box.SelectionEnd), Math.Max(box.SelectionStart, box.SelectionEnd));
            window.KeyTextInput("Mike");
            Dispatcher.UIThread.RunJobs();
            var typed = box.Text;
            Click(window, menu.FindControl<Button>("SaveButton")!);
            return (opened, typed, done(), account.Name);
        });
        Assert.Equal(("RACER", true, 0, 5), opened);
        Assert.Equal("Mike", typed);
        Assert.Equal("MIKE", state.Settings.Racer.Name);                       // kept as the game shows it
        Assert.Equal("MIKE", shown);                                           // the left column's name
        Assert.Equal("MIKE", SavedName());                                     // and in the file
        Assert.Equal(1, closed);                                               // the menu closes after the save
    }

    [Fact]
    public async Task EnterSavesTheTypedName()
    {
        var state = State("OLD NAME");
        var closed = await WithTheMenu(state, (window, _, _, done) =>
        {
            window.KeyTextInput("Captain & Co.");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            return done();
        });
        Assert.Equal("CAPTAIN & CO.", state.Settings.Racer.Name);
        Assert.Equal("CAPTAIN & CO.", SavedName());
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task ANameTheGameCannotShowSaysWhichLettersAndIsNotSaved()
    {
        var state = State("OLD NAME");
        var (help, shownAs, canSave, saveEnabled, closed, mixedHelp, mixedShown) = await WithTheMenu(state, (window, menu, account, done) =>
        {
            var save = menu.FindControl<Button>("SaveButton")!;
            window.KeyTextInput("Женя");
            Dispatcher.UIThread.RunJobs();
            var first = (DraftHelp: (account.HasLost, account.DraftLost), account.DraftShown, account.CanSave, save.IsEffectivelyEnabled);
            // neither the button nor Enter saves it or closes the menu
            Click(window, save);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var closed = done();
            // a name of which some is shown says what is left out, and can be saved
            account.Draft = "José 7";
            return (first.DraftHelp, first.DraftShown, first.CanSave, first.IsEffectivelyEnabled, closed, (account.HasLost, account.DraftLost), account.DraftShown);
        });
        Assert.Equal((true, "Ж е н я"), help);
        Assert.Equal("[YOUR NAME]", shownAs);
        Assert.False(canSave);
        Assert.False(saveEnabled);
        Assert.Equal(0, closed);
        Assert.Equal("OLD NAME", state.Settings.Racer.Name);
        Assert.Equal((true, "é"), mixedHelp);
        Assert.Equal("JOS 7", mixedShown);
    }

    [Fact]
    public async Task CancelClosesTheMenuAndKeepsTheName()
    {
        var state = State("OLD NAME");
        var closed = await WithTheMenu(state, (window, menu, _, done) =>
        {
            window.KeyTextInput("Other");
            var cancel = menu.GetLogicalDescendantsOfType<Button>().First(b => Equals(b.Content, "Cancel"));
            Click(window, cancel);
            return done();
        });
        Assert.Equal("OLD NAME", state.Settings.Racer.Name);
        Assert.Equal(1, closed);
    }
}

internal static class LogicalTree
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
