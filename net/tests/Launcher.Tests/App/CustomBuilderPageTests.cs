using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>The Garage's builder pressed as a player would, with the headless window's mouse: the Custom shelf, a
/// body and a pilot from their lists, Save; the machine is kept and raced; a change not saved survives opening another
/// machine; a stock machine picked races stock again.</summary>
[Collection(HeadlessApp.Name)]
public sealed class CustomBuilderPageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task AMachineBuiltAndSavedIsTheOneRaced()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}}""");
        var state = AppState.Load(data);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                window.Show();
                main.Current = main.Garage;
                PumpFor(200);
                var garage = window.GetVisualDescendants().OfType<GarageView>().Single();
                Click(window, Named<Button>(garage, "ShelfCustom"));
                Pump(() => main.Garage.CustomShown, "the Custom shelf did not open");
                PumpFor(100);
                Click(window, Named<Button>(garage, "NewMachineTile"));          // New machine, the shelf's last tile: the builder
                Pump(() => main.Garage.Previewing, "New machine did not open the builder");
                PumpFor(100);

                Click(window, Part(window, garage, 0, 2));    // the third body
                Click(window, Part(window, garage, 3, 29));   // the pilot of machine 29
                Pump(() => main.Garage.Builder.Build is { Body: 2, Pilot: 29 }, "the parts were not picked");
                Click(window, Named<Button>(garage, "Save"));

                Pump(() => state.Settings.Racer.Custom is not null, "Save raced nothing");
                Assert.Equal([new Launcher.Core.CustomBuild(2, CustomMachine.Cockpits, CustomMachine.Boosters, 29)],
                    state.Settings.Racer.Customs);
                var raced = state.Settings.Racer.Custom!;
                Assert.Equal((2, CustomMachine.Cockpits, CustomMachine.Boosters, 29), (raced.Body, raced.Cockpit, raced.Booster, raced.Pilot));
                Assert.Equal([0u, 0u, 0u], raced.Colors);   // each part in its stock colour 0
                Assert.Single(main.Garage.Builder.Saved);
                Assert.True(main.Garage.Builder.Saved[0].Raced);

                // a change not saved is kept while another machine is open, and is there again on going back
                Click(window, Part(window, garage, 0, 4));
                Pump(() => main.Garage.Builder.Unsaved, "the change did not show as unsaved");
                Click(window, Named<Button>(garage, "BackButton"));
                PumpFor(100);
                Click(window, Named<Button>(garage, "NewMachineTile"));
                Pump(() => main.Garage.Builder.Build.Body == 0 && main.Garage.Builder.Editing is null, "no new machine");
                Click(window, Named<Button>(garage, "BackButton"));
                PumpFor(100);
                Click(window, window.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is CustomCard { Index: 0 } && b.IsEffectivelyVisible));
                Pump(() => main.Garage.Builder.Editing == 0, "the card did not open");
                Assert.Equal(4, main.Garage.Builder.Build.Body);
                Assert.Equal(2, state.Settings.Racer.Customs![0].Body);   // kept, not saved

                // back to the stock shelf: a machine used there races, the custom one is kept but not raced
                Click(window, Named<Button>(garage, "BackButton"));
                PumpFor(100);
                Click(window, Named<Button>(garage, "ShelfStock"));
                Pump(() => main.Garage.StockShown, "the Stock shelf did not open");
                PumpFor(100);
                Click(window, CardOf(window, 10)!);
                Click(window, Named<Button>(garage, "Use"));
                Pump(() => state.Settings.Racer.Custom is null, "the stock machine did not take the custom one's place");
                Assert.Equal(10, state.Settings.Racer.Machine);
                Assert.Single(state.Settings.Racer.Customs!);
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

    /// <summary>A part's row: its list's tab pressed (Body, Cockpit, Booster, Pilot), the row scrolled into the list's
    /// view (as a player scrolls to it).</summary>
    private static Button Part(MainWindow window, GarageView garage, int column, int id)
    {
        Click(window, garage.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is SectionTab t && t.Index == column));
        PumpFor(100);
        var row = garage.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is PartChoice p && p.Column == column && p.Id == id);
        row.BringIntoView();
        PumpFor(100);
        return row;
    }
}
