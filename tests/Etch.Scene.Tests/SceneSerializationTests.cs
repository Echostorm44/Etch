using System;
using Etch.Geometry;
using Etch.Scene.Serialization;
using TUnit;

using static Etch.Scene.Serialization.SceneWriter;
using static Etch.Scene.Serialization.SceneReader;

namespace Etch.Scene.Tests;

internal sealed class SceneSerializationTests
{
    [Test]
    public void RoundTrip_BeginEndFrame()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        var original = sb.End();

        int size = SceneWriter.GetRequiredSize(original);
        byte[] buffer = new byte[size];
        int written = SceneWriter.Write(original, buffer);
        var restored = SceneReader.Read(buffer);

        if (restored.CommandCount != original.CommandCount)
            throw new InvalidOperationException($"CommandCount mismatch: {restored.CommandCount} vs {original.CommandCount}");
        if (restored.Commands.Length != original.Commands.Length)
            throw new InvalidOperationException($"Commands.Length mismatch");

        for (int i = 0; i < original.Commands.Length; i++)
        {
            if (restored.Commands[i].Op != original.Commands[i].Op)
                throw new InvalidOperationException($"Opcode mismatch at {i}");
        }
    }

    [Test]
    public void RoundTrip_FillPath()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.CommandCount != original.CommandCount)
            throw new InvalidOperationException("CommandCount mismatch");
        if (serialized.Commands[1].Op != SceneOpcode.FillPath)
            throw new InvalidOperationException("Expected FillPath opcode");
        if (serialized.Commands[1].FillPath.PathId != 0)
            throw new InvalidOperationException("PathId mismatch");
        if (serialized.Commands[1].FillPath.PaintId != 0)
            throw new InvalidOperationException("PaintId mismatch");
    }

    [Test]
    public void RoundTrip_StrokePath()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.StrokePath(pathId, paintId, transformId, 3.5f, new StrokeStyle());
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.StrokePath)
            throw new InvalidOperationException("Expected StrokePath opcode");
        if (serialized.Commands[1].StrokePath.StrokeWidth != 3.5f)
            throw new InvalidOperationException("StrokeWidth mismatch");
    }

    [Test]
    public void RoundTrip_DrawShadow()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFF000000));
        var transformId = sb.AddTransform(Affine.Identity);
        var shadowOffset = new Vec2(5.0, -3.0);
        const float blurRadius = 10.0f;
        const uint shadowColor = 0x40000000u;
        sb.DrawShadow(pathId, paintId, transformId, shadowOffset, blurRadius, shadowColor);
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.DrawShadow)
            throw new InvalidOperationException("Expected DrawShadow opcode");
        if (serialized.Commands[1].DrawShadow.PathId != 0)
            throw new InvalidOperationException("PathId mismatch");
        if (serialized.Commands[1].DrawShadow.PaintId != 0)
            throw new InvalidOperationException("PaintId mismatch");
        if (serialized.Commands[1].DrawShadow.TransformId != 0)
            throw new InvalidOperationException("TransformId mismatch");
        if (serialized.Commands[1].DrawShadow.ShadowOffsetX != 5.0f)
            throw new InvalidOperationException("ShadowOffsetX mismatch");
        if (serialized.Commands[1].DrawShadow.ShadowOffsetY != -3.0f)
            throw new InvalidOperationException("ShadowOffsetY mismatch");
        if (serialized.Commands[1].DrawShadow.BlurRadius != 10.0f)
            throw new InvalidOperationException("BlurRadius mismatch");
        if (serialized.Commands[1].DrawShadow.ShadowColor != 0x40000000u)
            throw new InvalidOperationException("ShadowColor mismatch");
    }

    [Test]
    public void RoundTrip_FillRect()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.FillRect(Rect.FromLTRB(10, 20, 100, 200), paintId, transformId);
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.FillRect)
            throw new InvalidOperationException("Expected FillRect opcode");
        if (serialized.RectCount != 1)
            throw new InvalidOperationException("RectCount mismatch");
    }

    [Test]
    public void RoundTrip_PushPopLayer()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        sb.PushLayer(Rect.FromLTRB(0, 0, 100, 100), 0.75f, BlendMode.SrcOver, LayerFlags.Isolated);
        sb.PopLayer();
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.PushLayer)
            throw new InvalidOperationException("Expected PushLayer opcode");
        if (serialized.Commands[1].PushLayer.Opacity != 0.75f)
            throw new InvalidOperationException("Opacity mismatch");
        if (serialized.Commands[1].PushLayer.BlendMode != (byte)BlendMode.SrcOver)
            throw new InvalidOperationException("BlendMode mismatch");
        if (serialized.Commands[2].Op != SceneOpcode.PopLayer)
            throw new InvalidOperationException("Expected PopLayer opcode");
    }

    [Test]
    public void RoundTrip_PushPopClip()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var clipId = sb.AddPath(CreateSquarePath());
        sb.PushClip(clipId, FillRule.EvenOdd);
        sb.PopClip();
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.PushClip)
            throw new InvalidOperationException("Expected PushClip opcode");
        if (serialized.Commands[1].PushClip.FillRule != (byte)FillRule.EvenOdd)
            throw new InvalidOperationException("FillRule mismatch");
        if (serialized.Commands[2].Op != SceneOpcode.PopClip)
            throw new InvalidOperationException("Expected PopClip opcode");
    }

    [Test]
    public void RoundTrip_SetTransform()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var transformId = sb.AddTransform(Affine.Translate(new Vec2(50, 75)));
        sb.SetTransform(transformId);
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.SetTransform)
            throw new InvalidOperationException("Expected SetTransform opcode");
        if (serialized.Commands[1].SetTransform.TransformId != 0)
            throw new InvalidOperationException("TransformId mismatch");
    }

    [Test]
    public void RoundTrip_SetBlendMode()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        sb.SetBlendMode(BlendMode.DstOver);
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.SetBlendMode)
            throw new InvalidOperationException("Expected SetBlendMode opcode");
        if (serialized.Commands[1].SetBlendMode.BlendMode != (byte)BlendMode.DstOver)
            throw new InvalidOperationException("BlendMode mismatch");
    }

    [Test]
    public void RoundTrip_DrawImage()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        _ = sb.AddPaint(Paint.Solid(0xFFFF0000));
        _ = sb.AddPaint(Paint.Solid(0xFF00FF00));
        _ = sb.AddTransform(Affine.Identity);
        _ = sb.AddTransform(Affine.Translate(10, 20));
        sb.DrawImage(imageId: 5, paintId: 1, transformId: 1);
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.DrawImage)
            throw new InvalidOperationException("Expected DrawImage opcode");
        if (serialized.Commands[1].DrawImage.ImageId != 5)
            throw new InvalidOperationException("ImageId mismatch");
    }

    [Test]
    public void RoundTrip_DrawGlyphRun()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        _ = sb.AddPaint(Paint.Solid(0xFFFF0000));
        _ = sb.AddTransform(Affine.Identity);
        sb.DrawGlyphRun(glyphRunId: 3, paintId: 0, transformId: 0);
        sb.EndFrame();
        var original = sb.End();

        var serialized = RoundTrip(original);

        if (serialized.Commands[1].Op != SceneOpcode.DrawGlyphRun)
            throw new InvalidOperationException("Expected DrawGlyphRun opcode");
        if (serialized.Commands[1].DrawGlyphRun.GlyphRunId != 3)
            throw new InvalidOperationException("GlyphRunId mismatch");
    }

    [Test]
    public void RoundTrip_AllOpcodes()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);

        sb.PushLayer(Rect.FromLTRB(0, 0, 100, 100), 1.0f, BlendMode.SrcOver);
        sb.PopLayer();
        sb.PushClip(pathId, FillRule.NonZero);
        sb.PopClip();
        sb.SetTransform(transformId);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.StrokePath(pathId, paintId, transformId, 1.0f, new StrokeStyle());
        sb.FillRect(Rect.FromLTRB(0, 0, 100, 100), paintId, transformId);
        sb.DrawImage(imageId: 0, paintId: paintId, transformId: transformId);
        sb.DrawGlyphRun(glyphRunId: 0, paintId: paintId, transformId: transformId);
        sb.SetBlendMode(BlendMode.SrcOver);
        sb.EndFrame();

        var original = sb.End();
        var serialized = RoundTrip(original);

        if (serialized.CommandCount != original.CommandCount)
            throw new InvalidOperationException($"CommandCount mismatch: {serialized.CommandCount} vs {original.CommandCount}");
        if (serialized.PathCount != original.PathCount)
            throw new InvalidOperationException($"PathCount mismatch");
        if (serialized.PaintCount != original.PaintCount)
            throw new InvalidOperationException($"PaintCount mismatch");
        if (serialized.TransformCount != original.TransformCount)
            throw new InvalidOperationException($"TransformCount mismatch");
        if (serialized.RectCount != original.RectCount)
            throw new InvalidOperationException($"RectCount mismatch");
    }

    [Test]
    public void Read_BadMagic_PanicsET_P_0404()
    {
        byte[] buffer = new byte[64];
        buffer[0] = 0x00;
        buffer[1] = 0x00;
        buffer[2] = 0x00;
        buffer[3] = 0x00;

        bool threw = false;
        try
        {
            SceneReader.Read(buffer);
        }
        catch (EtchException ex)
        {
            threw = true;
            if (ex.Code != Etch.PanicCodes.SceneFormatBadMagic)
                throw new InvalidOperationException($"Expected SceneFormatBadMagic, got {ex.Code}");
        }
        if (!threw)
            throw new InvalidOperationException("Expected panic not thrown");
    }

    [Test]
    public void Read_TruncatedBuffer_PanicsET_P_0406()
    {
        byte[] buffer = new byte[10];

        bool threw = false;
        try
        {
            SceneReader.Read(buffer);
        }
        catch (EtchException ex)
        {
            threw = true;
            if (ex.Code != Etch.PanicCodes.SceneFormatTruncated)
                throw new InvalidOperationException($"Expected SceneFormatTruncated, got {ex.Code}");
        }
        if (!threw)
            throw new InvalidOperationException("Expected panic not thrown");
    }

    [Test]
    public void GetRequiredSize_ReturnsNonNegative()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        var scene = sb.End();

        int size = SceneWriter.GetRequiredSize(scene);
        if (size < 0)
            throw new InvalidOperationException($"GetRequiredSize returned negative: {size}");
    }

    [Test]
    public void Write_BufferTooSmall_Panics()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        var scene = sb.End();

        byte[] buffer = new byte[1];

        bool threw = false;
        try
        {
            SceneWriter.Write(scene, buffer);
        }
        catch (EtchException ex)
        {
            threw = true;
            if (ex.Code != Etch.PanicCodes.BufferOverflow)
                throw new InvalidOperationException($"Expected BufferOverflow, got {ex.Code}");
        }
        if (!threw)
            throw new InvalidOperationException("Expected panic not thrown");
    }

    private static SceneBuffer RoundTrip(SceneBuffer original)
    {
        int size = SceneWriter.GetRequiredSize(original);
        byte[] buffer = new byte[size];
        int written = SceneWriter.Write(original, buffer);
        return SceneReader.Read(buffer.AsSpan(0, written));
    }

    private static BezPath CreateSquarePath()
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(100, 0));
        builder.LineTo(new Point(100, 100));
        builder.LineTo(new Point(0, 100));
        builder.Close();
        return builder.Build();
    }
}