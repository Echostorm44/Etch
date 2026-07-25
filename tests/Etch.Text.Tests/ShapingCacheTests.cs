using Etch.Text.Shape;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class ShapingCacheTests
{
    private static FontFace GetRoboto() =>
        FontFace.Load(TestFonts.RobotoRegular, 2048, 12f);

    [Test]
    public async Task RepeatedShapeRequest_1K_CacheHitRate999()
    {
        using var face = GetRoboto();
        var cache = new ShapingCache(capacity: 4096);

        int misses = SyncHelpers.CountMisses(cache, face, "Hello", 1000);

        await Assert.That(misses).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DistinctStrings_1K_AllMisses()
    {
        using var face = GetRoboto();
        var cache = new ShapingCache(capacity: 4096);

        int misses = SyncHelpers.CountDistinctMisses(cache, face, 1000);

        await Assert.That(misses).IsEqualTo(1000);
        await Assert.That(cache.Count).IsEqualTo(1000);
    }

    [Test]
    public async Task LruTouch_KeepsEntryAlive()
    {
        using var face = GetRoboto();
        var cache = new ShapingCache(capacity: 3);

        SyncHelpers.ShapeOnce(cache, face, "A");
        SyncHelpers.ShapeOnce(cache, face, "B");
        SyncHelpers.ShapeOnce(cache, face, "C");

        // Touch A (moves to head).
        SyncHelpers.ShapeOnce(cache, face, "A");

        // Insert D — should evict B (oldest untouched).
        SyncHelpers.ShapeOnce(cache, face, "D");

        // Check C and D FIRST — checking B (a miss) would insert B and evict C.
        await Assert.That(SyncHelpers.IsHit(cache, face, "C")).IsTrue();
        await Assert.That(SyncHelpers.IsHit(cache, face, "D")).IsTrue();
        await Assert.That(SyncHelpers.IsHit(cache, face, "A")).IsTrue();
        await Assert.That(SyncHelpers.IsHit(cache, face, "B")).IsFalse();
    }

    [Test]
    public async Task DifferentFace_SameText_DifferentEntry()
    {
        using var faceA = GetRoboto();
        using var faceB = GetRoboto();
        var cache = new ShapingCache(capacity: 10);

        SyncHelpers.ShapeOnce(cache, faceA, "Hello");
        SyncHelpers.ShapeOnce(cache, faceB, "Hello");

        await Assert.That(cache.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CapacityOverflow_EvictsOldest()
    {
        using var face = GetRoboto();
        var cache = new ShapingCache(capacity: 100);

        SyncHelpers.FillCache(cache, face, "A", 100);
        await Assert.That(cache.Count).IsEqualTo(100);

        SyncHelpers.FillCache(cache, face, "B", 50);
        await Assert.That(cache.Count).IsEqualTo(100);

        // The newer A entries (A50..A99) should still be present.
        // Check these FIRST because CountHits on misses causes insertions + evictions.
        await Assert.That(SyncHelpers.CountHits(cache, face, "A", 50, 100)).IsEqualTo(50);

        // The oldest entries (A0..A49) should have been evicted.
        await Assert.That(SyncHelpers.CountHits(cache, face, "A", 0, 50)).IsEqualTo(0);
    }

    [Test]
    public async Task DifferentScriptTag_SameText_DifferentEntry()
    {
        using var face = GetRoboto();
        var cache = new ShapingCache(capacity: 10);

        SyncHelpers.ShapeOnce(cache, face, "Hello", BiDiLevel.LeftToRight, "Latn");
        SyncHelpers.ShapeOnce(cache, face, "Hello", BiDiLevel.LeftToRight, "Arab");

        await Assert.That(cache.Count).IsEqualTo(2);
    }

    [Test]
    public async Task DifferentSize_SameText_DifferentEntry()
    {
        using var face12 = FontFace.Load(TestFonts.RobotoRegular, 2048, 12f);
        using var face24 = FontFace.Load(TestFonts.RobotoRegular, 2048, 24f);
        var cache = new ShapingCache(capacity: 10);

        SyncHelpers.ShapeOnce(cache, face12, "Hello");
        SyncHelpers.ShapeOnce(cache, face24, "Hello");

        await Assert.That(cache.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CollisionFabrication_CorrectResultForActualText()
    {
        using var face = GetRoboto();
        var cache = new ShapingCache(capacity: 10);

        var runA = SyncHelpers.ShapeOnce(cache, face, "A");
        var runB = SyncHelpers.ShapeOnce(cache, face, "B");

        // Both should be cached.
        await Assert.That(SyncHelpers.IsHit(cache, face, "A")).IsTrue();
        await Assert.That(SyncHelpers.IsHit(cache, face, "B")).IsTrue();

        // Glyph counts should match.
        var cachedA = SyncHelpers.ShapeOnce(cache, face, "A");
        var cachedB = SyncHelpers.ShapeOnce(cache, face, "B");
        await Assert.That(cachedA.GlyphCount).IsEqualTo(runA.GlyphCount);
        await Assert.That(cachedB.GlyphCount).IsEqualTo(runB.GlyphCount);
    }

    [Test]
    public async Task EmptyString_CachesCorrectly()
    {
        using var face = GetRoboto();
        var cache = new ShapingCache(capacity: 10);

        bool first = SyncHelpers.TryShape(cache, face, string.Empty);
        bool second = SyncHelpers.TryShape(cache, face, string.Empty);
        var run1 = SyncHelpers.ShapeOnce(cache, face, string.Empty);
        var run2 = SyncHelpers.ShapeOnce(cache, face, string.Empty);

        await Assert.That(first).IsFalse();
        await Assert.That(second).IsTrue();
        await Assert.That(run1.GlyphCount).IsEqualTo(0);
        await Assert.That(run2.GlyphCount).IsEqualTo(0);
    }

    // ------------------------------------------------------------------
    // Fully synchronous helpers — no ref struct crosses await boundary
    // ------------------------------------------------------------------
    private static class SyncHelpers
    {
        public static bool TryShape(ShapingCache cache, FontFace face, string text, BiDiLevel level = BiDiLevel.LeftToRight, string script = "Latn")
        {
            var request = new ShapeRequest(text, face, level, script);
            return cache.TryShape(request, out _);
        }

        public static ShapedRun ShapeOnce(ShapingCache cache, FontFace face, string text, BiDiLevel level = BiDiLevel.LeftToRight, string script = "Latn")
        {
            var request = new ShapeRequest(text, face, level, script);
            cache.TryShape(request, out var run);
            return run;
        }

        public static int CountMisses(ShapingCache cache, FontFace face, string text, int iterations)
        {
            var request = new ShapeRequest(text, face, BiDiLevel.LeftToRight, "Latn");
            int misses = 0;
            for (int i = 0; i < iterations; i++)
            {
                if (!cache.TryShape(request, out _))
                    misses++;
            }
            return misses;
        }

        public static int CountDistinctMisses(ShapingCache cache, FontFace face, int count)
        {
            int misses = 0;
            for (int i = 0; i < count; i++)
            {
                var request = new ShapeRequest($"Text{i}", face, BiDiLevel.LeftToRight, "Latn");
                if (!cache.TryShape(request, out _))
                    misses++;
            }
            return misses;
        }

        public static void FillCache(ShapingCache cache, FontFace face, string prefix, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var request = new ShapeRequest($"{prefix}{i}", face, BiDiLevel.LeftToRight, "Latn");
                cache.TryShape(request, out _);
            }
        }

        public static int CountHits(ShapingCache cache, FontFace face, string prefix, int start, int end)
        {
            int hits = 0;
            for (int i = start; i < end; i++)
            {
                var request = new ShapeRequest($"{prefix}{i}", face, BiDiLevel.LeftToRight, "Latn");
                if (cache.TryShape(request, out _))
                    hits++;
            }
            return hits;
        }

        public static bool IsHit(ShapingCache cache, FontFace face, string text)
        {
            var request = new ShapeRequest(text, face, BiDiLevel.LeftToRight, "Latn");
            return cache.TryShape(request, out _);
        }
    }
}
