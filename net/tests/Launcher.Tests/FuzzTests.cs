using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// Fuzzing of what the launcher reads from other people (what a hostile or broken peer or server can hand it): replay files, the
/// coordinator's messages, LAN announcements and relay packets. A structured mutation loop (no coverage feedback:
/// no fuzzing engine for .NET is at hand): every input starts from a file or message these tests make, and is then
/// changed in its lengths, its marks, single bytes, or its JSON (a value swapped for a hostile one). The rule: no
/// reader throws, whatever it is given.
/// <para>Each test runs a short loop in the ordinary suite. A long run: <c>FZGX_FUZZ_SECONDS=600</c> (for each
/// test), <c>FZGX_FUZZ_SEED=N</c> another start; an input that breaks the rule is written to
/// <c>FZGX_FUZZ_OUT</c> (default: the temporary folder) and named in the failure.</para>
/// </summary>
public sealed partial class FuzzTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-fuzz").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly double Seconds =
        double.TryParse(Environment.GetEnvironmentVariable("FZGX_FUZZ_SECONDS"), out var s) ? s : 0;

    private static readonly int Seed =
        int.TryParse(Environment.GetEnvironmentVariable("FZGX_FUZZ_SEED"), out var s) ? s : 20261006;

    /// <summary>Runs <paramref name="once"/> for the time asked (or <paramref name="smoke"/> times), and fails on the
    /// first exception with the input kept.</summary>
    private static void Loop(string name, int smoke, Func<Random, byte[]> make, Action<byte[]> once)
    {
        var random = new Random(Seed);
        var end = DateTime.UtcNow.AddSeconds(Seconds);
        long n = 0;
        for (; Seconds > 0 ? DateTime.UtcNow < end : n < smoke; n++)
        {
            var input = make(random);
            try
            {
                once(input);
            }
            catch (Exception e)
            {
                var folder = Environment.GetEnvironmentVariable("FZGX_FUZZ_OUT") ?? Path.GetTempPath();
                var file = Path.Combine(folder, $"fuzz-{name}-{Seed}-{n}.bin");
                File.WriteAllBytes(file, input);
                Assert.Fail($"{name}: input {n} (seed {Seed}, {input.Length} bytes, kept as {file}) threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }
        if (Seconds > 0)
        {
            TestContext.Current.SendDiagnosticMessage($"fuzz {name}: {n} inputs in {Seconds} s, seed {Seed}, none threw");
        }
    }

    /// <summary>JSON values a reader does not expect where the seed has an ordinary one.</summary>
    private static readonly string[] Hostile =
    [
        "null", "true", "[]", "{}", "\"\"", "0", "-1", "30", "31", "255", "65536", "2147483647", "2147483648", "-2147483649",
        "4294967295", "9223372036854775807", "-9223372036854775808", "18446744073709551616", "1e308", "-1e308", "1e999", "0.5", "-0.0",
        "\"..\\\\..\\\\..\\\\x\"", "\"../../x\"", "\"C:\\\\Windows\\\\x\"", "\"a\\u0000b\"", "\"\\u202E\"", "\"9999-12-31T23:59:59+14:00\"",
        "\"0001-01-01T00:00:00-14:00\"", "\"finished\"", "\"out\"", "[null]", "[[]]", "[{}]", "{\"r\":300,\"g\":-1,\"b\":\"x\"}",
    ];

    private static readonly JsonDocumentOptions Deep = new() { MaxDepth = 256 };

    /// <summary>The JSON with one value (at any depth) swapped for a hostile one, a member removed, or an array grown.</summary>
    private static string MutateJson(string json, Random random)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: Deep);
        }
        catch (JsonException)
        {
            return json;
        }
        if (root is null)
        {
            return json;
        }
        var places = new List<(JsonNode Parent, string? Key, int Index)>();
        void Walk(JsonNode node)
        {
            if (node is JsonObject o)
            {
                foreach (var (k, v) in o.ToList())
                {
                    places.Add((o, k, 0));
                    if (v is not null)
                    {
                        Walk(v);
                    }
                }
            }
            else if (node is JsonArray a)
            {
                for (var i = 0; i < a.Count; i++)
                {
                    places.Add((a, null, i));
                    if (a[i] is { } v)
                    {
                        Walk(v);
                    }
                }
            }
        }
        Walk(root);
        for (var changes = 1 + random.Next(3); changes > 0 && places.Count > 0; changes--)
        {
            var (parent, key, index) = places[random.Next(places.Count)];
            var value = random.Next(12) == 0
                ? JsonValue.Create(new string((char)('A' + random.Next(26)), 1 << random.Next(18)))
                : random.Next(12) == 0 ? JsonNode.Parse(new string('[', 70) + new string(']', 70), documentOptions: Deep)
                : JsonNode.Parse(Hostile[random.Next(Hostile.Length)]);
            try
            {
                if (parent is JsonObject o && key is not null)
                {
                    if (random.Next(6) == 0)
                    {
                        o.Remove(key);
                    }
                    else
                    {
                        o[key] = value;
                    }
                }
                else if (parent is JsonArray a && index < a.Count)
                {
                    if (random.Next(6) == 0)
                    {
                        for (var k = 1 << random.Next(8); k > 0; k--)
                        {
                            a.Add(a[index]?.DeepClone());
                        }
                    }
                    else
                    {
                        a[index] = value;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // the place went with an earlier change
            }
        }
        // 1e999 and the like are numbers the writer does not write: they go in as text
        return root.ToJsonString(new JsonSerializerOptions { MaxDepth = 256 }).Replace("\"1e999\"", "1e999", StringComparison.Ordinal);
    }

    private static void Bytes(byte[] data, Random random)
    {
        for (var changes = 1 + random.Next(4); changes > 0 && data.Length > 0; changes--)
        {
            var at = random.Next(data.Length);
            switch (random.Next(4))
            {
                case 0: data[at] ^= (byte)(1 << random.Next(8)); break;
                case 1: data[at] = (byte)random.Next(256); break;
                case 2: data[at] = random.Next(2) == 0 ? (byte)0 : (byte)0xFF; break;
                default:
                    if (at + 4 <= data.Length)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), Lengths[random.Next(Lengths.Length)]);
                    }
                    break;
            }
        }
    }

    private static readonly uint[] Lengths = [0, 1, 7, 8, 121, 122, 124, 0x7FFFFFFF, 0x80000000, 0xFFFFFFF8, 0xFFFFFFFF, 1 << 20, (1 << 20) + 1];

    /// <summary>A replay file as the game writes it, from parts a mutation can change one at a time.</summary>
    private static byte[] Replay(string header, uint mask, uint seat, byte[]? lineup, byte[] steps, string? results,
        uint? headerLength = null, uint? lineupLength = null, uint? resultsLength = null, int version = 2)
    {
        using var file = new MemoryStream();
        Span<byte> u32 = stackalloc byte[4];
        void U32(Span<byte> at, uint v)
        {
            BinaryPrimitives.WriteUInt32BigEndian(at, v);
            file.Write(at);
        }
        var json = Encoding.UTF8.GetBytes(header);
        file.Write("FZRP"u8);
        file.WriteByte((byte)(version >> 8));
        file.WriteByte((byte)version);
        U32(u32, headerLength ?? (uint)json.Length);
        file.Write(json);
        file.Write("FZRF"u8);
        U32(u32, mask);
        U32(u32, seat);
        if (lineup is not null)
        {
            file.Write("FZRL"u8);
            U32(u32, lineupLength ?? (uint)lineup.Length);
            file.Write(lineup);
        }
        file.Write(steps);
        if (results is not null)
        {
            var block = Encoding.UTF8.GetBytes(results);
            file.Write(block);
            U32(u32, resultsLength ?? (uint)block.Length);
            file.Write("FZRS"u8);
        }
        return file.ToArray();
    }

    private const string Header = """
        {"match": {"matchId": "m7", "seat": 1, "setup": {"course": 21, "laps": 2, "shuffleSeed": 4660, "cpuSeed": 305419896,
          "rules": {"cpuLevel": 3, "boostFromStart": false, "restore": false}, "seats": [
          {"seat": 0, "playerId": "p1", "name": "ANA", "machine": 1, "setting": 70, "stockColor": 2},
          {"seat": 1, "playerId": "p2", "name": "BEN", "machine": 0, "color": {"r": 54, "g": 201, "b": 245}},
          {"seat": 2, "name": "CPU", "machine": 3}]}},
         "seat": 1, "mask": 3, "delay": 2, "recorded": "2026-10-04T01:43:43+00:00", "game": "GFZE01",
         "mod_sha1": "aba727f7aba727f7aba727f7aba727f7aba727f7", "engine": "native", "module_sha1": "175d563c"}
        """;

    private const string Results = """
        {"results":[
          {"seat":0,"rank":1,"finished":true,"out":false,"laps":2,"timeMs":130655,"bestLapMs":64210},
          {"seat":1,"rank":0,"finished":true,"out":false,"laps":2,"timeMs":129001,"bestLapMs":63999},
          {"seat":2,"rank":2,"finished":false,"out":true,"laps":0,"timeMs":null,"bestLapMs":0}]}
        """;

    /// <summary>A header of the formats since 0.2.0 (3 a pack, 4 a custom machine, 5 mines that come back, 6 packs a
    /// seat; a practice), by its format version.</summary>
    private static string NewerHeader(int version) =>
        Header.Replace("\"seat\": 1, \"mask\"", "\"practice\": true, \"seat\": 1, \"mask\"", StringComparison.Ordinal)
            .Replace("\"restore\": false}", version switch
            {
                5 => "\"restore\": false, \"mineRegenMs\": 200}",
                _ => "\"restore\": false}",
            }, StringComparison.Ordinal)
            .Replace("\"stockColor\": 2}", version switch
            {
                4 => "\"stockColor\": 2, \"custom\": {\"body\": 3, \"cockpit\": 30, \"booster\": 60, \"pilot\": 6, \"colors\": [4278190081, 0, 65281]}}",
                6 => "\"stockColor\": 2, \"pack\": 1}",
                _ => "\"stockColor\": 2}",
            }, StringComparison.Ordinal)
            .Replace("\"seats\": [", version is 3 or 4 or 6
                ? $"\"stats\": {{\"name\": \"{FuzzPack.Name}\", \"fingerprint\": \"{FuzzPack.Fingerprint}\", \"values\": \"{FuzzPack.ToWire().Values}\"}}, " +
                  (version == 6 ? $"\"packs\": [{{\"name\": \"{FuzzPack.Name}\", \"fingerprint\": \"{FuzzPack.Fingerprint}\", \"values\": \"{FuzzPack.ToWire().Values}\"}}], " : "") +
                  "\"seats\": ["
                : "\"seats\": [", StringComparison.Ordinal);

    private static byte[] MutatedReplay(Random random)
    {
        var version = random.Next(20) == 0 ? random.Next(9) : random.Next(2) == 0 ? 2 : random.Next(3, 7);
        var seed = version is >= 3 and <= 6 ? NewerHeader(version) : Header;
        var header = random.Next(3) == 0 ? MutateJson(seed, random) : seed;
        var results = random.Next(4) == 0 ? null : random.Next(3) == 0 ? MutateJson(Results, random) : Results;
        var mask = random.Next(6) == 0 ? Lengths[random.Next(Lengths.Length)] : 3u;
        var seat = random.Next(6) == 0 ? Lengths[random.Next(Lengths.Length)] : 1u;
        var lineup = random.Next(8) == 0 ? null : new byte[random.Next(8) == 0 ? random.Next(200) : 122];
        var steps = new byte[random.Next(8) == 0 ? random.Next(400) : 16 * random.Next(40)];
        random.NextBytes(steps);
        var data = Replay(header, mask, seat, lineup, steps, results,
            random.Next(8) == 0 ? Lengths[random.Next(Lengths.Length)] : null,
            random.Next(8) == 0 ? Lengths[random.Next(Lengths.Length)] : null,
            random.Next(8) == 0 ? Lengths[random.Next(Lengths.Length)] : null,
            version);
        if (random.Next(3) == 0)
        {
            Bytes(data, random);
        }
        if (random.Next(6) == 0)
        {
            data = data[..random.Next(data.Length + 1)];                    // cut short
        }
        else if (random.Next(10) == 0)
        {
            data = [.. data, .. new byte[random.Next(64)]];                  // padded
        }
        return data;
    }

    /// <summary>What the Replays page reads of a replay it lists (Launcher.App ReplayItem's own expressions).</summary>
    private static void Show(ReplayInfo r)
    {
        _ = r.CourseName + r.FileName + r.You + string.Join(",", r.Players) + r.Laps;
        _ = r.Recorded.ToLocalTime().ToString("MMM d, yyyy • h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        _ = r.Length.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
        _ = r.Recorder?.Name + r.Recorder?.Machine + r.Recorder?.Color;
        _ = r.RecorderResult;
        _ = r.StatPackLabel + r.Practice + ReplayWords.Racer(r) + ReplayWords.Place(r) + ReplayWords.Of(r) + ReplayWords.Time(r)
            + ReplayWords.Laps(r.Laps) + ReplayWords.Hex(r.Recorder?.Color) + ReplayWords.Machine(r.Recorder?.Machine ?? -1);
        Assert.InRange(ReplayWords.Results(r).Count, 0, ReplayWords.MostResults);
        foreach (var x in r.Results ?? [])
        {
            _ = x.Racer.Name + x.Place + x.Racer.Machine + x.Racer.Color;
            foreach (var t in new[] { x.Time, x.BestLap })
            {
                _ = t?.ToString(t.Value.TotalHours >= 1 ? @"h\:mm\:ss\.fff" : @"m\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>The fuzzer's seeds of the newer formats are replays the list shows and the launcher plays.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TheNewerFormatsSeedsAreSoundReplays(int version)
    {
        var file = Path.Combine(_dir, $"v{version}.fzrp");
        var steps = new byte[16 * 10];
        File.WriteAllBytes(file, Replay(NewerHeader(version), 3, 1, new byte[122], steps, Results, version: version));
        Assert.True(ReplayFile.Read(file) is { Practice: true, StatPackProblem: null }, ReplayFile.Problem(file));
        Assert.Null(ReplayFile.Problem(file));
    }

    [Fact]
    public void NoReplayFileMakesItsReadersThrow()
    {
        var file = Path.Combine(_dir, "f.fzrp");
        Loop("replay", 4000, MutatedReplay, data =>
        {
            File.WriteAllBytes(file, data);
            if (ReplayFile.Read(file) is { } info)
            {
                Show(info);
            }
            _ = ReplayFile.Problem(file);
            _ = ReplayFile.ModImage(file);
            _ = ReplayFile.RecordedBy(file);
            foreach (var listed in ReplayFile.List(_dir))
            {
                Show(listed);
            }
        });
    }

    [Fact]
    public void NoLanAnnouncementMakesTheListThrow()
    {
        var seed = Encoding.UTF8.GetString(new LanAnnouncement(LanAnnouncement.ServiceName, "Den", 47810, ControlProtocol.Version).Encode());
        using var browser = new LanBrowser(port: 0);
        Loop("lan", 4000, random =>
        {
            var data = Encoding.UTF8.GetBytes(MutateJson(seed, random));
            if (random.Next(4) == 0)
            {
                Bytes(data, random);
            }
            return data;
        }, data =>
        {
            if (LanAnnouncement.Decode(data) is { } a)
            {
                browser.Heard(a, new IPAddress([192, 168, 1, (byte)(data.Length % 250)]));
            }
            foreach (var s in browser.Servers)
            {
                _ = s.Name + s.Coordinator + s.Address + s.ControlPort;     // what the LAN page shows of each
            }
        });
    }

    [Fact]
    public void NoRelayPacketMakesItsReaderThrow()
    {
        var seeds = new RelayPacket[]
        {
            new Ping(1, 2), new Pong(1, 2), new Pong(1, 2, 3), new Join(4), new Joined(1, 2, 3), new Ready(4), new Go(1, 180),
            new InputPacket(4, 10, 9, 2, new byte[12]), new OfficialPacket(10, 2, 3, new byte[24]), new ChecksumPacket(4, 59, 7),
            new Desync(59), new SeatLeft(2),
        }.Select(RelayProtocol.Encode).ToArray();
        Loop("relay", 20000, random =>
        {
            var data = (byte[])seeds[random.Next(seeds.Length)].Clone();
            Bytes(data, random);
            return random.Next(5) == 0 ? data[..random.Next(data.Length + 1)] : random.Next(8) == 0 ? [.. data, .. new byte[random.Next(40)]] : data;
        }, data =>
        {
            _ = RelayProtocol.Decode(data);
            _ = RelayProtocol.ForeignVersion(data);
        });
    }
}
