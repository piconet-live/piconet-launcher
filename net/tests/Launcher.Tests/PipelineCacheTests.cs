using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The graphics cache's stamp and the mods (<see cref="PipelineCache"/>): only a mod's files that can add
/// pipelines (models, course files) are in it; a texture's change, or a mod switched off, needs no build; a model added
/// does, and the stamp a build leaves keeps what was built before it.</summary>
public sealed class PipelineCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pipeline-cache-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A model file as the runner's test knows one: an LZ file, its first word its packed size less 8.</summary>
    private OverlayFile Model(string name, string disc, int size = 64)
    {
        var bytes = new byte[size];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), (uint)(bytes.Length - 8));
        bytes[8] = (byte)name.Length;
        var from = Path.Combine(_dir, name);
        File.WriteAllBytes(from, bytes);
        return new OverlayFile(disc, from);
    }

    /// <summary>A texture (a TPL's header: nothing the planner reads).</summary>
    private OverlayFile Texture(string name, string disc, byte fill = 1)
    {
        var bytes = Enumerable.Repeat(fill, 64).ToArray();
        bytes[0] = 0x00; bytes[1] = 0x20; bytes[2] = 0xAF; bytes[3] = 0x30;   // TPL's magic
        var from = Path.Combine(_dir, name);
        File.WriteAllBytes(from, bytes);
        return new OverlayFile(disc, from);
    }

    private RecompPaths Paths(params OverlayFile[] files) =>
        new(Path.Combine(_dir, "runner.exe"), Path.Combine(_dir, "m.dll"), Path.Combine(_dir, "game"), Path.Combine(_dir, "user"),
            Mods: new ModOverlay(files, files));

    private static PipelineStamp Want(RecompPaths paths) => PipelineCache.Want(paths, PipelineCache.Plain, "drivers 1");

    [Fact]
    public void OnlyModelsAndCourseFilesCanAddPipelines()
    {
        Assert.True(PipelineCache.AddsPipelines(Model("car.arc", "vehicle/bfalcon_e.arc.lz")));
        Assert.False(PipelineCache.AddsPipelines(Texture("paint.tpl", "vehicle/bfalcon_e.tpl")));
        // a course file goes by its name (its fog type), whatever its bytes
        Assert.True(PipelineCache.AddsPipelines(Texture("course", "stage/COLI_COURSE01.lz")));
        Assert.False(PipelineCache.AddsPipelines(new OverlayFile("stage/st01.gma", Path.Combine(_dir, "gone"))));
        // the runner's test, as it is there: a U8 archive, and a model file's table
        Assert.True(PipelineCache.MightHoldModels([0x55, 0xAA, 0x38, 0x2D, 0, 0, 0, 0], 100));
        Assert.True(PipelineCache.MightHoldModels([0, 0, 0, 2, 0, 0, 0, 0x20], 100));
        Assert.False(PipelineCache.MightHoldModels([0, 0, 0, 2, 0, 0, 0, 0x21], 100));
    }

    [Fact]
    public void ATextureChangeOrAModSwitchedOffNeedsNoBuildAndAModelAddedDoes()
    {
        var car = Model("car.arc", "vehicle/bfalcon_e.arc.lz");
        var paint = Texture("paint.tpl", "vehicle/bfalcon_e.tpl");
        var built = Paths(car, paint);
        PipelineCache.Mark(built, Want(built));
        Assert.Null(PipelineCache.Why(built, Want(built)));

        // the texture changed, or switched off: the same models
        Texture("paint.tpl", "vehicle/bfalcon_e.tpl", fill: 2);
        Assert.Null(PipelineCache.Why(built, Want(built)));
        Assert.Null(PipelineCache.Why(Paths(car), Want(Paths(car))));
        // every mod off: fewer models than built
        Assert.Null(PipelineCache.Why(Paths(), Want(Paths())));

        // another model: a build, and its stamp holds the first one too
        var other = Model("other.arc", "vehicle/wgoose_e.arc.lz");
        Assert.Equal(PipelineWhy.Mods, PipelineCache.Why(Paths(other), Want(Paths(other))));
        PipelineCache.Mark(Paths(other), Want(Paths(other)));
        Assert.Null(PipelineCache.Why(Paths(car, other), Want(Paths(car, other))));

        // a model whose file changed is another model
        car = Model("car.arc", "vehicle/bfalcon_e.arc.lz", size: 72);
        Assert.Equal(PipelineWhy.Mods, PipelineCache.Why(Paths(car), Want(Paths(car))));
    }

    [Fact]
    public void AStampFromBeforeTheListIsReadAndAnotherAuroraKeepsNothing()
    {
        var car = Model("car.arc", "vehicle/bfalcon_e.arc.lz");
        var paths = Paths(car);
        var want = Want(paths);
        // a stamp without the files' list: only the same set is ready
        PipelineCache.Mark(paths, want with { ModFiles = null });
        Assert.Null(PipelineCache.Why(paths, want));
        Assert.Equal(PipelineWhy.Mods, PipelineCache.Why(Paths(), Want(Paths())));

        // another Aurora: its build's stamp keeps no file of the old one's
        PipelineCache.Mark(paths, want);
        PipelineCache.Mark(Paths(), Want(Paths()) with { Aurora = "another" });
        Assert.Equal("", PipelineCache.Built(paths, PipelineCache.Plain)!.ModFiles);
        Assert.Equal(want, PipelineStamp.Parse(want.Text));
    }
}
