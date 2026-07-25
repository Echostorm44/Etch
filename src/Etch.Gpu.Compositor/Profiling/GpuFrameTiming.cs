using System;
using System.Globalization;

namespace Etch.Gpu.Compositor.Profiling;

public readonly struct GpuFrameTiming
{
    public double ClassifyMs { get; }
    public double StripUploadMs { get; }
    public double StripPassMs { get; }
    public double PresentMs { get; }

    public GpuFrameTiming(double classifyMs, double stripUploadMs, double stripPassMs, double presentMs)
    {
        ClassifyMs = classifyMs;
        StripUploadMs = stripUploadMs;
        StripPassMs = stripPassMs;
        PresentMs = presentMs;
    }

    public static GpuFrameTiming Unavailable => new(-1.0, -1.0, -1.0, -1.0);

    public string ToDisplayString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"GPU timing [ms]: classify={ClassifyMs:F3} strip_upload={StripUploadMs:F3} strip_pass={StripPassMs:F3} present={PresentMs:F3}");
    }

    public bool IsAvailable => ClassifyMs >= 0;
}
