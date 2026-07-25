using System;

namespace Etch.Gpu;

// ═══════════════════════════════════════════════════════════════════════════
// WebGPU enum definitions mirroring wgpu-native v29 (webgpu.h, wgpu.h).
//
// Conventions:
//   • All plain enums begin with Undefined = 0 unless noted. v29 uses 32-bit
//     enum values.
//   • Bind-related *BindingType enums use BindingNotUsed = 0, Undefined = 1.
//   • Flag enums are typed `: uint` here for ergonomics, but every struct
//     field that holds one must be declared `ulong` because v29 widens
//     WGPUFlags to uint64_t.
//   • Values below are copied literally from webgpu.h v29 and wgpu.h v29.
// ═══════════════════════════════════════════════════════════════════════════

// ─── Flags (WGPUFlags = uint64_t in v29, stored as ulong in structs) ──────

[Flags]
public enum BufferUsage : uint
{
    None = 0,
    MapRead = 1u << 0,
    MapWrite = 1u << 1,
    CopySrc = 1u << 2,
    CopyDst = 1u << 3,
    Index = 1u << 4,
    Vertex = 1u << 5,
    Uniform = 1u << 6,
    Storage = 1u << 7,
    Indirect = 1u << 8,
    QueryResolve = 1u << 9,
}

[Flags]
public enum TextureUsage : uint
{
    None = 0,
    CopySrc = 1u << 0,
    CopyDst = 1u << 1,
    TextureBinding = 1u << 2,
    StorageBinding = 1u << 3,
    RenderAttachment = 1u << 4,
}

[Flags]
public enum ShaderStage : uint
{
    None = 0,
    Vertex = 1u << 0,
    Fragment = 1u << 1,
    Compute = 1u << 2,
}

[Flags]
public enum ColorWriteMask : uint
{
    None = 0,
    Red = 1u << 0,
    Green = 1u << 1,
    Blue = 1u << 2,
    Alpha = 1u << 3,
    All = Red | Green | Blue | Alpha,
}

[Flags]
public enum MapMode : uint
{
    None = 0,
    Read = 1u << 0,
    Write = 1u << 1,
}

// wgpu.h native extension flags.
[Flags]
public enum InstanceBackend : uint
{
    All = 0,
    Vulkan = 1u << 0,
    GL = 1u << 1,
    Metal = 1u << 2,
    DX12 = 1u << 3,
    DX11 = 1u << 4,
    BrowserWebGPU = 1u << 5,
    Primary = Vulkan | Metal | DX12 | BrowserWebGPU,
    Secondary = GL | DX11,
}

[Flags]
public enum InstanceFlag : uint
{
    Default = 0,
    Debug = 1u << 0,
    Validation = 1u << 1,
    DiscardHalLabels = 1u << 2,
}

// ─── Plain enums (Undefined = 0) ──────────────────────────────────────────

public enum TextureDimension : uint
{
    Undefined = 0,
    D1 = 1,
    D2 = 2,
    D3 = 3,
}

public enum TextureViewDimension : uint
{
    Undefined = 0,
    D1 = 1,
    D2 = 2,
    D2Array = 3,
    Cube = 4,
    CubeArray = 5,
    D3 = 6,
}

public enum TextureAspect : uint
{
    Undefined = 0,
    All = 1,
    StencilOnly = 2,
    DepthOnly = 3,
    Plane0Only = 4,
    Plane1Only = 5,
    Plane2Only = 6,
}

public enum AddressMode : uint
{
    Undefined = 0,
    ClampToEdge = 1,
    Repeat = 2,
    MirrorRepeat = 3,
}

public enum FilterMode : uint
{
    Undefined = 0,
    Nearest = 1,
    Linear = 2,
}

public enum MipmapFilterMode : uint
{
    Undefined = 0,
    Nearest = 1,
    Linear = 2,
}

public enum CompareFunction : uint
{
    Undefined = 0,
    Never = 1,
    Less = 2,
    Equal = 3,
    LessEqual = 4,
    Greater = 5,
    NotEqual = 6,
    GreaterEqual = 7,
    Always = 8,
}

public enum IndexFormat : uint
{
    Undefined = 0,
    Uint16 = 1,
    Uint32 = 2,
}

public enum VertexFormat : uint
{
    // v29 removed the Undefined=0 slot here; values start at 1.
    Uint8 = 1,
    Uint8x2 = 2,
    Uint8x4 = 3,
    Sint8 = 4,
    Sint8x2 = 5,
    Sint8x4 = 6,
    Unorm8 = 7,
    Unorm8x2 = 8,
    Unorm8x4 = 9,
    Snorm8 = 10,
    Snorm8x2 = 11,
    Snorm8x4 = 12,
    Uint16 = 13,
    Uint16x2 = 14,
    Uint16x4 = 15,
    Sint16 = 16,
    Sint16x2 = 17,
    Sint16x4 = 18,
    Unorm16 = 19,
    Unorm16x2 = 20,
    Unorm16x4 = 21,
    Snorm16 = 22,
    Snorm16x2 = 23,
    Snorm16x4 = 24,
    Float16 = 25,
    Float16x2 = 26,
    Float16x4 = 27,
    Float32 = 28,
    Float32x2 = 29,
    Float32x3 = 30,
    Float32x4 = 31,
    Uint32 = 32,
    Uint32x2 = 33,
    Uint32x3 = 34,
    Uint32x4 = 35,
    Sint32 = 36,
    Sint32x2 = 37,
    Sint32x3 = 38,
    Sint32x4 = 39,
    Unorm1010102 = 40,
    Unorm8x4BGRA = 41,
}

public enum VertexStepMode : uint
{
    Undefined = 0,
    Vertex = 1,
    Instance = 2,
}

public enum PrimitiveTopology : uint
{
    Undefined = 0,
    PointList = 1,
    LineList = 2,
    LineStrip = 3,
    TriangleList = 4,
    TriangleStrip = 5,
}

public enum FrontFace : uint
{
    Undefined = 0,
    Ccw = 1,
    Cw = 2,
}

public enum CullMode : uint
{
    Undefined = 0,
    None = 1,
    Front = 2,
    Back = 3,
}

public enum BlendFactor : uint
{
    Undefined = 0,
    Zero = 1,
    One = 2,
    Src = 3,
    OneMinusSrc = 4,
    SrcAlpha = 5,
    OneMinusSrcAlpha = 6,
    Dst = 7,
    OneMinusDst = 8,
    DstAlpha = 9,
    OneMinusDstAlpha = 10,
    SrcAlphaSaturated = 11,
    Constant = 12,
    OneMinusConstant = 13,
    Src1 = 14,
    OneMinusSrc1 = 15,
    Src1Alpha = 16,
    OneMinusSrc1Alpha = 17,
}

public enum BlendOperation : uint
{
    Undefined = 0,
    Add = 1,
    Subtract = 2,
    ReverseSubtract = 3,
    Min = 4,
    Max = 5,
}

public enum StoreOp : uint
{
    Undefined = 0,
    Store = 1,
    Discard = 2,
}

public enum LoadOp : uint
{
    Undefined = 0,
    Load = 1,
    Clear = 2,
}

public enum PowerPreference : uint
{
    Undefined = 0,
    LowPower = 1,
    HighPerformance = 2,
}

public enum BackendType : uint
{
    Undefined = 0,
    Null = 1,
    WebGPU = 2,
    D3D11 = 3,
    D3D12 = 4,
    Metal = 5,
    Vulkan = 6,
    OpenGL = 7,
    OpenGLES = 8,
}

public enum FeatureLevel : uint
{
    Undefined = 0,
    Compatibility = 1,
    Core = 2,
}

public enum AdapterType : uint
{
    DiscreteGPU = 1,
    IntegratedGPU = 2,
    CPU = 3,
    Unknown = 4,
}

public enum DeviceLostReason : uint
{
    Unknown = 1,
    Destroyed = 2,
    CallbackCancelled = 3,
    FailedCreation = 4,
}

public enum CallbackMode : uint
{
    WaitAnyOnly = 1,
    AllowProcessEvents = 2,
    AllowSpontaneous = 3,
}

public enum ErrorType : uint
{
    NoError = 1,
    Validation = 2,
    OutOfMemory = 3,
    Internal = 4,
    Unknown = 5,
}

public enum RequestAdapterStatus : uint
{
    Success = 1,
    CallbackCancelled = 2,
    Unavailable = 3,
    Error = 4,
}

public enum RequestDeviceStatus : uint
{
    Success = 1,
    CallbackCancelled = 2,
    Error = 3,
}

// ─── *BindingType family: BindingNotUsed = 0, Undefined = 1 ────────────────

public enum BufferBindingType : uint
{
    BindingNotUsed = 0,
    Undefined = 1,
    Uniform = 2,
    Storage = 3,
    ReadOnlyStorage = 4,
}

public enum SamplerBindingType : uint
{
    BindingNotUsed = 0,
    Undefined = 1,
    Filtering = 2,
    NonFiltering = 3,
    Comparison = 4,
}

public enum TextureSampleType : uint
{
    BindingNotUsed = 0,
    Undefined = 1,
    Float = 2,
    UnfilterableFloat = 3,
    Depth = 4,
    Sint = 5,
    Uint = 6,
}

public enum StorageTextureAccess : uint
{
    BindingNotUsed = 0,
    Undefined = 1,
    WriteOnly = 2,
    ReadOnly = 3,
    ReadWrite = 4,
}

// ─── Texture formats (WGPUTextureFormat v29, webgpu.h) ─────────────────────
// 0 = Undefined. Numeric gaps below are intentional and mirror v29 exactly.

public enum TextureFormat : uint
{
    Undefined = 0,
    R8Unorm = 1,
    R8Snorm = 2,
    R8Uint = 3,
    R8Sint = 4,
    R16Unorm = 5,
    R16Snorm = 6,
    R16Uint = 7,
    R16Sint = 8,
    R16Float = 9,
    Rg8Unorm = 10,
    Rg8Snorm = 11,
    Rg8Uint = 12,
    Rg8Sint = 13,
    R32Float = 14,
    R32Uint = 15,
    R32Sint = 16,
    Rg16Unorm = 17,
    Rg16Snorm = 18,
    Rg16Uint = 19,
    Rg16Sint = 20,
    Rg16Float = 21,
    Rgba8Unorm = 22,
    Rgba8UnormSrgb = 23,
    Rgba8Snorm = 24,
    Rgba8Uint = 25,
    Rgba8Sint = 26,
    Bgra8Unorm = 27,
    Bgra8UnormSrgb = 28,
    Rgb10A2Uint = 29,
    Rgb10A2Unorm = 30,
    Rg11B10Ufloat = 31,
    Rgb9E5Ufloat = 32,
    Rg32Float = 33,
    Rg32Uint = 34,
    Rg32Sint = 35,
    Rgba16Unorm = 36,
    Rgba16Snorm = 37,
    Rgba16Uint = 38,
    Rgba16Sint = 39,
    Rgba16Float = 40,
    Rgba32Float = 41,
    Rgba32Uint = 42,
    Rgba32Sint = 43,
    Stencil8 = 44,
    Depth16Unorm = 45,
    Depth24Plus = 46,
    Depth24PlusStencil8 = 47,
    Depth32Float = 48,
    Depth32FloatStencil8 = 49,
    // BC / ETC2 / ASTC formats intentionally omitted until needed.
}

// ─── Present mode / alpha mode / surface texture status (v29) ─────────────

public enum PresentMode : uint
{
    Undefined = 0,
    Fifo = 1,
    FifoRelaxed = 2,
    Immediate = 3,
    Mailbox = 4,
}

public enum CompositeAlphaMode : uint
{
    Auto = 0,
    Opaque = 1,
    Premultiplied = 2,
    Unpremultiplied = 3,
    Inherit = 4,
}

public enum SurfaceGetCurrentTextureStatus : uint
{
    SuccessOptimal = 1,
    SuccessSuboptimal = 2,
    Timeout = 3,
    Outdated = 4,
    Lost = 5,
    OutOfMemory = 6,
    DeviceLost = 7,
    Error = 8,
}
