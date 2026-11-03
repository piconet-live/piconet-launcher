# fzgx-coli (vendored)

The course file readers of the stage editor's `fzgx-tools` (`src/Fzgx.Coli`, GPL-3.0-or-later like the launcher),
copied unchanged from its commit f4b26ce (2026-10-20): `ColiScene.cs`, `ColiSceneReader.cs`, `ColiTrack.cs`,
`ColiTrackReader.cs`. They read an unpacked `stage/COLI_COURSENN.lz`: the header, the placed objects and the track
(nodes, checkpoints, segment trees). They need only `Fzgx.GameCube.Io.BigEndianReader`, which the launcher already
builds from `external/tools`.

`Launcher.Core` compiles them (`Launcher.Core.csproj`); the mod guard (`Launcher.Core/Files/ModGuard.cs`) uses them to
say what part of a mod's course file differs from the disc's. To update, copy the four files again from a newer
commit and change the commit named here. The library lives on in the stage editor; if `piconet-tools` ever takes
`Fzgx.Coli` in, this folder goes and the project is referenced from there instead.
