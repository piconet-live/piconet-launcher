using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The Garage fits the window at every size from the least (1280 x 720) up, wide and low ones too (a
/// window maximised on a wide, short screen drew the cells over the 3D view and the colour wheel):
/// the Garage tab and the lobby's machine stage, at the interface scale of 100% and 125%, in both phases. Nothing
/// overlaps: SELECT shows the whole roster (41 tiles) inside the page; PREVIEW's stage, with the panel floating 16 px
/// in from its edge and the colour wheel inside it, keeps its least height and ends inside the page; neither scrolls.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class GarageFitTests : IDisposable
{
    private static readonly int[] All = [.. Enumerable.Range(0, GameMachines.Count)];

    /// <summary>The window's sizes and interface scales: the least; full HD; a wide screen maximised; a wide, short
    /// window (2556 x 947) at 100%, 125% and 130%, and its size in the units of 125% and 130%; a very
    /// wide screen; wide and as low as allowed; a 1440 screen maximised.</summary>
    public static TheoryData<int, int, int> Sizes => new()
    {
        { 1280, 720, 50 }, { 1280, 720, 100 }, { 1280, 800, 100 }, { 1280, 800, 130 }, { 1280, 720, 250 }, { 1920, 1080, 100 }, { 2560, 1080, 100 }, { 2556, 947, 100 }, { 2045, 758, 100 },
        { 2556, 947, 125 }, { 2556, 947, 130 }, { 3440, 1440, 100 }, { 2560, 720, 100 }, { 1966, 728, 100 }, { 2560, 1392, 130 }, { 2560, 1440, 130 }, { 2560, 1392, 100 },
    };

    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State(int scale)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "interfaceScale": """ + scale +
            """, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data);
    }

    /// <summary>A control's place in the window.</summary>
    private static Rect In(Window window, Control control) =>
        new(control.TranslatePoint(default, window)!.Value, control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value);

    /// <summary>The Garage laid out without overlaps at this size, in both phases. SELECT: every machine of the
    /// shelf (all 41) shows at once, inside the page, and the page does not scroll. PREVIEW: the stage keeps its least
    /// height and ends inside the page, the panel floats 16 px in from its edge and ends above its foot, and the
    /// colour wheel inside it.</summary>
    private static void Fits(Window window, GarageView garage, string where)
    {
        var model = (GarageViewModel)garage.DataContext!;
        var page = garage.FindAncestorOfType<ScrollViewer>()!;
        var view = In(window, page);
        var at = $"{where} at {window.Width} x {window.Height}";

        model.BackCommand.Execute(null);
        PumpFor(150);
        var tiles = garage.RosterCards().Where(b => b.DataContext is GarageCard).ToList();
        Assert.Equal(GameMachines.Count, tiles.Count);
        Assert.Equal(GameMachines.Count, model.Shelved().Count);
        Assert.All(tiles, t => Assert.True(t.IsEffectivelyVisible && t.Bounds.Height > 0, $"{at}: a tile is not shown"));
        var lowest = tiles.Max(t => In(window, t).Bottom);
        Assert.True(lowest <= view.Bottom + 0.5, $"{at}: the roster ends at {lowest}, under the page's foot {view.Bottom}");
        Assert.True(garage.TileHeight >= 48, $"{at}: the tiles are {garage.TileHeight} px high");
        Assert.True(page.Extent.Height <= page.Viewport.Height + 0.5, $"{at}: SELECT scrolls ({page.Extent.Height} in {page.Viewport.Height})");
        // no name cut short: on narrow tiles it takes two lines, or a size smaller (not "Green Pant…")
        var cut = garage.RosterCards().SelectMany(t => t.GetVisualDescendants().OfType<TextBlock>().Where(b => b.Classes.Contains("cardname")))
            .Where(b => b.TextLayout.TextLines.Any(l => l.HasCollapsed)).Select(b => b.Text).ToList();
        Assert.True(cut.Count == 0, $"{at}: names cut: {string.Join(", ", cut)}");

        model.PreviewMachineCommand.Execute(model.MachineIndex);
        PumpFor(150);
        Assert.True(model.Previewing, $"{at}: no PREVIEW");
        var stage = In(window, Named<Control>(garage, "Stage"));
        var panel = In(window, Named<Control>(garage, "SidePanel"));
        var wheel = In(window, Named<Control>(garage, "Wheel"));
        Assert.True(stage.Height >= 260, $"{at}: the stage is {stage.Height} px high");
        Assert.True(stage.Bottom <= view.Bottom + 0.5, $"{at}: the stage ends at {stage.Bottom}, under the page's foot {view.Bottom}");
        Assert.True(panel.Bottom <= stage.Bottom, $"{at}: the panel ends at {panel.Bottom}, under the stage's foot {stage.Bottom}");
        Assert.True(wheel.Bottom <= panel.Bottom, $"{at}: the colour wheel ends at {wheel.Bottom}, under the panel's foot {panel.Bottom}");
        // (16 px of the page's own units: at another interface scale more or fewer of the window's)
        var inset = garage.TranslatePoint(new Point(16, 0), window)!.Value.X - garage.TranslatePoint(default, window)!.Value.X;
        Assert.True(panel.Right <= stage.Right - inset + 0.5, $"{at}: the panel ends at {panel.Right}, not 16 px in from the stage's edge {stage.Right}");
        Assert.True(page.Extent.Height <= page.Viewport.Height + 0.5, $"{at}: PREVIEW scrolls ({page.Extent.Height} in {page.Viewport.Height})");
        model.BackCommand.Execute(null);
        PumpFor(100);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task TheGarageTabFits(int width, int height, int scale)
    {
        var state = State(scale);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = width, Height = height };
                window.Show();
                main.Current = main.Garage;
                PumpFor(200);
                Fits(window, window.GetVisualDescendants().OfType<GarageView>().Single(), "the Garage tab");
                window.Close();
                main.Dispose();
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            state.Shutdown();
            await ui.DisposeAsync();
        }
    }

    /// <summary>One resize is enough (a window resized once could keep its old heights, the colour
    /// wheel and its box cut off, until a second resize): the Garage resized from the least size is laid out as one
    /// opened at the new size.</summary>
    [Fact]
    public async Task OneResizeFitsTheGarageAsAWindowOpenedAtThatSize()
    {
        var state = State(100);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                (double Stage, double Wheel, (int, double, double) Roster) Open(int width, int height, int? toWidth = null, int? toHeight = null)
                {
                    var main = new MainViewModel(state);
                    var window = new MainWindow { DataContext = main, Width = width, Height = height };
                    window.Show();
                    main.Current = main.Garage;
                    PumpFor(200);
                    if (toWidth is { } w && toHeight is { } h)
                    {
                        (window.Width, window.Height) = (w, h);
                        PumpFor(200);
                    }
                    var garage = window.GetVisualDescendants().OfType<GarageView>().Single();
                    Fits(window, garage, "the Garage tab, resized");
                    var sizes = (Named<Control>(garage, "Stage").Height, Named<Control>(garage, "Wheel").Height, (garage.RosterColumns, garage.TileWidth, garage.TileHeight));
                    window.Close();
                    main.Dispose();
                    return sizes;
                }
                var opened = Open(1440, 860);
                Assert.Equal(opened, Open(1280, 720, 1440, 860));
                Assert.Equal(Open(1280, 720), Open(1440, 860, 1280, 720));
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            state.Shutdown();
            await ui.DisposeAsync();
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task TheLobbysMachineStageFits(int width, int height, int scale)
    {
        await using var server = new LedServer();
        var cancel = TestContext.Current.CancellationToken;
        var state = State(scale);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state);
                window = new MainWindow { DataContext = main, Width = width, Height = height };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var connections = server.Connections;
                var joining = state.JoinAsync(server.Address);
                Pump(() => server.Connections == connections + 1 && server.Of<Hello>().Count == 1, "the launcher did not connect");
                server.SendAsync(LedServer.Welcome(setting: SettingRange.Free)).GetAwaiter().GetResult();
                Pump(() => server.Of<EnterQuickPlay>().Count == 1, "the launcher did not queue");
                server.SendAsync(LedServer.Lobby(LobbyState.Forming)).GetAwaiter().GetResult();
                PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby is not null);
                main.Current = main.Lobby;
                server.SendAsync(new ChooseMachine("m1", 21, All, 40_000), LedServer.Lobby(LobbyState.Choosing)).GetAwaiter().GetResult();
                PumpTelling(state, () => main.Lobby.ChoiceShown && main.Lobby.GarageShown);
                PumpFor(200, state);
                Fits(window, window.GetVisualDescendants().OfType<GarageView>().Single(), "the lobby's machine stage");
            }, cancel);
        }
        finally
        {
            await state.LeaveAsync();
            state.Shutdown();
            await Task.Delay(300, CancellationToken.None);
            await ui.Dispatch(() =>
            {
                window?.Close();
                main?.Dispose();
            }, CancellationToken.None);
            await ui.DisposeAsync();
        }
    }

    /// <summary>Ways to the machine stage: the window maximised, then restored; the stage begun on another tab;
    /// the window made larger and back; the Garage tab first; the lobby's Garage opened while gathering, and so in a
    /// larger window that is then restored.</summary>
    public static TheoryData<string> Ways => new() { "big-then-small", "away-then-back", "small-then-big-then-small", "garage-tab-first", "opened-while-gathering", "opened-while-gathering-big" };

    /// <summary>The machine stage after the window or the page changed under it (at 130%, a common scaled display).</summary>
    [Theory]
    [MemberData(nameof(Ways))]
    public async Task TheLobbysMachineStageFitsAfter(string way)
    {
        await using var server = new LedServer();
        var cancel = TestContext.Current.CancellationToken;
        var state = State(130);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state);
                window = new MainWindow { DataContext = main, Width = 2556, Height = way.StartsWith("big") || way.EndsWith("-big") ? 1392 : 947 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var connections = server.Connections;
                var joining = state.JoinAsync(server.Address);
                Pump(() => server.Connections == connections + 1 && server.Of<Hello>().Count == 1, "the launcher did not connect");
                server.SendAsync(LedServer.Welcome(setting: SettingRange.Free)).GetAwaiter().GetResult();
                Pump(() => server.Of<EnterQuickPlay>().Count == 1, "the launcher did not queue");
                server.SendAsync(LedServer.Lobby(LobbyState.Forming)).GetAwaiter().GetResult();
                PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby is not null);
                main.Current = way == "garage-tab-first" ? main.Garage : main.Lobby;
                PumpFor(100, state);
                if (way == "away-then-back") { main.Current = main.Garage; PumpFor(100, state); }
                if (way.StartsWith("opened-while-gathering")) { Click(window, Named<Button>(window, "Face")); PumpFor(200, state); }
                if (way.StartsWith("small-then-big")) { window.Height = 1392; PumpFor(100, state); }
                server.SendAsync(new ChooseMachine("m1", 21, All, 40_000), LedServer.Lobby(LobbyState.Choosing)).GetAwaiter().GetResult();
                PumpTelling(state, () => main.Lobby.ChoiceShown && main.Lobby.GarageShown);
                PumpFor(200, state);
                if (way.StartsWith("opened-while-gathering")) { if (way.EndsWith("-big")) { window.Height = 947; } PumpFor(200, state); }
                else if (way != "away-then-back" && way != "garage-tab-first") { window.Height = 947; PumpFor(200, state); }
                else { main.Current = main.Lobby; PumpFor(200, state); }
                Fits(window, window.GetVisualDescendants().OfType<GarageView>().Single(), "the lobby's machine stage " + way);
            }, cancel);
        }
        finally
        {
            await state.LeaveAsync();
            state.Shutdown();
            await Task.Delay(300, CancellationToken.None);
            await ui.Dispatch(() =>
            {
                window?.Close();
                main?.Dispose();
            }, CancellationToken.None);
            await ui.DisposeAsync();
        }
    }
}
