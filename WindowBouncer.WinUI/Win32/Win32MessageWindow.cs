using System;
using System.Runtime.InteropServices;

namespace WindowBouncer.Win32;

/// <summary>
/// Hidden, message-only Win32 window that surfaces incoming messages as a managed event.
/// Replaces WPF's <c>HwndSource</c> for hosting <c>WM_HOTKEY</c>, <c>TaskbarCreated</c>, etc.
/// </summary>
internal sealed class Win32MessageWindow : IDisposable
{
    private const string ClassName = "WindowBouncer.MessageWindow";
    private static readonly nint HWND_MESSAGE = -3;

    public delegate nint MessageHandler(nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled);

    public event MessageHandler? MessageReceived;

    public nint Handle { get; private set; }

    private readonly WndProc _wndProc;
    private readonly ushort _atom;
    private readonly nint _hInstance;
    private bool _disposed;

    public Win32MessageWindow(string title = "WindowBouncer_MessageWindow")
    {
        _wndProc = WndProcImpl;
        _hInstance = GetModuleHandle(null);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = _hInstance,
            hIcon = 0,
            hCursor = 0,
            hbrBackground = 0,
            lpszMenuName = null,
            lpszClassName = ClassName + Guid.NewGuid().ToString("N"),
            hIconSm = 0
        };

        _atom = RegisterClassEx(ref wc);
        if (_atom == 0)
            throw new InvalidOperationException(
                $"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

        Handle = CreateWindowEx(
            dwExStyle: 0,
            lpClassName: wc.lpszClassName,
            lpWindowName: title,
            dwStyle: 0,
            X: 0, Y: 0, nWidth: 0, nHeight: 0,
            hWndParent: HWND_MESSAGE,
            hMenu: 0,
            hInstance: _hInstance,
            lpParam: 0);

        if (Handle == 0)
            throw new InvalidOperationException(
                $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
    }

    private nint WndProcImpl(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        bool handled = false;
        var result = MessageReceived?.Invoke(hwnd, msg, wParam, lParam, ref handled) ?? 0;
        if (handled)
            return result;
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Handle != 0)
        {
            DestroyWindow(Handle);
            Handle = 0;
        }
        if (_atom != 0)
        {
            UnregisterClass(_atom, _hInstance);
        }
    }

    // P/INVOKE

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(ushort lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle,
        string? lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int X, int Y, int nWidth, int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
