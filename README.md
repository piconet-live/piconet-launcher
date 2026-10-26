# piconet-launcher

[![CI](https://github.com/piconet-live/piconet-launcher/actions/workflows/ci.yml/badge.svg)](https://github.com/piconet-live/piconet-launcher/actions/workflows/ci.yml)

The Piconet launcher: the program a player opens to play F-Zero GX online or alone, to watch and keep replays and
to prepare the game from their own disc. It starts the native game (the runner,
[piconet-runner](https://github.com/piconet-live/piconet-runner)) and talks to Piconet's online service. Piconet is
online multiplayer for F-Zero GX: see [piconet](https://github.com/piconet-live/piconet).

No game data is stored here: the launcher reads your own disc image.

## Install

Download the Setup file (the `…-Setup.exe`) from the
[latest release](https://github.com/piconet-live/piconet-launcher/releases/latest) and run it. It installs the launcher for your Windows user (no administrator rights) and puts it on the Start menu
and the desktop. The first start asks for your F-Zero GX (USA) disc image and prepares the game's files from it.

Played before from a copy you unzipped (0.4.0 and earlier)? On the first screen, or later in Settings, Launcher,
choose **Bring over an older copy…** and pick that copy's folder: its settings (your name and tag), replays, memory
card and prepared game files are copied over, and the launcher starts again. The older copy is left as it was:
delete it when you like.

## Updates

Updates are automatic. The launcher looks for a new version when it starts and downloads it in the background;
it goes in the next time the launcher starts, or at once with **Restart to update** in the bottom left corner. It
never restarts while a game runs. You can switch this off in Settings, Launcher, "Update automatically", and check
by hand there. If a server says your launcher is too old for it, the launcher looks for the update straight away.

Updates come from this repository's GitHub releases (the beta channel for 0.5.0).

## Where your data lives

Everything the launcher writes is in your own folder, `%LOCALAPPDATA%\fzgx-launcher` (for example
`C:\Users\you\AppData\Local\fzgx-launcher`), never in the program's folder, which every update replaces:

| In it | What |
|---|---|
| `settings.json` | Your settings, including the secret your player tag comes from |
| `replays/` | Your replays (Settings, Replays can name another folder) |
| `recomp-game/` | The game's files, prepared from your disc |
| `recomp-user/` | The game's own folder: memory card, controller set-up, its log |
| `launcher.log`, `notices.json`, `cache/`, `boards/`, `race/` | The launcher's log, the server's notices, and working files it makes again |

The program itself (the launcher, the game's runner and native module, the image bundle) is in
`%LOCALAPPDATA%\Piconet.Launcher`, where Setup and the updates put it. Uninstall it from Windows' Settings, Apps;
your data folder stays.

A copy unzipped from a zip (the builds before 0.5.0) keeps its data in its own `data` folder instead.

## Layout

| Folder | What it is |
|---|---|
| `net/src/Launcher.App/` | The window (Avalonia) and the modes without one; the names players see are only in `Branding.cs` |
| `net/src/Launcher.Core/` | The launcher's logic, with no UI type |
| `net/tests/Launcher.Tests/` | Its tests (xunit.v3 on Microsoft.Testing.Platform; the window's tests run headless) |
| `net/tools/DiscPinsWriter/` | The developer's tool that writes `Launcher.Core/DiscPins.json` from a disc image |
| `external/protocol` | [piconet-protocol](https://github.com/piconet-live/piconet-protocol), a submodule: `Online.Protocol`, the generated launch, replay and status-board code, the shipped stat packs |
| `external/tools` | [piconet-tools](https://github.com/piconet-live/piconet-tools), a submodule: `Fzgx.GameCube`, built from source |

The two submodules are found through `ProtocolFolder` and `FzgxTools` in `net/Directory.Build.props`; more in
[net/README.md](net/README.md).

## Requirements

- the .NET SDK 10.0.401 or a later 10.0 patch (`net/global.json`)
- nuget.org (Avalonia 12.1.3, Huskui.Avalonia, CommunityToolkit.Mvvm, DiscordRichPresence, Velopack, xunit.v3; the versions are in `net/Directory.Packages.props`)
- Windows 10 or later to run the launcher with the game; the build and the tests also run on Linux

## Build and test

    git clone --recurse-submodules https://github.com/piconet-live/piconet-launcher.git
    cd piconet-launcher/net
    dotnet build net.slnx
    dotnet test --project tests/Launcher.Tests

The disc tests (`RealDisc*`) need your own disc image, named in a piconet-runner checkout beside this repository
(`DISC_DEFAULT` in `~/.config/piconet/pins.tsv`, read over the runner's `port/setup/pins.tsv`); without it they are
skipped, never failed.

A launcher built from this source names no server stack, so it has no official server: it shows "No server" until
you enter one in Settings, Online (your own test server, for example). The releases get their server at packaging
(`-p:ServerStack=NAME -p:StackServer=wss://HOST/ws -p:StackKind=public`; `Launcher.Core/StackStamp.cs`).

A launcher with the game beside it (runner, module, game folder) is staged by the runner's build
(`port/build/build.sh`, step 6), which publishes `net/src/Launcher.App` for `win-x64`.

### Dev mode

To work on the launcher's pages without a server or keys, start it on the stand-in service, which runs inside the
launcher:

    cd piconet-launcher/net
    dotnet run --project src/Launcher.App -- --dev-service --data ../../piconet-dev-data

`--dev-service` skips the first screen and points Quick Play at the stand-in (`net/src/Launcher.Core/StandIn`): RACE
joins a lobby of five stand-in racers who chat, vote to start, vote for a course and choose their machines. Races do
not start there (the stand-in has no race server): after the machine stage the lobby says so and forms again. No
disc, game or server is needed for that; the pictures from the disc show only with a disc image set. `--data` keeps
the dev launcher's settings and replays apart from yours (your settings file keeps your own server either way). The
switch is never on without being typed: no player's start, update or shortcut uses it, and it turns off Discord's
status and the automatic updates for that run.

## Contributing

Fork the repository, make a branch, and open a pull request; the build and the tests must pass. Please discuss
bigger changes on Piconet's [Discord](https://discord.gg/DcauFmsGh) first. The details are in
[CONTRIBUTING.md](CONTRIBUTING.md).

## Credits

The Piconet logo by Emarcee.

## Licence

GPL-3.0-or-later (`LICENSE`). The Saira fonts in `net/src/Launcher.App/Assets/Fonts/` are under the SIL Open Font
Licence 1.1 (`OFL.txt` beside them).
