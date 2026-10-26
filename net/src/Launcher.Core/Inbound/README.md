# Launcher.Core/Inbound

What reaches the launcher over the network: from a server, from the local network, from a relay, from the player's
own game (its status board, `../Board/`, a file only this PC's programs can open). Anyone can send any of it (a LAN server is anyone's computer; a server passes on what players send), so
each file here takes the bytes in within a length and gives the rest of the launcher a checked value or nothing.

**The rule.** A socket is read here and nowhere else in `Launcher.Core` or `Launcher.App` (`ReceiveAsync`), and so
is JSON that came over one. The structure check reports a read that stands elsewhere.

| File | What arrives |
|---|---|
| `CoordinatorClient.Receive.cs` | A server's messages: the connection's receive loop, each message whole and within its length and in its order; a ping answered at once, also while relays are measured (the rest of the class is `../CoordinatorClient.cs`) |
| `ServerMessages.cs` | Every message of a server checked before anything is kept or shown: `Checked`, `Sound`, `Text` |
| `ServerStatusFeed.cs` | What a server says of itself to anyone (`/api/status`), asked every few seconds while a page watches |
| `ServerHealth.cs` | Whether the server answers (`/health`): the connection dot |
| `RelayMeasure.cs` | A relay's answers to five pings: the round trip the server asks for when a match forms |
| `LanBrowser.cs` | Servers announced on the local network: the list, bounded, with names as plain text |
| `ControllerMessages.cs` | The controller helper's lines (the game's own program on a pipe; a device names itself): each within a length, its lists within a count, its numbers within their range |
| `TypedNumber.cs` | A whole number the player typed into a field (a stepper's figure): digits only, kept inside its range |

Still outside: `QuickPlaySession.OnReceived` uses a checked message's numbers as indexes in places (a machine's
name). A new field of a message is checked in `ServerMessages` before a page reads it. Tests: `Security*Tests`, `FuzzTests`, `ServerHealthTests`,
`ServerStatusFeedTests`, `BoardTests` in `net/tests/Launcher.Tests`.
