namespace Launcher.Core;

/// <summary>The courses a player picks from, by cup as the game orders them: Ruby, Sapphire, Emerald, Diamond, AX,
/// and the story's stages a race can be held on. A course picker's cards and chips (Practice; a custom lobby's
/// pool) are made from this list.</summary>
public static class CourseCups
{
    public static IReadOnlyList<string> Cups { get; } = ["Ruby", "Sapphire", "Emerald", "Diamond", "AX", "Story"];

    /// <summary>Every course, in the game's order, with its cup.</summary>
    public static IReadOnlyList<(string Cup, int Course)> All { get; } =
    [
        ("Ruby", 1), ("Ruby", 16), ("Ruby", 26), ("Ruby", 8), ("Ruby", 5),
        ("Sapphire", 14), ("Sapphire", 13), ("Sapphire", 11), ("Sapphire", 7), ("Sapphire", 3),
        ("Emerald", 15), ("Emerald", 10), ("Emerald", 29), ("Emerald", 9), ("Emerald", 27),
        ("Diamond", 24), ("Diamond", 25), ("Diamond", 17), ("Diamond", 21), ("Diamond", 28),
        ("AX", 31), ("AX", 32), ("AX", 33), ("AX", 34), ("AX", 35), ("AX", 36),
        ("Story", 39), ("Story", 43), ("Story", 44), ("Story", 45),
    ];

    /// <summary>The cup a course is in; "" for a course that is in none.</summary>
    public static string CupOf(int course) => All.FirstOrDefault(c => c.Course == course).Cup ?? "";
}
