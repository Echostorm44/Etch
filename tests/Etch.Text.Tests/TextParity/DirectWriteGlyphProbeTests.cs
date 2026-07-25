using System;
using System.Buffers;
using TUnit;

namespace Etch.Text.Tests.TextParity;

/// <summary>
/// WP-3527: smoke test for the hand-bound DirectWrite reference probe. Validates
/// the vtable COM interop actually produces glyph coverage on Windows; on other
/// platforms the probe reports unsupported and the test is a no-op.
/// </summary>
public class DirectWriteGlyphProbeTests
{
    [Test]
    public async Task DirectWrite_RendersGlyphCoverage_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // non-Windows: nothing to assert
        }

        using var dw = DirectWriteGlyphProbe.TryCreate(TestFonts.RobotoRegular);
        await Assert.That(dw).IsNotNull();

        byte[] buf = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            bool ok = dw!.TryRenderChar("H", 48f, buf, out int w, out int h, out float advance);
            await Assert.That(ok).IsTrue().Because(dw.LastError ?? "no error");
            await Assert.That(w).IsGreaterThan(0);
            await Assert.That(h).IsGreaterThan(0);
            await Assert.That(advance).IsGreaterThan(0f);

            long ink = 0;
            for (int i = 0; i < w * h; i++)
            {
                ink += buf[i];
            }
            await Assert.That(ink).IsGreaterThan(0);

            // Whitespace has no alpha bounds → empty.
            bool space = dw.TryRenderChar(" ", 48f, buf, out _, out _, out _);
            await Assert.That(space).IsFalse();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}
