using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.Tests;

/// <summary>
/// The pad that works the window (<see cref="PadReader"/>) and its one hard rule: a helper of its own runs only
/// while the window is in front and no game runs; it is ended the moment the window is left, and a game's start ends
/// it (<see cref="ControllerHelper.HoldForGame"/>) and none is started again until the game has gone. It reads the
/// first port that is a pad's (never a keyboard's), says that port's controller and no other's, and gives a game
/// with no controller settings its first ones.
/// </summary>
[Collection(ControllerHelpersAlone.Name)]   // the helpers of one process are one set (ControllerHelpersAlone)
public sealed class PadReaderTests
{
    private static readonly PadConditions InFront = new(Focused: true, GameRunning: false, Checking: false, PageHelper: false);

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
    public void ThePadsAreReadOnlyInFrontAndWithNoGame()
    {
        Assert.True(InFront.MayRead);
        Assert.False((InFront with { Focused = false }).MayRead);
        Assert.False((InFront with { GameRunning = true }).MayRead);
        Assert.False((InFront with { Checking = true }).MayRead);
        Assert.False((InFront with { PageHelper = true }).MayRead);
        using (ControllerHelper.HoldForGame())
        {
            Assert.False(InFront.MayRead);   // a game starting: the devices are the game's
        }
        Assert.True(InFront.MayRead);
    }

    [Fact]
    public async Task TheHelperRunsInFrontAndIsEndedWhenTheWindowIsLeft()
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

        reader.Update(InFront with { Focused = false });
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(doubles);                                          // never while the window is not in front

        reader.Update(InFront);
        await Until(() => reader.Holding, "the helper started");
        var first = doubles.Single();
        first.Say(Devices, Ports((6, "DInput/0/Keyboard Mouse", true), (6, "SDL/0/Pad", true)));
        await Until(() => first.Sent.Contains("""{"do":"watch","port":1}"""), "the pad's port watched");
        first.Say(PadLine(0, "A"), PadLine(1, "B"));
        await Until(() => heard.Count == 1, "the pad's line");
        Assert.Equal(1, heard.Single().Port);                           // the keyboard's port is never heard
        Assert.DoesNotContain(first.Sent, s => s.Contains("adapter", StringComparison.Ordinal));   // a test's: no adapter

        reader.Update(InFront with { Focused = false });
        Assert.False(reader.Holding);
        await Until(() => first.InputClosed, "the helper ended when the window was left");
        await reader.Ending;

        reader.Update(InFront);
        await Until(() => doubles.Count == 2 && reader.Holding, "a new helper in front again");
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
        reader.Update(InFront);
        await Until(() => reader.Holding, "the helper started");
        helper.Say(Devices);
        await Until(() => helper.Sent.Contains("""{"do":"reload"}"""), "the settings read again once written");
        Assert.Equal("SDL/0/Pad", Assert.Single(given!).Id);
    }

    [Fact]
    public void AnotherHelpersLinesCountForAnyPadsPort()
    {
        using var reader = new PadReader(() => null);
        var heard = new List<int>();
        reader.Pad += pad => heard.Add(pad.Port);
        reader.Heard(ControllerMessages.Checked(Ports((6, "SDL/0/Pad", true), (6, "DInput/0/Keyboard Mouse", true), (12, "", true)))!);
        Assert.Equal(0, reader.NavPort);
        reader.Heard(ControllerMessages.Checked(PadLine(1, "A"))!);
        reader.Heard(ControllerMessages.Checked(PadLine(2, "A"))!);
        reader.Heard(ControllerMessages.Checked(PadLine(0, "A"))!);
        Assert.Equal([2, 0], heard);                                    // the page shows the adapter's port: heard too
        Assert.True(reader.IsPadPort(2));
        Assert.False(reader.IsPadPort(1));
        Assert.False(reader.IsPadPort(7));
    }
}
