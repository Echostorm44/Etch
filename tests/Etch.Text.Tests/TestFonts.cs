using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Etch.Text.Tests;

internal static class TestFonts
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    public static byte[] RobotoRegular
    {
        get
        {
            return Cache.GetOrAdd("Roboto-Regular", static _ => DownloadRoboto().GetAwaiter().GetResult());
        }
    }

    private static async Task<byte[]> DownloadRoboto()
    {
        var uri = new Uri("https://fonts.gstatic.com/s/roboto/v32/KFOmCnqEu92Fr1Me5Q.ttf");
        using var response = await HttpClient.GetAsync(uri).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    public static byte[] AmiriArabic
    {
        get
        {
            return Cache.GetOrAdd("AmiriArabic", static _ => DownloadAmiriArabic().GetAwaiter().GetResult());
        }
    }

    private static async Task<byte[]> DownloadAmiriArabic()
    {
        var uri = new Uri("https://github.com/google/fonts/raw/main/ofl/amiri/Amiri-Regular.ttf");
        using var response = await HttpClient.GetAsync(uri).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }
}
