using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Native;

namespace Etch.Gpu.Descriptors;

// ═══════════════════════════════════════════════════════════════════════════
// Bind-group layout entries (v29).
//
//  • Visibility is u64 (WGPUFlags).
//  • v29 added BindingArraySize (u32) right after Visibility.
//  • The four sub-layouts are inlined by value; each *BindingType is
//    u32 enum where 0 = BindingNotUsed, 1 = Undefined.
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
public struct BindGroupLayoutEntry
{
    public IntPtr NextInChain;           // WGPUChainedStruct const*
    public uint Binding;
    public ulong Visibility;             // WGPUShaderStage (WGPUFlags u64)
    public uint BindingArraySize;        // v29 addition
    public BufferBindingLayout Buffer;
    public SamplerBindingLayout Sampler;
    public TextureBindingLayout Texture;
    public StorageTextureBindingLayout StorageTexture;
}

[StructLayout(LayoutKind.Sequential)]
public struct BufferBindingLayout
{
    public IntPtr NextInChain;
    public BufferBindingType Type;
    public uint HasDynamicOffset;        // WGPUBool
    public ulong MinBindingSize;
}

[StructLayout(LayoutKind.Sequential)]
public struct SamplerBindingLayout
{
    public IntPtr NextInChain;
    public SamplerBindingType Type;
}

[StructLayout(LayoutKind.Sequential)]
public struct TextureBindingLayout
{
    public IntPtr NextInChain;
    public TextureSampleType SampleType;
    public TextureViewDimension ViewDimension;
    public uint Multisampled;            // WGPUBool
}

[StructLayout(LayoutKind.Sequential)]
public struct StorageTextureBindingLayout
{
    public IntPtr NextInChain;
    public StorageTextureAccess Access;
    public TextureFormat Format;
    public TextureViewDimension ViewDimension;
}

[StructLayout(LayoutKind.Sequential)]
public struct BindGroupLayoutDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public UIntPtr EntryCount;
    public IntPtr Entries;               // WGPUBindGroupLayoutEntry const*
}

// WGPUPipelineLayoutDescriptor v29 adds trailing ImmediateSize (u32).
[StructLayout(LayoutKind.Sequential)]
public struct PipelineLayoutDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public UIntPtr BindGroupLayoutCount;
    public IntPtr BindGroupLayouts;      // WGPUBindGroupLayout const*
    public uint ImmediateSize;           // v29 addition
}

// WGPUBindGroupEntry v29 is flat, not {Binding, Resource}.
[StructLayout(LayoutKind.Sequential)]
public struct BindGroupEntry
{
    public IntPtr NextInChain;
    public uint Binding;
    public IntPtr Buffer;                // WGPUBuffer (nullable)
    public ulong Offset;
    public ulong Size;                   // Use ulong.MaxValue for WGPU_WHOLE_SIZE
    public IntPtr Sampler;               // WGPUSampler (nullable)
    public IntPtr TextureView;           // WGPUTextureView (nullable)
}

[StructLayout(LayoutKind.Sequential)]
public struct BindGroupDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public BindGroupLayoutHandle Layout;
    public UIntPtr EntryCount;
    public IntPtr Entries;               // WGPUBindGroupEntry const*
}

// ═══════════════════════════════════════════════════════════════════════════
// Render pass state (v29).
//   • ColorAttachment adds DepthSlice (u32) after View.
//   • Color / DepthStencil use the 16-byte-aligned Color struct.
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
public struct Color
{
    public double R;
    public double G;
    public double B;
    public double A;
}

[StructLayout(LayoutKind.Sequential)]
public struct RenderPassColorAttachment
{
    public IntPtr NextInChain;
    public IntPtr View;                  // WGPUTextureView (nullable)
    public uint DepthSlice;              // v29 addition; use 0xFFFFFFFF when unused
    public IntPtr ResolveTarget;
    public LoadOp LoadOp;
    public StoreOp StoreOp;
    public Color ClearValue;
}

[StructLayout(LayoutKind.Sequential)]
public struct RenderPassDepthStencilAttachment
{
    public IntPtr View;
    public LoadOp DepthLoadOp;
    public StoreOp DepthStoreOp;
    public float DepthClearValue;
    public uint DepthReadOnly;           // WGPUBool
    public LoadOp StencilLoadOp;
    public StoreOp StencilStoreOp;
    public uint StencilClearValue;
    public uint StencilReadOnly;         // WGPUBool
}

[StructLayout(LayoutKind.Sequential)]
public struct RenderPassDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public UIntPtr ColorAttachmentCount;
    public IntPtr ColorAttachments;      // WGPURenderPassColorAttachment const*
    public IntPtr DepthStencilAttachment;// WGPURenderPassDepthStencilAttachment const*
    public IntPtr OcclusionQuerySet;     // WGPUQuerySet
    public IntPtr TimestampWrites;       // WGPURenderPassTimestampWrites const*
}
