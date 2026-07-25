using System;
using System.Runtime.InteropServices;
using System.Text;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Pipelines;
using Etch.Shaders;

namespace Etch.Gpu.Compositor.Pipelines;

public readonly struct GradientSpecKey : IShaderSpecKey
{
    public GradientKind Kind { get; init; }
    public ExtendMode Extend { get; init; }

    public int Hash => HashCode.Combine(Kind, Extend);

    public ReadOnlySpan<Shaders.ConstantEntry> ToEntries() => new[]
    {
        new Shaders.ConstantEntry("gradient_kind", (uint)Kind),
        new Shaders.ConstantEntry("extend", (uint)Extend),
    };
}

public sealed unsafe class GradientGpuPipeline : IDisposable
{
    private readonly Device _device;
    private readonly SpecializedPipelineCache _cache;
    private readonly Buffer _perFrameBuffer;
    private readonly Buffer _perDrawBuffer;
    private readonly BindGroupLayout _lutLayout;
    private readonly BindGroupLayout _perDrawLayout;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct PerFrameData
    {
        public float SurfaceSizeX;
        public float SurfaceSizeY;
        public float Pad0X;
        public float Pad0Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerDrawData
    {
        public float P0X;
        public float P0Y;
        public float P1X;
        public float P1Y;
        public float CenterX;
        public float CenterY;
        public float Radius;
        public float StartAngle;
        public float EndAngle;
        public float Color0R;
        public float Color0G;
        public float Color0B;
        public float Color0A;
        public float Color1R;
        public float Color1G;
        public float Color1B;
        public float Color1A;
    }

    public GradientGpuPipeline(Device device)
    {
        _device = device;
        _cache = new SpecializedPipelineCache();
        _perFrameBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = 64ul,
            Size = (ulong)sizeof(PerFrameData)
        });
        _perDrawBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = 64ul,
            Size = (ulong)sizeof(PerDrawData)
        });
        _lutLayout = CreateLutLayout();
        _perDrawLayout = CreatePerDrawLayout();
    }

    public void SetSurfaceSize(float width, float height)
    {
        PerFrameData data;
        data.SurfaceSizeX = width;
        data.SurfaceSizeY = height;
        data.Pad0X = 0;
        data.Pad0Y = 0;

        var span = new ReadOnlySpan<byte>(&data, sizeof(PerFrameData));
        _device.Queue.WriteBuffer(_perFrameBuffer, 0, span);
    }

    public void SetGradient(
        float x0, float y0,
        float x1, float y1,
        float centerX, float centerY,
        float radius,
        float startAngle, float endAngle,
        float r0, float g0, float b0, float a0,
        float r1, float g1, float b1, float a1)
    {
        PerDrawData data;
        data.P0X = x0;
        data.P0Y = y0;
        data.P1X = x1;
        data.P1Y = y1;
        data.CenterX = centerX;
        data.CenterY = centerY;
        data.Radius = radius;
        data.StartAngle = startAngle;
        data.EndAngle = endAngle;
        data.Color0R = r0;
        data.Color0G = g0;
        data.Color0B = b0;
        data.Color0A = a0;
        data.Color1R = r1;
        data.Color1G = g1;
        data.Color1B = b1;
        data.Color1A = a1;

        var span = new ReadOnlySpan<byte>(&data, sizeof(PerDrawData));
        _device.Queue.WriteBuffer(_perDrawBuffer, 0, span);
    }

    public void Record(
        FrameContext frame,
        RenderPass pass,
        TileQuadBuffers quadBuffers,
        GradientKind kind,
        ExtendMode extend)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = new GradientSpecKey { Kind = kind, Extend = extend };
        var pipeline = _cache.GetOrCreate(key, k => CreatePipeline(k.Kind, k.Extend));

        pass.SetPipeline(pipeline);
        pass.SetVertexBuffer(0, quadBuffers.UnitQuadVertex, 0, 32);
        pass.SetVertexBuffer(1, quadBuffers.PerTileInstance, 0, (ulong)(quadBuffers.PerTileInstance.IsInvalid ? 0 : 256 * 32));
        pass.Draw(4, (uint)(quadBuffers.PerTileInstance.IsInvalid ? 0 : 256));
        pass.End();
    }

    private RenderPipeline CreatePipeline(GradientKind kind, ExtendMode extend)
    {
        var perFrameLayout = CreatePerFrameLayout();

        var layouts = stackalloc nint[3];
        layouts[0] = perFrameLayout.Handle;
        layouts[1] = _lutLayout.Handle;
        layouts[2] = _perDrawLayout.Handle;

        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 3,
            BindGroupLayouts = (IntPtr)layouts
        };

        var pipelineLayout = _device.CreatePipelineLayout(pipelineLayoutDesc);
        perFrameLayout.Dispose();

        byte[] shaderBytes = ShaderResources.gradient.ToArray();
        string wgsl = Encoding.UTF8.GetString(shaderBytes);
        using var shaderModule = _device.CreateShaderModuleWgsl(wgsl, "Gradient");

        var vertexAttributes = stackalloc VertexAttribute[1];
        vertexAttributes[0] = new VertexAttribute
        {
            Format = VertexFormat.Float32x2,
            Offset = 0,
            ShaderLocation = 0
        };

        var instanceAttributes = stackalloc VertexAttribute[1];
        instanceAttributes[0] = new VertexAttribute
        {
            Format = VertexFormat.Float32x4,
            Offset = 0,
            ShaderLocation = 1
        };

        var vertexBuffers = stackalloc VertexBufferLayout[2];
        vertexBuffers[0] = new VertexBufferLayout
        {
            StepMode = VertexStepMode.Vertex,
            ArrayStride = 8,
            AttributeCount = 1,
            Attributes = (IntPtr)vertexAttributes
        };
        vertexBuffers[1] = new VertexBufferLayout
        {
            StepMode = VertexStepMode.Instance,
            ArrayStride = 16,
            AttributeCount = 1,
            Attributes = (IntPtr)instanceAttributes
        };

        byte[] vertexEntryBytes = Encoding.UTF8.GetBytes(ShaderResources.GradientLayout.VertexEntryPoint);
        byte[] fragmentEntryBytes = Encoding.UTF8.GetBytes(ShaderResources.GradientLayout.FragmentEntryPoint);

        const int MaxKeyLength = 16;
        int bufferSize = 2 * sizeof(Descriptors.ConstantEntry) + 2 * MaxKeyLength;
        byte* specBuffer = stackalloc byte[bufferSize];

        Descriptors.ConstantEntry* entries = (Descriptors.ConstantEntry*)specBuffer;
        byte* stringBase = specBuffer + 2 * sizeof(Descriptors.ConstantEntry);

        entries[0] = new Descriptors.ConstantEntry
        {
            NextInChain = IntPtr.Zero,
            Key = (IntPtr)(stringBase),
            Value = (uint)kind
        };
        byte[] key0Bytes = Encoding.UTF8.GetBytes("gradient_kind\0");
        for (int i = 0; i < key0Bytes.Length && i < MaxKeyLength; i++)
        {
            stringBase[i] = key0Bytes[i];
        }

        entries[1] = new Descriptors.ConstantEntry
        {
            NextInChain = IntPtr.Zero,
            Key = (IntPtr)(stringBase + MaxKeyLength),
            Value = (uint)extend
        };
        byte[] key1Bytes = Encoding.UTF8.GetBytes("extend\0");
        for (int i = 0; i < key1Bytes.Length && i < MaxKeyLength; i++)
        {
            stringBase[MaxKeyLength + i] = key1Bytes[i];
        }

        fixed (byte* vertexEntryPtr = vertexEntryBytes)
        fixed (byte* fragmentEntryPtr = fragmentEntryBytes)
        {
            var vertexState = new VertexState
            {
                Module = shaderModule.Handle,
                EntryPoint = new StringView
                {
                    Data = (IntPtr)vertexEntryPtr,
                    Length = (UIntPtr)vertexEntryBytes.Length
                },
                ConstantCount = 2,
                Constants = (IntPtr)entries,
                BufferCount = 2,
                Buffers = (IntPtr)vertexBuffers
            };

            var colorTarget = new ColorTargetState
            {
                Format = TextureFormat.Bgra8UnormSrgb,
                WriteMask = (ulong)ColorWriteMask.All
            };

            var fragmentState = new FragmentState
            {
                Module = shaderModule.Handle,
                EntryPoint = new StringView
                {
                    Data = (IntPtr)fragmentEntryPtr,
                    Length = (UIntPtr)fragmentEntryBytes.Length
                },
                ConstantCount = 2,
                Constants = (IntPtr)entries,
                TargetCount = 1,
                Targets = (IntPtr)(&colorTarget)
            };

            var renderPipelineDesc = new RenderPipelineDescriptor
            {
                Layout = pipelineLayout.Handle,
                Vertex = vertexState,
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.Back
                },
                Multisample = new MultisampleState
                {
                    Count = 1
                },
                Fragment = (IntPtr)(&fragmentState)
            };

            var pipeline = _device.CreateRenderPipeline(renderPipelineDesc);
            pipelineLayout.Dispose();
            return pipeline;
        }
    }

    private BindGroupLayout CreatePerFrameLayout()
    {
        var entries = stackalloc BindGroupLayoutEntry[1];
        entries[0] = new BindGroupLayoutEntry
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

        var desc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)entries
        };

        return _device.CreateBindGroupLayout(desc);
    }

    private BindGroupLayout CreateLutLayout()
    {
        var entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                Multisampled = 0,
                ViewDimension = TextureViewDimension.D1
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = (ulong)ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout
            {
                Type = SamplerBindingType.Filtering
            }
        };

        var desc = new BindGroupLayoutDescriptor
        {
            EntryCount = 2,
            Entries = (IntPtr)entries
        };

        return _device.CreateBindGroupLayout(desc);
    }

    private BindGroupLayout CreatePerDrawLayout()
    {
        var entries = stackalloc BindGroupLayoutEntry[1];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = 0,
                MinBindingSize = (ulong)sizeof(PerDrawData)
            }
        };

        var desc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)entries
        };

        return _device.CreateBindGroupLayout(desc);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _perFrameBuffer.Dispose();
        _perDrawBuffer.Dispose();
        _lutLayout.Dispose();
        _perDrawLayout.Dispose();
    }
}
