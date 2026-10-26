using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// The import against the game itself: your own disc image, where this computer has it, and skipped where it
/// does not (as <see cref="RealDiscStatsTests"/>). Every other test of the import makes its car data up, and so
/// holds the import only to the test's own idea of the file. This one holds it to the game: the disc's car data is
/// car data by the import's shape check, it differs from itself nowhere, every one of its own values is inside its
/// range (so that a pack can name any of them), and one changed weight is a pack of one value. Nothing is taken from
/// the disc into the repository.
/// </summary>
public sealed class RealDiscStatPackTests
{
    private static string? PlayersDisc() => RealDiscStatsTests.PlayersDisc();

    [Fact]
    public void TheRealDiscsCarDataIsCarDataAndEveryValueOfItIsInsideItsRange()
    {
        var image = PlayersDisc();
        Assert.SkipWhen(image is null || !File.Exists(image), "no disc image on this computer: set DISC_DEFAULT to your own F-Zero GX (USA) ISO in ~/.config/piconet/pins.tsv");
        using var art = DiscArt.Open(image);
        var disc = art!.CarData();
        Assert.NotNull(disc);
        Assert.True(art.IsUnchanged(DiscPins.Game));          // the image the import holds a disc to: every file of it

        var (unpacked, refusal) = StatPackImport.CarDataOf(disc);

        Assert.Null(refusal);
        var data = unpacked!;
        Assert.Equal("Every value of the file is the disc's own: there is nothing for a pack to hold.", StatPackImport.Differences(data, data, "p").Refusal);
        // every word of the disc, as a pack would hold it, is one a pack may hold
        for (var block = 0; block < StatPackRules.Blocks; block++)
        {
            for (var at = 4; at < StatPackRules.BlockSize; at += 4)
            {
                var place = data.AsSpan(StatPackRules.BlockAt(block) + at);
                var bits = at == 0x48 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(place) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(place);
                Assert.True(StatPackRules.Problem(block, at, bits) is null, $"{StatPackRules.BlockName(block)} {StatPackRules.WordName(at)} of the disc is outside its range");
            }
        }
        // one machine's weight doubled: a pack of that one value
        var changed = (byte[])data.Clone();
        var weight = BitConverter.ToSingle(changed, (6 * StatPackRules.BlockSize) + 4);
        BitConverter.GetBytes(2 * weight).CopyTo(changed, (6 * StatPackRules.BlockSize) + 4);
        var (pack, _, names) = StatPackImport.Differences(data, changed, "Twice the weight");
        Assert.Equal([StatEntry.Of(6, 4, 2 * weight)], pack!.Entries);
        Assert.False(names);
        Assert.Equal(2 * art.MachineStats()!.Value(6, MachineStat.Weight), art.MachineStats()!.With(pack).Value(6, MachineStat.Weight));
    }
}
