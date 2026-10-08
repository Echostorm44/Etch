using Etch.TestFonts;

namespace Etch.Text.Tests;

/// <summary>Test fonts, downloaded once and cached on disk (<see cref="TestFontCache"/>).</summary>
internal static class TestFonts
{
    public static byte[] RobotoRegular => TestFontCache.RobotoRegular();

    public static byte[] AmiriArabic => TestFontCache.AmiriRegular();
}
