using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace WindowBouncer.Services;

// Sets a child Window as owned by a parent Window (z-order, taskbar grouping, etc.)
// and centers it over the parent. Project is x64-only, so SetWindowLongPtrW is fine.
internal static class OwnedDialog
{
    private const int GWLP_HWNDPARENT = -8;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    public static void CenterOver(Window child, Window? owner)
    {
        if (owner is null) return;

        var childHwnd = WindowNative.GetWindowHandle(child);
        var ownerHwnd = WindowNative.GetWindowHandle(owner);
        SetWindowLongPtr(childHwnd, GWLP_HWNDPARENT, ownerHwnd);

        var childApp = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(childHwnd));
        var ownerApp = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(ownerHwnd));

        var x = ownerApp.Position.X + (ownerApp.Size.Width  - childApp.Size.Width)  / 2;
        var y = ownerApp.Position.Y + (ownerApp.Size.Height - childApp.Size.Height) / 2;
        childApp.Move(new PointInt32(x, y));
    }
}
