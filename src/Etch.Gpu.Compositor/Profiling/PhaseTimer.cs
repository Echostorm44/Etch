using System;
using Etch.Gpu;

namespace Etch.Gpu.Compositor.Profiling;

public enum GpuProfilingPhase
{
    Classify,
    StripUpload,
    StripPass,
    Present
}

public sealed class PhaseTimer : IDisposable
{
    private readonly Device _device;
    private readonly GpuFrameTiming[] _timingRing;
    private int _currentFrame;
    private bool _disposed;
    private bool _isSupported;

    public PhaseTimer(Device device)
    {
        _device = device;
        _timingRing = new GpuFrameTiming[4];
        _currentFrame = 0;
        _isSupported = false;
    }

    public void BeginPhase(CommandEncoder encoder, GpuProfilingPhase phase)
    {
        if (!_isSupported)
        {
            return;
        }
    }

    public void EndPhase(CommandEncoder encoder, GpuProfilingPhase phase)
    {
        if (!_isSupported)
        {
            return;
        }
    }

    public GpuFrameTiming GetTimingForFrame(int frameIndex)
    {
        if ((uint)frameIndex >= (uint)_timingRing.Length)
        {
            return GpuFrameTiming.Unavailable;
        }

        return _timingRing[frameIndex];
    }

    public void AdvanceFrame()
    {
        _currentFrame = (_currentFrame + 1) % 4;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
    }
}
