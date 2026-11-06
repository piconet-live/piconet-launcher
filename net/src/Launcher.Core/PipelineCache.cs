using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Launcher.Core;

/// <summary>
/// Aurora's graphics cache for the look in use: every shader of the game's models compiled once, ahead of any race, so
/// that no race ever compiles one (a shader compiled in a race is a draw left out of the picture: a piece "loads in").
/// The runner builds it (<c>moderngekko-run.exe --build-pipelines</c>, piconet-runner's pipelines_build.cpp): no game, no
/// window that can take focus, below normal priority, its progress as lines of JSON. What it leaves is Dawn's cache in
/// the game's user folder (<c>Aurora/dawn_cache.db</c>), which every later game reads instead of compiling.
/// <para>A look's cache is ready when its stamp file holds the stamp the launcher works out now (<see cref="Want"/>):
/// the look (plain, or ray traced: their shaders differ), Aurora itself (its library and the pipeline seed beside the
/// runner: a new build compiles other shaders), the graphics drivers (a driver's compiled code is its own), and the mods
/// that are on (their models are planned too). The launcher writes the stamp when a build has said it is done.</para>
/// <para>When Aurora or the drivers changed, what Dawn's cache holds is of no use any more: it is emptied first
/// (<see cref="Clear"/>), with every look's stamp. A change of the look or of the mods only adds to it.</para>
/// </summary>
public static class PipelineCache
{
    /// <summary>The look with ray tracing off: Aurora's picture as the game draws it.</summary>
    public const string Plain = "plain";

    /// <summary>The look with ray tracing on (the runner's tested set of ray switches).</summary>
    public const string Rays = "rays";

    private static readonly string[] Looks = [Plain, Rays];

    /// <summary>Aurora's files beside the runner a build depends on: its library and its pipeline seed.</summary>
    private static readonly string[] AuroraFiles = ["aurora-present.dll", "initial_pipeline_cache.db"];

    /// <summary>What <see cref="Clear"/> deletes in Aurora's folder.</summary>
    private static readonly string[] Cleared = ["dawn_cache.db", "dawn_cache.db-wal", "dawn_cache.db-shm", "pipelines_built",
        $"pipelines-{Plain}.stamp", $"pipelines-{Rays}.stamp"];

    /// <summary>The runner's word for the build (piconet-runner switches.cpp).</summary>
    public const string BuildArgument = "--build-pipelines";

    /// <summary>Aurora's folder in the game's user folder (the runner's aurora.cpp: its pipeline and Dawn caches).</summary>
    public static string Folder(RecompPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.UserFolder, "Aurora");
    }

    /// <summary>Where a look's stamp is kept once its build is done.</summary>
    public static string StampFile(RecompPaths paths, string look) => Path.Combine(Folder(paths), $"pipelines-{look}.stamp");

    /// <summary>The build's launch file: the offline game's, as a game's start writes it, beside the game's own.</summary>
    public static string LaunchFile(RecompPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.UserFolder, "pipelines-launch.json");
    }

    /// <summary>Whether Aurora draws the games this runner starts: its library beside the runner. Without it the runner draws
    /// the emulator's own pictures (piconet-runner's aurora.cpp, "the library is missing"), which need no cache. Reads the
    /// runner's folder: off the window's thread.</summary>
    public static bool AuroraDraws(RecompPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var beside = Path.GetDirectoryName(Path.GetFullPath(paths.Runner)) ?? ".";
        return File.Exists(Path.Combine(beside, AuroraFiles[0]));
    }

    /// <summary>The stamp a look's cache must have now. It reads Aurora's two files beside the runner and the mods' files'
    /// sizes and times: off the window's thread.</summary>
    /// <param name="drivers">The graphics drivers' words (<see cref="Drivers"/>; a test's own).</param>
    public static PipelineStamp Want(RecompPaths paths, string look, string drivers)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var beside = Path.GetDirectoryName(Path.GetFullPath(paths.Runner)) ?? ".";
        var aurora = Hash(w =>
        {
            foreach (var name in AuroraFiles)
            {
                w.Write(name);
                var file = Path.Combine(beside, name);
                if (File.Exists(file))
                {
                    using var s = File.OpenRead(file);
                    w.Write(Convert.ToHexString(SHA256.HashData(s)));
                }
            }
        });
        var mods = Hash(w =>
        {
            foreach (var f in paths.Mods?.Offline ?? [])
            {
                var info = new FileInfo(f.From);
                w.Write(f.Disc);
                w.Write(f.From);
                w.Write(info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) + "/" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "-");
            }
        });
        return new PipelineStamp(look, aurora, Hash(w => w.Write(drivers)), mods);
    }

    /// <summary>The stamp of a look's last finished build; null: none.</summary>
    public static PipelineStamp? Built(RecompPaths paths, string look)
    {
        try
        {
            return File.Exists(StampFile(paths, look)) ? PipelineStamp.Parse(File.ReadAllText(StampFile(paths, look))) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Why the look's cache has to be built (null: it is ready): never built, or what changed since.</summary>
    public static PipelineWhy? Why(RecompPaths paths, PipelineStamp want)
    {
        ArgumentNullException.ThrowIfNull(want);
        if (Built(paths, want.Look) is not { } built)
        {
            // another look built with this Aurora and these drivers: the same words as a first build
            return PipelineWhy.First;
        }
        return built == want ? null
            : built.Aurora != want.Aurora ? PipelineWhy.Aurora
            : built.Drivers != want.Drivers ? PipelineWhy.Drivers
            : PipelineWhy.Mods;
    }

    /// <summary>Some look was built with another Aurora or other drivers than <paramref name="want"/>'s: Dawn's cache holds
    /// nothing a game can use, and is emptied before the build (<see cref="Clear"/>).</summary>
    public static bool Stale(RecompPaths paths, PipelineStamp want)
    {
        ArgumentNullException.ThrowIfNull(want);
        return Looks.Select(look => Built(paths, look))
            .Any(b => b is not null && (b.Aurora != want.Aurora || b.Drivers != want.Drivers));
    }

    /// <summary>Dawn's cache emptied, with Aurora's mark that it was built and every look's stamp (Aurora's own
    /// pipeline list stays: it is what the game drew, and is compiled again from it).</summary>
    public static void Clear(RecompPaths paths)
    {
        var folder = Folder(paths);
        foreach (var name in Cleared)
        {
            File.Delete(Path.Combine(folder, name));
        }
    }

    /// <summary>A look's build is done: its stamp kept.</summary>
    public static void Mark(RecompPaths paths, PipelineStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        Directory.CreateDirectory(Folder(paths));
        File.WriteAllText(StampFile(paths, stamp.Look), stamp.Text);
    }

    /// <summary>The display drivers this PC has, each one's name and version (Windows keeps them under the display
    /// adapters' class key): a driver update changes them. Elsewhere none.</summary>
    public static string Drivers()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "";
        }
        try
        {
            using var display = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (display is null)
            {
                return "";
            }
            var found = new List<string>();
            foreach (var name in display.GetSubKeyNames().Where(n => n.All(char.IsAsciiDigit)).Order(StringComparer.Ordinal))
            {
                using var adapter = display.OpenSubKey(name);
                if (adapter?.GetValue("DriverVersion") is string version)
                {
                    found.Add($"{adapter.GetValue("DriverDesc")} {version}");
                }
            }
            return string.Join("; ", found);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return "";
        }
    }

    /// <summary>The runner's arguments for a build: where the game and its user folder are, the build's launch file. No
    /// <c>--fzgx-log</c>: the build's standard output is its progress, read by the launcher (its log is the user folder's
    /// <c>Logs/pipelines.log</c>).</summary>
    public static IReadOnlyList<string> Arguments(RecompPaths paths, string launchFile)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return ["--game", paths.GameFolder, "--module", paths.Module, "--user-dir", paths.UserFolder, "--no-mods",
            "--config", launchFile, BuildArgument];
    }

    /// <summary>One line of the build's output: its progress (0 to 1), its end, or nothing of the build's (the runner's own
    /// lines before them).</summary>
    public static PipelineLine Read(string? line)
    {
        if (line is null || !line.StartsWith('{'))
        {
            return default;
        }
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number)
            {
                return new PipelineLine(Math.Clamp(p.GetDouble(), 0, 1), false, null);
            }
            if (root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True)
            {
                return new PipelineLine(1, true, null);
            }
            if (root.TryGetProperty("failed", out var f))
            {
                return new PipelineLine(null, false, f.ValueKind == JsonValueKind.String ? f.GetString() ?? "" : "");
            }
        }
        catch (JsonException)
        {
        }
        return default;
    }

    /// <summary>Runs a build of the launch file's look to its end: the runner with no console, its standard input held
    /// open (the build ends when it closes: the launcher went away), its output read line by line. Cancelled: the runner
    /// is ended (what it compiled stays in Dawn's cache).</summary>
    public static async Task<PipelineBuildEnd> RunAsync(RecompPaths paths, string launchText, IProgress<double>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var launchFile = LaunchFile(paths);
        Directory.CreateDirectory(paths.UserFolder);
        await File.WriteAllTextAsync(launchFile, launchText, cancel).ConfigureAwait(false);
        var info = RecompLaunch.StartInfo(paths, Arguments(paths, launchFile));
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new PipelineBuildEnd(false, "The game did not start");
        }
        if (process is null)
        {
            return new PipelineBuildEnd(false, "The game did not start");
        }
        using (process)
        {
            using var stop = cancel.Register(() =>
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    // it had ended
                }
            });
            string? failed = null;
            var done = false;
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                var read = Read(line);
                if (read.Progress is { } fraction)
                {
                    progress?.Report(fraction);
                }
                done |= read.Done;
                failed ??= read.Failed;
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            return done && process.ExitCode == 0 ? new PipelineBuildEnd(true, null)
                : new PipelineBuildEnd(false, failed ?? $"The build ended with {process.ExitCode}");
        }
    }

    private static string Hash(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var w = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            write(w);
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()))[..16].ToLowerInvariant();
    }
}

/// <summary>What a look's cache was built for (<see cref="PipelineCache.Want"/>): each part a short hash.</summary>
public sealed record PipelineStamp(string Look, string Aurora, string Drivers, string Mods)
{
    /// <summary>The stamp file's text: a part a line.</summary>
    public string Text => $"look={Look}\naurora={Aurora}\ndrivers={Drivers}\nmods={Mods}\n";

    /// <summary>A stamp file's text; null: none (a part missing).</summary>
    public static PipelineStamp? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        return parts.TryGetValue("look", out var look) && parts.TryGetValue("aurora", out var aurora) &&
               parts.TryGetValue("drivers", out var drivers) && parts.TryGetValue("mods", out var mods)
            ? new PipelineStamp(look, aurora, drivers, mods)
            : null;
    }
}

/// <summary>Why a look's cache is built (<see cref="PipelineCache.Why"/>).</summary>
public enum PipelineWhy
{
    /// <summary>Never built for this look.</summary>
    First,

    /// <summary>Aurora changed (a new build of the game's picture, or its pipeline seed).</summary>
    Aurora,

    /// <summary>A graphics driver changed.</summary>
    Drivers,

    /// <summary>The mods that are on changed.</summary>
    Mods,
}

/// <summary>A line of the build's output: <paramref name="Progress"/> 0 to 1, or the build's end (<paramref name="Done"/>,
/// or <paramref name="Failed"/> with why); all empty: not a line of the build's.</summary>
public readonly record struct PipelineLine(double? Progress, bool Done, string? Failed);

/// <summary>How a build ended: done, or why not.</summary>
public sealed record PipelineBuildEnd(bool Done, string? Why);
