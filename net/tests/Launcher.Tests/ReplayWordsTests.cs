using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>What the Replays page says of a replay. A replay is a file somebody sent: its names, machines, places
/// and times are whatever the file says, and a row shows them on one line, in a row's room, without an error.</summary>
public sealed class ReplayWordsTests
{
    private static ReplayInfo Replay(ReplayRacer? recorder, IReadOnlyList<RaceResult>? results, int course = 1, string you = "ANA") =>
        new("race.fzrp", new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), course, 3, ["ANA", "BEN"], you, TimeSpan.FromSeconds(125),
            recorder, results);

    [Fact]
    public void ARowSaysTheCourseTheRacerAndTheirResult()
    {
        var ana = new ReplayRacer(0, "ANA", 6, new Rgb(48, 96, 232), true);
        var ben = new ReplayRacer(1, "BEN", 0, null, true);
        var cpu = new ReplayRacer(2, "CPU", 14, null, false);
        var pilot = new ReplayRacer(3, "DR STEWART", 2, null, false);
        var info = Replay(ana,
        [
            new RaceResult(ben, 1, true, false, TimeSpan.FromMilliseconds(118250), TimeSpan.FromMilliseconds(37610)),
            new RaceResult(ana, 2, true, false, TimeSpan.FromMilliseconds(121182), null),
            new RaceResult(pilot, 3, false, false, null, TimeSpan.FromMilliseconds(40000)),
            new RaceResult(cpu, 4, false, true, null, null),
        ]);

        Assert.Equal(("Mute City", "Twist Road"), ReplayWords.Course(info.CourseName));
        Assert.Equal("ANA", ReplayWords.Racer(info));
        Assert.Equal("2nd", ReplayWords.Place(info));
        Assert.Equal("of 4", ReplayWords.Of(info));
        Assert.Equal("2'01\"182", ReplayWords.Time(info));
        Assert.Equal("3 laps", ReplayWords.Laps(info.Laps));
        Assert.Equal("1 lap", ReplayWords.Laps(1));
        Assert.Equal(
            new[]
            {
                new ReplayResultLine("1st", "BEN", "Red Gazelle", "#7D8794", "1'58\"250", "0'37\"610", false),
                new ReplayResultLine("2nd", "ANA", "Blue Falcon", "#3060E8", "2'01\"182", "", true),
                // no finish and not out: the race ended for them before the line (the host's rule)
                new ReplayResultLine("3rd", "DR STEWART (CPU)", "Golden Fox", "#7D8794", "Disqualified", "0'40\"000", false),
                new ReplayResultLine("-", "CPU", "Big Fang", "#7D8794", "KO", "", false),
            },
            ReplayWords.Results(info));
    }

    [Fact]
    public void AReplayWithoutResultsHasNoPlaceAndNoLines()
    {
        var info = Replay(new ReplayRacer(0, "ANA", 6, null, true), null);

        Assert.Equal("", ReplayWords.Place(info));
        Assert.Equal("", ReplayWords.Of(info));
        Assert.Equal("", ReplayWords.Time(info));
        Assert.Empty(ReplayWords.Results(info));
        // no racer recorded: the recorder's name as the header gives it
        Assert.Equal("ANA", ReplayWords.Racer(Replay(null, null)));
    }

    [Fact]
    public void AKnockedOutRacerIsOut()
    {
        var ana = new ReplayRacer(0, "ANA", 6, null, true);

        var info = Replay(ana, [new RaceResult(ana, 7, false, true, null, null)]);

        Assert.Equal("KO", ReplayWords.Place(info));
        Assert.Equal("", ReplayWords.Time(info));
    }

    [Fact]
    public void ARacerTheRaceEndedForBeforeTheLineIsDisqualified()
    {
        // the host's rule ended the race with them still racing: the place they held, and the game's word for it
        var ana = new ReplayRacer(0, "ANA", 6, null, true);

        var info = Replay(ana, [new RaceResult(ana, 30, false, false, null, TimeSpan.FromMilliseconds(41000))]);

        Assert.Equal("30th", ReplayWords.Place(info));
        Assert.Equal("Disqualified", ReplayWords.Time(info));
        Assert.Equal("Disqualified", ReplayWords.Results(info)[0].Time);
    }

    /// <summary>What a file can say that no race does.</summary>
    [Fact]
    public void WhateverAFileSaysARowCanShowIt()
    {
        var odd = new ReplayRacer(0, "TWO\nLINES\tAND\0MORE " + new string('W', 300), 9999, new Rgb(255, 0, 0), true);
        var results = Enumerable.Range(0, 500).Select(i => new RaceResult(
            new ReplayRacer(i, i == 1 ? "" : "R\r\n" + i, i - 250, null, i % 2 == 0), i - 3, true, false,
            i == 2 ? TimeSpan.MaxValue : i == 3 ? TimeSpan.MinValue : TimeSpan.FromMilliseconds(-5), TimeSpan.FromDays(3))).ToList();
        var info = Replay(odd, results, course: 77777, you: "");

        var name = ReplayWords.Racer(info);
        Assert.Equal(ReplayWords.MostName, name.Length);
        Assert.StartsWith("TWOLINESANDMORE W", name, StringComparison.Ordinal);
        Assert.DoesNotContain(name, char.IsControl);
        Assert.Equal("", ReplayWords.Machine(9999));
        Assert.Equal("", ReplayWords.Machine(-1));
        Assert.Equal(("", "Course 77777"), ReplayWords.Course(info.CourseName));
        Assert.Equal("of 30", ReplayWords.Of(info));
        var lines = ReplayWords.Results(info);
        Assert.Equal(ReplayWords.MostResults, lines.Count);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain(line.Name, char.IsControl);
            Assert.True(line.Name.Length is > 0 and <= ReplayWords.MostName + 6);
            Assert.DoesNotContain(line.Time, char.IsControl);
            Assert.Equal("4320'00\"000", line.BestLap);
        });
        Assert.Equal("CPU", lines[1].Name);
        Assert.Equal("0'00\"000", lines[0].Time);   // a time before the start is none
        Assert.Equal("0'00\"000", lines[3].Time);
        Assert.Equal("", ReplayWords.Name(null));
        Assert.Equal("0th", ReplayWords.Ordinal(0));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(30, "30th")]
    public void PlacesAreSaidAsOrdinals(int place, string said) => Assert.Equal(said, ReplayWords.Ordinal(place));

    /// <summary>A countdown (the lobby's clock, the vote's on Quick Play) and a span the launcher says (Practice's
    /// rewind), the series' way.</summary>
    [Theory]
    [InlineData(0, "0'00\"")]
    [InlineData(29, "0'29\"")]
    [InlineData(60, "1'00\"")]
    [InlineData(65, "1'05\"")]
    [InlineData(600, "10'00\"")]
    [InlineData(-3, "0'00\"")]
    public void SecondsAreWrittenTheSeriesWay(int seconds, string written) => Assert.Equal(written, ReplayWords.Seconds(seconds));
}
