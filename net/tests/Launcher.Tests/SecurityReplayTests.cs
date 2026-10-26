using System.Buffers.Binary;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>A replay is a file somebody sent: whatever is in it, the launcher's readers say "not a replay" and do
/// not throw (the Replays page lists the folder when the launcher starts, on the window's thread).</summary>
public sealed class SecurityReplayTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Seats = """
        "setup": {"course": 21, "laps": 2, "seats": [
          {"seat": 0, "playerId": "p1", "name": "ANA", "machine": 1}, {"seat": 1, "playerId": "p2", "name": "BEN", "machine": 0}]}
        """;

    private string Write(string name, byte[] header, string? results = null)
    {
        using var f = File.Create(Path.Combine(_dir, name));
        var w = new BinaryWriter(f);
        Span<byte> u32 = stackalloc byte[4];
        w.Write("FZRP"u8);
        w.Write((byte)0);
        w.Write((byte)2);
        BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)header.Length);
        w.Write(u32);
        w.Write(header);
        w.Write("FZRF"u8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 3);
        w.Write(u32);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 1);
        w.Write(u32);
        w.Write("FZRL"u8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, 122);
        w.Write(u32);
        w.Write(new byte[122]);
        w.Write(new byte[60 * 16]);
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

    private static byte[] Header(string more = "") =>
        Encoding.UTF8.GetBytes("{\"match\": {\"matchId\": \"m7\", " + Seats + "}, \"seat\": 1, \"mask\": 3, \"delay\": 2" + more + "}");

    /// <summary>A results block whose times are no times (found by the fuzzer: the largest number threw out of the
    /// reader, and so out of the list).</summary>
    [Theory]
    [InlineData("9223372036854775807", "1")]
    [InlineData("-9223372036854775808", "1")]
    [InlineData("1", "9223372036854775807")]
    [InlineData("922337203685477", "1")]
    public void AResultsBlockWithATimeThatIsNoneIsNotAReplay(string time, string best)
    {
        var results = $$"""{"results":[{"seat":0,"rank":1,"finished":true,"out":false,"laps":2,"timeMs":{{time}},"bestLapMs":{{best}}}]}""";
        var path = Write("r.fzrp", Header(), results);
        // no exception: not a replay, or (a time that is one) a replay whose times the page can write out
        foreach (var replay in ReplayFile.List(_dir).Append(ReplayFile.Read(path)).OfType<ReplayInfo>())
        {
            Assert.All(replay.Results!, r => Assert.NotEmpty(r.Time!.Value.ToString(@"h\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture)));
        }
        _ = ReplayFile.Problem(path);
    }

    /// <summary>A header whose text is not UTF-8 where the launcher reads a name (the game, the image, the engine):
    /// the file was listed, and playing it threw on the window's thread.</summary>
    [Fact]
    public void AHeaderWithTextThatIsNotTextIsNotAReplay()
    {
        foreach (var key in new[] { "game", "mod_sha1", "engine", "module_sha1", "recorded" })
        {
            var header = Header($", \"{key}\": \"AB\"");
            header[Array.LastIndexOf(header, (byte)'A')] = 0xFF;               // a byte no UTF-8 text holds
            var path = Write($"{key}.fzrp", header);
            _ = ReplayFile.Read(path);
            _ = ReplayFile.Problem(path);
            _ = ReplayFile.ModImage(path);
            _ = ReplayFile.RecordedBy(path);
        }
        Assert.Equal(ReplayFile.NotAReplay, ReplayFile.Problem(Path.Combine(_dir, "game.fzrp")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("3")]
    [InlineData("\"FZRP\"")]
    [InlineData("null")]
    [InlineData("{\"match\": []}")]
    [InlineData("{\"match\": {\"setup\": {\"seats\": [null]}}}")]
    [InlineData("{\"match\": {\"setup\": {\"course\": 1e400, \"laps\": 1, \"seats\": []}}}")]
    public void AHeaderThatIsNoHeaderIsNotAReplay(string json)
    {
        var path = Write("h.fzrp", Encoding.UTF8.GetBytes(json));
        Assert.Null(ReplayFile.Read(path));
        Assert.Equal(ReplayFile.NotAReplay, ReplayFile.Problem(path));
        Assert.Null(ReplayFile.ModImage(path));
        Assert.Equal((null, null), ReplayFile.RecordedBy(path));
        Assert.Empty(ReplayFile.List(_dir));
    }

    /// <summary>A replay's names are shown on the Replays page (the racer it is seen from, the
    /// results): a mark that turns the reading direction made one name show as another's, as it
    /// can in chat when unchecked.</summary>
    [Theory]
    [InlineData("ANA\u202EBEN", "ANABEN")]
    [InlineData("\u2067ANA\u2069", "ANA")]
    [InlineData("A\u200FB\u200E", "AB")]
    public void AReplaysNamesAreShownWithoutMarksThatTurnTheReadingDirection(string name, string shown) =>
        Assert.Equal(shown, ReplayWords.Name(System.Text.RegularExpressions.Regex.Unescape(name)));
}
