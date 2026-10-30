using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// Quick Play's rules as the service's own code sets them (piconet-service's ServerOptions, no option given): the
/// rules' defaults of protocol/rules.toml (<see cref="HostRule.Default"/>, <see cref="RaceRules.QuickPlay"/>,
/// <see cref="SettingRange.QuickPlay"/>), and the lobby's times, which rules.toml gives no default, as the service's
/// numbers. What the launcher takes for a server that names none of its rules, and what the stand-in service races
/// (<see cref="StandInService"/>).
/// </summary>
public static class QuickPlayDefaults
{
    /// <summary>The course vote's length, in seconds (the service's CourseVoteMs; vote-seconds has no default in
    /// rules.toml).</summary>
    public const int VoteSeconds = 10;

    /// <summary>The machine choice's length, in seconds (the service's MachineChoiceMs; choose-seconds has no default in
    /// rules.toml).</summary>
    public const int ChooseSeconds = 40;

    /// <summary>The players a lobby needs before its race starts by vote (the service's VotingMinimum; voting-minimum
    /// has no default in rules.toml).</summary>
    public const int VotingMinimum = 10;

    /// <summary>Quick Play's rules as a server's welcome carries them: any machine at top speed, the race's rules'
    /// defaults.</summary>
    public static ServerRules Rules { get; } = new(HostRules.Laps.Default, HostRules.Racers.Default, CpuFillMode.Vote,
        CourseSelectMode.Vote, null, [.. Enumerable.Range(0, Machines.Names.Count)], SettingRange.QuickPlay, RaceRules.QuickPlay);

    /// <summary>Quick Play's rules whole, as a server's lobby list carries them (<see cref="LobbyList.QuickPlay"/>), the
    /// default stat pack; <paramref name="votingMinimum"/>: a server's own, where its operator set one.</summary>
    public static LobbyRules Whole(int votingMinimum = VotingMinimum) => new(LobbyRules.Settable.Where(r => r.Kind != RuleKind.Pack)
        .Select(r => KeyValuePair.Create(r.Name, r.Name switch
        {
            "vote-seconds" => VoteSeconds,
            "choose-seconds" => ChooseSeconds,
            "voting-minimum" => votingMinimum,
            "machine-setting" => SettingRange.QuickPlay.Low,
            _ => r.Default,
        }))
        .Append(KeyValuePair.Create(LobbyRules.SettingHighKey, SettingRange.QuickPlay.High))
        .ToDictionary(p => p.Key, p => p.Value), HostRules.DefaultStatPack);
}
