using System;
using System.IO;
using System.Linq;
using Etch.SvgTranslator;
using Etch.Testing;
using TUnit;

namespace Etch.Correctness.Tests.Svg11;

public class Svg11Tests
{
    private const int RenderSize = 64;

    private static string CorpusDir => Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "Svg11", "corpus");

    [Test]
    public async Task SimpleRect_RendersNonZeroPixels()
    {
        var scene = await TranslateAsync("simple-rect.svg");
        byte[] output = SceneRunner.RunCpu(scene, RenderSize, RenderSize);

        int nonZero = output.Count(b => b != 0);
        await Assert.That(nonZero).IsGreaterThan(0);
    }

    [Test]
    public async Task SimpleRect_CenterPixelIsRed()
    {
        var scene = await TranslateAsync("simple-rect.svg");
        byte[] output = SceneRunner.RunCpu(scene, RenderSize, RenderSize);

        // Center of the 64x64 image, inside the red rect
        int cx = RenderSize / 2;
        int cy = RenderSize / 2;
        int idx = (cy * RenderSize + cx) * 4;

        // RunCpu returns RGBA byte order (R at index 0), matching SceneWriter/SceneReader.
        byte r = output[idx + 0];
        byte g = output[idx + 1];
        byte b = output[idx + 2];
        byte a = output[idx + 3];

        await Assert.That((int)r).IsGreaterThan(200);
        await Assert.That((int)g).IsEqualTo(0);
        await Assert.That((int)b).IsEqualTo(0);
        await Assert.That((int)a).IsGreaterThan(200);
    }

    [Test]
    public async Task CircleAndLine_TranslatesWithoutCrash()
    {
        var scene = await TranslateAsync("circle-and-line.svg");
        await Assert.That(scene.CommandCount).IsGreaterThan(0);
    }

    [Test]
    public async Task PathStar_TranslatesWithoutCrash()
    {
        var scene = await TranslateAsync("path-star.svg");
        await Assert.That(scene.CommandCount).IsGreaterThan(0);
    }

    [Test]
    public async Task GradientLinear_TranslatesWithoutCrash()
    {
        var scene = await TranslateAsync("gradient-linear.svg");
        await Assert.That(scene.CommandCount).IsGreaterThan(0);
    }

    [Test]
    public async Task GroupTransform_TranslatesWithoutCrash()
    {
        var scene = await TranslateAsync("group-transform.svg");
        await Assert.That(scene.CommandCount).IsGreaterThan(0);
    }

    [Test]
    public async Task MalformedSvg_ThrowsEtchExceptionOrReturnsScene()
    {
        string badSvg = "<svg><rect x='abc' y='def' width='10' height='10'/></svg>";

        try
        {
            var scene = SvgToSceneTranslator.Translate(badSvg, RenderSize, RenderSize);
            byte[] output = SceneRunner.RunCpu(scene, RenderSize, RenderSize);
            await Assert.That(output.Length).IsEqualTo(RenderSize * RenderSize * 4);
        }
        catch (EtchException)
        {
            await Assert.That(true).IsTrue();
        }
    }

    [Test]
    public async Task Corpus_AllFilesTranslateWithoutCrash()
    {
        string dir = CorpusDir;
        if (!Directory.Exists(dir))
        {
            await Assert.That(true).IsTrue();
            return;
        }

        string[] files = Directory.GetFiles(dir, "*.svg");
        int successCount = 0;

        foreach (string file in files)
        {
            try
            {
                string svg = await File.ReadAllTextAsync(file);
                var scene = SvgToSceneTranslator.Translate(svg, RenderSize, RenderSize);
                byte[] output = SceneRunner.RunCpu(scene, RenderSize, RenderSize);
                if (output.Length == RenderSize * RenderSize * 4)
                    successCount++;
            }
            catch (EtchException)
            {
                // Acceptable panic
            }
        }

        await Assert.That(successCount).IsEqualTo(files.Length);
    }

    [Test]
    public async Task Debug_CbgScene_Renders()
    {
        var sb = Scene.SceneBuilder.Begin();
        sb.BeginFrame();

        var identity = sb.AddTransform(Etch.Geometry.Affine.Identity);
        var red = sb.AddPaint(Scene.Paint.Solid(0xFFFF0000));

        sb.FillRect(new Etch.Geometry.Rect(10, 10, 54, 54), red, identity);
        sb.EndFrame();

        var scene = sb.End();
        byte[] output = SceneRunner.RunCpu(scene, RenderSize, RenderSize);

        int nonZero = output.Count(b => b != 0);
        await Assert.That(nonZero).IsGreaterThan(0);

        int cx = RenderSize / 2;
        int cy = RenderSize / 2;
        int idx = (cy * RenderSize + cx) * 4;

        // RGBA byte order: red channel is at index 0.
        await Assert.That((int)output[idx + 0]).IsGreaterThan(200);
    }

    private static async Task<Scene.SceneBuffer> TranslateAsync(string filename)
    {
        string path = Path.Combine(CorpusDir, filename);
        string svg = await File.ReadAllTextAsync(path);
        return SvgToSceneTranslator.Translate(svg, RenderSize, RenderSize);
    }
}
