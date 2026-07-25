using System;
using System.IO;
using System.Text;
using Etch.Abstractions.Diagnostics;
using Etch.Gpu.Diagnostics;
using Etch.Primitives;

namespace Etch.Gpu.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Unit tests for FFI-010's diagnostics surface: ValidationLogRing (hot path
// zero-alloc + round-trip), AdapterInfo + SurfaceConfigInfo (encode/decode),
// GpuSceneReproducer (envelope round-trip), and CrashDump (file-write path).
// ═══════════════════════════════════════════════════════════════════════════

internal sealed class GpuSceneReproducerTests
{
    [Test]
    public async Task ValidationLogRingPushIsZeroAlloc()
    {
        var ring = new ValidationLogRing(capacity: 256);
        byte[] message = Encoding.UTF8.GetBytes("sample validation message");

        // Warm the JIT thoroughly — force tier-1 compilation before measuring.
        for (int i = 0; i < 200; i++)
        {
            ring.Push(ErrorType.Validation, message, timestampTicks: i);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            ring.Push(ErrorType.Validation, message, timestampTicks: i);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;
        if (delta > 0)
        {
            throw new InvalidOperationException($"Push loop allocated {delta} bytes across 1000 iterations (expected 0)");
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidationLogRingRoundTripsEntriesInOrder()
    {
        var ring = new ValidationLogRing(capacity: 16);
        for (int i = 0; i < 5; i++)
        {
            ring.Push(ErrorType.Validation, Encoding.UTF8.GetBytes($"entry-{i}"), timestampTicks: 100 + i);
        }

        byte[] snapshot = ring.Snapshot();
        bool decoded = ValidationLogRing.TryDecode(snapshot, out var log);

        if (!decoded) throw new InvalidOperationException("Decode returned false");
        if (log.Count != 5) throw new InvalidOperationException($"Expected 5 entries, got {log.Count}");
        for (int i = 0; i < 5; i++)
        {
            var entry = log[i];
            if (entry.Message != $"entry-{i}") throw new InvalidOperationException($"Entry {i} message mismatch: {entry.Message}");
            if (entry.TimestampTicks != 100 + i) throw new InvalidOperationException($"Entry {i} timestamp mismatch: {entry.TimestampTicks}");
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task ValidationLogRingWrapsAroundOldestDropped()
    {
        var ring = new ValidationLogRing(capacity: 4);
        for (int i = 0; i < 10; i++)
        {
            ring.Push(ErrorType.Validation, Encoding.UTF8.GetBytes($"msg-{i}"), timestampTicks: i);
        }

        bool decoded = ValidationLogRing.TryDecode(ring.Snapshot(), out var log);
        if (!decoded) throw new InvalidOperationException("Decode returned false");
        if (log.Count != 4) throw new InvalidOperationException($"Ring of capacity 4 must hold exactly 4 entries, got {log.Count}");

        // After 10 writes with capacity 4, we expect slots corresponding to writes 6..9.
        // Because concurrent slot overwrites on a single thread are impossible, this is
        // deterministic here: entries [0..3] correspond to writes 6..9.
        for (int i = 0; i < 4; i++)
        {
            int expectedWriteIdx = 6 + i;
            if (log[i].Message != $"msg-{expectedWriteIdx}")
            {
                throw new InvalidOperationException($"Wrap-around entry {i}: expected msg-{expectedWriteIdx}, got {log[i].Message}");
            }
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task AdapterInfoRoundTrips()
    {
        var info = new AdapterInfo(
            backendType: 3,
            adapterType: 1,
            vendorId: 0x10DE,
            deviceId: 0x2684,
            featuresBitmask: 0x0000_0000_0000_00FFUL,
            deviceName: "NVIDIA GeForce RTX 4090",
            driverDescription: "NVIDIA 560.94.0",
            backendName: "Vulkan");

        Span<byte> buffer = stackalloc byte[AdapterInfo.MaxEncodedSize];
        int written = info.Encode(buffer);

        byte[] copy = buffer.Slice(0, written).ToArray();
        if (!AdapterInfo.TryDecode(copy, out var decoded))
        {
            throw new InvalidOperationException("TryDecode returned false");
        }

        if (decoded.BackendType != 3) throw new InvalidOperationException("BackendType mismatch");
        if (decoded.AdapterType != 1) throw new InvalidOperationException("AdapterType mismatch");
        if (decoded.VendorId != 0x10DE) throw new InvalidOperationException("VendorId mismatch");
        if (decoded.DeviceId != 0x2684) throw new InvalidOperationException("DeviceId mismatch");
        if (decoded.FeaturesBitmask != 0xFFUL) throw new InvalidOperationException("FeaturesBitmask mismatch");
        if (decoded.DeviceName != "NVIDIA GeForce RTX 4090") throw new InvalidOperationException("DeviceName mismatch");
        if (decoded.DriverDescription != "NVIDIA 560.94.0") throw new InvalidOperationException("DriverDescription mismatch");
        if (decoded.BackendName != "Vulkan") throw new InvalidOperationException("BackendName mismatch");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AdapterInfoTruncatesOversizeStringsAtUtf8Boundary()
    {
        // Build a 300-character name (> MaxDeviceNameBytes=256) with mixed multi-byte code points.
        string oversize = new string('Ω', 150) + new string('π', 150); // each 2 bytes = 600 bytes total
        var info = new AdapterInfo(0, 0, 0, 0, 0, oversize, "drv", "bk");

        Span<byte> buffer = stackalloc byte[AdapterInfo.MaxEncodedSize];
        int written = info.Encode(buffer);
        byte[] copy = buffer.Slice(0, written).ToArray();

        bool decoded = AdapterInfo.TryDecode(copy, out var parsed);
        if (!decoded) throw new InvalidOperationException("Decode failed");

        // Parsed name must be at most 256 bytes when re-encoded to UTF-8.
        int byteLen = Encoding.UTF8.GetByteCount(parsed.DeviceName);
        if (byteLen > AdapterInfo.MaxDeviceNameBytes) throw new InvalidOperationException($"Truncation failed: {byteLen} bytes");

        // Every character must be a valid Ω or π — truncation must not have split a code point.
        foreach (char ch in parsed.DeviceName)
        {
            if (ch != 'Ω' && ch != 'π') throw new InvalidOperationException($"Unexpected character {ch} after truncation");
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task SurfaceConfigInfoRoundTrips()
    {
        var cfg = new SurfaceConfigInfo(format: 18, width: 1920, height: 1080, presentMode: 2, alphaMode: 1, usage: 0x10);
        Span<byte> buffer = stackalloc byte[SurfaceConfigInfo.EncodedSize];
        int written = cfg.Encode(buffer);
        byte[] copy = buffer.Slice(0, written).ToArray();

        if (!SurfaceConfigInfo.TryDecode(copy, out var decoded)) throw new InvalidOperationException("Decode failed");
        if (decoded.Format != 18) throw new InvalidOperationException("Format mismatch");
        if (decoded.Width != 1920) throw new InvalidOperationException("Width mismatch");
        if (decoded.Height != 1080) throw new InvalidOperationException("Height mismatch");
        if (decoded.PresentMode != 2) throw new InvalidOperationException("PresentMode mismatch");
        if (decoded.AlphaMode != 1) throw new InvalidOperationException("AlphaMode mismatch");
        if (decoded.Usage != 0x10) throw new InvalidOperationException("Usage mismatch");

        await Task.CompletedTask;
    }

    [Test]
    public async Task GpuSceneReproducerCaptureRoundTripsAllSections()
    {
        var ring = new ValidationLogRing(capacity: 16);
        ring.Push(ErrorType.Validation, Encoding.UTF8.GetBytes("bad usage"), 1000);
        ring.Push(ErrorType.OutOfMemory, Encoding.UTF8.GetBytes("alloc fail"), 2000);

        var adapter = new AdapterInfo(3, 1, 0x10DE, 0x2684, 0x3FUL, "RTX 4090", "driver", "Vulkan");
        var surface = new SurfaceConfigInfo(18, 800, 600, 2, 1, 0x10);
        byte[] sceneBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var reproducer = new GpuSceneReproducer(ring, adapter, surface, sceneBytes);
        int upper = reproducer.CalculateUpperBound(sceneBytes.Length);
        byte[] destination = new byte[upper];
        int written = reproducer.CaptureTo(destination);
        if (written <= 0) throw new InvalidOperationException("CaptureTo returned 0");

        var envelope = destination.AsSpan(0, written).ToArray();
        var reader = new SceneReproReader(envelope);
        if (!reader.TryReadHeader()) throw new InvalidOperationException($"Envelope header parse failed: {reader.Result}");
        if (reader.SectionCount != 4) throw new InvalidOperationException($"Expected 4 sections, got {reader.SectionCount}");

        bool sawScene = false, sawValidation = false, sawAdapter = false, sawSurface = false;
        while (reader.TryReadNextSection(out var id, out var payload))
        {
            switch (id)
            {
                case ReproSection.Scene:
                    sawScene = true;
                    if (payload.Length != sceneBytes.Length) throw new InvalidOperationException("Scene payload length mismatch");
                    for (int i = 0; i < sceneBytes.Length; i++)
                    {
                        if (payload[i] != sceneBytes[i]) throw new InvalidOperationException("Scene payload byte mismatch");
                    }
                    break;
                case ReproSection.GpuValidationLog:
                    sawValidation = true;
                    if (!ValidationLogRing.TryDecode(payload, out var log)) throw new InvalidOperationException("Validation decode failed");
                    if (log.Count != 2) throw new InvalidOperationException($"Expected 2 validation entries, got {log.Count}");
                    if (log[0].Message != "bad usage") throw new InvalidOperationException("Validation entry 0 mismatch");
                    if (log[1].Message != "alloc fail") throw new InvalidOperationException("Validation entry 1 mismatch");
                    break;
                case ReproSection.GpuAdapterInfo:
                    sawAdapter = true;
                    if (!AdapterInfo.TryDecode(payload, out var a)) throw new InvalidOperationException("Adapter decode failed");
                    if (a.DeviceName != "RTX 4090") throw new InvalidOperationException("Adapter name mismatch");
                    break;
                case ReproSection.GpuSurfaceConfig:
                    sawSurface = true;
                    if (!SurfaceConfigInfo.TryDecode(payload, out var s)) throw new InvalidOperationException("Surface decode failed");
                    if (s.Width != 800) throw new InvalidOperationException("Surface width mismatch");
                    break;
            }
        }

        if (!sawScene) throw new InvalidOperationException("Missing Scene section");
        if (!sawValidation) throw new InvalidOperationException("Missing GpuValidationLog section");
        if (!sawAdapter) throw new InvalidOperationException("Missing GpuAdapterInfo section");
        if (!sawSurface) throw new InvalidOperationException("Missing GpuSurfaceConfig section");

        await Task.CompletedTask;
    }

    [Test]
    public async Task CrashDumpWritesFileWithPidAndTimestamp()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "etch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var ring = new ValidationLogRing(capacity: 8);
            ring.Push(ErrorType.Validation, Encoding.UTF8.GetBytes("boom"), 1);
            var adapter = new AdapterInfo(1, 1, 1, 1, 0, "n", "d", "b");
            var surface = new SurfaceConfigInfo(1, 2, 3, 4, 5, 6);
            var reproducer = new GpuSceneReproducer(ring, adapter, surface);

            string? path = CrashDump.TryWrite(
                reproducer,
                reproducer.CalculateUpperBound(0),
                "20260423120000001",
                directoryOverride: tempDir);
            if (path is null) throw new InvalidOperationException("TryWrite returned null");
            if (!File.Exists(path)) throw new InvalidOperationException($"Dump file missing: {path}");
            if (!Path.GetFileName(path).Contains(Environment.ProcessId.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Dump filename missing pid: {path}");
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task CrashDumpProducesDistinctFilesAcrossPanics()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "etch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var ring = new ValidationLogRing(capacity: 8);
            var adapter = new AdapterInfo(1, 1, 1, 1, 0, "n", "d", "b");
            var surface = new SurfaceConfigInfo(1, 2, 3, 4, 5, 6);
            var reproducer = new GpuSceneReproducer(ring, adapter, surface);
            int upper = reproducer.CalculateUpperBound(0);

            string? path1 = CrashDump.TryWrite(reproducer, upper, "20260423120000011", directoryOverride: tempDir);
            string? path2 = CrashDump.TryWrite(reproducer, upper, "20260423120000022", directoryOverride: tempDir);

            if (path1 is null || path2 is null) throw new InvalidOperationException("Dump write failed");
            if (path1 == path2) throw new InvalidOperationException("Two panics produced the same filename");
            if (!File.Exists(path1) || !File.Exists(path2)) throw new InvalidOperationException("Files missing");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task CrashDumpFallsBackToTempWhenEnvMissing()
    {
        // Snapshot + clear the env var for the duration of this assertion. We do not
        // run in parallel with other tests that read the env var because both of the
        // other CrashDump tests use directoryOverride.
        string? prior = Environment.GetEnvironmentVariable(CrashDump.DirectoryEnvVar);
        Environment.SetEnvironmentVariable(CrashDump.DirectoryEnvVar, null);
        try
        {
            string resolved = CrashDump.ResolveDirectory();
            string expectedPrefix = Path.Combine(Path.GetTempPath(), "etch");
            if (!resolved.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Fallback dir not under temp/etch: {resolved}");
            }
            if (!Directory.Exists(resolved)) throw new InvalidOperationException("Fallback directory not created");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CrashDump.DirectoryEnvVar, prior);
        }

        await Task.CompletedTask;
    }
}
