#if WINDOWS
using System;
using System.Runtime.InteropServices;

namespace HelloEtch;

internal readonly struct Win32Window : IDisposable
{
    private readonly IntPtr _hwnd;

    public IntPtr Handle => _hwnd;

    private Win32Window(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public static Win32Window Create(int width, int height, string title)
    {
        IntPtr hwnd = CreateWindowExW(
            0,
            "STATIC",
            title,
            0x80000000 | 0x10000000 | 0x40000000,
            0, 0, width, height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandleW(IntPtr.Zero),
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create window");
        }

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, 0x0040);
        ShowWindow(hwnd, 1);

        return new Win32Window(hwnd);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
        }
    }

    /// <summary>
    /// Drains all pending window messages. Returns false if a WM_QUIT was received (app should exit).
    /// </summary>
    public static bool PumpMessages()
    {
        while (PeekMessageW(out NativeMessage msg, IntPtr.Zero, 0, 0, 0x0001 /* PM_REMOVE */))
        {
            if (msg.Message == 0x0012 /* WM_QUIT */)
            {
                return false;
            }
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref NativeMessage lpMsg);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int X, int Y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);
}
#endif
