namespace Launcher.Core;

/// <summary>One option's part of the draw's strip: where it begins and how wide it is, the strip being 1 wide, and
/// its slots: one a vote (one when nobody voted), side by side and alike, the steps the draw's frame takes on it.</summary>
public sealed record DrawPart(int Option, double Start, double Width, int Slots = 1)
{
    public double End => Start + Width;
}

/// <summary>One step of the draw: the option the frame is on, which of its votes (0 first), and the step's place
/// along the whole strip (0 first), so that two votes of one card are two steps.</summary>
public sealed record DrawStep(int Option, int Vote, int Index);

/// <summary>The draw's frame as it steps: the step it is on, and a tick for every step it moves to (the first
/// included), at most one a frame.</summary>
public sealed class DrawFrame
{
    public DrawStep? Step { get; private set; }

    /// <summary>The frame is now on <paramref name="step"/> (null: off every card); true if that is a step it was
    /// not on, which ticks.</summary>
    public bool Move(DrawStep? step)
    {
        var moved = step is not null && step != Step;
        Step = step;
        return moved;
    }
}

/// <summary>
/// The course vote's draw as the launcher shows it. The server draws with one ticket a vote (every option alike when
/// nobody voted) and tells the result; this only stages it, so that the picture is the lottery it was: each option's
/// chance as a percentage, a strip whose parts are as wide as the options' chances, and a pointer that runs along the
/// strip, slows down and stops inside the part of the option drawn. An option with three votes has a part three
/// times as wide as one with one, and the pointer is over it three times as long on every pass. As Overwatch's map
/// vote does, the frame steps through the votes, not the cards: each vote is a slot of its option's part, and every
/// slot the pointer enters is a step that ticks, so a card with three votes is stepped on three times a pass.
/// </summary>
public static class CourseDraw
{
    /// <summary>How often the pointer passes the whole strip before it stops.</summary>
    public const int Passes = 3;

    /// <summary>How long the pointer runs, and how long it then stands on the option drawn. Together under the 4 s
    /// a server waits between the result and the machine stage.</summary>
    public const int RunMs = 2800;

    public const int HoldMs = 900;

    /// <summary>Each option's chance of being drawn, 0 to 1: its share of the votes; all alike when nobody voted.</summary>
    public static IReadOnlyList<double> Chances(IReadOnlyList<int> votes)
    {
        ArgumentNullException.ThrowIfNull(votes);
        var all = votes.Sum(v => (long)Math.Max(0, v));
        return [.. votes.Select(v => all == 0 ? 1.0 / Math.Max(1, votes.Count) : Math.Max(0, v) / (double)all)];
    }

    /// <summary>A chance as it is shown: "50%", "17%"; a chance that is none, "0%".</summary>
    public static string Percent(double chance) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)Math.Round(100 * Math.Clamp(chance, 0, 1), MidpointRounding.AwayFromZero)}%");

    /// <summary>The strip: a part for every option that can be drawn, in the options' order, as wide as its chance.</summary>
    public static IReadOnlyList<DrawPart> Parts(IReadOnlyList<int> votes)
    {
        var chances = Chances(votes);
        var parts = new List<DrawPart>();
        var at = 0.0;
        for (var i = 0; i < chances.Count; i++)
        {
            if (chances[i] > 0)
            {
                parts.Add(new DrawPart(i, at, chances[i], Math.Max(1, votes[i])));
                at += chances[i];
            }
        }
        return parts;
    }

    /// <summary>Where on the strip the pointer stops: inside one of the winner's slots (one of its votes), away from
    /// the slot's edges, the slot and the place in it differing from draw to draw (<paramref name="spin"/>: any
    /// number, the same for the same draw). A winner that has no part (no vote: a server's slip) stops the pointer at
    /// the strip's start.</summary>
    public static double Landing(IReadOnlyList<DrawPart> parts, int winner, int spin)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.FirstOrDefault(p => p.Option == winner) is not { } part)
        {
            return 0;
        }
        var slot = (int)((uint)spin % (uint)part.Slots);
        var within = 0.2 + (0.6 * ((uint)spin / (uint)part.Slots % 1000 / 999.0));
        var width = part.Width / part.Slots;
        return part.Start + (width * (slot + within));
    }

    /// <summary>Where the pointer is on the strip (0 to 1) when <paramref name="t"/> of its run is over (0 to 1): it
    /// passes the strip <see cref="Passes"/> times, ever slower, and is at <paramref name="landing"/> at the end.</summary>
    public static double Position(double landing, double t)
    {
        t = double.IsFinite(t) ? Math.Clamp(t, 0, 1) : 1;
        var left = 1 - t;
        var behind = Passes * left * left * left;
        var at = (landing - behind) % 1;
        return at < 0 ? at + 1 : at;
    }

    /// <summary>The option whose part the pointer is over; null over none (a strip with no parts).</summary>
    public static int? Under(IReadOnlyList<DrawPart> parts, double position)
    {
        ArgumentNullException.ThrowIfNull(parts);
        foreach (var part in parts)
        {
            if (position < part.End)
            {
                return part.Option;
            }
        }
        return parts.Count > 0 ? parts[^1].Option : null;
    }

    /// <summary>The step the pointer is on: the option whose part it is over and the vote whose slot; null over none
    /// (a strip with no parts).</summary>
    public static DrawStep? Step(IReadOnlyList<DrawPart> parts, double position)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var before = 0;
        foreach (var part in parts)
        {
            if (position < part.End || part == parts[^1])
            {
                var vote = Math.Clamp((int)Math.Floor((position - part.Start) / part.Width * part.Slots), 0, part.Slots - 1);
                return new DrawStep(part.Option, vote, before + vote);
            }
            before += part.Slots;
        }
        return null;
    }

    /// <summary>The steps of one pass along the strip, in order: each option's votes one after another.</summary>
    public static IReadOnlyList<DrawStep> Sequence(IReadOnlyList<DrawPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var steps = new List<DrawStep>();
        foreach (var part in parts)
        {
            for (var k = 0; k < part.Slots; k++)
            {
                steps.Add(new DrawStep(part.Option, k, steps.Count));
            }
        }
        return steps;
    }
}
