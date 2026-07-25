using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Descriptors;
using Etch.Shaders;

namespace Etch.Gpu.Pipelines;

public enum SampleMode : uint
{
    Bilinear = 0u,
    Bicubic = 1u,
}

public sealed unsafe class ImagePipeline : IDisposable
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

    public ImagePipeline(Device device)
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
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
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

        byte[] shaderBytes = ShaderResources.image_sample.ToArray();
        string wgsl = System.Text.Encoding.UTF8.GetString(shaderBytes);
        using var shaderModule = device.CreateShaderModuleWgsl(wgsl, "ImageSample");

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

        byte[] vertexEntryBytes = System.Text.Encoding.UTF8.GetBytes(ShaderResources.Image_sampleLayout.VertexEntryPoint);
        byte[] fragmentEntryBytes = System.Text.Encoding.UTF8.GetBytes(ShaderResources.Image_sampleLayout.FragmentEntryPoint);

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

    public void SetTransform(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
    {
        PerDrawData data;
        data.TransformM00 = m00; data.TransformM01 = m01; data.TransformM02 = m02;
        data.TransformM10 = m10; data.TransformM11 = m11; data.TransformM12 = m12;
        data.TransformM20 = m20; data.TransformM21 = m21; data.TransformM22 = m22;
        data.ColorR = 1f; data.ColorG = 1f; data.ColorB = 1f; data.ColorA = 1f;

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