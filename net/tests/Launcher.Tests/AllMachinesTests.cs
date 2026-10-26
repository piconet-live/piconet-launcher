using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The game's 41 machines (<see cref="GameMachines"/>): each has its name and its files, and one beyond the
/// Grand Prix's thirty goes from the settings through a server in this process to the file the game is given.</summary>
public sealed class AllMachinesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The backdrop behind the pages is a venue of a course the disc has a picture for. The server's pool
    /// holds four of the story's stages (39, 43, 44, 45), which have none: the backdrop is drawn
    /// from the others, all 26 of them, and never from those.</summary>
    [Fact]
    public void TheBackdropIsDrawnFromTheCoursesThatHaveAVenuesPicture()
    {
        var backdrops = Launcher.App.Theme.GameSkin.BackdropCourses;
        Assert.Equal(26, backdrops.Count);
        Assert.All(backdrops, course => Assert.True(DiscArt.HasVenue(course)));
        Assert.Equal([39, 43, 44, 45], Courses.Pool.Except(backdrops));
        Assert.Equal(30, Courses.Pool.Count);
    }

    /// <summary>The launcher names every machine the game has, and knows each one's files on the disc: the ids are
    /// the game's, so a list that came apart would name one machine and race another.</summary>
    [Fact]
    public void EveryMachineOfTheGameHasANameAndItsFiles()
    {
        Assert.Equal(GameMachines.Count, Machines.Names.Count);
        Assert.Equal(GameMachines.Count, DiscArt.MachineFiles.Count);
        Assert.Equal(GameMachines.Count, Machines.Names.Distinct().Count());
        Assert.Equal(GameMachines.Count, DiscArt.MachineFiles.Distinct().Count());
        Assert.Equal(("Blue Falcon", "bfalcon"), (Machines.Names[6], DiscArt.MachineFiles[6]));
        Assert.Equal(("Black Bull", "bbull"), (Machines.Names[29], DiscArt.MachineFiles[29]));
        Assert.Equal(("Dark Schneider", "darks"), (Machines.Names[30], DiscArt.MachineFiles[30]));
        Assert.Equal(("Rainbow Phoenix", "rainp"), (Machines.Names[40], DiscArt.MachineFiles[40]));
        Assert.True(Machines.Has(0) && Machines.Has(40));
        Assert.False(Machines.Has(41) || Machines.Has(-1));
    }
}
