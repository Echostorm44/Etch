using System;
using Etch.Geometry;
using Etch.Scene;
using BlendMode = Etch.ClipBlendGradient.BlendMode;

namespace SimpleCascade;

internal sealed class SimpleCascadeRenderer
{
    private readonly int _w, _h;
    private int _frame;
    private static readonly BlendMode[] Modes = Enum.GetValues<BlendMode>();

    public SimpleCascadeRenderer(int width, int height) { _w = width; _h = height; }

    public (byte[] Pixels, int Width, int Height) Render()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);

        // Dark background
        int bg = builder.AddPaint(Paint.Solid(0xFF1A1A2Eu));
        builder.FillRect(new Rect(0, 0, _w, _h), bg, identity);

        int cols = 8, rows = 4;
        int tw = _w / cols, th = _h / rows;
        double t = _frame * 0.03;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int modeIdx = (r * cols + c) % Modes.Length;
                var mode = Modes[modeIdx];

                double phase = t + r * 0.5 + c * 0.3;
                double offsetX = Math.Sin(phase) * tw * 0.3;
                double offsetY = Math.Cos(phase * 1.3) * th * 0.3;

                int cx = c * tw + tw / 2;
                int cy = r * th + th / 2;
                int half = Math.Min(tw, th) / 3;

                // Backdrop
                int hBrightness = (int)(128 + 127 * Math.Sin(phase * 0.7));
                uint backColor = 0xFF000000u | (uint)(hBrightness << 16) | (uint)(hBrightness << 8) | (uint)hBrightness;
                int backPaint = builder.AddPaint(Paint.Solid(backColor));
                builder.FillRect(new Rect(c * tw, r * th, (c + 1) * tw, (r + 1) * th), backPaint, identity);

                // Foreground with blend mode
                uint fgColor = modeIdx switch
                {
                    0 => 0xFFFF4466u, 1 => 0xFF44BB44u, 2 => 0xFF4488FFu, 3 => 0xFFFFCC44u,
                    4 => 0xFFCC44FFu, 5 => 0xFF44FFCCu, 6 => 0xFFFF8844u, 7 => 0xFF88FF44u,
                    8 => 0xFFCC6644u, 9 => 0xFF44CC88u, 10 => 0xFF8844CCu, 11 => 0xFFCCCC44u,
                    12 => 0xFF44CCCCu, 13 => 0xFFCC44CCu, 14 => 0xFF88CC44u, _ => 0xFFCC8844u,
                };
                int fgPaint = builder.AddPaint(Paint.Solid(fgColor, blendModeId: (byte)mode));

                double fgX = cx - half + offsetX;
                double fgY = cy - half + offsetY;
                builder.FillRect(new Rect(fgX, fgY, fgX + half * 2, fgY + half * 2), fgPaint, identity);

        // Mode label — colored bar at top
        uint labelHue = (uint)(_frame * 5 % 360);
        uint labelColor = HsvToRgb(labelHue, 0.6f, 0.9f);
        int labelPaint = builder.AddPaint(Paint.Solid(labelColor));
        builder.FillRect(new Rect(c * tw + 4, r * th + 4, c * tw + tw - 8, r * th + 8), labelPaint, identity);
            }
        }

        builder.EndFrame();
        var scene = builder.End();

        _frame++;
        return (Etch.Testing.SceneRunner.RunCpu(scene, _w, _h), _w, _h);
    }

    static uint HsvToRgb(uint h, float s, float v)
    {
        float c = v * s;
        float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
        float m = v - c;
        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return 0xFF000000u | ((uint)((r + m) * 255) << 16) | ((uint)((g + m) * 255) << 8) | (uint)((b + m) * 255);
    }
}
