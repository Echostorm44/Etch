using System;
using System.Runtime.InteropServices;
using Etch;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;

unsafe
{
    bool useGpu = args.Length > 0 && args[0] == "--gpu";
    const int Width = 640, Height = 480;

    var hInstance = Win32.GetModuleHandle(null);

    fixed (char* className = "FilledCircleWindow")
    {
        var wc = new WNDCLASSW
        {
            style = 0,
            lpfnWndProc = &WindowProc,
            hInstance = hInstance,
            hCursor = Win32.LoadCursor(IntPtr.Zero, 32512),
            lpszClassName = className,
        };
        Win32.RegisterClassW(&wc);
    }

    int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);
    var hwnd = Win32.CreateWindowExW(0, "FilledCircleWindow", "Filled Circle", 0xCF0000,
        (sw - Width) / 2, (sh - Height) / 2, Width, Height, IntPtr.Zero, IntPtr.Zero, hInstance, null);

    _ = Win32.ShowWindow(hwnd, 1);
    _ = Win32.UpdateWindow(hwnd);

    var bgrPixels = new byte[Width * Height * 3];
    bool running = true;

    while (running)
    {
        while (Win32.PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, 1))
        {
            if (msg.message == 0x0012) { running = false; break; }
            Win32.TranslateMessage(&msg);
            Win32.DispatchMessageW(&msg);
        }
        if (!running) break;

        byte[] pixels;
        if (useGpu)
        {
            try { pixels = SceneRunner.RunGpu(BuildCircleScene(Width, Height), Width, Height); }
            catch (EtchException) { pixels = SceneRunner.RunCpu(BuildCircleScene(Width, Height), Width, Height); }
        }
        else
        {
            pixels = SceneRunner.RunCpu(BuildCircleScene(Width, Height), Width, Height);
        }

        for (int i = 0; i < Width * Height; i++)
        {
            int src = i * 4, dst = i * 3;
            bgrPixels[dst + 0] = pixels[src + 0];
            bgrPixels[dst + 1] = pixels[src + 1];
            bgrPixels[dst + 2] = pixels[src + 2];
        }

        var hdc = Win32.GetDC(hwnd);
        if (hdc == IntPtr.Zero) continue;
        var bmi = new BITMAPINFO { bmiHeader = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = Width, biHeight = -Height, biPlanes = 1, biBitCount = 24, biCompression = 0 } };
        fixed (byte* ptr = bgrPixels) _ = Win32.StretchDIBits(hdc, 0, 0, Width, Height, 0, 0, Width, Height, ptr, &bmi, 0, 0x00CC0020);
        _ = Win32.ReleaseDC(hwnd, hdc);
        System.Threading.Thread.Sleep(14);
    }
}

static SceneBuffer BuildCircleScene(int w, int h)
{
    var builder = SceneBuilder.Begin();
    builder.BeginFrame();
    int identity = builder.AddTransform(Affine.Identity);
    int paintId = builder.AddPaint(Paint.Solid(0xFFFF0000u));
    int cx = w / 2, cy = h / 2, r = 100;
    double k = 0.5522847498;
    using var pb = BezPathBuilder.Begin();
    pb.MoveTo(new Point(cx + r, cy));
    pb.CubicTo(new Point(cx + r, cy + k * r), new Point(cx + k * r, cy + r), new Point(cx, cy + r));
    pb.CubicTo(new Point(cx - k * r, cy + r), new Point(cx - r, cy + k * r), new Point(cx - r, cy));
    pb.CubicTo(new Point(cx - r, cy - k * r), new Point(cx - k * r, cy - r), new Point(cx, cy - r));
    pb.CubicTo(new Point(cx + k * r, cy - r), new Point(cx + r, cy - k * r), new Point(cx + r, cy));
    pb.Close();
    int pathId = builder.AddPath(pb.Build());
    builder.FillPath(pathId, paintId, identity, FillRule.NonZero);
    builder.EndFrame();
    return builder.End();
}

[UnmanagedCallersOnly]
static nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
{
    if (msg == 0x0014) return 1;
    if (msg == 0x000F) { Win32.ValidateRect(hwnd, IntPtr.Zero); return 0; }
    return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
}

[StructLayout(LayoutKind.Sequential)] unsafe struct WNDCLASSW { public uint style; public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc; public int cbClsExtra, cbWndExtra; public nint hInstance, hIcon, hCursor, hbrBackground; public char* lpszMenuName, lpszClassName; }
[StructLayout(LayoutKind.Sequential)] struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public int time, pt_x, pt_y; }
[StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
[StructLayout(LayoutKind.Sequential)] struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }
static unsafe class Win32
{
    [DllImport("user32")] public static extern nint GetDC(nint h);
    [DllImport("user32")] public static extern int ReleaseDC(nint h, nint dc);
    [DllImport("user32")] public static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32", CharSet=CharSet.Unicode)] public static extern nint CreateWindowExW(uint ex, string cn, string wn, uint s, int x, int y, int w, int h, nint p, nint m, nint i, void* lp);
    [DllImport("user32")] public static extern int ShowWindow(nint h, int c);
    [DllImport("user32")] public static extern int UpdateWindow(nint h);
    [DllImport("user32")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32")] public static extern bool PeekMessageW(out MSG m, nint h, uint fmin, uint fmax, uint r);
    [DllImport("user32")] public static extern bool TranslateMessage(MSG* m);
    [DllImport("user32")] public static extern nint DispatchMessageW(MSG* m);
    [DllImport("user32")] public static extern nint LoadCursor(nint h, int c);
    [DllImport("user32")] public static extern ushort RegisterClassW(WNDCLASSW* w);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] public static extern nint GetModuleHandle(string? n);
    [DllImport("gdi32")] public static extern int StretchDIBits(nint hdc, int dx, int dy, int dw, int dh, int sx, int sy, int sw, int sh, byte* b, BITMAPINFO* bmi, uint u, uint r);
    [DllImport("user32")] public static extern int ValidateRect(nint h, IntPtr r);
}
