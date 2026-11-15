using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Controls.NextUi;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using Launcher.App.Views;
using Launcher.App.Views.NextUi;
using Launcher.Core;
using Launcher.Core.Pads;
using static Launcher.Tests.Headless;
using static Launcher.Tests.NextHeadless;

namespace Launcher.Tests;

/// <summary>
/// The redesigned Settings and Controller ("Experimental UI" on), in the launcher's own window: Settings in steps (the
/// groups first, a group's option rows with the help panel for the row the focus is on, B back onto the group it came
/// from); Graphics setting
/// the three settings together and saying Custom when they part; the keyboard on the screen for the name (Done saves,
/// Escape cancels) and for any text box the pad presses; the Controller page's rows; the button icons in the hints
/// (a family chosen, the confirm button on the right). By themselves: the families' icons and the confirm button's
/// swap. With <c>NEXT_SHOTS</c> naming a folder, the pages there as PNGs.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}}""");
        return AppState.Load(data, standIn: true);
    }

    [Fact]
    public async Task SettingsAreGroupsInStepsAndGraphicsSetsThreeAtOnce()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            Click(window, Named<Button>(Shell(window), "Settings"));
            Assert.Same(main.Settings, main.Current);
            var page = Page<SettingsPageView>(window);
            var rows = main.Shell.SettingsPage;
            // the first step: the groups, each with what is in it
            Assert.Equal(["Basics", "Display", "Graphics", "Sound", "Controller", "Online", "Social", "Launcher", "Replays"],
                rows.Categories.Select(c => c.Title));
            Assert.All(rows.Categories, c => Assert.NotEmpty(c.Line));
            Assert.False(rows.IsOpen);
            Assert.Equal(["Open", "Back"], Hints(main));
            Assert.False(Named<Button>(Shell(window), "Back").IsEffectivelyVisible);
            Shot(window, state, "next-settings-groups");
            // Basics: its six rows
            Click(window, Shown(Said(page, "Basics")));
            Assert.True(rows.IsOpen);
            Assert.Equal("BASICS", Named<TextBlock>(page, "Title").Text);
            Assert.Equal(["Change", "Select", "Back"], Hints(main));
            Assert.True(Named<Button>(Shell(window), "Back").IsEffectivelyVisible);
            Assert.Equal(["Volume", "Graphics", "Full screen mode", "Input delay", "Rumble", "Controller"], rows.Basic[0].Items.Select(i => i.Label));
            // Graphics: Balanced (the default); left: Performance, its three settings with it
            var graphics = Row(page, "Graphics");
            Focus(graphics);
            Assert.Equal("Graphics", rows.Focused?.Label);
            Assert.StartsWith("One choice for how the game looks", Named<TextBlock>(page, "HelpText").Text, StringComparison.Ordinal);
            Assert.Equal("Balanced", rows.Focused!.ValueText);
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal("Performance", rows.Focused.ValueText);
            Assert.Equal("2", state.Settings.AuroraSize);
            Shot(window, state, "next-settings");

            // Escape (as B): the groups again, the focus on Basics
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(rows.IsOpen);
            Assert.Same(main.Settings, main.Current);
            Assert.True(Said(page, "Basics").IsFocused);
            // Graphics: the resolution one by one, and Graphics says Custom
            Click(window, Shown(Said(page, "Graphics")));
            var resolution = Row(page, "Resolution");
            Focus(resolution);
            Assert.Equal("2x", rows.Focused?.ValueText);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal("3", state.Settings.AuroraSize);
            Assert.Equal("Custom", rows.Basic[0].Items[1].ValueText);
            // Social: a switch, right turns it on
            Press(window, Key.Escape, PhysicalKey.Escape);
            Click(window, Shown(Said(page, "Social")));
            Focus(Row(page, "Hide swear words"));
            var before = state.Settings.HideSwearWords;
            Press(window, before ? Key.Left : Key.Right, before ? PhysicalKey.ArrowLeft : PhysicalKey.ArrowRight);
            Assert.NotEqual(before, state.Settings.HideSwearWords);
            Shot(window, state, "next-settings-advanced");

            // the Controller row (Basics): its page, and Back to Settings, Basics still open
            Press(window, Key.Escape, PhysicalKey.Escape);
            Click(window, Shown(Said(page, "Basics")));
            Focus(Row(page, "Controller"));
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.Same(main.Controller, main.Current);
            var pad = Page<ControllerPageView>(window);
            Assert.Equal(["Port", "Input", "Device", "Button icons", "Confirm button", "Test rumble", "Set up in the game", "Stick dead zone", "C-Stick dead zone"],
                main.Shell.ControllerPage.Groups[0].Items.Select(i => i.Label));
            Assert.Equal([.. PadControls.All.Select(c => c.Label), "Reset the buttons"], main.Shell.ControllerPage.Groups[1].Items.Select(i => i.Label));
            Focus(Row(pad, "Port"));
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal(1, main.Controller.PortIndex);
            Shot(window, state, "next-controller");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Same(main.Settings, main.Current);
            Assert.Equal("Basics", rows.Open?.Title);
        });
    }

    [Fact]
    public async Task TheKeyboardOnTheScreenTypesTheNameAndAnyTextBox()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            Click(window, Named<Button>(Shell(window), "Settings"));
            var page = Page<SettingsPageView>(window);
            Click(window, Shown(Said(page, "Online")));
            Focus(Row(page, "Your name"));
            Press(window, Key.Enter, PhysicalKey.Enter);
            var keyboard = Assert.IsType<KeyboardViewModel>(main.Shell.Keyboard);
            Assert.Equal("ANA", keyboard.Text);
            Assert.Equal(["Type", "Done", "Cancel"], main.Shell.Hints.Select(h => h.Label));
            var layer = Named<Panel>(Shell(window), "KeyboardLayer");
            Assert.True(layer.IsEffectivelyVisible);
            Click(window, Said(layer, "Delete a character"));
            Click(window, Said(layer, "Delete a character"));
            Click(window, Said(layer, "Delete a character"));
            Click(window, Said(layer, "b"));
            Click(window, Said(layer, "o"));
            Click(window, Said(layer, "b"));
            Assert.Equal("bob", keyboard.Text);
            Shot(window, state, "next-keyboard");
            Click(window, Said(layer, "Done typing"));
            Assert.Null(main.Shell.Keyboard);
            Assert.Equal("BOB", state.Settings.Racer.Name);

            // Escape: closed, nothing changed
            Focus(Row(page, "Your name"));
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.NotNull(main.Shell.Keyboard);
            main.Shell.Keyboard!.Text = "ZED";
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Null(main.Shell.Keyboard);
            Assert.Equal("BOB", state.Settings.Racer.Name);

            // a text box the pad presses (the Garage's search): the keyboard for it, its text in the box
            Click(window, Named<StackPanel>(Shell(window), "Areas").GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Garage"));
            var search = Named<TextBox>(Page<GaragePageView>(window), "Search");
            Assert.True(PadNav.IsTarget(search));
            Assert.True(PadActions.Press(search));
            Assert.Equal("Search machines", main.Shell.Keyboard?.Title);
            main.Shell.Keyboard!.Text = "falcon";
            Click(window, Said(Named<Panel>(Shell(window), "KeyboardLayer"), "Done typing"));
            Assert.Equal("falcon", main.Garage.Query);
        });
    }

    [Fact]
    public async Task TheHintsShowTheChosenFamilysIconsAndTheConfirmButtonsPlace()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var navigator = window.Pad!;
            Click(window, Named<Button>(Shell(window), "Settings"));
            navigator.Act(new PadMoved(PadMove.Down, Repeat: false));     // (the pad in use: its icons)
            Assert.True(main.Shell.PadInUse);
            Assert.Equal("A", main.Shell.Hints.Single(h => h.Label == "Open").Glyph);
            main.Settings.ButtonIconsIndex = (int)ButtonIcons.PlayStation;
            Pump(() => main.Shell.Hints.Single(h => h.Label == "Open").Glyph == "✕", "the hints did not take PlayStation's icons");
            Assert.Equal("○", main.Shell.Hints.Single(h => h.Label == "Back").Glyph);
            main.Settings.ConfirmButtonIndex = (int)ConfirmButton.Right;
            Pump(() => main.Shell.Hints.Single(h => h.Label == "Open").Glyph == "○", "the confirm button did not move right");
            Assert.Equal(ButtonIcons.PlayStation, state.Settings.ButtonIcons);
            Assert.Equal(ConfirmButton.Right, state.Settings.ConfirmButton);
            Shot(window, state, "next-settings-icons");
        });
    }

    [Fact]
    public void EachFamilyHasItsIconsAndTheRightConfirmButtonSwapsAAndB()
    {
        Assert.Equal(ButtonIcons.Xbox, PadIcons.FamilyOf("SDL/0/Xbox Series X Controller", adapter: false));
        Assert.Equal(ButtonIcons.PlayStation, PadIcons.FamilyOf("SDL/0/DualSense Wireless Controller", adapter: false));
        Assert.Equal(ButtonIcons.Nintendo, PadIcons.FamilyOf("SDL/0/Nintendo Switch Pro Controller", adapter: false));
        Assert.Equal(ButtonIcons.GameCube, PadIcons.FamilyOf("", adapter: true));
        Assert.Equal(ButtonIcons.GameCube, PadIcons.FamilyOf("SDL/0/Nintendo GameCube Controller", adapter: false));
        Assert.Equal(ButtonIcons.GameCube, PadIcons.FamilyOf("DInput/0/MAYFLASH GameCube Controller Adapter", adapter: false));
        Assert.Equal(ButtonIcons.GameCube, PadIcons.FamilyOf("SDL/1/WUP-028", adapter: false));
        Assert.Equal(ButtonIcons.Other, PadIcons.FamilyOf("SDL/0/Some Stick", adapter: false));
        Assert.Equal(["A", "B", "X", "Y", "☰"], new[] { PadButton.A, PadButton.B, PadButton.X, PadButton.Y, PadButton.Start }
            .Select(b => PadIcons.Glyph(b, ButtonIcons.Xbox, confirmRight: false)));
        Assert.Equal(["A", "B", "Y", "X", "+"], new[] { PadButton.A, PadButton.B, PadButton.X, PadButton.Y, PadButton.Start }
            .Select(b => PadIcons.Glyph(b, ButtonIcons.Nintendo, confirmRight: true)));
        Assert.Equal(["1", "2"], new[] { PadButton.A, PadButton.B }.Select(b => PadIcons.Glyph(b, ButtonIcons.Other, confirmRight: false)));
        // L and R are where the game's standard layout puts them: a pad's triggers (Z its right shoulder)
        Assert.Equal(("LT", "RT"), PadIcons.Shoulders(ButtonIcons.Xbox));
        Assert.Equal(("L2", "R2"), PadIcons.Shoulders(ButtonIcons.PlayStation));
        Assert.Equal(("ZL", "ZR"), PadIcons.Shoulders(ButtonIcons.Nintendo));
        Assert.Equal(("L", "R"), PadIcons.Shoulders(ButtonIcons.GameCube));
        Assert.Equal(["RB", "R1", "Z"], new[] { ButtonIcons.Xbox, ButtonIcons.PlayStation, ButtonIcons.GameCube }
            .Select(f => PadIcons.Glyph(PadButton.Z, f, confirmRight: false)));
        // Nintendo's confirm is on the right by itself; the pad's A and B change places then (the adapter's stay)
        Assert.True(PadIcons.ConfirmRight(ConfirmButton.Auto, ButtonIcons.Nintendo));
        Assert.False(PadIcons.ConfirmRight(ConfirmButton.Auto, ButtonIcons.Xbox));
        Assert.Equal(PadButton.B, PadIcons.Swap(PadButton.A, confirmRight: true, ButtonIcons.Nintendo));
        Assert.Equal(PadButton.A, PadIcons.Swap(PadButton.A, confirmRight: true, ButtonIcons.GameCube));
        Assert.Equal(PadButton.X, PadIcons.Swap(PadButton.X, confirmRight: true, ButtonIcons.Xbox));
    }
}
