using System.Reflection;
using Launcher.App;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>No program or library of the launcher names a person: each one's company is the product's name, as players
/// see it (<see cref="Branding.DisplayName"/>). <c>net/Directory.Build.props</c> gives it to every project; a player
/// copy is searched for anybody's name before it is made (<c>port/build/package_identities.py</c>).</summary>
public sealed class AssemblyNamesTests
{
    [Fact]
    public void EveryAssemblyOfTheLauncherSaysTheProductIsItsCompany()
    {
        foreach (var assembly in new[] { typeof(Branding).Assembly, typeof(Install).Assembly, typeof(CoursePool).Assembly })
        {
            Assert.Equal(Branding.DisplayName, assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
            Assert.Null(assembly.GetCustomAttribute<AssemblyCopyrightAttribute>());       // nobody's name there either
        }
    }
}
