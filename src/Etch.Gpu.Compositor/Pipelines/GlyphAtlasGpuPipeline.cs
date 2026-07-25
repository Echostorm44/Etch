using System;
using System.Runtime.InteropServices;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Pipelines;

namespace Etch.Gpu.Compositor.Pipelines;

[StructLayout(LayoutKind.Sequential)]
public struct GlyphInstance
{
    public float AtlasUvX;
    public float AtlasUvY;
    public float QuadWidth;
    public float QuadHeight;
    public float SubpixelOffset;
}

public sealed unsafe class GlyphAtlasGpuPipeline : IDisposable
{
    private readonly Device _device;
    private readonly GlyphAtlasPipeline _pipeline;
    private readonly PipelineLayout _layout;
    private readonly Buffer _perFrameBuffer;
    private readonly BindGroupLayout _atlasLayout;
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

    public GlyphAtlasGpuPipeline(Device device)
    {
        _device = device;
        _pipeline = new GlyphAtlasPipeline(device);

        _perFrameBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = 64ul,
            Size = (ulong)sizeof(PerFrameData)
        });

        _atlasLayout = CreateAtlasLayout();
        _perDrawLayout = CreatePerDrawLayout();

        var perFrameLayout = CreatePerFrameLayout();

        var layouts = stackalloc nint[3];
        layouts[0] = perFrameLayout.Handle;
        layouts[1] = _atlasLayout.Handle;
        layouts[2] = _perDrawLayout.Handle;

        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 3,
            BindGroupLayouts = (IntPtr)layouts
        };

        _layout = device.CreatePipelineLayout(pipelineLayoutDesc);
        perFrameLayout.Dispose();
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

        return _device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)entries
        });
    }

    private BindGroupLayout CreateAtlasLayout()
    {
        var entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
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
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout
            {
                Type = SamplerBindingType.NonFiltering
            }
        };

        return _device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            EntryCount = 2,
            Entries = (IntPtr)entries
        });
    }

    private BindGroupLayout CreatePerDrawLayout()
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
                MinBindingSize = (ulong)sizeof(GlyphInstance)
            }
        };

        return _device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)entries
        });
    }

    public void SetSurfaceSize(float width, float height)
    {
        _pipeline.SetSurfaceSize(width, height);
    }

    public void Record(
        FrameContext frame,
        RenderPass pass,
        TextureView atlasView,
        Sampler sampler,
        SubpixelMode subpixelMode,
        ReadOnlySpan<GlyphInstance> instances)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (instances.Length == 0)
        {
            return;
        }

        var pipeline = _pipeline.GetPipeline(subpixelMode);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = (IntPtr)atlasView.Handle,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 1 }
        };

        pass.SetPipeline(pipeline);

        pass.Draw(4, (uint)instances.Length);
        pass.End();
    }

    public PipelineLayout Layout => _layout;
    public GlyphAtlasPipeline Pipeline => _pipeline;
    public BindGroupLayout AtlasLayout => _atlasLayout;
    public BindGroupLayout PerDrawLayout => _perDrawLayout;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _pipeline.Dispose();
        _layout.Dispose();
        _perFrameBuffer.Dispose();
        _atlasLayout.Dispose();
        _perDrawLayout.Dispose();
    }
}