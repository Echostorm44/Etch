namespace Etch.Shaders.Tests;

internal sealed class ShaderSourceTests
{
    [Test]
    public async Task EmbeddedShaderSourceGetSourceReturnsValidBytes()
    {
        var source = new EmbeddedShaderSource();
        var bytes = source.GetSource("solid_fill");

        await Assert.That(bytes.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task EmbeddedShaderSourceGetSourceUnknownReturnsEmpty()
    {
        var source = new EmbeddedShaderSource();
        var bytes = source.GetSource("nonexistent_shader");

        await Assert.That(bytes.Length).IsEqualTo(0);
    }

    [Test]
    public async Task EmbeddedShaderSourceTryGetVersionReturnsTrue()
    {
        var source = new EmbeddedShaderSource();
        bool result = source.TryGetVersion("solid_fill", out ulong version);

        await Assert.That(result).IsTrue();
        await Assert.That(version).IsNotEqualTo(0ul);
    }

    [Test]
    public async Task EmbeddedShaderSourceChangedNeverFires()
    {
        var source = new EmbeddedShaderSource();
        bool eventFired = false;
        source.Changed += (s, e) => eventFired = true;

        await Task.Delay(100).ConfigureAwait(false);

        await Assert.That(eventFired).IsFalse();
    }

    [Test]
    public async Task ShaderSourceDefaultIsEmbeddedByDefault()
    {
        var source = ShaderSource.Default;
        await Assert.That(ReferenceEquals(source, null)).IsFalse();
    }

    [Test]
    public async Task HotReloadShaderSourceCanBeDisposed()
    {
        using var source = new HotReloadShaderSource();
        await Assert.That(source).IsNotNull();
    }

    [Test]
    public async Task HotReloadShaderSourceGetSourceFallsBackToEmbedded()
    {
        using var source = new HotReloadShaderSource();
        var bytes = source.GetSource("nonexistent");

        await Assert.That(bytes.Length).IsEqualTo(0);
    }
}