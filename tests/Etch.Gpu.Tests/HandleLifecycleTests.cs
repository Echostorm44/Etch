namespace Etch.Gpu.Tests;

internal sealed class HandleLifecycleTests
{
    [Test]
    public async Task InstanceDisposeIdempotentDoesNotThrow()
    {
        var instance = new Instance(Gpu.Native.InstanceHandle.Invalid);
        instance.Dispose();
        instance.Dispose();
    }

    [Test]
    public async Task AdapterDisposeIdempotentDoesNotThrow()
    {
        var adapter = new Adapter(Gpu.Native.AdapterHandle.Invalid);
        adapter.Dispose();
        adapter.Dispose();
    }

    [Test]
    public async Task DeviceDisposeIdempotentDoesNotThrow()
    {
        var device = new Device(Gpu.Native.DeviceHandle.Invalid);
        device.Dispose();
        device.Dispose();
    }

    [Test]
    public async Task BufferDisposeIdempotentDoesNotThrow()
    {
        var buffer = new Buffer(Gpu.Native.BufferHandle.Invalid);
        buffer.Dispose();
        buffer.Dispose();
    }

    [Test]
    public async Task ShaderModuleDisposeIdempotentDoesNotThrow()
    {
        var module = new ShaderModule(Gpu.Native.ShaderModuleHandle.Invalid);
        module.Dispose();
        module.Dispose();
    }

    [Test]
    public async Task RenderPipelineDisposeIdempotentDoesNotThrow()
    {
        var pipeline = new RenderPipeline(Gpu.Native.RenderPipelineHandle.Invalid);
        pipeline.Dispose();
        pipeline.Dispose();
    }

    [Test]
    public async Task CommandEncoderDisposeIdempotentDoesNotThrow()
    {
        var encoder = new CommandEncoder(Gpu.Native.CommandEncoderHandle.Invalid);
        encoder.Dispose();
        encoder.Dispose();
    }

    [Test]
    public async Task CommandBufferDisposeIdempotentDoesNotThrow()
    {
        var cmdBuffer = new CommandBuffer(Gpu.Native.CommandBufferHandle.Invalid);
        cmdBuffer.Dispose();
        cmdBuffer.Dispose();
    }

    [Test]
    public async Task QueueDisposeIdempotentDoesNotThrow()
    {
        var queue = new Queue(Gpu.Native.QueueHandle.Invalid);
        queue.Dispose();
        queue.Dispose();
    }

    [Test]
    public async Task RenderPassDisposeIdempotentDoesNotThrow()
    {
        var pass = new RenderPass(Gpu.Native.RenderPassEncoderHandle.Invalid);
        pass.Dispose();
        pass.Dispose();
    }

    [Test]
    public async Task BindGroupDisposeIdempotentDoesNotThrow()
    {
        var bindGroup = new BindGroup(Gpu.Native.BindGroupHandle.Invalid);
        bindGroup.Dispose();
        bindGroup.Dispose();
    }

    [Test]
    public async Task BindGroupLayoutDisposeIdempotentDoesNotThrow()
    {
        var layout = new BindGroupLayout(Gpu.Native.BindGroupLayoutHandle.Invalid);
        layout.Dispose();
        layout.Dispose();
    }

    [Test]
    public async Task PipelineLayoutDisposeIdempotentDoesNotThrow()
    {
        var layout = new PipelineLayout(Gpu.Native.PipelineLayoutHandle.Invalid);
        layout.Dispose();
        layout.Dispose();
    }

    [Test]
    public async Task TextureDisposeIdempotentDoesNotThrow()
    {
        var texture = new Texture(Gpu.Native.TextureHandle.Invalid);
        texture.Dispose();
        texture.Dispose();
    }

    [Test]
    public async Task TextureViewDisposeIdempotentDoesNotThrow()
    {
        var view = new TextureView(Gpu.Native.TextureViewHandle.Invalid);
        view.Dispose();
        view.Dispose();
    }

    [Test]
    public async Task SamplerDisposeIdempotentDoesNotThrow()
    {
        var sampler = new Sampler(Gpu.Native.SamplerHandle.Invalid);
        sampler.Dispose();
        sampler.Dispose();
    }
}