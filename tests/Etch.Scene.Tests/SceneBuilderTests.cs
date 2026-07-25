using System;
using System.Runtime.CompilerServices;
using Etch.Geometry;
using TUnit;

namespace Etch.Scene.Tests;

internal sealed class SceneBuilderTests
{
    [Test]
    public void BeginFrame_EndFrame_CommandCountIsTwo()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        var scene = sb.End();

        if (scene.CommandCount != 2)
            throw new InvalidOperationException($"Expected 2 commands, got {scene.CommandCount}");
        if (scene.Commands[0].Op != SceneOpcode.BeginFrame)
            throw new InvalidOperationException("Expected BeginFrame opcode");
        if (scene.Commands[1].Op != SceneOpcode.EndFrame)
            throw new InvalidOperationException("Expected EndFrame opcode");
    }

    [Test]
    public void AddPath_ReturnsZeroBasedMonotonicIds()
    {
        var sb = SceneBuilder.Begin(4);
        var path0 = sb.AddPath(CreateSquarePath());
        var path1 = sb.AddPath(CreateSquarePath());
        var path2 = sb.AddPath(CreateSquarePath());

        if (path0 != 0) throw new InvalidOperationException($"path0 = {path0}");
        if (path1 != 1) throw new InvalidOperationException($"path1 = {path1}");
        if (path2 != 2) throw new InvalidOperationException($"path2 = {path2}");

        sb.Dispose();
    }

    [Test]
    public void AddPaint_ReturnsZeroBasedMonotonicIds()
    {
        var sb = SceneBuilder.Begin(4);
        var paint0 = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var paint1 = sb.AddPaint(Paint.Solid(0xFF00FF00));
        var paint2 = sb.AddPaint(Paint.Solid(0xFF0000FF));

        if (paint0 != 0) throw new InvalidOperationException($"paint0 = {paint0}");
        if (paint1 != 1) throw new InvalidOperationException($"paint1 = {paint1}");
        if (paint2 != 2) throw new InvalidOperationException($"paint2 = {paint2}");

        sb.Dispose();
    }

    [Test]
    public void AddTransform_ReturnsZeroBasedMonotonicIds()
    {
        var sb = SceneBuilder.Begin(4);
        var t0 = sb.AddTransform(Affine.Identity);
        var t1 = sb.AddTransform(Affine.Translate(new Vec2(10, 20)));
        var t2 = sb.AddTransform(Affine.Scale(2.0));

        if (t0 != 0) throw new InvalidOperationException($"t0 = {t0}");
        if (t1 != 1) throw new InvalidOperationException($"t1 = {t1}");
        if (t2 != 2) throw new InvalidOperationException($"t2 = {t2}");

        sb.Dispose();
    }

    [Test]
    public void FillPath_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();

        if (scene.CommandCount != 3)
            throw new InvalidOperationException($"Expected 3 commands, got {scene.CommandCount}");
        if (scene.Commands[1].Op != SceneOpcode.FillPath)
            throw new InvalidOperationException("Expected FillPath opcode");
        if (scene.Commands[1].FillPath.PathId != pathId)
            throw new InvalidOperationException($"PathId mismatch");
        if (scene.Commands[1].FillPath.PaintId != paintId)
            throw new InvalidOperationException($"PaintId mismatch");
        if (scene.Commands[1].FillPath.TransformId != transformId)
            throw new InvalidOperationException($"TransformId mismatch");
    }

    [Test]
    public void StrokePath_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.StrokePath(pathId, paintId, transformId, 2.0f, new StrokeStyle());
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.StrokePath)
            throw new InvalidOperationException("Expected StrokePath opcode");
        if (scene.Commands[1].StrokePath.PathId != pathId)
            throw new InvalidOperationException($"PathId mismatch");
        if (scene.Commands[1].StrokePath.StrokeWidth != 2.0f)
            throw new InvalidOperationException($"StrokeWidth mismatch");
    }

    [Test]
    public void FillRect_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        var r = Rect.FromLTRB(0, 0, 100, 100);
        sb.FillRect(r, paintId, transformId);
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.FillRect)
            throw new InvalidOperationException("Expected FillRect opcode");
        if (scene.Commands[1].FillRect.PaintId != paintId)
            throw new InvalidOperationException($"PaintId mismatch");
        if (scene.Commands[1].FillRect.TransformId != transformId)
            throw new InvalidOperationException($"TransformId mismatch");
        if (scene.RectCount != 1)
            throw new InvalidOperationException($"Expected 1 rect, got {scene.RectCount}");
    }

    [Test]
    public void DrawShadow_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFF000000));
        var transformId = sb.AddTransform(Affine.Identity);
        var shadowOffset = new Vec2(5.0, -3.0);
        const float blurRadius = 10.0f;
        const uint shadowColor = 0x40000000u;
        sb.DrawShadow(pathId, paintId, transformId, shadowOffset, blurRadius, shadowColor);
        sb.EndFrame();
        var scene = sb.End();

        if (scene.CommandCount != 3)
            throw new InvalidOperationException($"Expected 3 commands, got {scene.CommandCount}");
        if (scene.Commands[1].Op != SceneOpcode.DrawShadow)
            throw new InvalidOperationException("Expected DrawShadow opcode");
        if (scene.Commands[1].DrawShadow.PathId != pathId)
            throw new InvalidOperationException($"PathId mismatch");
        if (scene.Commands[1].DrawShadow.PaintId != paintId)
            throw new InvalidOperationException($"PaintId mismatch");
        if (scene.Commands[1].DrawShadow.TransformId != transformId)
            throw new InvalidOperationException($"TransformId mismatch");
        if (scene.Commands[1].DrawShadow.ShadowOffsetX != 5.0f)
            throw new InvalidOperationException($"ShadowOffsetX mismatch");
        if (scene.Commands[1].DrawShadow.ShadowOffsetY != -3.0f)
            throw new InvalidOperationException($"ShadowOffsetY mismatch");
        if (scene.Commands[1].DrawShadow.BlurRadius != 10.0f)
            throw new InvalidOperationException($"BlurRadius mismatch");
        if (scene.Commands[1].DrawShadow.ShadowColor != 0x40000000u)
            throw new InvalidOperationException($"ShadowColor mismatch");
    }

    [Test]
    public void PushPopLayer_EmitsCorrectCommands()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        sb.PushLayer(Rect.FromLTRB(0, 0, 100, 100), 0.5f, BlendMode.SrcOver, LayerFlags.None);
        sb.PopLayer();
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.PushLayer)
            throw new InvalidOperationException("Expected PushLayer opcode");
        if (scene.Commands[2].Op != SceneOpcode.PopLayer)
            throw new InvalidOperationException("Expected PopLayer opcode");
        if (scene.Commands[1].PushLayer.Opacity != 0.5f)
            throw new InvalidOperationException($"Opacity mismatch");
        if (scene.Commands[1].PushLayer.BlendMode != (byte)BlendMode.SrcOver)
            throw new InvalidOperationException($"BlendMode mismatch");
        if (scene.RectCount != 1)
            throw new InvalidOperationException($"Expected 1 rect, got {scene.RectCount}");
    }

    [Test]
    public void PushPopClip_EmitsCorrectCommands()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var clipPathId = sb.AddPath(CreateSquarePath());
        sb.PushClip(clipPathId, FillRule.EvenOdd);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.PushClip)
            throw new InvalidOperationException("Expected PushClip opcode");
        if (scene.Commands[2].Op != SceneOpcode.PopClip)
            throw new InvalidOperationException("Expected PopClip opcode");
        if (scene.Commands[1].PushClip.ClipId != clipPathId)
            throw new InvalidOperationException($"ClipId mismatch");
        if (scene.Commands[1].PushClip.FillRule != (byte)FillRule.EvenOdd)
            throw new InvalidOperationException($"FillRule mismatch");
    }

    [Test]
    public void SetTransform_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var transformId = sb.AddTransform(Affine.Translate(new Vec2(50, 50)));
        sb.SetTransform(transformId);
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.SetTransform)
            throw new InvalidOperationException("Expected SetTransform opcode");
        if (scene.Commands[1].SetTransform.TransformId != transformId)
            throw new InvalidOperationException($"TransformId mismatch");
    }

    [Test]
    public void SetBlendMode_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.SetBlendMode(BlendMode.Src);
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.SetBlendMode)
            throw new InvalidOperationException("Expected SetBlendMode opcode");
        if (scene.Commands[1].SetBlendMode.BlendMode != (byte)BlendMode.Src)
            throw new InvalidOperationException($"BlendMode mismatch");
    }

    [Test]
    public void DrawImage_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.DrawImage(imageId: 0, paintId, transformId);
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.DrawImage)
            throw new InvalidOperationException("Expected DrawImage opcode");
        if (scene.Commands[1].DrawImage.ImageId != 0)
            throw new InvalidOperationException($"ImageId mismatch");
    }

    [Test]
    public void DrawGlyphRun_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        _ = sb.AddPaint(Paint.Solid(0xFFFF0000));
        _ = sb.AddPaint(Paint.Solid(0xFF00FF00));
        _ = sb.AddPaint(Paint.Solid(0xFF0000FF));
        _ = sb.AddTransform(Affine.Identity);
        _ = sb.AddTransform(Affine.Translate(10, 20));
        sb.DrawGlyphRun(glyphRunId: 5, paintId: 2, transformId: 1);
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.DrawGlyphRun)
            throw new InvalidOperationException("Expected DrawGlyphRun opcode");
        if (scene.Commands[1].DrawGlyphRun.GlyphRunId != 5)
            throw new InvalidOperationException($"GlyphRunId mismatch");
        if (scene.Commands[1].DrawGlyphRun.PaintId != 2)
            throw new InvalidOperationException($"PaintId mismatch");
        if (scene.Commands[1].DrawGlyphRun.TransformId != 1)
            throw new InvalidOperationException($"TransformId mismatch");
    }

    [Test]
    public void EveryOpcode_HasCorrespondingBuilderMethod()
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

        var scene = sb.End();
        if (scene.CommandCount != 13)
            throw new InvalidOperationException($"Expected 13 commands, got {scene.CommandCount}");
    }

    [Test]
    public void SceneBuffer_ContainsResourceTables()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Translate(new Vec2(10, 20)));
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();

        if (scene.PathCount != 1)
            throw new InvalidOperationException($"PathCount expected 1, got {scene.PathCount}");
        if (scene.PaintCount != 1)
            throw new InvalidOperationException($"PaintCount expected 1, got {scene.PaintCount}");
        if (scene.TransformCount != 1)
            throw new InvalidOperationException($"TransformCount expected 1, got {scene.TransformCount}");
    }

    [Test]
    public void End_ReturnsImmutableSceneBuffer()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        var scene = sb.End();

        if (scene.CommandCount != 2)
            throw new InvalidOperationException($"Expected 2 commands, got {scene.CommandCount}");
    }

    [Test]
    public void FillPath_InvalidPathId_PanicsET_P_0403()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);

        try
        {
            sb.FillPath(pathId: 99, paintId, transformId, FillRule.NonZero);
            throw new InvalidOperationException("Expected panic not thrown");
        }
        catch (EtchException ex)
        {
            if (ex.Code != Etch.PanicCodes.InvalidSceneResourceId)
                throw new InvalidOperationException($"Expected InvalidSceneResourceId, got {ex.Code}");
        }
        finally
        {
            sb.Dispose();
        }
    }

    [Test]
    public void FillPath_InvalidPaintId_PanicsET_P_0403()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var transformId = sb.AddTransform(Affine.Identity);

        try
        {
            sb.FillPath(pathId, paintId: 99, transformId, FillRule.NonZero);
            throw new InvalidOperationException("Expected panic not thrown");
        }
        catch (EtchException ex)
        {
            if (ex.Code != Etch.PanicCodes.InvalidSceneResourceId)
                throw new InvalidOperationException($"Expected InvalidSceneResourceId, got {ex.Code}");
        }
        finally
        {
            sb.Dispose();
        }
    }

    [Test]
    public void FillPath_InvalidTransformId_PanicsET_P_0403()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));

        try
        {
            sb.FillPath(pathId, paintId, transformId: 99, FillRule.NonZero);
            throw new InvalidOperationException("Expected panic not thrown");
        }
        catch (EtchException ex)
        {
            if (ex.Code != Etch.PanicCodes.InvalidSceneResourceId)
                throw new InvalidOperationException($"Expected InvalidSceneResourceId, got {ex.Code}");
        }
        finally
        {
            sb.Dispose();
        }
    }

    [Test]
    public void PushClip_InvalidPathId_PanicsET_P_0403()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();

        try
        {
            sb.PushClip(pathId: 99, FillRule.NonZero);
            throw new InvalidOperationException("Expected panic not thrown");
        }
        catch (EtchException ex)
        {
            if (ex.Code != Etch.PanicCodes.InvalidSceneResourceId)
                throw new InvalidOperationException($"Expected InvalidSceneResourceId, got {ex.Code}");
        }
        finally
        {
            sb.Dispose();
        }
    }

    [Test]
    public void SetTransform_InvalidTransformId_PanicsET_P_0403()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();

        try
        {
            sb.SetTransform(transformId: 99);
            throw new InvalidOperationException("Expected panic not thrown");
        }
        catch (EtchException ex)
        {
            if (ex.Code != Etch.PanicCodes.InvalidSceneResourceId)
                throw new InvalidOperationException($"Expected InvalidSceneResourceId, got {ex.Code}");
        }
        finally
        {
            sb.Dispose();
        }
    }

    [Test]
    public void PopClip_WithoutPushClip_PanicsET_P_0901()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();

        try
        {
            sb.PopClip();
            throw new InvalidOperationException("Expected panic not thrown");
        }
        catch (EtchException ex)
        {
            if (ex.Code != Etch.PanicCodes.UnbalancedClipStack)
                throw new InvalidOperationException($"Expected UnbalancedClipStack, got {ex.Code}");
        }
        finally
        {
            sb.Dispose();
        }
    }

    [Test]
    public void PushClip_SeventeenNestedClips_PanicsET_P_0902()
    {
        var sb = SceneBuilder.Begin(32);
        sb.BeginFrame();
        var clipPathId = sb.AddPath(CreateSquarePath());

        try
        {
            for (int i = 0; i < 17; i++)
            {
                sb.PushClip(clipPathId, FillRule.NonZero);
            }
            throw new InvalidOperationException("Expected panic not thrown");
        }
        catch (EtchException ex)
        {
            if (ex.Code != Etch.PanicCodes.ClipStackTooDeep)
                throw new InvalidOperationException($"Expected ClipStackTooDeep, got {ex.Code}");
        }
        finally
        {
            sb.Dispose();
        }
    }

    [Test]
    public void PushClip_WithClipModeDifference_EmitsCorrectCommand()
    {
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        var clipPathId = sb.AddPath(CreateSquarePath());
        sb.PushClip(clipPathId, FillRule.EvenOdd, ClipMode.Difference);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();

        if (scene.Commands[1].Op != SceneOpcode.PushClip)
            throw new InvalidOperationException("Expected PushClip opcode");
        if (scene.Commands[1].PushClip.ClipMode != (byte)ClipMode.Difference)
            throw new InvalidOperationException($"ClipMode mismatch");
    }

    [Test]
    public void End_ThenUseBuilder_PanicsET_P_0402()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        sb.End();

        try
        {
            sb.EndFrame();
            throw new InvalidOperationException("Expected panic not thrown");
        }
        catch (EtchException ex)
        {
            if (ex.Code != Etch.PanicCodes.SceneBuilderConsumed)
                throw new InvalidOperationException($"Expected SceneBuilderConsumed, got {ex.Code}");
        }
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