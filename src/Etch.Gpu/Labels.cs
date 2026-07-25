using System;
using System.Text;
using Etch.Gpu.Descriptors;

namespace Etch.Gpu;

// ═══════════════════════════════════════════════════════════════════════════
// Label encoding helpers for WGPUStringView interop.
//
// `CreateStringView` is INTENTIONALLY NOT PROVIDED as a standalone API:
// producing a StringView requires a pinned pointer to the encoded UTF-8
// bytes, and a function returning a struct containing a pointer cannot keep
// the backing array pinned. Every prior "CreateStringView" helper we shipped
// had a dangling-pointer bug.
//
// Correct usage pattern at call sites:
//
//     Span<byte> scratch = stackalloc byte[128];
//     int length = Labels.EncodeUtf8(labelText, scratch);
//     fixed (byte* labelPtr = scratch)
//     {
//         desc.Label = new StringView
//         {
//             Data   = (IntPtr)labelPtr,
//             Length = (UIntPtr)length,
//         };
//         // ...call native function while the `fixed` scope is live...
//     }
// ═══════════════════════════════════════════════════════════════════════════

public static class Labels
{
    public const int MaxLabelLength = 127;

    // Encodes `label` as UTF-8 into `scratch` and returns the byte count
    // (no trailing null included — wgpu StringView carries an explicit
    // length). Returns 0 when label is null.
    public static int EncodeUtf8(string? label, Span<byte> scratch)
    {
        if (label is null)
        {
            return 0;
        }

        int encodedLength = Encoding.UTF8.GetBytes(label, scratch);
        if (encodedLength > MaxLabelLength)
        {
            Panic.Invariant(
                PanicCodes.LabelTooLong,
                $"Label '{label}' exceeds maximum length of {MaxLabelLength} bytes (encoded to {encodedLength}).");
        }

        return encodedLength;
    }

    // Same thing but also writes a trailing NUL byte for APIs that
    // accept either a counted or null-terminated view. Returns the
    // payload length (excluding the NUL).
    public static int EncodeUtf8NullTerminated(string? label, Span<byte> scratch)
    {
        int length = EncodeUtf8(label, scratch);
        if ((uint)length >= (uint)scratch.Length)
        {
            Panic.Invariant(
                PanicCodes.LabelTooLong,
                "Scratch buffer has no room for trailing NUL byte.");
        }
        scratch[length] = 0;
        return length;
    }
}
