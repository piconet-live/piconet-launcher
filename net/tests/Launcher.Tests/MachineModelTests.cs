using System.Numerics;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.GarageFiles;

namespace Launcher.Tests;

/// <summary>The garage's preview on made-up files: the archive and the model.</summary>
public sealed class MachineModelTests
{
    [Fact]
    public void AnArchiveGivesItsFilesByName()
    {
        var archive = U8Archive.Parse(Archive(("m.gma", [1, 2, 3]), ("m.tpl", [9, 8])));
        Assert.Equal([1, 2, 3], archive.File("m.gma"));
        Assert.Equal([9, 8], archive.File("m.tpl"));
        Assert.Null(archive.File("other.tpl"));
        Assert.Throws<InvalidDataException>(() => U8Archive.Parse(new byte[64]));
    }

    [Fact]
    public void AnObjectGoesWhereTheAttachmentFileSays()
    {
        // Fire Stingray's fin and Rainbow Phoenix's wings are stored around their hinges: left at the file's own
        // coordinates the fin sank into the body and the wings crossed under it
        var own = Tpl(Cmpr(8, 8, (_, _) => new Rgb(200, 0, 0)));
        var at = new Vector3(1.5f, 0.25f, -2);
        var plain = MachineModel.Read(Gma(0, 0), own, null);
        var placed = MachineModel.Read(Gma(0, 0), own, null, Fmi(("other", Vector3.One), ("body", at)));
        Assert.Equal(plain.Meshes.Count, placed.Meshes.Count);
        Assert.Equal(plain.Meshes[0].Positions.Select(p => p + at), placed.Meshes[0].Positions);
        Assert.Equal(plain.Radius, placed.Radius, 3);
        Assert.Equal(plain.Center + at, placed.Center);
        // a name it does not hold stays where the model has it
        Assert.Equal(plain.Meshes[0].Positions, MachineModel.Read(Gma(0, 0), own, null, Fmi(("other", at))).Meshes[0].Positions);
    }

    [Fact]
    public void AnAttachmentFileThatDoesNotFitPlacesNothing()
    {
        Assert.Empty(MachineModel.Attachments(null));
        Assert.Empty(MachineModel.Attachments(new byte[16]));
        // the count says more records than there is room for: those there are still read
        var many = Fmi(("a", Vector3.One));
        many[2] = 255;
        Assert.Equal(Vector3.One, Assert.Single(MachineModel.Attachments(many)).Value);
        // a name's place outside the file, and a point that is no number
        var far = Fmi(("a", Vector3.One));
        far.AsSpan(0x204).Fill(0xFF);
        Assert.Empty(MachineModel.Attachments(far));
        Assert.Empty(MachineModel.Attachments(Fmi(("a", new Vector3(float.NaN, 0, 0)))));
    }

    [Fact]
    public void TheModelReadsItsMeshesColoursAndTextures()
    {
        // two meshes: the machine's own texture 0, and the shared number 51, the shared file's first texture
        var own = Tpl(Cmpr(8, 8, (_, _) => new Rgb(200, 0, 0)));
        var shared = Tpl(Cmpr(8, 8, (_, _) => new Rgb(0, 0, 200)));
        var model = MachineModel.Read(Gma(0, 51, MachineModel.CommonTexture), own, shared);
        Assert.Equal(2, model.Meshes.Count);
        Assert.Equal(1, model.OwnTextures);
        Assert.Equal(0, model.Meshes[0].Material.Texture);
        Assert.Equal(1, model.Meshes[1].Material.Texture);
        Assert.InRange(model.Textures[1]!.Picture.Rgba[2], 192, 208);
        // the colours from the meshes' headers
        Assert.Equal(new Vector3(0.8f, 0.6f, 0.4f), model.Meshes[0].Material.Diffuse);
        Assert.Equal(42, model.Meshes[0].Material.Shininess);
        Assert.Equal(1, model.Meshes[0].Normals[0].Length(), 3);
        // a number that is neither the machine's nor in the shared file: drawn without a texture
        Assert.Equal(-1, MachineModel.Read(Gma(0, 7), own, shared).Meshes[1].Material.Texture);
        Assert.Equal(-1, MachineModel.Read(Gma(0, 7, MachineModel.CommonTexture), own, shared).Meshes[1].Material.Texture);
    }

    [Fact]
    public void AMaterialSaysWhichFileItsTextureNumberIsFrom()
    {
        // number 0 is the machine's own first texture, or with the shared mark no texture of the shared list at all:
        // a Queen Meteor wore its own emblem where its number plate's digits (shared 5 and 3) belong
        var own = Tpl(Cmpr(8, 8, (_, _) => new Rgb(200, 0, 0)));
        var shared = Tpl(Cmpr(8, 8, (_, _) => new Rgb(0, 0, 200)));
        Assert.Equal(0, MachineModel.Read(Gma(0, 0), own, shared).Meshes[1].Material.Texture);
        Assert.Equal(-1, MachineModel.Read(Gma(0, 0, MachineModel.CommonTexture), own, shared).Meshes[1].Material.Texture);
        // a reflection is not put on by the mesh's coordinates: the studio's sky stands in for it
        Assert.Equal(-1, MachineModel.Read(Gma(0, 51, MachineModel.CommonTexture | MachineModel.ReflectedTexture), own, shared)
            .Meshes[1].Material.Texture);
    }

    [Fact]
    public void TheSharedNumbersAreTheGamesListNotTheFilesOrder()
    {
        // the digits in pairs, light then dark: the file has the light ones and four dark ones early, six dark ones late
        Assert.Equal([1, 2, 3, 47, 4, 5, 6, 7, 8, 42, 9, 43, 10, 44, 11, 45, 12, 46, 13, 14],
            Enumerable.Range(1, 20).Select(MachineModel.SharedSlot));
        // the highlight shapes, the reflected city, the grilles and vents, and the nine pictures from 76 (the game's
        // table, .rodata 0x3968: the custom parts use 76 to 84, and the stock machines 76, 78 and 80)
        int[] numbers = [31, 42, 51, 61, 64, 75, 76, 79, 80, 81, 84];
        Assert.Equal([30, 41, 0, 15, 18, 29, 52, 55, 56, 48, 51], numbers.Select(MachineModel.SharedSlot));
        // what the game makes as it runs, and what its table does not list, has no picture in the file
        Assert.All([0, 21, 30, 43, 50, 52, 60, 85], n => Assert.Equal(-1, MachineModel.SharedSlot(n)));
        // every slot of the file's 57 has exactly one number
        Assert.Equal(Enumerable.Range(0, 57), Enumerable.Range(0, 85).Select(MachineModel.SharedSlot).Where(s => s >= 0).Order());
    }

    [Fact]
    public void AMaterialSaysHowItsTextureGoesOn()
    {
        var own = Tpl(Cmpr(8, 8, (_, _) => new Rgb(200, 0, 0)));
        MachineMaterial Second(byte wrap) => MachineModel.Read(Gma(0, 0, wrap1: wrap), own, null).Meshes[1].Material;
        // neither bit: the edge; 4 and 0x10 repeat across and down; 8 and 0x20 mirror
        Assert.Equal((TextureWrap.Clamp, TextureWrap.Clamp), (Second(0x80).WrapS, Second(0x80).WrapT));
        Assert.Equal((TextureWrap.Repeat, TextureWrap.Clamp), (Second(0x04).WrapS, Second(0x04).WrapT));
        Assert.Equal((TextureWrap.Mirror, TextureWrap.Repeat), (Second(0x18).WrapS, Second(0x18).WrapT));
        Assert.Equal((TextureWrap.Clamp, TextureWrap.Mirror), (Second(0x20).WrapS, Second(0x20).WrapT));
    }
}
