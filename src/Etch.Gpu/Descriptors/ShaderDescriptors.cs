using System;
using System.Runtime.InteropServices;

namespace Etch.Gpu.Descriptors;

// ═══════════════════════════════════════════════════════════════════════════
// Shader module descriptor (v29).
//
// WGSL source does NOT live on WGPUShaderModuleDescriptor in v29. Instead,
// the descriptor carries a chained WGPUShaderSourceWGSL { chain, code } where
// chain.sType == 0x02 (SType.ShaderSourceWGSL). `code` is a StringView into
// the WGSL bytes; lifetime must outlive wgpuDeviceCreateShaderModule().
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
public struct ShaderModuleDescriptor
{
    public IntPtr NextInChain;           // WGPUChainedStruct const* (→ ShaderSourceWGSL or SPIRV)
    public StringView Label;
}

// WGPUShaderSourceWGSL. Chain.sType must be WGPUSType.ShaderSourceWGSL = 0x02.
[StructLayout(LayoutKind.Sequential)]
public struct ShaderSourceWGSL
{
    public ChainedStruct Chain;          // { NextInChain, SType }
    public StringView Code;
}

// ═══════════════════════════════════════════════════════════════════════════
// Sampler (v29). MipmapFilter is its own enum now.
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
public struct SamplerDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public AddressMode AddressModeU;
    public AddressMode AddressModeV;
    public AddressMode AddressModeW;
    public FilterMode MagFilter;
    public FilterMode MinFilter;
    public MipmapFilterMode MipmapFilter;
    public float LodMinClamp;
    public float LodMaxClamp;
    public CompareFunction Compare;
    public ushort MaxAnisotropy;

    public SamplerDescriptor()
    {
        MaxAnisotropy = 1;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Texture / TextureView (v29).
//
// WGPUTextureDescriptor v29:
//   NextInChain, Label, Usage (u64), Dimension, Size (extent3D),
//   Format, MipLevelCount, SampleCount, ViewFormatCount, ViewFormats.
//
// WGPUTextureViewDescriptor v29 adds trailing Usage (u64).
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
public struct Extent3D
{
    public uint Width;
    public uint Height;
    public uint DepthOrArrayLayers;
}

[StructLayout(LayoutKind.Sequential)]
public struct TextureDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public ulong Usage;                  // WGPUTextureUsage (WGPUFlags u64)
    public TextureDimension Dimension;
    public Extent3D Size;
    public TextureFormat Format;
    public uint MipLevelCount;
    public uint SampleCount;
    public UIntPtr ViewFormatCount;
    public IntPtr ViewFormats;           // WGPUTextureFormat const*

    public TextureDescriptor()
    {
        MipLevelCount = 1;
        SampleCount = 1;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct TextureViewDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public TextureFormat Format;
    public TextureViewDimension Dimension;
    public uint BaseMipLevel;
    public uint MipLevelCount;
    public uint BaseArrayLayer;
    public uint ArrayLayerCount;
    public TextureAspect Aspect;
    public ulong Usage;                  // v29 addition (WGPUFlags u64)
}
