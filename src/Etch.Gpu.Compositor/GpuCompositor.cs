using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Geometry;
using Etch.Gpu.Native;
using Etch.Gpu.Compositor.Pipelines;
using Etch.Gpu.Descriptors;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;

namespace Etch.Gpu.Compositor;

/// <summary>
/// End-to-end GPU compositor for Etch scenes using the strip-coverage pipeline.
/// Orchestrates: classify → strip emit → upload → render → readback.
/// </summary>
public sealed unsafe class GpuCompositor : IDisposable
{
    private readonly Device _device;
    private readonly StripCoverageGpuPipeline _pipeline;
    private readonly StripBufferUploader _stripUploader;
    private readonly Buffer _perFrameUniform;
    private readonly Buffer _unitQuadVertex;
    private bool _disposed;

    // RenderToRgba8 offscreen cache
    private Texture _offscreenTexture;
    private TextureView _offscreenView;
    private Buffer _readbackBuffer;
    private int _offscreenWidth;
    private int _offscreenHeight;

    // RecordRenderPass scene cache
    private SceneBuffer? _cachedScene;
    private int _cachedSceneWidth;
    private int _cachedSceneHeight;
    private uint _cachedGridWidth;
    private uint _cachedGridHeight;
    private Buffer _cachedPaintBuffer;
    private Buffer _cachedGradientBuffer;
    private Buffer _cachedStripsBuffer;
    private Buffer _cachedCoverageBuffer;
    private long _cachedStripsSize;
    private long _cachedCoverageSize;
    private uint _cachedInstanceCount;

    public uint LastStripCount => _cachedInstanceCount;

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private struct PerFrameData
    {
        public float SurfaceWidth;
        public float SurfaceHeight;
        public uint TileWidth;
        public uint TileHeight;
        public uint GridWidth;
        public uint GridHeight;
    }

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private struct PaintData
    {
        public float R, G, B, A;
        public uint Kind;
        public uint GradientStart;
        public uint GradientCount;
        public uint _pad;
        public float P0X, P0Y;
        public float P1X, P1Y;

        public const uint KindSolid = 0;
        public const uint KindLinearGradient = 1;
        public const uint KindRadialGradient = 2;

        public static PaintData FromSolid(uint argb)
        {
            float a = ((argb >> 24) & 0xFF) / 255.0f;
            float r = ((argb >> 16) & 0xFF) / 255.0f;
            float g = ((argb >> 8) & 0xFF) / 255.0f;
            float b = (argb & 0xFF) / 255.0f;
            return new PaintData
            {
                R = r * a,
                G = g * a,
                B = b * a,
                A = a,
                Kind = KindSolid,
                GradientStart = 0,
                GradientCount = 0,
                P0X = 0,
                P0Y = 0,
                P1X = 0,
                P1Y = 0
            };
        }
    }

    public GpuCompositor(Device device)
    {
        _device = device;
        _pipeline = new StripCoverageGpuPipeline(device);
        _stripUploader = new StripBufferUploader(device);

        _perFrameUniform = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
            Size = (ulong)sizeof(PerFrameData)
        });

        _unitQuadVertex = CreateUnitQuadVertexBuffer(device);
    }

    public byte[] RenderToRgba8(SceneBuffer scene, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width <= 0 || height <= 0)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Render dimensions must be positive");

        // 1. Classify and emit strips
        var grid = new TileGrid<TTile8>(width, height);
        var classified = ClassifyScene(scene, grid);
        var stripBuffer = StripEmitter.Emit(scene, classified, grid);

        // 2. Build paint table
        var (paintBuffer, gradientBuffer) = BuildPaintBufferWithGradients(scene);

        // 3. Upload strip data
        var stripGpuBuffers = _stripUploader.Upload(stripBuffer);
        ValidateBuffer("stripsBuffer", stripGpuBuffers.Strips);
        ValidateBuffer("coverageBuffer", stripGpuBuffers.Coverage);

        // 4. Upload per-frame uniform
        var perFrame = new PerFrameData
        {
            SurfaceWidth = width,
            SurfaceHeight = height,
            TileWidth = (uint)TTile8.Width,
            TileHeight = (uint)TTile8.Height,
            GridWidth = (uint)grid.TileCountX,
            GridHeight = (uint)grid.TileCountY
        };
        var perFrameSpan = new ReadOnlySpan<byte>(&perFrame, sizeof(PerFrameData));
        ValidateBuffer("perFrameUniform", _perFrameUniform);
        _device.Queue.WriteBuffer(_perFrameUniform, 0, perFrameSpan);

        // 5. Create or reuse offscreen render target
        uint bytesPerRow = (uint)(width * 4);
        uint alignedRow = (bytesPerRow + 255u) & ~255u;
        var (texture, texView) = GetOrCreateOffscreenTarget(width, height);
        var readback = GetOrCreateReadbackBuffer(alignedRow * (uint)height);

        // 6. Record render pass

        using var encoder = _device.CreateCommandEncoder();
        var colorAttachment = new RenderPassColorAttachment
        {
            View = (nint)texView.Handle,
            DepthSlice = 0xFFFFFFFFu,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 0 }
        };
        var passDesc = new RenderPassDescriptor
        {
            ColorAttachmentCount = (UIntPtr)1,
            ColorAttachments = (nint)(&colorAttachment)
        };

        using var pass = encoder.BeginRenderPass(passDesc);
        uint instanceCount = stripGpuBuffers.StripCount;
        if (instanceCount > 0)
        {
            _pipeline.Record(pass, _unitQuadVertex, stripGpuBuffers, paintBuffer, gradientBuffer, _perFrameUniform, instanceCount);
        }
        pass.End();

        // 7. Copy to readback buffer
        if (instanceCount > 0)
        {
            encoder.CopyTextureToBuffer(
                texture,
                0,
                new WGPUOrigin3D(),
                readback,
                new WGPUTexelCopyBufferLayout { Offset = 0, BytesPerRow = alignedRow, RowsPerImage = (uint)height },
                new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 });
        }

        var cb = encoder.Finish();
        var cmds = new ReadOnlySpan<CommandBuffer>(in cb);
        _device.Queue.Submit(cmds);
        cb.Dispose();

        // 8. Read back
        var result = new byte[width * height * 4];
        if (instanceCount > 0)
        {
            readback.MapSync(_device, MapMode.Read, 0, alignedRow * (uint)height);
            var mapped = readback.GetConstMappedRange(0, alignedRow * (uint)height);
            if (!mapped.IsEmpty)
            {
                fixed (byte* dst = result)
                {
                    for (int y = 0; y < height; y++)
                    {
                        var srcPtr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(mapped)) + y * alignedRow;
                        System.Buffer.MemoryCopy(srcPtr, dst + y * bytesPerRow, bytesPerRow, bytesPerRow);
                    }
                }
            }
            readback.Unmap();
        }

        paintBuffer.Dispose();
        if (!gradientBuffer.IsInvalid)
        {
            gradientBuffer.Dispose();
        }

        return result;
    }

    /// <summary>
    /// Records the strip-coverage geometry pass into an existing <paramref name="pass"/>.
    /// Caches GPU buffers by scene reference so static scenes skip classify/emit/upload.
    /// </summary>
    public Buffer RecordRenderPass(RenderPass pass, SceneBuffer scene, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width <= 0 || height <= 0)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Render dimensions must be positive");

        bool sceneChanged = !ReferenceEquals(scene, _cachedScene)
            || width != _cachedSceneWidth
            || height != _cachedSceneHeight;

        if (sceneChanged)
        {
            // Dispose old cached buffers
            if (!_cachedPaintBuffer.IsInvalid) { _cachedPaintBuffer.Dispose(); _cachedPaintBuffer = default; }
            if (!_cachedGradientBuffer.IsInvalid) { _cachedGradientBuffer.Dispose(); _cachedGradientBuffer = default; }
            if (!_cachedStripsBuffer.IsInvalid) { _cachedStripsBuffer.Dispose(); _cachedStripsBuffer = default; }
            if (!_cachedCoverageBuffer.IsInvalid) { _cachedCoverageBuffer.Dispose(); _cachedCoverageBuffer = default; }

            var grid = new TileGrid<TTile8>(width, height);
            var classified = ClassifyScene(scene, grid);
            var stripBuffer = StripEmitter.Emit(scene, classified, grid);

            string debugPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "compositor-debug.txt");
            System.IO.File.AppendAllText(debugPath, $"[{DateTime.Now:HH:mm:ss.fff}] sceneCmds={scene.Commands.Length} entries={classified.AllEntries.Length} strips={stripBuffer.StripCount}\n");

            var (paintBuffer, gradientBuffer) = BuildPaintBufferWithGradients(scene);
            _cachedPaintBuffer = paintBuffer;
            _cachedGradientBuffer = gradientBuffer;

            // Create persistent strip/coverage buffers
            long stripsSize = stripBuffer.StripCount * (long)sizeof(Strip);
            long coverageSize = stripBuffer.CoverageBytes.Length;
            long alignedCoverageSize = (coverageSize + 3) & ~3;

            _cachedStripsBuffer = CreatePersistentBuffer(stripsSize);
            _cachedCoverageBuffer = CreatePersistentBuffer(alignedCoverageSize);
            _cachedStripsSize = stripsSize;
            _cachedCoverageSize = coverageSize;

            if (stripsSize > 0)
            {
                var stripsBytes = MemoryMarshal.AsBytes(stripBuffer.Strips);
                _device.Queue.WriteBuffer(_cachedStripsBuffer, 0, stripsBytes);
            }

            if (coverageSize > 0)
            {
                var coverageBytes = MemoryMarshal.AsBytes(stripBuffer.CoverageBytes);
                int alignedSize = (int)((coverageSize + 3) & ~3);
                if (alignedSize == coverageSize)
                {
                    _device.Queue.WriteBuffer(_cachedCoverageBuffer, 0, coverageBytes);
                }
                else
                {
                    byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(alignedSize);
                    try
                    {
                        var padded = rented.AsSpan(0, alignedSize);
                        coverageBytes.CopyTo(padded);
                        padded.Slice((int)coverageSize).Clear();
                        _device.Queue.WriteBuffer(_cachedCoverageBuffer, 0, padded);
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(rented);
                    }
                }
            }

            _cachedInstanceCount = (uint)stripBuffer.StripCount;
            _cachedGridWidth = (uint)grid.TileCountX;
            _cachedGridHeight = (uint)grid.TileCountY;
            _cachedScene = scene;
            _cachedSceneWidth = width;
            _cachedSceneHeight = height;
        }

        // Update per-frame uniform (dimensions may have changed even if scene is the same)
        var perFrame = new PerFrameData
        {
            SurfaceWidth = width,
            SurfaceHeight = height,
            TileWidth = (uint)TTile8.Width,
            TileHeight = (uint)TTile8.Height,
            GridWidth = _cachedGridWidth,
            GridHeight = _cachedGridHeight
        };
        var perFrameSpan = new ReadOnlySpan<byte>(&perFrame, sizeof(PerFrameData));
        ValidateBuffer("perFrameUniform", _perFrameUniform);
        _device.Queue.WriteBuffer(_perFrameUniform, 0, perFrameSpan);

        if (_cachedInstanceCount > 0)
        {
            var stripGpuBuffers = new StripGpuBuffers(_cachedStripsBuffer, _cachedCoverageBuffer, _cachedInstanceCount);
            _pipeline.Record(pass, _unitQuadVertex, stripGpuBuffers, _cachedPaintBuffer, _cachedGradientBuffer, _perFrameUniform, _cachedInstanceCount);
        }

        return _cachedPaintBuffer;
    }

    private static ClassifiedScene ClassifyScene(SceneBuffer scene, TileGrid<TTile8> grid)
    {
        var accum = new ClassificationAccumulator(4096);
        try
        {
            BBoxClassifier.Classify(scene, grid, ref accum);
            var entries = accum.Finish().ToArray();
            return ClassificationMerge.Merge([entries], grid);
        }
        finally
        {
            accum.Dispose();
        }
    }

    private (Buffer paintBuffer, Buffer gradientBuffer) BuildPaintBufferWithGradients(SceneBuffer scene)
    {
        int count = scene.PaintCount;
        var paints = new PaintData[count];
        var gradientFloats = new List<float>();

        // First pass: collect gradient geometries per paint
        var gradientGeoms = ComputeGradientGeometries(scene);

        for (int i = 0; i < count; i++)
        {
            var paint = scene.GetPaint(i);
            if (paint.Kind == PaintKind.Solid)
            {
                paints[i] = PaintData.FromSolid(paint.Color);
            }
            else if (paint.Kind == PaintKind.LinearGradient || paint.Kind == PaintKind.RadialGradient)
            {
                var stops = scene.GetGradientStops((int)paint.GradientId);
                uint startIdx = (uint)gradientFloats.Count;
                uint stopCount = (uint)stops.Count;
                for (int s = 0; s < stops.Count; s++)
                {
                    var (offset, argb) = stops.GetStop(s);
                    float a = ((argb >> 24) & 0xFF) / 255.0f;
                    float r = ((argb >> 16) & 0xFF) / 255.0f;
                    float green = ((argb >> 8) & 0xFF) / 255.0f;
                    float blue = (argb & 0xFF) / 255.0f;
                    gradientFloats.Add(offset);
                    gradientFloats.Add(r * a);
                    gradientFloats.Add(green * a);
                    gradientFloats.Add(blue * a);
                    gradientFloats.Add(a);
                }

                var geom = gradientGeoms.TryGetValue(i, out var g) ? g : default;
                paints[i] = new PaintData
                {
                    R = 0,
                    G = 0,
                    B = 0,
                    A = 1,
                    Kind = paint.Kind == PaintKind.LinearGradient
                        ? PaintData.KindLinearGradient
                        : PaintData.KindRadialGradient,
                    GradientStart = startIdx,
                    GradientCount = stopCount,
                    P0X = geom.P0X,
                    P0Y = geom.P0Y,
                    P1X = geom.P1X,
                    P1Y = geom.P1Y
                };
            }
            else
            {
                paints[i] = PaintData.FromSolid(0xFF000000u);
            }
        }

        var paintBytes = MemoryMarshal.AsBytes(paints.AsSpan());
        var paintBuffer = _device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            Size = (ulong)paintBytes.Length
        });
        _device.Queue.WriteBuffer(paintBuffer, 0, paintBytes);

        Buffer gradientBuffer = default;
        if (gradientFloats.Count > 0)
        {
            var gradSpan = MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(gradientFloats));
            gradientBuffer = _device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
                Size = (ulong)gradSpan.Length
            });
            _device.Queue.WriteBuffer(gradientBuffer, 0, gradSpan);
        }

        return (paintBuffer, gradientBuffer);
    }

    private static Dictionary<int, GradientGeom> ComputeGradientGeometries(SceneBuffer scene)
    {
        var geoms = new Dictionary<int, GradientGeom>();
        for (int i = 0; i < scene.Commands.Length; i++)
        {
            ref readonly var cmd = ref scene.Commands[i];
            int paintId = -1;
            Affine xf = Affine.Identity;
            Rect? bounds = null;

            if (cmd.Op == SceneOpcode.FillRect)
            {
                paintId = cmd.FillRect.PaintId;
                xf = scene.GetTransform(cmd.FillRect.TransformId);
                var rect = scene.GetRect(cmd.FillRect.RectId);
                bounds = rect.Transform(xf);
            }
            else if (cmd.Op == SceneOpcode.FillPath)
            {
                paintId = cmd.FillPath.PaintId;
                xf = scene.GetTransform(cmd.FillPath.TransformId);
                if (scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
                {
                    bounds = pathData.Path.Aabb().Transform(xf);
                }
            }

            if (paintId >= 0 && bounds.HasValue && !bounds.Value.IsEmpty)
            {
                var b = bounds.Value;
                var paint = scene.GetPaint(paintId);
                if (paint.Kind == PaintKind.LinearGradient || paint.Kind == PaintKind.RadialGradient)
                {
                    if (!geoms.ContainsKey(paintId))
                    {
                        if (paint.Kind == PaintKind.LinearGradient)
                        {
                            geoms[paintId] = new GradientGeom
                            {
                                P0X = (float)b.MinX,
                                P0Y = (float)b.MinY,
                                P1X = (float)b.MaxX,
                                P1Y = (float)b.MinY
                            };
                        }
                        else
                        {
                            float cx = (float)((b.MinX + b.MaxX) * 0.5);
                            float cy = (float)((b.MinY + b.MaxY) * 0.5);
                            float radius = (float)Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY) * 0.5f;
                            geoms[paintId] = new GradientGeom
                            {
                                P0X = cx,
                                P0Y = cy,
                                P1X = radius,
                                P1Y = 0
                            };
                        }
                    }
                }
            }
        }
        return geoms;
    }

    private struct GradientGeom
    {
        public float P0X, P0Y, P1X, P1Y;
    }

    private (Texture texture, TextureView view) GetOrCreateOffscreenTarget(int width, int height)
    {
        if (width == _offscreenWidth && height == _offscreenHeight
            && !_offscreenTexture.IsInvalid && !_offscreenView.IsInvalid)
        {
            return (_offscreenTexture, _offscreenView);
        }

        if (!_offscreenView.IsInvalid) { _offscreenView.Dispose(); _offscreenView = default; }
        if (!_offscreenTexture.IsInvalid) { _offscreenTexture.Dispose(); _offscreenTexture = default; }

        _offscreenTexture = _device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8UnormSrgb,
            Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.CopySrc)
        });
        _offscreenView = _offscreenTexture.CreateView();
        _offscreenWidth = width;
        _offscreenHeight = height;
        return (_offscreenTexture, _offscreenView);
    }

    private Buffer GetOrCreateReadbackBuffer(uint size)
    {
        if (!_readbackBuffer.IsInvalid && _readbackBufferSize >= size)
        {
            return _readbackBuffer;
        }

        if (!_readbackBuffer.IsInvalid) { _readbackBuffer.Dispose(); }

        _readbackBuffer = _device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
            Size = size
        });
        _readbackBufferSize = size;
        return _readbackBuffer;
    }

    private uint _readbackBufferSize;

    private Buffer CreatePersistentBuffer(long size)
    {
        if (size <= 0)
        {
            return default;
        }

        return _device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            Size = (ulong)size
        });
    }

    private static Buffer CreateUnitQuadVertexBuffer(Device device)
    {
        float* vertices = stackalloc float[8];
        vertices[0] = 0.0f; vertices[1] = 0.0f;
        vertices[2] = 1.0f; vertices[3] = 0.0f;
        vertices[4] = 0.0f; vertices[5] = 1.0f;
        vertices[6] = 1.0f; vertices[7] = 1.0f;

        var buffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Vertex | BufferUsage.CopyDst),
            Size = 32
        });

        var span = new ReadOnlySpan<byte>(vertices, 32);
        device.Queue.WriteBuffer(buffer, 0, span);
        return buffer;
    }

    private static void ValidateBuffer(string name, Buffer buffer)
    {
        if (buffer.IsInvalid)
        {
            Etch.Panic.Invariant(Etch.PanicCodes.GpuDeviceCreationFailed, $"Buffer '{name}' is invalid (null handle)");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (!_offscreenView.IsInvalid) { _offscreenView.Dispose(); }
        if (!_offscreenTexture.IsInvalid) { _offscreenTexture.Dispose(); }
        if (!_readbackBuffer.IsInvalid) { _readbackBuffer.Dispose(); }

        if (!_cachedPaintBuffer.IsInvalid) { _cachedPaintBuffer.Dispose(); }
        if (!_cachedGradientBuffer.IsInvalid) { _cachedGradientBuffer.Dispose(); }
        if (!_cachedStripsBuffer.IsInvalid) { _cachedStripsBuffer.Dispose(); }
        if (!_cachedCoverageBuffer.IsInvalid) { _cachedCoverageBuffer.Dispose(); }

        _pipeline.Dispose();
        _stripUploader.Dispose();
        _perFrameUniform.Dispose();
        _unitQuadVertex.Dispose();
    }
}
