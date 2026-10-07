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
/// uploads the frame's instances, then records its batches into as few render passes as the
/// framebuffer copies (for mono glyphs and backdrop blurs) allow.
/// </summary>
/// <remarks>
/// The target is sRGB-encoded, so the fixed-function blend runs in linear light and every draw is
/// re-quantized to 8-bit sRGB when it is written. The CPU composer reproduces that model exactly.
/// </remarks>
public sealed unsafe class GpuComposer : IDisposable
{
    /// <summary>Monochrome glyph atlas page size (one page; resets when full).</summary>
    public const int GlyphAtlasSize = 2048;

    /// <summary>Colour glyph atlas page size (one page; resets when full).</summary>
    public const int ColorGlyphAtlasSize = 1024;

    private const int ImageQuadFloats = 24; // 6 vertices × (x, y, u, v)
    private const int InitialImageQuadCapacity = 4096;

    private readonly Device device;
    private uint targetWidth;
    private uint targetHeight;
    private bool disposed;

    // Shape pipeline
    private readonly ShaderModule geomShader;
    private readonly RenderPipeline geomPipeline;
    private readonly PipelineLayout geomPipelineLayout;
    private readonly BindGroupLayout geomBindGroupLayout;
    private readonly GpuBuffer geomUniformBuffer;
    private GpuBuffer geomStorageBuffer;
    private int geomStorageCapacity;
    private BindGroup geomBindGroup;

    // Textured-quad pipeline — images and the full-frame CPU blit
    private readonly ShaderModule textShader;
    private readonly RenderPipeline textPipeline;
    private readonly PipelineLayout textPipelineLayout;
    private readonly BindGroupLayout textBindGroupLayout;
    private readonly Sampler textSampler;
    private readonly GpuBuffer textVertexBuffer;

    // Full-frame CPU blit
    private Texture fallbackTexture;
    private TextureView fallbackTextureView;
    private BindGroup fallbackBindGroup;
    private uint fallbackWidth;
    private uint fallbackHeight;

    // Images
    private readonly Dictionary<int, Texture> imageTextures = new();
    private readonly Dictionary<int, TextureView> imageTextureViews = new();
    private readonly Dictionary<int, BindGroup> imageBindGroups = new();
    private GpuBuffer imageVertexBuffer;
    private int imageQuadCapacity = InitialImageQuadCapacity;
    private float[] imageVertexScratch = new float[64 * ImageQuadFloats];

    // Mono glyphs
    private readonly GlyphAtlas glyphAtlas;
    private readonly TextureView glyphAtlasView;
    private readonly ShaderModule glyphShader;
    private readonly RenderPipeline glyphPipeline;
    private readonly PipelineLayout glyphPipelineLayout;
    private readonly BindGroupLayout glyphAtlasLayout;
    private readonly BindGroupLayout glyphInstanceLayout;
    private readonly GpuBuffer glyphUniformBuffer;
    private GpuBuffer glyphInstanceBuffer;
    private int glyphInstanceCapacity;
    private readonly Sampler glyphSampler;
    private BindGroup glyphAtlasBindGroup;
    private BindGroup glyphInstanceBindGroup;

    // A copy of the framebuffer taken before each mono-glyph or blur batch: glyphs read the local
    // background behind them for their contrast-adaptive weight; blurs sample it.
    private Texture bgCopyTexture;
    private TextureView bgCopyView;
    private uint bgCopyWidth;
    private uint bgCopyHeight;

    // Backdrop blur
    private readonly ShaderModule blurShader;
    private readonly RenderPipeline blurPipeline;
    private readonly PipelineLayout blurPipelineLayout;
    private readonly BindGroupLayout blurBind0Layout;
    private readonly BindGroupLayout blurInstanceLayout;
    private readonly Sampler blurSampler;
    private GpuBuffer blurInstanceBuffer;
    private int blurInstanceCapacity;

    // Colour glyphs
    private readonly GlyphAtlas colorGlyphAtlas;
    private readonly TextureView colorGlyphAtlasView;
    private readonly ShaderModule colorGlyphShader;
    private readonly RenderPipeline colorGlyphPipeline;
    private readonly PipelineLayout colorGlyphPipelineLayout;
    private readonly Sampler colorGlyphSampler;
    private BindGroup colorGlyphAtlasBindGroup;
    private BindGroup colorGlyphInstanceBindGroup;
    private GpuBuffer colorGlyphInstanceBuffer;
    private int colorGlyphInstanceCapacity;

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

    // Fullscreen quad: pos (x,y), uv (u,v)
    private static ReadOnlySpan<float> TextQuadVertices =>
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

        geomShader = device.CreateShaderModuleWgsl(ComposeShaders.GeometryWgsl, "Geometry");
        geomUniformBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
            Size = (ulong)sizeof(SurfaceSizeData),
        });
        geomStorageBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            Size = 16,
        });
        geomStorageCapacity = 0;
        geomPipeline = BuildGeometryPipeline(out geomPipelineLayout, out geomBindGroupLayout);
        UpdateGeometryBindGroup();

        textShader = device.CreateShaderModuleWgsl(ComposeShaders.TextWgsl, "Text");
        textSampler = device.CreateSampler(new SamplerDescriptor
        {
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        });
        textVertexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Vertex | BufferUsage.CopyDst),
            Size = (ulong)(TextQuadVertices.Length * sizeof(float)),
        });
        device.Queue.WriteBuffer(textVertexBuffer, 0, MemoryMarshal.AsBytes(TextQuadVertices));
        textPipeline = BuildTextPipeline(out textPipelineLayout, out textBindGroupLayout);

        // Atlas sizes are a resident-memory budget: 2048² R8 is 4 MB and holds thousands of UI
        // glyphs; when it fills it is reset and refilled (WasExhausted).
        glyphAtlas = new GlyphAtlas(device, GlyphAtlasSize, TextureFormat.R8Unorm, 128, maxPages: 1);
        glyphAtlasView = glyphAtlas.GetPage(0).Texture.CreateView();
        glyphShader = device.CreateShaderModuleWgsl(ComposeShaders.GlyphAtlasWgsl, "GlyphAtlas");
        glyphSampler = device.CreateSampler(new SamplerDescriptor
        {
            MagFilter = FilterMode.Nearest,
            MinFilter = FilterMode.Nearest,
            MipmapFilter = MipmapFilterMode.Nearest,
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        });
        glyphUniformBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
            Size = (ulong)sizeof(SurfaceSizeData),
        });
        glyphInstanceBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            Size = 16,
        });
        glyphInstanceCapacity = 0;
        glyphPipeline = BuildGlyphPipeline(out glyphPipelineLayout, out glyphAtlasLayout, out glyphInstanceLayout);

        EnsureBgCopyTexture(width, height);
        glyphAtlasBindGroup = default;
        glyphInstanceBindGroup = default;
        UpdateGlyphAtlasBindGroup();

        // Colour glyph atlas for emoji: 1024² RGBA is 4 MB. Resets when full like the text atlas.
        colorGlyphAtlas = new GlyphAtlas(device, ColorGlyphAtlasSize, TextureFormat.Rgba8UnormSrgb, 128, maxPages: 1);
        colorGlyphAtlasView = colorGlyphAtlas.GetPage(0).Texture.CreateView();
        colorGlyphShader = device.CreateShaderModuleWgsl(ComposeShaders.ColorGlyphAtlasWgsl, "ColorGlyphAtlas");
        colorGlyphSampler = device.CreateSampler(new SamplerDescriptor
        {
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        });
        colorGlyphAtlasBindGroup = default;
        colorGlyphInstanceBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            Size = 16,
        });
        colorGlyphInstanceCapacity = 0;
        colorGlyphPipeline = BuildColorGlyphPipeline(out colorGlyphPipelineLayout);
        UpdateColorGlyphAtlasBindGroup();

        blurShader = device.CreateShaderModuleWgsl(ComposeShaders.BackdropBlurWgsl, "BackdropBlur");
        blurSampler = device.CreateSampler(new SamplerDescriptor
        {
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            LodMinClamp = 0,
            LodMaxClamp = 0,
            MaxAnisotropy = 1,
        });
        blurInstanceBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            Size = 16,
        });
        blurInstanceCapacity = 0;
        blurPipeline = BuildBlurPipeline(out blurPipelineLayout, out blurBind0Layout, out blurInstanceLayout);

        // One quad (6 verts × 4 floats) per image of the frame, each at its own offset: queued
        // buffer writes all land before the encoder's passes run, so a shared slot would collapse
        // every image onto the last one.
        imageVertexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Vertex | BufferUsage.CopyDst),
            Size = (ulong)(imageQuadCapacity * ImageQuadFloats * sizeof(float)),
        });
    }

    /// <summary>The monochrome glyph atlas glyph instances' UVs refer to.</summary>
    public GlyphAtlas MonoAtlas => glyphAtlas;

    /// <summary>The colour glyph atlas colour-glyph instances' UVs refer to.</summary>
    public GlyphAtlas ColorAtlas => colorGlyphAtlas;

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
            UpdateGlyphAtlasBindGroup();
            UpdateColorGlyphAtlasBindGroup();
        }
    }

    /// <summary>
    /// Clears an atlas that filled up last frame (or both, when <paramref name="force"/>). Must run
    /// between frames, before any glyph of the next frame is looked up, so no instance holds a stale UV.
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
    }

    /// <summary>
    /// Uploads <paramref name="list"/> (which must be <see cref="DrawList.Finish">finished</see>) and
    /// records its batches into <paramref name="encoder"/>, clearing <paramref name="target"/> to
    /// opaque black first.
    /// </summary>
    public void Encode(CommandEncoder encoder, Texture target, TextureView targetView, DrawList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        foreach (var (handle, image) in list.Images)
        {
            EnsureImageTexture(handle, image);
        }
        UploadSchedule(list);
        UploadSurfaceSize(list.Parameters);
        LastCopyCount = EncodeSchedule(encoder, target, targetView, list);
    }

    /// <summary>
    /// Records a full-target blit of a CPU-rendered frame (<paramref name="rgba"/>: RGBA8,
    /// sRGB-encoded, tightly packed, <paramref name="width"/> × <paramref name="height"/>).
    /// </summary>
    public void EncodeFullFrameUpload(CommandEncoder encoder, TextureView targetView, ReadOnlySpan<byte> rgba, uint width, uint height)
    {
        EnsureFallbackTexture(width, height);

        var origin = new WGPUOrigin3D { X = 0, Y = 0, Z = 0 };
        var writeSize = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 };
        device.Queue.WriteTexture(fallbackTexture, 0, origin, rgba, width * 4, height, writeSize);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = (nint)targetView.Handle,
            DepthSlice = 0xFFFFFFFFu,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 1 },
        };
        var passDesc = new RenderPassDescriptor
        {
            ColorAttachmentCount = (UIntPtr)1,
            ColorAttachments = (nint)(&colorAttachment),
        };

        using var pass = encoder.BeginRenderPass(passDesc);
        pass.SetPipeline(textPipeline);
        pass.SetVertexBuffer(0, textVertexBuffer, 0, (ulong)(TextQuadVertices.Length * sizeof(float)));
        pass.SetBindGroup(0, fallbackBindGroup);
        pass.Draw(6);
        pass.End();
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
            imageBindGroups[handle].Dispose();
            imageTextureViews[handle].Dispose();
            imageTextures[handle].Dispose();
            imageBindGroups.Remove(handle);
            imageTextureViews.Remove(handle);
            imageTextures.Remove(handle);
        }
    }

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

    private void EnsureFallbackTexture(uint width, uint height)
    {
        if (!fallbackTexture.IsInvalid && fallbackWidth == width && fallbackHeight == height)
        {
            return;
        }
        if (!fallbackTextureView.IsInvalid)
        {
            fallbackTextureView.Dispose();
        }
        if (!fallbackTexture.IsInvalid)
        {
            fallbackTexture.Dispose();
        }
        if (!fallbackBindGroup.IsInvalid)
        {
            fallbackBindGroup.Dispose();
        }

        // The CPU frame is sRGB-encoded: an sRGB view decodes it on sample, so the sRGB target
        // re-encodes it once.
        fallbackTexture = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8UnormSrgb,
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            Dimension = TextureDimension.D2,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        fallbackTextureView = fallbackTexture.CreateView();
        fallbackWidth = width;
        fallbackHeight = height;

        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = fallbackTextureView.Handle };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = textSampler.Handle };
        fallbackBindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = textBindGroupLayout.Handle,
            EntryCount = (UIntPtr)2,
            Entries = (nint)entries,
        });
    }

    private void UpdateGeometryBindGroup()
    {
        if (!geomBindGroup.IsInvalid)
        {
            geomBindGroup.Dispose();
        }

        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry
        {
            Binding = 0,
            Buffer = (nint)geomUniformBuffer.Handle,
            Size = (ulong)sizeof(SurfaceSizeData),
        };
        entries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = (nint)geomStorageBuffer.Handle,
            Size = ulong.MaxValue,
        };
        geomBindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = geomBindGroupLayout.Handle,
            EntryCount = (UIntPtr)2,
            Entries = (nint)entries,
        });
    }

    /// <summary>Uploads shapes, glyphs, image quads and blur instances in batch order — one buffer write per kind.</summary>
    private void UploadSchedule(DrawList list)
    {
        var shapes = list.OrderedShapes;
        EnsureGeometryBufferCapacity(shapes.Count);
        if (shapes.Count > 0)
        {
            device.Queue.WriteBuffer(geomStorageBuffer, 0, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(shapes)));
        }

        var glyphs = list.OrderedGlyphs;
        if (glyphs.Count > 0)
        {
            int requiredBytes = glyphs.Count * sizeof(GlyphInstance);
            if (glyphInstanceCapacity < requiredBytes)
            {
                glyphInstanceCapacity = Math.Max(requiredBytes, glyphInstanceCapacity * 2);
                if (!glyphInstanceBuffer.IsInvalid)
                {
                    glyphInstanceBuffer.Dispose();
                }
                glyphInstanceBuffer = device.CreateBuffer(new BufferDescriptor
                {
                    Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
                    Size = (ulong)glyphInstanceCapacity,
                });
                UpdateGlyphInstanceBindGroup();
            }
            device.Queue.WriteBuffer(glyphInstanceBuffer, 0, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(glyphs)));
        }

        var colorGlyphs = list.OrderedColorGlyphs;
        if (colorGlyphs.Count > 0)
        {
            int requiredBytes = colorGlyphs.Count * sizeof(GlyphInstance);
            if (colorGlyphInstanceCapacity < requiredBytes)
            {
                colorGlyphInstanceCapacity = Math.Max(requiredBytes, colorGlyphInstanceCapacity * 2);
                if (!colorGlyphInstanceBuffer.IsInvalid)
                {
                    colorGlyphInstanceBuffer.Dispose();
                }
                colorGlyphInstanceBuffer = device.CreateBuffer(new BufferDescriptor
                {
                    Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
                    Size = (ulong)colorGlyphInstanceCapacity,
                });
                UpdateColorGlyphInstanceBindGroup();
            }
            device.Queue.WriteBuffer(colorGlyphInstanceBuffer, 0, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(colorGlyphs)));
        }

        var images = list.OrderedImages;
        if (images.Count > 0)
        {
            UploadImageQuads(images);
        }

        var blurs = list.OrderedBlurs;
        if (blurs.Count > 0)
        {
            int byteSize = blurs.Count * sizeof(BlurInstance);
            if (blurs.Count > blurInstanceCapacity)
            {
                blurInstanceBuffer.Dispose();
                blurInstanceBuffer = device.CreateBuffer(new BufferDescriptor
                {
                    Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
                    Size = (ulong)Math.Max(byteSize, 16),
                });
                blurInstanceCapacity = blurs.Count;
            }
            device.Queue.WriteBuffer(blurInstanceBuffer, 0, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(blurs)));
        }
    }

    /// <summary>
    /// Writes every image quad of the frame (device rect → clip space, with its UV sub-rect) into
    /// the image vertex buffer in one write; quad <c>i</c> occupies vertices [6i, 6i+6).
    /// </summary>
    private void UploadImageQuads(List<ImageQuad> quads)
    {
        int floats = quads.Count * ImageQuadFloats;
        if (imageVertexScratch.Length < floats)
        {
            imageVertexScratch = new float[Math.Max(floats, imageVertexScratch.Length * 2)];
        }
        if (quads.Count > imageQuadCapacity)
        {
            imageVertexBuffer.Dispose();
            imageQuadCapacity = Math.Max(quads.Count, imageQuadCapacity * 2);
            imageVertexBuffer = device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.Vertex | BufferUsage.CopyDst),
                Size = (ulong)(imageQuadCapacity * ImageQuadFloats * sizeof(float)),
            });
        }

        var verts = imageVertexScratch.AsSpan(0, floats);
        int o = 0;
        foreach (ref readonly var q in CollectionsMarshal.AsSpan(quads))
        {
            float l = (float)(q.L / targetWidth * 2.0 - 1.0);
            float r = (float)(q.R / targetWidth * 2.0 - 1.0);
            float t = (float)(1.0 - q.T / targetHeight * 2.0);
            float b = (float)(1.0 - q.B / targetHeight * 2.0);
            verts[o] = l; verts[o + 1] = t; verts[o + 2] = q.U0; verts[o + 3] = q.V0;
            verts[o + 4] = r; verts[o + 5] = t; verts[o + 6] = q.U1; verts[o + 7] = q.V0;
            verts[o + 8] = l; verts[o + 9] = b; verts[o + 10] = q.U0; verts[o + 11] = q.V1;
            verts[o + 12] = r; verts[o + 13] = t; verts[o + 14] = q.U1; verts[o + 15] = q.V0;
            verts[o + 16] = r; verts[o + 17] = b; verts[o + 18] = q.U1; verts[o + 19] = q.V1;
            verts[o + 20] = l; verts[o + 21] = b; verts[o + 22] = q.U0; verts[o + 23] = q.V1;
            o += ImageQuadFloats;
        }
        device.Queue.WriteBuffer(imageVertexBuffer, 0, MemoryMarshal.AsBytes(verts));
    }

    /// <summary>
    /// Records the batches into as few render passes as possible: one pass, broken only where a
    /// mono-glyph or backdrop-blur batch needs a fresh copy of the framebuffer under it. Returns the
    /// number of framebuffer copies made.
    /// </summary>
    private int EncodeSchedule(CommandEncoder encoder, Texture target, TextureView targetView, DrawList list)
    {
        int copies = 0;
        BindGroup blurBind0 = default;
        BindGroup blurInstanceBind = default;
        var images = list.OrderedImages;
        var pass = BeginFramePass(encoder, targetView, clear: true);
        try
        {
            DrawKind? bound = null;
            foreach (ref readonly var batch in list.Batches)
            {
                if (batch.Count == 0)
                {
                    continue;
                }

                if ((batch.Kind == DrawKind.Glyph && !SkipGlyphs) || batch.Kind == DrawKind.Blur)
                {
                    if (CanCopyBackground())
                    {
                        pass.End();
                        pass.Dispose();
                        CopyFramebufferRegion(encoder, target, batch.MinX, batch.MinY, batch.MaxX, batch.MaxY);
                        copies++;
                        pass = BeginFramePass(encoder, targetView, clear: false);
                        bound = null;
                    }
                    else if (batch.Kind == DrawKind.Blur)
                    {
                        continue;
                    }
                }

                switch (batch.Kind)
                {
                    case DrawKind.Shape:
                        if (bound != DrawKind.Shape)
                        {
                            pass.SetPipeline(geomPipeline);
                            pass.SetBindGroup(0, geomBindGroup);
                        }
                        pass.Draw(4, (uint)batch.Count, 0, (uint)batch.Start);
                        break;

                    case DrawKind.Glyph:
                        if (SkipGlyphs)
                        {
                            continue;
                        }
                        if (bound != DrawKind.Glyph)
                        {
                            pass.SetPipeline(glyphPipeline);
                            pass.SetBindGroup(0, glyphAtlasBindGroup);
                            pass.SetBindGroup(1, glyphInstanceBindGroup);
                        }
                        pass.Draw(4, (uint)batch.Count, 0, (uint)batch.Start);
                        break;

                    case DrawKind.ColorGlyph:
                        if (SkipGlyphs)
                        {
                            continue;
                        }
                        if (bound != DrawKind.ColorGlyph)
                        {
                            pass.SetPipeline(colorGlyphPipeline);
                            pass.SetBindGroup(0, colorGlyphAtlasBindGroup);
                            pass.SetBindGroup(1, colorGlyphInstanceBindGroup);
                        }
                        pass.Draw(4, (uint)batch.Count, 0, (uint)batch.Start);
                        break;

                    case DrawKind.Image:
                        if (bound != DrawKind.Image)
                        {
                            pass.SetPipeline(textPipeline);
                            pass.SetVertexBuffer(0, imageVertexBuffer, 0, (ulong)(images.Count * ImageQuadFloats * sizeof(float)));
                        }
                        for (int i = batch.Start; i < batch.Start + batch.Count; i++)
                        {
                            pass.SetBindGroup(0, imageBindGroups[images[i].Handle]);
                            pass.Draw(6, 1, (uint)(i * 6), 0);
                        }
                        break;

                    case DrawKind.Blur:
                        if (blurBind0.IsInvalid)
                        {
                            CreateBlurBindGroups(list.OrderedBlurs.Count, out blurBind0, out blurInstanceBind);
                        }
                        if (bound != DrawKind.Blur)
                        {
                            pass.SetPipeline(blurPipeline);
                            pass.SetBindGroup(0, blurBind0);
                            pass.SetBindGroup(1, blurInstanceBind);
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
            if (!blurBind0.IsInvalid)
            {
                blurBind0.Dispose();
            }
            if (!blurInstanceBind.IsInvalid)
            {
                blurInstanceBind.Dispose();
            }
        }
        return copies;
    }

    private static RenderPass BeginFramePass(CommandEncoder encoder, TextureView targetView, bool clear)
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

    /// <summary>
    /// Copies the device-pixel region (rounded out, clamped to the target) of the framebuffer into
    /// the background-copy texture at the same position.
    /// </summary>
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

    private void CreateBlurBindGroups(int blurCount, out BindGroup bind0, out BindGroup instanceBind)
    {
        int byteSize = Math.Max(blurCount * sizeof(BlurInstance), 16);
        var bind0Entries = stackalloc BindGroupEntry[3];
        bind0Entries[0] = new BindGroupEntry { Binding = 0, Buffer = geomUniformBuffer.Handle, Offset = 0, Size = (ulong)sizeof(SurfaceSizeData) };
        bind0Entries[1] = new BindGroupEntry { Binding = 1, TextureView = bgCopyView.Handle };
        bind0Entries[2] = new BindGroupEntry { Binding = 2, Sampler = blurSampler.Handle };
        bind0 = device.CreateBindGroup(new BindGroupDescriptor { Layout = blurBind0Layout.Handle, EntryCount = (UIntPtr)3, Entries = (nint)bind0Entries });

        var instEntries = stackalloc BindGroupEntry[1];
        instEntries[0] = new BindGroupEntry { Binding = 0, Buffer = blurInstanceBuffer.Handle, Offset = 0, Size = (ulong)byteSize };
        instanceBind = device.CreateBindGroup(new BindGroupDescriptor { Layout = blurInstanceLayout.Handle, EntryCount = (UIntPtr)1, Entries = (nint)instEntries });
    }

    private void EnsureImageTexture(int imageId, ComposeImage img)
    {
        if (imageBindGroups.ContainsKey(imageId))
        {
            return;
        }

        // Image pixels are sRGB-encoded (like every 8-bit image file).
        var texture = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)img.Width, Height = (uint)img.Height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8UnormSrgb,
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            Dimension = TextureDimension.D2,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        var view = texture.CreateView();

        var origin = new WGPUOrigin3D { X = 0, Y = 0, Z = 0 };
        var writeSize = new Extent3D { Width = (uint)img.Width, Height = (uint)img.Height, DepthOrArrayLayers = 1 };
        device.Queue.WriteTexture(texture, 0, origin, img.Pixels, (uint)img.Width * 4, (uint)img.Height, writeSize);

        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = view.Handle };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = textSampler.Handle };
        var bindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = textBindGroupLayout.Handle,
            EntryCount = (UIntPtr)2,
            Entries = (nint)entries,
        });

        imageTextures[imageId] = texture;
        imageTextureViews[imageId] = view;
        imageBindGroups[imageId] = bindGroup;
    }

    private void EnsureGeometryBufferCapacity(int minCapacity)
    {
        int requiredCapacity = minCapacity == 0 ? 1 : minCapacity;
        if (geomStorageCapacity < requiredCapacity)
        {
            if (!geomStorageBuffer.IsInvalid)
            {
                geomStorageBuffer.Dispose();
            }
            geomStorageCapacity = Math.Max(256, requiredCapacity * 2);
            geomStorageBuffer = device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
                Size = (ulong)((long)geomStorageCapacity * sizeof(ShapeInstance)),
            });
            UpdateGeometryBindGroup();
        }
    }

    private void UploadSurfaceSize(ComposeParameters parameters)
    {
        var data = new SurfaceSizeData
        {
            Width = targetWidth,
            Height = targetHeight,
            OffsetX = 0,
            OffsetY = 0,
            TextGamma = parameters.TextGamma,
            LightWeight = parameters.LightWeight,
            Dissolve = parameters.Dissolve,
        };
        var byteSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref data, 1));
        device.Queue.WriteBuffer(geomUniformBuffer, 0, byteSpan);
        device.Queue.WriteBuffer(glyphUniformBuffer, 0, byteSpan);
    }

    private static (StringView Vs, StringView Fs) EntryPoints(byte* vs, byte* fs)
        => (new StringView { Data = (nint)vs, Length = 2 }, new StringView { Data = (nint)fs, Length = 2 });

    private RenderPipeline BuildGeometryPipeline(out PipelineLayout pipelineLayout, out BindGroupLayout bindGroupLayout)
    {
        var bglEntries = stackalloc BindGroupLayoutEntry[2];
        bglEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)sizeof(SurfaceSizeData) },
        };
        bglEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = 0 },
        };
        bindGroupLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = (UIntPtr)2, Entries = (nint)bglEntries });

        var layoutHandles = stackalloc nint[1];
        layoutHandles[0] = bindGroupLayout.Handle;
        pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = (UIntPtr)1,
            BindGroupLayouts = (nint)layoutHandles,
        });

        var blendState = new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
        };
        return BuildPipeline(geomShader, pipelineLayout, &blendState, PrimitiveTopology.TriangleStrip, vertexBuffers: null, vertexBufferCount: 0);
    }

    private RenderPipeline BuildTextPipeline(out PipelineLayout pipelineLayout, out BindGroupLayout bindGroupLayout)
    {
        var entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2, Multisampled = 0 },
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        bindGroupLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = (UIntPtr)2, Entries = (nint)entries });

        var layoutHandles = stackalloc nint[1];
        layoutHandles[0] = bindGroupLayout.Handle;
        pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = (UIntPtr)1,
            BindGroupLayouts = (nint)layoutHandles,
        });

        var vertexAttributes = stackalloc VertexAttribute[2];
        vertexAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        vertexAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 };
        var vertexBuffers = stackalloc VertexBufferLayout[1];
        vertexBuffers[0] = new VertexBufferLayout
        {
            StepMode = VertexStepMode.Vertex,
            ArrayStride = 16,
            AttributeCount = (UIntPtr)2,
            Attributes = (nint)vertexAttributes,
        };

        var blendState = new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
        };
        return BuildPipeline(textShader, pipelineLayout, &blendState, PrimitiveTopology.TriangleList, vertexBuffers, 1);
    }

    private RenderPipeline BuildGlyphPipeline(out PipelineLayout pipelineLayout, out BindGroupLayout atlasLayout, out BindGroupLayout instanceLayout)
    {
        var atlasEntries = stackalloc BindGroupLayoutEntry[4];
        atlasEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)sizeof(SurfaceSizeData) },
        };
        atlasEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2, Multisampled = 0 },
        };
        atlasEntries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = (ulong)ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        // Binding 3: the framebuffer copy (local background), read via textureLoad.
        atlasEntries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = (ulong)ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2, Multisampled = 0 },
        };
        atlasLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = (UIntPtr)4, Entries = (nint)atlasEntries });

        var instanceEntries = stackalloc BindGroupLayoutEntry[1];
        instanceEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = 0 },
        };
        instanceLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = (UIntPtr)1, Entries = (nint)instanceEntries });

        var layoutHandles = stackalloc nint[2];
        layoutHandles[0] = atlasLayout.Handle;
        layoutHandles[1] = instanceLayout.Handle;
        pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = (UIntPtr)2,
            BindGroupLayouts = (nint)layoutHandles,
        });

        var blendState = new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
        };
        return BuildPipeline(glyphShader, pipelineLayout, &blendState, PrimitiveTopology.TriangleStrip, vertexBuffers: null, vertexBufferCount: 0);
    }

    private RenderPipeline BuildBlurPipeline(out PipelineLayout pipelineLayout, out BindGroupLayout bind0Layout, out BindGroupLayout instanceLayout)
    {
        var bind0Entries = stackalloc BindGroupLayoutEntry[3];
        bind0Entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)sizeof(SurfaceSizeData) },
        };
        bind0Entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2, Multisampled = 0 },
        };
        bind0Entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = (ulong)ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        bind0Layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = (UIntPtr)3, Entries = (nint)bind0Entries });

        var instanceEntries = stackalloc BindGroupLayoutEntry[1];
        instanceEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = 0 },
        };
        instanceLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = (UIntPtr)1, Entries = (nint)instanceEntries });

        var layoutHandles = stackalloc nint[2];
        layoutHandles[0] = bind0Layout.Handle;
        layoutHandles[1] = instanceLayout.Handle;
        pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = (UIntPtr)2,
            BindGroupLayouts = (nint)layoutHandles,
        });

        var blendState = new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
        };
        return BuildPipeline(blurShader, pipelineLayout, &blendState, PrimitiveTopology.TriangleStrip, vertexBuffers: null, vertexBufferCount: 0);
    }

    private RenderPipeline BuildColorGlyphPipeline(out PipelineLayout pipelineLayout)
    {
        var layoutHandles = stackalloc nint[2];
        layoutHandles[0] = glyphAtlasLayout.Handle;
        layoutHandles[1] = glyphInstanceLayout.Handle;
        pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = (UIntPtr)2,
            BindGroupLayouts = (nint)layoutHandles,
        });

        var blendState = new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
        };
        return BuildPipeline(colorGlyphShader, pipelineLayout, &blendState, PrimitiveTopology.TriangleStrip, vertexBuffers: null, vertexBufferCount: 0);
    }

    private RenderPipeline BuildPipeline(ShaderModule shader, PipelineLayout layout, BlendState* blend,
        PrimitiveTopology topology, VertexBufferLayout* vertexBuffers, int vertexBufferCount)
    {
        Span<byte> vsName = stackalloc byte[3];
        Span<byte> fsName = stackalloc byte[3];
        Encoding.UTF8.GetBytes("vs", vsName);
        Encoding.UTF8.GetBytes("fs", fsName);

        fixed (byte* vsPtr = vsName)
        fixed (byte* fsPtr = fsName)
        {
            var (vsEntry, fsEntry) = EntryPoints(vsPtr, fsPtr);
            var colorTarget = new ColorTargetState
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Blend = (nint)blend,
                WriteMask = (ulong)ColorWriteMask.All,
            };
            var vertex = new VertexState
            {
                Module = shader.Handle,
                EntryPoint = vsEntry,
                BufferCount = (UIntPtr)vertexBufferCount,
                Buffers = (nint)vertexBuffers,
            };
            var fragment = new FragmentState
            {
                Module = shader.Handle,
                EntryPoint = fsEntry,
                TargetCount = (UIntPtr)1,
                Targets = (nint)(&colorTarget),
            };
            var desc = new RenderPipelineDescriptor
            {
                Layout = layout.Handle,
                Vertex = vertex,
                Fragment = (nint)(&fragment),
                Primitive = new PrimitiveState
                {
                    Topology = topology,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None,
                },
                Multisample = new MultisampleState { Count = 1, Mask = ~0u },
            };
            return device.CreateRenderPipeline(desc);
        }
    }

    private void UpdateGlyphAtlasBindGroup()
    {
        if (!glyphAtlasBindGroup.IsInvalid)
        {
            glyphAtlasBindGroup.Dispose();
        }

        var entries = stackalloc BindGroupEntry[4];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = glyphUniformBuffer.Handle, Offset = 0, Size = (ulong)sizeof(SurfaceSizeData) };
        entries[1] = new BindGroupEntry { Binding = 1, TextureView = glyphAtlasView.Handle };
        entries[2] = new BindGroupEntry { Binding = 2, Sampler = glyphSampler.Handle };
        entries[3] = new BindGroupEntry { Binding = 3, TextureView = bgCopyView.Handle };
        glyphAtlasBindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = glyphAtlasLayout.Handle,
            EntryCount = (UIntPtr)4,
            Entries = (nint)entries,
        });
    }

    private void UpdateGlyphInstanceBindGroup()
    {
        if (!glyphInstanceBindGroup.IsInvalid)
        {
            glyphInstanceBindGroup.Dispose();
        }

        var entries = stackalloc BindGroupEntry[1];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = glyphInstanceBuffer.Handle, Offset = 0, Size = (ulong)glyphInstanceCapacity };
        glyphInstanceBindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = glyphInstanceLayout.Handle,
            EntryCount = (UIntPtr)1,
            Entries = (nint)entries,
        });
    }

    private void UpdateColorGlyphInstanceBindGroup()
    {
        if (!colorGlyphInstanceBindGroup.IsInvalid)
        {
            colorGlyphInstanceBindGroup.Dispose();
        }

        var entries = stackalloc BindGroupEntry[1];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = colorGlyphInstanceBuffer.Handle, Offset = 0, Size = (ulong)colorGlyphInstanceCapacity };
        colorGlyphInstanceBindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = glyphInstanceLayout.Handle,
            EntryCount = (UIntPtr)1,
            Entries = (nint)entries,
        });
    }

    private void UpdateColorGlyphAtlasBindGroup()
    {
        if (!colorGlyphAtlasBindGroup.IsInvalid)
        {
            colorGlyphAtlasBindGroup.Dispose();
        }

        var entries = stackalloc BindGroupEntry[4];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = glyphUniformBuffer.Handle, Offset = 0, Size = (ulong)sizeof(SurfaceSizeData) };
        entries[1] = new BindGroupEntry { Binding = 1, TextureView = colorGlyphAtlasView.Handle };
        entries[2] = new BindGroupEntry { Binding = 2, Sampler = colorGlyphSampler.Handle };
        // Binding 3 is required by the shared layout; the colour glyph shader does not sample it.
        entries[3] = new BindGroupEntry { Binding = 3, TextureView = bgCopyView.Handle };
        colorGlyphAtlasBindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = glyphAtlasLayout.Handle,
            EntryCount = (UIntPtr)4,
            Entries = (nint)entries,
        });
    }

    /// <summary>Releases every GPU resource the composer created (not the device).</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        geomBindGroup.Dispose();
        if (!geomStorageBuffer.IsInvalid)
        {
            geomStorageBuffer.Dispose();
        }
        geomUniformBuffer.Dispose();
        geomBindGroupLayout.Dispose();
        geomPipeline.Dispose();
        geomPipelineLayout.Dispose();
        geomShader.Dispose();

        textVertexBuffer.Dispose();
        textSampler.Dispose();
        textBindGroupLayout.Dispose();
        textPipeline.Dispose();
        textPipelineLayout.Dispose();
        textShader.Dispose();

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

        foreach (var bg in imageBindGroups.Values)
        {
            bg.Dispose();
        }
        foreach (var view in imageTextureViews.Values)
        {
            view.Dispose();
        }
        foreach (var tex in imageTextures.Values)
        {
            tex.Dispose();
        }
        imageBindGroups.Clear();
        imageTextureViews.Clear();
        imageTextures.Clear();
        imageVertexBuffer.Dispose();

        if (!colorGlyphInstanceBindGroup.IsInvalid)
        {
            colorGlyphInstanceBindGroup.Dispose();
        }
        colorGlyphInstanceBuffer.Dispose();
        colorGlyphAtlasBindGroup.Dispose();
        colorGlyphSampler.Dispose();
        colorGlyphAtlasView.Dispose();
        colorGlyphAtlas.Dispose();
        colorGlyphPipeline.Dispose();
        colorGlyphPipelineLayout.Dispose();
        colorGlyphShader.Dispose();

        if (!glyphInstanceBindGroup.IsInvalid)
        {
            glyphInstanceBindGroup.Dispose();
        }
        glyphAtlasBindGroup.Dispose();
        glyphInstanceBuffer.Dispose();
        glyphUniformBuffer.Dispose();
        if (!bgCopyView.IsInvalid)
        {
            bgCopyView.Dispose();
        }
        if (!bgCopyTexture.IsInvalid)
        {
            bgCopyTexture.Dispose();
        }
        glyphAtlasView.Dispose();
        glyphAtlas.Dispose();
        glyphSampler.Dispose();
        glyphInstanceLayout.Dispose();
        glyphAtlasLayout.Dispose();
        glyphPipeline.Dispose();
        glyphPipelineLayout.Dispose();
        glyphShader.Dispose();

        blurPipeline.Dispose();
        blurPipelineLayout.Dispose();
        blurBind0Layout.Dispose();
        blurInstanceLayout.Dispose();
        blurSampler.Dispose();
        blurShader.Dispose();
        if (!blurInstanceBuffer.IsInvalid)
        {
            blurInstanceBuffer.Dispose();
        }
    }
}
