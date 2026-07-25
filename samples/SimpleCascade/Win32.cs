using System;
using System.Runtime.InteropServices;

namespace SimpleCascade;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSW
{
    public uint style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public int time;
    public int pt_x, pt_y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth, biHeight;
    public ushort biPlanes, biBitCount;
    public uint biCompression, biSizeImage;
    public int biXPelsPerMeter, biYPelsPerMeter;
    public uint biClrUsed, biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
}

internal static unsafe partial class Win32
{
    [DllImport("user32")]
    public static extern int ValidateRect(nint hwnd, IntPtr lpRect);
    [DllImport("user32")]
    public static extern nint GetDC(nint hwnd);
    [DllImport("user32")]
    public static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("user32")]
    public static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, void* lpParam);
    [DllImport("user32")]
    public static extern int ShowWindow(nint hwnd, int nCmdShow);
    [DllImport("user32")]
    public static extern int UpdateWindow(nint hwnd);
    [DllImport("user32")]
    public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32")]
    public static extern bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
    [DllImport("user32")]
    public static extern bool TranslateMessage(MSG* lpMsg);
    [DllImport("user32")]
    public static extern nint DispatchMessageW(MSG* lpMsg);
    [DllImport("user32")]
    public static extern nint LoadCursor(nint hInstance, int lpCursorName);
    [DllImport("user32")]
    public static extern ushort RegisterClassW(WNDCLASSW* lpWndClass);
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? lpModuleName);
    [DllImport("gdi32")]
    public static extern int StretchDIBits(nint hdc, int xDest, int yDest, int wDest, int hDest,
        int xSrc, int ySrc, int wSrc, int hSrc, byte* lpBits, BITMAPINFO* lpbmi,
        uint iUsage, uint rop);
}
