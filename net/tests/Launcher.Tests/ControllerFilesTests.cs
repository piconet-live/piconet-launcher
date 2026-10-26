using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The game's controller settings on disk: read, written a port at a time, every line the page does not
/// know kept; and the first start's default.</summary>
public sealed class ControllerFilesTests : IDisposable
{
    private readonly string _user = Path.Combine(Path.GetTempPath(), "controller-files-" + Guid.NewGuid().ToString("N"));

    public ControllerFilesTests() => Directory.CreateDirectory(Path.Combine(_user, "Config"));

    public void Dispose() => Directory.Delete(_user, recursive: true);

    private static readonly ControllerDevice Xbox = new("SDL/0/Xbox One Controller", "SDL", 0, "Xbox One Controller", true, true);
    private static readonly ControllerDevice Keyboard = new("DInput/0/Keyboard Mouse", "DInput", 0, "Keyboard Mouse", false, false);
    private static readonly ControllerDevice Wheel = new("DInput/0/G27 Racing Wheel", "DInput", 0, "G27 Racing Wheel", false, true);

    [Fact]
    public void APortWithNoLineHasTheEmulatorsOwnKind()
    {
        var ports = ControllerFiles.Read(_user);
        Assert.Null(ports.Problem);
        Assert.Equal(4, ports.Ports.Count);
        Assert.Equal(PortKind.Controller, ports.Ports[0].Kind);   // the emulator's default for port 1
        Assert.All(ports.Ports.Skip(1), p => Assert.Equal(PortKind.Off, p.Kind));
        Assert.All(ports.Ports, p => Assert.Empty(p.Map));
    }

    [Fact]
    public void EachKindOfPortIsReadFromItsNumber()
    {
        string[] main = ["[Core]", "SIDevice0 = 12", "SIDevice1 = 6", "SIDevice2 = 0", "SIDevice3 = 13"];
        Assert.Equal(PortKind.Adapter, ControllerFiles.PortOf(main, [], 0).Kind);
        Assert.Equal(PortKind.Controller, ControllerFiles.PortOf(main, [], 1).Kind);
        Assert.Equal(PortKind.Off, ControllerFiles.PortOf(main, [], 2).Kind);
        var other = ControllerFiles.PortOf(main, [], 3);
        Assert.Equal(PortKind.Other, other.Kind);
        Assert.Equal(13, other.Number);   // a kind the page does not offer keeps its number
        // a number that is none is the emulator's default, not an error
        Assert.Equal(PortKind.Controller, ControllerFiles.PortOf(["[Core]", "SIDevice0 = many"], [], 0).Kind);
        Assert.Equal(PortKind.Off, ControllerFiles.PortOf(["[Core]", "SIDevice1 = -3"], [], 1).Kind);
    }

    [Fact]
    public void FourPortsAreWrittenAndReadBackAsTheyWere()
    {
        var gamepad = StandardLayouts.Applied(PortSettings.Off, Xbox) with { StickDeadZone = 15, CStickDeadZone = 10 };
        var keys = StandardLayouts.Applied(PortSettings.Off, Keyboard);
        var adapter = PortSettings.Off with { Kind = PortKind.Adapter };
        ControllerFiles.Write(_user, 0, gamepad);
        ControllerFiles.Write(_user, 1, keys);
        ControllerFiles.Write(_user, 2, adapter);
        ControllerFiles.Write(_user, 3, PortSettings.Off);
        var read = ControllerFiles.Read(_user).Ports;

        Assert.Equal([PortKind.Controller, PortKind.Controller, PortKind.Adapter, PortKind.Off], read.Select(p => p.Kind));
        Assert.Equal("SDL/0/Xbox One Controller", read[0].Device);
        Assert.Equal(gamepad.Map.OrderBy(p => p.Key), read[0].Map.OrderBy(p => p.Key));
        Assert.Equal((15.0, 10.0), (read[0].StickDeadZone, read[0].CStickDeadZone));
        Assert.Equal("DInput/0/Keyboard Mouse", read[1].Device);
        Assert.Equal(keys.Map.OrderBy(p => p.Key), read[1].Map.OrderBy(p => p.Key));
        Assert.Equal("Button A", read[0].Shown("Buttons/A"));
        Assert.Equal("RETURN", read[1].Shown("Buttons/Start"));
        Assert.Equal("`Motor L` | `Motor R`", read[0].Shown("Rumble/Motor"));   // more than one name: shown as written

        // the files as the game reads them
        var main = File.ReadAllLines(ControllerFiles.MainFile(_user));
        Assert.Equal(["6", "6", "12", "0"], Enumerable.Range(0, 4).Select(i => IniLines.Get(main, "Core", $"SIDevice{i}")));
        var pad = File.ReadAllLines(ControllerFiles.PadFile(_user));
        Assert.Equal("`Button A`", IniLines.Get(pad, "GCPad1", "Buttons/A"));
        Assert.Equal("15.0", IniLines.Get(pad, "GCPad1", "Main Stick/Dead Zone"));
        Assert.Equal("`UP`", IniLines.Get(pad, "GCPad2", "Main Stick/Up"));
        Assert.DoesNotContain("[GCPad3]", pad);   // a port that is no controller writes no mapping
        // written a second time, nothing changes
        ControllerFiles.Write(_user, 0, read[0]);
        Assert.Equal(pad, File.ReadAllLines(ControllerFiles.PadFile(_user)));
    }

    [Fact]
    public void LinesThePageDoesNotKnowAreKeptWhereTheyWere()
    {
        File.WriteAllLines(ControllerFiles.MainFile(_user),
            ["[Interface]", "ConfirmStop = False", "", "[Core]", "Overclock = 6.0", "SIDevice1 = 6", "AdapterRumble0 = False", "", "[DSP]", "Volume = 40"]);
        File.WriteAllLines(ControllerFiles.PadFile(_user),
        [
            "[GCPad1]", "Device = XInput/0/Gamepad", "Buttons/A = `Button A`", "Main Stick/Modifier/Range = 50.0",
            "Options/Always Connected = True", "Buttons/B = `Button B` & !`Back`", "Some Future Key = 7", "",
            "[GCPad2]", "Device = DInput/0/Keyboard Mouse", "Buttons/A = `SPACE`",
        ]);
        var port = ControllerFiles.Read(_user).Ports[0];
        Assert.Equal("XInput/0/Gamepad", port.Device);
        Assert.Equal("`Button B` & !`Back`", port.Shown("Buttons/B"));   // an expression is shown as it is written
        ControllerFiles.Write(_user, 0, port.With("Buttons/A", "`Button X`") with { StickDeadZone = 20 });
        ControllerFiles.Write(_user, 3, PortSettings.Off with { Kind = PortKind.Adapter });

        Assert.Equal(
        [
            "[GCPad1]", "Device = XInput/0/Gamepad", "Buttons/A = `Button X`", "Main Stick/Modifier/Range = 50.0",
            "Options/Always Connected = True", "Buttons/B = `Button B` & !`Back`", "Some Future Key = 7", "Main Stick/Dead Zone = 20.0", "",
            "[GCPad2]", "Device = DInput/0/Keyboard Mouse", "Buttons/A = `SPACE`",
        ], File.ReadAllLines(ControllerFiles.PadFile(_user)));
        Assert.Equal(
        [
            "[Interface]", "ConfirmStop = False", "", "[Core]", "Overclock = 6.0", "SIDevice1 = 6", "AdapterRumble0 = False",
            "SIDevice0 = 6", "SIDevice3 = 12", "", "[DSP]", "Volume = 40",
        ], File.ReadAllLines(ControllerFiles.MainFile(_user)));
    }

    [Fact]
    public void TheStandardLayoutReplacesTheMappingAndNothingElse()
    {
        File.WriteAllLines(ControllerFiles.PadFile(_user),
            ["[GCPad1]", "Device = DInput/0/Keyboard Mouse", "Buttons/A = `SPACE`", "Main Stick/Modifier = `Shift`", "Options/Always Connected = True"]);
        var port = ControllerFiles.Read(_user).Ports[0];
        ControllerFiles.Write(_user, 0, StandardLayouts.Applied(port, Xbox));
        var pad = File.ReadAllLines(ControllerFiles.PadFile(_user));
        Assert.Equal("SDL/0/Xbox One Controller", IniLines.Get(pad, "GCPad1", "Device"));
        Assert.Equal("`Button A`", IniLines.Get(pad, "GCPad1", "Buttons/A"));
        Assert.Null(IniLines.Get(pad, "GCPad1", "Main Stick/Modifier"));          // the keyboard's, gone with its layout
        Assert.Equal("True", IniLines.Get(pad, "GCPad1", "Options/Always Connected"));
        // a device with no standard layout keeps the mapping it had
        var wheel = StandardLayouts.Applied(ControllerFiles.Read(_user).Ports[0], Wheel);
        Assert.Equal("DInput/0/G27 Racing Wheel", wheel.Device);
        Assert.Equal("`Button A`", wheel.Map["Buttons/A"]);
        Assert.Null(StandardLayouts.For(Wheel));
    }

    [Fact]
    public void TheLayoutSentenceSaysWhatIsSetAndNothingElse()
    {
        // the standard layout in words only while the port's mapping is that layout
        var keys = StandardLayouts.Applied(PortSettings.Off, Keyboard);
        Assert.True(StandardLayouts.IsStandard(keys, Keyboard));
        Assert.Equal(ControllerWords.Standard, ControllerWords.Layout(keys, Keyboard));
        var pad = StandardLayouts.Applied(PortSettings.Off, Xbox);
        Assert.True(StandardLayouts.IsStandard(pad, Xbox));
        Assert.Equal("Standard", ControllerWords.Layout(pad, Xbox));

        // one control set to something else, one taken out, or a mapping that is mostly empty: the player's own
        const string own = "Your own layout";
        Assert.Equal(own, ControllerWords.Layout(keys.With("Buttons/A", "`F15`"), Keyboard));
        Assert.Equal(own, ControllerWords.Layout(pad.With("Triggers/L", "`Shoulder L`"), Xbox));
        var few = PortSettings.Off with { Kind = PortKind.Controller, Device = Keyboard.Id, Map = new Dictionary<string, string> { ["Buttons/A"] = "`F15`", ["Buttons/B"] = "`Z`" } };
        Assert.False(StandardLayouts.IsStandard(few, Keyboard));
        Assert.Equal(own, ControllerWords.Layout(few, Keyboard));
        Assert.Equal(own, ControllerWords.Layout(pad with { Map = pad.Map.Where(p => p.Key != "Rumble/Motor").ToDictionary() }, Xbox));
        // the keyboard's layout on a gamepad is not the gamepad's standard one
        Assert.Equal(own, ControllerWords.Layout(keys with { Device = Xbox.Id }, Xbox));
        // "Use the standard layout" makes it the standard one again; the dead zones are no part of a layout
        Assert.Equal("Standard", ControllerWords.Layout(StandardLayouts.Applied(few, Keyboard) with { StickDeadZone = 15 }, Keyboard));

        // the runner's own gamepad profile is the measure when it is there
        var profile = new Dictionary<string, string>(StandardLayouts.Gamepad) { ["Buttons/Z"] = "`Shoulder L`" };
        Assert.False(StandardLayouts.IsStandard(pad, Xbox, profile));
        Assert.True(StandardLayouts.IsStandard(StandardLayouts.Applied(pad, Xbox, profile), Xbox, profile));
        // a device with no standard layout, and no device
        Assert.Equal("No standard layout", ControllerWords.Layout(few, Wheel));
        Assert.Equal("", ControllerWords.Layout(few, null));
    }

    [Fact]
    public void TheRunnersOwnGamepadProfileIsTheStandardLayout()
    {
        var runner = Path.Combine(_user, "recomp", "moderngekko-run.exe");
        Assert.Null(ControllerFiles.GamepadProfile(runner));   // not there: the launcher's own copy of it is used
        var folder = Path.Combine(_user, "recomp", "Sys", "Profiles", "GCPad");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "SDL Gamepad.ini"),
            ["[Profile]", .. StandardLayouts.Gamepad.Select(p => $"{p.Key} = {p.Value}"), "Buttons/Z = `Shoulder L`"]);
        var profile = ControllerFiles.GamepadProfile(runner)!;
        // the profile's own lines, read as the game reads them (of a key written twice, the last line)
        var expected = new Dictionary<string, string>(StandardLayouts.Gamepad) { ["Buttons/Z"] = "`Shoulder L`" };
        Assert.Equal(expected.OrderBy(p => p.Key), profile.OrderBy(p => p.Key));
        Assert.Equal("`Shoulder L`", StandardLayouts.Applied(PortSettings.Off, Xbox, profile).Map["Buttons/Z"]);
    }

    [Fact]
    public void AFirstStartGetsTheFirstGamepadElseTheKeyboard()
    {
        // a gamepad among other devices: the first one SDL knows, with the standard layout
        var first = StandardLayouts.FirstStart([Keyboard, Wheel, new("XInput/0/Gamepad", "XInput", 0, "Gamepad", false, true), Xbox]);
        Assert.Equal((PortKind.Controller, "SDL/0/Xbox One Controller"), (first.Kind, first.Device));
        Assert.Equal("`Left X+`", first.Map["Main Stick/Right"]);
        // none: the keyboard, by the name the game gave it, else by its usual name
        Assert.Equal("DInput/0/Keyboard Mouse", StandardLayouts.FirstStart([Wheel, Keyboard]).Device);
        var keys = StandardLayouts.FirstStart([]);
        Assert.Equal("DInput/0/Keyboard Mouse", keys.Device);
        Assert.Equal(("`UP`", "`X`", "`Q`"), (keys.Map["Main Stick/Up"], keys.Map["Buttons/A"], keys.Map["Triggers/L"]));

        File.WriteAllLines(ControllerFiles.MainFile(_user), ["[Core]", "Overclock = 6.0"]);
        Assert.True(ControllerFiles.WriteFirstStart(_user, first));
        var read = ControllerFiles.Read(_user).Ports;
        Assert.Equal((PortKind.Controller, "SDL/0/Xbox One Controller"), (read[0].Kind, read[0].Device));
        Assert.Equal("6", IniLines.Get(File.ReadAllLines(ControllerFiles.MainFile(_user)), "Core", "SIDevice0"));
        Assert.All(read.Skip(1), p => Assert.Equal(PortKind.Off, p.Kind));
    }

    [Fact]
    public void AGameCubeControllerOnTheAdapterIsUsedThroughTheAdapter()
    {
        // with no port set to the adapter SDL shows its controllers as gamepads, their B and X by place, not by name
        var onAdapter = new ControllerDevice("SDL/0/Nintendo GameCube Controller", "SDL", 0, "Nintendo GameCube Controller", true, true);
        Assert.True(onAdapter.OnAdapter);
        Assert.False(onAdapter.Offered);                 // the page offers it as "GameCube adapter", not as a device
        Assert.False(Xbox.OnAdapter);
        var first = StandardLayouts.FirstStart([Keyboard, onAdapter, Xbox]);
        Assert.Equal(PortKind.Adapter, first.Kind);
        Assert.Empty(first.Map);
        Assert.True(ControllerFiles.WriteFirstStart(_user, first));
        Assert.Equal("12", IniLines.Get(File.ReadAllLines(ControllerFiles.MainFile(_user)), "Core", "SIDevice0"));
        Assert.True(ControllerFiles.HasSettings(_user));  // written once: the next start is no first start
        Assert.Equal(PortKind.Adapter, ControllerFiles.Read(_user).Ports[0].Kind);
        // another gamepad found before it is taken as before
        Assert.Equal("SDL/0/Xbox One Controller", StandardLayouts.FirstStart([Xbox, onAdapter]).Device);
    }

    [Fact]
    public void APlayerWhoHasSettingsKeepsThemUntouched()
    {
        File.WriteAllLines(ControllerFiles.MainFile(_user), ["[Core]", "SIDevice0 = 12"]);
        File.WriteAllLines(ControllerFiles.PadFile(_user), ["[GCPad1]", "Device = XInput/0/Gamepad"]);
        var (main, pad) = (File.ReadAllBytes(ControllerFiles.MainFile(_user)), File.ReadAllBytes(ControllerFiles.PadFile(_user)));
        Assert.False(ControllerFiles.WriteFirstStart(_user, StandardLayouts.FirstStart([Xbox])));
        Assert.Equal(main, File.ReadAllBytes(ControllerFiles.MainFile(_user)));
        Assert.Equal(pad, File.ReadAllBytes(ControllerFiles.PadFile(_user)));

        // a first start keeps a port kind the settings already name (the adapter, set by hand before any start)
        File.Delete(ControllerFiles.PadFile(_user));
        Assert.True(ControllerFiles.WriteFirstStart(_user, StandardLayouts.FirstStart([Xbox])));
        Assert.Equal(PortKind.Adapter, ControllerFiles.Read(_user).Ports[0].Kind);
    }

    /// <summary>The Controller page makes the folder ready each time it shows, also while a game runs or starts: a
    /// settings file that already has every line is not written again (it is written in place, and a game reading
    /// it at that moment would read a file cut short).</summary>
    [Fact]
    public void AFolderThatIsReadyIsNotWrittenAgain()
    {
        RecompLaunch.PrepareUserFolder(_user);
        var ini = ControllerFiles.MainFile(_user);
        var then = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ini, then);
        RecompLaunch.PrepareUserFolder(_user);
        Assert.Equal(then, File.GetLastWriteTimeUtc(ini));

        // one of the module's fixed lines with another value is still put right
        File.WriteAllLines(ini, File.ReadAllLines(ini).Select(l => l.StartsWith("Overclock =", StringComparison.Ordinal) ? "Overclock = 1.0" : l));
        File.SetLastWriteTimeUtc(ini, then);
        RecompLaunch.PrepareUserFolder(_user);
        Assert.Equal("6.0", RecompLaunch.Setting(File.ReadAllLines(ini), "Core", "Overclock"));
        Assert.NotEqual(then, File.GetLastWriteTimeUtc(ini));
    }

    [Fact]
    public void AFileThatCannotBeReadIsSaidAndNotWrittenOver()
    {
        File.WriteAllBytes(ControllerFiles.PadFile(_user), new byte[ControllerFiles.MaxBytes + 1]);
        var read = ControllerFiles.Read(_user);
        Assert.Contains("could not be read", read.Problem, StringComparison.Ordinal);
        Assert.All(read.Ports, p => Assert.Equal(PortKind.Off, p.Kind));
        var e = Assert.Throws<IOException>(() => ControllerFiles.Write(_user, 0, StandardLayouts.FirstStart([Xbox])));
        Assert.True(ControllerFiles.Failed(e));
        Assert.Equal(ControllerFiles.MaxBytes + 1, new FileInfo(ControllerFiles.PadFile(_user)).Length);
    }

    /// <summary>The emulator reads every section of one name as one, and of a key written twice the last line
    /// (its Common/IniFile.cpp). The page reads what the game reads, and what it writes is what is in force.</summary>
    [Fact]
    public void ASectionOrAKeyWrittenTwiceIsReadAsTheGameReadsIt()
    {
        string[] twice = ["[Core]", "GFXBackend = Vulkan", "SIDevice0 = 6", "[Interface]", "ConfirmStop = False", "[core]", "SIDevice0 = 12", "SIDevice1 = 6"];
        Assert.Equal(PortKind.Adapter, ControllerFiles.PortOf(twice, [], 0).Kind);
        Assert.Equal(PortKind.Controller, ControllerFiles.PortOf(twice, [], 1).Kind);
        Assert.Equal(PortKind.Adapter, ControllerFiles.PortOf(["[Core]", "SIDevice0 = 6", "SIDevice0 = 12"], [], 0).Kind);
        Assert.Equal("`B`", IniLines.Get(["[GCPad1]", "Buttons/A = `A`", "[GCPad2]", "Buttons/A = `C`", "[GCPad1]", "Buttons/A = `B`"], "GCPad1", "Buttons/A"));

        // a write leaves one line of the key, the one in force, and every other line where it was
        Assert.Equal(["[Core]", "GFXBackend = Vulkan", "[Interface]", "ConfirmStop = False", "[core]", "SIDevice0 = 0", "SIDevice1 = 6"],
            ControllerFiles.WithKind(twice, 0, PortSettings.Off));
        Assert.Equal(["[Core]", "SIDevice0 = 0"], ControllerFiles.WithKind(["[Core]", "SIDevice0 = 6", "SIDevice0 = 12"], 0, PortSettings.Off));
        // a key that is not there goes into the last section of the name: nothing after it can outrank it
        Assert.Equal(["[Core]", "A = 1", "[DSP]", "[Core]", "B = 2", "SIDevice2 = 12", ""],
            ControllerFiles.WithKind(["[Core]", "A = 1", "[DSP]", "[Core]", "B = 2", ""], 2, PortSettings.Off with { Kind = PortKind.Adapter }));
        // taken out: every line of it
        Assert.Equal(["[GCPad1]", "[GCPad1]"], IniLines.Set(["[GCPad1]", "Device = a", "[GCPad1]", "Device = b"], "GCPad1", "Device", null));

        // on disk, with the page's own write
        File.WriteAllLines(ControllerFiles.MainFile(_user), twice);
        Assert.Equal(PortKind.Adapter, ControllerFiles.Read(_user).Ports[0].Kind);
        ControllerFiles.Write(_user, 0, PortSettings.Off);
        Assert.Equal(PortKind.Off, ControllerFiles.Read(_user).Ports[0].Kind);
        Assert.Single(File.ReadAllLines(ControllerFiles.MainFile(_user)), l => l.StartsWith("SIDevice0", StringComparison.Ordinal));
    }

    /// <summary>"Every other line kept as it was", to the byte: a file's mark, its line ends, and a line in letters
    /// that are no UTF-8 (a file written by hand in the system's own code page) come through a write.</summary>
    [Fact]
    public void AWriteKeepsTheFilesBytesItDoesNotChange()
    {
        byte[] mark = [0xEF, 0xBB, 0xBF];
        File.WriteAllBytes(ControllerFiles.MainFile(_user), [.. mark, .. "[Core]\r\nSIDevice0 = 12\r\nFoo = bar\r\n"u8]);
        byte[] theirs = [.. "Device = DInput/0/Manette "u8, 0xE9];   // port 2's device, in Latin-1
        File.WriteAllBytes(ControllerFiles.PadFile(_user), [.. "[GCPad1]\nOptions/Mine = 3\n[GCPad2]\n"u8, .. theirs, .. "\n"u8]);
        var read = ControllerFiles.Read(_user);
        Assert.Equal(PortKind.Adapter, read.Ports[0].Kind);                       // read past the mark
        Assert.Equal("DInput/0/Manette \u00e9", read.Ports[1].Device);

        // port 1 written with a name in other letters: its own lines are UTF-8, as the emulator writes them
        ControllerFiles.Write(_user, 0, StandardLayouts.Applied(read.Ports[0], Xbox) with { Device = "SDL/0/Manette \u00e9t\u00e9" });
        var main = File.ReadAllBytes(ControllerFiles.MainFile(_user));
        Assert.Equal([.. mark, .. "[Core]\r\nSIDevice0 = 6\r\nFoo = bar\r\n"u8], main);   // the mark and the line ends are the file's own
        var pad = File.ReadAllBytes(ControllerFiles.PadFile(_user));
        Assert.True(pad.AsSpan().IndexOf(theirs) >= 0, "port 2's device line was changed by a write of port 1");
        Assert.True(pad.AsSpan().IndexOf("Device = SDL/0/Manette \u00e9t\u00e9\n"u8) >= 0);
        Assert.True(pad.AsSpan().IndexOf("Options/Mine = 3\n"u8) >= 0);
        Assert.DoesNotContain((byte)'\r', pad);
        var again = ControllerFiles.Read(_user);
        Assert.Equal(("SDL/0/Manette \u00e9t\u00e9", "DInput/0/Manette \u00e9"), (again.Ports[0].Device, again.Ports[1].Device));
        // a write that changes nothing writes nothing
        var then = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ControllerFiles.PadFile(_user), then);
        ControllerFiles.Write(_user, 0, again.Ports[0]);
        Assert.Equal(then, File.GetLastWriteTimeUtc(ControllerFiles.PadFile(_user)));
    }

    [Fact]
    public void AWriteThatFailsLeavesNothingBesideTheFile()
    {
        // the file's place is taken by a folder: the new text is written beside it, cannot be moved over it, and goes
        Directory.CreateDirectory(ControllerFiles.PadFile(_user));
        var e = Record.Exception(() => ControllerFiles.Write(_user, 0, StandardLayouts.FirstStart([Xbox])));
        Assert.True(e is not null && ControllerFiles.Failed(e), e?.ToString());
        Assert.Equal(["Dolphin.ini", "GCPadNew.ini"], Directory.EnumerateFileSystemEntries(Path.Combine(_user, "Config")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void AValueIsOneLineOfTheFile()
    {
        var port = StandardLayouts.FirstStart([Xbox]) with { Device = "SDL/0/Bad\n[GCPad2]\nDevice = x" };
        var pad = ControllerFiles.WithPort([], 0, port.With("Buttons/A", "`A`\r\nButtons/B = `B`"));
        Assert.DoesNotContain(pad, l => l.StartsWith("[GCPad2]", StringComparison.Ordinal));
        Assert.Equal("`A` Buttons/B = `B`", IniLines.Get(pad, "GCPad1", "Buttons/A"));
    }
}
