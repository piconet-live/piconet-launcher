using System.Buffers.Binary;
using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// A course of a mod that is single player only takes the mod's looks of it with it (<see cref="ModManifest.Files"/>):
/// the course's models and textures, its course-select preview and the background it loads, which the game picks
/// from the main module's venue table (<see cref="CourseBackgrounds"/>). GXtreme's Story Course Replacements on its
/// real listing (ModListings/gxtreme_v5.tsv); the table on a made-up module, and on the player's disc with MODS_DISC.
/// </summary>
public sealed class ModCourseLooksTests
{
    /// <summary>The backgrounds the disc's game picks for GXtreme's story courses (MODS_DISC's table below).</summary>
    private static readonly Dictionary<int, string> GXtremesBackgrounds = new() { [3] = "bg_mut", [16] = "bg_cas", [24] = "bg_ele", [28] = "bg_rai" };

    /// <summary>A part of GXtreme as its listing has it: each file classed by its disc path, a course file single
    /// player only (its track changed), every other file of a look for the race.</summary>
    private static readonly string Sha = new('0', 64);

    private static ModPart Part(string root, bool on)
    {
        var files = ModTreeTests.Listing("gxtreme_v5.tsv").Where(e => e.Path.StartsWith(root + "/root/", StringComparison.Ordinal))
            .Select(e => e.Path[(root.Length + "/root/".Length)..]).Select(disc => ModGuard.Classify(disc) switch
            {
                ModFileClass.Course => new ModFile(disc, ModFileClass.Course, false, "Course changed: track shape: single player only", false, 1, Sha),
                ModFileClass.Look => new ModFile(disc, ModFileClass.Look, true, null, false, 1, Sha),
                var kind => new ModFile(disc, kind, false, "why", false, 1, Sha),
            });
        return new ModPart(root + "/root", on, [.. files], []);
    }

    private static ModManifest GXtreme(bool core, bool story, IReadOnlyDictionary<int, string>? backgrounds) =>
        new("gx", "GXtreme", "gxtreme_v5.zip", "", DateTimeOffset.UnixEpoch, [Part("Story Course Replacements", story), Part("_XTREMEv5", core)],
            new Dictionary<string, int>(), null) { Backgrounds = backgrounds };

    private static ModFile Of(ModManifest mod, string disc) => mod.Files.Single(f => f.Disc == disc);

    [Fact]
    public void GXtremesStoryCoursesTakeTheirLooksAndBackgroundToSinglePlayer()
    {
        var mod = GXtreme(true, true, GXtremesBackgrounds);
        string[] trident = ["stage/st24.gma.lz", "stage/st24.tpl.lz", "stage/selst24.gma.lz", "stage/selst24.tpl.lz", "bg/bg_ele.gma.lz", "bg/bg_ele.tpl.lz"];
        foreach (var disc in trident)
        {
            Assert.False(Of(mod, disc).Online);
            Assert.Equal($"Looks of a changed course ({CourseNames.Name(24)}): single player only", Of(mod, disc).Why);
        }
        // the other courses it changes take their own looks; the core's looks stay for the race
        Assert.False(Of(mod, "stage/st03.gma.lz").Online);
        Assert.False(Of(mod, "stage/selst28.tpl.lz").Online);
        Assert.True(Of(mod, "init/race.tpl").Online);
        Assert.True(Of(mod, "bmp_e/bmp_sel.tpl.lz").Online);

        var guarded = ModOverlay.Of([new InstalledMod(mod, Path.Combine(Path.GetTempPath(), "mods", "gx"), true)]).Guarded.Select(f => f.Disc).ToList();
        Assert.DoesNotContain("stage/COLI_COURSE24.lz", guarded);
        Assert.All(trident, disc => Assert.DoesNotContain(disc, guarded));
        Assert.Contains("init/race.tpl", guarded);
    }

    [Fact]
    public void GXtremesCoreAloneIsAsBefore()
    {
        var mod = GXtreme(true, false, GXtremesBackgrounds);
        Assert.Equal(Part("_XTREMEv5", true).Files.OrderBy(f => f.Disc, StringComparer.Ordinal), mod.Files);
        Assert.All(mod.Files.Where(f => f.Class == ModFileClass.Look), f => Assert.True(f.Online));
    }

    [Fact]
    public void ALookOfACourseWhoseCourseFileIsTheDiscsOrPassedStaysForTheRace()
    {
        ModManifest Mod(params ModFile[] files) =>
            new("m", "m", "m.zip", "", DateTimeOffset.UnixEpoch, [new ModPart("", true, files, [])], new Dictionary<string, int>(), null)
            { Backgrounds = new Dictionary<int, string> { [3] = "bg_mut", [24] = "bg_ele" } };
        var look = new ModFile("stage/st24.tpl.lz", ModFileClass.Look, true, null, false, 1, "");
        var background = new ModFile("bg/bg_ele.tpl.lz", ModFileClass.Look, true, null, false, 1, "");
        // a re-texture of Trident alone
        Assert.All(Mod(look, background).Files, f => Assert.True(f.Online));
        // with a course file whose fog alone changed (passed the guard)
        var fog = new ModFile("stage/COLI_COURSE24.lz", ModFileClass.Course, true, null, false, 1, "");
        Assert.All(Mod(look, background, fog).Files, f => Assert.True(f.Online));
        // another course changed: Trident's looks stay
        var other = new ModFile("stage/COLI_COURSE03.lz", ModFileClass.Course, false, "Course changed: objects: single player only", false, 1, "");
        Assert.All(Mod(look, background, other).Files.Where(f => f.Class == ModFileClass.Look), f => Assert.True(f.Online));
    }

    [Fact]
    public void WhereTheBackgroundsAreNotKnownEveryBackgroundGoesWithAChangedCourse()
    {
        var known = GXtreme(false, true, new Dictionary<int, string>(GXtremesBackgrounds) { [24] = "bg_tow" });
        Assert.True(Of(known, "bg/bg_ele.gma.lz").Online);
        var unknown = GXtreme(false, true, null);
        Assert.False(Of(unknown, "bg/bg_ele.gma.lz").Online);
        Assert.False(Of(unknown, "stage/st24.gma.lz").Online);
    }

    [Fact]
    public void TheBackgroundsAreKeptInTheManifest()
    {
        var folder = Path.Combine(Path.GetTempPath(), "piconet-tests", "course-looks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            ModLibrary.WriteManifest(folder, GXtreme(true, true, GXtremesBackgrounds));
            Assert.Equal(GXtremesBackgrounds, ModLibrary.ReadManifest(folder)!.Backgrounds);
            ModLibrary.WriteManifest(folder, GXtreme(true, true, null));
            Assert.Null(ModLibrary.ReadManifest(folder)!.Backgrounds);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>A made-up main module: the header, two sections (code, then .data where the game's is), the venue
    /// bytes and the names' table in .data, the names' strings after it, and the module's relocations of itself that
    /// point the table's entries at them.</summary>
    private static byte[] Module(string[] names, byte[] venues)
    {
        var data = (int)GameFacts.Rel.RelData;
        var module = new byte[CourseBackgrounds.VenuesAt + 0x200];
        void Word(int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(module.AsSpan(at), value);
        Word(0x00, GameFacts.Rel.MainRelId);
        Word(0x0C, 2);
        Word(0x10, 0x40);
        Word(0x40, 0x100 | 1);    // code
        Word(0x44, 0x100);
        Word(0x48, (uint)data);   // .data
        Word(0x4C, 0x40000);
        venues.CopyTo(module, CourseBackgrounds.VenuesAt);
        var strings = CourseBackgrounds.VenuesAt + 0x80;
        var relocations = new List<(ushort Step, byte Type, byte Section, uint Addend)> { (0, 202, 1, 0) };
        var place = 0;
        for (int i = 0, at = strings; i < names.Length; i++)
        {
            Encoding.ASCII.GetBytes(names[i]).CopyTo(module, at);
            var slot = CourseBackgrounds.NamesAt + (i * 4) - data;
            for (; slot - place > ushort.MaxValue; place += ushort.MaxValue)
            {
                relocations.Add((ushort.MaxValue, 201, 0, 0));    // a gap: no-ops move the place on
            }
            relocations.Add(((ushort)(slot - place), 1, 1, (uint)(at - data)));
            (place, at) = (slot, at + names[i].Length + 1);
        }
        relocations.Add((0, 203, 0, 0));
        var imports = 0x60;
        var list = 0x100;
        Word(0x28, (uint)imports);
        Word(0x2C, 8);
        Word(imports, GameFacts.Rel.MainRelId);
        Word(imports + 4, (uint)list);
        foreach (var (step, type, section, addend) in relocations)
        {
            BinaryPrimitives.WriteUInt16BigEndian(module.AsSpan(list), step);
            (module[list + 2], module[list + 3]) = (type, section);
            Word(list + 4, addend);
            list += 8;
        }
        return module;
    }

    [Fact]
    public void TheBackgroundOfACourseIsItsVenuesNameInTheModule()
    {
        var names = Enumerable.Range(1, CourseBackgrounds.Venues).Select(v => $"bg_v{v}").ToArray();
        var venues = new byte[CourseBackgrounds.Courses];
        (venues[1], venues[24], venues[40]) = (1, 16, 5);
        var table = CourseBackgrounds.Read(Module(names, venues))!;
        Assert.Equal(new Dictionary<int, string> { [1] = "bg_v1", [24] = "bg_v16", [40] = "bg_v5" }, table);
        // a name that is no background's is none; no relocations, no table
        names[15] = "../x";
        Assert.False(CourseBackgrounds.Read(Module(names, venues))!.ContainsKey(24));
        Assert.Null(CourseBackgrounds.Read(Module([], venues)));
        Assert.Null(CourseBackgrounds.Read(new byte[64]));
    }

    [Fact]
    public void TheDiscsGamePicksEachCoursesBackground()
    {
        var gameFiles = Environment.GetEnvironmentVariable("MODS_DISC");
        Assert.SkipWhen(gameFiles is null || !Directory.Exists(gameFiles), "no disc on this computer: set MODS_DISC to its files folder");
        var table = CourseBackgrounds.OfDisc(gameFiles!)!;
        foreach (var (course, name) in GXtremesBackgrounds)
        {
            Assert.Equal(name, table[course]);
        }
        Assert.Equal("bg_big_s", table[40]);
        Assert.Equal("bg_win_gx", table[50]);
    }
}
