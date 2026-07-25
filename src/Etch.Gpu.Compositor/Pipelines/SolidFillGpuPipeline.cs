using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Compositor;
using Etch.Gpu.Descriptors;
using Etch.Shaders;

namespace Etch.Gpu.Compositor.Pipelines;

public sealed unsafe class SolidFillGpuPipeline : IDisposable
{
    private readonly Device _device;
    private readonly RenderPipeline _pipeline;
    private readonly PipelineLayout _layout;
    private bool _disposed;

    public SolidFillGpuPipeline(Device device)
    {
        _device = device;

        var perFrameLayout = CreatePerFrameLayout();
        var perDrawLayout = CreatePerDrawLayout();

        _layout = CreatePipelineLayout(perFrameLayout, perDrawLayout);

        perFrameLayout.Dispose();
        perDrawLayout.Dispose();

        _pipeline = CreatePipeline();
    }

    public void Record(
        FrameContext frame,
        RenderPass pass,
        TileQuadBuffers buffers,
        float surfaceWidth,
        float surfaceHeight,
        ReadOnlySpan<float> transforms,
        ReadOnlySpan<byte> colors)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = (IntPtr)frame.Texture.View,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 1 }
        };

        pass.SetPipeline(_pipeline);
        pass.SetVertexBuffer(0, buffers.UnitQuadVertex, 0, 32);
        pass.SetVertexBuffer(1, buffers.PerTileInstance, 0, (ulong)(buffers.PerTileInstance.IsInvalid ? 0 : 256 * 32));
        pass.Draw(4, (uint)(buffers.PerTileInstance.IsInvalid ? 0 : 256));
        pass.End();
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
                MinBindingSize = 16u
            }
        };

        var desc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
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
            Visibility = (ulong)(ShaderStage.Vertex | ShaderStage.Fragment),
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = 0,
                MinBindingSize = 64u
            }
        };

        var desc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)entries
        };

        return _device.CreateBindGroupLayout(desc);
    }

    private PipelineLayout CreatePipelineLayout(BindGroupLayout perFrameLayout, BindGroupLayout perDrawLayout)
    {
        var layouts = stackalloc nint[3];
        layouts[0] = perFrameLayout.Handle;
        layouts[1] = IntPtr.Zero;
        layouts[2] = perDrawLayout.Handle;

        var desc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 3,
            BindGroupLayouts = (IntPtr)layouts
        };

        return _device.CreatePipelineLayout(desc);
    }

    private RenderPipeline CreatePipeline()
    {
        byte[] shaderBytes = ShaderResources.solid_fill.ToArray();
        string wgsl = System.Text.Encoding.UTF8.GetString(shaderBytes);
        using var shaderModule = _device.CreateShaderModuleWgsl(wgsl, "SolidFill");

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

        byte[] vertexEntryBytes = System.Text.Encoding.UTF8.GetBytes(ShaderResources.Solid_fillLayout.VertexEntryPoint);
        byte[] fragmentEntryBytes = System.Text.Encoding.UTF8.GetBytes(ShaderResources.Solid_fillLayout.FragmentEntryPoint);

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _pipeline.Dispose();
        _layout.Dispose();
    }
}
