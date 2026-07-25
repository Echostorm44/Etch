using System;
using System.Runtime.InteropServices;
using SimpleCascade;

unsafe
{
    const int Width = 400;
    const int Height = 200;

    var hInstance = Win32.GetModuleHandle(null);

    fixed (char* className = "SimpleCascadeWindow")
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

    int screenW = Win32.GetSystemMetrics(0);
    int screenH = Win32.GetSystemMetrics(1);
    int x = (screenW - Width) / 2;
    int y = (screenH - Height) / 2;

    var hwnd = Win32.CreateWindowExW(
        0, "SimpleCascadeWindow", "SimpleCascade", 0xCF0000,
        x, y, Width, Height, IntPtr.Zero, IntPtr.Zero, hInstance, null);

    _ = Win32.ShowWindow(hwnd, 1);
    _ = Win32.UpdateWindow(hwnd);

    var renderer = new SimpleCascadeRenderer(Width, Height);
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

        var (pixels, w, h) = renderer.Render();

        for (int i = 0; i < w * h; i++)
        {
            int src = i * 4, dst = i * 3;
            bgrPixels[dst + 0] = pixels[src + 0];
            bgrPixels[dst + 1] = pixels[src + 1];
            bgrPixels[dst + 2] = pixels[src + 2];
        }

        var hdc = Win32.GetDC(hwnd);
        if (hdc == IntPtr.Zero) continue;

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 24,
                biCompression = 0,
            },
        };

        fixed (byte* ptr = bgrPixels)
        {
            _ = Win32.StretchDIBits(hdc, 0, 0, Width, Height, 0, 0, w, h,
                ptr, &bmi, 0, 0x00CC0020);
        }

        _ = Win32.ReleaseDC(hwnd, hdc);

        // Target 60 fps
        System.Threading.Thread.Sleep(14);
    }
}

#pragma warning disable CA1812
[UnmanagedCallersOnly]
static nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
{
    if (msg == 0x0014) return 1; // WM_ERASEBKGND — don't erase
    if (msg == 0x000F) { Win32.ValidateRect(hwnd, IntPtr.Zero); return 0; } // WM_PAINT — skip
    return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
}
