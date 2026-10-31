using System.Text.Json.Nodes;
using Launcher.Core;
using Online.Protocol;
using Xunit;

namespace Launcher.Tests.Files;

/// <summary>Practice as a game mode: the file beside the program, its Restore and laps parameters, the match's rules.</summary>
public sealed class PracticeModeTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "quickplay");

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void TheMatchCarriesTheModeAndRestoreAsItsParameter(bool restore, int value)
    {
        var mode = PracticeMode.Start(Folder, restore, 3, out var problem);
        Assert.Null(problem);
        var setup = PracticeSetup.Default with { Restore = restore, Laps = 3 };
        var file = JsonNode.Parse(PracticeMatch.Build(setup, "P", null, DateTimeOffset.UnixEpoch, mode: mode))!;
        var set = file["match"]!["setup"]!;
        Assert.True((bool)file["practice"]!);
        Assert.Equal(3, (int)set["laps"]!);
        Assert.Equal(value, (int)set["mode"]!["params"]![0]!);
        Assert.Equal(3, (int)set["mode"]!["params"]![1]!);
        Assert.Equal(mode!.Hash, (string)set["mode"]!["hash"]!);
        Assert.False(set["rules"]?["restore"] is { } r && (bool)r);
    }

    [Fact]
    public void AMissingFileIsAProblemNotAPlainPractice()
    {
        Assert.Null(PracticeMode.Start(Path.Combine(Folder, "nowhere"), false, 0, out var problem));
        Assert.NotNull(problem);
    }
}
