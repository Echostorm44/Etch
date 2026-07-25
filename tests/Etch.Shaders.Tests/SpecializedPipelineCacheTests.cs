using System;
using System.Threading.Tasks;
using Etch.Gpu;
using Etch.Gpu.Pipelines;
using Etch.Shaders;

namespace Etch.Shaders.Tests;

internal sealed class SpecializedPipelineCacheTests
{
    [Test]
    public void ConstantEntry_Equals_Works()
    {
        var entry1 = new ConstantEntry("gamma_mode", 1u);
        var entry2 = new ConstantEntry("gamma_mode", 1u);
        var entry3 = new ConstantEntry("gamma_mode", 2u);
        var entry4 = new ConstantEntry("blend_mode", 1u);

        if (!entry1.Equals(entry2))
        {
            throw new InvalidOperationException("entry1 should equal entry2");
        }
        if (entry1.Equals(entry3))
        {
            throw new InvalidOperationException("entry1 should not equal entry3");
        }
        if (entry1.Equals(entry4))
        {
            throw new InvalidOperationException("entry1 should not equal entry4");
        }
    }

    [Test]
    public void ShaderSpecializationKey_Equals_Works()
    {
        var key1a = new TestSpecKey { GammaMode = 1u, BlendMode = 0u };
        var key1b = new TestSpecKey { GammaMode = 1u, BlendMode = 0u };
        var key2 = new TestSpecKey { GammaMode = 2u, BlendMode = 0u };
        var key3 = new TestSpecKey { GammaMode = 1u, BlendMode = 1u };

        var wrapper1a = new ShaderSpecializationKey<TestSpecKey>(key1a);
        var wrapper1b = new ShaderSpecializationKey<TestSpecKey>(key1b);
        var wrapper2 = new ShaderSpecializationKey<TestSpecKey>(key2);
        var wrapper3 = new ShaderSpecializationKey<TestSpecKey>(key3);

        if (!wrapper1a.Equals(wrapper1b))
        {
            throw new InvalidOperationException("wrapper1a should equal wrapper1b");
        }
        if (wrapper1a.Equals(wrapper2))
        {
            throw new InvalidOperationException("wrapper1a should not equal wrapper2");
        }
        if (wrapper1a.Equals(wrapper3))
        {
            throw new InvalidOperationException("wrapper1a should not equal wrapper3");
        }
    }

    [Test]
    public void ShaderSpecializationKey_Hash_Consistent()
    {
        var key1a = new TestSpecKey { GammaMode = 1u, BlendMode = 0u };
        var key1b = new TestSpecKey { GammaMode = 1u, BlendMode = 0u };

        var wrapper1a = new ShaderSpecializationKey<TestSpecKey>(key1a);
        var wrapper1b = new ShaderSpecializationKey<TestSpecKey>(key1b);

        if (wrapper1a.Hash != wrapper1b.Hash)
        {
            throw new InvalidOperationException("Hash should be equal for equal keys");
        }
    }

    [Test]
    public void ShaderSpecializationKey_ToEntries_ReturnsCorrectEntries()
    {
        var key = new TestSpecKey { GammaMode = 5u, BlendMode = 3u };
        var wrapper = new ShaderSpecializationKey<TestSpecKey>(key);
        var entries = wrapper.ToEntries();

        if (entries.Length != 2)
        {
            throw new InvalidOperationException($"Expected 2 entries, got {entries.Length}");
        }

        if (entries[0].Name != "gamma_mode" || entries[0].Value != 5u)
        {
            throw new InvalidOperationException($"Entry 0 incorrect: {entries[0].Name} = {entries[0].Value}");
        }
        if (entries[1].Name != "blend_mode" || entries[1].Value != 3u)
        {
            throw new InvalidOperationException($"Entry 1 incorrect: {entries[1].Name} = {entries[1].Value}");
        }
    }

    [Test]
    public void SpecializedPipelineCache_ReturnsCachedPipeline()
    {
        var cache = new SpecializedPipelineCache();
        var key = new TestSpecKey { GammaMode = 1u, BlendMode = 0u };
        int factoryCallCount = 0;

        RenderPipeline Factory(TestSpecKey k)
        {
            factoryCallCount++;
            return new RenderPipeline(Gpu.Native.RenderPipelineHandle.Invalid);
        }

        var pipeline1 = cache.GetOrCreate(key, Factory);
        var pipeline2 = cache.GetOrCreate(key, Factory);

        if (factoryCallCount != 1)
        {
            throw new InvalidOperationException($"Factory should be called once, was called {factoryCallCount} times");
        }

        if (pipeline1.Handle != pipeline2.Handle)
        {
            throw new InvalidOperationException("Should return same pipeline for same key");
        }

        pipeline1.Dispose();
        pipeline2.Dispose();
    }

    [Test]
    public void SpecializedPipelineCache_DifferentKeysCreateDifferentPipelines()
    {
        var cache = new SpecializedPipelineCache();
        var key1 = new TestSpecKey { GammaMode = 1u, BlendMode = 0u };
        var key2 = new TestSpecKey { GammaMode = 2u, BlendMode = 0u };
        int factoryCallCount = 0;

        RenderPipeline Factory(TestSpecKey k)
        {
            factoryCallCount++;
            return new RenderPipeline(Gpu.Native.RenderPipelineHandle.Invalid);
        }

        var pipeline1 = cache.GetOrCreate(key1, Factory);
        var pipeline2 = cache.GetOrCreate(key2, Factory);

        if (factoryCallCount != 2)
        {
            throw new InvalidOperationException($"Factory should be called twice, was called {factoryCallCount} times");
        }

        if (pipeline1.Handle == pipeline2.Handle)
        {
            throw new InvalidOperationException("Different keys should create different pipelines");
        }

        pipeline1.Dispose();
        pipeline2.Dispose();
    }

    [Test]
    public async Task SpecializedPipelineCache_ZeroAllocOnHit()
    {
        var cache = new SpecializedPipelineCache();
        var key = new TestSpecKey { GammaMode = 1u, BlendMode = 0u };

        RenderPipeline Factory(TestSpecKey k)
        {
            return new RenderPipeline(Gpu.Native.RenderPipelineHandle.Invalid);
        }

        cache.GetOrCreate(key, Factory);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            cache.GetOrCreate(key, Factory);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;

        if (delta > 0)
        {
            throw new InvalidOperationException($"Cache hit should be zero-alloc, but allocated {delta} bytes");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private readonly struct TestSpecKey : IShaderSpecKey
    {
        public uint GammaMode { get; init; }
        public uint BlendMode { get; init; }

        public int Hash => HashCode.Combine(GammaMode, BlendMode);

        public ReadOnlySpan<ConstantEntry> ToEntries() => new[]
        {
            new ConstantEntry("gamma_mode", GammaMode),
            new ConstantEntry("blend_mode", BlendMode),
        };
    }
}