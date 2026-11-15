using System.Buffers.Binary;
using System.Text;
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
using static Launcher.Tests.Headless;
using static Launcher.Tests.NextHeadless;

namespace Launcher.Tests;

/// <summary>
/// The redesigned Replays, More and profile ("Experimental UI" on), in the launcher's own window: Replays with its
/// tabs and its groups, a replay's panel (its facts, Results, Delete after a question); More with Stats (the large
/// figures, More numbers), Mods (nothing imported yet) and About (the switches as option rows, the licences); the
/// profile from the top bar's chip (the name, the dot, the figures, the recent races, Change name in a panel, Back to
/// where it was opened). With <c>NEXT_SHOTS</c> naming a folder, the pages there as PNGs.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextMoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string ReplayFolder => Path.Combine(_dir, "data", "replays");

    private const string Results = """
        {"results":[{"seat":0,"rank":1,"finished":true,"out":false,"laps":3,"timeMs":131930,"bestLapMs":43001}]}
        """;

    /// <summary>A replay of one racer, with results (as ReplayTabsTests writes them); a practice run when
    /// <paramref name="practice"/>.</summary>
    private void Write(string name, string recorded, bool practice = false)
    {
        var header = $$$"""
            {"match": {"matchId": "x", "seat": 0, "setup": {"course": 5, "laps": 3, "seats": [
              {"seat": 0, "playerId": "p1", "name": "ANA", "machine": 1}]}},
             "seat": 0, "mask": 1, "delay": 2, "recorded": "{{{recorded}}}"{{{(practice ? ", \"practice\": true" : "")}}}}
            """;
        Directory.CreateDirectory(ReplayFolder);
        var json = Encoding.UTF8.GetBytes(header);
        using var f = File.Create(Path.Combine(ReplayFolder, name));
        var w = new BinaryWriter(f);
        Span<byte> u32 = stackalloc byte[4];
        w.Write("FZRP"u8);
        w.Write((byte)0);
        w.Write((byte)2);
        BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)json.Length);
        w.Write(u32);
        w.Write(json);
        w.Write("FZRF"u8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 1);
        w.Write(u32);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 0);
        w.Write(u32);
        w.Write("FZRL"u8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 122);
        w.Write(u32);
        w.Write(new byte[122]);
        w.Write(new byte[600 * 10]);
        var block = Encoding.UTF8.GetBytes(Results);
        w.Write(block);
        BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)block.Length);
        w.Write(u32);
        w.Write("FZRS"u8);
    }

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}}""");
        Write("a.fzrp", "2026-11-14T18:00:00Z");
        Write("b.fzrp", "2026-11-13T18:00:00Z");
        Write("c.fzrp", "2026-11-14T17:00:00Z", practice: true);
        return AppState.Load(data, standIn: true);
    }

    private static void Area(MainWindow window, string name) =>
        Click(window, Named<StackPanel>(Shell(window), "Areas").GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name));

    [Fact]
    public async Task ReplaysListInGroupsAndAReplaysPanelDeletesItAfterAQuestion()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var replays = main.Replays;
            Area(window, "Replays");
            Assert.Same(replays, main.Current);
            var page = Page<ReplaysListView>(window);
            Assert.Equal("YOUR RACES", Named<TextBlock>(page, "Title").Text);
            Pump(() => !replays.Loading && replays.Items.Count == 3, "the replays were not read");
            Assert.Equal([3, 2, 1], replays.Tabs.Select(t => t.Count));
            Assert.Equal(["Open", "Filter", "Sections", "Back"], Hints(main));
            Dispatcher.UIThread.RunJobs();
            var rows = Named<ItemsControl>(page, "ReplayList").GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(3, rows.Count);
            Shot(window, state, "next-replays");

            // the Races tab: the two races
            Click(window, Said(page, "Races"));
            Pump(() => replays.Items.Count == 2);

            // a row pressed: its panel, with its facts and its results
            Click(window, Named<ItemsControl>(page, "ReplayList").GetVisualDescendants().OfType<Button>().First());
            var sheet = Assert.IsType<ReplaySheetViewModel>(main.Shell.Sheet);
            var view = Page<ReplaySheetView>(window);
            Assert.Equal(["Course", "Result", "Racer", "Race", "When"], sheet.Facts.Select(f => f.Name));
            Click(window, Said(view, "Results"));
            Assert.True(sheet.ShowingResults);
            Assert.Single(sheet.Results);
            Shot(window, state, "next-replay-sheet");

            // Delete: asked first; Escape keeps it, then Delete removes the file and closes the panel
            var path = sheet.Item.Path;
            Click(window, Said(view, "Delete"));
            Assert.True(main.Shell.AskingConfirm);
            Assert.Equal("Delete this replay?", main.Shell.ConfirmTitle);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.AskingConfirm);
            Assert.True(File.Exists(path));
            Click(window, Said(Page<ReplaySheetView>(window), "Delete"));
            Click(window, Named<Button>(Shell(window), "ConfirmDo"));
            Assert.False(File.Exists(path));
            Assert.Null(main.Shell.Sheet);
            Assert.Single(replays.Items);
        });
    }

    [Fact]
    public async Task MoreHoldsStatsModsAndAboutWithItsSwitches()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            Area(window, "More");
            Assert.Same(main.Shell.More, main.Current);
            Assert.Equal(["Mods", "Stats", "About"], main.Shell.More.Rows.Select(r => r.Label));

            // Stats: three large figures, More numbers for the others
            Click(window, Said(Shell(window), "Stats"));
            var stats = Page<StatsPageView>(window);
            Pump(() => !main.Stats.Reading && main.Stats.Tiles.Count > 0, "the figures were not read");
            Assert.Equal(["Races", "Wins", "Podiums"], main.Stats.Highlights.Select(t => t.Label));
            Assert.False(Named<ItemsControl>(stats, "MoreFigures").IsVisible);
            Click(window, Said(stats, "More numbers"));
            Assert.True(Named<ItemsControl>(stats, "MoreFigures").IsVisible);
            Assert.Equal(["Average place", "Knocked out"], main.Stats.OtherTiles.Select(t => t.Label));
            Shot(window, state, "next-stats");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Same(main.Shell.More, main.Current);

            // Mods: none yet, the import's two ways
            Click(window, Said(Shell(window), "Mods"));
            var mods = Page<ModsListView>(window);
            Assert.True(Named<TextBlock>(mods, "NoMods").IsEffectivelyVisible);
            Assert.True(Named<Button>(mods, "Import").IsEffectivelyEnabled);
            Assert.Equal(["Options", "Back"], Hints(main));
            Press(window, Key.Escape, PhysicalKey.Escape);

            // About: the version, the switches as rows (the Settings page's own), the licences
            Click(window, Said(Shell(window), "About"));
            var about = Assert.IsType<AboutViewModel>(main.Current);
            var page = Page<AboutView>(window);
            Assert.Equal(ShellViewModel.MoreArea, main.Shell.Area);
            Assert.True(main.Shell.BackShown);
            Assert.Contains(BuildInfo.Version, Named<TextBlock>(page, "Version").Text, StringComparison.Ordinal);
            Assert.Equal(["Update automatically", "Developer mode"], about.Switches[0].Items.Select(i => i.Label));   // (Experimental UI: Settings' alone)
            Assert.False(main.Settings.DeveloperMode);
            var developer = page.GetVisualDescendants().OfType<OptionButton>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Developer mode");
            developer.Focus(NavigationMethod.Directional);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.True(main.Settings.DeveloperMode);
            Assert.True(state.Settings.DeveloperMode);
            Shot(window, state, "next-about");
            var licencesRow = Said(page, "Licences");
            licencesRow.BringIntoView();                                   // (at the page's foot)
            Click(window, licencesRow);
            var licences = Assert.IsType<LicencesSheetViewModel>(main.Shell.Sheet);
            Assert.Equal(3, licences.Entries.Count);
            Assert.All(licences.Entries.Skip(1), e => Assert.Contains("Permission", e.Text, StringComparison.OrdinalIgnoreCase));
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Null(main.Shell.Sheet);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<OptionButton>(), b => AutomationProperties.GetName(b) == "Experimental UI");
        });
    }

    /// <summary>A mod of one part, its files' bytes their disc paths' (as ModsPageTests makes them up).</summary>
    private static void MadeUpMod(string mods, string id, string name, params ModFile[] files)
    {
        var folder = Path.Combine(mods, id);
        Directory.CreateDirectory(Path.Combine(folder, ModManifest.StoreFolder));
        var stored = files.Select(f =>
        {
            var bytes = Encoding.UTF8.GetBytes(":" + f.Disc);
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            File.WriteAllBytes(Path.Combine(folder, ModManifest.StoreFolder, sha), bytes);
            return f with { Sha256 = sha };
        }).ToList();
        ModLibrary.WriteManifest(folder, new ModManifest(id, name, name + ".zip", "00", DateTimeOffset.UnixEpoch, [new ModPart("", true, stored, [])],
            new Dictionary<string, int>(), null));
    }

    [Fact]
    public async Task AModsPanelSwitchesMovesAndRemovesIt()
    {
        var state = State();
        var data = Path.Combine(_dir, "data");
        var mods = Path.Combine(data, ModLibrary.FolderName);
        MadeUpMod(mods, "aaaaaaaaaaaa", "Looks", new ModFile("stage/st05.tpl.lz", ModFileClass.Look, true, null, false, 3, ""));
        MadeUpMod(mods, "bbbbbbbbbbbb", "Ghosts", new ModFile("staff_ghost/ghost1.dat", ModFileClass.Ghost, false, "Ghosts: single player only", false, 3, ""));
        new ModLibrary(data).Save([.. new ModLibrary(data).Read().OrderBy(m => m.Manifest.Name == "Looks" ? 0 : 1).Select(m => m with { On = true })]);
        await state.LoadModsAsync();
        await InWindow(state, (window, main) =>
        {
            Area(window, "More");
            Click(window, Said(Shell(window), "Mods"));
            var page = Page<ModsListView>(window);
            Pump(() => main.Mods.Cards.Count == 2, "the mods were not listed");
            Dispatcher.UIThread.RunJobs();
            var rows = Named<ItemsControl>(page, "ModRows").GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(["Looks: options", "Ghosts: options"], rows.Select(AutomationProperties.GetName));
            Assert.Equal([1, 2], main.Mods.Cards.Select(c => c.Rank));
            Shot(window, state, "next-mods");

            // the second's panel: off, up, then removed after a question
            Click(window, rows[1]);
            var sheet = Assert.IsType<ModSheetViewModel>(main.Shell.Sheet);
            var view = Page<ModSheetView>(window);
            Assert.Equal("Mod · 2 of 2", sheet.Place);
            Click(window, Said(view, "On"));
            Assert.False(sheet.Card.On);
            Pump(() => state.Mods!.Single(m => m.Manifest.Name == "Ghosts").On == false, "the switch was not kept");
            Click(window, Said(view, "Move up"));
            Pump(() => state.Mods![0].Manifest.Name == "Ghosts", "the order did not change");
            Pump(() => sheet.Place == "Mod · 1 of 2");
            Click(window, Said(Page<ModSheetView>(window), "Remove"));
            Assert.True(main.Shell.AskingConfirm);
            Click(window, Named<Button>(Shell(window), "ConfirmDo"));
            Pump(() => main.Mods.Cards.Count == 1, "the mod was not removed");
            Assert.Null(main.Shell.Sheet);
        });
    }

    [Fact]
    public async Task TheProfileShowsTheRacerAndChangesTheName()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            Area(window, "Garage");
            Click(window, Named<Button>(Shell(window), "Profile"));
            var profile = Assert.IsType<ProfileViewModel>(main.Current);
            var page = Page<ProfileView>(window);
            Assert.Equal("ANA", Named<TextBlock>(page, "PlayerName").Text);
            Assert.Equal("In races it reads ANA", Named<TextBlock>(page, "InRaces").Text);
            Assert.Equal(main.Account.ConnectionText, profile.Status);
            Assert.True(main.Shell.BackShown);
            Assert.Contains("on", Named<Button>(Shell(window), "Profile").Classes);
            Assert.Equal(["Races", "Wins", "Podiums"], profile.Figures.Select(f => f.Label));
            Pump(() => profile.Recent.Count == 2, "the recent races did not come");
            Shot(window, state, "next-profile");

            // Change name: a panel; the name saved as the game shows it
            Click(window, Said(page, "Change name"));
            var sheet = Assert.IsType<NameSheetViewModel>(main.Shell.Sheet);
            var box = Named<TextBox>(Page<NameSheetView>(window), "NameBox");
            box.Text = "bob";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("In races it reads BOB", sheet.Reads);
            Click(window, Said(Page<NameSheetView>(window), "Save the name"));
            Assert.Null(main.Shell.Sheet);
            Assert.Equal("BOB", state.Settings.Racer.Name);
            Assert.Equal("In races it reads BOB", Named<TextBlock>(Page<ProfileView>(window), "InRaces").Text);

            // Back: where the profile was opened from
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Same(main.Garage, main.Current);
        });
    }
}
