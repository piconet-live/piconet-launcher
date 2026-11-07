using System.Collections.Immutable;
using System.Numerics;

namespace Fzgx.Coli;

/// <summary>How a course's ends connect.</summary>
public enum ColiCircuit
{
    /// <summary>A lap.</summary>
    Closed,

    /// <summary>A start and a separate finish.</summary>
    Open,
}

/// <summary>
/// A model placed in a course. Scene objects name the model they show;
/// the name is looked up in the course's, the venue's or the shared model file.
/// </summary>
/// <param name="Name">Model name, matching a GMA model name (e.g. <c>C01_ROAD01</c>); may be empty.</param>
/// <param name="World">
/// Object-to-world transform in game coordinates (meters, +Y up, right-handed),
/// in System.Numerics' row-vector convention: <c>Vector3.Transform(p, World)</c>.
/// </param>
/// <param name="IsStatic">True for static objects, which have no transform (placed at the origin).</param>
/// <param name="IsAnimated">True if the object has an animation clip; <paramref name="World"/> is its rest pose.</param>
public sealed record ColiSceneObject(string Name, Matrix4x4 World, bool IsStatic, bool IsAnimated);

/// <summary>The parts of a course file read so far (stage 1: header and placed objects).</summary>
/// <param name="TrackLength">Lap length in meters.</param>
/// <param name="TrackMinHeight">Lowest track height in meters.</param>
/// <param name="Circuit">Closed lap or open course.</param>
/// <param name="Objects">Placed objects: dynamic objects in file order, then static ones.</param>
public sealed record ColiScene(float TrackLength, float TrackMinHeight, ColiCircuit Circuit, ImmutableArray<ColiSceneObject> Objects);
