using System;
using System.Buffers;

namespace Etch.Scene;

/// <summary>
/// An immutable, compact encoding of a vector-graphics scene. Built by
/// <see cref="SceneBuilder"/> and consumed by the CPU/GPU renderers.
/// </summary>
public sealed class SceneBuffer : IDisposable
{
    private readonly SceneCommand[] _commands;
    private readonly PathEntry[] _pathTable;
    private readonly byte[] _pathArena;
    private readonly Paint[] _paints;
    private readonly Geometry.Affine[] _transforms;
    private readonly Geometry.Rect[] _rects;
    private readonly GradientStops[] _gradientStops;
    private readonly MeshGradient[] _meshGradients;
    private readonly NoiseSpec[] _noiseSpecs;
    private readonly ColorFilter[] _colorFilters;
    private readonly bool _ownsArrays;
    private readonly int _commandCount;
    private readonly int _pathCount;
    private readonly int _pathArenaUsed;
    private readonly int _paintCount;
    private readonly int _transformCount;
    private readonly int _rectCount;
    private readonly int _gradientStopsCount;
    private readonly int _meshGradientCount;
    private readonly int _noiseSpecCount;
    private readonly int _colorFilterCount;
    private bool _disposed;

    public SceneBuffer(
        SceneCommand[] commands,
        PathEntry[] pathTable,
        byte[] pathArena,
        Paint[] paints,
        Geometry.Affine[] transforms,
        Geometry.Rect[] rects,
        GradientStops[] gradientStops)
    {
        _commands = commands;
        _pathTable = pathTable;
        _pathArena = pathArena;
        _paints = paints;
        _transforms = transforms;
        _rects = rects;
        _gradientStops = gradientStops;
        _meshGradients = Array.Empty<MeshGradient>();
        _noiseSpecs = Array.Empty<NoiseSpec>();
        _colorFilters = Array.Empty<ColorFilter>();
        _ownsArrays = false;
        _commandCount = commands?.Length ?? 0;
        _pathCount = pathTable?.Length ?? 0;
        _pathArenaUsed = pathArena?.Length ?? 0;
        _paintCount = paints?.Length ?? 0;
        _transformCount = transforms?.Length ?? 0;
        _rectCount = rects?.Length ?? 0;
        _gradientStopsCount = gradientStops?.Length ?? 0;
        _meshGradientCount = 0;
        _noiseSpecCount = 0;
        _colorFilterCount = 0;
    }

    internal SceneBuffer(
        SceneCommand[] commands,
        PathEntry[] pathTable,
        byte[] pathArena,
        Paint[] paints,
        Geometry.Affine[] transforms,
        Geometry.Rect[] rects,
        GradientStops[] gradientStops,
        MeshGradient[] meshGradients)
    {
        _commands = commands;
        _pathTable = pathTable;
        _pathArena = pathArena;
        _paints = paints;
        _transforms = transforms;
        _rects = rects;
        _gradientStops = gradientStops;
        _meshGradients = meshGradients;
        _noiseSpecs = Array.Empty<NoiseSpec>();
        _colorFilters = Array.Empty<ColorFilter>();
        _ownsArrays = false;
        _commandCount = commands?.Length ?? 0;
        _pathCount = pathTable?.Length ?? 0;
        _pathArenaUsed = pathArena?.Length ?? 0;
        _paintCount = paints?.Length ?? 0;
        _transformCount = transforms?.Length ?? 0;
        _rectCount = rects?.Length ?? 0;
        _gradientStopsCount = gradientStops?.Length ?? 0;
        _meshGradientCount = meshGradients?.Length ?? 0;
        _noiseSpecCount = 0;
        _colorFilterCount = 0;
    }

    internal SceneBuffer(
        SceneCommand[] commands,
        PathEntry[] pathTable,
        byte[] pathArena,
        Paint[] paints,
        Geometry.Affine[] transforms,
        Geometry.Rect[] rects,
        GradientStops[] gradientStops,
        MeshGradient[] meshGradients,
        NoiseSpec[] noiseSpecs)
    {
        _commands = commands;
        _pathTable = pathTable;
        _pathArena = pathArena;
        _paints = paints;
        _transforms = transforms;
        _rects = rects;
        _gradientStops = gradientStops;
        _meshGradients = meshGradients;
        _noiseSpecs = noiseSpecs;
        _colorFilters = Array.Empty<ColorFilter>();
        _ownsArrays = false;
        _commandCount = commands?.Length ?? 0;
        _pathCount = pathTable?.Length ?? 0;
        _pathArenaUsed = pathArena?.Length ?? 0;
        _paintCount = paints?.Length ?? 0;
        _transformCount = transforms?.Length ?? 0;
        _rectCount = rects?.Length ?? 0;
        _gradientStopsCount = gradientStops?.Length ?? 0;
        _meshGradientCount = meshGradients?.Length ?? 0;
        _noiseSpecCount = noiseSpecs?.Length ?? 0;
        _colorFilterCount = 0;
    }

    internal SceneBuffer(
        SceneCommand[] commands,
        PathEntry[] pathTable,
        byte[] pathArena,
        Paint[] paints,
        Geometry.Affine[] transforms,
        Geometry.Rect[] rects,
        GradientStops[] gradientStops,
        MeshGradient[] meshGradients,
        NoiseSpec[] noiseSpecs,
        ColorFilter[] colorFilters)
    {
        _commands = commands;
        _pathTable = pathTable;
        _pathArena = pathArena;
        _paints = paints;
        _transforms = transforms;
        _rects = rects;
        _gradientStops = gradientStops;
        _meshGradients = meshGradients;
        _noiseSpecs = noiseSpecs;
        _colorFilters = colorFilters;
        _ownsArrays = false;
        _commandCount = commands?.Length ?? 0;
        _pathCount = pathTable?.Length ?? 0;
        _pathArenaUsed = pathArena?.Length ?? 0;
        _paintCount = paints?.Length ?? 0;
        _transformCount = transforms?.Length ?? 0;
        _rectCount = rects?.Length ?? 0;
        _gradientStopsCount = gradientStops?.Length ?? 0;
        _meshGradientCount = meshGradients?.Length ?? 0;
        _noiseSpecCount = noiseSpecs?.Length ?? 0;
        _colorFilterCount = colorFilters?.Length ?? 0;
    }

    // Internal constructor for SceneBuilder — takes ownership of pooled arrays
    internal SceneBuffer(
        SceneCommand[] commands, int commandCount,
        PathEntry[] pathTable, int pathCount,
        byte[] pathArena, int pathArenaUsed,
        Paint[] paints, int paintCount,
        Geometry.Affine[] transforms, int transformCount,
        Geometry.Rect[] rects, int rectCount,
        GradientStops[] gradientStops, int gradientStopsCount,
        MeshGradient[] meshGradients, int meshGradientCount,
        NoiseSpec[] noiseSpecs, int noiseSpecCount,
        ColorFilter[] colorFilters, int colorFilterCount)
    {
        _commands = commands;
        _commandCount = commandCount;
        _pathTable = pathTable;
        _pathCount = pathCount;
        _pathArena = pathArena;
        _pathArenaUsed = pathArenaUsed;
        _paints = paints;
        _paintCount = paintCount;
        _transforms = transforms;
        _transformCount = transformCount;
        _rects = rects;
        _rectCount = rectCount;
        _gradientStops = gradientStops;
        _gradientStopsCount = gradientStopsCount;
        _meshGradients = meshGradients;
        _meshGradientCount = meshGradientCount;
        _noiseSpecs = noiseSpecs;
        _noiseSpecCount = noiseSpecCount;
        _colorFilters = colorFilters;
        _colorFilterCount = colorFilterCount;
        _ownsArrays = true;
    }

    /// <summary>All scene commands, in execution order.</summary>
    public ReadOnlySpan<SceneCommand> Commands => _commands.AsSpan(0, _commandCount);

    /// <summary>Number of commands in the scene.</summary>
    public int CommandCount => _commandCount;

    public int PathCount => _pathCount;
    public int PathArenaLength => _pathArenaUsed;
#pragma warning disable CA1819
    public byte[] PathArenaBytes => _pathArena;
#pragma warning restore CA1819
    public int PaintCount => _paintCount;
    public int TransformCount => _transformCount;
    public int RectCount => _rectCount;
    public int GradientStopsCount => _gradientStopsCount;
    public int MeshGradientCount => _meshGradientCount;
    public int NoiseSpecCount => _noiseSpecCount;
    public int ColorFilterCount => _colorFilterCount;

    public bool TryGetPath(int pathId, out PathData path)
    {
        if (pathId < 0 || pathId >= _pathCount)
        {
            path = default;
            return false;
        }

        ref readonly var entry = ref _pathTable[pathId];
        Span<byte> data = _pathArena.AsSpan(entry.ByteOffset, entry.ByteLength);

        int verbCount = ReadInt(data[..4]);
        int coordCount = ReadInt(data[4..8]);

        // Use arena-backed BezPath to avoid allocating temporary arrays
        path = new PathData(new Geometry.BezPath(
            _pathArena,
            entry.ByteOffset + 8,
            verbCount,
            entry.ByteOffset + 8 + verbCount,
            coordCount));
        return true;
    }

    public Paint GetPaint(int paintId) => _paints[paintId];
    public Geometry.Affine GetTransform(int transformId) => _transforms[transformId];
    public Geometry.Rect GetRect(int rectId) => _rects[rectId];
    public GradientStops GetGradientStops(int gradientId) => _gradientStops[gradientId];

    public MeshGradient GetMeshGradient(int meshGradientId) => _meshGradients[meshGradientId];

    public NoiseSpec GetNoiseSpec(int noiseId) => _noiseSpecs[noiseId];

    public ColorFilter GetColorFilter(int filterId) => _colorFilters[filterId];

    private static int ReadInt(ReadOnlySpan<byte> data)
    {
        return data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    }

    public void Dispose()
    {
        if (_disposed || !_ownsArrays)
        {
            return;
        }
        _disposed = true;

        ArrayPool<SceneCommand>.Shared.Return(_commands, clearArray: false);
        ArrayPool<PathEntry>.Shared.Return(_pathTable, clearArray: false);
        ArrayPool<byte>.Shared.Return(_pathArena, clearArray: false);
        ArrayPool<Paint>.Shared.Return(_paints, clearArray: false);
        ArrayPool<Geometry.Affine>.Shared.Return(_transforms, clearArray: false);
        ArrayPool<Geometry.Rect>.Shared.Return(_rects, clearArray: false);
        ArrayPool<GradientStops>.Shared.Return(_gradientStops, clearArray: false);
    }
}

public readonly struct PathData
{
    public readonly Geometry.BezPath Path;

    public PathData(Geometry.BezPath path)
    {
        Path = path;
    }
}
