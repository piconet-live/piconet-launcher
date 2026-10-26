using System.Buffers.Binary;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// A stat pack's code constants imported from a modified main module (<see cref="StatPackImport.ModuleDifferences"/>;
/// handbook/stat-pack-constants.md), on a made-up module with the game's section table: the allowed words are kept,
/// the dash plate's formula rewritten as GX Unleashed rewrites it is read back into its two constants, and every
/// other difference is named by where it is and left out.
/// </summary>
public sealed class StatPackImportModuleTests
{
    private const int Energy = (int)GameFacts.Files.ModuleBoostEnergyUse, Cap = (int)GameFacts.Files.ModuleSpeedCap;
    private const int Dash = (int)GameFacts.Files.ModuleDashPlate, Pool = (int)GameFacts.Files.ModuleDashPool;

    private static void Put(byte[] m, int at, uint word) => BinaryPrimitives.WriteUInt32BigEndian(m.AsSpan(at), word);

    /// <summary>A module of the game's shape: module 1, its code (section 1), constants (4) and data (5) where the
    /// game's are, and the stock words of every place the import reads.</summary>
    private static byte[] Stock()
    {
        var m = new byte[0x1B0000];
        Put(m, 0, 1);
        Put(m, 0x0C, 6);
        Put(m, 0x10, 0x40);
        Put(m, 0x40 + 8, 0xDC | 1);
        Put(m, 0x40 + 12, 0x15F62C);
        Put(m, 0x40 + 32, 0x15F710);
        Put(m, 0x40 + 36, 0xDEF0);
        Put(m, 0x40 + 40, 0x16D600);
        Put(m, 0x40 + 44, 0x30000);
        Put(m, Energy, 0x3E2AAAAB);
        Put(m, Cap, 0x40C38300);
        Put(m, Dash, 0xC03F0258);
        Put(m, Dash + 8, 0xC05F001C);
        Put(m, Dash + 0x1C, 0xEC020032);
        Put(m, Pool + 0x2C4, BitConverter.SingleToUInt32Bits(15));
        Put(m, Pool + 0x300, BitConverter.SingleToUInt32Bits(30));
        return m;
    }

    [Fact]
    public void GxUnleashedsChangesBecomeItsConstantsAndTheRestIsNamed()
    {
        var disc = Stock();
        var mod = (byte[])disc.Clone();
        Put(mod, Energy, 0x3E19999A);
        Put(mod, Cap, 0x7FEFFFFF);
        Put(mod, Cap + 4, 0xFFFFFFFF);
        Put(mod, Dash, 0xC03F02C4);
        Put(mod, Dash + 8, 0xC05F0300);
        Put(mod, Dash + 0x1C, 0xFC02002A);
        Put(mod, 0x16D600 + 0x28060, 7);      // the menus' copy of the stats: data
        Put(mod, 0x16D600 + 0x28064, 8);
        Put(mod, 0x20, 9);                     // the header
        var (constants, ignored) = StatPackImport.ModuleDifferences(disc, mod);
        Assert.Equal(
        [
            new StatEntry(StatPackConstants.Block, 0, 0x3E19999A), new StatEntry(StatPackConstants.Block, 1, 0x7FEFFFFF),
            new StatEntry(StatPackConstants.Block, 2, 0xFFFFFFFF), new StatEntry(StatPackConstants.Block, 3, BitConverter.SingleToUInt32Bits(30)),
            new StatEntry(StatPackConstants.Block, 4, BitConverter.SingleToUInt32Bits(15)),
        ], constants);
        Assert.Equal(
        [
            "its dash plate's code is int(A + B x boost duration), A = 30, B = 15: carried as DashPlateBase and DashPlateScale",
            "2 other words of its data (.data): not on the list of allowed words, left out",
            "1 other word of its header and relocations: not on the list of allowed words, left out",
        ], ignored);
        var (pack, refusal) = StatPack.Of("GX Unleashed", constants);
        Assert.Null(refusal);
        Assert.Equal(5, pack!.Constants.Count);
    }

    [Fact]
    public void AnUnchangedModuleGivesNothingAndOtherCodeIsLeftOut()
    {
        var disc = Stock();
        Assert.Equal((0, 0), (StatPackImport.ModuleDifferences(disc, disc).Constants.Count, StatPackImport.ModuleDifferences(disc, disc).Ignored.Count));
        var mod = (byte[])disc.Clone();
        Put(mod, Dash + 0x1C, 0xEC000032);     // the dash plate's code in another shape
        Put(mod, 0x100, 0x60000000);          // a word of code elsewhere
        Put(mod, Pool + 0x258, 1);            // a shared constant (60.0), read a dozen times
        var (constants, ignored) = StatPackImport.ModuleDifferences(disc, mod);
        Assert.Empty(constants);
        Assert.Equal(
        [
            "its dash plate's code is changed in a way no constant carries: left out",
            "1 other word of its code: not on the list of allowed words, left out (the race runs the disc's code there: .text+0x24)",
            "1 other word of its constants (.rodata): not on the list of allowed words, left out",
        ], ignored);
    }

    /// <summary>A module cut short after the least the import takes, whose dash plate's code names a constant past
    /// its end: named and left out (a read past the end must not throw out of the import).</summary>
    [Fact]
    public void ADashPlateConstantPastTheModulesEndIsLeftOut()
    {
        var disc = Stock();
        var mod = disc.AsSpan(0, Pool + 0x1000).ToArray();
        Put(mod, Dash, 0xC03F7FFC);
        Put(mod, Dash + 8, 0xC05F0300);
        Put(mod, Dash + 0x1C, 0xFC02002A);
        var (constants, ignored) = StatPackImport.ModuleDifferences(disc, mod);
        Assert.Empty(constants);
        Assert.Contains("its dash plate's code is changed in a way no constant carries: left out", ignored);
    }

    private const int Curve = (int)GameFacts.Files.ModuleSettingCurve, BoostLength = (int)GameFacts.Files.ModuleBoostLength;

    private static uint F(float value) => BitConverter.SingleToUInt32Bits(value);

    /// <summary>The disc's machine setting at five of its loads (fn_1_22B10 + offset, the pool's offset), with their
    /// numbers, and two spare words of the pool.</summary>
    private static byte[] StockWithCurve()
    {
        var m = Stock();
        Put(m, Curve + 0x038, 0xC084007C);    // lfs f4, 0x7C(r4): DriftFastLoss 1.8
        Put(m, Curve + 0x0C4, 0xC064008C);    // lfs f3, 0x8C(r4): DriftCap 2.3, a word only it reads
        Put(m, Curve + 0x020, 0xC0040014);    // lfs f0, 0x14(r4): DriftFastFloor and SpeedFastRef, 1.0 (shared)
        Put(m, Curve + 0x010, 0xC064000C);    // lfs f3, 0x0C(r4): a test of the setting's side, 0.0
        Put(m, Curve + 0x204, 0xC88400B8);    // lfd f4, 0xB8(r4): AccelGain
        Put(m, BoostLength, 0xC05F0258);       // lfs f2, 0x258(r31): the boost's 60.0
        Put(m, Pool + 0x7C, F(1.8f));
        Put(m, Pool + 0x8C, F(2.3f));
        Put(m, Pool + 0x14, F(1.0f));
        Put(m, Pool + 0xB8, 0x3FE33333);
        Put(m, Pool + 0xBC, 0x40000000);
        Put(m, Pool + 0x258, F(60.0f));
        Put(m, Pool + 0xE8, F(1.0f));         // F-Zero GX+'s target
        Put(m, Pool + 0xF0, 0x3FF00000);       // a double 1.0
        Put(m, Pool + 0xF4, 0);
        Put(m, Pool + 0xF8, F(50.0f));
        return m;
    }

    [Fact]
    public void TheMachineSettingsChangesBecomeItsSlots()
    {
        var disc = StockWithCurve();
        Assert.Empty(StatPackImport.ModuleDifferences(disc, disc).Constants);
        var mod = (byte[])disc.Clone();
        Put(mod, Curve + 0x038, 0xC08400E8);  // F-Zero GX+'s: the drift loss at another word, 1.0
        Put(mod, Pool + 0x8C, F(2.5f));       // the drift cap's own word changed
        Put(mod, Curve + 0x204, 0xC88400F0);  // the acceleration gain at a double 1.0
        Put(mod, BoostLength, 0xC05F00F8);     // the boost's 60 at 50
        var (constants, ignored) = StatPackImport.ModuleDifferences(disc, mod);
        Assert.Equal(
        [
            new StatEntry(StatPackConstants.Block, StatPackConstants.BoostLengthScale, F(50.0f)),
            new StatEntry(StatPackConstants.Block, StatPackConstants.Named("DriftFastLoss")!.Slot, F(1.0f)),
            new StatEntry(StatPackConstants.Block, StatPackConstants.Named("DriftCap")!.Slot, F(2.5f)),
            new StatEntry(StatPackConstants.Block, StatPackConstants.Named("AccelGain")!.Slot, 0x3FF00000),
            new StatEntry(StatPackConstants.Block, StatPackConstants.Named("AccelGain")!.Slot + 1, 0),
        ], constants);
        Assert.Equal(
        [
            "its machine setting's DriftFastLoss is 1 (the game's 1.8): carried",
            "its machine setting's DriftCap is 2.5 (the game's 2.3): carried",
            "its machine setting's AccelGain is 1 (the game's 0.6000000238418579): carried",
            "its energy boost lasts int(50 x boost duration) frames: carried as BoostLengthScale",
        ], ignored);
        Assert.Null(StatPack.Of("Curve", constants).Refusal);
    }

    [Fact]
    public void WhatTheCurveCannotCarryIsLeftOut()
    {
        var disc = StockWithCurve();
        var mod = (byte[])disc.Clone();
        Put(mod, Pool + 0x14, F(1.1f));      // 1.0, which 59 other loads of the game read
        Put(mod, Curve + 0x010, 0xC06400E8);  // the side's test reads 1.0
        Put(mod, Curve + 0x038, 0xEC000032);  // a load made another instruction
        var (constants, ignored) = StatPackImport.ModuleDifferences(disc, mod);
        Assert.Empty(constants);
        Assert.Equal(
        [
            "its machine setting's test of the setting's side at .text+0x22B20 reads another number: left out",
            "its pool word .rodata+0x854, which the machine setting and other routines read, is changed: left out",
            "its machine setting's load at .text+0x22B48 is changed into another instruction: left out",
            "1 other word of its code: not on the list of allowed words, left out (the race runs the disc's code there: .text+0x22B48)",
        ], ignored);   // (the shared pool word is named once, not counted again among the constants' other words)
    }

    [Fact]
    public void TheLongestBoostIsAMachinesOrThreePartsSummed()
    {
        var data = new byte[(int)GameFacts.Files.CarPartNamesAt];
        void Duration(int block, float seconds) =>
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(StatPackRules.BlockAt(block) + 0x38), F(seconds));
        Duration(3, 2.5f);
        Assert.Equal(2.5f, StatPackImport.LongestBoost(data));
        Duration(StatPackRules.Machines + 4, 1.0f);
        Duration(StatPackRules.Machines + 30, 1.0f);
        Duration(StatPackRules.Machines + 74, 1.0f);
        Assert.Equal(3.0f, StatPackImport.LongestBoost(data));
    }

    [Fact]
    public void AFileThatIsNoMainModuleIsRefused()
    {
        Assert.NotNull(StatPackImport.ModuleOf([1, 2, 3, 4, 5, 6, 7, 8, 9]).Refusal);
        Assert.NotNull(StatPackImport.ModuleOf(new byte[64]).Refusal);
    }
}
