using System.Runtime.InteropServices;
using System.Text;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;
using Etch.Text.Atlas;
using GpuBuffer = Etch.Gpu.Buffer;

namespace Etch.Compose;

/// <summary>
/// Executes a <see cref="DrawList"/> on a wgpu device into an <c>Rgba8UnormSrgb</c> render target:
/// uploads the frame's instances and tables, then records its batches into as few render passes as
/// the framebuffer copies (for mono glyphs and backdrop blurs) allow.
/// </summary>
/// <remarks>
/// The target is sRGB-encoded, so the fixed-function blend runs in linear light and every draw is
/// re-quantized to 8-bit sRGB when it is written. the CPU composer reproduces that model.
/// Destination alpha accumulates with <c>One, OneMinusSrcAlpha</c> for every kind, so a frame cleared
/// opaque stays opaque.
/// </remarks>
public sealed unsafe class GpuComposer : IDisposable
{
    /// <summary>Monochrome glyph atlas page size (one page; resets when full).</summary>
    public const int GlyphAtlasSize = 2048;

    /// <summary>Colour glyph atlas page size (one page; resets when full).</summary>
    public const int ColorGlyphAtlasSize = 1024;

    private readonly Device device;
    private uint targetWidth;
    private uint targetHeight;
    private bool disposed;

    private readonly GpuBuffer uniformBuffer;
    private readonly GrowableBuffer shapeBuffer;
    private readonly GrowableBuffer clipBuffer;
    private readonly GrowableBuffer gradientBuffer;
    private readonly GrowableBuffer stopBuffer;
    private readonly GrowableBuffer glyphBuffer;
    private readonly GrowableBuffer colorGlyphBuffer;
    private readonly GrowableBuffer imageBuffer;
    private readonly GrowableBuffer blurBuffer;
    private readonly GrowableBuffer maskTileBuffer;

    private readonly Sampler linearSampler;
    private readonly Sampler nearestSampler;
    private readonly Sampler blurSampler;

    private readonly GlyphAtlas glyphAtlas;
    private readonly TextureView glyphAtlasView;
    private readonly GlyphAtlas colorGlyphAtlas;
    private readonly TextureView colorGlyphAtlasView;
    private readonly MaskAtlas maskAtlas;
    private readonly Texture emptyMask;
    private readonly TextureView emptyMaskView;

    // A copy of the framebuffer taken before each mono-glyph or blur batch: glyphs read the local
    // background behind them for their contrast-adaptive weight; blurs sample it.
    private Texture bgCopyTexture;
    private TextureView bgCopyView;
    private uint bgCopyWidth;
    private uint bgCopyHeight;

    private readonly Pipeline shapePipeline;
    private readonly Pipeline imagePipeline;
    private readonly Pipeline glyphPipeline;
    private readonly Pipeline colorGlyphPipeline;
    private readonly Pipeline blurPipeline;
    private readonly Pipeline fullFramePipeline;
    private readonly GpuBuffer fullFrameVertices;

    // Images
    private readonly Dictionary<int, (Texture Texture, TextureView View, BindGroup Group)> imageTextures = new();

    // Full-frame CPU blit
    private Texture fallbackTexture;
    private TextureView fallbackTextureView;
    private BindGroup fallbackBindGroup;
    private uint fallbackWidth;
    private uint fallbackHeight;

    // Bind groups referencing per-frame buffers; rebuilt when a buffer grows or a view changes.
    private BindGroup shapeGroup;
    private BindGroup imageGroup;
    private BindGroup glyphGroup0;
    private BindGroup glyphGroup1;
    private BindGroup colorGlyphGroup0;
    private BindGroup colorGlyphGroup1;
    private BindGroup blurGroup0;
    private BindGroup blurGroup1;
    private int bindGeneration = -1;
    private int resourceGeneration;
    private int boundMaskTexture = -1;

    private readonly record struct Pipeline(ShaderModule Shader, RenderPipeline Render, PipelineLayout Layout, BindGroupLayout Group0, BindGroupLayout Group1)
    {
        public void Dispose()
        {
            Render.Dispose();
            Layout.Dispose();
            Group0.Dispose();
            if (!Group1.IsInvalid)
            {
                Group1.Dispose();
            }
            Shader.Dispose();
        }
    }

    // A storage (or vertex) buffer that grows by doubling; Generation changes when it is replaced.
    private sealed class GrowableBuffer
    {
        private readonly Device device;
        private readonly ulong usage;

        public GrowableBuffer(Device device, ulong usage)
        {
            this.device = device;
            this.usage = usage;
            Buffer = device.CreateBuffer(new BufferDescriptor { Usage = usage, Size = 256 });
            Capacity = 256;
        }

        public GpuBuffer Buffer { get; private set; }

        public ulong Capacity { get; private set; }

        public bool Ensure(ulong bytes)
        {
            if (bytes <= Capacity)
            {
                return false;
            }
            Buffer.Dispose();
            Capacity = Math.Max(bytes, Capacity * 2);
            Buffer = device.CreateBuffer(new BufferDescriptor { Usage = usage, Size = Capacity });
            return true;
        }

        public void Dispose() => Buffer.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SurfaceSizeData
    {
        public float Width, Height;
        public float OffsetX, OffsetY;
        public float TextGamma;
        public float LightWeight;
        public float Dissolve;
        public float Pad2;
    }

    private static ReadOnlySpan<float> FullFrameQuad =>
    [
        -1.0f, -1.0f, 0.0f, 1.0f,
         1.0f, -1.0f, 1.0f, 1.0f,
        -1.0f,  1.0f, 0.0f, 0.0f,
         1.0f, -1.0f, 1.0f, 1.0f,
         1.0f,  1.0f, 1.0f, 0.0f,
        -1.0f,  1.0f, 0.0f, 0.0f,
    ];

    /// <summary>Creates the composer's pipelines and atlases on <paramref name="device"/> for a target of the given size.</summary>
    public GpuComposer(Device device, uint width, uint height)
    {
        this.device = device;
        targetWidth = width;
        targetHeight = height;

        ulong storage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst);
        uniformBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
            Size = (ulong)sizeof(SurfaceSizeData),
        });
        shapeBuffer = new GrowableBuffer(device, storage);
        clipBuffer = new GrowableBuffer(device, storage);
        gradientBuffer = new GrowableBuffer(device, storage);
        stopBuffer = new GrowableBuffer(device, storage);
        glyphBuffer = new GrowableBuffer(device, storage);
        colorGlyphBuffer = new GrowableBuffer(device, storage);
        imageBuffer = new GrowableBuffer(device, storage);
        blurBuffer = new GrowableBuffer(device, storage);
        maskTileBuffer = new GrowableBuffer(device, storage);

        linearSampler = CreateSampler(FilterMode.Linear);
        nearestSampler = CreateSampler(FilterMode.Nearest);
        blurSampler = CreateSampler(FilterMode.Linear);

        // Atlas sizes are a resident-memory budget: 2048² R8 is 4 MB and holds thousands of UI
        // glyphs; 1024² RGBA for emoji is 4 MB. Both reset when full (WasExhausted).
        glyphAtlas = new GlyphAtlas(device, GlyphAtlasSize, TextureFormat.R8Unorm, 128, maxPages: 1);
        glyphAtlasView = glyphAtlas.GetPage(0).Texture.CreateView();
        colorGlyphAtlas = new GlyphAtlas(device, ColorGlyphAtlasSize, TextureFormat.Rgba8UnormSrgb, 128, maxPages: 1);
        colorGlyphAtlasView = colorGlyphAtlas.GetPage(0).Texture.CreateView();
        maskAtlas = new MaskAtlas(device);

        // Bound in place of the mask page until a mask exists (the page is created lazily).
        emptyMask = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 },
            Format = TextureFormat.R8Unorm,
            Dimension = TextureDimension.D2,
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            MipLevelCount = 1,
            SampleCount = 1,
        });
        emptyMaskView = emptyMask.CreateView(new TextureViewDescriptor
        {
            Format = TextureFormat.R8Unorm,
            Dimension = TextureViewDimension.D2Array,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        });

        EnsureBgCopyTexture(width, height);

        shapePipeline = BuildPipeline("Shape", ComposeShaders.ShapeWgsl, ShapeLayout(), default, StraightOver(), PrimitiveTopology.TriangleStrip, null, 0);
        imagePipeline = BuildPipeline("Image", ComposeShaders.ImageWgsl, ImageLayout0(), TextureSamplerLayout(), StraightOver(), PrimitiveTopology.TriangleStrip, null, 0);
        glyphPipeline = BuildPipeline("Glyph", ComposeShaders.GlyphWgsl, GlyphLayout0(), StorageLayout(), Premultiplied(), PrimitiveTopology.TriangleStrip, null, 0);
        colorGlyphPipeline = BuildPipeline("ColorGlyph", ComposeShaders.ColorGlyphWgsl, GlyphLayout0(), StorageLayout(), Premultiplied(), PrimitiveTopology.TriangleStrip, null, 0);
        blurPipeline = BuildPipeline("Blur", ComposeShaders.BlurWgsl, BlurLayout0(), StorageLayout(), Premultiplied(), PrimitiveTopology.TriangleStrip, null, 0);

        var attributes = stackalloc VertexAttribute[2];
        attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 };
        var vertexLayout = new VertexBufferLayout { StepMode = VertexStepMode.Vertex, ArrayStride = 16, AttributeCount = (UIntPtr)2, Attributes = (nint)attributes };
        fullFramePipeline = BuildPipeline("FullFrame", ComposeShaders.FullFrameWgsl, TextureSamplerLayout(), default, StraightOver(), PrimitiveTopology.TriangleList, &vertexLayout, 1);
        fullFrameVertices = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Vertex | BufferUsage.CopyDst),
            Size = (ulong)(FullFrameQuad.Length * sizeof(float)),
        });
        device.Queue.WriteBuffer(fullFrameVertices, 0, MemoryMarshal.AsBytes(FullFrameQuad));
    }

    /// <summary>The monochrome glyph atlas glyph instances' UVs refer to.</summary>
    public GlyphAtlas MonoAtlas => glyphAtlas;

    /// <summary>The colour glyph atlas colour-glyph instances' UVs refer to.</summary>
    public GlyphAtlas ColorAtlas => colorGlyphAtlas;

    /// <summary>The coverage-mask atlas mask shapes and mask clips refer to.</summary>
    public MaskAtlas Masks => maskAtlas;

    /// <summary>When set, glyph batches are skipped (bisect text vs geometry).</summary>
    public bool SkipGlyphs { get; set; }

    /// <summary>Number of GPU textures held for images.</summary>
    public int ImageTextureCount => imageTextures.Count;

    /// <summary>Number of framebuffer copies the last <see cref="Encode"/> made.</summary>
    public int LastCopyCount { get; private set; }

    /// <summary>Tracks a resized target; recreates the framebuffer-copy texture to match.</summary>
    public void Resize(uint width, uint height)
    {
        targetWidth = width;
        targetHeight = height;
        if (EnsureBgCopyTexture(width, height))
        {
            resourceGeneration++;
        }
    }

    /// <summary>
    /// Clears every atlas that filled up last frame (or all of them, when <paramref name="force"/>).
    /// Must run between frames, before the next frame's draw list is built.
    /// </summary>
    public void ResetAtlasesIfExhausted(bool force)
    {
        if (force || glyphAtlas.WasExhausted)
        {
            glyphAtlas.Reset();
        }
        if (force || colorGlyphAtlas.WasExhausted)
        {
            colorGlyphAtlas.Reset();
        }
        if (force || maskAtlas.WasExhausted)
        {
            maskAtlas.Reset();
        }
    }

    /// <summary>
    /// Uploads <paramref name="list"/> (which must be finished) and records its batches into
    /// <paramref name="encoder"/>, clearing <paramref name="target"/> to opaque black first.
    /// </summary>
    public void Encode(CommandEncoder encoder, Texture target, TextureView targetView, DrawList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        foreach (var (handle, image) in list.Images)
        {
            EnsureImageTexture(handle, image);
        }
        Upload(list);
        LastCopyCount = EncodeBatches(encoder, target, targetView, list);
    }

    /// <summary>
    /// Records a full-target blit of a CPU-rendered frame: <paramref name="pixels"/> is a
    /// <see cref="Cpu.CpuFramebuffer"/>'s BGRA8 (0xAARRGGBB, sRGB-encoded) pixels,
    /// <paramref name="width"/> × <paramref name="height"/>. Only <paramref name="dirty"/> is
    /// uploaded into the persistent frame texture (everything when the texture is new or resized);
    /// the blit draws all of it.
    /// </summary>
    public void EncodeFramebufferUpload(CommandEncoder encoder, TextureView targetView, ReadOnlySpan<uint> pixels, uint width, uint height,
        ReadOnlySpan<Cpu.CpuDirtyRect> dirty)
    {
        bool fresh = EnsureFallbackTexture(width, height);
        var bytes = MemoryMarshal.AsBytes(pixels);
        if (fresh)
        {
            UploadFrameRect(bytes, width, new Cpu.CpuDirtyRect(0, 0, (int)width, (int)height));
        }
        else
        {
            foreach (var rect in dirty)
            {
                UploadFrameRect(bytes, width, rect);
            }
        }

        using var pass = BeginPass(encoder, targetView, clear: true);
        pass.SetPipeline(fullFramePipeline.Render);
        pass.SetVertexBuffer(0, fullFrameVertices, 0, (ulong)(FullFrameQuad.Length * sizeof(float)));
        pass.SetBindGroup(0, fallbackBindGroup);
        pass.Draw(6);
        pass.End();
    }

    private void UploadFrameRect(ReadOnlySpan<byte> bytes, uint width, Cpu.CpuDirtyRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }
        var origin = new WGPUOrigin3D { X = (uint)rect.X, Y = (uint)rect.Y, Z = 0 };
        var size = new Extent3D { Width = (uint)rect.Width, Height = (uint)rect.Height, DepthOrArrayLayers = 1 };
        int start = (rect.Y * (int)width + rect.X) * 4;
        int length = ((rect.Height - 1) * (int)width + rect.Width) * 4;
        device.Queue.WriteTexture(fallbackTexture, 0, origin, bytes.Slice(start, length), width * 4, (uint)rect.Height, size);
    }

    /// <summary>Drops the GPU textures of images not in <paramref name="liveHandles"/>.</summary>
    public void ReleaseImagesExcept(IReadOnlySet<int> liveHandles)
    {
        ArgumentNullException.ThrowIfNull(liveHandles);
        List<int>? dead = null;
        foreach (int handle in imageTextures.Keys)
        {
            if (!liveHandles.Contains(handle))
            {
                (dead ??= new List<int>()).Add(handle);
            }
        }
        if (dead is null)
        {
            return;
        }
        foreach (int handle in dead)
        {
            var entry = imageTextures[handle];
            entry.Group.Dispose();
            entry.View.Dispose();
            entry.Texture.Dispose();
            imageTextures.Remove(handle);
        }
    }

    // ── Upload ──────────────────────────────────────────────────────────

    private void Upload(DrawList list)
    {
        bool grew = false;
        grew |= Write(shapeBuffer, CollectionsMarshal.AsSpan(list.OrderedShapes));
        grew |= Write(clipBuffer, list.Clips);
        grew |= Write(gradientBuffer, list.Gradients);
        grew |= Write(stopBuffer, list.GradientStops);
        grew |= Write(glyphBuffer, CollectionsMarshal.AsSpan(list.OrderedGlyphs));
        grew |= Write(colorGlyphBuffer, CollectionsMarshal.AsSpan(list.OrderedColorGlyphs));
        grew |= Write(imageBuffer, CollectionsMarshal.AsSpan(list.OrderedImages));
        grew |= Write(blurBuffer, CollectionsMarshal.AsSpan(list.OrderedBlurs));
        grew |= Write(maskTileBuffer, list.MaskTiles);
        if (grew)
        {
            resourceGeneration++;
        }
        if (boundMaskTexture != maskAtlas.TextureGeneration)
        {
            boundMaskTexture = maskAtlas.TextureGeneration;
            resourceGeneration++;
        }
        if (bindGeneration != resourceGeneration)
        {
            RebuildBindGroups();
            bindGeneration = resourceGeneration;
        }

        var data = new SurfaceSizeData
        {
            Width = targetWidth,
            Height = targetHeight,
            TextGamma = list.Parameters.TextGamma,
            LightWeight = list.Parameters.LightWeight,
            Dissolve = list.Parameters.Dissolve,
        };
        device.Queue.WriteBuffer(uniformBuffer, 0, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref data, 1)));
    }

    private bool Write<T>(GrowableBuffer buffer, ReadOnlySpan<T> items)
        where T : unmanaged
    {
        ulong bytes = (ulong)(items.Length * sizeof(T));
        bool grew = buffer.Ensure(Math.Max(bytes, 16));
        if (bytes > 0)
        {
            device.Queue.WriteBuffer(buffer.Buffer, 0, MemoryMarshal.AsBytes(items));
        }
        return grew;
    }

    // ── Encoding ────────────────────────────────────────────────────────

    /// <summary>
    /// Records the batches into as few render passes as possible: one pass, broken only where a
    /// mono-glyph or backdrop-blur batch needs a fresh copy of the framebuffer under it. Returns the
    /// number of framebuffer copies made.
    /// </summary>
    private int EncodeBatches(CommandEncoder encoder, Texture target, TextureView targetView, DrawList list)
    {
        int copies = 0;
        var images = list.OrderedImages;
        var pass = BeginPass(encoder, targetView, clear: true);
        try
        {
            DrawKind? bound = null;
            foreach (ref readonly var batch in list.Batches)
            {
                if (batch.Count == 0)
                {
                    continue;
                }
                if (SkipGlyphs && (batch.Kind == DrawKind.Glyph || batch.Kind == DrawKind.ColorGlyph))
                {
                    continue;
                }

                if (batch.Kind == DrawKind.Glyph || batch.Kind == DrawKind.Blur)
                {
                    if (!CanCopyBackground())
                    {
                        if (batch.Kind == DrawKind.Blur)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        pass.End();
                        pass.Dispose();
                        CopyFramebufferRegion(encoder, target, batch.MinX, batch.MinY, batch.MaxX, batch.MaxY);
                        copies++;
                        pass = BeginPass(encoder, targetView, clear: false);
                        bound = null;
                    }
                }

                switch (batch.Kind)
                {
                    case DrawKind.Shape:
                        if (bound != DrawKind.Shape)
                        {
                            pass.SetPipeline(shapePipeline.Render);
                            pass.SetBindGroup(0, shapeGroup);
                        }
                        pass.Draw(4, (uint)batch.Count, 0, (uint)batch.Start);
                        break;

                    case DrawKind.Glyph:
                        if (bound != DrawKind.Glyph)
                        {
                            pass.SetPipeline(glyphPipeline.Render);
                            pass.SetBindGroup(0, glyphGroup0);
                            pass.SetBindGroup(1, glyphGroup1);
                        }
                        pass.Draw(4, (uint)batch.Count, 0, (uint)batch.Start);
                        break;

                    case DrawKind.ColorGlyph:
                        if (bound != DrawKind.ColorGlyph)
                        {
                            pass.SetPipeline(colorGlyphPipeline.Render);
                            pass.SetBindGroup(0, colorGlyphGroup0);
                            pass.SetBindGroup(1, colorGlyphGroup1);
                        }
                        pass.Draw(4, (uint)batch.Count, 0, (uint)batch.Start);
                        break;

                    case DrawKind.Image:
                        if (bound != DrawKind.Image)
                        {
                            pass.SetPipeline(imagePipeline.Render);
                            pass.SetBindGroup(0, imageGroup);
                        }
                        for (int i = batch.Start; i < batch.Start + batch.Count; i++)
                        {
                            pass.SetBindGroup(1, imageTextures[images[i].Handle].Group);
                            pass.Draw(4, 1, 0, (uint)i);
                        }
                        break;

                    case DrawKind.Blur:
                        if (bound != DrawKind.Blur)
                        {
                            pass.SetPipeline(blurPipeline.Render);
                            pass.SetBindGroup(0, blurGroup0);
                            pass.SetBindGroup(1, blurGroup1);
                        }
                        pass.Draw(4, (uint)batch.Count, 0, (uint)batch.Start);
                        break;
                }
                bound = batch.Kind;
            }
        }
        finally
        {
            pass.End();
            pass.Dispose();
        }
        return copies;
    }

    private static RenderPass BeginPass(CommandEncoder encoder, TextureView targetView, bool clear)
    {
        var colorAttachment = new RenderPassColorAttachment
        {
            View = (nint)targetView.Handle,
            DepthSlice = 0xFFFFFFFFu,
            LoadOp = clear ? LoadOp.Clear : LoadOp.Load,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 1 },
        };
        var passDesc = new RenderPassDescriptor
        {
            ColorAttachmentCount = (UIntPtr)1,
            ColorAttachments = (nint)(&colorAttachment),
        };
        return encoder.BeginRenderPass(passDesc);
    }

    private bool CanCopyBackground()
        => !bgCopyTexture.IsInvalid && bgCopyWidth == targetWidth && bgCopyHeight == targetHeight;

    // Copies the device-pixel region (rounded out, clamped to the target) of the framebuffer into
    // the background-copy texture at the same position.
    private void CopyFramebufferRegion(CommandEncoder encoder, Texture target, float minX, float minY, float maxX, float maxY)
    {
        uint x0 = (uint)Math.Clamp(MathF.Floor(minX), 0f, targetWidth);
        uint y0 = (uint)Math.Clamp(MathF.Floor(minY), 0f, targetHeight);
        uint x1 = (uint)Math.Clamp(MathF.Ceiling(maxX), 0f, targetWidth);
        uint y1 = (uint)Math.Clamp(MathF.Ceiling(maxY), 0f, targetHeight);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        var origin = new WGPUOrigin3D { X = x0, Y = y0, Z = 0 };
        var extent = new Extent3D { Width = x1 - x0, Height = y1 - y0, DepthOrArrayLayers = 1 };
        var copySrc = new WGPUTexelCopyTextureInfo { Aspect = (uint)TextureAspect.All, MipLevel = 0, Origin = origin, Texture = target.Handle };
        var copyDst = new WGPUTexelCopyTextureInfo { Aspect = (uint)TextureAspect.All, MipLevel = 0, Origin = origin, Texture = bgCopyTexture.Handle };
        WebGPU.CommandEncoderCopyTextureToTexture(encoder.Handle, (nint)(&copySrc), (nint)(&copyDst), (nint)(&extent));
    }

    // ── Resources ───────────────────────────────────────────────────────

    private Sampler CreateSampler(FilterMode filter) => device.CreateSampler(new SamplerDescriptor
    {
        MagFilter = filter,
        MinFilter = filter,
        MipmapFilter = MipmapFilterMode.Nearest,
        AddressModeU = AddressMode.ClampToEdge,
        AddressModeV = AddressMode.ClampToEdge,
        AddressModeW = AddressMode.ClampToEdge,
        LodMinClamp = 0,
        LodMaxClamp = 32,
        MaxAnisotropy = 1,
    });

    private bool EnsureBgCopyTexture(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            return false;
        }
        if (!bgCopyTexture.IsInvalid && bgCopyWidth == width && bgCopyHeight == height)
        {
            return false;
        }
        if (!bgCopyView.IsInvalid)
        {
            bgCopyView.Dispose();
        }
        if (!bgCopyTexture.IsInvalid)
        {
            bgCopyTexture.Dispose();
        }
        bgCopyTexture = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8UnormSrgb,
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            Dimension = TextureDimension.D2,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        bgCopyView = bgCopyTexture.CreateView();
        bgCopyWidth = width;
        bgCopyHeight = height;
        return true;
    }

    // True when the texture was (re)created and holds nothing yet.
    private bool EnsureFallbackTexture(uint width, uint height)
    {
        if (!fallbackTexture.IsInvalid && fallbackWidth == width && fallbackHeight == height)
        {
            return false;
        }
        if (!fallbackBindGroup.IsInvalid)
        {
            fallbackBindGroup.Dispose();
        }
        if (!fallbackTextureView.IsInvalid)
        {
            fallbackTextureView.Dispose();
        }
        if (!fallbackTexture.IsInvalid)
        {
            fallbackTexture.Dispose();
        }

        // The CPU frame is sRGB-encoded BGRA: an sRGB view decodes it on sample, so the sRGB target
        // re-encodes it to the same bytes.
        fallbackTexture = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Bgra8UnormSrgb,
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            Dimension = TextureDimension.D2,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        fallbackTextureView = fallbackTexture.CreateView();
        fallbackWidth = width;
        fallbackHeight = height;
        fallbackBindGroup = CreateTextureSamplerGroup(fullFramePipeline.Group0, fallbackTextureView, nearestSampler);
        return true;
    }

    /// <summary>Releases the CPU-frame texture (call when CPU frames stop, or the window hides).</summary>
    public void ReleaseFramebufferTexture()
    {
        DisposeGroup(ref fallbackBindGroup);
        if (!fallbackTextureView.IsInvalid)
        {
            fallbackTextureView.Dispose();
        }
        if (!fallbackTexture.IsInvalid)
        {
            fallbackTexture.Dispose();
        }
        fallbackTextureView = default;
        fallbackTexture = default;
        fallbackWidth = fallbackHeight = 0;
    }

    private void EnsureImageTexture(int handle, ComposeImage image)
    {
        if (imageTextures.ContainsKey(handle))
        {
            return;
        }
        var texture = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)image.Width, Height = (uint)image.Height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8UnormSrgb,
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            Dimension = TextureDimension.D2,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        var view = texture.CreateView();
        var origin = new WGPUOrigin3D { X = 0, Y = 0, Z = 0 };
        var writeSize = new Extent3D { Width = (uint)image.Width, Height = (uint)image.Height, DepthOrArrayLayers = 1 };
        device.Queue.WriteTexture(texture, 0, origin, image.Pixels, (uint)image.Width * 4, (uint)image.Height, writeSize);
        imageTextures[handle] = (texture, view, CreateTextureSamplerGroup(imagePipeline.Group1, view, linearSampler));
    }

    private BindGroup CreateTextureSamplerGroup(BindGroupLayout layout, TextureView view, Sampler sampler)
    {
        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = view.Handle };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = sampler.Handle };
        return device.CreateBindGroup(new BindGroupDescriptor { Layout = layout.Handle, EntryCount = (UIntPtr)2, Entries = (nint)entries });
    }

    private TextureView MaskView => maskAtlas.HasPage ? maskAtlas.PageView : emptyMaskView;

    private void RebuildBindGroups()
    {
        DisposeFrameBindGroups();

        var shape = stackalloc BindGroupEntry[7];
        shape[0] = Uniform(0);
        shape[1] = Storage(1, shapeBuffer);
        shape[2] = Storage(2, clipBuffer);
        shape[3] = Storage(3, gradientBuffer);
        shape[4] = Storage(4, stopBuffer);
        shape[5] = new BindGroupEntry { Binding = 5, TextureView = MaskView.Handle };
        shape[6] = Storage(6, maskTileBuffer);
        shapeGroup = CreateGroup(shapePipeline.Group0, shape, 7);

        var image = stackalloc BindGroupEntry[5];
        image[0] = Uniform(0);
        image[1] = Storage(1, imageBuffer);
        image[2] = Storage(2, clipBuffer);
        image[3] = new BindGroupEntry { Binding = 3, TextureView = MaskView.Handle };
        image[4] = Storage(4, maskTileBuffer);
        imageGroup = CreateGroup(imagePipeline.Group0, image, 5);

        glyphGroup0 = CreateGlyphGroup(glyphPipeline.Group0, glyphAtlasView, nearestSampler);
        glyphGroup1 = CreateInstanceGroup(glyphPipeline.Group1, glyphBuffer);
        // Glyph quads map 1:1 onto their atlas texels: nearest sampling reads exactly that texel.
        colorGlyphGroup0 = CreateGlyphGroup(colorGlyphPipeline.Group0, colorGlyphAtlasView, nearestSampler);
        colorGlyphGroup1 = CreateInstanceGroup(colorGlyphPipeline.Group1, colorGlyphBuffer);

        var blur = stackalloc BindGroupEntry[6];
        blur[0] = Uniform(0);
        blur[1] = new BindGroupEntry { Binding = 1, TextureView = bgCopyView.Handle };
        blur[2] = new BindGroupEntry { Binding = 2, Sampler = blurSampler.Handle };
        blur[3] = Storage(3, clipBuffer);
        blur[4] = new BindGroupEntry { Binding = 4, TextureView = MaskView.Handle };
        blur[5] = Storage(5, maskTileBuffer);
        blurGroup0 = CreateGroup(blurPipeline.Group0, blur, 6);
        blurGroup1 = CreateInstanceGroup(blurPipeline.Group1, blurBuffer);
    }

    private BindGroup CreateGlyphGroup(BindGroupLayout layout, TextureView atlasView, Sampler sampler)
    {
        var entries = stackalloc BindGroupEntry[7];
        entries[0] = Uniform(0);
        entries[1] = new BindGroupEntry { Binding = 1, TextureView = atlasView.Handle };
        entries[2] = new BindGroupEntry { Binding = 2, Sampler = sampler.Handle };
        entries[3] = new BindGroupEntry { Binding = 3, TextureView = bgCopyView.Handle };
        entries[4] = Storage(4, clipBuffer);
        entries[5] = new BindGroupEntry { Binding = 5, TextureView = MaskView.Handle };
        entries[6] = Storage(6, maskTileBuffer);
        return CreateGroup(layout, entries, 7);
    }

    private BindGroup CreateInstanceGroup(BindGroupLayout layout, GrowableBuffer buffer)
    {
        var entries = stackalloc BindGroupEntry[1];
        entries[0] = Storage(0, buffer);
        return CreateGroup(layout, entries, 1);
    }

    private BindGroup CreateGroup(BindGroupLayout layout, BindGroupEntry* entries, int count)
        => device.CreateBindGroup(new BindGroupDescriptor { Layout = layout.Handle, EntryCount = (UIntPtr)count, Entries = (nint)entries });

    private BindGroupEntry Uniform(uint binding)
        => new() { Binding = binding, Buffer = uniformBuffer.Handle, Offset = 0, Size = (ulong)sizeof(SurfaceSizeData) };

    private static BindGroupEntry Storage(uint binding, GrowableBuffer buffer)
        => new() { Binding = binding, Buffer = buffer.Buffer.Handle, Offset = 0, Size = buffer.Capacity };

    private void DisposeFrameBindGroups()
    {
        DisposeGroup(ref shapeGroup);
        DisposeGroup(ref imageGroup);
        DisposeGroup(ref glyphGroup0);
        DisposeGroup(ref glyphGroup1);
        DisposeGroup(ref colorGlyphGroup0);
        DisposeGroup(ref colorGlyphGroup1);
        DisposeGroup(ref blurGroup0);
        DisposeGroup(ref blurGroup1);
    }

    private static void DisposeGroup(ref BindGroup group)
    {
        if (!group.IsInvalid)
        {
            group.Dispose();
        }
        group = default;
    }

    // ── Pipelines ───────────────────────────────────────────────────────


    private BindGroupLayout CreateLayout(ReadOnlySpan<BindGroupLayoutEntry> entries)
    {
        fixed (BindGroupLayoutEntry* ptr = entries)
        {
            return device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = (UIntPtr)entries.Length, Entries = (nint)ptr });
        }
    }

    private static BindGroupLayoutEntry UniformEntry(uint binding) => new()
    {
        Binding = binding,
        Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
        Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)sizeof(SurfaceSizeData) },
    };

    private static BindGroupLayoutEntry StorageEntry(uint binding) => new()
    {
        Binding = binding,
        Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
        Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = 0 },
    };

    private static BindGroupLayoutEntry TextureEntry(uint binding, TextureSampleType sampleType) => new()
    {
        Binding = binding,
        Visibility = (ulong)ShaderStage.Fragment,
        Texture = new TextureBindingLayout { SampleType = sampleType, ViewDimension = TextureViewDimension.D2, Multisampled = 0 },
    };

    private static BindGroupLayoutEntry MaskArrayEntry(uint binding) => new()
    {
        Binding = binding,
        Visibility = (ulong)ShaderStage.Fragment,
        Texture = new TextureBindingLayout { SampleType = TextureSampleType.UnfilterableFloat, ViewDimension = TextureViewDimension.D2Array, Multisampled = 0 },
    };

    private static BindGroupLayoutEntry SamplerEntry(uint binding) => new()
    {
        Binding = binding,
        Visibility = (ulong)ShaderStage.Fragment,
        Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
    };

    private BindGroupLayout ShapeLayout()
    {
        Span<BindGroupLayoutEntry> entries =
        [
            UniformEntry(0), StorageEntry(1), StorageEntry(2), StorageEntry(3), StorageEntry(4),
            MaskArrayEntry(5), StorageEntry(6),
        ];
        return CreateLayout(entries);
    }

    private BindGroupLayout ImageLayout0()
    {
        Span<BindGroupLayoutEntry> entries =
        [
            UniformEntry(0), StorageEntry(1), StorageEntry(2), MaskArrayEntry(3), StorageEntry(4),
        ];
        return CreateLayout(entries);
    }

    private BindGroupLayout GlyphLayout0()
    {
        Span<BindGroupLayoutEntry> entries =
        [
            UniformEntry(0), TextureEntry(1, TextureSampleType.Float), SamplerEntry(2),
            TextureEntry(3, TextureSampleType.UnfilterableFloat), StorageEntry(4), MaskArrayEntry(5), StorageEntry(6),
        ];
        return CreateLayout(entries);
    }

    private BindGroupLayout BlurLayout0()
    {
        Span<BindGroupLayoutEntry> entries =
        [
            UniformEntry(0), TextureEntry(1, TextureSampleType.Float), SamplerEntry(2), StorageEntry(3),
            MaskArrayEntry(4), StorageEntry(5),
        ];
        return CreateLayout(entries);
    }

    private BindGroupLayout StorageLayout()
    {
        Span<BindGroupLayoutEntry> entries = [StorageEntry(0)];
        return CreateLayout(entries);
    }

    private BindGroupLayout TextureSamplerLayout()
    {
        Span<BindGroupLayoutEntry> entries = [TextureEntry(0, TextureSampleType.Float), SamplerEntry(1)];
        return CreateLayout(entries);
    }

    // Straight-alpha colour over the target, destination alpha accumulated as coverage.
    private static BlendState StraightOver() => new()
    {
        Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
        Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
    };


    private static BlendState Premultiplied() => new()
    {
        Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
        Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
    };

    private Pipeline BuildPipeline(string label, string wgsl, BindGroupLayout group0, BindGroupLayout group1, BlendState blend,
        PrimitiveTopology topology, VertexBufferLayout* vertexBuffers, int vertexBufferCount)
    {
        var shader = device.CreateShaderModuleWgsl(wgsl, label);
        var layouts = stackalloc nint[2];
        layouts[0] = group0.Handle;
        int layoutCount = 1;
        if (!group1.IsInvalid)
        {
            layouts[1] = group1.Handle;
            layoutCount = 2;
        }
        var pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = (UIntPtr)layoutCount,
            BindGroupLayouts = (nint)layouts,
        });

        Span<byte> vsName = stackalloc byte[3];
        Span<byte> fsName = stackalloc byte[3];
        Encoding.UTF8.GetBytes("vs", vsName);
        Encoding.UTF8.GetBytes("fs", fsName);
        RenderPipeline render;
        fixed (byte* vsPtr = vsName)
        fixed (byte* fsPtr = fsName)
        {
            var colorTarget = new ColorTargetState
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Blend = (nint)(&blend),
                WriteMask = (ulong)ColorWriteMask.All,
            };
            var vertex = new VertexState
            {
                Module = shader.Handle,
                EntryPoint = new StringView { Data = (nint)vsPtr, Length = 2 },
                BufferCount = (UIntPtr)vertexBufferCount,
                Buffers = (nint)vertexBuffers,
            };
            var fragment = new FragmentState
            {
                Module = shader.Handle,
                EntryPoint = new StringView { Data = (nint)fsPtr, Length = 2 },
                TargetCount = (UIntPtr)1,
                Targets = (nint)(&colorTarget),
            };
            render = device.CreateRenderPipeline(new RenderPipelineDescriptor
            {
                Layout = pipelineLayout.Handle,
                Vertex = vertex,
                Fragment = (nint)(&fragment),
                Primitive = new PrimitiveState { Topology = topology, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = ~0u },
            });
        }
        return new Pipeline(shader, render, pipelineLayout, group0, group1);
    }

    /// <summary>Releases every GPU resource the composer created (not the device).</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        DisposeFrameBindGroups();
        foreach (var entry in imageTextures.Values)
        {
            entry.Group.Dispose();
            entry.View.Dispose();
            entry.Texture.Dispose();
        }
        imageTextures.Clear();
        DisposeGroup(ref fallbackBindGroup);
        if (!fallbackTextureView.IsInvalid)
        {
            fallbackTextureView.Dispose();
        }
        if (!fallbackTexture.IsInvalid)
        {
            fallbackTexture.Dispose();
        }

        shapePipeline.Dispose();
        imagePipeline.Dispose();
        glyphPipeline.Dispose();
        colorGlyphPipeline.Dispose();
        blurPipeline.Dispose();
        fullFramePipeline.Dispose();
        fullFrameVertices.Dispose();

        uniformBuffer.Dispose();
        shapeBuffer.Dispose();
        clipBuffer.Dispose();
        gradientBuffer.Dispose();
        stopBuffer.Dispose();
        glyphBuffer.Dispose();
        colorGlyphBuffer.Dispose();
        imageBuffer.Dispose();
        blurBuffer.Dispose();
        maskTileBuffer.Dispose();

        linearSampler.Dispose();
        nearestSampler.Dispose();
        blurSampler.Dispose();

        glyphAtlasView.Dispose();
        glyphAtlas.Dispose();
        colorGlyphAtlasView.Dispose();
        colorGlyphAtlas.Dispose();
        maskAtlas.Dispose();
        emptyMaskView.Dispose();
        emptyMask.Dispose();

        if (!bgCopyView.IsInvalid)
        {
            bgCopyView.Dispose();
        }
        if (!bgCopyTexture.IsInvalid)
        {
            bgCopyTexture.Dispose();
        }
    }
}
