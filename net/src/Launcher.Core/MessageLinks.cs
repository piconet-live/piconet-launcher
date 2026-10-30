using System.Text.RegularExpressions;

namespace Launcher.Core;

/// <summary>A piece of a server's message: its text, and the address it opens when it is a link (null: plain text).</summary>
public sealed record MessagePiece(string Text, Uri? Link = null);

/// <summary>
/// The links in a server's message (the message of the day), for the page to show as links: an address written with
/// http:// or https://, or a bare one whose name ends in a known top-level domain (discord.gg/xyz, piconet.live),
/// which opens as https. Nothing else is a link: no other scheme (javascript:, file:), no address with a user part,
/// and no word that only looks like one (8:00, e.g., 10/30). A link starts at the text's start or after a space or an
/// opening bracket, and the punctuation that ends a sentence is left out of it.
/// </summary>
public static partial class MessageLinks
{
    /// <summary>The top-level domains a bare address may end in: the common ones and those players' links use.</summary>
    private static readonly HashSet<string> BareDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "net", "org", "live", "gg", "io", "tv", "me", "co", "app", "dev", "gl", "ly", "be", "us", "uk", "eu", "de",
        "fr", "jp", "ca", "info", "xyz",
    };

    /// <summary>The longest address taken as a link.</summary>
    public const int MostLink = 300;

    // a word that starts with http(s)://, or a host of two or more labels with an optional path, after the start, a
    // space or an opening bracket
    [GeneratedRegex(@"(?<=^|[\s(\[<""'])(?:(?<scheme>https?://)[^\s<>""]+|(?<host>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24})(?<path>/[^\s<>""]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Candidate();

    /// <summary>The message in pieces, in order, links apart; the pieces' texts put together are the message.</summary>
    public static IReadOnlyList<MessagePiece> Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }
        var pieces = new List<MessagePiece>();
        var at = 0;
        try
        {
            foreach (Match m in Candidate().Matches(text))
            {
                var shown = Trimmed(m.Value);
                if (LinkOf(shown, m.Groups["scheme"].Success, m.Groups["host"].Value) is not { } link)
                {
                    continue;
                }
                if (m.Index > at)
                {
                    pieces.Add(new MessagePiece(text[at..m.Index]));
                }
                pieces.Add(new MessagePiece(shown, link));
                at = m.Index + shown.Length;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return [new MessagePiece(text)];
        }
        if (at < text.Length)
        {
            pieces.Add(new MessagePiece(text[at..]));
        }
        return pieces;
    }

    /// <summary>A sentence's end is no part of a link: trailing . , ! ? ; : and a closing bracket with no opening one.</summary>
    private static string Trimmed(string word)
    {
        while (word.Length > 0)
        {
            var last = word[^1];
            if (last is '.' or ',' or '!' or '?' or ';' or ':' or '\'' ||
                (last == ')' && word.Count(c => c == '(') < word.Count(c => c == ')')) ||
                (last == ']' && word.Count(c => c == '[') < word.Count(c => c == ']')))
            {
                word = word[..^1];
                continue;
            }
            break;
        }
        return word;
    }

    /// <summary>The address a word opens; null when it is none (an unknown top-level domain, a user part, too long).</summary>
    private static Uri? LinkOf(string word, bool schemed, string host)
    {
        if (word.Length is 0 or > MostLink)
        {
            return null;
        }
        if (!schemed)
        {
            var tld = host[(host.LastIndexOf('.') + 1)..];
            if (!BareDomains.Contains(tld))
            {
                return null;
            }
            word = "https://" + word;
        }
        return Uri.TryCreate(word, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
               uri.Host.Length > 0 && uri.UserInfo.Length == 0
            ? uri
            : null;
    }
}
