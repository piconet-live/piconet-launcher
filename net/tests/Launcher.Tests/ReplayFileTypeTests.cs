using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The replay's file type (<see cref="ReplayFileType"/>): what is registered for a launcher, which launcher
/// owns it (an installed one whenever it is there; else the portable launcher or player copy started last; a
/// development copy never), written only when it differs, and taken away only by its owner. On a store in memory:
/// no test writes the registry of the PC it runs on.</summary>
public sealed class ReplayFileTypeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The player's file types, in memory: keys (case does not count, as in the registry) and their values.</summary>
    private sealed class Classes : IClassesStore
    {
        public Dictionary<string, Dictionary<string, string>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Writes { get; private set; }

        public string? Read(string key, string name) => Keys.TryGetValue(key, out var k) && k.TryGetValue(name, out var v) ? v : null;

        public void Write(string key, string name, string value)
        {
            Writes++;
            for (var at = key; at.Length > 0; at = at.Contains('\\', StringComparison.Ordinal) ? at[..at.LastIndexOf('\\')] : "")
            {
                Keys.TryAdd(at, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }
            Keys[key][name] = value;
        }

        public void DeleteValue(string key, string name)
        {
            Writes++;
            if (Keys.TryGetValue(key, out var k))
            {
                k.Remove(name);
            }
        }

        public void DeleteTree(string key)
        {
            Writes++;
            foreach (var k in Keys.Keys.Where(k => k.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                                                    k.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                Keys.Remove(k);
            }
        }

        public void DeleteIfEmpty(string key)
        {
            Writes++;
            if (Keys.TryGetValue(key, out var k) && k.Count == 0 &&
                !Keys.Keys.Any(other => other.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                Keys.Remove(key);
            }
        }
    }

    /// <summary>An installed launcher's folder (the marker beside its libraries) and its program.</summary>
    private (Install Install, string Program) Installed()
    {
        var folder = Path.Combine(_dir, "Piconet.Launcher", "current");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Install.InstalledMarkerName), "installed");
        var program = Path.Combine(folder, "Piconet Launcher.exe");
        File.WriteAllText(program, "");
        return (Install.Of(folder), program);
    }

    /// <summary>A player copy as the package script lays it out, and the program at its top.</summary>
    private (Install Install, string Program) PlayerCopy(string name)
    {
        var copy = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(copy, Install.ProgramFolder));
        File.WriteAllText(Path.Combine(copy, Install.ProgramFolder, Install.MarkerName), "a player copy");
        var program = Path.Combine(copy, "Piconet Launcher.exe");
        File.WriteAllText(program, "");
        return (Install.Of(Path.Combine(copy, Install.ProgramFolder)), program);
    }

    /// <summary>Velopack's portable zip: the installed launcher's files in <c>current</c>, its <c>.portable</c> beside.</summary>
    private (Install Install, string Program) VelopackPortable()
    {
        var top = Path.Combine(_dir, "PiconetPortable");
        var current = Path.Combine(top, "current");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(current, Install.InstalledMarkerName), "installed");
        File.WriteAllText(Path.Combine(top, Install.VelopackPortableName), "");
        var program = Path.Combine(current, "Piconet Launcher.exe");
        File.WriteAllText(program, "");
        return (Install.Of(current), program);
    }

    private const string Name = "Piconet replay";

    [Fact]
    public void TheTypeIsTheLaunchersProgramWithItsIconAndTheFileAsItsArgument()
    {
        var (install, program) = Installed();
        var classes = new Classes();

        Assert.True(ReplayFileType.Claim(classes, install, program, Name));

        Assert.Equal("Piconet.Replay", classes.Read(".fzrp", ""));
        Assert.Equal("", classes.Read(@".fzrp\OpenWithProgids", "Piconet.Replay"));
        Assert.Equal("Piconet replay", classes.Read("Piconet.Replay", ""));
        Assert.Equal($"\"{program}\",0", classes.Read(@"Piconet.Replay\DefaultIcon", ""));
        Assert.Equal($"\"{program}\" \"%1\"", classes.Read(@"Piconet.Replay\shell\open\command", ""));
        Assert.Equal(program, ReplayFileType.Owner(classes));
        // exactly these values, and no other key
        Assert.Equal(5, ReplayFileType.Values(program, Name).Count);
        Assert.Equal([".fzrp", @".fzrp\OpenWithProgids", "Piconet.Replay", @"Piconet.Replay\DefaultIcon", @"Piconet.Replay\shell",
                      @"Piconet.Replay\shell\open", @"Piconet.Replay\shell\open\command"], classes.Keys.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>A registration as it is already is not written again (and the shell is not told: false).</summary>
    [Fact]
    public void ARegistrationAsItIsIsNotWrittenAgain()
    {
        var (install, program) = PlayerCopy("Copy");
        var classes = new Classes();
        Assert.True(ReplayFileType.Claim(classes, install, program, Name));
        var writes = classes.Writes;

        Assert.False(ReplayFileType.Claim(classes, install, program, Name));
        Assert.Equal(writes, classes.Writes);

        // one value changed by hand: that one alone is written back
        classes.Write(@"Piconet.Replay\DefaultIcon", "", "elsewhere.ico");
        writes = classes.Writes;
        Assert.True(ReplayFileType.Claim(classes, install, program, Name));
        Assert.Equal(writes + 1, classes.Writes);
    }

    /// <summary>A portable launcher or player copy takes the type when nobody has it, when its owner's program has
    /// gone, and from another portable copy (the copy started last owns it).</summary>
    [Fact]
    public void ThePortableCopyStartedLastTakesTheType()
    {
        var (first, firstProgram) = PlayerCopy("First copy");
        var (second, secondProgram) = VelopackPortable();
        var classes = new Classes();

        Assert.True(ReplayFileType.Claim(classes, first, firstProgram, Name));             // nobody had it
        Assert.Equal(firstProgram, ReplayFileType.Owner(classes));
        Assert.True(ReplayFileType.Claim(classes, second, secondProgram, Name));           // another portable copy's
        Assert.Equal(secondProgram, ReplayFileType.Owner(classes));
        Assert.True(ReplayFileType.Claim(classes, first, firstProgram, Name));             // and back
        Assert.Equal(firstProgram, ReplayFileType.Owner(classes));

        // the owner moved away (its program is not there): the next copy takes it
        File.Delete(firstProgram);
        Assert.True(ReplayFileType.Claim(classes, second, secondProgram, Name));
        Assert.Equal(secondProgram, ReplayFileType.Owner(classes));
    }

    /// <summary>An installed launcher that is there keeps the type: a portable copy does not take it from it; the
    /// installed launcher takes it from a portable copy; once the installed launcher has gone, a copy takes it.</summary>
    [Fact]
    public void AnInstalledLauncherWins()
    {
        var (installed, installedProgram) = Installed();
        var (copy, copyProgram) = PlayerCopy("Copy");
        var classes = new Classes();

        Assert.True(ReplayFileType.Claim(classes, copy, copyProgram, Name));
        Assert.True(ReplayFileType.Claim(classes, installed, installedProgram, Name));     // from the copy
        Assert.Equal(installedProgram, ReplayFileType.Owner(classes));
        var writes = classes.Writes;
        Assert.False(ReplayFileType.Claim(classes, copy, copyProgram, Name));              // not from the installed one
        Assert.Equal(writes, classes.Writes);
        Assert.Equal(installedProgram, ReplayFileType.Owner(classes));

        // the decision alone, by the owner's kind
        Assert.False(ReplayFileType.Takes(copy, copyProgram, installedProgram, _ => true));
        Assert.True(ReplayFileType.Takes(copy, copyProgram, installedProgram, _ => false));
        Assert.True(ReplayFileType.Takes(installed, installedProgram, copyProgram, _ => false));
        Assert.True(ReplayFileType.Takes(copy, copyProgram, null, _ => true));

        // its folder gone without its uninstall: the copy takes the type
        Directory.Delete(Path.GetDirectoryName(installedProgram)!, recursive: true);
        Assert.True(ReplayFileType.Claim(classes, copy, copyProgram, Name));
        Assert.Equal(copyProgram, ReplayFileType.Owner(classes));
    }

    /// <summary>Velopack's portable zip has the installed marker too, and is still a portable copy: it neither wins
    /// over a player copy nor keeps the type from one.</summary>
    [Fact]
    public void VelopacksPortableZipIsNoInstalledLauncher()
    {
        var (portable, portableProgram) = VelopackPortable();
        Assert.False(ReplayFileType.IsInstalledLauncher(portableProgram));
        Assert.True(ReplayFileType.IsInstalledLauncher(Installed().Program));
        Assert.False(ReplayFileType.IsInstalledLauncher(PlayerCopy("Copy").Program));
        Assert.False(ReplayFileType.IsInstalledLauncher(Path.Combine(_dir, "none.exe")));
        Assert.True(portable.Portable);
    }

    /// <summary>A development copy (no marker) never registers the type.</summary>
    [Fact]
    public void ADevelopmentCopyNeverTakesTheType()
    {
        var dev = Install.Of(_dir);
        var program = Path.Combine(_dir, "Launcher.App.exe");
        var classes = new Classes();

        Assert.False(ReplayFileType.Claim(classes, dev, program, Name));
        Assert.Empty(classes.Keys);
        Assert.False(ReplayFileType.Takes(dev, program, null, _ => false));
    }

    /// <summary>The uninstall takes away its own registration, and the extension's link to it, and leaves the keys of
    /// anything else; another launcher's registration it leaves whole.</summary>
    [Fact]
    public void TheUninstallTakesAwayOnlyItsOwnRegistration()
    {
        var (installed, installedProgram) = Installed();
        var (copy, copyProgram) = PlayerCopy("Copy");
        var classes = new Classes();

        Assert.True(ReplayFileType.Claim(classes, copy, copyProgram, Name));
        Assert.False(ReplayFileType.Release(classes, installedProgram));                  // the copy's: left
        Assert.Equal(copyProgram, ReplayFileType.Owner(classes));

        Assert.True(ReplayFileType.Claim(classes, installed, installedProgram, Name));
        classes.Write(@".fzrp\OpenWithProgids", "Other.Program", "");                        // another program's, kept
        Assert.True(ReplayFileType.Release(classes, installedProgram.ToUpperInvariant()));  // (case does not count)
        Assert.Null(ReplayFileType.Owner(classes));
        Assert.Null(classes.Read(".fzrp", ""));
        Assert.DoesNotContain("Piconet.Replay", classes.Keys.Keys);
        Assert.Equal("", classes.Read(@".fzrp\OpenWithProgids", "Other.Program"));

        classes.DeleteValue(@".fzrp\OpenWithProgids", "Other.Program");
        Assert.True(ReplayFileType.Claim(classes, installed, installedProgram, Name));
        Assert.True(ReplayFileType.Release(classes, installedProgram));
        Assert.Empty(classes.Keys);                                                          // nothing left behind
        Assert.False(ReplayFileType.Release(classes, installedProgram));                     // nothing to take away
    }

    /// <summary>The owner read from the open command, quoted or not; a command that names nothing is no owner.</summary>
    [Theory]
    [InlineData("\"C:\\Piconet Copy\\Piconet Launcher.exe\" \"%1\"", "C:\\Piconet Copy\\Piconet Launcher.exe")]
    [InlineData("C:\\Copy\\launcher.exe \"%1\"", "C:\\Copy\\launcher.exe")]
    [InlineData("C:\\Copy\\launcher.exe", "C:\\Copy\\launcher.exe")]
    [InlineData("\"\" \"%1\"", null)]
    [InlineData("\"C:\\unclosed", null)]
    [InlineData("  ", null)]
    public void TheOwnerIsTheProgramTheOpenCommandNames(string command, string? owner)
    {
        var classes = new Classes();
        classes.Write(ReplayFileType.CommandKey, "", command);
        Assert.Equal(owner, ReplayFileType.Owner(classes));
    }
}
