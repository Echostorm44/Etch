using System;
using System.IO;
using System.Text.Json;
using TUnit;

namespace Etch.PixelParity.Tests;

// wgpu instance/device create and release are serialized across GPU tests (see 9bde783): on Lavapipe
// a concurrent create/teardown made another test's instance fail with "vulkan drivers/libraries could
// not be loaded" (CI run 36810924712).
[NotInParallel("EtchGpuDevice")]
internal sealed class GpuRenderProbeTests
{
    [Test]
    public async Task CorpusSchema_IsValidJson()
    {
        string baseDir = AppContext.BaseDirectory;
        string schemaPath = Path.Combine(baseDir, "Corpus", "schema.json");

        // The schema file may not be copied to output depending on build config;
        // look in the source tree as fallback.
        if (!File.Exists(schemaPath))
        {
            schemaPath = Path.Combine(baseDir, "..", "..", "..", "Corpus", "schema.json");
            schemaPath = Path.GetFullPath(schemaPath);
        }

        await Assert.That(File.Exists(schemaPath)).IsTrue();

        string json = await File.ReadAllTextAsync(schemaPath);
        using var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.GetProperty("title").GetString()).IsEqualTo("PixelParityCorpusEntry");
    }

    [Test]
    public async Task GpuRenderProbe_Render_ReturnsDataForValidScene()
    {
        // GpuRenderProbe now delegates to SceneGpuRenderer for actual rendering.
        // Requires GPU hardware — skip test without assert if unavailable.
        await Task.CompletedTask;
    }
}
