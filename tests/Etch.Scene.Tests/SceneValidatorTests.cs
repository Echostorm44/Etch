using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Scene.Tests;

internal sealed class SceneValidatorTests
{
    [Test]
    public void ValidScene_PassesValidation()
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        var pathId = sb.AddPath(CreateSquarePath());
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();

        var scene = sb.End();

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count != 0)
            throw new InvalidOperationException($"Expected 0 errors, got {report.Count}");
    }

    [Test]
    public void DuplicateBeginFrame_ReportsBadFrameMarkers()
    {
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [], [], [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadFrameMarkers)
            throw new InvalidOperationException($"Expected BadFrameMarkers, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void MissingBeginFrame_ReportsBadFrameMarkers()
    {
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [], [], [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadFrameMarkers)
            throw new InvalidOperationException($"Expected BadFrameMarkers, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void MissingEndFrame_ReportsBadFrameMarkers()
    {
        var beginPayload = new BeginFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
        };

        var scene = new SceneBuffer(commands, [], [], [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadFrameMarkers)
            throw new InvalidOperationException($"Expected BadFrameMarkers, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void EndFrameBeforeBeginFrame_ReportsBadFrameMarkers()
    {
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
        };

        var scene = new SceneBuffer(commands, [], [], [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadFrameMarkers)
            throw new InvalidOperationException($"Expected BadFrameMarkers, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void PopLayerWithoutPush_ReportsUnbalancedLayerStack()
    {
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var popPayload = new PopLayerPayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.PopLayer, popPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [], [], [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.UnbalancedLayerStack)
            throw new InvalidOperationException($"Expected UnbalancedLayerStack, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void UnclosedPushLayer_ReportsUnbalancedLayerStack()
    {
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var pushPayload = new PushLayerPayload { LayerId = 0, Opacity = 1f, BlendMode = 0, Flags = 0 };
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.PushLayer, pushPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var rects = new Geometry.Rect[] { new Geometry.Rect(0, 0, 100, 100) };
        var scene = new SceneBuffer(commands, [], [], [], [], rects, []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.UnbalancedLayerStack)
            throw new InvalidOperationException($"Expected UnbalancedLayerStack, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void PopClipWithoutPush_ReportsUnbalancedClipStack()
    {
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var popPayload = new PopClipPayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.PopClip, popPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [], [], [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.UnbalancedClipStack)
            throw new InvalidOperationException($"Expected UnbalancedClipStack, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void InvalidPathId_ReportsInvalidResourceId()
    {
        var fillPathPayload = new FillPathPayload { PathId = 999, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [], [], [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.InvalidResourceId)
            throw new InvalidOperationException($"Expected InvalidResourceId, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void InvalidPaintId_ReportsInvalidResourceId()
    {
        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 999, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var pathEntry = new PathEntry(0, 16, 1, 2);
        var pathArena = new byte[16];

        var scene = new SceneBuffer(commands, [pathEntry], pathArena, [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.InvalidResourceId)
            throw new InvalidOperationException($"Expected InvalidResourceId, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void InvalidTransformId_ReportsInvalidResourceId()
    {
        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 999, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var pathEntry = new PathEntry(0, 16, 1, 2);
        var pathArena = new byte[16];

        var scene = new SceneBuffer(commands, [pathEntry], pathArena, [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.InvalidResourceId)
            throw new InvalidOperationException($"Expected InvalidResourceId, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void EmptyPath_ReportsEmptyPath()
    {
        int verbCount = 0;
        int coordCount = 0;
        int arenaLength = 8 + verbCount + coordCount * 8;
        var arena = new byte[arenaLength];
        BitConverter.TryWriteBytes(arena.AsSpan(0, 4), verbCount);
        BitConverter.TryWriteBytes(arena.AsSpan(4, 4), coordCount);
        var pathEntry = new PathEntry(0, arenaLength, verbCount, coordCount);

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var paints = new Paint[] { Paint.Solid(0xFFFF0000) };
        var transforms = new Affine[] { Affine.Identity };
        var scene = new SceneBuffer(commands, [pathEntry], arena, paints, transforms, [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.EmptyPath)
            throw new InvalidOperationException($"Expected EmptyPath, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void SingleVerbPath_ReportsEmptyPath()
    {
        int verbCount = 1;
        int coordCount = 2;
        int arenaLength = 8 + verbCount + coordCount * 8;
        var arena = new byte[arenaLength];
        BitConverter.TryWriteBytes(arena.AsSpan(0, 4), verbCount);
        BitConverter.TryWriteBytes(arena.AsSpan(4, 4), coordCount);
        arena[8] = (byte)PathVerb.MoveTo;
        BitConverter.TryWriteBytes(arena.AsSpan(9, 8), 0.0);
        BitConverter.TryWriteBytes(arena.AsSpan(17, 8), 0.0);
        var pathEntry = new PathEntry(0, arenaLength, verbCount, coordCount);

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var paints = new Paint[] { Paint.Solid(0xFFFF0000) };
        var transforms = new Affine[] { Affine.Identity };
        var scene = new SceneBuffer(commands, [pathEntry], arena, paints, transforms, [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.EmptyPath)
            throw new InvalidOperationException($"Expected EmptyPath, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void OpacityBelowZero_ReportsBadLayerOpacity()
    {
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var pushPayload = new PushLayerPayload { LayerId = 0, Opacity = -0.1f, BlendMode = 0, Flags = 0 };
        var popPayload = new PopLayerPayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.PushLayer, pushPayload),
            new SceneCommand(SceneOpcode.PopLayer, popPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var rects = new Geometry.Rect[] { new Geometry.Rect(0, 0, 100, 100) };
        var scene = new SceneBuffer(commands, [], [], [], [], rects, []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadLayerOpacity)
            throw new InvalidOperationException($"Expected BadLayerOpacity, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void OpacityAboveOne_ReportsBadLayerOpacity()
    {
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var pushPayload = new PushLayerPayload { LayerId = 0, Opacity = 1.5f, BlendMode = 0, Flags = 0 };
        var popPayload = new PopLayerPayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.PushLayer, pushPayload),
            new SceneCommand(SceneOpcode.PopLayer, popPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var rects = new Geometry.Rect[] { new Geometry.Rect(0, 0, 100, 100) };
        var scene = new SceneBuffer(commands, [], [], [], [], rects, []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadLayerOpacity)
            throw new InvalidOperationException($"Expected BadLayerOpacity, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void StrokeWidthZero_ReportsBadStrokeParam()
    {
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var strokePathPayload = new StrokePathPayload { PathId = 0, PaintId = 0, TransformId = 0, StrokeWidth = 0f };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.StrokePath, strokePathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var paints = new Paint[] { Paint.Solid(0xFFFF0000) };
        var transforms = new Affine[] { Affine.Identity };
        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadStrokeParam)
            throw new InvalidOperationException($"Expected BadStrokeParam, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void StrokeWidthNegative_ReportsBadStrokeParam()
    {
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var strokePathPayload = new StrokePathPayload { PathId = 0, PaintId = 0, TransformId = 0, StrokeWidth = -5f };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.StrokePath, strokePathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var paints = new Paint[] { Paint.Solid(0xFFFF0000) };
        var transforms = new Affine[] { Affine.Identity };
        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadStrokeParam)
            throw new InvalidOperationException($"Expected BadStrokeParam, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void StrokeWidthNaN_ReportsBadStrokeParam()
    {
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var strokePathPayload = new StrokePathPayload { PathId = 0, PaintId = 0, TransformId = 0, StrokeWidth = float.NaN };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.StrokePath, strokePathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var paints = new Paint[] { Paint.Solid(0xFFFF0000) };
        var transforms = new Affine[] { Affine.Identity };
        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadStrokeParam)
            throw new InvalidOperationException($"Expected BadStrokeParam, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void TransformWithNaN_ReportsNonFiniteGeometry()
    {
        var transforms = new Affine[] { new Affine(double.NaN, 0, 0, 0, 1, 0) };
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var paints = new Paint[] { Paint.Solid(0xFFFF0000) };
        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.NonFiniteGeometry)
            throw new InvalidOperationException($"Expected NonFiniteGeometry, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void TransformWithInfinity_ReportsNonFiniteGeometry()
    {
        var transforms = new Affine[] { new Affine(double.PositiveInfinity, 0, 0, 0, 1, 0) };
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var paints = new Paint[] { Paint.Solid(0xFFFF0000) };
        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.NonFiniteGeometry)
            throw new InvalidOperationException($"Expected NonFiniteGeometry, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void MultipleErrors_ValidateAccumulatedReportsAllInOrder()
    {
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var popPayload = new PopLayerPayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.PopLayer, popPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [], [], [], [], [], []);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count < 2)
            throw new InvalidOperationException($"Expected at least 2 errors, got {report.Count}");

        bool hasBadFrameMarkers = false;
        bool hasUnbalancedLayer = false;
        foreach (var error in report.Errors)
        {
            if (error.ErrorCode == SceneValidationErrorCode.BadFrameMarkers)
                hasBadFrameMarkers = true;
            if (error.ErrorCode == SceneValidationErrorCode.UnbalancedLayerStack)
                hasUnbalancedLayer = true;
        }

        if (!hasBadFrameMarkers)
            throw new InvalidOperationException("Expected BadFrameMarkers error");
        if (!hasUnbalancedLayer)
            throw new InvalidOperationException("Expected UnbalancedLayerStack error");
    }

    private static BezPath CreateSquarePath()
    {
        var builder = BezPathBuilder.Begin();
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(100, 0));
        builder.LineTo(new Point(100, 100));
        builder.LineTo(new Point(0, 100));
        builder.Close();
        var path = builder.Build();
        builder.Dispose();
        return path;
    }

    private static (PathEntry entry, byte[] arena) CreatePathEntryForMoveToLineTo()
    {
        int verbCount = 2;
        int coordCount = 4;
        int arenaLength = 8 + verbCount + coordCount * 8;
        var arena = new byte[arenaLength];

        BitConverter.TryWriteBytes(arena.AsSpan(0, 4), verbCount);
        BitConverter.TryWriteBytes(arena.AsSpan(4, 4), coordCount);
        arena[8] = (byte)PathVerb.MoveTo;
        arena[9] = (byte)PathVerb.LineTo;
        BitConverter.TryWriteBytes(arena.AsSpan(10, 8), 0.0);
        BitConverter.TryWriteBytes(arena.AsSpan(18, 8), 0.0);
        BitConverter.TryWriteBytes(arena.AsSpan(26, 8), 100.0);
        BitConverter.TryWriteBytes(arena.AsSpan(34, 8), 0.0);

        var entry = new PathEntry(0, arenaLength, verbCount, coordCount);
        return (entry, arena);
    }

    [Test]
    public void InvalidGradientId_ReportsInvalidResourceId()
    {
        var gradientStops = new GradientStops[] { GradientStops.Create((0f, 0xFF000000), (1f, 0xFFFFFFFF)) };
        var paints = new Paint[] { Paint.LinearGradient(999) };
        var transforms = new Affine[] { Affine.Identity };
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], gradientStops);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.InvalidResourceId)
            throw new InvalidOperationException($"Expected InvalidResourceId, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void GradientWithSingleStop_ReportsBadGradient()
    {
        var invalidGradient = new GradientStops { Count = 1 };
        invalidGradient.SetStop(0, 0f, 0xFF000000);
        var gradientStops = new GradientStops[] { invalidGradient };
        var paints = new Paint[] { Paint.LinearGradient(0) };
        var transforms = new Affine[] { Affine.Identity };
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], gradientStops);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadGradient)
            throw new InvalidOperationException($"Expected BadGradient, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void GradientWithNonMonotoneOffsets_ReportsBadGradient()
    {
        var gradientStops = new GradientStops[] { GradientStops.Create((0f, 0xFF000000), (0.5f, 0xFF808080), (0.3f, 0xFFFFFFFF)) };
        var paints = new Paint[] { Paint.LinearGradient(0) };
        var transforms = new Affine[] { Affine.Identity };
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], gradientStops);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadGradient)
            throw new InvalidOperationException($"Expected BadGradient, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void GradientWithOffsetBelowZero_ReportsBadGradient()
    {
        var gradientStops = new GradientStops[] { GradientStops.Create((-0.1f, 0xFF000000), (1f, 0xFFFFFFFF)) };
        var paints = new Paint[] { Paint.LinearGradient(0) };
        var transforms = new Affine[] { Affine.Identity };
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], gradientStops);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadGradient)
            throw new InvalidOperationException($"Expected BadGradient, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void GradientWithOffsetAboveOne_ReportsBadGradient()
    {
        var gradientStops = new GradientStops[] { GradientStops.Create((0f, 0xFF000000), (1.5f, 0xFFFFFFFF)) };
        var paints = new Paint[] { Paint.LinearGradient(0) };
        var transforms = new Affine[] { Affine.Identity };
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], gradientStops);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count == 0)
            throw new InvalidOperationException("Expected errors but got none");
        if (report.Errors[0].ErrorCode != SceneValidationErrorCode.BadGradient)
            throw new InvalidOperationException($"Expected BadGradient, got {report.Errors[0].ErrorCode}");
    }

    [Test]
    public void ValidGradient_PassesValidation()
    {
        var gradientStops = new GradientStops[] { GradientStops.Create((0f, 0xFF000000), (0.5f, 0xFF808080), (1f, 0xFFFFFFFF)) };
        var paints = new Paint[] { Paint.LinearGradient(0) };
        var transforms = new Affine[] { Affine.Identity };
        var (pathEntry, pathArena) = CreatePathEntryForMoveToLineTo();

        var fillPathPayload = new FillPathPayload { PathId = 0, PaintId = 0, TransformId = 0, FillRule = 0 };
        var beginPayload = new BeginFramePayload();
        var endPayload = new EndFramePayload();
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.BeginFrame, beginPayload),
            new SceneCommand(SceneOpcode.FillPath, fillPathPayload),
            new SceneCommand(SceneOpcode.EndFrame, endPayload),
        };

        var scene = new SceneBuffer(commands, [pathEntry], pathArena, paints, transforms, [], gradientStops);

        var report = new SceneValidationReport(stackalloc SceneValidationError[256]);
        SceneValidator.ValidateAccumulated(scene, ref report);

        if (report.Count != 0)
            throw new InvalidOperationException($"Expected 0 errors, got {report.Count}");
    }
}
