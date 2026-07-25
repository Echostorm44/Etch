using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Etch.Primitives;

namespace Etch.Scene;

public enum FillRule
{
    NonZero = 0,
    EvenOdd = 1,
}

public enum BlendMode
{
    SrcOver = 0,
    Src = 1,
    DstOver = 2,
    Dst = 3,
    Clear = 4,
}

public enum ClipMode : byte
{
    Intersect = 0,
    Difference = 1,
}

public enum LayerFlags : byte
{
    None = 0,
    Isolated = 1,
    Offscreen = 2,
}

public readonly struct PathEntry
{
    public readonly int ByteOffset;
    public readonly int ByteLength;
    public readonly int VerbCount;
    public readonly int CoordCount;

    public PathEntry(int byteOffset, int byteLength, int verbCount, int coordCount)
    {
        ByteOffset = byteOffset;
        ByteLength = byteLength;
        VerbCount = verbCount;
        CoordCount = coordCount;
    }
}

/// <summary>
/// A ref-struct builder for constructing <see cref="SceneBuffer"/> instances.
/// Uses <see cref="System.Buffers.ArrayPool{T}"/> internally for zero-allocation
/// warm paths. Must be disposed after <see cref="End"/> or on error.
/// </summary>
public ref struct SceneBuilder
{
#pragma warning disable CA2213
    private PooledBuffer<SceneCommand> _commandBuffer;
    private int _commandCount;

    private PooledBuffer<byte> _pathArena;
    private int _pathArenaUsed;
    private PooledBuffer<PathEntry> _pathTable;
    private int _pathCount;

    private PooledBuffer<Paint> _paintTable;
    private int _paintCount;

    private PooledBuffer<Geometry.Affine> _transformTable;
    private int _transformCount;

    private PooledBuffer<Geometry.Rect> _rectTable;
    private int _rectCount;

    private PooledBuffer<GradientStops> _gradientStopsTable;
    private int _gradientStopsCount;

    private List<MeshGradient> _meshGradientTable;
    private int _meshGradientCount;

    private PooledBuffer<NoiseSpec> _noiseSpecTable;
    private int _noiseSpecCount;

    private PooledBuffer<ColorFilter> _colorFilterTable;
    private int _colorFilterCount;
    private int _colorFilterDepth;

    private int _clipDepth;
#pragma warning restore CA2213

    private bool _ended;

    /// <summary>Starts a new scene builder with the given command capacity.</summary>
    public static SceneBuilder Begin(int estimatedCommands = 256)
    {
        const int InitialPathArenaSize = 4096;
        const int InitialPathTableSize = 64;
        const int InitialPaintTableSize = 64;
        const int InitialTransformTableSize = 64;
        const int InitialRectTableSize = 64;
        const int InitialGradientStopsTableSize = 64;

        return new SceneBuilder
        {
            _commandBuffer = PooledBuffer<SceneCommand>.Rent(estimatedCommands),
            _commandCount = 0,
            _pathArena = PooledBuffer<byte>.Rent(InitialPathArenaSize),
            _pathArenaUsed = 0,
            _pathTable = PooledBuffer<PathEntry>.Rent(InitialPathTableSize),
            _pathCount = 0,
            _paintTable = PooledBuffer<Paint>.Rent(InitialPaintTableSize),
            _paintCount = 0,
            _transformTable = PooledBuffer<Geometry.Affine>.Rent(InitialTransformTableSize),
            _transformCount = 0,
            _rectTable = PooledBuffer<Geometry.Rect>.Rent(InitialRectTableSize),
            _rectCount = 0,
            _gradientStopsTable = PooledBuffer<GradientStops>.Rent(InitialGradientStopsTableSize),
            _gradientStopsCount = 0,
            _meshGradientTable = new List<MeshGradient>(4),
            _meshGradientCount = 0,
            _noiseSpecTable = PooledBuffer<NoiseSpec>.Rent(16),
            _noiseSpecCount = 0,
            _colorFilterTable = PooledBuffer<ColorFilter>.Rent(8),
            _colorFilterCount = 0,
            _colorFilterDepth = 0,
            _ended = false,
        };
    }

    public void BeginFrame()
    {
        EnsureNotEnded();
        WriteCommand(new SceneCommand(SceneOpcode.BeginFrame, new BeginFramePayload()));
    }

    public void EndFrame()
    {
        EnsureNotEnded();
        WriteCommand(new SceneCommand(SceneOpcode.EndFrame, new EndFramePayload()));
    }

    public int AddPath(Geometry.BezPath path)
    {
        EnsureNotEnded();

        int pathId = _pathCount++;
        EnsurePathTableCapacity();

        int verbCount = path.VerbCount;
        int coordCount = 0;
        int coordCountFromEnumerate = 0;

        int headerSize = 8;
        int estimatedCoordCount = verbCount * 6;
        int estimatedLength = headerSize + verbCount + estimatedCoordCount * 8;
        EnsurePathArenaCapacity(estimatedLength);

        int byteOffset = _pathArenaUsed;
        Span<byte> arena = _pathArena.Span;
        Span<byte> dest = arena.Slice(byteOffset, estimatedLength);

        WriteInt(dest[..4], verbCount);
        int verbIdx = 8;
        int coordIdx = 8 + verbCount;

        foreach (var seg in path.Iterate())
        {
            dest[verbIdx++] = (byte)seg.Verb;

            switch (seg.Verb)
            {
                case Geometry.PathVerb.MoveTo:
                case Geometry.PathVerb.LineTo:
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx, 8), seg.End.X);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 8, 8), seg.End.Y);
                    coordIdx += 16;
                    coordCountFromEnumerate += 2;
                    break;
                case Geometry.PathVerb.QuadTo:
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx, 8), seg.Control0.X);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 8, 8), seg.Control0.Y);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 16, 8), seg.End.X);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 24, 8), seg.End.Y);
                    coordIdx += 32;
                    coordCountFromEnumerate += 4;
                    break;
                case Geometry.PathVerb.CubicTo:
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx, 8), seg.Control0.X);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 8, 8), seg.Control0.Y);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 16, 8), seg.Control1.X);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 24, 8), seg.Control1.Y);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 32, 8), seg.End.X);
                    BitConverter.TryWriteBytes(dest.Slice(coordIdx + 40, 8), seg.End.Y);
                    coordIdx += 48;
                    coordCountFromEnumerate += 6;
                    break;
            }
        }

        coordCount = coordCountFromEnumerate;
        int actualLength = headerSize + verbCount + coordCount * 8;
        WriteInt(dest[4..8], coordCount);

        if (actualLength < estimatedLength)
        {
            dest = arena.Slice(byteOffset, actualLength);
        }

        _pathTable.Span[pathId] = new PathEntry(byteOffset, actualLength, verbCount, coordCount);
        _pathArenaUsed += actualLength;

        return pathId;
    }

    private static void WriteInt(Span<byte> dest, int value)
    {
        dest[0] = (byte)value;
        dest[1] = (byte)(value >> 8);
        dest[2] = (byte)(value >> 16);
        dest[3] = (byte)(value >> 24);
    }

    public int AddPaint(Paint paint)
    {
        EnsureNotEnded();
        int id = _paintCount++;
        EnsurePaintTableCapacity();
        _paintTable.Span[id] = paint;
        return id;
    }

    public int AddTransform(Geometry.Affine transform)
    {
        EnsureNotEnded();
        int id = _transformCount++;
        EnsureTransformTableCapacity();
        _transformTable.Span[id] = transform;
        return id;
    }

    public int AddGradientStops(GradientStops gradientStops)
    {
        EnsureNotEnded();
        int id = _gradientStopsCount++;
        EnsureGradientStopsTableCapacity();
        _gradientStopsTable.Span[id] = gradientStops;
        return id;
    }

    public int AddMeshGradient(MeshGradient mesh)
    {
        EnsureNotEnded();
        int id = _meshGradientCount++;
        if (id >= _meshGradientTable.Count)
        {
            _meshGradientTable.Add(mesh);
        }
        else
        {
            _meshGradientTable[id] = mesh;
        }
        return id;
    }

    public int AddNoiseSpec(NoiseSpec spec)
    {
        EnsureNotEnded();
        int id = _noiseSpecCount++;
        EnsureNoiseSpecTableCapacity();
        _noiseSpecTable.Span[id] = spec;
        return id;
    }

    public int AddColorFilter(ColorFilter filter)
    {
        EnsureNotEnded();
        int id = _colorFilterCount++;
        EnsureColorFilterTableCapacity();
        _colorFilterTable.Span[id] = filter;
        return id;
    }

    public void PushColorFilter(int filterId)
    {
        EnsureNotEnded();
        if (filterId < 0 || filterId >= _colorFilterCount)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidResourceId, $"Invalid color filter ID: {filterId}");
        if (++_colorFilterDepth > 16)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidResourceId, "Color filter stack depth exceeds 16");
        WriteCommand(new SceneCommand(SceneOpcode.PushColorFilter, new PushColorFilterPayload { ColorFilterId = filterId }));
    }

    public void PopColorFilter()
    {
        EnsureNotEnded();
        if (--_colorFilterDepth < 0)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidResourceId, "PopColorFilter without matching PushColorFilter");
        WriteCommand(new SceneCommand(SceneOpcode.PopColorFilter, new PopColorFilterPayload()));
    }

    private int AddRect(Geometry.Rect rect)
    {
        int id = _rectCount++;
        EnsureRectTableCapacity();
        _rectTable.Span[id] = rect;
        return id;
    }

    public void PushLayer(Geometry.Rect bounds, float opacity, BlendMode blend, LayerFlags flags = LayerFlags.None)
    {
        EnsureNotEnded();
        int layerId = AddRect(bounds);
        WriteCommand(new SceneCommand(SceneOpcode.PushLayer, new PushLayerPayload
        {
            LayerId = layerId,
            Opacity = opacity,
            BlendMode = (byte)blend,
            Flags = (byte)flags
        }));
    }

    public void PopLayer()
    {
        EnsureNotEnded();
        WriteCommand(new SceneCommand(SceneOpcode.PopLayer, new PopLayerPayload()));
    }

    public void PushClip(int pathId, FillRule rule, ClipMode clipMode = ClipMode.Intersect)
    {
        EnsureNotEnded();
        ValidatePathId(pathId);
        if (++_clipDepth > 16)
            Etch.Panic.Invariant(Etch.PanicCodes.ClipStackTooDeep, $"Clip depth {_clipDepth} exceeds limit of 16");
        WriteCommand(new SceneCommand(SceneOpcode.PushClip, new PushClipPayload { ClipId = pathId, FillRule = (byte)rule, ClipMode = (byte)clipMode }));
    }

    public void PopClip()
    {
        EnsureNotEnded();
        if (--_clipDepth < 0)
            Etch.Panic.Invariant(Etch.PanicCodes.UnbalancedClipStack, "PopClip without matching PushClip");
        WriteCommand(new SceneCommand(SceneOpcode.PopClip, new PopClipPayload()));
    }

    public void SetTransform(int transformId)
    {
        EnsureNotEnded();
        ValidateTransformId(transformId);
        WriteCommand(new SceneCommand(SceneOpcode.SetTransform, new SetTransformPayload { TransformId = transformId }));
    }

    public void FillPath(int pathId, int paintId, int transformId, FillRule rule)
    {
        EnsureNotEnded();
        ValidatePathId(pathId);
        ValidatePaintId(paintId);
        ValidateTransformId(transformId);
        WriteCommand(new SceneCommand(SceneOpcode.FillPath, new FillPathPayload
        {
            PathId = pathId,
            PaintId = paintId,
            TransformId = transformId,
            FillRule = (byte)rule
        }));
    }

    public void StrokePath(int pathId, int paintId, int transformId, float width, StrokeStyle style)
    {
        EnsureNotEnded();
        ValidatePathId(pathId);
        ValidatePaintId(paintId);
        ValidateTransformId(transformId);
        WriteCommand(new SceneCommand(SceneOpcode.StrokePath, new StrokePathPayload
        {
            PathId = pathId,
            PaintId = paintId,
            TransformId = transformId,
            StrokeWidth = width
        }));
    }

    public void FillRect(Geometry.Rect r, int paintId, int transformId)
    {
        EnsureNotEnded();
        ValidatePaintId(paintId);
        ValidateTransformId(transformId);
        int rectId = AddRect(r);
        WriteCommand(new SceneCommand(SceneOpcode.FillRect, new FillRectPayload
        {
            RectId = rectId,
            PaintId = paintId,
            TransformId = transformId
        }));
    }

    public void DrawImage(int imageId, int paintId, int transformId)
    {
        EnsureNotEnded();
        WriteCommand(new SceneCommand(SceneOpcode.DrawImage, new DrawImagePayload
        {
            ImageId = imageId,
            PaintId = paintId,
            TransformId = transformId
        }));
    }

    public void DrawGlyphRun(int glyphRunId, int paintId, int transformId)
    {
        EnsureNotEnded();
        WriteCommand(new SceneCommand(SceneOpcode.DrawGlyphRun, new DrawGlyphRunPayload
        {
            GlyphRunId = glyphRunId,
            PaintId = paintId,
            TransformId = transformId
        }));
    }

    public void DrawShadow(int pathId, int paintId, int transformId, Etch.Geometry.Vec2 offset, float blurRadius, uint shadowColor)
    {
        EnsureNotEnded();
        ValidatePathId(pathId);
        ValidatePaintId(paintId);
        ValidateTransformId(transformId);
        WriteCommand(new SceneCommand(SceneOpcode.DrawShadow, new DrawShadowPayload
        {
            PathId = pathId,
            PaintId = paintId,
            TransformId = transformId,
            ShadowOffsetX = (float)offset.X,
            ShadowOffsetY = (float)offset.Y,
            BlurRadius = blurRadius,
            ShadowColor = shadowColor
        }));
    }

    public void DrawMaterialRegion(Geometry.Rect bounds, float radius, int transformId)
    {
        EnsureNotEnded();
        ValidateTransformId(transformId);
        int rectId = AddRect(bounds);
        WriteCommand(new SceneCommand(SceneOpcode.DrawMaterialRegion, new DrawMaterialRegionPayload
        {
            RectId = rectId,
            TransformId = transformId,
            Radius = radius
        }));
    }

    public void SetBlendMode(BlendMode mode)
    {
        EnsureNotEnded();
        WriteCommand(new SceneCommand(SceneOpcode.SetBlendMode, new SetBlendModePayload { BlendMode = (byte)mode }));
    }

    public void FillSector(float centerX, float centerY, float outerRadius, float innerRadius,
        float startRad, float sweepRad, int paintId, int transformId)
    {
        EnsureNotEnded();
        ValidatePaintId(paintId);
        ValidateTransformId(transformId);
        WriteCommand(new SceneCommand(SceneOpcode.FillSector, new FillSectorPayload
        {
            CenterX = centerX,
            CenterY = centerY,
            OuterRadius = outerRadius,
            InnerRadius = innerRadius,
            StartRad = startRad,
            SweepRad = sweepRad,
            PaintId = paintId,
            TransformId = transformId
        }));
    }

    public SceneBuffer End()
    {
        EnsureNotEnded();
        _ended = true;

        var commands = RentAndCopy(_commandBuffer.Span[.._commandCount]);
        _commandBuffer.Dispose();

        var paths = RentAndCopy(_pathTable.Span[.._pathCount]);
        _pathTable.Dispose();

        var pathArena = RentAndCopy(_pathArena.Span[.._pathArenaUsed]);
        _pathArena.Dispose();

        var paints = RentAndCopy(_paintTable.Span[.._paintCount]);
        _paintTable.Dispose();

        var transforms = RentAndCopy(_transformTable.Span[.._transformCount]);
        _transformTable.Dispose();

        var rects = RentAndCopy(_rectTable.Span[.._rectCount]);
        _rectTable.Dispose();

        var gradientStops = RentAndCopy(_gradientStopsTable.Span[.._gradientStopsCount]);
        _gradientStopsTable.Dispose();

        MeshGradient[] meshGradients;
        if (_meshGradientCount == 0)
        {
            meshGradients = Array.Empty<MeshGradient>();
        }
        else
        {
            meshGradients = ArrayPool<MeshGradient>.Shared.Rent(_meshGradientCount);
            for (int i = 0; i < _meshGradientCount; i++)
            {
                meshGradients[i] = _meshGradientTable[i];
            }
        }

        var noiseSpecs = RentAndCopy(_noiseSpecTable.Span[.._noiseSpecCount]);
        _noiseSpecTable.Dispose();

        var colorFilters = RentAndCopy(_colorFilterTable.Span[.._colorFilterCount]);
        _colorFilterTable.Dispose();

        var buffer = new SceneBuffer(
            commands, _commandCount,
            paths, _pathCount,
            pathArena, _pathArenaUsed,
            paints, _paintCount,
            transforms, _transformCount,
            rects, _rectCount,
            gradientStops, _gradientStopsCount,
            meshGradients, _meshGradientCount,
            noiseSpecs, _noiseSpecCount,
            colorFilters, _colorFilterCount);

#if DEBUG
        SceneValidator.ValidateStrict(buffer);
#endif


        return buffer;
    }

    private static T[] RentAndCopy<T>(ReadOnlySpan<T> source)
    {
        if (source.Length == 0)
        {
            return Array.Empty<T>();
        }
        T[] rented = ArrayPool<T>.Shared.Rent(source.Length);
        source.CopyTo(rented);
        return rented;
    }

    public void Dispose()
    {
        if (!_ended)
        {
            _ended = true;
            _commandBuffer.Dispose();
            _pathTable.Dispose();
            _pathArena.Dispose();
            _paintTable.Dispose();
            _transformTable.Dispose();
            _rectTable.Dispose();
            _gradientStopsTable.Dispose();
            _noiseSpecTable.Dispose();
            _colorFilterTable.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCommand(SceneCommand command)
    {
        if (_commandCount >= _commandBuffer.Span.Length)
            Etch.Panic.Invariant(Etch.PanicCodes.BufferOverflow, "Scene command buffer overflow");
        _commandBuffer.Span[_commandCount++] = command;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotEnded()
    {
        if (_ended)
            Etch.Panic.Invariant(Etch.PanicCodes.SceneBuilderConsumed, "SceneBuilder used after End()");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidatePathId(int pathId)
    {
        if (pathId < 0 || pathId >= _pathCount)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSceneResourceId, $"Invalid path ID: {pathId}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidatePaintId(int paintId)
    {
        if (paintId < 0 || paintId >= _paintCount)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSceneResourceId, $"Invalid paint ID: {paintId}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateTransformId(int transformId)
    {
        if (transformId < 0 || transformId >= _transformCount)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSceneResourceId, $"Invalid transform ID: {transformId}");
    }

    private void EnsurePathTableCapacity()
    {
        if (_pathCount >= _pathTable.Length)
        {
            var newTable = PooledBuffer<PathEntry>.Rent(_pathTable.Length * 2);
            _pathTable.Span.CopyTo(newTable.Span);
            _pathTable.Dispose();
            _pathTable = newTable;
        }
    }

    private void EnsurePathArenaCapacity(int additionalBytes)
    {
        if (_pathArenaUsed + additionalBytes > _pathArena.Length)
        {
            var newArena = PooledBuffer<byte>.Rent(Math.Max(_pathArena.Length * 2, _pathArenaUsed + additionalBytes));
            _pathArena.Span[.._pathArenaUsed].CopyTo(newArena.Span);
            _pathArena.Dispose();
            _pathArena = newArena;
        }
    }

    private void EnsurePaintTableCapacity()
    {
        if (_paintCount >= _paintTable.Length)
        {
            var newTable = PooledBuffer<Paint>.Rent(_paintTable.Length * 2);
            _paintTable.Span.CopyTo(newTable.Span);
            _paintTable.Dispose();
            _paintTable = newTable;
        }
    }

    private void EnsureTransformTableCapacity()
    {
        if (_transformCount >= _transformTable.Length)
        {
            var newTable = PooledBuffer<Geometry.Affine>.Rent(_transformTable.Length * 2);
            _transformTable.Span.CopyTo(newTable.Span);
            _transformTable.Dispose();
            _transformTable = newTable;
        }
    }

    private void EnsureRectTableCapacity()
    {
        if (_rectCount >= _rectTable.Length)
        {
            var newTable = PooledBuffer<Geometry.Rect>.Rent(_rectTable.Length * 2);
            _rectTable.Span.CopyTo(newTable.Span);
            _rectTable.Dispose();
            _rectTable = newTable;
        }
    }

    private void EnsureGradientStopsTableCapacity()
    {
        if (_gradientStopsCount >= _gradientStopsTable.Length)
        {
            var newTable = PooledBuffer<GradientStops>.Rent(_gradientStopsTable.Length * 2);
            _gradientStopsTable.Span.CopyTo(newTable.Span);
            _gradientStopsTable.Dispose();
            _gradientStopsTable = newTable;
        }
    }

    private void EnsureNoiseSpecTableCapacity()
    {
        if (_noiseSpecCount >= _noiseSpecTable.Length)
        {
            var newTable = PooledBuffer<NoiseSpec>.Rent(_noiseSpecTable.Length * 2);
            _noiseSpecTable.Span.CopyTo(newTable.Span);
            _noiseSpecTable.Dispose();
            _noiseSpecTable = newTable;
        }
    }

    private void EnsureColorFilterTableCapacity()
    {
        if (_colorFilterCount >= _colorFilterTable.Length)
        {
            var newTable = PooledBuffer<ColorFilter>.Rent(_colorFilterTable.Length * 2);
            _colorFilterTable.Span.CopyTo(newTable.Span);
            _colorFilterTable.Dispose();
            _colorFilterTable = newTable;
        }
    }
}