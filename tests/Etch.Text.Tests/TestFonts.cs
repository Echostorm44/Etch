using Etch.Testing;

namespace Etch.Text.Tests;

/// <summary>Test fonts, downloaded once and cached on disk (<see cref="TestFontCache"/>).</summary>
internal static class TestFonts
{
    public static byte[] RobotoRegular => TestFontCache.Get("Roboto-Regular.ttf", new Uri("https://fonts.gstatic.com/s/roboto/v32/KFOmCnqEu92Fr1Me5Q.ttf"), allowSystemFallback: false);

    public static byte[] AmiriArabic => TestFontCache.AmiriRegular();
}
