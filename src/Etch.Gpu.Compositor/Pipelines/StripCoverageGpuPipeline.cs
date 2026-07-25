using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Compositor;
using Etch.Gpu.Descriptors;
using Etch.Shaders;

namespace Etch.Gpu.Compositor.Pipelines;

public sealed unsafe class StripCoverageGpuPipeline : IDisposable
{
    private readonly Device _device;
    private readonly RenderPipeline _pipeline;
    private readonly PipelineLayout _layout;
    private readonly BindGroupLayout _perFrameLayout;
    private readonly BindGroupLayout _stripBufferLayout;
    private readonly BindGroupLayout _paintBufferLayout;
    private readonly Buffer _dummyGradientBuffer;
    private bool _disposed;

    public StripCoverageGpuPipeline(Device device)
    {
        _device = device;

        _perFrameLayout = CreatePerFrameLayout();
        _stripBufferLayout = CreateStripBufferLayout();
        _paintBufferLayout = CreatePaintBufferLayout();

        _layout = CreatePipelineLayout(_perFrameLayout, _stripBufferLayout, _paintBufferLayout);

        _dummyGradientBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            Size = 4
        });

        _pipeline = CreatePipeline();
    }

    public void Record(
        RenderPass pass,
        Buffer unitQuadVertex,
        StripGpuBuffers strips,
        Buffer paintBuffer,
        Buffer gradientBuffer,
        Buffer perFrameUniform,
        uint instanceCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        pass.SetPipeline(_pipeline);
        pass.SetVertexBuffer(0, unitQuadVertex, 0, 32);

        // Bind group 0: per-frame uniform
        var frameEntry = stackalloc BindGroupEntry[1];
        frameEntry[0] = new BindGroupEntry
        {
            Binding = 0,
            Buffer = perFrameUniform.Handle,
            Offset = 0,
            Size = ulong.MaxValue
        };
        var frameBgDesc = new BindGroupDescriptor
        {
            Layout = _perFrameLayout.Handle,
            EntryCount = 1,
            Entries = (nint)frameEntry
        };
        var frameBg = _device.CreateBindGroup(frameBgDesc);
        pass.SetBindGroup(0, frameBg);

        // Bind group 1: strip + coverage buffers
        var stripEntries = stackalloc BindGroupEntry[2];
        stripEntries[0] = new BindGroupEntry
        {
            Binding = 0,
            Buffer = strips.Strips.Handle,
            Offset = 0,
            Size = ulong.MaxValue
        };
        stripEntries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = strips.Coverage.Handle,
            Offset = 0,
            Size = ulong.MaxValue
        };
        var stripBgDesc = new BindGroupDescriptor
        {
            Layout = _stripBufferLayout.Handle,
            EntryCount = 2,
            Entries = (nint)stripEntries
        };
        var stripBg = _device.CreateBindGroup(stripBgDesc);
        pass.SetBindGroup(1, stripBg);

        // Bind group 2: paint buffer + gradient stops
        var paintEntries = stackalloc BindGroupEntry[2];
        paintEntries[0] = new BindGroupEntry
        {
            Binding = 0,
            Buffer = paintBuffer.Handle,
            Offset = 0,
            Size = ulong.MaxValue
        };
        paintEntries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = gradientBuffer.IsInvalid ? _dummyGradientBuffer.Handle : gradientBuffer.Handle,
            Offset = 0,
            Size = ulong.MaxValue
        };
        var paintBgDesc = new BindGroupDescriptor
        {
            Layout = _paintBufferLayout.Handle,
            EntryCount = 2,
            Entries = (nint)paintEntries
        };
        var paintBg = _device.CreateBindGroup(paintBgDesc);
        pass.SetBindGroup(2, paintBg);

        pass.Draw(4, instanceCount);
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
                MinBindingSize = 24u
            }
        };

        var desc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)entries
        };

        return _device.CreateBindGroupLayout(desc);
    }

    private BindGroupLayout CreateStripBufferLayout()
    {
        var entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)ShaderStage.Vertex | (ulong)ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = 0,
                MinBindingSize = 0
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = 0,
                MinBindingSize = 0
            }
        };

        var desc = new BindGroupLayoutDescriptor
        {
            EntryCount = 2,
            Entries = (IntPtr)entries
        };

        return _device.CreateBindGroupLayout(desc);
    }

    private BindGroupLayout CreatePaintBufferLayout()
    {
        var entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = 0,
                MinBindingSize = 0
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = (ulong)ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = 0,
                MinBindingSize = 0
            }
        };

        var desc = new BindGroupLayoutDescriptor
        {
            EntryCount = 2,
            Entries = (IntPtr)entries
        };

        return _device.CreateBindGroupLayout(desc);
    }

    private PipelineLayout CreatePipelineLayout(BindGroupLayout perFrameLayout, BindGroupLayout stripBufferLayout, BindGroupLayout paintBufferLayout)
    {
        var layouts = stackalloc nint[3];
        layouts[0] = perFrameLayout.Handle;
        layouts[1] = stripBufferLayout.Handle;
        layouts[2] = paintBufferLayout.Handle;

        var desc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 3,
            BindGroupLayouts = (IntPtr)layouts
        };

        return _device.CreatePipelineLayout(desc);
    }

    private RenderPipeline CreatePipeline()
    {
        byte[] shaderBytes = ShaderResources.strip_coverage.ToArray();
        string wgsl = System.Text.Encoding.UTF8.GetString(shaderBytes);
        using var shaderModule = _device.CreateShaderModuleWgsl(wgsl, "StripCoverage");

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

        byte[] vertexEntryBytes = System.Text.Encoding.UTF8.GetBytes(ShaderResources.Strip_coverageLayout.VertexEntryPoint);
        byte[] fragmentEntryBytes = System.Text.Encoding.UTF8.GetBytes(ShaderResources.Strip_coverageLayout.FragmentEntryPoint);

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

            var blendState = new BlendState
            {
                Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
                Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
            };
            var colorTarget = new ColorTargetState
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Blend = (nint)(&blendState),
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
                    Topology = PrimitiveTopology.TriangleStrip,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None
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
        _perFrameLayout.Dispose();
        _stripBufferLayout.Dispose();
        _paintBufferLayout.Dispose();
        _dummyGradientBuffer.Dispose();
    }
}
