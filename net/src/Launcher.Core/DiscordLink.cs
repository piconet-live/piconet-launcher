using DiscordRPC;

namespace Launcher.Core;

/// <summary>
/// The connection to the player's own Discord: Lachee's DiscordRichPresence (MIT), which talks to the Discord app on
/// this computer by its local IPC (a named pipe on Windows, a socket on Linux and macOS) and nothing on the network.
/// The library's own thread connects, and connects again when Discord starts or comes back; its words reach
/// <see cref="DiscordPresence"/> as <see cref="PresenceLinkEvent"/>s. No join, spectate or account: only the status.
/// </summary>
public sealed class DiscordLink : IPresenceLink
{
    private readonly DiscordRpcClient _client;
    private readonly PresenceBrand _brand;

    public DiscordLink(PresenceBrand brand, Action<PresenceLinkEvent> heard)
    {
        ArgumentNullException.ThrowIfNull(brand);
        ArgumentNullException.ThrowIfNull(heard);
        _brand = brand;
        _client = new DiscordRpcClient(brand.ApplicationId)
        {
            SkipIdenticalPresence = true,
        };
        _client.OnReady += (_, _) => heard(PresenceLinkEvent.Ready);
        _client.OnConnectionFailed += (_, _) => heard(PresenceLinkEvent.NotFound);
        _client.OnClose += (_, _) => heard(PresenceLinkEvent.Lost);
        _client.Initialize();
    }

    public void Show(PresenceCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _client.SetPresence(Of(card, _brand));
    }

    public void Clear() => _client.ClearPresence();

    public void Dispose() => _client.Dispose();

    /// <summary>A card as the library sends it: the two lines, the pictures with their texts, the elapsed time, the
    /// button.</summary>
    public static RichPresence Of(PresenceCard card, PresenceBrand brand)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(brand);
        return new RichPresence
        {
            Details = card.Details,
            State = card.State,
            Assets = new Assets
            {
                LargeImageKey = brand.LargeImage,
                LargeImageText = brand.LargeText,
                SmallImageKey = card.SmallImage,
                SmallImageText = card.SmallImage is null ? null : card.SmallText,
            },
            Timestamps = card.Since is { } since ? new Timestamps(since.UtcDateTime) : null,
            Buttons = [new Button { Label = brand.ButtonLabel, Url = brand.ButtonUrl }],
        };
    }
}
