namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// Developer mode's keys at the rows (owner's plan, section 4): a setting's key in the game's launch file
/// ("launch · auroraSize"), a custom lobby's rule's key as the server has it ("rule · course-select"), the launcher's own
/// settings ("launcher setting · experimentalUi"); by the row's label. A row without one has none.
/// </summary>
public static class DevKeys
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["Resolution"] = "launch · auroraSize",
        ["Anti-aliasing"] = "launch · auroraEdges",
        ["Anisotropic filtering"] = "launch · auroraTextures",
        ["Ray tracing"] = "launch · auroraRayTracing",
        ["Colourblind filter"] = "launch · auroraColourblind",
        ["High refresh"] = "launch · auroraHighRefresh",
        ["Rumble"] = "launch · rumble",
        ["Course"] = "rule · course-select",
        ["Laps"] = "rule · laps",
        ["Racers"] = "rule · racers",
        ["CPU racers"] = "rule · cpu-fill",
        ["CPU level"] = "rule · cpu-level",
        ["Boost from the start"] = "rule · boost-start",
        ["Finish limit"] = "rule · finish-limit",
        ["Mines respawn"] = "rule · mine-regen",
        ["Machine setting"] = "rule · machine-setting",
        ["Stats"] = "rule · stat-pack",
        ["Mixed packs"] = "rule · mixed-packs",
        ["Vote"] = "rule · vote-seconds",
        ["Machine choice"] = "rule · choose-seconds",
        ["Voting minimum"] = "rule · voting-minimum",
        ["Latency limit"] = "rule · latency-limit",
        ["Experimental UI"] = "launcher setting · experimentalUi",
        ["Developer mode"] = "launcher setting · developerMode",
        ["Button icons"] = "launcher setting · buttonIcons",
        ["Confirm button"] = "launcher setting · confirmButton",
    };

    /// <summary>The row's key in words; "" none.</summary>
    public static string For(string label) => Keys.TryGetValue(label ?? "", out var key) ? key : "";
}
