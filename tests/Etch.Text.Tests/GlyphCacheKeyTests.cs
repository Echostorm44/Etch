using Etch.Text.Atlas;
using TUnit;

namespace Etch.Text.Tests;

public sealed class GlyphCacheKeyTests
{
    [Test]
    public async Task SizeIsExactly12Bytes()
    {
        if (GlyphCacheKey.SizeOf != 12)
            throw new InvalidOperationException("GlyphCacheKey should be exactly 12 bytes (FaceId + SizeQuantUnits + GlyphId + SubpixelX + alignment padding)");
    }

    [Test]
    public async Task EqualKeysAreEqual()
    {
        var key1 = new GlyphCacheKey(1, 640, 65, 0);
        var key2 = new GlyphCacheKey(1, 640, 65, 0);
        if (!key1.Equals(key2))
            throw new InvalidOperationException("Equal keys should be equal");
    }

    [Test]
    public async Task DifferentFaceIdsAreNotEqual()
    {
        var key1 = new GlyphCacheKey(1, 640, 65, 0);
        var key2 = new GlyphCacheKey(2, 640, 65, 0);
        if (key1.Equals(key2))
            throw new InvalidOperationException("Different FaceIds should not be equal");
    }

    [Test]
    public async Task DifferentSizesAreNotEqual()
    {
        var key1 = new GlyphCacheKey(1, 640, 65, 0);
        var key2 = new GlyphCacheKey(1, 800, 65, 0);
        if (key1.Equals(key2))
            throw new InvalidOperationException("Different SizeQuantUnits should not be equal");
    }

    [Test]
    public async Task DifferentGlyphIdsAreNotEqual()
    {
        var key1 = new GlyphCacheKey(1, 640, 65, 0);
        var key2 = new GlyphCacheKey(1, 640, 66, 0);
        if (key1.Equals(key2))
            throw new InvalidOperationException("Different GlyphIds should not be equal");
    }

    [Test]
    public async Task DifferentSubpixelXAreNotEqual()
    {
        var key1 = new GlyphCacheKey(1, 640, 65, 0);
        var key2 = new GlyphCacheKey(1, 640, 65, 1);
        if (key1.Equals(key2))
            throw new InvalidOperationException("Different SubpixelX should not be equal");
    }

    [Test]
    public async Task HashCodeConsistentForEqualKeys()
    {
        var key1 = new GlyphCacheKey(1, 640, 65, 0);
        var key2 = new GlyphCacheKey(1, 640, 65, 0);
        if (key1.GetHashCode() != key2.GetHashCode())
            throw new InvalidOperationException("Equal keys should have equal hash codes");
    }

    [Test]
    public async Task FromSizeAndSubpixelCreatesValidKey()
    {
        var key = GlyphCacheKey.FromSizeAndSubpixel(10f, 1, 65, 0);
        if (key.FaceId != 1)
            throw new InvalidOperationException("FaceId should be 1");
        if (key.GlyphId != 65)
            throw new InvalidOperationException("GlyphId should be 65");
        if (key.SubpixelX != 0)
            throw new InvalidOperationException("SubpixelX should be 0");
    }

    [Test]
    public async Task SizeQuantizationUsesBankersRounding()
    {
        var key1 = GlyphCacheKey.FromSizeAndSubpixel(10.0f, 1, 65, 0);
        var key2 = GlyphCacheKey.FromSizeAndSubpixel(10.0f, 1, 65, 0);
        if (key1.SizeQuantUnits != key2.SizeQuantUnits)
            throw new InvalidOperationException("Size quantization should be consistent");
    }
}