using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// The stat packs' files as the launcher reads them from other people (a hostile or broken file): a changed car data file
/// and a modded main module (<c>--import-stat-pack</c>: a mod somebody made), a pack file, and a pack as a server
/// sends it. Whatever they hold, a reader answers with a pack or a sentence, never with an exception, and soon.
/// </summary>
public sealed class SecurityImportTests
{
    private const int Pool = (int)GameFacts.Files.ModuleDashPool, Dash = (int)GameFacts.Files.ModuleDashPlate;

    private static readonly double Seconds =
        double.TryParse(Environment.GetEnvironmentVariable("FZGX_FUZZ_SECONDS"), out var s) ? s : 0;

    private static void Put(byte[] m, int at, uint word) => BinaryPrimitives.WriteUInt32BigEndian(m.AsSpan(at), word);

    /// <summary>A modded module whose dash plate has GX Unleashed's shape but names its two numbers past the file's end
    /// (the module need only reach 4 KiB past the pool; the loads name up to 32 KiB): the import read past its end
    /// and ended with an exception nothing caught. Such a formula is now named and left out.</summary>
    [Fact]
    public void ADashPlateThatNamesNumbersPastTheModulesEndIsLeftOut()
    {
        var disc = new byte[Pool + 0x1000];
        Put(disc, 0, 1);
        Put(disc, Dash, 0xC03F0258);
        Put(disc, Dash + 8, 0xC05F001C);
        Put(disc, Dash + 0x1C, 0xEC020032);
        var mod = (byte[])disc.Clone();
        Put(mod, Dash, 0xC03F7FF0);
        Put(mod, Dash + 8, 0xC05F0300);
        Put(mod, Dash + 0x1C, 0xFC02002A);
        var (constants, ignored) = StatPackImport.ModuleDifferences(disc, mod);
        Assert.Empty(constants);
        Assert.Contains("its dash plate's code is changed in a way no constant carries: left out", ignored);
    }

    private static void Loop(int smoke, Action<Random> once)
    {
        var random = new Random(20261019);
        var end = DateTime.UtcNow.AddSeconds(Seconds);
        var clock = Stopwatch.StartNew();
        long n = 0;
        for (; Seconds > 0 ? DateTime.UtcNow < end : n < smoke; n++)
        {
            once(random);
        }
        TestContext.Current.SendDiagnosticMessage($"{n} inputs in {clock.Elapsed.TotalSeconds:0} s");
    }

    private static void Turn(byte[] data, Random random)
    {
        for (var k = random.Next(1, 9); k > 0 && data.Length > 0; k--)
        {
            data[random.Next(data.Length)] = (byte)random.Next(256);
        }
    }

    /// <summary>Car data and main modules of any bytes, packed or not: a refusal or the file, never an exception.</summary>
    [Fact]
    public void NoCarDataOrModuleFileMakesTheImportThrow()
    {
        var car = MadeUpCarData.File();
        var disc = new byte[Pool + 0x1000];
        Put(disc, 0, 1);
        Loop(300, random =>
        {
            // a car data file, changed, or a packed file's header over any bytes
            var data = random.Next(3) == 0 ? new byte[random.Next(16, 0x7000)] : (byte[])car.Clone();
            if (random.Next(2) == 0)
            {
                random.NextBytes(data);
                BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)(data.Length - 8));
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)random.Next(0, 0x6001));
            }
            Turn(data, random);
            if (StatPackImport.CarDataOf(data) is { Data: { } changed })
            {
                _ = StatPackImport.Differences(car, changed, "GX Fuzz");
            }
            // a module: the made-up one changed anywhere, or a packed file of any bytes
            var module = (byte[])disc.Clone();
            for (var k = random.Next(1, 12); k > 0; k--)
            {
                var at = random.Next(4) == 0 ? Dash + (random.Next(8) * 4) : random.Next(module.Length / 4) * 4;
                Put(module, at, random.Next(2) == 0 ? (uint)random.Next() : (random.Next(2) == 0 ? 0xC03F0000u : 0xC05F0000u) | (uint)random.Next(0x8000));
            }
            Put(module, Dash + 0x1C, random.Next(2) == 0 ? 0xFC02002Au : (uint)random.Next());
            _ = StatPackImport.ModuleDifferences(disc, module.AsSpan(0, random.Next(Pool + 0x1000, module.Length + 1)));
            var packed = new byte[random.Next(8, 4096)];
            random.NextBytes(packed);
            BinaryPrimitives.WriteUInt32LittleEndian(packed, (uint)(packed.Length - 8));
            BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(4), (uint)random.Next(0, 1 << 16));
            _ = StatPackImport.ModuleOf(MachineStatFile.Decrypted(packed));
        });
    }

    /// <summary>Pack files and packs as a server sends them, of any text: a pack or a sentence.</summary>
    [Fact]
    public void NoPackFileOrServersPackMakesItsReaderThrow()
    {
        var (pack, _) = StatPack.Of("GX Fuzz", [StatEntry.Of(6, 4, 2520), StatEntry.Of(41 + 59, 0x04, 900), new StatEntry(41 + 27, 0x48, 0x05000000)],
            "someone");
        var text = StatPackText.Write(pack!);
        var wire = pack!.ToWire();
        string[] odd = ["", " ", "\t", "-1", "1e999", "NaN", "999999999999", "0x10", "‮", "#", "name", "author", "\n", "Name", "constant"];
        Loop(20000, random =>
        {
            var lines = text.Split('\n').ToList();
            for (var k = random.Next(1, 4); k > 0; k--)
            {
                var n = random.Next(lines.Count);
                var words = lines[n].Split(' ').ToList();
                switch (random.Next(4))
                {
                    case 0:
                        words[random.Next(words.Count)] = odd[random.Next(odd.Length)];
                        break;
                    case 1:
                        words.Insert(random.Next(words.Count + 1), odd[random.Next(odd.Length)]);
                        break;
                    case 2:
                        lines.Insert(n, lines[random.Next(lines.Count)]);
                        break;
                    default:
                        lines.RemoveAt(n);
                        break;
                }
                if (lines.Count > n)
                {
                    lines[n] = string.Join(' ', words);
                }
            }
            var changed = string.Join('\n', lines);
            if (StatPackText.Read(changed).Pack is { } read)
            {
                _ = StatPackText.Write(read);
                _ = read.UntriedWarning();
            }
            var values = Encoding.ASCII.GetBytes(wire.Values);
            Turn(values, random);
            _ = StatPack.FromWire(wire with { Values = Encoding.ASCII.GetString(values) });
            var bytes = Convert.FromBase64String(wire.Values);
            Turn(bytes, random);
            _ = StatPack.FromWire(wire with { Values = Convert.ToBase64String(bytes[..random.Next(bytes.Length + 1)]) });
        });
    }
}
