using System;
using Etch.Gpu.Compositor.Clip;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

internal sealed class ClipCompositorTests
{
    [Test]
    public async Task ClipMaskBuffers_MaxClipLevels_Is16()
    {
        await Assert.That(ClipMaskBuffers.MaxClipLevels).IsEqualTo(16);
    }

    [Test]
    public async Task ClipMaskBuffers_AtlasSize_Is2048()
    {
        await Assert.That(ClipMaskBuffers.AtlasSize).IsEqualTo(2048);
    }

    [Test]
    public async Task ClipMaskBuffers_SlotSize_Is512()
    {
        await Assert.That(ClipMaskBuffers.SlotSize).IsEqualTo(512);
    }

    [Test]
    public async Task ClipMaskBuffers_SlotsPerRow_Is4()
    {
        await Assert.That(ClipMaskBuffers.SlotsPerRow).IsEqualTo(4);
    }

    [Test]
    public async Task ClipMaskBuffers_CanFitSlot_ValidSlots()
    {
        await Assert.That(ClipMaskBuffers.CanFitSlot(0)).IsTrue();
        await Assert.That(ClipMaskBuffers.CanFitSlot(15)).IsTrue();
        await Assert.That(ClipMaskBuffers.CanFitSlot(3)).IsTrue();
        await Assert.That(ClipMaskBuffers.CanFitSlot(12)).IsTrue();
    }

    [Test]
    public async Task ClipMaskBuffers_CanFitSlot_InvalidSlots()
    {
        await Assert.That(ClipMaskBuffers.CanFitSlot(16)).IsFalse();
        await Assert.That(ClipMaskBuffers.CanFitSlot(17)).IsFalse();
        await Assert.That(ClipMaskBuffers.CanFitSlot(255)).IsFalse();
    }

    [Test]
    public async Task ClipCompositor_PushPop_SingleLevel()
    {
        var buffers = new ClipMaskBuffers(
            new Texture(Gpu.Native.TextureHandle.Invalid),
            new Buffer(Gpu.Native.BufferHandle.Invalid),
            ClipMaskBuffers.AtlasSize,
            ClipMaskBuffers.AtlasSize);

        var compositor = new ClipCompositor(buffers);

        await Assert.That(compositor.CurrentDepth).IsEqualTo(0);

        int slot = compositor.PushClip();
        await Assert.That(slot).IsEqualTo(0);
        await Assert.That(compositor.CurrentDepth).IsEqualTo(1);
        await Assert.That(compositor.CurrentClipIndex).IsEqualTo(0);

        compositor.PopClip();
        await Assert.That(compositor.CurrentDepth).IsEqualTo(0);

        compositor.Dispose();
    }

    [Test]
    public async Task ClipCompositor_PushPop_MultipleLevels()
    {
        var buffers = new ClipMaskBuffers(
            new Texture(Gpu.Native.TextureHandle.Invalid),
            new Buffer(Gpu.Native.BufferHandle.Invalid),
            ClipMaskBuffers.AtlasSize,
            ClipMaskBuffers.AtlasSize);

        var compositor = new ClipCompositor(buffers);

        int slot0 = compositor.PushClip();
        int slot1 = compositor.PushClip();
        int slot2 = compositor.PushClip();

        await Assert.That(slot0).IsEqualTo(0);
        await Assert.That(slot1).IsEqualTo(1);
        await Assert.That(slot2).IsEqualTo(2);
        await Assert.That(compositor.CurrentDepth).IsEqualTo(3);
        await Assert.That(compositor.CurrentClipIndex).IsEqualTo(2);

        compositor.PopClip();
        await Assert.That(compositor.CurrentDepth).IsEqualTo(2);
        await Assert.That(compositor.CurrentClipIndex).IsEqualTo(1);

        compositor.PopClip();
        await Assert.That(compositor.CurrentDepth).IsEqualTo(1);
        await Assert.That(compositor.CurrentClipIndex).IsEqualTo(0);

        compositor.PopClip();
        await Assert.That(compositor.CurrentDepth).IsEqualTo(0);

        compositor.Dispose();
    }

    [Test]
    public async Task ClipCompositor_FreeListReuse()
    {
        var buffers = new ClipMaskBuffers(
            new Texture(Gpu.Native.TextureHandle.Invalid),
            new Buffer(Gpu.Native.BufferHandle.Invalid),
            ClipMaskBuffers.AtlasSize,
            ClipMaskBuffers.AtlasSize);

        var compositor = new ClipCompositor(buffers);

        int slot0 = compositor.PushClip();
        int slot1 = compositor.PushClip();
        int slot2 = compositor.PushClip();

        compositor.PopClip();
        compositor.PopClip();

        int slot3 = compositor.PushClip();
        int slot4 = compositor.PushClip();

        await Assert.That(slot3).IsEqualTo(2);
        await Assert.That(slot4).IsEqualTo(1);

        compositor.Dispose();
    }

    [Test]
    public async Task ClipCompositor_Reset_ClearsStack()
    {
        var buffers = new ClipMaskBuffers(
            new Texture(Gpu.Native.TextureHandle.Invalid),
            new Buffer(Gpu.Native.BufferHandle.Invalid),
            ClipMaskBuffers.AtlasSize,
            ClipMaskBuffers.AtlasSize);

        var compositor = new ClipCompositor(buffers);

        compositor.PushClip();
        compositor.PushClip();
        compositor.PushClip();

        await Assert.That(compositor.CurrentDepth).IsEqualTo(3);

        compositor.Reset();

        await Assert.That(compositor.CurrentDepth).IsEqualTo(0);

        int slot = compositor.PushClip();
        await Assert.That(slot).IsEqualTo(0);

        compositor.Dispose();
    }

    [Test]
    public async Task ClipCompositor_Dispose_Idempotent()
    {
        var buffers = new ClipMaskBuffers(
            new Texture(Gpu.Native.TextureHandle.Invalid),
            new Buffer(Gpu.Native.BufferHandle.Invalid),
            ClipMaskBuffers.AtlasSize,
            ClipMaskBuffers.AtlasSize);

        var compositor = new ClipCompositor(buffers);

        compositor.Dispose();
        compositor.Dispose();
    }

    [Test]
    public async Task ClipMaskBuffers_Dispose_Idempotent()
    {
        var buffers = new ClipMaskBuffers(
            new Texture(Gpu.Native.TextureHandle.Invalid),
            new Buffer(Gpu.Native.BufferHandle.Invalid),
            ClipMaskBuffers.AtlasSize,
            ClipMaskBuffers.AtlasSize);

        buffers.Dispose();
        buffers.Dispose();
    }
}
