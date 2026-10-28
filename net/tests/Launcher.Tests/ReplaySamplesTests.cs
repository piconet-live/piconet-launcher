using System.Globalization;
using System.Text;
using Launcher.Core;
using TestSupport;

namespace Launcher.Tests;

/// <summary>The Replays list over the protocol's shared samples (a real recording of every version 2 to 7,
/// protocol/tests/vectors/replays): each row, and why a file is not played, what recorded it and its mod image, as
/// the hand-written reader of 0.4.0 read them (ReplaySamples.rows.tsv, written once by that reader). The generated
/// reader (ReplayParts, protocol/replay.toml) must read the very same rows. The compact sample (version 8, the
/// version 7 one made compact) reads version 7's row: the list shows a compact file as the recording it was made of.</summary>
public sealed class ReplaySamplesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Racer(ReplayRacer? r) => r is null
        ? "-"
        : $"{r.Seat}/{r.Name}/{r.Machine}/{r.Color}/{r.IsPlayer}/{(r.Custom is null ? "-" : System.Text.Json.JsonSerializer.Serialize(r.Custom))}";

    private static string Row(string path)
    {
        var sb = new StringBuilder();
        var (engine, module) = ReplayFile.RecordedBy(path);
        sb.Append(Path.GetFileName(path)).Append('\t').Append(ReplayFile.Problem(path) ?? "-").Append('\t')
            .Append(engine ?? "-").Append('\t').Append(module ?? "-").Append('\t').Append(ReplayFile.ModImage(path) ?? "-").Append('\t');
        if (ReplayFile.Read(path) is not { } i)
        {
            return sb.Append("null").ToString();
        }
        sb.Append(CultureInfo.InvariantCulture,
            $"{i.Recorded:O}\t{i.Course}\t{i.Laps}\t{string.Join(",", i.Players)}\t{i.You}\t{i.Length.Ticks}\t{Racer(i.Recorder)}\t{i.StatPack ?? "-"}\t{i.StatPackProblem ?? "-"}\t{i.Practice}\t");
        return sb.Append(i.Results is null
            ? "noresults"
            : string.Join(";", i.Results.Select(x => $"{Racer(x.Racer)}|{x.Place}|{x.Finished}|{x.Out}|{x.Time?.Ticks}|{x.BestLap?.Ticks}"))).ToString();
    }

    [Fact]
    public void EverySampleReadsTheRowsOfBefore()
    {
        var expected = File.ReadAllLines(RepositoryFolder.Of("net", "tests", "Launcher.Tests", "ReplaySamples.rows.tsv"))
            .Where(l => l.Length > 0 && l[0] != '#').ToList();
        var folder = RepositoryFolder.Protocol("protocol", "tests", "vectors", "replays");
        // each sample as the file it is (NAME.fzrp.hex holds its bytes as hex text)
        var files = Directory.GetFiles(folder, "*.fzrp.hex").Order(StringComparer.Ordinal).Select(hex =>
        {
            var path = Path.Combine(_dir, Path.GetFileNameWithoutExtension(hex));
            File.WriteAllBytes(path, Convert.FromHexString(string.Concat(File.ReadAllLines(hex))));
            return path;
        }).ToList();
        Assert.Equal(expected.Count, files.Count);
        Assert.True(expected.Count >= 6);
        foreach (var (want, path) in expected.Zip(files))
        {
            Assert.Equal(want, Row(path));
        }
    }
}
