using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The stat packs a practice can run on (Play's Practice, the Stats choice): the pack files that came with the
/// launcher, the folder <c>packs</c> beside the program (the repository's protocol/packs, as a server has them). Each
/// is read and checked as a server reads its pack; a file that is no valid pack is left out. A practice's match file
/// then carries the chosen pack whole (<see cref="PracticeMatch"/>), as a match start does.
/// </summary>
public static class PracticePacks
{
    /// <summary>The folder <c>packs</c> beside the launcher's program.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "packs");

    /// <summary>The valid packs of a folder's <c>.fzstats</c> files, by name; none when the folder is not there.</summary>
    public static IReadOnlyList<StatPack> Read(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }
        var packs = new List<StatPack>();
        foreach (var file in Directory.EnumerateFiles(folder, "*" + StatPackText.Extension).Order(StringComparer.Ordinal))
        {
            try
            {
                if (StatPackText.Read(File.ReadAllText(file)).Pack is { } pack &&
                    !packs.Exists(p => p.Name == pack.Name))
                {
                    packs.Add(pack);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // (a file that cannot be read is no pack to choose)
            }
        }
        return [.. packs.OrderBy(p => p.Name, StringComparer.Ordinal)];
    }
}
