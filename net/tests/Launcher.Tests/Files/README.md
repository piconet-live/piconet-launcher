# Launcher.Tests/Files

The tests of `Launcher.Core/Files`: the game's files prepared from a disc image, and the launch file. Every image
here is made up (`SyntheticDisc.cs`): no game file is read or written, except that
`RealDiscStatsTests.cs`, `RealDiscCourseMapTests.cs`, `RealDiscCourseStillTests.cs` and one test of `GameSoundsTests.cs` read a real disc image where this computer has it.

| File | What it tests |
|---|---|
| `SyntheticDisc.cs` | Makes the images: the same files in more than one layout |
| `DiscPrepTests.cs` | A preparation from start to end, and the class's helpers |
| `DiscPrepTests.Refusals.cs` | Every kind of file that is not the game, each refused with its sentence |
| `DiscPrepTests.Image.cs` | Images of the game laid out differently give the same folder |
| `DiscPrepTests.Hostile.cs` | An image whose own numbers lie: refused within the limits, whatever they say |
| `DiscPrepTests.Status.cs` | What the data folder holds (ready, stale, damaged, missing) and the exit status a script is told |
| `DiscPrepTests.Swap.cs` | A stopped or failed run keeps the working copy |
| `DiscPrepTests.Art.cs` | The look's readers (`DiscArt`) on images that would break them |
| `MadeUpStats.cs`, `MachineStatsTests.cs` | Made-up stat files (a car data file, a main module, its cipher); the machines' stats read from them, and none from files that are not the game's |
| `RealDiscCourseMapTests.cs` | The race page's map on your disc image: for every course of the pool, the road's centre line from the course file lies on the map (the five previews stored moved, `PreviewPlacement`); skipped where the image is not |
| `RealDiscCourseStillTests.cs` | The Practice page's course pictures on your disc image: every course it offers has its model, and the still and the turning hero hold all of it; skipped where the image is not |
| `RealDiscStatsTests.cs` | A test that reads the game: the stats of three machines from your disc image (your setting `DISC_DEFAULT`, a row of `~/.config/piconet/pins.tsv`), held to what the game's machine select shows; skipped where the image is not |
| `MadeUpSoundBank.cs`, `GameSoundsTests.cs` | A made-up GCAX sound bank; the launcher's sounds from it: DSP ADPCM decoded, a sound found with its pitch and volume, a bank cut short or changed read as none, the cache written and read back, the tones' level, the fallback without a disc; on your disc three sounds decode as a Python decoder did (counts and hashes only) |
| `LaunchConfigTests.cs` | The launch file: its settings read, everything else refused with what is wrong |
| `GameLaunchFileTests.cs` | The game's launch file as written: a player's has no `debug` part; the texts the game's reader is tested with |

A new guard comes with a made-up image that fails without it.
