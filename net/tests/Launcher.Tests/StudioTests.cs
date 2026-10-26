using System.Numerics;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>What the 3D views hand the graphics card, without one: the studio's camera and lens, the turntable's
/// floor, models packed as the card takes them (front faces, see-through triangles far to near) and the track map's
/// flat triangles.</summary>
public sealed class StudioTests
{
    [Theory]
    [InlineData(0f, 0.3f)]
    [InlineData(2.1f, 0.6f)]
    [InlineData(4f, 0.02f)]
    public void TheStudioFramesTheWholeModelAtAnyTurn(float yaw, float pitch)
    {
        var center = new Vector3(3, -1, 7);
        const float radius = 2.5f;
        var view = StudioCamera.View(center, radius, yaw, pitch, 1);
        var lens = view * StudioCamera.Projection(StudioCamera.FieldOfView / 2, 400, 300, 1, 100);
        // the centre in the middle of the view
        var (middle, depth) = StudioCamera.Project(center, lens, 400, 300);
        Assert.Equal(200, middle.X, 0.01f);
        Assert.Equal(150, middle.Y, 0.01f);
        Assert.Equal(StudioCamera.Distance(radius, 1), depth, 0.001f);
        // anything within the radius inside the shorter side (the height here)
        foreach (var direction in new[] { Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, Vector3.Normalize(new Vector3(1, -1, 1)) })
        {
            var (at, _) = StudioCamera.Project(center + (direction * radius), lens, 400, 300);
            Assert.InRange(at.Y, 0, 300);
            Assert.InRange(at.X, 0, 400);
        }
    }

    [Fact]
    public void TheLensSpansTheShorterSideAndTheDepthRange()
    {
        const float near = 2, far = 50;
        var half = StudioCamera.FieldOfView / 2;
        var wide = StudioCamera.Projection(half, 800, 400, near, far);
        var tall = StudioCamera.Projection(half, 400, 800, near, far);
        // a point at the lens's half angle above the axis is at the top of a wide view, inside a tall one's
        var edge = new Vector4(0, MathF.Tan(half) * 10, -10, 1);
        var clip = Vector4.Transform(edge, wide);
        Assert.Equal(1, clip.Y / clip.W, 0.0001f);
        clip = Vector4.Transform(edge, tall);
        Assert.Equal(0.5f, clip.Y / clip.W, 0.0001f);
        clip = Vector4.Transform(new Vector4(MathF.Tan(half) * 10, 0, -10, 1), tall);
        Assert.Equal(1, clip.X / clip.W, 0.0001f);
        // OpenGL's depth: -1 at the near plane, 1 at the far one
        clip = Vector4.Transform(new Vector4(0, 0, -near, 1), wide);
        Assert.Equal(-1, clip.Z / clip.W, 0.0001f);
        clip = Vector4.Transform(new Vector4(0, 0, -far, 1), wide);
        Assert.Equal(1, clip.Z / clip.W, 0.0001f);
    }

    [Fact]
    public void TheMapsCameraLooksAtItsCentreFromItsDistance()
    {
        var view = new MapView(new Vector3(500, 20, -300), 240, 0.4f, 0.9f);
        var camera = Vector3.Transform(view.Center, view.Camera);
        Assert.True(Vector3.Distance(camera, new Vector3(0, 0, -view.Distance)) < 0.01f, $"{camera}");
    }

    [Fact]
    public void TheFloorLiesUnderTheMachineRoundItsMiddle()
    {
        var model = new MachineModel([Quad(new Vector3(-2, 1, -1), new Vector3(2, 1, -1), new Vector3(2, 3, 3), new Vector3(-2, 3, 3), Vector3.UnitY, false)], [], 0);
        var floor = TurntableFloor.Of(model);
        Assert.True(floor.Ground < 1, $"ground {floor.Ground}");
        // the ring round the farthest corner seen from above; the shadow the footprint's half lengths
        Assert.True(floor.Ring >= new Vector2(2, 2).Length(), $"ring {floor.Ring}");
        Assert.Equal(new Vector2(2, 2), floor.Footprint);
        var corners = floor.Corners;
        Assert.All(corners, c => Assert.Equal(floor.Ground, c.Y));
        Assert.Equal(new Vector3(model.Center.X, floor.Ground, model.Center.Z), (corners[0] + corners[2]) / 2);
        // counter-clockwise seen from above: its face points up
        Assert.True(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).Y > 0);
    }

    [Fact]
    public void PackedTrianglesTurnCounterClockwiseToTheirFront()
    {
        // the second mesh's triangles turn against its normals: packed, they turn the other way
        var facing = Quad(new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0), Vector3.UnitZ, false);
        var against = facing with { Normals = [.. facing.Normals.Select(n => -n)] };
        var packed = PackedModel.Of(new MachineModel([facing, against], [], 0));
        Assert.Equal(PackedModel.PlainStride, packed.Stride);
        Assert.Equal(8 * PackedModel.PlainStride, packed.Vertices.Length);
        Assert.Equal(new PackedMesh(facing.Material, 0, 6), packed.Meshes[0]);
        Assert.Equal(6, packed.Meshes[1].First);
        foreach (var mesh in packed.Meshes)
        {
            for (var i = mesh.First; i < mesh.First + mesh.Count; i += 3)
            {
                var (a, b, c) = (Place(packed, packed.Indices[i]), Place(packed, packed.Indices[i + 1]), Place(packed, packed.Indices[i + 2]));
                var normal = Normal(packed, packed.Indices[i]);
                Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0, $"triangle {i} turns against its normal {normal}");
            }
        }
        // the second mesh's vertices follow the first's
        Assert.True(packed.Indices[6..].All(i => i >= 4));
        // a vertex's numbers: place, normal, texture coordinates, colour
        Assert.Equal([1, -1, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1], packed.Vertices[PackedModel.PlainStride..(2 * PackedModel.PlainStride)]);
    }

    [Fact]
    public void SeeThroughTrianglesComeFarToNearInRunsOfOneMesh()
    {
        // a solid square, then two see-through meshes: one square at z -5, one triangle at z -1 and one at z -9
        var solid = Quad(new Vector3(-1, -1, -3), new Vector3(1, -1, -3), new Vector3(1, 1, -3), new Vector3(-1, 1, -3), Vector3.UnitZ, false);
        var glass = Quad(new Vector3(-1, -1, -5), new Vector3(1, -1, -5), new Vector3(1, 1, -5), new Vector3(-1, 1, -5), Vector3.UnitZ, true);
        var glows = new MachineMesh(
            [new(0, 0, -1), new(1, 0, -1), new(0, 1, -1), new(0, 0, -9), new(1, 0, -9), new(0, 1, -9)],
            [.. Enumerable.Repeat(Vector3.UnitZ, 6)], new Vector2[6], [.. Enumerable.Repeat(Vector4.One, 6)], [0, 1, 2, 3, 4, 5],
            glass.Material);
        var packed = PackedModel.Of(new MachineModel([solid, glass, glows], [], 0));
        Assert.Equal(4, packed.TranslucentTriangles);
        uint[] indices = [];
        var runs = new List<PackedRun>();
        var count = packed.Translucent(Matrix4x4.Identity, ref indices, runs);
        Assert.Equal(12, count);
        // the far glow, the glass (its two triangles together), the near glow
        Assert.Equal([new PackedRun(2, 0, 3), new PackedRun(1, 3, 6), new PackedRun(2, 9, 3)], runs);
        Assert.Equal(-9, Place(packed, indices[0]).Z);
        Assert.Equal(-1, Place(packed, indices[9]).Z);
        // turned half round, the order turns too
        packed.Translucent(Matrix4x4.CreateRotationY(MathF.PI), ref indices, runs);
        Assert.Equal(-1, Place(packed, indices[0]).Z);
    }

    [Fact]
    public void TheMapsTrianglesAreFlatAndLitFromAbove()
    {
        // a level triangle and a wall
        var course = new CourseModel(
            [new(0, 0, 0), new(0, 0, 10), new(10, 0, 0), new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)],
            [.. Enumerable.Repeat(Vector3.UnitY, 6)], [0, 1, 2, 3, 4, 5]);
        var (corners, shades) = course.Flat();
        Assert.Equal(6, corners.Length);
        Assert.Equal(2, shades.Length);
        Assert.Equal(new Vector3(0, 10, 0), corners[5]);
        Assert.Equal(0.55f + (0.45f * CourseModel.Light.Y), shades[0], 5);
        Assert.True(shades[1] < shades[0], "a wall facing sideways is darker than the road under the light");
        Assert.InRange(shades[1], 0.55f, 1);
    }

    private static MachineMesh Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, bool translucent) => new(
        [a, b, c, d], [normal, normal, normal, normal], [new(0, 1), new(1, 1), new(1, 0), new(0, 0)],
        [Vector4.One, Vector4.One, Vector4.One, Vector4.One], [0, 1, 2, 0, 2, 3],
        new MachineMaterial(new Vector3(0.8f), new Vector3(0.5f), Vector3.Zero, 10, translucent ? 0.5f : 1, -1, translucent, false));

    private static Vector3 Place(PackedModel packed, uint vertex) =>
        new(packed.Vertices[vertex * packed.Stride], packed.Vertices[(vertex * packed.Stride) + 1], packed.Vertices[(vertex * packed.Stride) + 2]);

    private static Vector3 Normal(PackedModel packed, uint vertex) =>
        new(packed.Vertices[(vertex * packed.Stride) + 3], packed.Vertices[(vertex * packed.Stride) + 4], packed.Vertices[(vertex * packed.Stride) + 5]);
}
