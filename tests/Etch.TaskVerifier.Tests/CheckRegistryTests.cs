using Etch.TaskVerifier;
using TUnit;

namespace Etch.TaskVerifier.Tests;

internal sealed class CheckRegistryTests
{
    [Test]
    public async Task GetRegisteredVerbReturnsCheck()
    {
        var check = CheckRegistry.Get("file-exists");
        await Assert.That(check).IsNotNull();
        await Assert.That(check!.Verb).IsEqualTo("file-exists");
    }

    [Test]
    public async Task GetUnknownVerbReturnsNull()
    {
        var check = CheckRegistry.Get("nonexistent-verb");
        await Assert.That(check).IsNull();
    }

    [Test]
    public async Task AllVerbsContainsExpectedChecks()
    {
        var verbs = CheckRegistry.AllVerbs.ToList();
        await Assert.That(verbs).Contains("file-exists");
        await Assert.That(verbs).Contains("aot-publish");
        await Assert.That(verbs).Contains("tunit");
        await Assert.That(verbs).Contains("symbol-absent");
        await Assert.That(verbs).Contains("symbol-shape");
        await Assert.That(verbs).Contains("trim-warning-count");
        await Assert.That(verbs).Contains("bench-run");
        await Assert.That(verbs).Contains("bench-alloc");
    }

    [Test]
    public async Task CountReturnsCorrectNumber()
    {
        int count = CheckRegistry.Count;
        await Assert.That(count).IsGreaterThan(0);
    }
}
