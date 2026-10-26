using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The left column's custom machine in its colours (as the builder shows it, the paint
// there after Save). The colours are the saved machine's own, worked out as the builder works them out (a paint, one of
// the part's stock colours from the disc, or the part's own), so the column and the builder never differ; for a raced
// machine that is no saved one, the colour words it races with.
public sealed partial class MainViewModel
{
    /// <summary>A raced custom machine's colours by part (body, cockpit, booster), each null for the part's own.</summary>
    private IReadOnlyList<Rgb?> PartColoursOf(CustomMachine? custom)
    {
        if (custom is null)
        {
            return [null, null, null];
        }
        var reading = Theme.GameSkin.CustomPartsAsync();
        if (!reading.IsCompleted)
        {
            _ = reading.ContinueWith(_ => OnUi(RefreshMachine), TaskScheduler.Default);   // the stock colours once read
        }
        var parts = reading is { IsCompletedSuccessfully: true, Result: { } read } ? read : null;
        var stock = new PartColourWords(part => parts?.StockWords(part));
        var saved = (_state.Settings.Racer.Customs ?? []).FirstOrDefault(b =>
            (b.Body, b.Cockpit, b.Booster, b.Pilot) == (custom.Body, custom.Cockpit, custom.Booster, custom.Pilot));
        return [.. Enumerable.Range(0, 3).Select(k => saved is not null ? Shown(saved.PartOf(k), saved.ColourOf(k))
            : custom.Colors is { Count: 3 } words && words[k] != 0 ? new Rgb((byte)(words[k] >> 24), (byte)(words[k] >> 16), (byte)(words[k] >> 8)) : null)];

        Rgb? Shown(int part, PartColour colour) =>
            colour.Stock is not { } k ? colour.Paint : k == 0 ? null : stock.StockColours(part) is { } swatches ? swatches[k] : null;
    }
}
