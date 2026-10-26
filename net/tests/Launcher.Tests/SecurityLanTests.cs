using System.Net;
using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The list of LAN servers is made from datagrams any computer on the network can send.</summary>
public sealed class SecurityLanTests
{
    private static LanAnnouncement? Announcement(string name, long port) => LanAnnouncement.Decode(Encoding.UTF8.GetBytes(
        $$"""{"service":"fzgx-online","name":{{name}},"controlPort":{{port}},"version":{{ControlProtocol.Version}}}"""));

    /// <summary>One datagram with a port no server listens on closed every launcher on the network: the LAN page
    /// builds each server's address on the window's thread, and the address could not be built.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(2147483647)]
    public void AnAnnouncementWithAPortThatIsNoneIsNotListed(int port)
    {
        using var browser = new LanBrowser(port: 0);
        var changes = 0;
        browser.Changed += () => changes++;
        browser.Heard(Announcement("\"Den\"", port)!, IPAddress.Parse("192.168.1.66"));
        Assert.Empty(browser.Servers);
        Assert.Equal(0, changes);
        browser.Heard(Announcement("\"Den\"", 47810)!, IPAddress.Parse("192.168.1.20"));
        Assert.Equal(new Uri("ws://192.168.1.20:47810/ws"), Assert.Single(browser.Servers).Coordinator);   // what the LAN page reads
    }

    [Fact]
    public void AnAnnouncedNameIsShownAsAShortPlainName()
    {
        using var browser = new LanBrowser(port: 0);
        browser.Heard(Announcement("null", 47810)!, IPAddress.Parse("192.168.1.20"));
        browser.Heard(Announcement("\"" + new string('W', 60000) + "\"", 47811)!, IPAddress.Parse("192.168.1.20"));
        browser.Heard(Announcement("\"Den\\u202E\\u0007 of\\nus\"", 47812)!, IPAddress.Parse("192.168.1.20"));
        Assert.Equal(["", "Den ofus", new string('W', 40)], browser.Servers.Select(s => s.Name));
    }

    [Fact]
    public void TheListDoesNotGrowWithEveryAddressThatAnnounces()
    {
        using var browser = new LanBrowser(port: 0);
        for (var i = 0; i < 5000; i++)
        {
            browser.Heard(Announcement("\"flood\"", 1 + (i % 60000))!, new IPAddress([10, 0, (byte)(i >> 8), (byte)i]));
        }
        Assert.Equal(LanBrowser.MaxServers, browser.Servers.Count);
    }
}
