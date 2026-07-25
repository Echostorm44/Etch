using System;
using Etch.Tiling;
using TUnit;

namespace Etch.Gpu.Compositor.Tests;

internal sealed class TileQuadUploaderTests
{
    [Test]
    public async Task TileQuadList_CountMatchesQuadsSpan()
    {
        var quads = new TileQuad[10];
        quads[0] = new TileQuad(0, 0, 32, 32, 0, 1, 0, 0);
        quads[1] = new TileQuad(1, 0, 32, 32, 1, 1, 0, 1);

        var list = new TileQuadList(quads, 2);

        await Assert.That(list.Count).IsEqualTo(2);
        await Assert.That(list.Quads.Length).IsEqualTo(2);
    }

    [Test]
    public async Task TileQuadBuffers_DisposeIdempotent()
    {
        var buffers = new TileQuadBuffers(
            new Buffer(Gpu.Native.BufferHandle.Invalid),
            new Buffer(Gpu.Native.BufferHandle.Invalid));

        buffers.Dispose();
        buffers.Dispose();
    }

    [Test]
    public async Task TileQuadUploader_DisposeIdempotent()
    {
        var uploader = new TileQuadUploader(new Device(Gpu.Native.DeviceHandle.Invalid));
        uploader.Dispose();
        uploader.Dispose();
    }
}
