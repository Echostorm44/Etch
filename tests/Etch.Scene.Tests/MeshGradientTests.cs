using System;
using System.Threading.Tasks;
using Etch.Geometry;

namespace Etch.Scene.Tests;

internal sealed class MeshGradientTests
{
    [Test]
    public async Task MeshGradient_2x2_CornersAccessed()
    {
        var vertices = new MeshVertex[]
        {
            new(new RgbaFloat(1, 0, 0, 1)),
            new(new RgbaFloat(0, 1, 0, 1)),
            new(new RgbaFloat(0, 0, 1, 1)),
            new(new RgbaFloat(1, 1, 0, 1)),
        };

        var mesh = new MeshGradient(2, 2, vertices);

        await Assert.That(mesh.GetVertex(0, 0).Color.R).IsEqualTo(1);
        await Assert.That(mesh.GetVertex(0, 1).Color.G).IsEqualTo(1);
        await Assert.That(mesh.GetVertex(1, 0).Color.B).IsEqualTo(1);
        await Assert.That(mesh.GetVertex(1, 1).Color.B).IsEqualTo(0);
    }

    [Test]
    public async Task MeshGradient_InvalidRows_Throws()
    {
        await Assert.That(() => new MeshGradient(1, 2, Array.Empty<MeshVertex>())).Throws<EtchException>();
        await Assert.That(() => new MeshGradient(0, 2, Array.Empty<MeshVertex>())).Throws<EtchException>();
    }

    [Test]
    public async Task MeshGradient_InvalidCols_Throws()
    {
        await Assert.That(() => new MeshGradient(2, 1, Array.Empty<MeshVertex>())).Throws<EtchException>();
    }

    [Test]
    public async Task MeshGradient_MismatchedVertexCount_Throws()
    {
        await Assert.That(() => new MeshGradient(2, 2, new MeshVertex[3])).Throws<EtchException>();
    }

    [Test]
    public async Task MeshGradient_NullVertices_Throws()
    {
        await Assert.That(() => new MeshGradient(2, 2, null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task MeshGradient_VertexCount_MatchesRowsTimesCols()
    {
        var mesh = new MeshGradient(3, 4, new MeshVertex[12]);
        await Assert.That(mesh.VertexCount).IsEqualTo(12);
    }

    [Test]
    public async Task SceneBuilder_AddMeshGradient_ReturnsIncrementingIds()
    {
        int id0, id1;
        var sb = SceneBuilder.Begin();
        try
        {
            var mesh = new MeshGradient(2, 2, new MeshVertex[4]);
            id0 = sb.AddMeshGradient(mesh);
            id1 = sb.AddMeshGradient(mesh);
        }
        finally
        {
            sb.Dispose();
        }

        await Assert.That(id0).IsEqualTo(0);
        await Assert.That(id1).IsEqualTo(1);
    }

    [Test]
    public async Task SceneBuilder_End_PreservesMeshGradients()
    {
        SceneBuffer scene;
        {
            var sb = SceneBuilder.Begin();
            sb.BeginFrame();

            var vertices = new MeshVertex[]
            {
                new(new RgbaFloat(1, 0, 0, 1)),
                new(new RgbaFloat(0, 1, 0, 1)),
                new(new RgbaFloat(0, 0, 1, 1)),
                new(new RgbaFloat(0, 0, 0, 1)),
            };
            var mesh = new MeshGradient(2, 2, vertices);
            int meshId = sb.AddMeshGradient(mesh);

            int paintId = sb.AddPaint(Paint.MeshGradient((uint)meshId));
            var identity = sb.AddTransform(Affine.Identity);
            var bezPath = new BezPath(
                new byte[] { (byte)PathVerb.MoveTo, (byte)PathVerb.LineTo, (byte)PathVerb.LineTo, (byte)PathVerb.LineTo, (byte)PathVerb.Close },
                new double[] { 0, 0, 100, 0, 100, 100, 0, 100 }, 5);
            int pathId = sb.AddPath(bezPath);
            sb.FillPath(pathId, paintId, identity, FillRule.NonZero);

            sb.EndFrame();
            scene = sb.End();
        }

        await Assert.That(scene.MeshGradientCount).IsEqualTo(1);
        var restoredMesh = scene.GetMeshGradient(0);
        await Assert.That(restoredMesh.Rows).IsEqualTo(2);
        await Assert.That(restoredMesh.Cols).IsEqualTo(2);
        await Assert.That(restoredMesh.VertexCount).IsEqualTo(4);
        await Assert.That(restoredMesh.Vertices[0].Color.R).IsEqualTo(1);
    }

    [Test]
    public async Task MeshGradient_Serialization_RoundTrip()
    {
        SceneBuffer scene;
        {
            var vertices = new MeshVertex[]
            {
                new(new RgbaFloat(0.5f, 0.25f, 0.75f, 1), new Vec2(0, 0), new Vec2(0.3, 0), new Vec2(0, 0), new Vec2(0, 0.3)),
                new(new RgbaFloat(0.1f, 0.9f, 0.2f, 1), new Vec2(-0.3, 0), new Vec2(0, 0), new Vec2(0, 0), new Vec2(0, 0.3)),
                new(new RgbaFloat(0.8f, 0.1f, 0.9f, 1), new Vec2(0, 0), new Vec2(0.3, 0), new Vec2(0, -0.3), new Vec2(0, 0)),
                new(new RgbaFloat(0.3f, 0.5f, 0.8f, 1), new Vec2(-0.3, 0), new Vec2(0, 0), new Vec2(0, -0.3), new Vec2(0, 0)),
            };
            var mesh = new MeshGradient(2, 2, vertices);

            var sb = SceneBuilder.Begin();
            sb.BeginFrame();
            int meshId = sb.AddMeshGradient(mesh);
            sb.AddPaint(Paint.MeshGradient((uint)meshId));
            sb.EndFrame();
            scene = sb.End();
        }

        int requiredSize = Serialization.SceneWriter.GetRequiredSize(scene);
        var buffer = new byte[requiredSize];
        Serialization.SceneWriter.Write(scene, buffer);

        var restored = Serialization.SceneReader.Read(buffer);

        await Assert.That(restored.MeshGradientCount).IsEqualTo(1);
        var restoredMesh = restored.GetMeshGradient(0);
        await Assert.That(restoredMesh.Rows).IsEqualTo(2);
        await Assert.That(restoredMesh.Cols).IsEqualTo(2);
        await Assert.That(restoredMesh.Vertices[0].Color.R).IsEqualTo(0.5f);
        await Assert.That(restoredMesh.Vertices[0].DuOut.X).IsEqualTo(0.3);
        await Assert.That(restoredMesh.Vertices[1].DuIn.X).IsEqualTo(-0.3);
        await Assert.That(restoredMesh.Vertices[3].DvIn.Y).IsEqualTo(-0.3);
    }
}
