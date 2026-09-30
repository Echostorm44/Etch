using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Descriptors;
using Etch.Shaders;

namespace Etch.Gpu.Pipelines;

/// <summary>
/// GPU resources for the dual-filter blur (shaders/blur/down.wgsl, up.wgsl): the down and up
/// pipelines, their bind groups, and a full-target triangle. <c>Etch.Gpu.Compositor.BlurGpuPipeline</c>
/// drives the pass chain. Intermediate levels are <see cref="IntermediateFormat"/> so repeated passes
/// do not accumulate 8-bit rounding; only the last pass writes the caller's format.
/// </summary>
public sealed unsafe class BlurPipeline : IDisposable
{
    /// <summary>Format of the textures between passes.</summary>
    public const TextureFormat IntermediateFormat = TextureFormat.Rgba16Float;

    private readonly Device _device;
    private readonly BindGroupLayout _perFrameLayout;
    private readonly BindGroupLayout _textureLayout;
    private readonly PipelineLayout _layout;
    private readonly RenderPipeline _downPipeline;
    private readonly RenderPipeline _upPipeline;
    private readonly RenderPipeline _outputUpPipeline;
    private readonly Buffer _perFrameBuffer;
    private readonly BindGroup _perFrameBindGroup;
    private readonly Buffer _vertexBuffer;
    private readonly Sampler _sampler;
    private bool _disposed;

    /// <summary>Downsample into an <see cref="IntermediateFormat"/> level.</summary>
    public RenderPipeline DownPipeline => _downPipeline;

    /// <summary>Upsample into an <see cref="IntermediateFormat"/> level.</summary>
    public RenderPipeline UpPipeline => _upPipeline;

    /// <summary>The final upsample, writing <see cref="OutputFormat"/>.</summary>
    public RenderPipeline OutputUpPipeline => _outputUpPipeline;

    /// <summary>Format the final pass writes.</summary>
    public TextureFormat OutputFormat { get; }

    /// <summary>Group 0: the per-frame uniform.</summary>
    public BindGroup PerFrameBindGroup => _perFrameBindGroup;

    /// <summary>Three clip-space vertices of one triangle covering the whole target.</summary>
    public Buffer VertexBuffer => _vertexBuffer;

    [StructLayout(LayoutKind.Sequential)]
    private struct PerFrameData
    {
        public float SurfaceSizeX;
        public float SurfaceSizeY;
        public float Pad0X;
        public float Pad0Y;
    }

    /// <param name="device">Device the resources are created on.</param>
    /// <param name="outputFormat">Format of the blur's destination texture.</param>
    public BlurPipeline(Device device, TextureFormat outputFormat = TextureFormat.Bgra8UnormSrgb)
    {
        _device = device;
        OutputFormat = outputFormat;

        var perFrameEntries = stackalloc BindGroupLayoutEntry[1];
        perFrameEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = 0,
                MinBindingSize = (ulong)sizeof(PerFrameData)
            }
        };

        var textureEntries = stackalloc BindGroupLayoutEntry[2];
        textureEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                Multisampled = 0,
                ViewDimension = TextureViewDimension.D2
            }
        };
        textureEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout
            {
                Type = SamplerBindingType.Filtering
            }
        };

        _perFrameLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = 1, Entries = (IntPtr)perFrameEntries });
        _textureLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor { EntryCount = 2, Entries = (IntPtr)textureEntries });

        var layouts = stackalloc nint[2];
        layouts[0] = _perFrameLayout.Handle;
        layouts[1] = _textureLayout.Handle;
        _layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayoutCount = 2, BindGroupLayouts = (IntPtr)layouts });

        using var downShaderModule = device.CreateShaderModuleWgsl(System.Text.Encoding.UTF8.GetString(ShaderResources.down), "BlurDown");
        using var upShaderModule = device.CreateShaderModuleWgsl(System.Text.Encoding.UTF8.GetString(ShaderResources.up), "BlurUp");

        var vertexAttributes = stackalloc VertexAttribute[1];
        vertexAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        var vertexBuffers = stackalloc VertexBufferLayout[1];
        vertexBuffers[0] = new VertexBufferLayout
        {
            StepMode = VertexStepMode.Vertex,
            ArrayStride = 8,
            AttributeCount = (UIntPtr)1,
            Attributes = (IntPtr)vertexAttributes
        };

        byte[] downVertexEntry = System.Text.Encoding.UTF8.GetBytes(ShaderResources.DownLayout.VertexEntryPoint);
        byte[] downFragmentEntry = System.Text.Encoding.UTF8.GetBytes(ShaderResources.DownLayout.FragmentEntryPoint);
        byte[] upVertexEntry = System.Text.Encoding.UTF8.GetBytes(ShaderResources.UpLayout.VertexEntryPoint);
        byte[] upFragmentEntry = System.Text.Encoding.UTF8.GetBytes(ShaderResources.UpLayout.FragmentEntryPoint);

        _downPipeline = CreatePipeline(device, _layout, downShaderModule, downVertexEntry, downFragmentEntry, vertexBuffers, IntermediateFormat);
        _upPipeline = CreatePipeline(device, _layout, upShaderModule, upVertexEntry, upFragmentEntry, vertexBuffers, IntermediateFormat);
        _outputUpPipeline = CreatePipeline(device, _layout, upShaderModule, upVertexEntry, upFragmentEntry, vertexBuffers, outputFormat);

        _perFrameBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
            Size = (ulong)sizeof(PerFrameData)
        });

        var perFrameEntry = new BindGroupEntry { Binding = 0, Buffer = (IntPtr)(nint)_perFrameBuffer.Handle, Offset = 0, Size = (ulong)sizeof(PerFrameData) };
        _perFrameBindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = _perFrameLayout.Handle,
            EntryCount = (UIntPtr)1,
            Entries = (IntPtr)(&perFrameEntry)
        });

        // One oversized triangle: covers [-1,1]^2 with no diagonal seam.
        Span<float> triangle = stackalloc float[] { -1f, -1f, 3f, -1f, -1f, 3f };
        _vertexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Vertex | BufferUsage.CopyDst),
            Size = (ulong)(triangle.Length * sizeof(float))
        });
        device.Queue.WriteBuffer(_vertexBuffer, 0, MemoryMarshal.AsBytes(triangle));

        // The shaders address texels with textureLoad; the sampler only satisfies the layout.
        _sampler = device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0f,
            LodMaxClamp = 32f,
            MaxAnisotropy = 1
        });
    }

    private static RenderPipeline CreatePipeline(Device device, PipelineLayout layout, ShaderModule module, byte[] vertexEntry, byte[] fragmentEntry, VertexBufferLayout* vertexBuffers, TextureFormat target)
    {
        fixed (byte* vertexEntryPtr = vertexEntry)
        fixed (byte* fragmentEntryPtr = fragmentEntry)
        {
            var vertexState = new VertexState
            {
                Module = module.Handle,
                EntryPoint = new StringView { Data = (IntPtr)vertexEntryPtr, Length = (UIntPtr)vertexEntry.Length },
                BufferCount = 1,
                Buffers = (IntPtr)vertexBuffers
            };

            var colorTarget = new ColorTargetState { Format = target, WriteMask = (ulong)ColorWriteMask.All };

            var fragmentState = new FragmentState
            {
                Module = module.Handle,
                EntryPoint = new StringView { Data = (IntPtr)fragmentEntryPtr, Length = (UIntPtr)fragmentEntry.Length },
                TargetCount = 1,
                Targets = (IntPtr)(&colorTarget)
            };

            return device.CreateRenderPipeline(new RenderPipelineDescriptor
            {
                Layout = layout.Handle,
                Vertex = vertexState,
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = (IntPtr)(&fragmentState)
            });
        }
    }

    /// <summary>Group 1 for a pass reading <paramref name="source"/>. The caller disposes it after the pass is submitted.</summary>
    /// <param name="source">View of the level the pass reads (needs TextureBinding usage).</param>
    public BindGroup CreateTextureBindGroup(TextureView source)
    {
        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = (IntPtr)(nint)source.Handle };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = (IntPtr)(nint)_sampler.Handle };
        return _device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = _textureLayout.Handle,
            EntryCount = (UIntPtr)2,
            Entries = (IntPtr)entries
        });
    }

    /// <summary>Records one pass of <paramref name="pipeline"/> reading <paramref name="sourceBindGroup"/> into <paramref name="target"/>.</summary>
    /// <param name="encoder">Encoder the pass is recorded into.</param>
    /// <param name="pipeline"><see cref="DownPipeline"/>, <see cref="UpPipeline"/> or <see cref="OutputUpPipeline"/>.</param>
    /// <param name="sourceBindGroup">From <see cref="CreateTextureBindGroup"/>.</param>
    /// <param name="target">View of the level the pass writes.</param>
    public void RecordPass(CommandEncoder encoder, RenderPipeline pipeline, BindGroup sourceBindGroup, TextureView target)
    {
        var colorAttachment = new RenderPassColorAttachment
        {
            View = (nint)target.Handle,
            DepthSlice = 0xFFFFFFFFu,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 0 }
        };

        using var pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachmentCount = (UIntPtr)1,
            ColorAttachments = (nint)(&colorAttachment)
        });
        pass.SetPipeline(pipeline);
        pass.SetBindGroup(0, _perFrameBindGroup);
        pass.SetBindGroup(1, sourceBindGroup);
        pass.SetVertexBuffer(0, _vertexBuffer);
        pass.Draw(3);
        pass.End();
    }

    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    public void SetSurfaceSize(float width, float height)
    {
        PerFrameData data;
        data.SurfaceSizeX = width;
        data.SurfaceSizeY = height;
        data.Pad0X = 0;
        data.Pad0Y = 0;
        _device.Queue.WriteBuffer(_perFrameBuffer, 0, new ReadOnlySpan<byte>(&data, sizeof(PerFrameData)));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _perFrameBindGroup.Dispose();
        _perFrameBuffer.Dispose();
        _vertexBuffer.Dispose();
        _sampler.Dispose();
        _downPipeline.Dispose();
        _upPipeline.Dispose();
        _outputUpPipeline.Dispose();
        _layout.Dispose();
        _perFrameLayout.Dispose();
        _textureLayout.Dispose();
    }
}
