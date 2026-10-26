using Launcher.App.Gpu;
using Launcher.App.Theme;
using Launcher.Core;

namespace Launcher.App.Controls;

// A machine as a preview loads it, and where a load's time went (LoadTrace).
public sealed partial class MachinePreview
{
    /// <summary>A machine loaded for the view: its model, packing, floor and the look's textures (null: worked out by
    /// <see cref="PaintAsync"/>).</summary>
    private sealed record Ready(GarageMachine Loaded, PackedModel Packed, TurntableFloor Floor, IReadOnlyList<(int Texture, Picture Picture)>? Look);

    // where a load's time went, from the machine asked for to its first frame (LoadTrace)
    private Trace? _trace;

    private sealed class Trace(string what)
    {
        private readonly double _asked = LoadTrace.Now;

        public string What { get; } = what;

        public string Marks { get; private set; } = "";

        public bool Shown { get; set; }

        public double Upload { get; set; }

        public double Mesh { get; set; }

        public int Textures { get; set; }

        public int Frames { get; set; }

        public double Since => LoadTrace.Now - _asked;

        public void Mark(string what) => Marks += string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{(Marks.Length > 0 ? ", " : "")}{what} +{Since:F0}");
    }
}
