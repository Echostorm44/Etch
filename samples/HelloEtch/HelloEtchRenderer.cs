using System;
using System.IO;
using System.Text;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace HelloEtch;

/// <summary>
/// Encapsulates the red-triangle render pipeline.  Can render to any
/// <see cref="TextureView"/> — swap-chain frame or off-screen target.
/// </summary>
internal sealed unsafe class HelloEtchRenderer : IDisposable
{
    private readonly Device _device;
    private readonly ShaderModule _shader;
    private readonly PipelineLayout _pipelineLayout;
    private readonly RenderPipeline _pipeline;

    public HelloEtchRenderer(Device device, TextureFormat colorFormat)
    {
        _device = device;

        string wgslPath = Path.Combine(AppContext.BaseDirectory, "RedTriangle.wgsl");
        string wgsl = File.ReadAllText(wgslPath);

        _shader = device.CreateShaderModuleWgsl(wgsl, "RedTriangle Shader");
        _pipelineLayout = CreateEmptyPipelineLayout(device);
        _pipeline = BuildRedTrianglePipeline(device, _pipelineLayout, _shader, colorFormat);
    }

    public void Render(TextureView view)
    {
        var colorAttachment = new RenderPassColorAttachment
        {
            NextInChain = IntPtr.Zero,
            View = (nint)view.Handle,
            DepthSlice = 0xFFFFFFFFu,
            ResolveTarget = IntPtr.Zero,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0.0, G = 0.1, B = 0.2, A = 1.0 },
        };

        var passDesc = new RenderPassDescriptor
        {
            NextInChain = IntPtr.Zero,
            Label = default,
            ColorAttachmentCount = (UIntPtr)1,
            ColorAttachments = (nint)(&colorAttachment),
            DepthStencilAttachment = IntPtr.Zero,
            OcclusionQuerySet = IntPtr.Zero,
            TimestampWrites = IntPtr.Zero,
        };

        using CommandEncoder encoder = _device.CreateCommandEncoder();
        using RenderPass pass = encoder.BeginRenderPass(passDesc);
        pass.SetPipeline(_pipeline);
        pass.Draw(3);
        pass.End();

        using CommandBuffer commands = encoder.Finish();
        Span<CommandBuffer> commandList = stackalloc CommandBuffer[1];
        commandList[0] = commands;
        _device.Queue.Submit(commandList);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _pipelineLayout.Dispose();
        _shader.Dispose();
    }

    private static PipelineLayout CreateEmptyPipelineLayout(Device device)
    {
        PipelineLayoutDescriptor desc = default;
        desc.NextInChain = IntPtr.Zero;
        desc.Label = default;
        desc.BindGroupLayoutCount = UIntPtr.Zero;
        desc.BindGroupLayouts = IntPtr.Zero;
        desc.ImmediateSize = 0;
        return device.CreatePipelineLayout(desc);
    }

    private static RenderPipeline BuildRedTrianglePipeline(
        Device device,
        PipelineLayout layout,
        ShaderModule shader,
        TextureFormat colorFormat)
    {
        Span<byte> vsEntryScratch = stackalloc byte[16];
        Span<byte> fsEntryScratch = stackalloc byte[16];
        Span<byte> labelScratch = stackalloc byte[Labels.MaxLabelLength + 1];

        int vsLen = Encoding.UTF8.GetBytes("vs", vsEntryScratch);
        int fsLen = Encoding.UTF8.GetBytes("fs", fsEntryScratch);
        int labelLen = Encoding.UTF8.GetBytes("RedTriangle Pipeline", labelScratch);

        fixed (byte* vsPtr = vsEntryScratch)
        fixed (byte* fsPtr = fsEntryScratch)
        fixed (byte* labelPtr = labelScratch)
        {
            var primitive = new PrimitiveState
            {
                NextInChain = IntPtr.Zero,
                Topology = PrimitiveTopology.TriangleList,
                StripIndexFormat = IndexFormat.Undefined,
                FrontFace = FrontFace.Ccw,
                CullMode = CullMode.None,
                UnclippedDepth = 0,
            };

            var multisample = new MultisampleState
            {
                NextInChain = IntPtr.Zero,
                Count = 1,
                Mask = ~0u,
                AlphaToCoverageEnabled = 0,
            };

            var blend = new BlendState
            {
                Color = new BlendComponent
                {
                    Operation = BlendOperation.Add,
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.Zero,
                },
                Alpha = new BlendComponent
                {
                    Operation = BlendOperation.Add,
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.Zero,
                },
            };

            var colorTarget = new ColorTargetState
            {
                NextInChain = IntPtr.Zero,
                Format = colorFormat,
                Blend = (nint)(&blend),
                WriteMask = (ulong)ColorWriteMask.All,
            };

            var vertex = new VertexState
            {
                NextInChain = IntPtr.Zero,
                Module = shader.Handle,
                EntryPoint = new StringView { Data = (nint)vsPtr, Length = (UIntPtr)vsLen },
                ConstantCount = UIntPtr.Zero,
                Constants = IntPtr.Zero,
                BufferCount = UIntPtr.Zero,
                Buffers = IntPtr.Zero,
            };

            var fragment = new FragmentState
            {
                NextInChain = IntPtr.Zero,
                Module = shader.Handle,
                EntryPoint = new StringView { Data = (nint)fsPtr, Length = (UIntPtr)fsLen },
                ConstantCount = UIntPtr.Zero,
                Constants = IntPtr.Zero,
                TargetCount = (UIntPtr)1,
                Targets = (nint)(&colorTarget),
            };

            var desc = new RenderPipelineDescriptor
            {
                NextInChain = IntPtr.Zero,
                Label = new StringView { Data = (nint)labelPtr, Length = (UIntPtr)labelLen },
                Layout = layout.Handle,
                Vertex = vertex,
                Primitive = primitive,
                DepthStencil = IntPtr.Zero,
                Multisample = multisample,
                Fragment = (nint)(&fragment),
            };

            return device.CreateRenderPipeline(desc);
        }
    }
}
