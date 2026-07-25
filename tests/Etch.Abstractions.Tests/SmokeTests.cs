namespace Etch.Tests;

internal sealed class SmokeTests
{
    [Test]
    public async Task TrueIsTrue()
    {
        bool value = true;
        await Assert.That(value).IsTrue();
    }
}
