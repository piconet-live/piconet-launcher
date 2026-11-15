using System.Numerics;

namespace Launcher.Core;

/// <summary>
/// Where the camera stands at one moment of the showcase (the redesigned hub's machine behind the page): turned
/// <see cref="Yaw"/> radians about the machine's upright axis (0 shows its tail, π its nose: <see cref="StudioCamera"/>),
/// <see cref="Pitch"/> radians above it, <see cref="Zoom"/> as close (1: the whole machine fits the view's shorter side),
/// looking at <see cref="Focus"/>: a point of the machine's box, each axis from -1 to 1 (x across, y up, z from the nose
/// at -1 to the tail at 1). <see cref="Shown"/>: how much of the picture shows, 0 at a cut between two shots;
/// <see cref="Boost"/>: how far the machine's engines are boosting (<see cref="EngineFlames"/>), 0 at rest.
/// </summary>
public readonly record struct ShowcaseCamera(float Yaw, float Pitch, float Zoom, Vector3 Focus, float Shown = 1, float Boost = 0);

/// <summary>One shot: <see cref="Seconds"/> long, the camera moving from <see cref="From"/> to <see cref="To"/>, easing
/// in and out; with <see cref="Boost"/> the engines boost from a third of the way in.</summary>
public readonly record struct ShowcaseShot(string Name, float Seconds, ShowcaseCamera From, ShowcaseCamera To, float Boost = 0);

/// <summary>
/// The showcase's shots, as an advertisement for a machine is filmed, close in (never the whole machine at once): three
/// quarters from the front, low, then close on its nose from low down, over its cockpit from above, on its boosters
/// from behind (as they fire into a boost), and drawing back; each shot a slow move of its own, in slow motion (the
/// camera turns at most <see cref="SlowestTurn"/> radians a second about the machine, easing in and out of it), the
/// picture dissolving between two (<see cref="Cut"/> seconds out and in again), round and round. No shot runs along
/// the machine (the tracking shot alongside it, nose to tail, looked as if the machine were dragged across the
/// screen, not driven: the owner, 2026-11-15). Plain maths: drawn by <c>Launcher.App/Controls/MachinePreview.cs</c>.
/// </summary>
public static class ShowcaseShots
{
    /// <summary>The dissolve's seconds at each end of a shot: long and soft.</summary>
    public const float Cut = 1.4f;

    /// <summary>The most a shot turns the camera about the machine, in radians a second on average (about 1.5 degrees):
    /// slow motion, a camera on a dolly.</summary>
    public const float SlowestTurn = 0.026f;

    public static IReadOnlyList<ShowcaseShot> Shots { get; } =
    [
        new("three quarters from the front, low, pushing in",
            15, new(3.62f, 0.12f, 1.6f, new(0.1f, 0.05f, -0.15f)), new(3.9f, 0.1f, 1.85f, new(0.05f, 0.08f, -0.22f))),
        new("the nose, low down",
            14, new(3.08f, 0.04f, 2.8f, new(0, -0.1f, -0.62f)), new(3.24f, 0.07f, 3.3f, new(0, -0.1f, -0.7f))),
        new("over the cockpit",
            14, new(2.3f, 0.76f, 2.7f, new(0, 0.4f, -0.12f)), new(2.56f, 0.66f, 3.0f, new(0, 0.36f, -0.06f))),
        new("the boosters, from behind",
            14, new(0.18f, 0.18f, 2.7f, new(0, 0.05f, 0.72f)), new(-0.06f, 0.24f, 3.05f, new(0, 0.05f, 0.76f)), Boost: 1),
        new("drawing back",
            16, new(5.9f, 0.34f, 3.2f, new(0.25f, 0.18f, 0.3f)), new(6.26f, 0.24f, 1.75f, new(0, 0.05f, -0.05f))),
    ];

    /// <summary>All the shots' seconds: then they begin again.</summary>
    public static float Length { get; } = Shots.Sum(s => s.Seconds);

    /// <summary>The still under Reduced motion: the first shot, its middle, nothing moving.</summary>
    public static ShowcaseCamera Still => At(Shots[0].Seconds / 2);

    /// <summary>The camera <paramref name="seconds"/> into the showcase.</summary>
    public static ShowcaseCamera At(float seconds)
    {
        var t = seconds % Length;
        if (t < 0)
        {
            t += Length;
        }
        foreach (var shot in Shots)
        {
            if (t < shot.Seconds)
            {
                return Within(shot, t);
            }
            t -= shot.Seconds;
        }
        return Within(Shots[^1], Shots[^1].Seconds);
    }

    private static ShowcaseCamera Within(ShowcaseShot shot, float t)
    {
        var u = Drift(Math.Clamp(t / shot.Seconds, 0, 1));
        var (a, b) = (shot.From, shot.To);
        var shown = Ease(Math.Clamp(Math.Min(t, shot.Seconds - t) / Cut, 0, 1));
        var boost = shot.Boost * Ease(Math.Clamp(((t / shot.Seconds) - 0.3f) / 0.15f, 0, 1));
        return new ShowcaseCamera(Lerp(a.Yaw, b.Yaw, u), Lerp(a.Pitch, b.Pitch, u), Lerp(a.Zoom, b.Zoom, u),
            Vector3.Lerp(a.Focus, b.Focus, u), shown, boost);
    }

    /// <summary>Slow out, slow in, as a camera on a dolly moves, but never quite still: the move goes on through the
    /// dissolve. Weighted to the ease, so that a shot gathers and settles as a slow-motion one does.</summary>
    private static float Drift(float u) => (0.4f * u) + (0.6f * Ease(u));

    private static float Ease(float u) => u * u * (3 - (2 * u));

    private static float Lerp(float a, float b, float u) => a + ((b - a) * u);
}
