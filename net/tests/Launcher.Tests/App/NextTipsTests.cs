using Avalonia.Threading;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using static Launcher.Tests.Headless;
using static Launcher.Tests.NextHeadless;

namespace Launcher.Tests;

/// <summary>
/// The redesigned interface's tips ("Experimental UI" on): every option row of every option page (Settings, the
/// Controller page, About, Practice, a custom lobby's rules with each of the stand-in's modes) says what it does in its
/// help line, which is also the row's tip: no general stand-in ("A setting of this mode."), nothing empty, a whole
/// sentence. And the Settings window has more to say of every setting and group (<see cref="SettingFacts"/>).
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextTipsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data, standIn: true);
    }

    [Fact]
    public async Task EveryOptionRowSaysWhatItDoes()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var items = new List<OptionItem>();
            var settings = main.Shell.SettingsPage.Categories.SelectMany(c => c.Groups).SelectMany(g => g.Items).ToList();
            items.AddRange(settings);
            // the Settings window says more of every setting: when it takes effect, what it applies to, a tip
            var bare = settings.Where(i => SettingFacts.WhenOf(i.Label).Length == 0 || SettingFacts.WhereOf(i.Label).Length == 0
                || SettingFacts.TipOf(i.Label).Length == 0).Select(i => i.Label).Distinct().ToList();
            Assert.True(bare.Count == 0, "settings the window says little of: " + string.Join(", ", bare));
            Assert.All(main.Shell.SettingsPage.Categories, c => Assert.NotEmpty(SettingFacts.GroupTipOf(c.Title)));
            items.AddRange(main.Shell.ControllerPage.Groups.SelectMany(g => g.Items));
            items.AddRange(main.Shell.About.Switches.SelectMany(g => g.Items));
            items.AddRange(OptionGroup.Of(main.Practice.Groups, "Practice").SelectMany(g => g.Items));

            // a custom lobby's rules, with each mode the stand-in offers
            main.Current = main.Lobbies;
            PumpTelling(state, () => main.Lobbies.Status.Length == 0, "the stand-in's list did not come", 15);
            main.Lobbies.ShowCreateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var rules = Assert.IsType<RulesPageViewModel>(main.Shell.CreateRules);
            var mode = rules.Editor.ModeRow;
            for (var i = 0; i < mode.Options.Count; i++)
            {
                mode.Selected = i;
                Dispatcher.UIThread.RunJobs();
                items.AddRange(rules.Basic.Concat(rules.More).SelectMany(g => g.Items));
            }

            var unsaid = items
                .Where(i => i.Help.Length == 0 || i.Help == RuleHelp.General || !i.Help.TrimEnd().EndsWith('.'))
                .Select(i => $"{i.Group} › {i.Label}: \"{i.Help}\"")
                .Distinct()
                .ToList();
            Assert.True(unsaid.Count == 0, "rows without their own line:\n" + string.Join('\n', unsaid));
        });
    }
}
