using System;
using System.Runtime.InteropServices;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Pipelines;
using Etch.Shaders;

namespace Etch.Gpu.Compositor.Pipelines;

public enum ImageFilterMode : uint
{
    Bilinear = 0u,
    Bicubic = 1u,
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct ImageDrawCommand
{
    public TextureView Texture { get; }
    public Sampler Sampler { get; }
    public float M00 { get; }
    public float M01 { get; }
    public float M02 { get; }
    public float M10 { get; }
    public float M11 { get; }
    public float M12 { get; }
    public float M20 { get; }
    public float M21 { get; }
    public float M22 { get; }
    public ImageFilterMode FilterMode { get; }

    public ImageDrawCommand(
        TextureView texture,
        Sampler sampler,
        float m00, float m01, float m02,
        float m10, float m11, float m12,
        float m20, float m21, float m22,
        ImageFilterMode filterMode)
    {
        Texture = texture;
        Sampler = sampler;
        M00 = m00; M01 = m01; M02 = m02;
        M10 = m10; M11 = m11; M12 = m12;
        M20 = m20; M21 = m21; M22 = m22;
        FilterMode = filterMode;
    }
}

public sealed unsafe class ImageGpuPipeline : IDisposable
{
    private readonly Device _device;
    private readonly RenderPipeline _pipelineBilinear;
    private readonly RenderPipeline _pipelineBicubic;
    private readonly PipelineLayout _layout;
    private readonly Buffer _perFrameBuffer;
    private readonly BindGroupLayout _textureLayout;
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

    public ImageGpuPipeline(Device device)
    {
        _device = device;

        _perFrameBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = 64ul,
            Size = (ulong)sizeof(PerFrameData)
        });

        _textureLayout = CreateTextureLayout();

        var perFrameLayout = CreatePerFrameLayout();
        var layouts = stackalloc nint[2];
        layouts[0] = perFrameLayout.Handle;
        layouts[1] = _textureLayout.Handle;

        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 2,
            BindGroupLayouts = (IntPtr)layouts
        };

        _layout = device.CreatePipelineLayout(pipelineLayoutDesc);

        perFrameLayout.Dispose();

        _pipelineBilinear = CreatePipeline(ImageFilterMode.Bilinear);
        _pipelineBicubic = CreatePipeline(ImageFilterMode.Bicubic);
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

    private BindGroupLayout CreateTextureLayout()
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
                ViewDimension = TextureViewDimension.D2
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

    private RenderPipeline CreatePipeline(ImageFilterMode mode)
    {
        byte[] shaderBytes = ShaderResources.image_sample.ToArray();
        string wgsl = System.Text.Encoding.UTF8.GetString(shaderBytes);
        using var shaderModule = _device.CreateShaderModuleWgsl(wgsl, "ImageSample");

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
            AttributeCount = 1,
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

    public void Record(
        FrameContext frame,
        RenderPass pass,
        ImageDrawCommand cmd,
        ReadOnlySpan<float> quadVerticesXY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var pipeline = cmd.FilterMode == ImageFilterMode.Bicubic ? _pipelineBicubic : _pipelineBilinear;
        pass.SetPipeline(pipeline);

        var textureEntries = stackalloc BindGroupEntry[2];
        textureEntries[0] = new BindGroupEntry
        {
            Binding = 1,
            TextureView = cmd.Texture.Handle
        };
        textureEntries[1] = new BindGroupEntry
        {
            Binding = 2,
            Sampler = cmd.Sampler.Handle
        };

        var textureBindGroupDesc = new BindGroupDescriptor
        {
            Layout = _textureLayout.Handle,
            EntryCount = 2,
            Entries = (nint)textureEntries
        };
        var textureBindGroup = _device.CreateBindGroup(textureBindGroupDesc);
        pass.SetBindGroup(1, textureBindGroup);

        pass.Draw((uint)(quadVerticesXY.Length / 2));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _perFrameBuffer.Dispose();
        _pipelineBilinear.Dispose();
        _pipelineBicubic.Dispose();
        _layout.Dispose();
        _textureLayout.Dispose();
    }
}
