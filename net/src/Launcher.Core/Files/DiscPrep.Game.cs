using System.Globalization;

namespace Launcher.Core;

// Which file is the game: the kind of file, the id, the disc number and the revision.
public static partial class DiscPrep
{
    /// <summary>Why the file at <paramref name="imagePath"/> is not the game's disc, by what is cheap to see (the
    /// kind of file, the game's id, the disc number and the revision); null: nothing against it so far. What a
    /// preparation looks at first, by the same code, so that the Disc page (<see cref="DiscCheck"/>) and the
    /// preparation never disagree about it; the preparation then goes on to every file's bytes. Never throws.</summary>
    public static DiscPrepResult? Refusal(string? imagePath, DiscPins? pins = null)
    {
        try
        {
            using var image = Open(imagePath, pins ?? DiscPins.Game);
            return null;
        }
        catch (Stop stop)
        {
            return stop.Result;
        }
    }

    /// <summary>The image, opened, when it is a plain GameCube image of the pinned game; every other file is told
    /// apart here.</summary>
    private static DiscImage Open(string? imagePath, DiscPins pins)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            throw new Stop(new DiscPrepResult(DiscPrepOutcome.ImageMissing, $"There is no disc image at {imagePath}."));
        }
        DiscImage image;
        try
        {
            var convert = "The launcher reads plain ISO images only. In Dolphin, right-click the game, choose Convert File, pick the format ISO, and choose the new file here.";
            switch (DiscImage.Kind(imagePath))
            {
                case DiscImageKind.Rvz:
                    throw Unsupported("This is a compressed disc image (RVZ). " + convert);
                case DiscImageKind.Wia:
                    throw Unsupported("This is a compressed disc image (WIA). " + convert);
                case DiscImageKind.Gcz:
                    throw Unsupported("This is a compressed disc image (GCZ). " + convert);
                case DiscImageKind.Ciso:
                    throw Unsupported("This is a compact disc image (CISO). " + convert);
                case DiscImageKind.NKit:
                    throw Unsupported("This is an NKit image: a shrunk copy whose header holds NKit's own data where the original's was. " +
                        "Restore it to a full ISO with the NKit tools (Dolphin's Convert File does not put the original back), or dump the disc again.");
                case DiscImageKind.Archive:
                    throw Unsupported("This is an archive (.zip, .7z or .rar). Unpack it and choose the disc image inside.");
                case DiscImageKind.Wbfs:
                case DiscImageKind.Wii:
                    throw new Stop(new DiscPrepResult(DiscPrepOutcome.WrongGame, $"This is a Wii disc image. The launcher needs {pins.Name} for the GameCube ({pins.GameId})."));
                case DiscImageKind.Unknown:
                    throw new Stop(new DiscPrepResult(DiscPrepOutcome.NotADisc, "This file is not a GameCube disc image."));
            }
            image = DiscImage.Open(imagePath);
        }
        catch (DiscLimitException e)
        {
            throw NoDisc(e.Message);
        }
        catch (InvalidDataException e)
        {
            throw new Stop(new DiscPrepResult(DiscPrepOutcome.DamagedImage, $"This disc image is incomplete: {e.Message} It is {new FileInfo(imagePath).Length:N0} bytes."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new Stop(new DiscPrepResult(DiscPrepOutcome.DamagedImage, $"The disc image cannot be read: {e.Message}"));
        }
        DiscPrepResult? refusal = null;
        if (image.GameId != pins.GameId)
        {
            var region = image.GameId[3] switch { 'E' => "the USA", 'J' => "Japan", 'P' => "Europe", _ => null };
            var sameGame = image.GameId.AsSpan(0, 3).SequenceEqual(pins.GameId.AsSpan(0, 3)) && image.GameId.AsSpan(4).SequenceEqual(pins.GameId.AsSpan(4));
            refusal = new DiscPrepResult(DiscPrepOutcome.WrongGame, sameGame && region is not null
                ? $"This is the game's disc for {region} ({image.GameId}). The launcher needs {pins.Name}, {pins.GameId}."
                : $"This is another game: {image.Title} ({image.GameId}). The launcher needs {pins.Name}, {pins.GameId}.");
        }
        else if (image.Disc != pins.Disc || image.Revision != pins.Revision)
        {
            refusal = new DiscPrepResult(DiscPrepOutcome.WrongVersion, string.Create(CultureInfo.InvariantCulture,
                $"This is {pins.Name}, disc {image.Disc + 1}, revision {image.Revision}. The launcher needs disc {pins.Disc + 1}, revision {pins.Revision}: the game program was made from that one."));
        }
        if (refusal is not null)
        {
            image.Dispose();
            throw new Stop(refusal);
        }
        return image;
    }

    private static Stop Unsupported(string message) => new(new DiscPrepResult(DiscPrepOutcome.UnsupportedFormat, message));
}
