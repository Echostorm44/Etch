using System.Collections.Concurrent;

namespace Etch.Testing;

/// <summary>
/// Fonts for tests and benchmarks. Font files are not checked in (the repository ignores
/// <c>*.ttf</c>), so they are downloaded once and kept on disk — in <c>ETCH_TEST_FONTS_DIR</c> when
/// set (CI caches that directory between runs), else in the temp directory. Offline, with nothing
/// cached, a system UI font of the same role stands in where the caller allows it.
/// </summary>
public static class TestFontCache
{
    private static readonly ConcurrentDictionary<string, byte[]> Memory = new();

    /// <summary>The cache directory.</summary>
    public static string Directory
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("ETCH_TEST_FONTS_DIR");
            return string.IsNullOrEmpty(configured) ? Path.Combine(Path.GetTempPath(), "etch-test-fonts") : configured;
        }
    }

    /// <summary>Roboto Regular (Google Fonts), or the system UI font when offline.</summary>
    public static byte[] RobotoRegular() => Get("Roboto-Regular.ttf", new Uri("https://fonts.gstatic.com/s/roboto/v32/KFOmCnqEu92Fr1Me5Q.ttf"), allowSystemFallback: true);

    /// <summary>Amiri Regular (Arabic; Google Fonts). No fallback: shaping tests need this font.</summary>
    public static byte[] AmiriRegular() => Get("Amiri-Regular.ttf", new Uri("https://github.com/google/fonts/raw/main/ofl/amiri/Amiri-Regular.ttf"), allowSystemFallback: false);

    /// <summary>
    /// The font <paramref name="fileName"/>: from memory, the disk cache, or <paramref name="source"/>
    /// (then cached); offline, a system UI font when <paramref name="allowSystemFallback"/>.
    /// </summary>
    public static byte[] Get(string fileName, Uri source, bool allowSystemFallback)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Memory.GetOrAdd(fileName, _ => Load(fileName, source, allowSystemFallback));
    }

    private static byte[] Load(string fileName, Uri source, bool allowSystemFallback)
    {
        string cached = Path.Combine(Directory, fileName);
        if (File.Exists(cached))
        {
            return File.ReadAllBytes(cached);
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            byte[] bytes = http.GetByteArrayAsync(source).GetAwaiter().GetResult();
            System.IO.Directory.CreateDirectory(Directory);
            // Write then move, so a concurrent reader never sees a partial file.
            string temporary = cached + "." + Environment.ProcessId + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, cached, overwrite: true);
            return bytes;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && allowSystemFallback)
        {
            foreach (string candidate in SystemFallbacks())
            {
                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }
            }
            Panic.Invariant(PanicCodes.InvalidState, $"{fileName} could not be downloaded from {source} ({e.Message}) and no system fallback font exists.");
            return [];
        }
    }

    private static IEnumerable<string> SystemFallbacks()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        yield return "/usr/share/fonts/dejavu/DejaVuSans.ttf";
        yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
    }
}
