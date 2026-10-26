using System.Buffers.Binary;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The parts' stock colours from the main module (<see cref="PartColours"/>): read from made-up modules, and
/// once from your own disc where this computer has it. No colour of the game is written here.</summary>
public sealed class PartColoursTests
{
    // a made-up module: every part's four colours different, of the paint slot
    private static byte[] Module(Func<int, int, uint>? word = null)
    {
        var module = new byte[PartColours.TableAt + (75 * 4 * 4) + 16];
        for (var part = 0; part < 75; part++)
        {
            for (var n = 0; n < 4; n++)
            {
                var w = word?.Invoke(part, n) ?? PartColours.Word((byte)part, (byte)n, 7);
                BinaryPrimitives.WriteUInt32BigEndian(module.AsSpan(PartColours.TableAt + (((part * 4) + n) * 4)), w);
            }
        }
        return module;
    }

    [Fact]
    public void EachPartHasItsFourStockColoursInOrder()
    {
        var table = PartColours.Read(Module());

        Assert.NotNull(table);
        Assert.Equal(75, table.Length);
        Assert.All(table, colours => Assert.Equal(4, colours.Length));
        Assert.Equal(PartColours.Word(0, 0, 7), table[0][0]);
        Assert.Equal(PartColours.Word(25, 3, 7), table[25][3]);
        Assert.Equal(PartColours.Word(74, 2, 7), table[74][2]);
    }

    [Fact]
    public void BytesThatAreNotTheTableAreNoTable()
    {
        Assert.Null(PartColours.Read(Module().AsSpan(0, PartColours.TableAt + (75 * 16) - 1)));   // too short
        Assert.Null(PartColours.Read([]));
        // a word of another slot paints nothing: not the game's table
        Assert.Null(PartColours.Read(Module((part, n) => part == 40 && n == 1 ? 0xFF000002u : PartColours.Word(1, 2, 3))));
        Assert.Null(PartColours.Read(Module((_, _) => 0u)));
    }

    [Fact]
    public void APaintIsItsRgbAndTheSlot()
    {
        Assert.Equal(0x12345601u, PartColours.Word(0x12, 0x34, 0x56));
        Assert.Equal(1u, PartColours.Word(0, 0, 0));   // black is no 0: 0 is the part's own colour
        Assert.Equal(((byte)0x12, (byte)0x34, (byte)0x56), PartColours.Rgb(0x12345601u));
        Assert.Null(new Online.Protocol.CustomMachine(3, 43, 58, 6, [PartColours.Word(255, 255, 255), 0u, PartColours.Word(0, 0, 0)]).Problem());
    }

    [Fact]
    public void TheRealDiscHasFourColoursOfThePaintSlotForEveryPart()
    {
        var image = RealDiscStatsTests.PlayersDisc();
        Assert.SkipWhen(image is null || !File.Exists(image), "no disc image on this computer: set DISC_DEFAULT to your own F-Zero GX (USA) ISO in ~/.config/piconet/pins.tsv");
        using var art = DiscArt.Open(image);
        Assert.NotNull(art);

        var table = art.PartColours();

        Assert.NotNull(table);
        Assert.Equal(75, table.Length);
        Assert.All(table, colours => Assert.Equal(4, colours.Distinct().Count()));   // four different colours a part
    }
}
