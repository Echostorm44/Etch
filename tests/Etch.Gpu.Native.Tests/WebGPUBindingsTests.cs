namespace Etch.Gpu.Native.Tests;

internal sealed class WebGPUBindingsTests
{
    [Test]
    public async Task InstanceHandleEqualsSameHandleReturnsTrue()
    {
        var handle1 = new InstanceHandle(123);
        var handle2 = new InstanceHandle(123);
        await Assert.That(handle1 == handle2).IsTrue();
    }

    [Test]
    public async Task InstanceHandleEqualsDifferentHandleReturnsFalse()
    {
        var handle1 = new InstanceHandle(123);
        var handle2 = new InstanceHandle(456);
        await Assert.That(handle1 == handle2).IsFalse();
    }

    [Test]
    public async Task InstanceHandleInvalidReturnsInvalidState()
    {
        var handle = InstanceHandle.Invalid;
        await Assert.That(handle.IsInvalid).IsTrue();
    }

    [Test]
    public async Task InstanceHandleToStringFormatsCorrectly()
    {
        var handle = new InstanceHandle(0xABC);
        await Assert.That(handle.ToString()).IsEqualTo("InstanceHandle(0xABC)");
    }

    [Test]
    public async Task AdapterHandleInvalidIsInvalid()
    {
        await Assert.That(AdapterHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task DeviceHandleInvalidIsInvalid()
    {
        await Assert.That(DeviceHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task BufferHandleInvalidIsInvalid()
    {
        await Assert.That(BufferHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task ShaderModuleHandleInvalidIsInvalid()
    {
        await Assert.That(ShaderModuleHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task RenderPipelineHandleInvalidIsInvalid()
    {
        await Assert.That(RenderPipelineHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task CommandEncoderHandleInvalidIsInvalid()
    {
        await Assert.That(CommandEncoderHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task CommandBufferHandleInvalidIsInvalid()
    {
        await Assert.That(CommandBufferHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task QueueHandleInvalidIsInvalid()
    {
        await Assert.That(QueueHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task RenderPassEncoderHandleInvalidIsInvalid()
    {
        await Assert.That(RenderPassEncoderHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task BindGroupLayoutHandleInvalidIsInvalid()
    {
        await Assert.That(BindGroupLayoutHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task PipelineLayoutHandleInvalidIsInvalid()
    {
        await Assert.That(PipelineLayoutHandle.Invalid.IsInvalid).IsTrue();
    }

    [Test]
    public async Task BindGroupHandleInvalidIsInvalid()
    {
        await Assert.That(BindGroupHandle.Invalid.IsInvalid).IsTrue();
    }
}