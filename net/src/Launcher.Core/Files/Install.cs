namespace Launcher.Core;

/// <summary>
/// Where this launcher is, and what kind of launcher it is: an installed one, a player copy, or a development copy.
/// <para>An **installed** launcher (Setup.exe from the releases, kept up to date by Velopack)
/// has the file <see cref="InstalledMarkerName"/> beside its libraries, written by the private release script. Its
/// folder is the one its libraries are in, with the game's program in its <c>recomp</c>; Velopack replaces that
/// whole folder at every update, so nothing the launcher writes is kept there: the data is the player's own folder
/// under the local application data, as for a development copy. The game's files come only from that data folder
/// (the prepared folder), as in a player copy (<see cref="PlayerCopy"/>).</para>
/// <para>A **player copy** is a folder a player unzips anywhere, which keeps everything it writes inside itself
/// (<c>port/build/package.py</c> makes one). It has one program at its top, started with a double-click and no
/// command line, and everything that program is made of in the folder <see cref="ProgramFolder"/> beside it. So
/// nobody can tell the launcher that it is a copy; it finds out: the package script writes the file
/// <see cref="MarkerName"/> into <see cref="ProgramFolder"/>, beside the launcher's libraries, and a launcher that
/// finds it there is a copy. The copy's folder is then the one above (the game's program is in its <c>recomp</c>),
/// and its data folder is <see cref="DataName"/> in it. An installed launcher can take such a copy's data over once
/// (<see cref="DataMove"/>).</para>
/// <para>Without either file nothing changes: the launcher's folder is the one its libraries are in (a development
/// copy from <c>port/build/stage-launcher.sh</c> has <c>recomp</c> there), and the data is the player's own folder
/// under the local application data. <c>--data FOLDER</c> names the data folder in every case.</para>
/// </summary>
/// <param name="Folder">The launcher's folder: <c>recomp</c>, with the game's program, is looked for in it.</param>
/// <param name="DataFolder">The data folder of a player copy; null: this is none, and the data is the player's
/// own folder.</param>
/// <param name="Installed">An installed launcher (<see cref="InstalledMarkerName"/>): its data is the player's own
/// folder (<paramref name="DataFolder"/> is null), its game files the prepared folder's only.</param>
public sealed record Install(string Folder, string? DataFolder, bool Installed = false)
{
    /// <summary>The file that makes a folder a player copy. What it holds is for a person who opens it; the launcher
    /// looks only whether it is there.</summary>
    public const string MarkerName = "portable-copy.txt";

    /// <summary>The file that makes a folder an installed launcher's (the release script writes it beside the
    /// libraries, in place of <see cref="MarkerName"/>). As with that one, only whether it is there counts.</summary>
    public const string InstalledMarkerName = "installed-copy.txt";

    /// <summary>The folder of a player copy that holds the launcher's libraries, and the marker.</summary>
    public const string ProgramFolder = "app";

    /// <summary>A player copy's data folder, in the copy.</summary>
    public const string DataName = "data";

    /// <summary>This is a player copy or an installed launcher: a launcher a player has, whose game files are the
    /// prepared folder's and no other (<see cref="RecompLaunch.Resolve(RecompSettings?, string, string, DiscPins?, bool)"/>).</summary>
    public bool PlayerCopy => DataFolder is not null || Installed;

    /// <summary>A player copy that keeps its data in itself (a portable one, as every copy was before 0.5.0).</summary>
    public bool Portable => DataFolder is not null;

    /// <summary>What a player is told once when an older copy's files stand in this copy.</summary>
    public const string OlderCopyNotice =
        "Older copy's files in this folder · unzip the new copy into its own folder, move only \"data\"";

    /// <summary>What the first build's copy had at its top (the launcher itself, and its two start files) and in
    /// its <c>recomp</c> (the game's files, made by hand): none of them is in a copy of this layout.</summary>
    private static readonly string[] OfAnOlderCopy =
        ["Launcher.App.exe", "Launcher.App.dll", "2 Play.cmd", "1 Prepare disc - drag your ISO onto me.cmd"];

    /// <summary><see cref="OlderCopyNotice"/> when this player copy was unzipped over an older one, or files of
    /// one were carried into it; null: nothing of the kind, or this is no player copy. The launcher uses none of
    /// those files (<see cref="RecompLaunch.Resolve(RecompSettings?, string, string, DiscPins?, bool)"/>): the
    /// notice is so that the player knows the folder is a mix.</summary>
    public string? OlderCopy() =>
        Portable && (OfAnOlderCopy.Any(name => File.Exists(Path.Combine(Folder, name))) ||
                       Directory.Exists(Path.Combine(Folder, "recomp", RecompLaunch.CarriedGame)))
            ? OlderCopyNotice : null;

    /// <summary>This launcher's.</summary>
    public static Install This { get; } = Of(AppContext.BaseDirectory);

    /// <summary>The installation whose libraries are in <paramref name="libraries"/> (the running launcher's:
    /// <see cref="AppContext.BaseDirectory"/>).</summary>
    public static Install Of(string libraries)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraries);
        var here = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraries));
        if (File.Exists(Path.Combine(here, InstalledMarkerName)))
        {
            return new Install(here, null, Installed: true);
        }
        return File.Exists(Path.Combine(here, MarkerName)) && Path.GetDirectoryName(here) is { Length: > 0 } copy
            ? new Install(copy, Path.Combine(copy, DataName))
            : new Install(here, null);
    }
}
