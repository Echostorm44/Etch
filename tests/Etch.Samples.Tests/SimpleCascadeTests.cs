using System;
using System.Diagnostics;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit;

namespace Etch.Samples.Tests;

public class SimpleCascadeTests
{
    private const int RenderWidth = 400;
    private const int RenderHeight = 200;

    [Test]
    public async Task Smoke_FrameRendersWithButton()
    {
        var (pixels, w, h) = RenderTestFrame();
        await Assert.That(w).IsEqualTo(RenderWidth);
        await Assert.That(h).IsEqualTo(RenderHeight);

        bool hasFilledPixels = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 0 || pixels[i + 1] > 0 || pixels[i + 2] > 0)
            {
                hasFilledPixels = true;
                break;
            }
        }
        await Assert.That(hasFilledPixels).IsTrue();
    }

    [Test]
    public async Task Smoke_TitleBarRendersInBlue()
    {
        var (pixels, w, _) = RenderTestFrame();

        long blueSum = 0;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = (y * w + x) * 4;
            blueSum += pixels[idx + 0];
        }
        await Assert.That(blueSum).IsGreaterThan(0);
    }

    [Test]
    public async Task WorkingSet_WithinBudget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        for (int i = 0; i < 5; i++)
            _ = RenderTestFrame();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long wsMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

        await Assert.That(wsMb).IsGreaterThanOrEqualTo(1);
        await Assert.That(wsMb).IsLessThanOrEqualTo(400);
    }

    [Test]
    public async Task Smoke_CommandCount_IncludesTextRects()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);

        int bgId = builder.AddPaint(Paint.Solid(0xFFF0F0F0u));
        builder.FillRect(new Rect(0, 0, 400, 200), bgId, identity);

        int barId = builder.AddPaint(Paint.Solid(0xFF3366CCu));
        builder.FillRect(new Rect(0, 0, 400, 32), barId, identity);

        int whiteId = builder.AddPaint(Paint.Solid(0xFFFFFFFFu));
        builder.FillRect(new Rect(8, 7, 16, 17), whiteId, identity);
        builder.FillRect(new Rect(20, 7, 28, 17), whiteId, identity);

        builder.EndFrame();
        var scene = builder.End();

        await Assert.That(scene.Commands.Length).IsGreaterThanOrEqualTo(4);

        byte[] pixels = Etch.Testing.SceneRunner.RunCpu(scene, 400, 200);
        // Check pixel at center of first white rect (12, 12)
        int idx = (12 * 400 + 12) * 4;
        await Assert.That((int)pixels[idx + 2]).IsEqualTo(255); // R
        await Assert.That((int)pixels[idx + 1]).IsEqualTo(255); // G
        await Assert.That((int)pixels[idx + 0]).IsEqualTo(255); // B
    }

    [Test]
    public async Task Smoke_GlyphText_RendersPixels()
    {
        byte[] fontData = DownloadTestFont();
        using var face = Etch.Text.Shape.FontFace.Load(fontData, 2048, 14f);

        var shaped = Etch.Text.Shape.Shaper.Shape(
            new Etch.Text.Shape.ShapeRequest("A".AsSpan(), face, Etch.Text.Shape.BiDiLevel.LeftToRight, "latn"));
        ushort glyphId = shaped.Glyphs[0].GlyphId;

        BezPath path;
        using (var pathBuilder = BezPathBuilder.Begin(64))
        {
            var pathOpt = Etch.Text.Outline.GlyphOutlineBuilder.Build(face, glyphId, pathBuilder);
            path = pathOpt ?? new BezPath([], [], 0);
        }

        await Assert.That(path.IsEmpty).IsFalse();

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);

        int bgPaintId = builder.AddPaint(Paint.Solid(0xFF3366CCu));
        builder.FillRect(new Rect(0, 0, RenderWidth, 32), bgPaintId, identity);

        int whitePaintId = builder.AddPaint(Paint.Solid(0xFFFFFFFFu));
        double scale = face.PointSize / face.UnitsPerEm;
        var xf = Affine.Identity.PreScale(scale, scale).PreTranslate(8, 24);
        int xfId = builder.AddTransform(xf);
        int pathId = builder.AddPath(path);
        builder.FillPath(pathId, whitePaintId, xfId, FillRule.NonZero);

        builder.EndFrame();
        var scene = builder.End();

        byte[] pixels = Etch.Testing.SceneRunner.RunCpu(scene, RenderWidth, RenderHeight);

        int whiteCount = 0;
        for (int y = 4; y < 30; y++)
        for (int x = 4; x < RenderWidth - 4; x++)
        {
            int idx = (y * RenderWidth + x) * 4;
            if (pixels[idx + 0] > 200 && pixels[idx + 1] > 200 && pixels[idx + 2] > 200)
                whiteCount++;
        }
        await Assert.That(whiteCount).IsGreaterThan(5);
    }

    private static byte[] DownloadTestFont()
    {
        using var client = new System.Net.Http.HttpClient();
        var resp = client.GetAsync(new Uri("https://fonts.gstatic.com/s/roboto/v32/KFOmCnqEu92Fr1Me5Q.ttf"))
            .GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    private static (byte[] Pixels, int w, int h) RenderTestFrame()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);

        int bgPaintId = builder.AddPaint(Paint.Solid(0xFFF0F0F0u));
        builder.FillRect(new Rect(0, 0, RenderWidth, RenderHeight), bgPaintId, identity);

        int titleBarPaintId = builder.AddPaint(Paint.Solid(0xFF3366CCu));
        builder.FillRect(new Rect(0, 0, RenderWidth, 32), titleBarPaintId, identity);

        int buttonX = 150, buttonY = 80, buttonW = 100, buttonH = 30;
        int buttonPaintId = builder.AddPaint(Paint.Solid(0xFFE0E0E0u));
        builder.FillRect(new Rect(buttonX, buttonY, buttonX + buttonW, buttonY + buttonH), buttonPaintId, identity);

        // Draw text labels
        DrawLabel(ref builder, "SimpleCascade  Clicks: 0", 8, 6, 14, 0xFFFFFFFFu, identity);
        DrawLabel(ref builder, "Click me", buttonX + 14, buttonY + 6, 12, 0xFF000000u, identity);

        builder.EndFrame();
        var scene = builder.End();

        byte[] pixels = SceneRunner.RunCpu(scene, RenderWidth, RenderHeight);
        return (pixels, RenderWidth, RenderHeight);
    }

    private static void DrawLabel(ref SceneBuilder builder, string text, int x, int y, int pxSize, uint color, int transformId)
    {
        int paintId = builder.AddPaint(Paint.Solid(color));
        double charX = x;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            int width = pxSize / 2 + 1;
            if (width < 2) width = 2;

            if (c == ' ')
            {
                charX += pxSize * 0.6;
                continue;
            }

            double cx = charX;
            double cy = y + 1;
            double h = pxSize * 0.7;
            if (h < 4) h = 4;

            builder.FillRect(new Rect(cx, cy, cx + width, cy + h), paintId, transformId);

            if (c >= 'A' && c <= 'Z' || c >= '0' && c <= '9')
            {
                builder.FillRect(new Rect(cx, cy, cx + width + 2, cy + width), paintId, transformId);
                builder.FillRect(new Rect(cx, cy + h - width, cx + width + 1, cy + h), paintId, transformId);
            }
            else if (c >= 'a' && c <= 'z')
            {
                builder.FillRect(new Rect(cx, cy + 2, cx + width, cy + h), paintId, transformId);
            }
            else
            {
                builder.FillRect(new Rect(cx, cy + h / 2, cx + width * 2, cy + h / 2 + width), paintId, transformId);
            }

            charX += pxSize * 0.7;
        }
    }
}
