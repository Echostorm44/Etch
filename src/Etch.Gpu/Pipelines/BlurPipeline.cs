using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Descriptors;
using Etch.Shaders;

namespace Etch.Gpu.Pipelines;

public sealed unsafe class BlurPipeline : IDisposable
{
    private readonly Device _device;
    private readonly RenderPipeline _downPipeline;
    private readonly RenderPipeline _upPipeline;
    private readonly PipelineLayout _layout;
    private readonly Buffer _perFrameBuffer;
    private bool _disposed;

    public RenderPipeline DownPipeline => _downPipeline;
    public RenderPipeline UpPipeline => _upPipeline;

    [StructLayout(LayoutKind.Sequential)]
    private struct PerFrameData
    {
        public float SurfaceSizeX;
        public float SurfaceSizeY;
        public float Pad0X;
        public float Pad0Y;
    }

    public BlurPipeline(Device device)
    {
        _device = device;

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
            Binding = 1,
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
            Binding = 2,
            Visibility = (ulong)ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout
            {
                Type = SamplerBindingType.Filtering
            }
        };

        var perFrameLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)perFrameEntries
        };

        var textureLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 2,
            Entries = (IntPtr)textureEntries
        };

        var perFrameLayout = device.CreateBindGroupLayout(perFrameLayoutDesc);
        var textureLayout = device.CreateBindGroupLayout(textureLayoutDesc);

        var layouts = stackalloc nint[2];
        layouts[0] = perFrameLayout.Handle;
        layouts[1] = textureLayout.Handle;

        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 2,
            BindGroupLayouts = (IntPtr)layouts
        };

        _layout = device.CreatePipelineLayout(pipelineLayoutDesc);

        perFrameLayout.Dispose();
        textureLayout.Dispose();

        byte[] downShaderBytes = ShaderResources.down.ToArray();
        string downWgsl = System.Text.Encoding.UTF8.GetString(downShaderBytes);
        using var downShaderModule = device.CreateShaderModuleWgsl(downWgsl, "BlurDown");

        byte[] upShaderBytes = ShaderResources.up.ToArray();
        string upWgsl = System.Text.Encoding.UTF8.GetString(upShaderBytes);
        using var upShaderModule = device.CreateShaderModuleWgsl(upWgsl, "BlurUp");

        var vertexAttributes = stackalloc VertexAttribute[1];
        vertexAttributes[0] = new VertexAttribute
        {
            Format = VertexFormat.Float32x2,
            Offset = 0,
            ShaderLocation = 0
        };

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

        _downPipeline = CreatePipeline(device, downShaderModule, downVertexEntry, downFragmentEntry, vertexBuffers);
        _upPipeline = CreatePipeline(device, upShaderModule, upVertexEntry, upFragmentEntry, vertexBuffers);

        _perFrameBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = 64ul,
            Size = (ulong)sizeof(PerFrameData)
        });
    }

    private static RenderPipeline CreatePipeline(Device device, ShaderModule module, byte[] vertexEntry, byte[] fragmentEntry, VertexBufferLayout* vertexBuffers)
    {
        fixed (byte* vertexEntryPtr = vertexEntry)
        fixed (byte* fragmentEntryPtr = fragmentEntry)
        {
            var vertexState = new VertexState
            {
                Module = module.Handle,
                EntryPoint = new StringView
                {
                    Data = (IntPtr)vertexEntryPtr,
                    Length = (UIntPtr)vertexEntry.Length
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
                Module = module.Handle,
                EntryPoint = new StringView
                {
                    Data = (IntPtr)fragmentEntryPtr,
                    Length = (UIntPtr)fragmentEntry.Length
                },
                TargetCount = 1,
                Targets = (IntPtr)(&colorTarget)
            };

            var renderPipelineDesc = new RenderPipelineDescriptor
            {
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

            return device.CreateRenderPipeline(renderPipelineDesc);
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _perFrameBuffer.Dispose();
            _downPipeline.Dispose();
            _upPipeline.Dispose();
            _layout.Dispose();
        }
    }
}