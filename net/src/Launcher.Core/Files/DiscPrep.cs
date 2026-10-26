using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace Launcher.Core;

/// <summary>
/// Prepares the game's files from the player's own disc image (the
/// release holds the launcher, the runner and the compiled module, and no game file).
/// The image is checked against <see cref="DiscPins"/>, its <c>sys</c> and <c>files</c> are written into the
/// launcher's data folder as the runner boots them, and the words the native module needs are written into that copy,
/// never into the image: the pins, and the executable's words of the mod bundle's patch list.
/// <para>What is checked, in this order, each refusal naming what was found: the kind of file; the game's id, disc
/// number and revision; the SHA-256 of the system files and of the files the native module was made from (every
/// <c>fze.*.rel</c> and <c>enemy_line/line__.bin</c>, which main.rel is decrypted from); that the image's files are
/// the game's by name and size, with no other beside them; the free space; and, while the files are copied, the
/// SHA-256 of every file, gathered into one digest that must equal the pinned one. Every file is hashed because the
/// game is played online in lockstep: a changed course or machine file is a race that goes apart, and the hashing
/// is nearly free, the bytes being in memory for the copy (0.7 s for the game's 1.43 GB on one PC, of a
/// copy that took 33 s). Names and sizes alone would let a changed file of the same size through.</para>
/// <para>Where an image has its files is not checked (the same files are the
/// same game): the file table's own bytes, the files' places and order, and the filler between them may be any, so
/// an image that was rebuilt or packed closer is taken when every file in it is intact. What is written does not
/// depend on it either. Two system files say where things lie, and both are written in one form:
/// <c>sys/boot.bin</c> with the pinned words at <see cref="DiscImage.LayoutAt"/> (the original pressing's), and
/// <c>sys/fst.bin</c> made anew from the names and sizes (<see cref="DiscImage.CanonicalTable"/>); their pins are
/// of that form, which is how the header's other bytes and the names are checked. So every accepted image gives
/// the same game folder byte for byte, and the record keeps its digest (<see cref="FolderDigest"/>). It matters
/// beyond tidiness: the runner builds the disc it boots from the folder (every file under <c>files</c> in name
/// order, 32 KiB apart, and the header with its last four layout words as the folder has them), and when a file
/// is read follows from where it lies, so two players' discs must be the same disc.</para>
/// <para>The words. The pins are two (<see cref="DiscPins.Words"/>): one in the executable and one in
/// <c>files/fze.sample.rel</c>, the loader that decrypts main.rel and chooses its address. The native bundle's list
/// names the executable's words only (the pin again, and the branches into the mod image, which change with every
/// image), so the sample module's word is the launcher's to write: without it main.rel loads at an address that
/// moves with the clock, and none of the native module's main.rel code is used. No stock copy is kept beside a
/// patched file (the hand-prepared folder had <c>*.orig</c>): the runner builds the disc it boots from every file
/// under <c>files</c>, sorted by name, so an extra file there moves each file after it; the record keeps the stock
/// words instead.</para>
/// <para>Layout under the data folder: <c>recomp-game/GFZE01/sys</c> and <c>files</c>, and beside the game folder
/// <c>recomp-game/prepared.json</c>, the record: which disc, which words, the hashes of the named files as they
/// stand. The game folder is written under another name and renamed when it is complete, and the record is written
/// last, so a preparation that was interrupted leaves nothing that looks ready.</para>
/// </summary>
public static partial class DiscPrep
{
    /// <summary>The folder under the launcher's data folder.</summary>
    public const string FolderName = "recomp-game";

    /// <summary>The record of what was prepared.</summary>
    public const string RecordName = "prepared.json";

    /// <summary>The record's format; another number is prepared again.</summary>
    public const int RecordFormat = 2;

    /// <summary>The native bundle's patch list (port/image/build.py).</summary>
    public const string PatchListName = "patches.json";

    public const string NotPreparedMessage = "Game files not prepared";

    /// <summary>The file a preparation holds open while it works, beside the record: a second one finds it held.</summary>
    public const string LockName = "preparing.lock";

    public const string BusyMessage = "Game files being prepared · another launcher window";

    /// <summary>After the game folder's name: the new copy until it is checked.</summary>
    private const string Staging = ".preparing";

    /// <summary>After the game folder's name: the copy that worked, for the moment the new one takes its name.</summary>
    private const string Old = ".old";

    private const string Header = "sys/boot.bin";
    private const string Executable = "sys/main.dol";
    private const string Table = "sys/fst.bin";
    private const long Margin = 64L << 20;

    /// <summary>Characters no file of the game has in its name, and Windows would not take.</summary>
    private static readonly System.Buffers.SearchValues<char> NotInAName = System.Buffers.SearchValues.Create("\\/:*?\"<>|");

    /// <summary>Where the prepared game is (or will be): <c>sys</c> and <c>files</c> are inside.</summary>
    public static string GameFolder(string dataFolder, DiscPins? pins = null) =>
        Path.Combine(dataFolder, FolderName, (pins ?? DiscPins.Game).GameId);

    /// <summary>The record's file.</summary>
    public static string RecordFile(string dataFolder) => Path.Combine(dataFolder, FolderName, RecordName);

    /// <summary><see cref="Prepare"/> on another thread, for a screen: progress comes through
    /// <paramref name="progress"/> (a <see cref="Progress{T}"/> made on the screen's thread brings it there), and
    /// cancelling <paramref name="cancel"/> ends it with <see cref="DiscPrepOutcome.Cancelled"/>.</summary>
    public static Task<DiscPrepResult> PrepareAsync(string? imagePath, string dataFolder, string? bundle,
        IProgress<DiscPrepProgress>? progress = null, DiscPrepOptions? options = null, CancellationToken cancel = default) =>
        Task.Run(() => Prepare(imagePath, dataFolder, bundle, progress, options, cancel), CancellationToken.None);

    /// <summary>Makes the game's files ready in <paramref name="dataFolder"/> from the disc image at
    /// <paramref name="imagePath"/>: nothing to do when they are ready, the words rewritten when only they are
    /// another version's, else the whole preparation (the class's summary says what is checked). Every ending is a
    /// result; this never throws for anything the image, the disk or a cancellation does.</summary>
    /// <param name="bundle">The mod bundle in use (<see cref="RecompPaths.Bundle"/>).</param>
    public static DiscPrepResult Prepare(string? imagePath, string dataFolder, string? bundle,
        IProgress<DiscPrepProgress>? progress = null, DiscPrepOptions? options = null, CancellationToken cancel = default)
    {
        options ??= new DiscPrepOptions();
        var pins = options.Pins;
        var clock = Stopwatch.StartNew();
        var game = GameFolder(dataFolder, pins);
        var staging = game + Staging;
        FileStream? only = null;
        try
        {
            if ((only = Only(dataFolder)) is null)
            {
                return new DiscPrepResult(DiscPrepOutcome.Busy, BusyMessage) { Elapsed = clock.Elapsed };
            }
            Recover(game);
            var status = Status(dataFolder, bundle, pins);
            if (!options.Again && status.State == PreparedState.Ready)
            {
                return new DiscPrepResult(DiscPrepOutcome.AlreadyReady, status.Message) { GameFolder = game, FolderSha256 = status.FolderSha256, Elapsed = clock.Elapsed };
            }
            if (!options.Again && status is { State: PreparedState.Stale, NeedsDisc: false })
            {
                if (Refreshed(dataFolder, bundle, pins) is { Ready: true } refreshed)
                {
                    return refreshed;
                }
                status = Status(dataFolder, bundle, pins);
            }
            // a copy whose files are intact stays until the new one is checked; anything else makes room first
            var keep = status.State == PreparedState.Ready || status is { State: PreparedState.Stale, NeedsDisc: false };
            return Extract(imagePath, dataFolder, bundle, staging, progress, options, keep, cancel) with { Elapsed = clock.Elapsed };
        }
        catch (Stop stop)
        {
            Remove(staging);
            return stop.Result with { Elapsed = clock.Elapsed };
        }
        catch (OperationCanceledException)
        {
            Remove(staging);
            return new DiscPrepResult(DiscPrepOutcome.Cancelled, "Stopped · nothing kept") { Elapsed = clock.Elapsed };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Remove(staging);
            return (DiskFull(e)
                ? new DiscPrepResult(DiscPrepOutcome.NotEnoughSpace, $"The disk filled up while the game's files were written to {dataFolder}. Free some space and try again.")
                : new DiscPrepResult(DiscPrepOutcome.Failed, $"The game's files could not be written to {dataFolder}: {e.Message}"))
                with { Elapsed = clock.Elapsed };
        }
        finally
        {
            only?.Dispose();
        }
    }

    /// <summary>The one preparation at a time in a data folder: <see cref="LockName"/> held open and shared with
    /// nobody until the preparation ends (the system lets go of it when a launcher dies, and the file goes with
    /// it). Null: another preparation holds it.</summary>
    /// <exception cref="IOException">The folder cannot be written.</exception>
    private static FileStream? Only(string dataFolder)
    {
        var path = Path.Combine(dataFolder, FolderName, LockName);
        Directory.CreateDirectory(Path.Combine(dataFolder, FolderName));
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException) when (File.Exists(path))
        {
            return null;
        }
    }

    /// <summary>What a preparation that was stopped half-way left behind (the launcher closed, the power cut), put
    /// right before the next one looks: its unfinished copy is removed, and a copy that worked and had just been
    /// moved aside for the new one gets its name back when the new one never took it.</summary>
    private static void Recover(string game)
    {
        Remove(game + Staging);
        if (!Directory.Exists(game + Old))
        {
            return;
        }
        if (Directory.Exists(game))
        {
            Remove(game + Old);
            return;
        }
        try
        {
            Directory.Move(game + Old, game);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // held by another program: it stays aside, and the state says the game's files are missing
        }
    }

    /// <summary>The free space on the disk a folder is (or will be) on; null when it cannot be asked.</summary>
    public static long? FreeSpace(string folder)
    {
        try
        {
            var at = Path.GetFullPath(folder);
            while (!Directory.Exists(at) && Path.GetDirectoryName(at) is { Length: > 0 } parent)
            {
                at = parent;
            }
            return new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(at)! : at).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A size as Windows shows it: "1.34 GB", "64 MB".</summary>
    public static string Size(long bytes) => bytes >= 1L << 30
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.00} GB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, bytes >> 20)} MB");

    private static string Local(string root, string name) => Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));

    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>A refusal on its way out of the preparation.</summary>
    private sealed class Stop(DiscPrepResult result) : Exception(result.Message)
    {
        public DiscPrepResult Result { get; } = result;
    }
}
