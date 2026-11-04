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
    public void ARowSaysWhatIsChosen()
    {
        var told = new List<Rgb?>();
        var row = new Launcher.App.ViewModels.TrailRow("Boost", null, told.Add);
        Assert.True(row.Swatches[0].Selected && row.Swatches[0].Own);
        row.PickCommand.Execute(row.Swatches.First(s => s.Label == "Green"));
        Assert.Equal(new Rgb(0x28, 0xFF, 0x3C), told[^1]);
        Assert.Equal("#28FF3C", row.Hex);
        Assert.Equal("Green", row.Swatches.Single(s => s.Selected).Label);
        var count = told.Count;
        row.Hex = "nope";
        Assert.False(row.Valid);
        Assert.Equal(count, told.Count);          // a box that holds no colour changes nothing
        row.Hex = "#010203";
        Assert.True(row.Valid);
        Assert.Equal(new Rgb(1, 2, 3), told[^1]);
        Assert.DoesNotContain(row.Swatches, s => s.Selected);
        row.PickCommand.Execute(row.Swatches[0]);
        Assert.Null(told[^1]);
        Assert.Equal("", row.Hex);
        Assert.Equal("Its own", row.Swatches[0].Label);
        // the sparks' unset swatch says what an unset one follows (the game's reader: the boost flame's colour)
        var sparks = new Launcher.App.ViewModels.TrailRow("Boost sparks", null, told.Add, "Same as the boost flame");
        Assert.True(sparks.Swatches[0].Own && sparks.Swatches[0].Selected);
        Assert.Equal("Same as the boost flame", sparks.Swatches[0].Label);
    }

    [Fact]
    public void TwoProfilesDifferByTheirTrail()
    {
        var a = RacerProfile.Default;
        Assert.NotEqual(a, a with { Trail = new BoostTrail(new Rgb(1, 2, 3)) });
        Assert.Equal(a with { Trail = new BoostTrail(new Rgb(1, 2, 3)) }, a with { Trail = new BoostTrail(new Rgb(1, 2, 3)) });
    }
}
