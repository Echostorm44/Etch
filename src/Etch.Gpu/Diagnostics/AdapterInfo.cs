using System;
using System.Buffers.Binary;
using System.Text;

namespace Etch.Gpu.Diagnostics;

// ═══════════════════════════════════════════════════════════════════════════
// AdapterInfo — a POD snapshot of the wgpu adapter + surface configuration at
// the moment of crash. Intentionally self-contained: a crash-dump reader must
// be able to interpret the bytes without re-binding to the live wgpu-native
// library. Hence we embed the backend/device/driver strings inline (length-
// prefixed UTF-8) rather than pointer-into-native.
//
// Binary layout (little-endian):
//   u32 BackendType        (matches Etch.Gpu.BackendType ordinal)
//   u32 AdapterType        (matches Etch.Gpu.AdapterType ordinal)
//   u32 VendorId
//   u32 DeviceId
//   u64 FeaturesBitmask    (feature bits; interpretation is build-version-tied)
//   u16 DeviceNameLen      + UTF-8 bytes (<= 256)
//   u16 DriverDescLen      + UTF-8 bytes (<= 256)
//   u16 BackendNameLen     + UTF-8 bytes (<= 64)
//
// SurfaceConfigInfo is stored in its own section and has an even simpler
// fixed layout — see SurfaceConfigInfo below.
// ═══════════════════════════════════════════════════════════════════════════

public readonly struct AdapterInfo
{
    public uint BackendType { get; }
    public uint AdapterType { get; }
    public uint VendorId { get; }
    public uint DeviceId { get; }
    public ulong FeaturesBitmask { get; }
    public string DeviceName { get; }
    public string DriverDescription { get; }
    public string BackendName { get; }

    public AdapterInfo(
        uint backendType,
        uint adapterType,
        uint vendorId,
        uint deviceId,
        ulong featuresBitmask,
        string deviceName,
        string driverDescription,
        string backendName)
    {
        BackendType = backendType;
        AdapterType = adapterType;
        VendorId = vendorId;
        DeviceId = deviceId;
        FeaturesBitmask = featuresBitmask;
        DeviceName = deviceName ?? string.Empty;
        DriverDescription = driverDescription ?? string.Empty;
        BackendName = backendName ?? string.Empty;
    }

    public const int MaxDeviceNameBytes = 256;
    public const int MaxDriverDescBytes = 256;
    public const int MaxBackendNameBytes = 64;

    /// <summary>
    /// Upper-bound envelope size for a freshly built adapter info. Caller-supplied
    /// destination must be at least this large to avoid truncation.
    /// </summary>
    public const int MaxEncodedSize =
        4 + 4 + 4 + 4 + 8 /* scalars */
      + 2 + MaxDeviceNameBytes
      + 2 + MaxDriverDescBytes
      + 2 + MaxBackendNameBytes;

    /// <summary>
    /// Encodes this adapter info into a little-endian byte blob. Returns the number of
    /// bytes written. Strings that exceed their budget are truncated at a UTF-8 boundary
    /// (never in the middle of a code point).
    /// </summary>
    public int Encode(Span<byte> destination)
    {
        if (destination.Length < MaxEncodedSize)
        {
            Panic.ArgumentOutOfRange(nameof(destination), "Destination too small for AdapterInfo.MaxEncodedSize.");
        }

        int pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), BackendType); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), AdapterType); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), VendorId); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), DeviceId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(pos, 8), FeaturesBitmask); pos += 8;

        pos += EncodeString(DeviceName, MaxDeviceNameBytes, destination.Slice(pos));
        pos += EncodeString(DriverDescription, MaxDriverDescBytes, destination.Slice(pos));
        pos += EncodeString(BackendName, MaxBackendNameBytes, destination.Slice(pos));

        return pos;
    }

    private static int EncodeString(string value, int maxBytes, Span<byte> destination)
    {
        Span<byte> scratch = stackalloc byte[maxBytes];
        int utf8Len = SafeGetUtf8Bytes(value, scratch, maxBytes);

        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), (ushort)utf8Len);
        scratch.Slice(0, utf8Len).CopyTo(destination.Slice(2));
        return 2 + utf8Len;
    }

    // Encodes as many complete UTF-8 code points as fit into `budget` bytes.
    private static int SafeGetUtf8Bytes(string value, Span<byte> destination, int budget)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        int charsConsumed = Encoding.UTF8.GetByteCount(value) <= budget
            ? value.Length
            : FindMaxCharsThatFit(value, budget);

        return Encoding.UTF8.GetBytes(value.AsSpan(0, charsConsumed), destination);
    }

    private static int FindMaxCharsThatFit(string value, int budget)
    {
        int lo = 0, hi = value.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >>> 1;
            int needed = Encoding.UTF8.GetByteCount(value.AsSpan(0, mid));
            if (needed <= budget) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    public static bool TryDecode(ReadOnlySpan<byte> source, out AdapterInfo info)
    {
        info = default;
        if (source.Length < 24) return false;

        int pos = 0;
        uint backend = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(pos, 4)); pos += 4;
        uint adapter = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(pos, 4)); pos += 4;
        uint vendor = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(pos, 4)); pos += 4;
        uint device = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(pos, 4)); pos += 4;
        ulong features = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(pos, 8)); pos += 8;

        if (!TryDecodeString(source, ref pos, out string deviceName)) return false;
        if (!TryDecodeString(source, ref pos, out string driverDesc)) return false;
        if (!TryDecodeString(source, ref pos, out string backendName)) return false;

        info = new AdapterInfo(backend, adapter, vendor, device, features, deviceName, driverDesc, backendName);
        return true;
    }

    private static bool TryDecodeString(ReadOnlySpan<byte> source, ref int pos, out string value)
    {
        value = string.Empty;
        if (pos + 2 > source.Length) return false;
        int len = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(pos, 2));
        pos += 2;
        if (pos + len > source.Length) return false;
        value = Encoding.UTF8.GetString(source.Slice(pos, len));
        pos += len;
        return true;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// SurfaceConfigInfo — fixed 24-byte snapshot of swapchain state at crash.
//   u32 Format, u32 Width, u32 Height, u32 PresentMode, u32 AlphaMode, u32 Usage
// (All enum values are the raw wgpu ordinals; Usage is truncated to u32 since
// the real WGPUFlags are u64 but the lower bits are the only meaningful ones
// for surface usages in v29.)
// ═══════════════════════════════════════════════════════════════════════════

public readonly struct SurfaceConfigInfo
{
    public uint Format { get; }
    public uint Width { get; }
    public uint Height { get; }
    public uint PresentMode { get; }
    public uint AlphaMode { get; }
    public uint Usage { get; }

    public SurfaceConfigInfo(uint format, uint width, uint height, uint presentMode, uint alphaMode, uint usage)
    {
        Format = format;
        Width = width;
        Height = height;
        PresentMode = presentMode;
        AlphaMode = alphaMode;
        Usage = usage;
    }

    public const int EncodedSize = 24;

    public int Encode(Span<byte> destination)
    {
        if (destination.Length < EncodedSize)
        {
            Panic.ArgumentOutOfRange(nameof(destination), "Destination too small for SurfaceConfigInfo.EncodedSize.");
        }

        int pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), Format); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), Width); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), Height); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), PresentMode); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), AlphaMode); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pos, 4), Usage); pos += 4;
        return pos;
    }

    public static bool TryDecode(ReadOnlySpan<byte> source, out SurfaceConfigInfo info)
    {
        info = default;
        if (source.Length < EncodedSize) return false;

        uint format = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4));
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4));
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8, 4));
        uint present = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(12, 4));
        uint alpha = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(16, 4));
        uint usage = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(20, 4));

        info = new SurfaceConfigInfo(format, width, height, present, alpha, usage);
        return true;
    }
}
