using System;
using System.Runtime.InteropServices;
using Etch.ClipBlendGradient;
using Etch.Gpu.Descriptors;
using Etch.Shaders;

namespace Etch.Gpu.Pipelines;

public sealed unsafe class BlendPipeline : IDisposable
{
    private readonly Device _device;
    private readonly RenderPipeline _separablePipeline;
    private readonly RenderPipeline _nonseparablePipeline;
    private readonly PipelineLayout _separableLayout;
    private readonly PipelineLayout _nonseparableLayout;
    private readonly Buffer _perDrawBuffer;
    private bool _disposed;

    [StructLayout(LayoutKind.Explicit)]
    private struct PerDrawData
    {
        [FieldOffset(0)]
        public uint BlendMode;
        [FieldOffset(16)]
        public float Color0R;
        [FieldOffset(20)]
        public float Color0G;
        [FieldOffset(24)]
        public float Color0B;
        [FieldOffset(28)]
        public float Color0A;
        [FieldOffset(32)]
        public float Color1R;
        [FieldOffset(36)]
        public float Color1G;
        [FieldOffset(40)]
        public float Color1B;
        [FieldOffset(44)]
        public float Color1A;
    }

    public BlendPipeline(Device device)
    {
        _device = device;

        var perDrawEntries = stackalloc BindGroupLayoutEntry[1];
        perDrawEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = 0,
                MinBindingSize = 48u
            }
        };

        var perDrawLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = (IntPtr)perDrawEntries
        };

        var perDrawLayout = device.CreateBindGroupLayout(perDrawLayoutDesc);

        var layouts = stackalloc nint[1];
        layouts[0] = perDrawLayout.Handle;

        var separablePipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = (IntPtr)layouts
        };

        _separableLayout = device.CreatePipelineLayout(separablePipelineLayoutDesc);

        var nonseparablePipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = (IntPtr)layouts
        };

        _nonseparableLayout = device.CreatePipelineLayout(nonseparablePipelineLayoutDesc);

        perDrawLayout.Dispose();

        byte[] separableShaderBytes = ShaderResources.separable.ToArray();
        string separableWgsl = System.Text.Encoding.UTF8.GetString(separableShaderBytes);
        using var separableModule = device.CreateShaderModuleWgsl(separableWgsl, "SeparableBlend");

        byte[] nonseparableShaderBytes = ShaderResources.nonseparable.ToArray();
        string nonseparableWgsl = System.Text.Encoding.UTF8.GetString(nonseparableShaderBytes);
        using var nonseparableModule = device.CreateShaderModuleWgsl(nonseparableWgsl, "NonseparableBlend");

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

        byte[] vertexEntryBytes = System.Text.Encoding.UTF8.GetBytes("vs_main");
        byte[] separableFragmentEntryBytes = System.Text.Encoding.UTF8.GetBytes("fs_main");
        byte[] nonseparableFragmentEntryBytes = System.Text.Encoding.UTF8.GetBytes("fs_main");

        fixed (byte* vertexEntryPtr = vertexEntryBytes)
        fixed (byte* separableFragmentEntryPtr = separableFragmentEntryBytes)
        fixed (byte* nonseparableFragmentEntryPtr = nonseparableFragmentEntryBytes)
        {
            var vertexState = new VertexState
            {
                Module = separableModule.Handle,
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

            var separableFragmentState = new FragmentState
            {
                Module = separableModule.Handle,
                EntryPoint = new StringView
                {
                    Data = (IntPtr)separableFragmentEntryPtr,
                    Length = (UIntPtr)separableFragmentEntryBytes.Length
                },
                TargetCount = 1,
                Targets = (IntPtr)(&colorTarget)
            };

            var separableRenderPipelineDesc = new RenderPipelineDescriptor
            {
                Layout = _separableLayout.Handle,
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
                Fragment = (IntPtr)(&separableFragmentState)
            };

            _separablePipeline = device.CreateRenderPipeline(separableRenderPipelineDesc);

            var nonseparableFragmentState = new FragmentState
            {
                Module = nonseparableModule.Handle,
                EntryPoint = new StringView
                {
                    Data = (IntPtr)nonseparableFragmentEntryPtr,
                    Length = (UIntPtr)nonseparableFragmentEntryBytes.Length
                },
                TargetCount = 1,
                Targets = (IntPtr)(&colorTarget)
            };

            var nonseparableRenderPipelineDesc = new RenderPipelineDescriptor
            {
                Layout = _nonseparableLayout.Handle,
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
                Fragment = (IntPtr)(&nonseparableFragmentState)
            };

            _nonseparablePipeline = device.CreateRenderPipeline(nonseparableRenderPipelineDesc);
        }

        _perDrawBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = 64ul,
            Size = (ulong)sizeof(PerDrawData)
        });
    }

    public void SetBlendColors(BlendMode mode, LinearColor src, LinearColor dst)
    {
        PerDrawData data = default;
        data.BlendMode = (byte)mode;
        data.Color0R = (float)src.R;
        data.Color0G = (float)src.G;
        data.Color0B = (float)src.B;
        data.Color0A = (float)src.A;
        data.Color1R = (float)dst.R;
        data.Color1G = (float)dst.G;
        data.Color1B = (float)dst.B;
        data.Color1A = (float)dst.A;

        var span = new ReadOnlySpan<byte>(&data, sizeof(PerDrawData));
        _device.Queue.WriteBuffer(_perDrawBuffer, 0, span);
    }

    public RenderPipeline GetPipeline(BlendMode mode)
    {
        return (byte)mode >= 12 ? _nonseparablePipeline : _separablePipeline;
    }

    public PipelineLayout GetLayout(BlendMode mode)
    {
        return (byte)mode >= 12 ? _nonseparableLayout : _separableLayout;
    }

    public Buffer PerDrawBuffer => _perDrawBuffer;

    public void RecordBlend(
        CommandEncoder encoder,
        TextureView renderTarget,
        ReadOnlySpan<float> quadVerticesXY,
        BlendMode mode,
        LinearColor src,
        LinearColor dst)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        SetBlendColors(mode, src, dst);

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

        pass.SetPipeline(GetPipeline(mode));
        pass.Draw((uint)(quadVerticesXY.Length / 2));
        pass.End();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _perDrawBuffer.Dispose();
            _separablePipeline.Dispose();
            _nonseparablePipeline.Dispose();
            _separableLayout.Dispose();
            _nonseparableLayout.Dispose();
        }
    }
}
