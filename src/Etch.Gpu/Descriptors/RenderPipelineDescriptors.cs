using System;
using System.Runtime.InteropServices;
using System.Text;
using Etch.Gpu.Native;

namespace Etch.Gpu.Descriptors;

// ═══════════════════════════════════════════════════════════════════════════
// Render-pipeline descriptor tree (v29).
//
//  • StepMode moved OUT of VertexState INTO VertexBufferLayout in v29.
//  • BlendState is two WGPUBlendComponent { operation, srcFactor, dstFactor }.
//  • PrimitiveState gained StripIndexFormat and Unclipped-depth.
//  • DepthStencil / Fragment are pointers; Vertex / Primitive / Multisample
//    are inlined by value.
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
public struct ConstantEntry
{
    public IntPtr NextInChain;
    public IntPtr Key;
    public uint Value;
}

[StructLayout(LayoutKind.Sequential)]
public struct VertexAttribute
{
    public IntPtr NextInChain;
    public VertexFormat Format;
    public ulong Offset;
    public uint ShaderLocation;
}

[StructLayout(LayoutKind.Sequential)]
public struct VertexBufferLayout
{
    public IntPtr NextInChain;
    public VertexStepMode StepMode;
    public ulong ArrayStride;
    public UIntPtr AttributeCount;
    public IntPtr Attributes;            // WGPUVertexAttribute const*
}

[StructLayout(LayoutKind.Sequential)]
public struct VertexState
{
    public IntPtr NextInChain;
    public ShaderModuleHandle Module;
    public StringView EntryPoint;
    public UIntPtr ConstantCount;
    public IntPtr Constants;             // WGPUConstantEntry const*
    public UIntPtr BufferCount;
    public IntPtr Buffers;               // WGPUVertexBufferLayout const*
}

[StructLayout(LayoutKind.Sequential)]
public struct PrimitiveState
{
    public IntPtr NextInChain;
    public PrimitiveTopology Topology;
    public IndexFormat StripIndexFormat; // v29: after Topology, before FrontFace
    public FrontFace FrontFace;
    public CullMode CullMode;
    public uint UnclippedDepth;          // WGPUBool
}

[StructLayout(LayoutKind.Sequential)]
public struct DepthStencilState
{
    public IntPtr NextInChain;
    public TextureFormat Format;
    public uint DepthWriteEnabled;       // WGPUOptionalBool (0=false, 1=true, 2=undef)
    public CompareFunction DepthCompare;
    public StencilFaceState StencilFront;
    public StencilFaceState StencilBack;
    public uint StencilReadMask;
    public uint StencilWriteMask;
    public int DepthBias;
    public float DepthBiasSlopeScale;
    public float DepthBiasClamp;
}

[StructLayout(LayoutKind.Sequential)]
public struct StencilFaceState
{
    public CompareFunction Compare;
    public StencilOperation FailOp;
    public StencilOperation DepthFailOp;
    public StencilOperation PassOp;
}

public enum StencilOperation : uint
{
    Undefined = 0,
    Keep = 1,
    Zero = 2,
    Replace = 3,
    Invert = 4,
    IncrementClamp = 5,
    DecrementClamp = 6,
    IncrementWrap = 7,
    DecrementWrap = 8,
}

[StructLayout(LayoutKind.Sequential)]
public struct MultisampleState
{
    public IntPtr NextInChain;
    public uint Count;
    public uint Mask;
    public uint AlphaToCoverageEnabled;  // WGPUBool

    public MultisampleState()
    {
        Count = 1;
        Mask = 0xFFFFFFFFu;
    }
}

// WGPUBlendComponent { operation, srcFactor, dstFactor }.
[StructLayout(LayoutKind.Sequential)]
public struct BlendComponent
{
    public BlendOperation Operation;
    public BlendFactor SrcFactor;
    public BlendFactor DstFactor;
}

// WGPUBlendState { color, alpha }.
[StructLayout(LayoutKind.Sequential)]
public struct BlendState
{
    public BlendComponent Color;
    public BlendComponent Alpha;
}

[StructLayout(LayoutKind.Sequential)]
public struct ColorTargetState
{
    public IntPtr NextInChain;
    public TextureFormat Format;
    public IntPtr Blend;                 // WGPUBlendState const* (nullable for no-blend)
    public ulong WriteMask;              // WGPUColorWriteMask (WGPUFlags u64)
}

[StructLayout(LayoutKind.Sequential)]
public struct FragmentState
{
    public IntPtr NextInChain;
    public ShaderModuleHandle Module;
    public StringView EntryPoint;
    public UIntPtr ConstantCount;
    public IntPtr Constants;             // WGPUConstantEntry const*
    public UIntPtr TargetCount;
    public IntPtr Targets;               // WGPUColorTargetState const*
}

[StructLayout(LayoutKind.Sequential)]
public struct RenderPipelineDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
    public PipelineLayoutHandle Layout;
    public VertexState Vertex;
    public PrimitiveState Primitive;
    public IntPtr DepthStencil;          // WGPUDepthStencilState const*
    public MultisampleState Multisample;
    public IntPtr Fragment;              // WGPUFragmentState const*
}

[StructLayout(LayoutKind.Sequential)]
public struct CommandEncoderDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
}

[StructLayout(LayoutKind.Sequential)]
public struct CommandBufferDescriptor
{
    public IntPtr NextInChain;
    public StringView Label;
}
