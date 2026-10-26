# net

The launcher, in C# on .NET 10: the program a player opens (Avalonia), its logic, its tests and the developer's
tool that writes the disc's pins. Warnings are errors. The protocol's project comes from the submodule
`../external/protocol`, `Fzgx.GameCube` from `../external/tools`.

| Folder or file | What it is |
|---|---|
| `src/Launcher.Core/` | The launcher's logic, with no UI type |
| `src/Launcher.App/` | The program a player opens: the launcher's window (Avalonia) |
| `tests/Launcher.Tests/` | The launcher's tests |
| `tests/TestTimeLimit.cs` | A time limit for every test, compiled into each test project |
| `net.slnx` | The solution: every project above |
| `Directory.Build.props` | The settings every project shares (the framework, warnings as errors, the version) |
| `Directory.Packages.props` | Every package's version, in one place |
| `.editorconfig`, `tests/.editorconfig` | Code style and analyzer severities; the code metrics CA1502 (method complexity) and CA1506 (class coupling) are on, CA1506 not in the tests |
| `CodeMetricsConfig.txt`, `CodeMetrics.globalconfig` | The code metrics' limits (25; 95 a type, 40 a member), and CA1509, which checks that file |
| `global.json` | The .NET SDK's version and the test runner |
| `switches.tsv` | The switch registry's rows for the shared test files (the projects' own are in their folders) |
| `tools/DiscPinsWriter/` | The developer's tool that writes `Launcher.Core/DiscPins.json` from your disc image (not shipped) |

    cd net
    dotnet test --project tests/Launcher.Tests
    dotnet test --project tests/Launcher.Tests --filter-class Launcher.Tests.CoreTests

From `net/` (its `global.json` runs the tests on Microsoft.Testing.Platform). The tests that read your disc image
find it through the runner's `port/setup/pins.tsv` (`RunnerPins` in `Directory.Build.props`: a piconet-runner
checkout beside this repository) and are skipped without it.
