using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;
using WindowBouncer.Win32;

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
        var childApp = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(childHwnd));

        // Owner hidden in the tray (parked off-screen) or minimized: centering over it would put
        // the dialog off-screen, so center on the primary display and leave it unowned.
        if (!NativeMethods.IsWindowVisible(ownerHwnd) || NativeMethods.IsIconic(ownerHwnd))
        {
            var work = DisplayArea.Primary.WorkArea;
            childApp.Move(new PointInt32(
                work.X + (work.Width  - childApp.Size.Width)  / 2,
                work.Y + (work.Height - childApp.Size.Height) / 2));
            return;
        }

        SetWindowLongPtr(childHwnd, GWLP_HWNDPARENT, ownerHwnd);

        var ownerApp = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(ownerHwnd));

        var x = ownerApp.Position.X + (ownerApp.Size.Width  - childApp.Size.Width)  / 2;
        var y = ownerApp.Position.Y + (ownerApp.Size.Height - childApp.Size.Height) / 2;
        childApp.Move(new PointInt32(x, y));
    }
}
