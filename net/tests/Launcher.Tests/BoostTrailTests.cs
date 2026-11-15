using System.Text.Json;
using Launcher.Core;
using Online.Protocol;
using Xunit;

namespace Launcher.Tests;

public class BoostTrailTests
{
    [Fact]
    public void ADarkColourIsLiftedKeepingItsHue()
    {
        var least = (byte)GameFacts.Limits.TrailLeastBrightness;
        Assert.Equal(new Rgb(least, least, least), BoostTrail.Lifted(new Rgb(0, 0, 0)));
        Assert.Equal(new Rgb(least, (byte)(least / 2), 0), BoostTrail.Lifted(new Rgb(48, 24, 0)));
        Assert.Equal(new Rgb(least, 1, 2), BoostTrail.Lifted(new Rgb(least, 1, 2)));
    }

    [Fact]
    public void ASeatTravelsAsTheGameReadsItAndNoneIsNoKey()
    {
        var seat = new SeatSetup(0, "p", "A", 1, null, Trail: new BoostTrail(new Rgb(1, 2, 3)));
        var json = JsonSerializer.Serialize(seat, ControlProtocol.Json);
        Assert.Contains("\"trail\":{\"normal\":{\"r\":1,\"g\":2,\"b\":3}", json);
        Assert.DoesNotContain("trail", JsonSerializer.Serialize(seat with { Trail = null }, ControlProtocol.Json));
        Assert.DoesNotContain("isStock", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PracticeCarriesTheRacersTrail()
    {
        var at = DateTimeOffset.UnixEpoch;
        var trail = new BoostTrail(Boost: new Rgb(9, 8, 7));
        Assert.Contains("\"trail\"", PracticeMatch.Build(PracticeSetup.Default, "A", null, at, trail: trail));
        Assert.DoesNotContain("\"trail\"", PracticeMatch.Build(PracticeSetup.Default, "A", null, at));
        Assert.DoesNotContain("\"trail\"", PracticeMatch.Build(PracticeSetup.Default, "A", null, at, trail: new BoostTrail()));
        Assert.Contains("\"sparks\"", PracticeMatch.Build(PracticeSetup.Default, "A", null, at, trail: new BoostTrail(Sparks: new Rgb(1, 2, 3))));
    }

    [Fact]
    public void OneColourIsAllThreeWordsAndThreeKeptReadAsOne()
    {
        var red = new Rgb(255, 0, 0);
        Assert.Equal(new BoostTrail(red, red, red), TrailColour.Trail(red));
        Assert.Null(TrailColour.Trail(null));
        // an earlier launcher's three: the boost flame's, else the flame's at rest (else the sparks')
        var (blue, green) = (new Rgb(0, 0, 255), new Rgb(0, 255, 0));
        Assert.Equal(blue, TrailColour.Of(new BoostTrail(green, blue, red)));
        Assert.Equal(green, TrailColour.Of(new BoostTrail(green, null, red)));
        Assert.Equal(red, TrailColour.Of(new BoostTrail(Sparks: red)));
        Assert.Null(TrailColour.Of(new BoostTrail()));
        Assert.Null(TrailColour.Migrated(null));
        Assert.Equal(TrailColour.Trail(blue), TrailColour.Migrated(new BoostTrail(green, blue)));
    }

    [Fact]
    public void ThreeColoursSavedLoadAsOneWithoutARepairNotice()
    {
        var folder = Directory.CreateTempSubdirectory("trail-one-");
        try
        {
            var path = Path.Combine(folder.FullName, "settings.json");
            var store = new SettingsStore(path);
            var (green, blue) = (new Rgb(0, 255, 0), new Rgb(0, 0, 255));
            store.Save(new LauncherSettings { Racer = RacerProfile.Default with { Trail = new BoostTrail(green, blue) } });
            var back = new SettingsStore(path);
            Assert.Equal(TrailColour.Trail(blue), back.Load().Racer.Trail);
            Assert.Equal(SettingsRead.Fine, back.LastRead);
            store.Save(new LauncherSettings { Racer = RacerProfile.Default with { Trail = new BoostTrail(Normal: green) } });
            Assert.Equal(TrailColour.Trail(green), new SettingsStore(path).Load().Racer.Trail);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheMachinesOwnTrailColoursAreReadFromItsBurners()
    {
        // two burners: at rest (153,153,204) both, boosting (153,204,153) and (204,153,153)
        var fmi = new byte[0x40 + (2 * 0x38)];
        fmi[3] = 2;
        void Put(int at, float r, float g, float b)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(fmi.AsSpan(at), r);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(fmi.AsSpan(at + 4), g);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(fmi.AsSpan(at + 8), b);
        }
        Put(0x40 + 0x20, 153, 153, 204);
        Put(0x40 + 0x2C, 153, 204, 153);
        Put(0x78 + 0x20, 153, 153, 204);
        Put(0x78 + 0x2C, 203.6f, 153, 153);
        Assert.Equal([new Rgb(153, 153, 204), new Rgb(153, 204, 153), new Rgb(204, 153, 153)], BurnerColours.Read(fmi));
        Assert.Empty(BurnerColours.Read(null));
        Assert.Empty(BurnerColours.Read(fmi[..0x60]));   // a burner past the end
        // where each burner is, how large, its colours (Wild Goose's lower left burner: -0.213, 0.281, 2.363, size 8)
        Put(0x40 + 0x04, -0.213f, 0.281f, 2.363f);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(fmi.AsSpan(0x40 + 0x18), 8);
        var burners = BurnerColours.Burners(fmi);
        Assert.Equal(2, burners.Count);
        Assert.Equal(new Burner(new System.Numerics.Vector3(-0.213f, 0.281f, 2.363f), 8, new Rgb(153, 153, 204), new Rgb(153, 204, 153)), burners[0]);
        Assert.Equal(new Rgb(204, 153, 153), burners[1].Boosting);
        Assert.Empty(BurnerColours.Burners(null));
        Assert.Empty(BurnerColours.Burners(fmi[..0x60]));
        Put(0x40 + 0x20, float.NaN, 0, 0);
        Assert.Empty(BurnerColours.Read(fmi));
        Assert.Empty(BurnerColours.Burners(fmi));
    }

    /// <summary>The hub's showcase's engine flames (<see cref="EngineFlames"/>): from each burner a plume going back
    /// towards the tail, white-hot at the burner, narrowing and fading; longer and of the boosting colour as it
    /// boosts; flickering, the same at the same moment.</summary>
    [Fact]
    public void AnEnginesFlameGoesBackFromItsBurner()
    {
        var burner = new Burner(new System.Numerics.Vector3(0.2f, 0.3f, 2.4f), 8, new Rgb(204, 153, 204), new Rgb(153, 204, 204));
        var flame = EngineFlames.Sprites([burner], 1.5f);
        Assert.Equal(EngineFlames.Along + 1, flame.Count);   // the halo and the plume
        var plume = flame.Skip(1).ToList();
        Assert.All(plume, s => Assert.True(s.At.Z > burner.At.Z && s.At.X == burner.At.X && s.At.Y == burner.At.Y));
        Assert.True(plume[0].Heat > 0.9f && plume[^1].Heat < 0.01f);
        Assert.True(plume[0].Radius > plume[^1].Radius && plume[0].Bright > plume[^1].Bright);
        Assert.Equal(1, plume[0].Colour.X, 3);   // the burner's colour at full strength: (204,153,204) lilac
        Assert.True(plume[0].Colour.Y < 0.7f);
        var boosting = EngineFlames.Sprites([burner], 1.5f, boost: 1).Skip(1).ToList();
        Assert.True(boosting[^1].At.Z > plume[^1].At.Z);
        Assert.Equal(1, boosting[0].Colour.Y, 3);   // (153,204,204): cyan
        Assert.Equal(flame, EngineFlames.Sprites([burner], 1.5f));
        Assert.NotEqual(flame, EngineFlames.Sprites([burner], 1.6f));
        Assert.Empty(EngineFlames.Sprites([burner with { Size = 0 }], 1));
    }

    [Fact]
    public void ThePickerSaysWhatIsChosen()
    {
        var told = new List<Rgb?>();
        var picker = new Launcher.App.ViewModels.TrailPicker(null, told.Add);
        // the disc not read: its own (grey) and nothing else
        Assert.Equal(["Its own"], picker.Swatches.Select(s => s.Label));
        Assert.True(picker.Swatches[0].Selected);
        var (rest, boost) = (new Rgb(153, 153, 204), new Rgb(153, 204, 153));
        _ = picker.ShowAsync(Task.FromResult<IReadOnlyList<Rgb>?>([rest, boost]));
        Assert.Equal(["Its own", "2"], picker.Swatches.Select(s => s.Label));
        Assert.Equal("#9999CC", picker.Swatches[0].Hex);
        Assert.Equal("#9999CC", picker.ColorHex);   // the wheel shows the game's own at rest
        Assert.Empty(told);                          // and showing it chose nothing
        picker.PickSwatchCommand.Execute(1);
        Assert.Equal(boost, told[^1]);
        Assert.Equal("2", picker.Swatches.Single(s => s.Selected).Label);
        var count = told.Count;
        picker.ColorHex = "nope";
        Assert.False(picker.ColorValid);
        Assert.Equal(count, told.Count);             // a box that holds no colour changes nothing
        picker.ColorHex = "#010203";
        Assert.Equal(new Rgb(1, 2, 3), told[^1]);
        Assert.Equal("Yours", picker.Swatches.Single(s => s.Selected).Label);
        Assert.Equal(new Rgb(1, 2, 3), picker.Colour);
        picker.PickSwatchCommand.Execute(0);
        Assert.Null(told[^1]);
        Assert.Null(picker.Colour);
        Assert.Equal(["Its own", "2", "Yours"], picker.Swatches.Select(s => s.Label));   // the player's stays to pick again
        picker.PickSwatchCommand.Execute(-1);
        Assert.Equal(new Rgb(1, 2, 3), told[^1]);
    }

    [Fact]
    public void TwoProfilesDifferByTheirTrail()
    {
        var a = RacerProfile.Default;
        Assert.NotEqual(a, a with { Trail = new BoostTrail(new Rgb(1, 2, 3)) });
        Assert.Equal(a with { Trail = new BoostTrail(new Rgb(1, 2, 3)) }, a with { Trail = new BoostTrail(new Rgb(1, 2, 3)) });
    }
}
