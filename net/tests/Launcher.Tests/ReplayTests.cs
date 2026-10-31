using System.Buffers.Binary;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

public sealed class ReplayTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string recorded, int steps, string? results = null)
    {
        var header = $$$"""
            {"match": {"matchId": "m7", "seat": 1, "setup": {"course": 21, "laps": 2, "seats": [
              {"seat": 0, "playerId": "p1", "name": "ANA", "machine": 1},
              {"seat": 1, "playerId": "p2", "name": "BEN", "machine": 0, "color": {"r": 54, "g": 201, "b": 245}},
              {"seat": 2, "name": "CPU", "machine": 3}]}},
             "seat": 1, "mask": 3, "delay": 2, "recorded": "{{{recorded}}}"}
            """;
        var json = Encoding.UTF8.GetBytes(header);
        using var f = File.Create(Path.Combine(_dir, name));
        var w = new BinaryWriter(f);
        Span<byte> u32 = stackalloc byte[4];
        w.Write("FZRP"u8);
        w.Write((byte)0);
        w.Write((byte)2);
        BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)json.Length);
        w.Write(u32);
        w.Write(json);
        w.Write("FZRF"u8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 3);
        w.Write(u32);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 1);
        w.Write(u32);
        w.Write("FZRL"u8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 122);
        w.Write(u32);
        w.Write(new byte[122]);
        w.Write(new byte[steps * ((6 * 2) + 4)]);
        if (results is not null)
        {
            var block = Encoding.UTF8.GetBytes(results);
            w.Write(block);
            BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)block.Length);
            w.Write(u32);
            w.Write("FZRS"u8);
        }
        return f.Name;
    }

    [Fact]
    public void AReplaysHeaderGivesItsCourseRacersAndLength()
    {
        var r = ReplayFile.Read(Write("a.fzrp", "2026-10-03T22:11:10+00:00", 3600))!;
        Assert.Equal("Aeropolis: Dragon Slope", r.CourseName);
        Assert.Equal(2, r.Laps);
        Assert.Equal(["ANA", "BEN"], r.Players);
        Assert.Equal("BEN", r.You);
        Assert.Equal(TimeSpan.FromMinutes(1), r.Length);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 22, 11, 10, TimeSpan.Zero), r.Recorded);
    }

    [Fact]
    public void TheResultsAtTheEndGivePlacesTimesAndTheRecorder()
    {
        const string results = """
            {"results":[
              {"seat":0,"rank":1,"finished":true,"out":false,"laps":2,"timeMs":130655,"bestLapMs":64210},
              {"seat":1,"rank":0,"finished":true,"out":false,"laps":2,"timeMs":129001,"bestLapMs":63999},
              {"seat":2,"rank":2,"finished":false,"out":true,"laps":0,"timeMs":null,"bestLapMs":0}]}
            """;
        var r = ReplayFile.Read(Write("r.fzrp", "2026-10-04T01:43:43+00:00", 3600, results))!;
        Assert.Equal(TimeSpan.FromMinutes(1), r.Length);                    // the block is not counted as steps
        Assert.Equal(new ReplayRacer(1, "BEN", 0, new Online.Protocol.Rgb(54, 201, 245), true), r.Recorder);
        Assert.Equal(["BEN", "ANA", "CPU"], r.Results!.Select(x => x.Racer.Name));
        Assert.Equal([1, 2, 3], r.Results!.Select(x => x.Place));
        Assert.Equal(TimeSpan.FromMilliseconds(129001), r.RecorderResult!.Time);
        Assert.Equal(TimeSpan.FromMilliseconds(63999), r.RecorderResult.BestLap);
        Assert.True(r.Results![2].Out);
        Assert.Null(r.Results[2].Time);
        Assert.Null(r.Results[2].BestLap);
        Assert.Null(ReplayFile.Read(Write("old.fzrp", "2026-10-03T00:00:00+00:00", 60))!.Results);   // no block: none
    }

    [Fact]
    public void ADeletedReplayLeavesTheList()
    {
        var path = Write("gone.fzrp", "2026-10-01T10:00:00+00:00", 60);
        Assert.True(ReplayFile.Delete(path));
        Assert.Empty(ReplayFile.List(_dir));
    }

    [Fact]
    public void TheListIsNewestFirstAndSkipsOtherFiles()
    {
        Write("old.fzrp", "2026-10-01T10:00:00+00:00", 60);
        Write("new.fzrp", "2026-10-03T10:00:00+00:00", 60);
        File.WriteAllText(Path.Combine(_dir, "bad.fzrp"), "not a replay");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
        Assert.Equal(["new.fzrp", "old.fzrp"], ReplayFile.List(_dir).Select(r => Path.GetFileName(r.Path)));
        Assert.Empty(ReplayFile.List(Path.Combine(_dir, "none")));
    }

    [Fact]
    public void TheRacersLineNamesThreeAndCountsTheRest()
    {
        Assert.Equal("ANA, BEN, CAL and 2 more", Replays.Racers(["ANA", "BEN", "CAL", "DAN", "EVE"]));
    }

    [Fact]
    public void RevealQuotesThePathAloneForExplorer()
    {
        var path = System.IO.Path.GetFullPath("Name With Space/a.fzrp");
        Assert.Equal("/select,\"" + path + "\"", Replays.RevealArguments("Name With Space/a.fzrp"));
    }
}
