using System.Numerics;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// A course's models (<see cref="ModModels"/>), on made-up model files (GarageFiles.Gma: one model, two meshes of a
/// triangle each): with the disc's shape they pass for the race whatever their textures, a moved vertex makes them
/// single player. Textures, backgrounds, machines and every other file are the class's own check's.
/// </summary>
public sealed class ModModelsTests
{
    private static readonly byte[] Stock = GarageFiles.Gma(0, 1);

    private static byte[] Scaled(float by) => GarageFiles.Gma(0, 1, place: (_, at) => at * by);

    [Fact]
    public void ACoursesModelsWithTheDiscsShapePassWhateverTheirTextures()
    {
        Assert.Equal(ModVerdict.Anywhere, ModModels.Check("stage/st24.gma.lz", GarageFiles.Gma(5, 6), Stock));
        // a re-skin changes the textures' file only: no model check
        Assert.Null(ModModels.Check("stage/st24.tpl.lz", [1, 2, 3], [4, 5, 6]));
        Assert.Null(ModModels.Check("bg/bg_ele.gma.lz", Scaled(2), Stock));
        Assert.Null(ModModels.Check("stage/selst24.gma.lz", Scaled(2), Stock));
        Assert.Null(ModModels.Check("vehicle/bbull/bbull_emb.gma", Scaled(1.5f), Stock));
    }

    [Fact]
    public void AMovedVertexInACoursesModelIsSinglePlayer()
    {
        var moved = GarageFiles.Gma(0, 1, place: (v, at) => v == 2 ? at + new Vector3(0, 0, 0.01f) : at);
        Assert.Equal(new ModVerdict(false, ModModels.TrackWhy), ModModels.Check("stage/st24.gma.lz", moved, Stock));
        // within the tolerance (float rounding): the same shape
        var rounded = GarageFiles.Gma(0, 1, place: (v, at) => v == 2 ? at + new Vector3(0, 0, 1e-6f) : at);
        Assert.True(ModModels.Check("stage/st24.gma.lz", rounded, Stock)!.Online);
        Assert.False(ModModels.Check("stage/st24.gma.lz", [1, 2, 3], Stock)!.Online);
        // a course the disc has no models of (a new slot) is the course file's to answer
        Assert.True(ModModels.Check("stage/st60.gma.lz", moved, null)!.Online);
    }
}
