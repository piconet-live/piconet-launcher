using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The links in a server's message (the message of the day): http and https addresses and bare ones with a
/// known top-level domain are links, opened as https; nothing else is, and a sentence's end is no part of one. The
/// pieces put together are the message.</summary>
public sealed class MessageLinksTests
{
    private static List<(string Text, string? Link)> Read(string text)
    {
        var pieces = MessageLinks.Split(text);
        Assert.Equal(text, string.Concat(pieces.Select(p => p.Text)));
        return [.. pieces.Select(p => (p.Text, p.Link?.AbsoluteUri))];
    }

    private static List<string> Links(string text) => [.. Read(text).Where(p => p.Link is not null).Select(p => p.Link!)];

    [Fact]
    public void AnAddressWithItsSchemeIsALinkAndTheSentencesEndIsNot()
    {
        Assert.Equal([("Sign up at ", null), ("https://piconet.live/cup?day=fri", "https://piconet.live/cup?day=fri"), ("!", null)],
            Read("Sign up at https://piconet.live/cup?day=fri!"));
        Assert.Equal(["http://example.com/a_(b)"], Links("see http://example.com/a_(b)."));
        Assert.Equal(["https://example.com/x"], Links("(https://example.com/x)"));
    }

    [Theory]
    [InlineData("Join us on discord.gg/abc123.", "https://discord.gg/abc123")]
    [InlineData("piconet.live has the times", "https://piconet.live/")]
    [InlineData("www.youtube.com/watch?v=x, then", "https://www.youtube.com/watch?v=x")]
    public void ABareAddressWithAKnownDomainOpensAsHttps(string text, string link) => Assert.Equal([link], Links(text));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/system32")]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("someone@example.com")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("Races at 8:00 PM EST. This Saturday at 6:00 PM, on 10/30, e.g. now.")]
    [InlineData("version 0.5.2 or file.exe or notes.txt")]
    [InlineData("xhttps://example.com")]
    [InlineData("https://")]
    public void NothingElseIsALink(string text) => Assert.Empty(Links(text));

    [Fact]
    public void ManyLinksStayInOrderAndNoneIsNothing()
    {
        Assert.Equal(["https://a.com/", "https://discord.gg/x"], Links("a.com and https://discord.gg/x"));
        Assert.Empty(MessageLinks.Split(""));
        Assert.Empty(MessageLinks.Split(null));
        Assert.Empty(Links("https://" + new string('a', MessageLinks.MostLink) + ".com"));
    }
}
