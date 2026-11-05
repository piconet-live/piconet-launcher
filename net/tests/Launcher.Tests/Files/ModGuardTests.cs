using System.Buffers.Binary;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The mod guard (<see cref="ModGuard"/>) on made-up files: car data passes for the race only when every field the
/// race reads is the disc's, a course file only when nothing but its fog differs; and the overlays a start is given
/// (<see cref="ModOverlay"/>): code never, single-player files offline only, the file of a mod on top over a lower one's.
/// </summary>
public sealed class ModGuardTests
{
    [Theory]
    [InlineData("main.dol", ModFileClass.Code)]
    [InlineData("fze.sample.rel", ModFileClass.Code)]
    [InlineData("enemy_line/line__.bin", ModFileClass.Code)]
    [InlineData("game/cardata.lz", ModFileClass.CarData)]
    [InlineData("stage/COLI_COURSE05.lz", ModFileClass.Course)]
    [InlineData("staff_ghost/ghost1.dat", ModFileClass.Ghost)]
    [InlineData("stage/st05.tpl.lz", ModFileClass.Look)]
    [InlineData("snd/adx_bgm/garage.adx", ModFileClass.Look)]
    public void AFileIsClassedByItsPathOnTheDisc(string path, ModFileClass kind) => Assert.Equal(kind, ModGuard.Classify(path));

    [Fact]
    public void CarDataWithOnlyTheCameraChangedIsForTheRace()
    {
        var disc = MadeUpCarData.File();
        var camera = MadeUpCarData.With((6, 0x4C, MadeUpCarData.Bits(3)), (12, 0x50, MadeUpCarData.Bits(2)));
        Assert.True(ModGuard.CarData(camera, disc).Online);
        // names differ too: the race reads none
        Assert.True(ModGuard.CarData(MadeUpCarData.File(names: "OTHER\0NAMES\0"), disc).Online);
    }

    [Theory]
    [InlineData(6, 0x04)]     // weight
    [InlineData(0, 0x34)]     // boost strength
    [InlineData(40, 0x60)]    // a hover point
    [InlineData(3, 0x90)]     // a wall collision point
    [InlineData(5, 0x48)]     // the two bytes
    [InlineData(60, 0x08)]    // a body part's acceleration
    public void CarDataWithARaceFieldChangedIsSinglePlayerOnly(int block, int at)
    {
        var disc = MadeUpCarData.File();
        var changed = MadeUpCarData.With((block, at, MadeUpCarData.Bits(0.5f)));
        var verdict = ModGuard.CarData(changed, disc);
        Assert.False(verdict.Online);
        Assert.Contains("single player", verdict.Why, StringComparison.Ordinal);
    }

    /// <summary>A made-up course file the stage editor's reader takes: the header (0xE8), the fog at 0x100, one fog
    /// curve's two keys at 0x150, and data after it that stands for the course.</summary>
    private static byte[] Course()
    {
        var d = new byte[0x400];
        void Word(int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(at), value);
        void Float(int at, float value) => BinaryPrimitives.WriteSingleBigEndian(d.AsSpan(at), value);
        Word(0x20, 0xE8);
        Word(0x24, 0xFC);
        Float(0xFC, -10);
        Word(0x80, 0x120);   // fog curves
        Word(0x84, 0x100);   // fog
        Word(0x90, 0x1A0);
        Float(0x1A0, 3000);
        // fog: type, near, far, colour
        Word(0x100, 0x04000000);
        Float(0x104, 300);
        Float(0x108, 3500);
        Float(0x10C, 1);
        Float(0x110, 1);
        Float(0x114, 0.92f);
        // one curve of two keys at 0x150, the five others empty
        Word(0x120, 2);
        Word(0x124, 0x150);
        Float(0x158, 1);
        Float(0x16C, 2);
        // what stands for the course: anything after the fog
        for (var i = 0x200; i < d.Length; i++)
        {
            d[i] = (byte)(i * 7);
        }
        return d;
    }

    [Fact]
    public void ACourseWhoseFogAloneChangedIsForTheRace()
    {
        var disc = Course();
        var mod = Course();
        BinaryPrimitives.WriteSingleBigEndian(mod.AsSpan(0x10C), 0.2f);    // the colour, as Tron Aeropolis
        BinaryPrimitives.WriteSingleBigEndian(mod.AsSpan(0x104), 0);       // the near distance
        BinaryPrimitives.WriteSingleBigEndian(mod.AsSpan(0x15C), 0.5f);    // a fog key
        mod[0x100] = 2;                                                    // the fog's type
        Assert.True(ModGuard.Course(mod, disc).Online);
    }

    [Theory]
    [InlineData(0x2F0)]   // the course's data
    [InlineData(0x124)]   // the fog curves' own table: where the keys are
    [InlineData(0x00)]    // the header's "view range", whose meaning is unknown
    public void ACourseChangedElsewhereIsSinglePlayerOnly(int at)
    {
        var disc = Course();
        var mod = Course();
        mod[at] ^= 0x40;
        var verdict = ModGuard.Course(mod, disc);
        Assert.False(verdict.Online);
        Assert.StartsWith("Course changed", verdict.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongerCourseIsSinglePlayerOnly()
    {
        var disc = Course();
        Assert.False(ModGuard.Course([.. disc, 0, 0, 0, 0], disc).Online);
    }

    private static InstalledMod Mod(string id, bool on, params ModFile[] files) =>
        new(new ModManifest(id, id, id + ".zip", "", DateTimeOffset.UnixEpoch, [new ModPart("", true, files, [])], new Dictionary<string, int>(), null),
            Path.Combine(Path.GetTempPath(), "mods", id), on);

    private static ModFile File(string disc, ModFileClass kind, bool online) => new(disc, kind, online, online ? null : "why", false, 1, "");

    [Fact]
    public void OfflinePlayTakesTheWholeModAndEveryOtherStartTheGuardedPart()
    {
        var first = Mod("a", true,
            File("stage/st05.tpl.lz", ModFileClass.Look, true),
            File("staff_ghost/ghost1.dat", ModFileClass.Ghost, false),
            File("game/cardata.lz", ModFileClass.CarData, false),
            File("enemy_line/line__.bin", ModFileClass.Code, false));
        var second = Mod("b", true, File("stage/st05.tpl.lz", ModFileClass.Look, true), File("bg/bg_tow.tpl.lz", ModFileClass.Look, true));
        var off = Mod("c", false, File("bg/bg_big.tpl.lz", ModFileClass.Look, true));

        var overlay = ModOverlay.Of([first, second, off]);

        Assert.Equal(["bg/bg_tow.tpl.lz", "game/cardata.lz", "staff_ghost/ghost1.dat", "stage/st05.tpl.lz"], overlay.Offline.Select(f => f.Disc));
        Assert.Equal(["bg/bg_tow.tpl.lz", "stage/st05.tpl.lz"], overlay.Guarded.Select(f => f.Disc));
        // the mod on top's file wins
        Assert.Contains(Path.Combine("mods", "a", ModManifest.StoreFolder), overlay.Guarded.Single(f => f.Disc == "stage/st05.tpl.lz").From, StringComparison.Ordinal);
    }

    [Fact]
    public void TheModOnTopTakesPriority()
    {
        var look = File("stage/st05.tpl.lz", ModFileClass.Look, true);
        InstalledMod top = Mod("top", true, look), middle = Mod("middle", true, look, File("bg/bg_tow.tpl.lz", ModFileClass.Look, true)),
            bottom = Mod("bottom", true, look);
        string From(ModOverlay o, string disc) => Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(o.Offline.Single(f => f.Disc == disc).From))!)!;
        Assert.Equal("top", From(ModOverlay.Of([top, middle, bottom]), "stage/st05.tpl.lz"));
        Assert.Equal("middle", From(ModOverlay.Of([top, middle, bottom]), "bg/bg_tow.tpl.lz"));
        // moved down below the others: the one now on top wins; switched off: the next one down
        Assert.Equal("middle", From(ModOverlay.Of([middle, bottom, top]), "stage/st05.tpl.lz"));
        Assert.Equal("bottom", From(ModOverlay.Of([top with { On = false }, middle with { On = false }, bottom]), "stage/st05.tpl.lz"));
    }

    [Fact]
    public void ACheckedCourseFileGoesToEveryStartMarkedRaceSame()
    {
        var overlay = ModOverlay.Of([Mod("t", true, File("stage/COLI_COURSE05.lz", ModFileClass.Course, true),
            File("game/cardata.lz", ModFileClass.CarData, false), File("stage/st05.tpl.lz", ModFileClass.Look, true))]);
        Assert.Equal([("stage/COLI_COURSE05.lz", true), ("stage/st05.tpl.lz", false)], overlay.Guarded.Select(f => (f.Disc, f.RaceSame)));
        Assert.Contains(overlay.Offline, f => f is { Disc: "game/cardata.lz", RaceSame: false });
        var json = System.Text.Json.Nodes.JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", overlay: overlay.Guarded))!["overlay"]!.AsArray();
        Assert.True((bool?)json[0]!["raceSame"]);
        Assert.Null(json[1]!["raceSame"]);
    }

    [Fact]
    public void TheLaunchFileHasAnOverlayOnlyWithModsOn()
    {
        var none = GameLaunchFile.Offline(null);
        Assert.DoesNotContain("overlay", none, StringComparison.Ordinal);
        Assert.Equal(none, GameLaunchFile.Offline(null, overlay: ModOverlay.Of([Mod("x", false, File("bg/a", ModFileClass.Look, true))]).Offline));

        var with = GameLaunchFile.Offline(null, overlay: [new OverlayFile("stage/st05.tpl.lz", @"D:\m\files\stage\st05.tpl.lz")]);
        var json = System.Text.Json.Nodes.JsonNode.Parse(with)!["overlay"]!.AsArray();
        Assert.Equal("stage/st05.tpl.lz", (string?)json[0]!["disc"]);
        Assert.Equal(@"D:\m\files\stage\st05.tpl.lz", (string?)json[0]!["from"]);
    }

    [Fact]
    public void LetteredAlternativesAndOptionalStepsAreOffUntilChosen()
    {
        string[] roots =
        [
            "1 - Required files", "2A - Standard/1 (Optional) - Textures/A", "2A - Standard/2A - Standard/Step 1A - Camera",
            "2A - Standard/2A - Standard/Step 1B - Camera removed", "2A - Standard/2A - Standard/Step 2A - Ghosts",
            "2A - Standard/2A - Standard/Step 2C - Ghosts", "2A - Standard/2A - Standard/Step Final - Required files",
            "2A - Standard/2B - Standard and Story/Step 1 - Story", "2B - Legends/2A - Legends/Step 1A - Camera", "Japanese fixes",
        ];
        var on = ModImport.Defaults([.. roots.Select(r => new ModRoot(r, 1, false))]);
        Assert.Equal(
            ["1 - Required files", "2A - Standard/2A - Standard/Step 1A - Camera", "2A - Standard/2A - Standard/Step 2A - Ghosts",
             "2A - Standard/2A - Standard/Step Final - Required files"],
            roots.Where(on.Contains));
    }
}
