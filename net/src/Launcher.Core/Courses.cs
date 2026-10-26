namespace Launcher.Core;

/// <summary>Course names and the courses a server races by default (Online.Protocol's
/// <see cref="Online.Protocol.CourseNames"/> and <see cref="Online.Protocol.CoursePool"/>, shared with the server).</summary>
public static class Courses
{
    public static string Name(int course) => Online.Protocol.CourseNames.Name(course);

    /// <summary>The courses a server races by default (its vote's pool), in course-number order.</summary>
    public static IReadOnlyList<int> Pool => Online.Protocol.CoursePool.Default;
}
