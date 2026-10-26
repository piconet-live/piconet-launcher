namespace Launcher.Core;

/// <summary>What the launcher's data folder holds of the game's files.</summary>
public enum PreparedState
{
    /// <summary>Nothing: the game's files have to be prepared from the player's disc.</summary>
    NotPrepared,

    /// <summary>The game can start from them.</summary>
    Ready,

    /// <summary>Prepared for another version of the game program. With <see cref="PreparedStatus.NeedsDisc"/> false
    /// the files are intact and only the patched words differ: <see cref="DiscPrep.Refresh"/> rewrites them without
    /// the disc.</summary>
    Stale,

    /// <summary>A prepared file is missing or changed: prepare again from the disc.</summary>
    Damaged,
}

/// <summary>The state of the prepared game's files, with a sentence for the player.</summary>
/// <param name="NeedsDisc">Getting to <see cref="PreparedState.Ready"/> takes the disc image.</param>
/// <param name="File">The file a damaged state names (<c>sys/main.dol</c>, <c>files/...</c>).</param>
public sealed record PreparedStatus(PreparedState State, string Message, bool NeedsDisc, string? File = null)
{
    /// <summary>The digest of the game folder as the record has it (<see cref="DiscPrep.FolderDigest"/>), when
    /// <see cref="PreparedState.Ready"/>.</summary>
    public string? FolderSha256 { get; init; }
}

public enum DiscPrepStage
{
    /// <summary>Reading the image's header, system area and named files.</summary>
    Checking,

    /// <summary>Copying the game's files out of the image.</summary>
    Extracting,

    /// <summary>Writing the patched words and the record.</summary>
    Finishing,
}

/// <summary>How far a preparation is. Bytes are the measure to show; the file is the one being written.</summary>
public readonly record struct DiscPrepProgress(DiscPrepStage Stage, long BytesDone, long BytesTotal, int FilesDone, int FilesTotal, string? File)
{
    /// <summary>0 to 1.</summary>
    public double Fraction => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1) : 0;
}

public enum DiscPrepOutcome
{
    /// <summary>The game's files were extracted, checked and patched.</summary>
    Prepared,

    /// <summary>They were ready already: nothing was read from the image.</summary>
    AlreadyReady,

    /// <summary>They were prepared for another version of the game program and intact: the patched words were
    /// rewritten, nothing was read from the image.</summary>
    Refreshed,

    /// <summary>No file at the path.</summary>
    ImageMissing,

    /// <summary>Not a GameCube disc image at all.</summary>
    NotADisc,

    /// <summary>A disc image in a form the launcher does not read (RVZ, GCZ, NKit, an archive, ...): the message
    /// says what to do.</summary>
    UnsupportedFormat,

    /// <summary>A GameCube disc of another game, or of this game for another region.</summary>
    WrongGame,

    /// <summary>F-Zero GX (USA), but another revision or disc number than the native module was made from.</summary>
    WrongVersion,

    /// <summary>A file of the image is not the original: <see cref="DiscPrepResult.File"/> names it when it is one
    /// the launcher checks by name.</summary>
    ModifiedFile,

    /// <summary>The image is cut short or cannot be read.</summary>
    DamagedImage,

    /// <summary>Not enough free space: <see cref="DiscPrepResult.BytesNeeded"/> and <see cref="DiscPrepResult.BytesFree"/>.</summary>
    NotEnoughSpace,

    /// <summary>Stopped through the cancellation token; nothing is kept.</summary>
    Cancelled,

    /// <summary>The launcher's own fault or the computer's: the game program's patch list does not fit the game, or
    /// the data folder cannot be written.</summary>
    Failed,

    /// <summary>Another preparation is at work in the same data folder (another window of the launcher): this one
    /// did nothing.</summary>
    Busy,
}

/// <summary>How a preparation ended, with a sentence for the player.</summary>
public sealed record DiscPrepResult(DiscPrepOutcome Outcome, string Message)
{
    /// <summary>The game can start.</summary>
    public bool Ready => Outcome is DiscPrepOutcome.Prepared or DiscPrepOutcome.AlreadyReady or DiscPrepOutcome.Refreshed;

    /// <summary>The exit status of a preparation without the window (<c>Launcher.App --prepare-disc</c>), for the
    /// script that called it: 0 the files are ready; 2 the image is refused (not a disc, another game or version, a
    /// modified or damaged file); 3 not enough free space; 4 stopped; 5 the launcher's own fault or the data
    /// folder's; 6 another preparation is at work in the same data folder (nothing is wrong with the image: wait
    /// and call again).</summary>
    public int ExitStatus => Outcome switch
    {
        _ when Ready => 0,
        DiscPrepOutcome.NotEnoughSpace => 3,
        DiscPrepOutcome.Cancelled => 4,
        DiscPrepOutcome.Failed => 5,
        DiscPrepOutcome.Busy => 6,
        _ => 2,
    };

    /// <summary>The prepared game folder (<c>sys</c> and <c>files</c>) when <see cref="Ready"/>.</summary>
    public string? GameFolder { get; init; }

    /// <summary>The digest of the prepared game folder as it stands (<see cref="DiscPrep.FolderDigest"/>) when
    /// <see cref="Ready"/>: the same for every image the launcher accepts and one mod bundle.</summary>
    public string? FolderSha256 { get; init; }

    /// <summary>The file a refusal names.</summary>
    public string? File { get; init; }

    public long BytesNeeded { get; init; }

    public long BytesFree { get; init; }

    /// <summary>What a full preparation wrote.</summary>
    public long BytesWritten { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>The part of <see cref="Elapsed"/> spent hashing the files while they were copied.</summary>
    public TimeSpan Hashing { get; init; }
}

/// <summary>What a preparation may be told besides its paths (tests set the first two).</summary>
public sealed record DiscPrepOptions
{
    /// <summary>The disc that is accepted.</summary>
    public DiscPins Pins { get; init; } = DiscPins.Game;

    /// <summary>The free space where a folder is, in bytes; null: not known (the preparation then goes on and stops
    /// if the disk fills).</summary>
    public Func<string, long?> FreeSpace { get; init; } = DiscPrep.FreeSpace;

    /// <summary>Prepare from the image even when the files are ready (a repair). A copy that is there with its files
    /// intact stays until the new one is written and checked, and is replaced then; one that is damaged makes room
    /// first.</summary>
    public bool Again { get; init; }
}
