using System.Text;
using System.Threading.Channels;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>A double for the controller helper: it says what a test tells it to, keeps what the launcher sent, and
/// ends when its input closes, as the real one does.</summary>
internal sealed class HelperDouble
{
    private readonly Channel<string> _said = Channel.CreateUnbounded<string>();

    public HelperDouble()
    {
        To = new Input(this);
        From = new Output(_said.Reader);
    }

    public TextWriter To { get; }

    public TextReader From { get; }

    /// <summary>The launcher's lines, as they came.</summary>
    public List<string> Sent { get; } = [];

    public bool InputClosed { get; private set; }

    /// <summary>What it answers a line of the launcher's with.</summary>
    public Func<string, IEnumerable<string>>? Answer { get; set; }

    public ControllerHelper Helper() => new(To, From);

    public void Say(params string[] lines)
    {
        foreach (var line in lines)
        {
            _said.Writer.TryWrite(line + "\n");
        }
    }

    public void End() => _said.Writer.TryComplete();

    private sealed class Input(HelperDouble owner) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            lock (owner.Sent)
            {
                owner.Sent.Add(value ?? "");
            }
            owner.Say([.. owner.Answer?.Invoke(value ?? "") ?? []]);
        }

        protected override void Dispose(bool disposing)
        {
            owner.InputClosed = true;
            owner.End();
            base.Dispose(disposing);
        }
    }

    private sealed class Output(ChannelReader<string> said) : TextReader
    {
        private string _rest = "";

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            while (_rest.Length == 0)
            {
                if (!await said.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return 0;
                }
                if (said.TryRead(out var next))
                {
                    _rest = next;
                }
            }
            var n = Math.Min(buffer.Length, _rest.Length);
            _rest.AsSpan(0, n).CopyTo(buffer.Span);
            _rest = _rest[n..];
            return n;
        }
    }
}

/// <summary>The collection of the tests of the launcher's own controller helpers (<see cref="ControllerHelper"/>,
/// <see cref="Launcher.Core.Pads.PadReader"/>, the check before a start), which run with nothing beside them: the
/// helpers of one process are one set, and a test of another collection ends them all (every launcher window that
/// closes calls <see cref="ControllerHelper.StopAll"/>, as every game start does) or holds them off
/// (<see cref="ControllerHelper.HoldForGame"/>). Run beside the app's tests, a helper of these was now and then ended in
/// the middle of its test, or none was started.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ControllerHelpersAlone
{
    public const string Name = "controller helper";
}

[Collection(ControllerHelpersAlone.Name)]   // one class at a time, and nothing beside it
public sealed partial class ControllerHelperTests : IDisposable
{
    private const string Devices =
        """{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[{"id":"DInput/0/Keyboard Mouse","backend":"DInput","index":0,"name":"Keyboard Mouse","gamepad":false,"rumble":false},{"id":"XInput/0/Gamepad","backend":"XInput","index":0,"name":"Gamepad","gamepad":false,"rumble":true},{"id":"SDL/0/Xbox One Controller","backend":"SDL","index":0,"name":"Xbox One Controller","gamepad":true,"rumble":true}]}""";

    private const string NoGamepad =
        """{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[{"id":"DInput/0/Keyboard Mouse","backend":"DInput","index":0,"name":"Keyboard Mouse","gamepad":false,"rumble":false}]}""";

    private const string Ready = """{"is":"ready","version":1}""";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "controller-helper-" + Guid.NewGuid().ToString("N"));

    public ControllerHelperTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private RecompPaths Paths => new(Path.Combine(_folder, "recomp", "moderngekko-run.exe"), Path.Combine(_folder, "recomp", "m.dll"),
        Path.Combine(_folder, "game"), Path.Combine(_folder, "user"));

    private static string Ports(int kind, string device, bool connected) =>
        $$"""{"is":"ports","ports":[{"kind":{{kind}},"device":"{{device}}","connected":{{(connected ? "true" : "false")}}},{"kind":0,"device":"","connected":false},{"kind":0,"device":"","connected":false},{"kind":0,"device":"","connected":false}]}""";

    [Fact]
    public void EveryMessageIsReadAsWhatItSays()
    {
        var devices = Assert.IsType<HelperDevices>(ControllerMessages.Checked(Devices));
        Assert.Equal("DInput/0/Keyboard Mouse", devices.Default);
        Assert.Equal(["Keyboard", "Gamepad (XInput)", "Xbox One Controller"], devices.Devices.Select(d => d.Label));
        Assert.Equal([true, false, true], devices.Devices.Select(d => d.Offered));   // XInput's is SDL's a second time
        Assert.True(devices.Devices[2] is { Gamepad: true, Rumble: true, Backend: "SDL", Index: 0 });
        Assert.True(devices.Devices[0].Keyboard);

        Assert.Equal(1, Assert.IsType<HelperReady>(ControllerMessages.Checked(Ready)).Version);
        var ports = Assert.IsType<HelperPorts>(ControllerMessages.Checked(Ports(6, "SDL/0/Xbox One Controller", true)));
        Assert.Equal(new HelperPort(6, "SDL/0/Xbox One Controller", true), ports.Ports[0]);
        Assert.Equal(4, ports.Ports.Count);

        var adapter = Assert.IsType<HelperAdapter>(ControllerMessages.Checked(
            """{"is":"adapter","state":"found","detail":"","ports":[true,false,false,true]}"""));
        Assert.Equal(AdapterState.Found, adapter.State);
        Assert.Equal([true, false, false, true], adapter.Ports);
        var busy = Assert.IsType<HelperAdapter>(ControllerMessages.Checked(
            """{"is":"adapter","state":"inUse","detail":"Access denied (insufficient permissions)","ports":[false,false,false,false]}"""));
        Assert.Equal((AdapterState.InUse, "Access denied (insufficient permissions)"), (busy.State, busy.Detail));

        var pad = Assert.IsType<HelperPad>(ControllerMessages.Checked(
            """{"is":"pad","port":2,"kind":6,"connected":true,"buttons":["A","Start","Left"],"stick":[255,0],"cStick":[128,130],"l":40,"r":255}"""));
        Assert.Equal((2, 6, true), (pad.Port, pad.Kind, pad.Connected));
        Assert.Equal(["A", "Left", "Start"], pad.Buttons.Order());
        Assert.Equal((255, 0, 128, 130, 40, 255), (pad.StickX, pad.StickY, pad.CStickX, pad.CStickY, pad.L, pad.R));

        var captured = Assert.IsType<HelperCaptured>(ControllerMessages.Checked(
            """{"is":"captured","id":7,"device":"SDL/0/Xbox One Controller","input":"Button A","expression":"`Button A`"}"""));
        Assert.Equal((7, "SDL/0/Xbox One Controller", "Button A", "`Button A`"), (captured.Id, captured.Device, captured.Input, captured.Expression));
        var nothing = Assert.IsType<HelperCaptured>(ControllerMessages.Checked(
            """{"is":"captured","device":"SDL/0/Xbox One Controller","input":null,"expression":null}"""));
        Assert.Null(nothing.Input);
        Assert.Equal("not a command: x", Assert.IsType<HelperProblem>(ControllerMessages.Checked("""{"is":"problem","text":"not a command: x"}""")).Text);
    }

    [Fact]
    public void ALineThatIsNoMessageIsNothingAndAMessageIsHeldToItsLimits()
    {
        // the emulator's own output between the lines, half a line, another kind of JSON, a kind not known
        foreach (var line in new[] { null, "", "audio backend: Cubeb", "{\"is\":\"dev", "[1,2]", "7", "{\"is\":7}", "{\"is\":\"later\"}", "{}" })
        {
            Assert.Null(ControllerMessages.Checked(line));
        }
        Assert.Null(ControllerMessages.Checked("{\"is\":\"problem\",\"text\":\"" + new string('x', ControllerMessages.MaxLineLength) + "\"}"));

        // a device names itself: a long name is cut, control characters go, and no more devices than the page lists
        var many = string.Join(",", Enumerable.Range(0, 200).Select(i =>
            $$"""{"id":"SDL/{{i}}/Pad","backend":"SDL","index":{{i}},"name":"Pad\u0007\u001b[31m{{new string('n', 40)}}","gamepad":true}"""));
        var devices = Assert.IsType<HelperDevices>(ControllerMessages.Checked($$"""{"is":"devices","devices":[{{many}}]}"""));
        Assert.Equal(ControllerMessages.MaxDevices, devices.Devices.Count);
        Assert.DoesNotContain(devices.Devices, d => d.Name.Any(char.IsControl));
        var long1 = Assert.IsType<HelperDevices>(ControllerMessages.Checked(
            $$"""{"is":"devices","devices":[{"id":"SDL/0/{{new string('n', 5000)}}","name":7,"index":-4,"gamepad":"yes"},{"name":"no id"},5]}"""));
        var one = Assert.Single(long1.Devices);
        Assert.Equal((ControllerMessages.MaxNameLength, "", 0, false), (one.Id.Length, one.Name, one.Index, one.Gamepad));

        // numbers outside their range are brought into it; parts that are missing are at rest
        var pad = Assert.IsType<HelperPad>(ControllerMessages.Checked(
            """{"is":"pad","port":99,"kind":-1,"connected":"yes","buttons":["A","Fire",3,"A"],"stick":[900,-5],"cStick":[1],"l":1e9,"r":"x"}"""));
        Assert.Equal((3, 0, false), (pad.Port, pad.Kind, pad.Connected));
        Assert.Equal(["A"], pad.Buttons.ToArray());
        Assert.Equal((255, 0, 128, 128, 0, 0), (pad.StickX, pad.StickY, pad.CStickX, pad.CStickY, pad.L, pad.R));
        var ports = Assert.IsType<HelperPorts>(ControllerMessages.Checked("""{"is":"ports","ports":[{"kind":12},7,{},{},{},{}]}"""));
        Assert.Equal([12, 0, 0, 0], ports.Ports.Select(p => p.Kind));
        var adapter = Assert.IsType<HelperAdapter>(ControllerMessages.Checked("""{"is":"adapter","state":"broken","ports":"all"}"""));
        Assert.Equal(AdapterState.NotAsked, adapter.State);
        Assert.Equal([false, false, false, false], adapter.Ports);
    }

    [Fact]
    public async Task TheLauncherTalksToTheHelperInLines()
    {
        var double1 = new HelperDouble();
        using var helper = double1.Helper();
        var got = new List<ControllerMessage>();
        var ended = new TaskCompletionSource();
        var captured = new TaskCompletionSource<HelperCaptured>();
        helper.Received += m =>
        {
            lock (got)
            {
                got.Add(m);
            }
            if (m is HelperCaptured c)
            {
                captured.TrySetResult(c);
            }
        };
        helper.Ended += () => ended.TrySetResult();
        double1.Answer = line => line.Contains("\"capture\"", StringComparison.Ordinal) && line.Contains("Xbox", StringComparison.Ordinal)
            ? ["""{"is":"captured","id":1,"device":"SDL/0/Xbox One \"Elite\"","input":"Button A","expression":"`Button A`"}"""]
            : [];
        double1.Say(Devices, "audio backend: Cubeb", Ready);
        helper.Listen();
        helper.AskAdapter();
        helper.Watch(2);
        helper.Watch(null);
        Assert.Equal(1, helper.Capture("SDL/0/Xbox One \"Elite\"", 6000));
        Assert.Equal(2, helper.Capture(null));   // each listening has its number, which its answer carries
        helper.Rumble(1, 400);
        helper.Reload();
        var heard = await captured.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal((1, "Button A"), (heard.Id, heard.Input));
        await helper.SentAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);   // lines are written by a task of the helper's own
        Assert.Equal(
        [
            """{"do":"adapter"}""", """{"do":"watch","port":2}""", """{"do":"watch","port":-1}""",
            """{"do":"capture","id":1,"device":"SDL/0/Xbox One \"Elite\"","ms":6000}""", """{"do":"capture","id":2,"device":"","ms":5000}""",
            """{"do":"rumble","port":1,"ms":400}""", """{"do":"reload"}""",
        ], double1.Sent);

        // ending it closes its input, which is its word to go; nothing is sent after
        helper.Dispose();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(double1.InputClosed);
        helper.Reload();
        Assert.Equal(7, double1.Sent.Count);
        lock (got)
        {
            Assert.Equal([typeof(HelperDevices), typeof(HelperReady), typeof(HelperCaptured)], got.Select(m => m.GetType()));
        }
    }

    [Fact]
    public async Task NoHelperIsLeftWhenAGameStarts()
    {
        var (a, b) = (new HelperDouble(), new HelperDouble());
        using var first = a.Helper();
        using var second = b.Helper();
        ControllerHelper.StopAll();   // what every game start does first (RecompLaunch.Start)
        Assert.True(a.InputClosed && b.InputClosed);
        Assert.Null(await ControllerProbe.AskAsync(() => null, cancel: TestContext.Current.CancellationToken));   // no runner: nothing known, and nothing raised
    }

    /// <summary>The page ends its helper off the window's thread. A game start at that moment (StopAll) comes back
    /// only when that helper has gone too: until then it holds the devices the game is about to open.</summary>
    [Fact]
    public async Task AGameStartWaitsForAHelperThatIsBeingEnded()
    {
        var token = TestContext.Current.CancellationToken;
        using var closing = new ManualResetEventSlim();   // set: the helper's input is being closed
        using var gone = new ManualResetEventSlim();      // set: its program has gone
        var helper = new ControllerHelper(new SlowToGo(closing, gone), new StringReader(""));
        var ending = Task.Run(helper.Dispose, token);              // the page is left
        try
        {
            Assert.True(closing.Wait(TimeSpan.FromSeconds(10), token));
            var start = Task.Run(ControllerHelper.StopAll, token);     // what every game start does first
            Assert.NotSame(start, await Task.WhenAny(start, Task.Delay(500, token)));
            gone.Set();
            await start.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        finally
        {
            gone.Set();
            await ending.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
    }

    /// <summary>A helper's input whose closing takes as long as the program takes to go.</summary>
    private sealed class SlowToGo(ManualResetEventSlim closing, ManualResetEventSlim gone) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        protected override void Dispose(bool disposing)
        {
            closing.Set();
            gone.Wait(TimeSpan.FromSeconds(30));
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task OneQuestionGivesTheDevicesAndThePorts()
    {
        var double1 = new HelperDouble();
        double1.Say(Devices, Ports(6, "SDL/0/Xbox One Controller", true), """{"is":"adapter","state":"notAsked","detail":"","ports":[]}""", Ready);
        var snapshot = await ControllerProbe.AskAsync(double1.Helper, adapter: true, cancel: TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot.Devices.Count);
        Assert.Equal("DInput/0/Keyboard Mouse", snapshot.DefaultDevice);
        Assert.True(snapshot.Ports[0].Connected);
        Assert.Null(snapshot.Adapter);                    // no port is the adapter's: it was not asked for
        Assert.DoesNotContain(double1.Sent, l => l.Contains("adapter", StringComparison.Ordinal));
        Assert.True(double1.InputClosed);                 // the helper is ended with the answer
        Assert.Null(ControllerWords.Port1Problem(snapshot));

        // a helper that says nothing in time, or ends first: nothing known
        var silent = new HelperDouble();
        Assert.Null(await ControllerProbe.AskAsync(silent.Helper, limit: TimeSpan.FromMilliseconds(200), cancel: TestContext.Current.CancellationToken));
        Assert.True(silent.InputClosed);
        var gone = new HelperDouble();
        gone.End();
        Assert.Null(await ControllerProbe.AskAsync(gone.Helper, cancel: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APort1WithNothingPluggedInIsSaidBeforeTheRace()
    {
        async Task<string?> Said(string ports, string? adapter = null)
        {
            var d = new HelperDouble
            {
                Answer = line => line.Contains("adapter", StringComparison.Ordinal) && adapter is not null ? [adapter] : [],
            };
            d.Say(Devices, ports, Ready);
            return ControllerWords.Port1Problem(await ControllerProbe.AskAsync(d.Helper, adapter: true));
        }

        Assert.Null(await Said(Ports(6, "SDL/0/Xbox One Controller", true)));
        Assert.Equal("Not plugged in · DualSense Wireless Controller",
            await Said(Ports(6, "SDL/0/DualSense Wireless Controller", false)));
        Assert.Equal("Port 1 off · set a controller in Settings, Controller", await Said(Ports(0, "", false)));
        Assert.Equal("Port 1 has no controller · Settings, Controller", await Said(Ports(6, "", false)));
        // the adapter is asked for only when port 1 is set to it, and said in the page's own words
        Assert.Null(await Said(Ports(12, "", false), """{"is":"adapter","state":"found","detail":"","ports":[true,false,false,false]}"""));
        Assert.Equal("Adapter port 1 empty",
            await Said(Ports(12, "", false), """{"is":"adapter","state":"found","detail":"","ports":[false,true,false,false]}"""));
        Assert.Equal("In use by another program",
            await Said(Ports(12, "", false), """{"is":"adapter","state":"inUse","detail":"Access denied","ports":[]}"""));
        Assert.Equal("No GameCube adapter · Wii U switch",
            await Said(Ports(12, "", false), """{"is":"adapter","state":"none","detail":"","ports":[]}"""));
        Assert.Null(ControllerWords.Port1Problem(null));
        // every state of the adapter has its sentence
        Assert.All(Enum.GetValues<AdapterState>(), s => Assert.NotEmpty(ControllerWords.Adapter(new HelperAdapter(s, "why", [false, false, false, false]))));
        Assert.Equal("the keyboard", ControllerWords.DeviceName("DInput/0/Keyboard Mouse"));
    }

    [Fact]
    public async Task AFirstStartHasAWorkingController()
    {
        HelperDouble With(params string[] lines)
        {
            var d = new HelperDouble();
            d.Say(lines);
            return d;
        }

        // no settings, nothing to copy: the first gamepad found, with the standard layout
        var paths = Paths;
        Assert.Null(await ControllerStart.PrepareAsync(paths, null, start: With(Devices, Ports(6, "", false), Ready).Helper, cancel: TestContext.Current.CancellationToken));
        var port = ControllerFiles.Read(paths.UserFolder).Ports[0];
        Assert.Equal((PortKind.Controller, "SDL/0/Xbox One Controller", "`Button A`"), (port.Kind, port.Device, port.Map["Buttons/A"]));
        Assert.Equal("True", RecompLaunch.Setting(File.ReadAllLines(ControllerFiles.MainFile(paths.UserFolder)), "Core", "OverclockEnable"));

        // the next start touches nothing, and says when that controller is gone
        var before = File.ReadAllBytes(ControllerFiles.PadFile(paths.UserFolder));
        var said = await ControllerStart.PrepareAsync(paths, null, start: With(NoGamepad, Ports(6, "SDL/0/Xbox One Controller", false), Ready).Helper,
            cancel: TestContext.Current.CancellationToken);
        Assert.Equal("Not plugged in · Xbox One Controller", said);
        Assert.Equal(before, File.ReadAllBytes(ControllerFiles.PadFile(paths.UserFolder)));
    }

    [Fact]
    public async Task WithNoGamepadAFirstStartHasTheKeyboardAndWithNoAnswerNothingIsWritten()
    {
        var keyboard = Paths with { UserFolder = Path.Combine(_folder, "keys") };
        var d = new HelperDouble();
        d.Say(NoGamepad, Ports(6, "", false), Ready);
        Assert.Null(await ControllerStart.PrepareAsync(keyboard, null, start: d.Helper, cancel: TestContext.Current.CancellationToken));
        var port = ControllerFiles.Read(keyboard.UserFolder).Ports[0];
        Assert.Equal((PortKind.Controller, "DInput/0/Keyboard Mouse", "`UP`"), (port.Kind, port.Device, port.Map["Main Stick/Up"]));

        // no answer (the runner is not there; a helper that says nothing in time; one that ends first): nothing is
        // written, so nothing is pinned by a guess, and the emulator falls back to the keyboard by itself
        var none = Paths with { UserFolder = Path.Combine(_folder, "none") };
        Assert.Null(await ControllerStart.PrepareAsync(none, null, cancel: TestContext.Current.CancellationToken));
        Assert.False(ControllerFiles.HasSettings(none.UserFolder));
        var gone = new HelperDouble();
        gone.End();
        Assert.Null(await ControllerStart.PrepareAsync(none, null, start: gone.Helper, cancel: TestContext.Current.CancellationToken));
        Assert.False(ControllerFiles.HasSettings(none.UserFolder));

        // the next start, with the helper there and a gamepad plugged in, chooses: the gamepad
        var next = new HelperDouble();
        next.Say(Devices, Ports(6, "DInput/0/Keyboard Mouse", true), Ready);
        Assert.Null(await ControllerStart.PrepareAsync(none, null, start: next.Helper, cancel: TestContext.Current.CancellationToken));
        Assert.Equal("SDL/0/Xbox One Controller", ControllerFiles.Read(none.UserFolder).Ports[0].Device);
    }

    [Fact]
    public async Task WhatTheForksFolderGivesComesBeforeTheDefault()
    {
        var fork = Path.Combine(_folder, "dolphin-user");
        Directory.CreateDirectory(Path.Combine(fork, "Config"));
        File.WriteAllLines(Path.Combine(fork, "Config", "Dolphin.ini"), ["[Core]", "SIDevice0 = 12"]);
        File.WriteAllLines(Path.Combine(fork, "Config", "GCPadNew.ini"), ["[GCPad1]", "Device = XInput/0/Gamepad"]);
        var d = new HelperDouble();
        d.Say(Devices, Ports(12, "XInput/0/Gamepad", true), Ready);
        Assert.Null(await ControllerStart.PrepareAsync(Paths, fork, adapter: false, start: d.Helper, cancel: TestContext.Current.CancellationToken));
        var port = ControllerFiles.Read(Paths.UserFolder).Ports[0];
        Assert.Equal((PortKind.Adapter, "XInput/0/Gamepad"), (port.Kind, port.Device));
        Assert.Empty(port.Map);
    }

    private static readonly string[] Choices = ["F13", "X", "TAB", "RETURN", "Button A", "Left X+", "Shift", "LCONTROL"];
    private static readonly string[] LeavingKeys = ["Alt", "LMENU", "RMENU", "LWIN", "RWIN", "alt", "Alt_L", "Super_L"];
    private static readonly string[] EndingOrMouse = ["ESCAPE", "Escape", "Click 0", "Cursor X+"];

    [Fact]
    public void AKeyThatLeavesOrEndsAListeningIsNoChoice()
    {
        static HelperCaptured Heard(string? input) => new(1, "DInput/0/Keyboard Mouse", input, input is null ? null : $"`{input}`");

        // a key, a button, a stick: the player's choice
        Assert.All(Choices, input => Assert.True(ControllerWords.IsChoice(Heard(input)), input));
        // Alt and the Windows key are how the window is left (Alt+Tab, the Start menu): heard first, never taken
        Assert.All(LeavingKeys, input => Assert.False(ControllerWords.IsChoice(Heard(input)), input));
        // Esc cancels; the mouse sets nothing; nothing heard in time is nothing
        Assert.All(EndingOrMouse, input => Assert.False(ControllerWords.IsChoice(Heard(input)), input));
        Assert.False(ControllerWords.IsChoice(Heard(null)));
        Assert.False(ControllerWords.IsChoice(new HelperCaptured(1, "d", "F13", "")));
    }

    [Fact]
    public void ATestCanTellTheLauncherToLeaveTheAdapterAlone()
    {
        var (config, problem) = LaunchConfig.Parse("""{"about": "a page test", "debug": {"leaveAdapter": true}}"""u8.ToArray());
        Assert.Null(problem);
        Assert.True(config!.Debug.LeaveAdapter);
        Assert.False(config.Debug.ReachesARace);
        Assert.False(DebugSettings.Off.LeaveAdapter);
        Assert.Equal(["--controllers", "C:/data/recomp-user"], ControllerHelper.Arguments("C:/data/recomp-user"));
    }
}
