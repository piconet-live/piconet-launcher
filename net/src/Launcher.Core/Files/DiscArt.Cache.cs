namespace Launcher.Core;

// What the machines' cache (MachineCache) keys a disc by.
public sealed partial class DiscArt
{
    /// <summary>The machines' files as the disc's table names them (every file under <c>vehicle/</c> and
    /// <c>vehicle_parts/</c>: name, place, length) and the shared machine textures' bytes: the machines' cache's key
    /// (<see cref="MachineCache.DiscKey"/>).</summary>
    public string MachinesKey() =>
        MachineCache.DiscKey(
            _table.Values.Where(f => f.Path.StartsWith("vehicle/", StringComparison.OrdinalIgnoreCase)
                || f.Path.StartsWith("vehicle_parts/", StringComparison.OrdinalIgnoreCase)).Select(f => (f.Path, (long)f.Offset, (long)f.Length)),
            Read("vehicle/carcmntex.tpl"));
}
