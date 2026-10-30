using System.Buffers.Binary;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>What each of the four questions asked of a replay's header says of one file, shape by shape: the list's
/// (<see cref="ReplayFile.Read"/>), the reason a replay is not played (<see cref="ReplayFile.Problem"/>), what recorded
/// it (<see cref="ReplayFile.RecordedBy"/>) and its mod image (<see cref="ReplayFile.ModImage"/>). One reader reads the
/// file for all four; they were four readers once, and do not agree on every file (only two look at the format
/// version, one at the frames' mark, one at the game). Each difference is written down here with its refusal, as
/// it was before the four became one.</summary>
public sealed class ReplayReadersTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly string Image = new('c', 40);
    private static readonly string Module = new('d', 40);

    private const string Match = """
        "match": {"matchId": "m7", "setup": {"course": 21, "laps": 2, "seats": [
          {"seat": 0, "playerId": "p1", "name": "ANA", "machine": 1}, {"seat": 1, "playerId": "p2", "name": "BEN", "machine": 0}]}}
        """;

    /// <summary>A header as the recompiled game writes one: the match, the recorder's seat, what recorded it.</summary>
    private static string Header(string match = Match, string more = "") =>
        "{" + match + ", \"seat\": 1, \"recorded\": \"2026-10-03T22:11:10+00:00\", \"engine\": \"native\", " +
        $"\"module_sha1\": \"{Module}\", \"mod_sha1\": \"{Image}\"{more}}}";

    /// <summary>A replay file in its parts, each of which a shape can change: the mark and the format version, the
    /// header (and the length the file names for it), the frames' block with its mark and its mask of seats, the
    /// lineup block (null: none), the steps' bytes (16 a step for two seats), the results block; cut to
    /// <paramref name="cut"/> bytes when given (below zero: that many taken off the end).</summary>
    private string Replay(string? header = null, string mark = "FZRP", ushort version = 2, uint? named = null, string frames = "FZRF",
        uint mask = 3, int? lineup = 122, int steps = 60 * 16, string? results = null, int? cut = null)
    {
        var json = Encoding.UTF8.GetBytes(header ?? Header());
        using var bytes = new MemoryStream();
        Span<byte> number = stackalloc byte[4];
        void Word(uint value, Span<byte> into)
        {
            BinaryPrimitives.WriteUInt32BigEndian(into, value);
            bytes.Write(into);
        }
        bytes.Write(Encoding.ASCII.GetBytes(mark));
        bytes.Write([(byte)(version >> 8), (byte)version]);
        Word(named ?? (uint)json.Length, number);
        bytes.Write(json);
        bytes.Write(Encoding.ASCII.GetBytes(frames));
        Word(mask, number);
        Word(1, number);
        if (lineup is { } length)
        {
            bytes.Write("FZRL"u8);
            Word((uint)length, number);
            bytes.Write(new byte[length]);
        }
        bytes.Write(new byte[steps]);
        if (results is not null)
        {
            var block = Encoding.UTF8.GetBytes(results);
            bytes.Write(block);
            Word((uint)block.Length, number);
            bytes.Write("FZRS"u8);
        }
        var all = bytes.ToArray();
        var path = Path.Combine(_dir, $"{Directory.GetFiles(_dir).Length}.fzrp");
        File.WriteAllBytes(path, cut is { } at ? all[..(at < 0 ? all.Length + at : at)] : all);
        return path;
    }

    /// <summary>Where the header ends in a file of <see cref="Replay"/> with the usual header.</summary>
    private static int HeaderEnd => 10 + Encoding.UTF8.GetByteCount(Header());

    private static readonly (string? Engine, string? Module) Nobody = (null, null);
    private static readonly (string? Engine, string? Module) Native = ("native", Module);

    /// <summary>The four readers on one file: the list's entry is told by the recorder's name and its length in
    /// steps (null: not listed).</summary>
    private static ((string You, double Steps)? Listed, string? Problem, (string? Engine, string? Module) By, string? Image) Read(string path) =>
        (ReplayFile.Read(path) is { } r ? (r.You, Math.Round(r.Length.TotalSeconds * 60)) : null, ReplayFile.Problem(path), ReplayFile.RecordedBy(path),
            ReplayFile.ModImage(path));

    [Fact]
    public void AWholeReplayIsReadByAllFour()
    {
        // with its lineup block, without one, and with the results at its end (not counted as steps)
        const string results = """{"results":[{"seat":1,"rank":0,"finished":true,"out":false,"laps":2,"timeMs":129001,"bestLapMs":63999}]}""";
        foreach (var path in new[] { Replay(), Replay(lineup: null), Replay(results: results), Replay(lineup: 0) })
        {
            Assert.Equal((("BEN", 60), null, Native, Image), Read(path));
        }
        Assert.Equal(TimeSpan.FromMilliseconds(129001), ReplayFile.Read(Replay(results: results))!.RecorderResult!.Time);
        Assert.Null(ReplayFile.Read(Replay())!.Results);
        // a header that says nothing of what recorded it (the fork's): played, and nobody named
        Assert.Equal((("BEN", 60), null, Nobody, null), Read(Replay("{" + Match + ", \"seat\": 1}")));
        // an image's name that is no SHA-1, an engine that is no text: not told
        Assert.Equal((("BEN", 60), null, (null, Module), null), Read(Replay(Header().Replace("\"native\"", "7", StringComparison.Ordinal)
            .Replace(Image, "cccc", StringComparison.Ordinal))));
    }

    [Fact]
    public void AFileThatIsNoReplayIsRefusedByAllFour()
    {
        var refused = ((string, double)?)null;
        var missing = Path.Combine(_dir, "none.fzrp");
        var empty = Replay(cut: 0);
        var stub = Replay(cut: 9);                                           // the mark, and the lengths cut off
        var other = Replay(mark: "FZRQ");
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "folder.fzrp")).FullName;
        var long1 = Replay(named: (1 << 20) + 1);                            // a header longer than 1 MiB is not read
        var noJson = Replay("{\"match\": ");
        foreach (var path in new[] { missing, empty, stub, other, folder, long1, noJson })
        {
            Assert.Equal((refused, ReplayFile.NotAReplay, Nobody, null), Read(path));
        }
        // 1 MiB itself is read: the file ends before it does
        Assert.Equal((refused, ReplayFile.Incomplete, Nobody, null), Read(Replay(named: 1 << 20)));
    }

    /// <summary>Another format version: the list and the image's reader leave the file out, the reason names the
    /// version before it looks at anything else, and what recorded it is read whatever the version (the list of
    /// replays as text names the engine of every file it lists, so it never asks for such a file's).</summary>
    [Fact]
    public void AnotherFormatVersionIsSaidAndOnlyWhatRecordedItIsStillRead()
    {
        const string said = "Replay of another version of the game · format 10";
        Assert.Equal((null, said, Native, null), Read(Replay(version: 10)));
        Assert.Equal((null, said, Nobody, null), Read(Replay(version: 10, named: (1 << 20) + 1)));   // the version first
        Assert.Equal((null, said, Nobody, null), Read(Replay(version: 10, cut: HeaderEnd - 5)));
        Assert.Equal((null, said.Replace("format 10", "format 1", StringComparison.Ordinal), Native, null), Read(Replay(version: 1)));
        // a recording's layout under the compact file's number (8) is no compact file: its header is not read
        Assert.Equal((null, "Replay of another version of the game · format 8", Nobody, null), Read(Replay(version: 8)));
    }

    /// <summary>The header of a race with this mine rule in its set-up (milliseconds, as the server sends it).</summary>
    private static string WithMines(string ms) =>
        Header(Match.Replace("\"setup\": {", $"\"setup\": {{\"rules\": {{\"cpuLevel\": 3, \"mineRegenMs\": {ms}}}, ", StringComparison.Ordinal));

    /// <summary>Format 7 is a race on the coordinator's start grid (its set-up's grid), whatever else it holds: listed
    /// and played as format 2 is, a mine rule in it too (the game reads the rest of a format 7 file as any other's).</summary>
    [Fact]
    public void AReplayOfARaceOnTheStartGridIsFormat7AndPlaysWithAMineRuleToo()
    {
        Assert.True(ReplayFormat.Plays(ReplayFile.GridFormatVersion) && ReplayFile.GridFormatVersion == 7);
        var grid = Header(Match.Replace("\"setup\": {", "\"setup\": {\"grid\": [0], ", StringComparison.Ordinal));
        Assert.Equal((("BEN", 60), null, Native, Image), Read(Replay(grid, version: 7)));
        Assert.Equal((("BEN", 60), null, Native, Image), Read(Replay(WithMines("200"), version: 7)));
        Assert.Null(ReplayFile.Problem(Replay(WithMines("200"), version: 7)));
    }

    /// <summary>Format 5 is a race whose mines came back: the same file under another number (the game writes it so
    /// that a program from before the rule refuses it, and does not play the race with the mines gone). It is listed
    /// and played as format 2 is; a file whose format and rule disagree is listed with that said, as the game refuses
    /// it. A rule of 0, or one too short for a whole step, is no rule, as the game reads it.</summary>
    [Fact]
    public void AReplayOfARaceWhoseMinesCameBackIsFormat5AndOneThatDisagreesSaysSo()
    {
        Assert.True(ReplayFormat.Plays(5) && ReplayFormat.Plays(6) && ReplayFormat.Plays(7) && ReplayFormat.Plays(8) && ReplayFormat.Plays(9) && !ReplayFormat.Plays(10));
        foreach (var ms in new[] { "200", "50", "5000", "100000" })
        {
            Assert.Equal((("BEN", 60), null, Native, Image), Read(Replay(WithMines(ms), version: 5)));
        }
        foreach (var ms in new[] { "0", "16", "\"200\"" })
        {
            Assert.Equal((("BEN", 60), null, Native, Image), Read(Replay(WithMines(ms))));
            Assert.Equal(ReplayFile.MinesDisagree, ReplayFile.Problem(Replay(WithMines(ms), version: 5)));
        }
        Assert.Equal(ReplayFile.MinesDisagree, ReplayFile.Problem(Replay(version: 5)));
        foreach (var version in new ushort[] { 2, 3, 4 })
        {
            Assert.Equal(ReplayFile.MinesDisagree, ReplayFile.Problem(Replay(WithMines("200"), version: version)));
        }
        Assert.Equal((12, 0, 3, 300), (MinesOf(WithMines("200")), MinesOf(Header()), MinesOf(WithMines("50")), MinesOf(WithMines("9000"))));
    }

    private static int MinesOf(string header)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(header);
        return ReplayFile.MineRegenSteps(doc.RootElement);
    }

    /// <summary>The header of a race with this stat pack in its set-up.</summary>
    private static string WithPack(Online.Protocol.StatPackWire pack) => Header(Match.Replace("\"setup\": {",
        $"\"setup\": {{\"stats\": {{\"name\": \"{pack.Name}\", \"fingerprint\": \"{pack.Fingerprint}\", \"values\": \"{pack.Values}\"}}, ", StringComparison.Ordinal));

    /// <summary>Format 3 is a race that ran on a stat pack: the same file under another number (the game writes it
    /// so that a program from before stat packs refuses the file). It is listed with the pack's name and played as
    /// format 2 is. A file the game would refuse for its pack (the format and the pack disagree, the pack is none,
    /// or it holds a value this launcher takes from nobody) is listed with that said and is not played: no game is
    /// started to find it out.</summary>
    [Fact]
    public void AReplayOfARaceWithAStatPackIsListedWithItsNameAndOneTheGameWouldRefuseSaysSo()
    {
        var pack = Online.Protocol.StatPack.Of("GX Made Up 1.0", [Online.Protocol.StatEntry.Of(6, 4, 2520)]).Pack!.ToWire();
        Assert.True(ReplayFormat.Plays(2) && ReplayFormat.Plays(3) && !ReplayFormat.Plays(1) && ReplayFormat.Plays(4) && ReplayFormat.Plays(6) && ReplayFormat.Plays(7) && ReplayFormat.Plays(8) && ReplayFormat.Plays(9) && !ReplayFormat.Plays(10));

        var good = Replay(WithPack(pack), version: 3);
        Assert.Null(ReplayFile.Problem(good));
        Assert.Equal(("GX Made Up 1.0", null, "GX Made Up 1.0"),
            (ReplayFile.Read(good)!.StatPack, ReplayFile.Read(good)!.StatPackProblem, ReplayFile.Read(good)!.StatPackLabel));
        Assert.Equal((null, null, ""), (ReplayFile.Read(Replay())!.StatPack, ReplayFile.Read(Replay())!.StatPackProblem, ReplayFile.Read(Replay())!.StatPackLabel));

        var cases = new (string Path, string Why)[]
        {
            (Replay(version: 3), "This replay says it was raced on a stat pack and holds none: the game does not play it."),
            (Replay(WithPack(pack), version: 2), "This replay holds a stat pack in the format of races without one: the game does not play it."),
            (Replay(WithPack(pack with { Fingerprint = new string('0', 40) }), version: 3),
                $"This replay's stat pack is not one this launcher takes: The pack's values have the fingerprint {pack.Fingerprint}, and it says another."),
            (Replay(WithPack(pack with { Values = "BgQAAAAB" }), version: 3),
                "This replay's stat pack is not one this launcher takes: machine 6 Weight is the bits 00000001, which is no number a pack may hold (a denormal, -0, an infinity or none at all)."),
            (Replay(Header(Match.Replace("\"setup\": {", "\"setup\": {\"stats\": 7, ", StringComparison.Ordinal)), version: 3),
                "This replay's stat pack is not one this launcher takes: The pack's values are not base64, or are more than the whole car data."),
        };
        Assert.All(cases, c =>
        {
            Assert.Equal(c.Why, ReplayFile.Problem(c.Path));
            var listed = ReplayFile.Read(c.Path);                              // still a row of the list, which says it
            Assert.Equal((c.Why, "Stat pack not playable"), (listed!.StatPackProblem, listed.StatPackLabel));
        });
    }

    /// <summary>A file that ends early is incomplete, wherever it ends; the header's own readers need only the
    /// header whole.</summary>
    [Fact]
    public void AFileCutShortIsIncompleteAndAWholeHeaderIsStillRead()
    {
        var refused = ((string, double)?)null;
        // in the header: nobody reads it
        Assert.Equal((refused, ReplayFile.Incomplete, Nobody, null), Read(Replay(cut: HeaderEnd - 5)));
        // after the header, in the frames' block: the list needs that block, the header's readers do not
        Assert.Equal((refused, ReplayFile.Incomplete, Native, Image), Read(Replay(cut: HeaderEnd)));
        Assert.Equal((refused, ReplayFile.Incomplete, Native, Image), Read(Replay(cut: HeaderEnd + 6)));
        // in the lineup, before any step, in a step: listed (with the whole steps it has), not played
        Assert.Equal((("BEN", 0), ReplayFile.Incomplete, Native, Image), Read(Replay(cut: HeaderEnd + 12 + 40)));
        Assert.Equal((("BEN", 0), ReplayFile.Incomplete, Native, Image), Read(Replay(steps: 0)));
        Assert.Equal((("BEN", 59), ReplayFile.Incomplete, Native, Image), Read(Replay(cut: -3)));
        Assert.Equal((("BEN", 60), ReplayFile.Incomplete, Native, Image), Read(Replay(steps: (60 * 16) + 7)));
        // a race cut short ends on a whole step, and plays
        Assert.Equal((("BEN", 40), null, Native, Image), Read(Replay(cut: -20 * 16)));
    }

    /// <summary>What only the reason looks at: the frames' mark, the game, a mask that names no seat. The list
    /// shows such a file, and playing it says why not.</summary>
    [Fact]
    public void WhatOnlyTheReasonLooksAtIsListedAndNotPlayed()
    {
        Assert.Equal((("BEN", 60), ReplayFile.NotAReplay, Native, Image), Read(Replay(frames: "FZRX")));
        Assert.Equal((("BEN", 60), "Replay of another version of F-Zero GX · GFZJ01", Native, Image),
            Read(Replay(Header(more: ", \"game\": \"GFZJ01\""))));
        Assert.Equal((("BEN", 60), null, Native, Image), Read(Replay(Header(more: ", \"game\": \"GFZE01\""))));
        Assert.Equal((("BEN", 60), null, Native, Image), Read(Replay(Header(more: ", \"game\": 7"))));       // no name: not asked
        // no seat in the mask: a step is then its checksum alone (4 bytes) for the list, and no step for the reason
        Assert.Equal((("BEN", 240), ReplayFile.Incomplete, Native, Image), Read(Replay(mask: 0)));
        // the frames' mark is looked at before the game, the game before the steps
        Assert.Equal(ReplayFile.NotAReplay, ReplayFile.Problem(Replay(Header(more: ", \"game\": \"GFZJ01\""), frames: "FZRX")));
        Assert.StartsWith("Replay of another version of F-Zero GX", ReplayFile.Problem(Replay(Header(more: ", \"game\": \"GFZJ01\""), steps: 7)),
            StringComparison.Ordinal);
    }

    /// <summary>A header that is JSON and no match: not listed, so not played; what recorded it and its image are
    /// read when the header is an object, and from nothing else.</summary>
    [Fact]
    public void AHeaderWithoutAMatchIsNoReplayAndStillSaysWhatRecordedIt()
    {
        var refused = ((string, double)?)null;
        Assert.Equal((refused, ReplayFile.NotAReplay, Native, Image), Read(Replay(Header(match: "\"match\": {}"))));
        Assert.Equal((refused, ReplayFile.NotAReplay, Native, Image), Read(Replay(Header(match: "\"match\": {\"setup\": {\"seats\": [3]}}"))));
        Assert.Equal((refused, ReplayFile.NotAReplay, Nobody, null), Read(Replay("[]")));
        Assert.Equal((refused, ReplayFile.NotAReplay, Nobody, null), Read(Replay("{}")));
        // a recorder's seat that no racer has: listed with no recorder
        var listed = ReplayFile.Read(Replay(Header().Replace("\"seat\": 1, \"rec", "\"seat\": 9, \"rec", StringComparison.Ordinal)))!;
        Assert.Equal(("", null), (listed.You, listed.Recorder));
        // a time that is none: the file's own time is taken
        var undated = Replay(Header().Replace("2026-10-03T22:11:10+00:00", "yesterday", StringComparison.Ordinal));
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(undated)), ReplayFile.Read(undated)!.Recorded);
    }
}
