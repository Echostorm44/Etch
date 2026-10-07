using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Compose.Cpu;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;
using Etch.Gpu.Validation;
using Etch.Testing;

namespace Etch.Compose.Tests;

/// <summary>Per-pixel comparison of two RGBA8 frames (colour channels only).</summary>
internal readonly record struct ParityStats(int Pixels, int DifferentPixels, double MeanError, int P999, int Max, int MaxX, int MaxY)
{
    public override string ToString()
        => $"{DifferentPixels}/{Pixels} px differ, mean {MeanError:F4}, p99.9 {P999}, max {Max} at ({MaxX},{MaxY})";

    /// <summary>
    /// Compares colour channels. The mean is over every channel of every pixel; p99.9 and max are
    /// of each pixel's largest channel difference.
    /// </summary>
    public static ParityStats Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int width, int height)
    {
        int pixels = width * height;
        Span<int> histogram = stackalloc int[256];
        long sum = 0;
        int different = 0, max = 0, maxX = 0, maxY = 0;
        for (int i = 0; i < pixels; i++)
        {
            int dr = Math.Abs(a[i * 4] - b[i * 4]);
            int dg = Math.Abs(a[i * 4 + 1] - b[i * 4 + 1]);
            int db = Math.Abs(a[i * 4 + 2] - b[i * 4 + 2]);
            sum += dr + dg + db;
            int d = Math.Max(dr, Math.Max(dg, db));
            histogram[d]++;
            if (d > 0)
            {
                different++;
            }
            if (d > max)
            {
                max = d;
                maxX = i % width;
                maxY = i / width;
            }
        }
        int rank = (int)Math.Ceiling(pixels * 0.999);
        int seen = 0, p999 = 0;
        for (int d = 0; d < 256; d++)
        {
            seen += histogram[d];
            if (seen >= rank)
            {
                p999 = d;
                break;
            }
        }
        return new ParityStats(pixels, different, sum / (3.0 * pixels), p999, max, maxX, maxY);
    }
}

internal static class ParityReport
{
    /// <summary>The <paramref name="count"/> largest differences as "(x,y) gpu r,g,b,a cpu r,g,b,a".</summary>
    public static string WorstPixels(ReadOnlySpan<byte> gpu, ReadOnlySpan<byte> cpu, int width, int count)
    {
        var worst = new List<(int Diff, int Index)>();
        for (int i = 0; i < gpu.Length / 4; i++)
        {
            int d = Math.Max(Math.Abs(gpu[i * 4] - cpu[i * 4]), Math.Max(Math.Abs(gpu[i * 4 + 1] - cpu[i * 4 + 1]), Math.Abs(gpu[i * 4 + 2] - cpu[i * 4 + 2])));
            if (d > 1)
            {
                worst.Add((d, i));
            }
        }
        var sb = new System.Text.StringBuilder();
        foreach (var (diff, i) in worst.OrderByDescending(w => w.Diff).Take(count))
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"({i % width},{i / width}) d{diff} gpu {gpu[i * 4]},{gpu[i * 4 + 1]},{gpu[i * 4 + 2]},{gpu[i * 4 + 3]} cpu {cpu[i * 4]},{cpu[i * 4 + 1]},{cpu[i * 4 + 2]},{cpu[i * 4 + 3]}; ");
        }
        return sb.ToString();
    }
}

/// <summary>Which GPU adapter a <see cref="ParityHarness"/> renders its GPU half on.</summary>
internal enum ParityAdapter
{
    /// <summary>The software reference rasterizer (WARP on Windows): exact blending, 8-bit subtexel filtering.</summary>
    Reference,

    /// <summary>The high-performance hardware adapter.</summary>
    Hardware,
}

/// <summary>
/// Renders the same <see cref="DrawRecording"/> with the GPU composer (offscreen, read back) and the
/// CPU composer, for the parity tests. Without the requested adapter the GPU half is unavailable
/// and <see cref="TryCreate"/> says why.
/// </summary>
internal sealed unsafe class ParityHarness : IDisposable
{
    private readonly Instance instance;
    private readonly Adapter adapter;
    private readonly Device device;
    private readonly GpuComposer gpu;
    private readonly CpuComposer cpu = new();
    private readonly DrawList gpuList = new();
    private readonly DrawList cpuList = new();
    private readonly DrawListBuilder builder = new();
    private readonly CpuFramebuffer framebuffer = new();

    private ParityHarness(Instance instance, Adapter adapter, Device device, uint width, uint height)
    {
        this.instance = instance;
        this.adapter = adapter;
        this.device = device;
        gpu = new GpuComposer(device, width, height);
        var description = adapter.GetDescription();
        AdapterName = description.ToString();
        IsSoftware = description.IsSoftware;
    }

    public string AdapterName { get; }

    public bool IsSoftware { get; }

    public static ParityHarness? TryCreate(uint width, uint height, ParityAdapter kind, out string reason)
    {
        var instance = Instance.Create();
        var result = kind == ParityAdapter.Reference
            ? AsyncRequest.RequestAdapterSync(instance, forceFallbackAdapter: true)
            : AsyncRequest.RequestAdapterSync(instance, preference: PowerPreference.HighPerformance);
        if (result.Status != RequestAdapterStatus.Success || result.Adapter.IsInvalid)
        {
            instance.Dispose();
            reason = $"no {kind} GPU adapter ({result.Status}: {result.Message})";
            return null;
        }
        bool software = result.Adapter.GetDescription().IsSoftware;
        if (software != (kind == ParityAdapter.Reference))
        {
            result.Adapter.Dispose();
            instance.Dispose();
            reason = kind == ParityAdapter.Reference ? "the fallback adapter is not a software rasterizer" : "only a software adapter is available";
            return null;
        }

        DeviceDescriptor descriptor = default;
        ValidationBridge.ConfigureDeviceDescriptor(&descriptor);
        var deviceResult = AsyncRequest.RequestDeviceSync(instance, result.Adapter, &descriptor);
        if (deviceResult.Status != RequestDeviceStatus.Success || deviceResult.Device.IsInvalid)
        {
            result.Adapter.Dispose();
            instance.Dispose();
            reason = $"no GPU device ({deviceResult.Status})";
            return null;
        }
        reason = string.Empty;
        return new ParityHarness(instance, result.Adapter, deviceResult.Device, width, height);
    }

    /// <summary>The composer whose atlases the GPU half builds against.</summary>
    public GpuComposer Gpu => gpu;

    /// <summary>Pages the CPU half's mask atlas holds.</summary>
    public int MaskPages => cpu.Masks.PageCount;

    /// <summary>Clip-table entries with a mask in the last CPU frame.</summary>
    public int CpuMaskedClips
    {
        get
        {
            int n = 0;
            foreach (ref readonly var clip in cpuList.Clips)
            {
                n += clip.HasMask != 0 ? 1 : 0;
            }
            return n;
        }
    }

    /// <summary>Masks the last CPU frame's builder dropped for lack of atlas space.</summary>
    public int CpuDroppedMasks { get; private set; }

    /// <summary>Renders on both composers; returns RGBA8 frames (GPU, CPU).</summary>
    public (byte[] Gpu, byte[] Cpu) Render(DrawRecording recording, uint width, uint height, ComposeParameters parameters)
    {
        return (RenderGpu(recording, width, height, parameters), RenderCpu(recording, width, height, parameters));
    }

    public byte[] RenderCpu(DrawRecording recording, uint width, uint height, ComposeParameters parameters)
    {
        builder.Begin(cpuList, width, height, cpu.Masks, cpu.MonoAtlas, cpu.ColorAtlas);
        builder.Replay(recording);
        builder.End();
        CpuDroppedMasks = builder.DroppedMasks;
        cpuList.Parameters = parameters;
        cpu.Render(cpuList, framebuffer);
        var rgba = new byte[width * height * 4];
        framebuffer.CopyToRgba(rgba);
        return rgba;
    }

    public byte[] RenderGpu(DrawRecording recording, uint width, uint height, ComposeParameters parameters)
    {
        builder.Begin(gpuList, width, height, gpu.Masks, gpu.MonoAtlas, gpu.ColorAtlas);
        builder.Replay(recording);
        builder.End();
        gpuList.Parameters = parameters;
        gpu.Resize(width, height);

        using var target = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8UnormSrgb,
            Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.CopySrc),
            Dimension = TextureDimension.D2,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        using var view = target.CreateView();
        uint rowBytes = width * 4;
        uint alignedRow = (rowBytes + 255u) & ~255u;
        ulong size = alignedRow * height;
        using var readback = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
            Size = size,
        });

        using (var encoder = device.CreateCommandEncoder())
        {
            gpu.Encode(encoder, target, view, gpuList);
            var src = new WGPUTexelCopyTextureInfo { Aspect = (uint)TextureAspect.All, MipLevel = 0, Origin = default, Texture = target.Handle };
            var dst = new WGPUTexelCopyBufferInfo
            {
                Layout = new WGPUTexelCopyBufferLayout { Offset = 0, BytesPerRow = alignedRow, RowsPerImage = height },
                Buffer = readback.Handle,
            };
            var extent = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 };
            WebGPU.CommandEncoderCopyTextureToBuffer(encoder.Handle, (nint)(&src), (nint)(&dst), (nint)(&extent));
            using var commands = encoder.Finish();
            Span<CommandBuffer> submit = stackalloc CommandBuffer[1];
            submit[0] = commands;
            device.Queue.Submit(submit);
        }

        if (!readback.MapSync(device, MapMode.Read, 0, size, timeoutMilliseconds: 10_000))
        {
            throw new InvalidOperationException("GPU readback timed out");
        }
        var rgba = new byte[width * height * 4];
        var mapped = readback.GetConstMappedRange(0, size);
        for (int y = 0; y < height; y++)
        {
            mapped.Slice((int)(y * alignedRow), (int)rowBytes).CopyTo(rgba.AsSpan((int)(y * rowBytes), (int)rowBytes));
        }
        readback.Unmap();
        return rgba;
    }

    /// <summary>Writes actual | expected | diff panels for a failed comparison; returns the path.</summary>
    public static string WriteDiff(string name, byte[] gpuFrame, byte[] cpuFrame, int width, int height)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "parity-failures");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{name}.png");
        PixelDiffPngWriter.Write4PanelPng(path, cpuFrame, gpuFrame, width, height);
        ImageWriter.WriteRgbaToPng(Path.Combine(dir, $"{name}-gpu.png"), gpuFrame, width, height);
        ImageWriter.WriteRgbaToPng(Path.Combine(dir, $"{name}-cpu.png"), cpuFrame, width, height);
        return path;
    }

    public void Dispose()
    {
        gpu.Dispose();
        cpu.Dispose();
        device.Dispose();
        adapter.Dispose();
        instance.Dispose();
    }
}
