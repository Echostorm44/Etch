using System;

namespace Etch;

/// <summary>
/// Stable identifier for a panic or exception condition. Backed by a string of the form
/// <c>ET-P-####</c> where <c>####</c> is a zero-padded sequential number. Codes are the
/// primary key shared by logs, crash dumps, the scene-reproducer verifier, and support
/// diagnostics, so they must remain stable once allocated.
/// </summary>
/// <remarks>
/// <see cref="PanicCode"/> is a thin wrapper over <see cref="string"/> rather than an
/// enum so that the registry in <see cref="PanicCodes"/> can grow without recompiling
/// every downstream assembly, and so that codes survive round-trips through text logs
/// and structured telemetry without losing their exact spelling.
/// </remarks>
public readonly struct PanicCode : IEquatable<PanicCode>
{
    /// <summary>The <c>ET-P-####</c> identifier.</summary>
    public string Value { get; }

    /// <summary>Wraps the specified code string. Callers should use the constants on
    /// <see cref="PanicCodes"/> rather than constructing codes ad-hoc.</summary>
    public PanicCode(string value)
    {
        Value = value;
    }

    public bool Equals(PanicCode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is PanicCode other && Equals(other);
    public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(PanicCode left, PanicCode right) => left.Equals(right);
    public static bool operator !=(PanicCode left, PanicCode right) => !left.Equals(right);
}

/// <summary>
/// Central registry of allocated panic codes. Every code used anywhere in Etch must
/// appear here; the living catalogue of meanings lives in <c>docs/00-overview/panic-codes.md</c>
/// and is the single source of truth for human-readable context.
/// </summary>
/// <remarks>
/// When adding a code: append a constant here, append a row to <c>panic-codes.md</c>, and
/// leave the numeric suffix monotonically increasing. Never re-use a retired number.
/// </remarks>
public static class PanicCodes
{
    /// <summary>ET-P-0001 — write past the end of a fixed-size buffer.</summary>
    public static readonly PanicCode BufferOverflow = new("ET-P-0001");

    /// <summary>ET-P-0002 — managed allocation detected inside an <c>AllocGuard</c> scope.</summary>
    public static readonly PanicCode UnexpectedAllocation = new("ET-P-0002");

    /// <summary>ET-P-0003 — generic invariant violation with caller-supplied message.</summary>
    public static readonly PanicCode InvariantViolation = new("ET-P-0003");

    /// <summary>ET-P-0004 — a non-null parameter contract received <see langword="null"/>.</summary>
    public static readonly PanicCode ArgumentNull = new("ET-P-0004");

    /// <summary>ET-P-0005 — an argument fell outside its declared range.</summary>
    public static readonly PanicCode ArgumentOutOfRange = new("ET-P-0005");

    /// <summary>ET-P-0006 — operation invoked on a type in a state that forbids it (post-dispose, pre-init, etc.).</summary>
    public static readonly PanicCode InvalidState = new("ET-P-0006");

    /// <summary>ET-P-0007 — a declared feature path is not implemented on this build/platform.</summary>
    public static readonly PanicCode NotImplemented = new("ET-P-0007");

    /// <summary>ET-P-0020 — scene-reproducer file version is newer than supported by the reader.</summary>
    public static readonly PanicCode SceneReproVersionMismatch = new("ET-P-0020");

    /// <summary>ET-P-0200 — wgpu validation error (validation layer caught a usage mistake).</summary>
    public static readonly PanicCode GpuValidation = new("ET-P-0200");

    /// <summary>ET-P-0201 — wgpu out-of-memory error (allocation exceeded device limits).</summary>
    public static readonly PanicCode GpuOutOfMemory = new("ET-P-0201");

    /// <summary>ET-P-0202 — wgpu internal error (bug in wgpu-native or driver).</summary>
    public static readonly PanicCode GpuInternal = new("ET-P-0202");

    /// <summary>ET-P-0203 — unknown wgpu error type.</summary>
    public static readonly PanicCode GpuUnknown = new("ET-P-0203");

    /// <summary>ET-P-0204 — reserved for future GPU error codes.</summary>
    public static readonly PanicCode GpuReserved = new("ET-P-0204");

    /// <summary>ET-P-0205 — surface outlives the instance it was created from.</summary>
    public static readonly PanicCode SurfaceOutlivesInstance = new("ET-P-0205");

    /// <summary>ET-P-0206 — a surface handle passed to the native layer was null.</summary>
    public static readonly PanicCode SurfaceHandleNull = new("ET-P-0206");

    /// <summary>ET-P-0207 — swap chain configured with zero or invalid dimensions.</summary>
    public static readonly PanicCode InvalidSwapChainSize = new("ET-P-0207");

    /// <summary>ET-P-0208 — a debug label exceeds the maximum 127-byte length.</summary>
    public static readonly PanicCode LabelTooLong = new("ET-P-0208");

    /// <summary>ET-P-0209 — timestamp queries are not supported by the current adapter.</summary>
    public static readonly PanicCode TimestampQueryUnsupported = new("ET-P-0209");

    /// <summary>ET-P-0210 — no GPU adapter available for off-screen rendering.</summary>
    public static readonly PanicCode GpuAdapterUnavailable = new("ET-P-0210");

    /// <summary>ET-P-0211 — wgpu device creation failed.</summary>
    public static readonly PanicCode GpuDeviceCreationFailed = new("ET-P-0211");

    /// <summary>ET-P-0212 — GPU buffer map timed out during read-back.</summary>
    public static readonly PanicCode GpuBufferMapTimeout = new("ET-P-0212");

    /// <summary>ET-P-0301 — a geometric operation received a degenerate input (e.g. normalising a zero-length vector).</summary>
    public static readonly PanicCode DegenerateVector = new("ET-P-0301");

    /// <summary>ET-P-0302 — an affine transform is singular and cannot be inverted.</summary>
    public static readonly PanicCode NonInvertibleAffine = new("ET-P-0302");

    /// <summary>ET-P-0303 — a Rect was constructed with MaxX &lt; MinX or MaxY &lt; MinY.</summary>
    public static readonly PanicCode InvertedRect = new("ET-P-0303");

    /// <summary>ET-P-0304 — a Circle was constructed with a negative radius.</summary>
    public static readonly PanicCode InvalidCircle = new("ET-P-0304");

    /// <summary>ET-P-0305 — a path verb that requires a prior MoveTo (e.g. LineTo) was emitted before any MoveTo.</summary>
    public static readonly PanicCode PathVerbWithoutMoveTo = new("ET-P-0305");

    /// <summary>ET-P-0306 — Build() was called more than once on the same BezPathBuilder.</summary>
    public static readonly PanicCode BuilderConsumed = new("ET-P-0306");

    /// <summary>ET-P-0307 — src/dst spans passed to a batch operation had mismatched lengths.</summary>
    public static readonly PanicCode SpanLengthMismatch = new("ET-P-0307");

    /// <summary>ET-P-0308 — a FlattenSink accepted a point when its buffer was already full and autoflush was disabled.</summary>
    public static readonly PanicCode FlattenSinkOverflow = new("ET-P-0308");

    /// <summary>ET-P-0309 — lengths passed to SampleAtLengthsSorted were not sorted in ascending order.</summary>
    public static readonly PanicCode UnsortedLengths = new("ET-P-0309");

    /// <summary>ET-P-0402 — SceneBuilder was used after End() was called.</summary>
    public static readonly PanicCode SceneBuilderConsumed = new("ET-P-0402");

    /// <summary>ET-P-0403 — an invalid resource ID was passed to a SceneBuilder draw method.</summary>
    public static readonly PanicCode InvalidSceneResourceId = new("ET-P-0403");

    /// <summary>ET-P-0404 — scene format buffer does not start with the ETSC magic bytes.</summary>
    public static readonly PanicCode SceneFormatBadMagic = new("ET-P-0404");

    /// <summary>ET-P-0405 — scene format major version is newer than supported.</summary>
    public static readonly PanicCode SceneFormatVersionTooNew = new("ET-P-0405");

    /// <summary>ET-P-0406 — scene format buffer is too short to contain the declared header.</summary>
    public static readonly PanicCode SceneFormatTruncated = new("ET-P-0406");

    /// <summary>ET-P-0420 — scene frame markers are malformed (duplicate/missing BeginFrame/EndFrame).</summary>
    public static readonly PanicCode BadFrameMarkers = new("ET-P-0420");

    /// <summary>ET-P-0421 — PopLayer without a matching PushLayer.</summary>
    public static readonly PanicCode UnbalancedLayerStack = new("ET-P-0421");

    /// <summary>ET-P-0422 — PushLayer depth exceeds 32.</summary>
    public static readonly PanicCode LayerStackOverflow = new("ET-P-0422");

    /// <summary>ET-P-0423 — PopClip without a matching PushClip.</summary>
    public static readonly PanicCode UnbalancedClipStack = new("ET-P-0423");

    /// <summary>ET-P-0424 — a scene command references a resource ID that does not exist.</summary>
    public static readonly PanicCode InvalidResourceId = new("ET-P-0424");

    /// <summary>ET-P-0425 — NaN or Infinity found in transform or geometry coordinates.</summary>
    public static readonly PanicCode NonFiniteGeometry = new("ET-P-0425");

    /// <summary>ET-P-0426 — a path used in FillPath or StrokePath has fewer than 2 verbs.</summary>
    public static readonly PanicCode EmptyPath = new("ET-P-0426");

    /// <summary>ET-P-0427 — a gradient paint has fewer than 2 stops or non-monotone offsets.</summary>
    public static readonly PanicCode BadGradient = new("ET-P-0427");

    /// <summary>ET-P-0428 — PushLayer opacity is outside the valid range [0, 1].</summary>
    public static readonly PanicCode BadLayerOpacity = new("ET-P-0428");

    /// <summary>ET-P-0429 — stroke width is not &gt; 0, or miter limit is &lt; 1.</summary>
    public static readonly PanicCode BadStrokeParam = new("ET-P-0429");

    /// <summary>ET-P-0501 — surface or grid dimensions are &lt;= 0.</summary>
    public static readonly PanicCode InvalidSurfaceSize = new("ET-P-0501");

    /// <summary>ET-P-0502 — ClassificationAccumulator.Finish() called more than once.</summary>
    public static readonly PanicCode AccumulatorConsumed = new("ET-P-0502");

    /// <summary>ET-P-0503 — a null ITileScheduler was passed to ParallelClassifier.Classify.</summary>
    public static readonly PanicCode SchedulerRequired = new("ET-P-0503");

    /// <summary>ET-P-0601 — a shader failed to compile at runtime (WebGPU validation error).</summary>
    public static readonly PanicCode ShaderCompileError = new("ET-P-0601");

    /// <summary>ET-P-0702 — PushClip depth exceeds 16 in CPU rasterizer.</summary>
    public static readonly PanicCode ClipStackOverflow = new("ET-P-0702");

    /// <summary>ET-P-0801 — strip + coverage arena exceeds 64 MiB budget per frame.</summary>
    public static readonly PanicCode StripBudgetExceeded = new("ET-P-0801");

    /// <summary>ET-P-0802 — draw order is not sorted by (layer, commandIndex) in DEBUG mode.</summary>
    public static readonly PanicCode UnsortedDrawOrder = new("ET-P-0802");

    /// <summary>ET-P-0803 — GPU clip mask stack exceeds 16 levels.</summary>
    public static readonly PanicCode GpuClipStackOverflow = new("ET-P-0803");

    /// <summary>ET-P-0902 — PushClip depth exceeds 16.</summary>
    public static readonly PanicCode ClipStackTooDeep = new("ET-P-0902");

    /// <summary>ET-P-0903 — a radial gradient was created with a zero or negative radius.</summary>
    public static readonly PanicCode DegenerateRadialGradient = new("ET-P-0903");

    /// <summary>ET-P-0904 — a sweep gradient was created with endAngle &lt;= startAngle.</summary>
    public static readonly PanicCode DegenerateSweepGradient = new("ET-P-0904");

    /// <summary>ET-P-1001 — a dash pattern was created with null/empty segments or zero sum.</summary>
    public static readonly PanicCode DegenerateDashPattern = new("ET-P-1001");

    /// <summary>ET-P-1101 — a glyph atlas dimension was not 2048 or 4096.</summary>
    public static readonly PanicCode InvalidAtlasDimension = new("ET-P-1101");

    /// <summary>ET-P-1102 — eviction freed space but insert still failed.</summary>
    public static readonly PanicCode AtlasInsertFailedAfterEviction = new("ET-P-1102");

    /// <summary>ET-P-1103 — ICU4N data files are missing or could not be loaded.</summary>
    public static readonly PanicCode IcuDataMissing = new("ET-P-1103");

    /// <summary>ET-P-1201 — SharpImage failed to decode the image data.</summary>
    public static readonly PanicCode ImageDecodeFailed = new("ET-P-1201");

    /// <summary>ET-P-1202 — decoded image dimensions exceed the device's maxTextureDimension2D limit.</summary>
    public static readonly PanicCode ImageDimensionsExceedLimit = new("ET-P-1202");

    /// <summary>ET-P-1601 — a mesh gradient was created with fewer than 2 rows or 2 columns, or vertex count doesn't match.</summary>
    public static readonly PanicCode DegenerateMesh = new("ET-P-1601");

    /// <summary>ET-P-1602 — noise spec created with invalid octaves, negative scale, or zero opacity.</summary>
    public static readonly PanicCode DegenerateNoise = new("ET-P-1602");
}
