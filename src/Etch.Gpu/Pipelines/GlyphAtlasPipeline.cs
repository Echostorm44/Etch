using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Descriptors;
using Etch.Shaders;

namespace Etch.Gpu.Pipelines;

public enum SubpixelMode : uint
{
    None = 0u,
    ThreeChannel = 1u,
    FiveChannel = 2u,
}

public sealed unsafe class GlyphAtlasPipeline : IDisposable
{
    private readonly Device _device;
    private readonly RenderPipeline _pipelineNone;
    private readonly RenderPipeline _pipeline3;
    private readonly RenderPipeline _pipeline5;
    private readonly PipelineLayout _layout;
    private readonly Buffer _perFrameBuffer;
    private readonly Buffer _perDrawBuffer;
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
        public float TransformM00;
        public float TransformM01;
        public float TransformM02;
        public float TransformM10;
        public float TransformM11;
        public float TransformM12;
        public float TransformM20;
        public float TransformM21;
        public float TransformM22;
        public float ColorR;
        public float ColorG;
        public float ColorB;
        public float ColorA;
    }

    public GlyphAtlasPipeline(Device device)
    {
        _device = device;

        _perFrameBuffer = CreatePerFrameBuffer();
        _perDrawBuffer = CreatePerDrawBuffer();
        _layout = CreatePipelineLayout();

        _pipelineNone = CreatePipeline(SubpixelMode.None);
        _pipeline3 = CreatePipeline(SubpixelMode.ThreeChannel);
        _pipeline5 = CreatePipeline(SubpixelMode.FiveChannel);
    }

    public RenderPipeline GetPipeline(SubpixelMode mode)
    {
        return mode switch
        {
            SubpixelMode.ThreeChannel => _pipeline3,
            SubpixelMode.FiveChannel => _pipeline5,
            _ => _pipelineNone
        };
    }

    private Buffer CreatePerFrameBuffer()
    {
        var descriptor = new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapWrite | BufferUsage.CopySrc),
            Size = (ulong)sizeof(PerFrameData)
        };
        return _device.CreateBuffer(descriptor);
    }

    private Buffer CreatePerDrawBuffer()
    {
        var descriptor = new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapWrite | BufferUsage.CopySrc),
            Size = (ulong)sizeof(PerDrawData)
        };
        return _device.CreateBuffer(descriptor);
    }

    private PipelineLayout CreatePipelineLayout()
    {
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

        var perFrameLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)perFrameEntries
        };
        using var perFrameLayout = _device.CreateBindGroupLayout(perFrameLayoutDesc);

        var textureEntries = stackalloc BindGroupLayoutEntry[2];
        textureEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.UnfilterableFloat,
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
                Type = SamplerBindingType.NonFiltering
            }
        };

        var textureLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 2,
            Entries = (IntPtr)textureEntries
        };
        using var textureLayout = _device.CreateBindGroupLayout(textureLayoutDesc);

        var perDrawEntries = stackalloc BindGroupLayoutEntry[1];
        perDrawEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = 0,
                MinBindingSize = (ulong)sizeof(PerDrawData)
            }
        };

        var perDrawLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)perDrawEntries
        };
        using var perDrawLayout = _device.CreateBindGroupLayout(perDrawLayoutDesc);

        var layouts = stackalloc nint[3];
        layouts[0] = perFrameLayout.Handle;
        layouts[1] = textureLayout.Handle;
        layouts[2] = perDrawLayout.Handle;

        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 3,
            BindGroupLayouts = (IntPtr)layouts
        };

        return _device.CreatePipelineLayout(pipelineLayoutDesc);
    }

    private RenderPipeline CreatePipeline(SubpixelMode mode)
    {
        byte[] shaderBytes = ShaderResources.glyph_atlas.ToArray();
        string wgsl = System.Text.Encoding.UTF8.GetString(shaderBytes);
        using var shaderModule = _device.CreateShaderModuleWgsl(wgsl, "GlyphAtlas");

        byte[] vertexEntryBytes = System.Text.Encoding.UTF8.GetBytes("vs_main");
        byte[] fragmentEntryBytes = System.Text.Encoding.UTF8.GetBytes("fs_main");

        fixed (byte* vertexEntryPtr = vertexEntryBytes)
        fixed (byte* fragmentEntryPtr = fragmentEntryBytes)
        {
            var vertexAttributes = stackalloc VertexAttribute[3];
            vertexAttributes[0] = new VertexAttribute
            {
                Format = VertexFormat.Float32x2,
                Offset = 0,
                ShaderLocation = 0
            };
            vertexAttributes[1] = new VertexAttribute
            {
                Format = VertexFormat.Float32x2,
                Offset = 8,
                ShaderLocation = 1
            };
            vertexAttributes[2] = new VertexAttribute
            {
                Format = VertexFormat.Float32,
                Offset = 16,
                ShaderLocation = 2
            };

            var vertexBuffers = stackalloc VertexBufferLayout[1];
            vertexBuffers[0] = new VertexBufferLayout
            {
                StepMode = VertexStepMode.Vertex,
                ArrayStride = 24,
                AttributeCount = 3,
                Attributes = (IntPtr)vertexAttributes
            };

            var vertexState = new VertexState
            {
                Module = shaderModule.Handle,
                EntryPoint = new StringView
                {
                    Data = (IntPtr)vertexEntryPtr,
                    Length = (UIntPtr)vertexEntryBytes.Length
                },
                BufferCount = 1,
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
                TargetCount = 1,
                Targets = (IntPtr)(&colorTarget)
            };

            var renderPipelineDesc = new RenderPipelineDescriptor
            {
                Layout = _layout.Handle,
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

            return _device.CreateRenderPipeline(renderPipelineDesc);
        }
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

    public void SetPerDraw(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22, float r, float g, float b, float a)
    {
        PerDrawData data;
        data.TransformM00 = m00;
        data.TransformM01 = m01;
        data.TransformM02 = m02;
        data.TransformM10 = m10;
        data.TransformM11 = m11;
        data.TransformM12 = m12;
        data.TransformM20 = m20;
        data.TransformM21 = m21;
        data.TransformM22 = m22;
        data.ColorR = r;
        data.ColorG = g;
        data.ColorB = b;
        data.ColorA = a;

        var span = new ReadOnlySpan<byte>(&data, sizeof(PerDrawData));
        _device.Queue.WriteBuffer(_perDrawBuffer, 0, span);
    }

    public Buffer PerFrameBuffer => _perFrameBuffer;
    public Buffer PerDrawBuffer => _perDrawBuffer;
    public PipelineLayout Layout => _layout;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _pipelineNone.Dispose();
        _pipeline3.Dispose();
        _pipeline5.Dispose();
        _layout.Dispose();
        _perFrameBuffer.Dispose();
        _perDrawBuffer.Dispose();
    }
}