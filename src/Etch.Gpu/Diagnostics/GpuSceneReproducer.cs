using System;
using Etch.Abstractions.Diagnostics;

namespace Etch.Gpu.Diagnostics;

// ═══════════════════════════════════════════════════════════════════════════
// GpuSceneReproducer — implements ISceneReproducer (FND-015) for the GPU
// subsystem. A crash-time caller invokes CaptureTo(dest) and receives a
// fully-formed .etrp envelope containing:
//
//   Section 2 (Scene)           — last submitted scene bytes (empty until SCN track lands)
//   Section 5 (GpuValidationLog) — ring-buffer of recent validation messages
//   Section 6 (GpuAdapterInfo)   — backend, device, driver, features
//   Section 7 (GpuSurfaceConfig) — swapchain format + size + modes
//
// This type holds no native resources of its own — it is a thin assembler
// over external state (validation ring, adapter info snapshot, etc.). The
// caller supplies those at construction time so the capture path never
// touches the live GPU (which may be in an undefined state during a panic).
// ═══════════════════════════════════════════════════════════════════════════

public sealed class GpuSceneReproducer : ISceneReproducer
{
    private readonly ValidationLogRing _validationLog;
    private readonly AdapterInfo _adapterInfo;
    private readonly SurfaceConfigInfo _surfaceConfig;
    private readonly byte[] _lastSceneBytes;

    public GpuSceneReproducer(
        ValidationLogRing validationLog,
        AdapterInfo adapterInfo,
        SurfaceConfigInfo surfaceConfig,
        byte[]? lastSceneBytes = null)
    {
        if (validationLog is null)
        {
            Panic.ArgumentNull(nameof(validationLog));
        }

        _validationLog = validationLog!;
        _adapterInfo = adapterInfo;
        _surfaceConfig = surfaceConfig;
        _lastSceneBytes = lastSceneBytes ?? Array.Empty<byte>();
    }

    public int CaptureTo(Span<byte> destination)
    {
        // Build the four payloads first. The Scene payload is opaque to us here
        // (populated when SCN track lands — empty byte span is valid v1).
        byte[] scenePayload = _lastSceneBytes;
        byte[] validationPayload = _validationLog.Snapshot();

        Span<byte> adapterScratch = stackalloc byte[AdapterInfo.MaxEncodedSize];
        int adapterLen = _adapterInfo.Encode(adapterScratch);
        byte[] adapterPayload = adapterScratch.Slice(0, adapterLen).ToArray();

        Span<byte> surfaceScratch = stackalloc byte[SurfaceConfigInfo.EncodedSize];
        int surfaceLen = _surfaceConfig.Encode(surfaceScratch);
        byte[] surfacePayload = surfaceScratch.Slice(0, surfaceLen).ToArray();

        ReadOnlySpan<ReproSection> sectionIds = new[]
        {
            ReproSection.Scene,
            ReproSection.GpuValidationLog,
            ReproSection.GpuAdapterInfo,
            ReproSection.GpuSurfaceConfig,
        };

        byte[][] payloads = new byte[][]
        {
            scenePayload,
            validationPayload,
            adapterPayload,
            surfacePayload,
        };

        if (!SceneReproWriter.TryWriteEnvelope(
                destination,
                SceneReproFormat.CurrentVersion,
                sectionIds,
                payloads,
                out int bytesWritten))
        {
            return 0;
        }

        return bytesWritten;
    }

    /// <summary>
    /// Upper-bound envelope size for a capture with the supplied scene-bytes budget.
    /// Use when the caller needs to size an ArrayPool rental before calling CaptureTo.
    /// </summary>
    public int CalculateUpperBound(int sceneBytesBudget)
    {
        // Header + 4 section headers + payload budgets.
        int validationBudget = 4 + (_validationLog.Capacity * (8 + 4 + 2 + ValidationEntry.InlineMessageCapacity));
        int adapterBudget = AdapterInfo.MaxEncodedSize;
        int surfaceBudget = SurfaceConfigInfo.EncodedSize;

        return SceneReproFormat.HeaderSize
             + 4 * SceneReproFormat.SectionHeaderSize
             + sceneBytesBudget
             + validationBudget
             + adapterBudget
             + surfaceBudget;
    }
}
