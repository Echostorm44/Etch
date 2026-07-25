using System;
using System.IO;
using Etch.PixelParity.Tests;
using TUnit;

namespace Etch.PixelParity.Tests;

internal sealed class AdapterMatrixTests
{
    [Test]
    public async Task RecordThenDump_RoundTrips()
    {
        var matrix = new AdapterMatrix();
        matrix.Record("scene1", "Lavapipe", passed: true, notes: "within tolerance");
        matrix.Record("scene1", "SwiftShader", passed: true);
        matrix.Record("scene2", "Lavapipe", passed: false, notes: "diff > 2/255");

        string path = Path.Combine(Path.GetTempPath(), $"matrix-test-{Guid.NewGuid()}.md");
        matrix.DumpToFile(path);

        string content = await File.ReadAllTextAsync(path);

        await Assert.That(content).Contains("scene1");
        await Assert.That(content).Contains("Lavapipe");
        await Assert.That(content).Contains("SwiftShader");
        await Assert.That(content).Contains("PASS");
        await Assert.That(content).Contains("FAIL");
        await Assert.That(content).Contains("within tolerance");
        await Assert.That(content).Contains("diff > 2/255");

        File.Delete(path);
    }

    [Test]
    public async Task DumpToFile_CreatesDirectory()
    {
        var matrix = new AdapterMatrix();
        string dir = Path.Combine(Path.GetTempPath(), $"parity-results-{Guid.NewGuid()}");
        string path = Path.Combine(dir, "matrix.md");

        matrix.Record("a", "b", passed: true);
        matrix.DumpToFile(path);

        await Assert.That(File.Exists(path)).IsTrue();

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task Record_OverwritesSameEntry()
    {
        var matrix = new AdapterMatrix();
        matrix.Record("sceneX", "AdapterA", passed: false);
        matrix.Record("sceneX", "AdapterA", passed: true);

        string path = Path.Combine(Path.GetTempPath(), $"matrix-test-{Guid.NewGuid()}.md");
        matrix.DumpToFile(path);

        string content = await File.ReadAllTextAsync(path);

        // Should only have one line for sceneX|AdapterA and it should be PASS
        int passCount = content.Split("PASS").Length - 1;
        int failCount = content.Split("FAIL").Length - 1;

        await Assert.That(passCount).IsEqualTo(1);
        await Assert.That(failCount).IsEqualTo(0);

        File.Delete(path);
    }
}
