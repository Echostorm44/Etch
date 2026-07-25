namespace Etch.Gpu.Compositor;

public readonly struct StripGpuBuffers : IDisposable
{
    public Buffer Strips { get; }
    public Buffer Coverage { get; }
    public uint StripCount { get; }

    public StripGpuBuffers(Buffer strips, Buffer coverage, uint stripCount)
    {
        Strips = strips;
        Coverage = coverage;
        StripCount = stripCount;
    }

    public void Dispose()
    {
        Coverage.Dispose();
        Strips.Dispose();
    }
}