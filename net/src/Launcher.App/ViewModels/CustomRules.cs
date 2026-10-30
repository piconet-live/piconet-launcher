using System.Globalization;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// A custom lobby's rules as the launcher says them: the groups the rules panel shows them in, each rule's value in
/// words, and the one short line of the rules that differ from Quick Play's (<see cref="Summary"/>): what the
/// browser's rows and the lobby's title show; the whole list (<see cref="AllRules"/>) is behind a click. A rule a
/// lobby does not name is Quick Play's; Quick Play's are the server's (<see cref="LobbyList.QuickPlay"/>), else the
/// rules' own defaults (<see cref="Defaults"/>).
/// </summary>
public static class CustomRules
{
    /// <summary>The groups of the host's rules, by the rules' names, in order. A settable rule named in none goes in
    /// the first.</summary>
    private static readonly (string Heading, string[] Names)[] Grouped =
    [
        ("Race", ["course-select", "laps", "racers", "cpu-fill", "cpu-level", "boost-start", "finish-limit", "mine-regen"]),
        ("Machines", ["machine-setting", "stat-pack", "mixed-packs"]),
        ("Lobby", ["vote-seconds", "choose-seconds", "voting-minimum"]),
        ("Connection", ["latency-limit"]),
    ];

    /// <summary>The settable rules in their groups: each group's heading and rules.</summary>
    public static IReadOnlyList<(string Heading, IReadOnlyList<HostRule> Rules)> Groups { get; } = MakeGroups();

    private static List<(string, IReadOnlyList<HostRule>)> MakeGroups()
    {
        var named = Grouped.SelectMany(g => g.Names).ToHashSet(StringComparer.Ordinal);
        var unnamed = LobbyRules.Settable.Where(r => !named.Contains(r.Name)).ToList();
        return [.. Grouped.Select((g, i) => (g.Heading, (IReadOnlyList<HostRule>)[
            .. g.Names.Select(n => LobbyRules.Settable.FirstOrDefault(r => r.Name == n)).OfType<HostRule>(),
            .. i == 0 ? unnamed : []]))];
    }

    /// <summary>Quick Play's rules whole as the service's code sets them (<see cref="QuickPlayDefaults"/>): for a server
    /// that names none.</summary>
    public static LobbyRules Defaults { get; } = QuickPlayDefaults.Whole();

    /// <summary>The value under a key in <paramref name="rules"/>, else Quick Play's.</summary>
    public static int Value(LobbyRules? rules, string key, LobbyRules? quickPlay) =>
        rules?.Value(key) ?? quickPlay?.Value(key) ?? Defaults.Value(key) ?? 0;

    /// <summary>The value as it is raced: what the lobby's mode owns (<see cref="LobbyMode.Terms"/>), else
    /// <see cref="Value"/>. (A row of the host's holds their own value, which comes back without the mode.)</summary>
    public static int Raced(LobbyRules? rules, string key, LobbyRules? quickPlay) =>
        rules?.Mode?.Terms is { } terms && terms.TryGetValue(key, out var owned) ? owned : Value(rules, key, quickPlay);

    /// <summary>The stat pack as it is raced: the mode's when it names one ("": stock), else the lobby's (null: stock).</summary>
    private static string? Pack(LobbyRules? rules) => rules?.Mode?.Pack is { } pack ? pack.Length == 0 ? null : pack : rules?.StatPack;

    /// <summary>The rules that differ from Quick Play's, as one short line: "5 laps · GX+ · ping limit 150 ms";
    /// "" when none does.</summary>
    public static string Summary(LobbyRules? rules, LobbyRules? quickPlay)
    {
        if (rules is null)
        {
            return "";
        }
        var against = quickPlay ?? Defaults;
        // (a game mode first, by its name: "Overdrive · 5 laps")
        return string.Join(" · ", rules.Differing(against).Select(name => Said(name, rules, against)).Prepend(rules.Mode?.Name ?? "")
            .Append(TeamsLine(rules)).Where(s => s.Length > 0));
    }

    /// <summary>The lobby's teams in the summary: "3 teams"; "" for none.</summary>
    private static string TeamsLine(LobbyRules rules) => rules.Mode is not null && rules.Value(LobbyRules.TeamsKey) is > 0 and var teams ? N($"{teams} teams") : "";

    /// <summary>A rule as the summary line says it.</summary>
    public static string Said(string name, LobbyRules rules, LobbyRules? quickPlay)
    {
        ArgumentNullException.ThrowIfNull(rules);
        int V(string key) => Value(rules, key, quickPlay);
        var v = V(name);
        return name switch
        {
            "laps" => v == 0 ? "unlimited laps" : v == 1 ? "1 lap" : N($"{v} laps"),
            "racers" => N($"{v} racers"),
            "cpu-level" => "CPUs " + LanViewModel.CpuLevels[Math.Clamp(v, 0, LanViewModel.CpuLevels.Count - 1)],
            "cpu-fill" => (CpuFillMode)v switch
            {
                CpuFillMode.Always => "CPUs fill empty seats",
                CpuFillMode.Never => "no CPUs",
                _ => "CPU vote",
            },
            "course-select" => (CourseSelectMode)v switch
            {
                CourseSelectMode.Random => "random course",
                CourseSelectMode.Fixed => Courses.Name(V(LobbyRules.FixedCourseKey)),
                _ => "course vote",
            },
            "boost-start" => v != 0 ? "boost from the start" : "no boost on lap 1",
            "finish-limit" => v == 0 ? "no finish limit" : N($"finish limit {v} s"),
            "mine-regen" => LobbyViewModel.MinesRule(v) ?? "mines back after 0.2 s",
            "machine-setting" => LobbyViewModel.SettingRule(Range(rules, quickPlay)) ?? "setting 100",
            "stat-pack" => Pack(rules) ?? "stock stats",
            "mixed-packs" => v != 0 ? "mixed packs" : "one pack for all",
            "vote-seconds" => N($"course vote {v} s"),
            "choose-seconds" => N($"machine choice {v} s"),
            "voting-minimum" => N($"{v} to start"),
            "latency-limit" => LobbyViewModel.PingRule(v) ?? N($"ping limit {v} ms"),
            _ => "",
        };
    }

    /// <summary>A rule's value by itself, for the whole list: "3", "Vote", "On", "200 ms".</summary>
    public static string ValueText(HostRule rule, LobbyRules? rules, LobbyRules? quickPlay)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var v = Raced(rules, rule.Name, quickPlay);
        return rule.Name switch
        {
            "laps" when v == ModeTerms.LapsByCourse => "By course",
            "laps" when v == 0 => "Unlimited",
            "cpu-level" => LanViewModel.CpuLevels[Math.Clamp(v, 0, LanViewModel.CpuLevels.Count - 1)],
            "cpu-fill" => ((CpuFillMode)v).ToString(),
            "course-select" => (CourseSelectMode)v == CourseSelectMode.Fixed
                ? Courses.Name(Raced(rules, LobbyRules.FixedCourseKey, quickPlay)) : ((CourseSelectMode)v).ToString(),
            "finish-limit" => v == 0 ? "Off" : N($"{v} s"),
            "machine-setting" => new SettingRange(Raced(rules, HostRules.MachineSetting.Name, quickPlay), Raced(rules, LobbyRules.SettingHighKey, quickPlay)).Text,
            "stat-pack" => Pack(rules) ?? HostRuleRows.Stock,
            "vote-seconds" or "choose-seconds" => N($"{v} s"),
            "latency-limit" => N($"{v} ms"),
            _ => rule.Kind switch
            {
                RuleKind.Switch => v != 0 ? "On" : "Off",
                RuleKind.Delay => v == 0 ? "Off" : N($"{v} ms"),
                _ => N($"{v}"),
            },
        };
    }

    /// <summary>Every rule of a lobby, in its groups, a value each; those that differ from Quick Play's marked.</summary>
    public static IReadOnlyList<RuleGroup> AllRules(LobbyRules? rules, LobbyRules? quickPlay)
    {
        var differing = rules?.Differing(quickPlay ?? Defaults).ToHashSet(StringComparer.Ordinal) ?? [];
        IReadOnlyList<RuleGroup> groups = [.. Groups.Select((g, i) => new RuleGroup([.. g.Rules.Select(r =>
            (RuleRow)new ValueRule(r.Label, ValueText(r, rules, quickPlay)) { Marked = differing.Contains(r.Name) })])
        {
            Later = i > 0,
            Heading = g.Heading,
        })];
        // a game mode: its own group first, its name and what it changes
        return rules?.Mode is { } mode
            ? [new RuleGroup([new ValueRule("Mode", mode.Name) { Marked = true, Note = mode.About },
                .. rules.Value(LobbyRules.TeamsKey) is > 0 and var teams ? [new ValueRule("Teams", N($"{teams}")) { Marked = true }] : (RuleRow[])[]]) { Heading = "Mode" },
               .. groups.Select(g => g with { Later = true })]
            : groups;
    }

    /// <summary>The machine setting's range of a lobby's rules.</summary>
    public static SettingRange Range(LobbyRules? rules, LobbyRules? quickPlay) =>
        new(Value(rules, HostRules.MachineSetting.Name, quickPlay), Value(rules, LobbyRules.SettingHighKey, quickPlay));

    private static string N(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
