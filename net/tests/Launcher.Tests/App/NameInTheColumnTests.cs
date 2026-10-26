using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.App.Views;

namespace Launcher.Tests;

/// <summary>
/// The name at the top of the left column (<see cref="RacerView"/>), changed as players do it:
/// click the name, type, click Save. It uses only what a player has (the name, the box, the button that
/// reads "Save"), with the headless window's mouse and keyboard, so it is the same test before and after the fix:
/// before it, the Save button closed the menu in its Click handler and its command, read only after, was gone
/// (the menu out of the tree has no data context), so the name stayed; Enter saved. The view is shown in a window
/// with the toolkit's layer for menus (<see cref="WithMenuLayer"/>): there are no system windows here to open one
/// in, and the launcher's window theme has no such layer (on Windows a menu is a window of its own).
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NameInTheColumnTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A window's template with the part the toolkit opens a menu in when it has no system window for it.</summary>
    private static Avalonia.Controls.Templates.FuncControlTemplate<Window> WithMenuLayer() => new((window, scope) =>
    {
        var content = new Avalonia.Controls.Presenters.ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] = window[!ContentControl.ContentProperty],
        };
        var layers = new Avalonia.Controls.Primitives.VisualLayerManager { Name = "PART_VisualLayerManager", Child = content };
        scope.Register(content.Name, content);
        scope.Register(layers.Name, layers);
        return layers;
    });

    private static void Click(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), TopLevel.GetTopLevel(control)!)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task ClickTheNameTypeAndClickSave()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new { noServer = true }));
        var state = AppState.Load(data);
        Assert.Equal("RACER", state.Settings.Racer.Name);

        var session = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        (bool Opened, string? Typed, bool OpenAfterSave, string Column) seen;
        try
        {
            seen = await session.Dispatch(() =>
            {
                using var account = new AccountViewModel(state, () => { }, () => false);
                var view = new RacerView { DataContext = account };
                var window = new Window { Template = WithMenuLayer(), Content = view, Width = 600, Height = 500 };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var name = view.FindControl<Button>("NameButton")!;
                Click(window, name);
                var flyout = (Flyout)name.Flyout!;
                var opened = flyout.IsOpen;
                var menu = (Control)flyout.Content!;
                var box = menu.GetSelfAndLogicalDescendants().OfType<TextBox>().First();
                window.KeyTextInput("Mike");
                Dispatcher.UIThread.RunJobs();
                var typed = box.Text;
                Click(window, menu.GetSelfAndLogicalDescendants().OfType<Button>().First(b => Equals(b.Content, "Save")));
                var seen = (opened, typed, flyout.IsOpen, account.Name);
                window.Close();
                return seen;
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await session.DisposeAsync();
        }

        var file = File.ReadAllText(Path.Combine(data, "settings.json"));
        // every step is checked, so that a failure names each one that went wrong
        Assert.Multiple(
            () => Assert.True(seen.Opened),                                    // the click opens the menu
            () => Assert.Equal("Mike", seen.Typed),                            // on the old name, selected: typing replaces it
            () => Assert.Equal("MIKE", state.Settings.Racer.Name),             // Save saves, as the game shows the name
            () => Assert.Equal("MIKE", seen.Column),                           // the column shows it
            () => Assert.False(seen.OpenAfterSave),                            // the menu is closed
            () => Assert.Contains("\"MIKE\"", file, StringComparison.Ordinal));   // and the file holds it
    }
}
