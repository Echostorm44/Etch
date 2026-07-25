using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Descriptors;
using Etch.Shaders;

namespace Etch.Gpu.Pipelines;

public enum GradientKind : uint
{
    Linear = 0u,
    Radial = 1u,
    Conic = 2u,
    Sweep = 3u,
}

public enum ExtendMode : uint
{
    Pad = 0u,
    Reflect = 1u,
    Repeat = 2u,
}

public sealed unsafe class GradientPipeline : IDisposable
{
    private readonly Device _device;
    private readonly RenderPipeline _pipeline;
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

    public GradientPipeline(Device device)
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

        var perDrawEntries = stackalloc BindGroupLayoutEntry[1];
        perDrawEntries[0] = new BindGroupLayoutEntry
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

        var textureEntries = stackalloc BindGroupLayoutEntry[2];
        textureEntries[0] = new BindGroupLayoutEntry
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

        var perDrawLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)perDrawEntries
        };

        var textureLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 2,
            Entries = (IntPtr)textureEntries
        };

        var perFrameLayout = device.CreateBindGroupLayout(perFrameLayoutDesc);
        var perDrawLayout = device.CreateBindGroupLayout(perDrawLayoutDesc);
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
        perDrawLayout.Dispose();
        textureLayout.Dispose();

        byte[] shaderBytes = ShaderResources.gradient.ToArray();
        string wgsl = System.Text.Encoding.UTF8.GetString(shaderBytes);
        using var shaderModule = device.CreateShaderModuleWgsl(wgsl, "Gradient");

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

        byte[] vertexEntryBytes = System.Text.Encoding.UTF8.GetBytes(ShaderResources.GradientLayout.VertexEntryPoint);
        byte[] fragmentEntryBytes = System.Text.Encoding.UTF8.GetBytes(ShaderResources.GradientLayout.FragmentEntryPoint);

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

            _pipeline = device.CreateRenderPipeline(renderPipelineDesc);
        }

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

    public void SetLinearGradient(float x0, float y0, float x1, float y1)
    {
        PerDrawData data;
        data.P0X = x0;
        data.P0Y = y0;
        data.P1X = x1;
        data.P1Y = y1;
        data.CenterX = 0;
        data.CenterY = 0;
        data.Radius = 1f;
        data.StartAngle = 0;
        data.EndAngle = 0;
        data.Color0R = 1f;
        data.Color0G = 0f;
        data.Color0B = 0f;
        data.Color0A = 1f;
        data.Color1R = 0f;
        data.Color1G = 0f;
        data.Color1B = 1f;
        data.Color1A = 1f;

        var span = new ReadOnlySpan<byte>(&data, sizeof(PerDrawData));
        _device.Queue.WriteBuffer(_perDrawBuffer, 0, span);
    }

    public void Record(CommandEncoder encoder, TextureView renderTarget, ReadOnlySpan<float> quadVerticesXY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = renderTarget.Handle,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 1 }
        };

        var pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = (IntPtr)(&colorAttachment)
        });

        pass.SetPipeline(_pipeline);
        pass.Draw((uint)(quadVerticesXY.Length / 2));
        pass.End();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _perFrameBuffer.Dispose();
            _perDrawBuffer.Dispose();
            _pipeline.Dispose();
            _layout.Dispose();
        }
    }
}