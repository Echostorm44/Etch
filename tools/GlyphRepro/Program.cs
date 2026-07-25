using System;
using System.IO;
using Etch.Text.Rasterize;
using Etch.Text.Shape;

// Reproduces the exact glyph rasterization path used by EtchGpuPresenter.BuildGlyphInstances:
//   1. GlyphRasterizer.Measure(face, glyphId, out gw, out gh)   -- no subpixel transform
//   2. rent buffer of (gw + 1) * gh bytes
//   3. GlyphRasterizer.Rasterize(face, glyphId, subpixelQuant / 4f, buf, out rw, out rh, ...)
//   4. upload buf[0 .. rw * rh] to the atlas
//
// The buffer is pre-filled with sentinel 0xAA. Any sentinel byte remaining inside the
// uploaded rw * rh slice means uninitialized memory was uploaded to the glyph atlas.

string fontDir = args.Length > 0
    ? args[0]
    : @"F:\Code\CascadeUI\examples\HelloCascade\bin\Debug\net10.0\Fonts";

string[] fontFiles = ["Inter-Regular.ttf", "Inter-Medium.ttf", "Inter-SemiBold.ttf"];
char[] chars = ['n', '9', '4', 'D', '%', 'o', 'u', 't', 'M', 'g'];

int mismatchCount = 0;
int sentinelCount = 0;
int exceptionCount = 0;
int totalCount = 0;

bool dumpMode = args.Length > 1 && (args[1] == "--dump" || args[1] == "--gid");
if (dumpMode)
{
    fontFiles = [];
}

if (args.Length > 1 && args[1] == "--gid")
{
    foreach (string fontFile in new[] { "Inter-Regular.ttf", "Inter-Medium.ttf", "Inter-SemiBold.ttf" })
    {
        byte[] data = File.ReadAllBytes(Path.Combine(fontDir, fontFile));
        int upem = ReadUnitsPerEm(data);
        using var face = FontFace.Load(data, upem, 16f);
        var ids = new List<string>();
        foreach (char c in "Donut Gauges91452%DiskMemory")
        {
            face.Handle.TryGetGlyph(c, out uint gid);
            ids.Add($"'{c}'={gid}");
        }
        Console.WriteLine($"{fontFile}: {string.Join(" ", ids)}");
    }
    return;
}

foreach (string fontFile in fontFiles)
{
    string path = Path.Combine(fontDir, fontFile);
    if (!File.Exists(path))
    {
        Console.WriteLine($"SKIP missing font {path}");
        continue;
    }

    byte[] data = File.ReadAllBytes(path);
    int upem = ReadUnitsPerEm(data);

    for (float size = 10f; size <= 48f; size += 0.25f)
    {
        using var face = FontFace.Load(data, upem, size);

        foreach (char c in chars)
        {
            if (!face.Handle.TryGetGlyph(c, out uint gid))
            {
                continue;
            }
            ushort glyphId = (ushort)gid;

            for (int quant = 0; quant <= 3; quant++)
            {
                totalCount++;
                float subpixelX = quant / 4f;

                GlyphRasterizer.Measure(face, glyphId, out int gw, out int gh, subpixelX);
                if (gw <= 0 || gh <= 0)
                {
                    continue;
                }

                int bufSize = (gw + 1) * gh;
                byte[] buf = new byte[bufSize];
                buf.AsSpan().Fill(0xAA);

                int rw = 0, rh = 0;
                try
                {
                    GlyphRasterizer.Rasterize(face, glyphId, subpixelX, buf.AsSpan(0, bufSize), out rw, out rh, out _, out _);
                }
                catch (Exception ex)
                {
                    exceptionCount++;
                    Console.WriteLine($"THROW {fontFile} size={size} '{c}' sub={subpixelX}: measured {gw}x{gh}, {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                bool dimMismatch = rw != gw || rh != gh;
                if (dimMismatch)
                {
                    mismatchCount++;
                }

                // Simulate the atlas upload slice and look for sentinel bytes.
                int slice = Math.Min(rw * rh, bufSize);
                int sentinels = 0;
                for (int i = 0; i < slice; i++)
                {
                    if (buf[i] == 0xAA)
                    {
                        sentinels++;
                    }
                }

                // 0xAA == 170 is a plausible coverage value, so only report when
                // suspiciously many sentinel bytes survive (a full row is w bytes).
                bool sentinelLeak = sentinels >= rw;
                if (sentinelLeak)
                {
                    sentinelCount++;
                }

                if (dimMismatch || sentinelLeak)
                {
                    string overflow = rw * rh > bufSize ? $" SLICE OVERFLOWS BUFFER ({rw * rh} > {bufSize})" : "";
                    Console.WriteLine($"BAD  {fontFile} size={size} '{c}' sub={subpixelX}: measured {gw}x{gh} rasterized {rw}x{rh} sentinelBytes={sentinels}{overflow}");
                }
            }
        }
    }
}

Console.WriteLine();
Console.WriteLine($"total={totalCount} dimMismatches={mismatchCount} sentinelLeaks={sentinelCount} exceptions={exceptionCount}");

// Dump glyph bitmaps as ASCII art: --dump <char> <size> [fontFile]
if (args.Length > 2 && args[1] == "--dump")
{
    char dumpChar = args[2][0];
    float dumpSize = args.Length > 3 ? float.Parse(args[3]) : 24f;
    string dumpFont = args.Length > 4 ? args[4] : "Inter-Regular.ttf";

    string path = Path.Combine(fontDir, dumpFont);
    byte[] data = File.ReadAllBytes(path);
    int upem = ReadUnitsPerEm(data);
    using var face = FontFace.Load(data, upem, dumpSize);
    face.Handle.TryGetGlyph(dumpChar, out uint gid);

    for (int quant = 0; quant <= 3; quant++)
    {
        GlyphRasterizer.Measure(face, (ushort)gid, out int gw, out int gh);
        int bufSize = (gw + 1) * gh;
        byte[] buf = new byte[bufSize];
        buf.AsSpan().Fill(0xAA);
        GlyphRasterizer.Rasterize(face, (ushort)gid, quant / 4f, buf.AsSpan(0, bufSize), out int rw, out int rh, out _, out _);
        Console.WriteLine($"'{dumpChar}' {dumpFont} size={dumpSize} sub={quant / 4f}: measured {gw}x{gh} rasterized {rw}x{rh}");
        DumpAscii(buf, rw, rh);
    }
}

static void DumpAscii(byte[] buf, int w, int h)
{
    // Buffer rows are stored bottom-up; print top-down.
    for (int row = h - 1; row >= 0; row--)
    {
        var line = new char[w];
        for (int col = 0; col < w; col++)
        {
            byte v = buf[row * w + col];
            line[col] = v switch
            {
                0 => '.',
                < 64 => ':',
                < 128 => '+',
                < 192 => '#',
                _ => '@',
            };
        }
        Console.WriteLine(new string(line));
    }
    Console.WriteLine();
}

static unsafe int ReadUnitsPerEm(byte[] data)
{
    fixed (byte* ptr = data)
    {
        using var blob = new HarfBuzzSharp.Blob((nint)ptr, data.Length, HarfBuzzSharp.MemoryMode.ReadOnly, null!);
        using var hbFace = new HarfBuzzSharp.Face(blob, 0);
        return hbFace.UnitsPerEm;
    }
}
