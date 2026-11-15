using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.Tests;

/// <summary>
/// The Settings page's window on the right (<see cref="SettingInfo"/>): a group's settings and drawing, a row's value
/// now and how it is changed.
/// </summary>
public sealed class SettingInfoTests
{
    [Fact]
    public void TheSettingsWindowSaysWhatAGroupHoldsAndHowARowIsChanged()
    {
        var sound = new OptionGroup("", [new OptionItem(new SwitchRule("Mute launcher sounds", false), "Sound"),
            new OptionItem(new StepperRule("Launcher sounds", 50, 0, 100), "Sound")]);
        var group = SettingInfo.Of(new SettingsCategory("Sound", "Other racers' sounds and the launcher's own", [sound]));
        Assert.Equal(["Mute launcher sounds", "Launcher sounds"], group.Rows.Select(r => r.Label));
        Assert.False(group.HasItem);
        Assert.Equal(SettingInfo.IconOf("Sound"), group.Icon);
        var row = SettingInfo.Of(sound.Items[0], "Sound");
        Assert.Same(sound.Items[0], row.Item);
        Assert.False(row.HasRows);
        Assert.StartsWith("Left and right change it", row.HowTo, StringComparison.Ordinal);
        // every group its own drawing
        string[] groups = ["Basics", "Display", "Graphics", "Sound", "Controller", "Online", "Social", "Launcher", "Replays"];
        Assert.Equal(groups.Length, groups.Select(SettingInfo.IconOf).Distinct().Count());
        Assert.All(groups, g => Assert.StartsWith("M", SettingInfo.IconOf(g), StringComparison.Ordinal));
    }

    /// <summary>The window is not three lines (the owner, 2026-11-15): a row's every choice with the one chosen
    /// marked, or a number's place between its ends; when it takes effect, what it applies to, a number's range;
    /// something good to know; a group's rows each with its value now.</summary>
    [Fact]
    public void TheSettingsWindowSaysEveryChoiceWhenItHoldsAndSomethingGoodToKnow()
    {
        var at = 1;
        var widescreen = new OptionItem(new ChoiceRow("Widescreen", () => ["Off", "Game's", "True"], () => at, i => at = i), "Display");
        var info = SettingInfo.Of(widescreen, "Display");
        Assert.Equal([("Off", false), ("Game's", true), ("True", false)], info.Choices.Select(c => (c.Text, c.On)));
        Assert.False(info.HasMeter);
        Assert.Contains(new InfoFact("Takes effect", "The game's next start"), info.Facts);
        Assert.Contains(new InfoFact("Applies to", "The game"), info.Facts);
        Assert.True(info.HasTip);
        var volume = 40;
        var menu = new OptionItem(new NumberRow("Menu sounds", () => volume, v => volume = v, 0, 100, 5, "%"), "Sound");
        info = SettingInfo.Of(menu, "Sound");
        Assert.Equal(0.4, info.Share!.Value, 3);
        Assert.Equal(("0%", "100%"), (info.Low, info.High));
        Assert.Contains(new InfoFact("Range", "0% to 100%, in steps of 5"), info.Facts);
        Assert.Contains(new InfoFact("Takes effect", "At once"), info.Facts);
        Assert.Empty(info.Choices);
        var group = SettingInfo.Of(new SettingsCategory("Sound", "Sounds", [new OptionGroup("", [menu])]));
        Assert.Equal([new InfoRow("Menu sounds", "40%")], group.Rows);
        Assert.Contains(new InfoFact("Takes effect", "At once"), group.Facts);
    }
}
