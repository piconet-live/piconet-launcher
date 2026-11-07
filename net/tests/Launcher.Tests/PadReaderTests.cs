using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.Tests;

/// <summary>
/// The launcher's controller helper (<see cref="PadReader"/>) and its one hard rule: it holds the devices, the GameCube
/// adapter among them, for as long as the launcher runs and no game does, its window in front or not; a game's start
/// ends it (<see cref="ControllerHelper.HoldForGame"/>) and none is started again until the game has gone. The pad works
/// the window only while it is in front. It reads the first port that is a pad's (never a keyboard's), or the
/// Controller page's port while the page shows, says that port's controller and no other's, starts anew when whether a
/// port is the adapter's changed, and gives a game with no controller settings its first ones.
/// </summary>
[Collection(ControllerHelpersAlone.Name)]   // the helpers of one process are one set (ControllerHelpersAlone)
public sealed class PadReaderTests
{
    private static readonly PadConditions InFront = new(Focused: true, GameRunning: false, Checking: false);

    private const string Devices =
        """{"is":"devices","default":"DInput/0/Keyboard Mouse","devices":[{"id":"SDL/0/Pad","backend":"SDL","index":0,"name":"Pad","gamepad":true,"rumble":false}]}""";

    private static string Ports(params (int Kind, string Device, bool Connected)[] ports) =>
        "{\"is\":\"ports\",\"ports\":[" + string.Join(",", ports.Select(p =>
            $"{{\"kind\":{p.Kind},\"device\":\"{p.Device}\",\"connected\":{(p.Connected ? "true" : "false")}}}")) + "]}";

    private static string PadLine(int port, string button) =>
        $$"""{"is":"pad","port":{{port}},"kind":6,"connected":true,"buttons":["{{button}}"],"stick":[128,128],"cStick":[128,128],"l":0,"r":0}""";

    private static async Task Until(Func<bool> met, string what)
    {
        for (var i = 0; i < 300 && !met(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(met(), what);
    }

    [Fact]
    public void ThePadsPortIsTheFirstPadsNeverAKeyboards()
    {
        Assert.Null(PadReader.NavPortOf([new(6, "DInput/0/Keyboard Mouse", true), new(0, "", false)]));
        Assert.Equal(1, PadReader.NavPortOf([new(6, "DInput/0/Keyboard Mouse", true), new(6, "XInput/0/Gamepad", true)]));
        Assert.Equal(2, PadReader.NavPortOf([new(0, "", false), new(6, "SDL/0/Pad", false), new(12, "", true)]));   // plugged in first
        Assert.Equal(1, PadReader.NavPortOf([new(0, "", false), new(6, "SDL/0/Pad", false), new(6, "", true)]));    // no device: none
        Assert.Equal(0, PadReader.NavPortOf([new(12, "", false)]));
        Assert.True(PadReader.IsKeyboard("XInput2/0/Virtual core pointer"));
        Assert.False(PadReader.IsKeyboard("SDL/0/Keyboard Pad"));
    }

    [Fact]
    public void TheHelperIsHeldWithNoGameAndThePadActsOnlyInFront()
    {
        Assert.True(InFront.MayHold);
        Assert.True((InFront with { Focused = false }).MayHold);    // the window left: the adapter stays the launcher's
        Assert.False((InFront with { Focused = false }).MayAct);
        Assert.True(InFront.MayAct);
        Assert.False((InFront with { GameRunning = true }).MayHold);
        Assert.False((InFront with { Checking = true }).MayHold);
        using (ControllerHelper.HoldForGame())
        {
            Assert.False(InFront.MayHold);   // a game starting: the devices are the game's
        }
        Assert.True(InFront.MayHold);
    }

    [Fact]
    public async Task TheHelperStaysWhenTheWindowIsLeftAndThePadActsOnlyInFront()
    {
        var doubles = new List<HelperDouble>();
        using var reader = new PadReader(() =>
        {
            var d = new HelperDouble();
            lock (doubles)
            {
                doubles.Add(d);
            }
            return d.Helper();
        }, askAdapter: false);
        var heard = new List<HelperPad>();
        reader.Pad += pad =>
        {
            lock (heard)
            {
                heard.Add(pad);
            }
        };
        var said = 0;
        reader.Said += (_, message) => Interlocked.Add(ref said, message is HelperPad ? 1 : 0);

        reader.Update(InFront with { Focused = false });             // the launcher started behind another window
        await Until(() => reader.Holding, "the helper started without the window in front");
        var first = doubles.Single();
        first.Say(Devices, Ports((6, "DInput/0/Keyboard Mouse", true), (6, "SDL/0/Pad", true)));
        await Until(() => first.Sent.Contains("""{"do":"watch","port":1}"""), "the pad's port watched");
        first.Say(PadLine(1, "A"));
        await Until(() => Volatile.Read(ref said) == 1, "the pad's line heard");
        Assert.Empty(heard);                                            // the pad does not work a window not in front
        Assert.Equal("SDL/0/Pad", reader.Devices!.Devices.Single().Id);

        reader.Update(InFront);
        first.Say(PadLine(0, "A"), PadLine(1, "B"));
        await Until(() => heard.Count == 1, "the pad's line");
        Assert.Equal(1, heard.Single().Port);                           // the keyboard's port is never heard
        Assert.DoesNotContain(first.Sent, s => s.Contains("adapter", StringComparison.Ordinal));   // a test's: no adapter

        reader.Update(InFront with { Focused = false });                // left again: the same helper holds on
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.True(reader.Holding);
        Assert.False(first.InputClosed);
        Assert.Single(doubles);

        reader.Dispose();                                               // the launcher closes: released
        Assert.True(first.InputClosed);
        Assert.False(reader.Holding);
    }

    [Fact]
    public async Task AGamesStartEndsTheHelperAndNoneStartsUntilTheGameHasGone()
    {
        var doubles = new List<HelperDouble>();
        using var reader = new PadReader(() =>
        {
            // as ControllerHelper.Start: none while a game holds the devices
            if (ControllerHelper.Held)
            {
                return null;
            }
            var d = new HelperDouble();
            lock (doubles)
            {
                doubles.Add(d);
            }
            return d.Helper();
        }, askAdapter: true);
        reader.Update(InFront);
        await Until(() => reader.Holding, "the helper started");
        var first = doubles.Single();
        await Until(() => first.Sent.Contains("""{"do":"adapter"}"""), "the adapter asked for, as a game does");

        var hold = ControllerHelper.HoldForGame();                     // RecompLaunch.Start, before the game
        Assert.True(first.InputClosed);                                 // ended before HoldForGame came back
        await Until(() => !reader.Holding, "the reader knows its helper ended");
        reader.Update(InFront);                                         // the window still in front: no matter
        reader.Update(InFront with { GameRunning = true });
        await Task.Delay(1800, TestContext.Current.CancellationToken);  // longer than a fault's restart
        Assert.Single(doubles);
        Assert.False(reader.Holding);

        hold.Dispose();                                                 // the game has gone
        reader.Update(InFront);
        await Until(() => doubles.Count == 2 && reader.Holding, "read again after the game");
    }

    [Fact]
    public async Task AGameWithNoControllerSettingsGetsItsFirstOnes()
    {
        var helper = new HelperDouble();
        IReadOnlyList<ControllerDevice>? given = null;
        using var reader = new PadReader(helper.Helper, askAdapter: false, firstStart: devices =>
        {
            given = devices;
            return true;
        });
        var written = 0;
        reader.FirstStartWritten += () => Interlocked.Increment(ref written);
        reader.Update(InFront);
        await Until(() => reader.Holding, "the helper started");
        helper.Say(Devices);
        await Until(() => helper.Sent.Contains("""{"do":"reload"}"""), "the settings read again once written");
        Assert.Equal("SDL/0/Pad", Assert.Single(given!).Id);
        await Until(() => Volatile.Read(ref written) == 1, "the page told the files changed");
    }

    /// <summary>The Controller page uses the launcher's helper while it shows: the helper says the page's port, the pad
    /// still works the window from any pad's port (the adapter's too), and once the page is left the pad's port is
    /// watched again.</summary>
    [Fact]
    public async Task ThePageWatchesItsPortOnTheLaunchersHelper()
    {
        var helper = new HelperDouble();
        using var reader = new PadReader(helper.Helper, askAdapter: false);
        var heard = new List<int>();
        reader.Pad += pad =>
        {
            lock (heard)
            {
                heard.Add(pad.Port);
            }
        };
        reader.Update(InFront);
        await Until(() => reader.Holding, "the helper started");
        helper.Say(Ports((6, "SDL/0/Pad", true), (6, "DInput/0/Keyboard Mouse", true), (12, "", true)));
        await Until(() => helper.Sent.Contains("""{"do":"watch","port":0}"""), "the pad's port watched");
        Assert.Equal(0, reader.NavPort);

        reader.PageWatch(true, 2);                                      // the page shows the adapter's port
        await Until(() => helper.Sent.Contains("""{"do":"watch","port":2}"""), "the page's port watched");
        helper.Say(PadLine(1, "A"), PadLine(2, "A"));
        await Until(() => heard.Count == 1, "the adapter's port's line");
        Assert.Equal([2], heard);                                       // a pad's port works the window; the keyboard's never
        Assert.True(reader.IsPadPort(2));
        Assert.False(reader.IsPadPort(1));
        Assert.False(reader.IsPadPort(7));

        var watches = helper.Sent.Count(s => s == """{"do":"watch","port":0}""");
        reader.PageWatch(false);                                        // the page left
        await Until(() => helper.Sent.Count(s => s == """{"do":"watch","port":0}""") == watches + 1, "the pad's port watched again");
    }

    /// <summary>The emulator sets its devices up by whether a port is the adapter's, at its start: when the settings
    /// change that, the helper is started anew, as a game would be. The adapter's "not asked", said before the
    /// launcher's question came, is no state of the adapter's.</summary>
    [Fact]
    public async Task AnAdaptersPortSetOrUnsetStartsTheHelperAnew()
    {
        var doubles = new List<HelperDouble>();
        using var reader = new PadReader(() =>
        {
            var d = new HelperDouble();
            lock (doubles)
            {
                doubles.Add(d);
            }
            return d.Helper();
        }, askAdapter: true);
        var changes = new List<bool>();
        reader.HelperChanged += h =>
        {
            lock (changes)
            {
                changes.Add(h is not null);
            }
        };
        reader.Update(InFront);
        await Until(() => reader.Holding, "the helper started");
        var first = doubles.Single();
        first.Say("""{"is":"adapter","state":"notAsked","detail":"","ports":[false,false,false,false]}""",
            Ports((6, "SDL/0/Pad", true)));
        await Until(() => first.Sent.Contains("""{"do":"watch","port":0}"""), "the first ports line");
        Assert.Null(reader.Adapter);                                    // not known yet: the question was on its way
        first.Say(Ports((12, "", false)));                              // the player set port 1 to the adapter
        await Until(() => first.InputClosed, "the helper ended");
        await Until(() => doubles.Count == 2 && reader.Holding, "a new helper");
        var second = doubles[1];
        second.Say("""{"is":"adapter","state":"found","detail":"","ports":[true,false,false,false]}""");
        await Until(() => reader.Adapter is { State: AdapterState.Found }, "the adapter found");
        lock (changes)
        {
            Assert.Equal([true, false, true], changes);
        }
    }
}
