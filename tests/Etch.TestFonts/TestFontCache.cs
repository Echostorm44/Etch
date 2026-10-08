using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Etch.TestFonts;

/// <summary>
/// Fonts for tests and benchmarks. Font files are not checked in (the repository ignores
/// <c>*.ttf</c>), so they are downloaded once, verified against a pinned SHA-256 (a captive
/// portal's HTML or a moved upstream file is never cached), and kept on disk — in
/// <c>ETCH_TEST_FONTS_DIR</c> when set (CI caches that directory between runs), else in the temp
/// directory. Offline, with nothing cached, a system UI font of the same role stands in where the
/// caller allows it, and the substitution is reported on standard error.
/// </summary>
/// <remarks>Test infrastructure only: nothing that ships may reference this assembly.</remarks>
public static class TestFontCache
{
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> Memory = new();

    private static readonly FontSource Roboto = new(
        "Roboto-Regular.ttf",
        new Uri("https://fonts.gstatic.com/s/roboto/v32/KFOmCnqEu92Fr1Me5Q.ttf"),
        "791ABA3A80C988031DE40920E6805746129CCAB8774CBFDD75838A550087C3DB");

    private static readonly FontSource Amiri = new(
        "Amiri-Regular.ttf",
        new Uri("https://github.com/google/fonts/raw/39d11bc313031c9f68e21a297ce5e4a15cc5365e/ofl/amiri/Amiri-Regular.ttf"),
        "AB391C4147D054C48976E98322AD0EEFE1427AA0E0502A12A4C75D80A70CFCD7");

    private static readonly FontSource Twemoji = new(
        "Twemoji.Mozilla.ttf",
        new Uri("https://github.com/mozilla/twemoji-colr/releases/download/v0.7.0/Twemoji.Mozilla.ttf"),
        "6D90152EE0D29E82FE2A87793AF5AA4B7AD13E6538360889E141E81ED299EE8E");

    private static readonly string[] UiFallbacks =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"),
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
    ];

    private static readonly string[] EmojiFallbacks =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf"),
    ];

    private readonly record struct FontSource(string FileName, Uri Url, string Sha256);

    /// <summary>The cache directory.</summary>
    public static string Directory
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("ETCH_TEST_FONTS_DIR");
            return string.IsNullOrEmpty(configured) ? Path.Combine(Path.GetTempPath(), "etch-test-fonts") : configured;
        }
    }

    /// <summary>Roboto Regular (Google Fonts) — no fallback: text tests measure this font.</summary>
    public static byte[] RobotoRegular() => Get(Roboto, null);

    /// <summary>Roboto Regular, or the system UI font when offline (any font serves the caller).</summary>
    public static byte[] RobotoOrSystemUiFont() => Get(Roboto, UiFallbacks);

    /// <summary>Amiri Regular (Arabic). No fallback: shaping tests need this font.</summary>
    public static byte[] AmiriRegular() => Get(Amiri, null);

    /// <summary>A colour (COLR) emoji font: Twemoji Mozilla, or Segoe UI Emoji offline on Windows.</summary>
    public static byte[] ColorEmoji() => Get(Twemoji, EmojiFallbacks);

    // One load per font per process, however many threads ask at once.
    private static byte[] Get(FontSource source, string[]? fallbacks)
        => Memory.GetOrAdd(source.FileName, _ => new Lazy<byte[]>(() => Load(source, fallbacks), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static byte[] Load(FontSource source, string[]? fallbacks)
    {
        string cached = Path.Combine(Directory, source.FileName);
        if (File.Exists(cached))
        {
            byte[] bytes = File.ReadAllBytes(cached);
            if (Matches(bytes, source.Sha256))
            {
                return bytes;
            }
            Console.Error.WriteLine($"TestFontCache: {cached} does not match its pinned SHA-256; downloading it again.");
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            byte[] bytes = http.GetByteArrayAsync(source.Url).GetAwaiter().GetResult();
            if (!Matches(bytes, source.Sha256))
            {
                throw new InvalidDataException($"{source.Url} did not return the pinned font (SHA-256 mismatch; a captive portal or a changed upstream file).");
            }
            System.IO.Directory.CreateDirectory(Directory);
            // Write then move: a concurrent process never reads a partial file; the temporary name
            // is unique per process and attempt.
            string temporary = $"{cached}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, cached, overwrite: true);
            return bytes;
        }
        catch (Exception e) when (fallbacks is not null && e is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            foreach (string candidate in fallbacks)
            {
                if (File.Exists(candidate))
                {
                    Console.Error.WriteLine($"TestFontCache: {source.FileName} unavailable ({e.Message}); using {candidate} instead.");
                    return File.ReadAllBytes(candidate);
                }
            }
            throw new InvalidOperationException($"{source.FileName} could not be downloaded from {source.Url} and no fallback font exists.", e);
        }
    }

    private static bool Matches(byte[] bytes, string sha256)
        => Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
}
