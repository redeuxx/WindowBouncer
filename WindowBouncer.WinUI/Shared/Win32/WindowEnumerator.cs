using System.Diagnostics;
using System.IO;
using System.Text;

namespace WindowBouncer.Win32;

public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> GetVisibleWindows()
    {
        var desktop = NativeMethods.GetDesktopWindow();
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!IsTaskbarWindow(hWnd, desktop))
                return true;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            var processName = GetProcessName(pid);
            var isMinimized = NativeMethods.IsIconic(hWnd);

            windows.Add(new WindowInfo(hWnd, title, processName, pid, isMinimized));
            return true;
        }, 0);

        return windows.AsReadOnly();
    }

    // DIAGNOSTIC - dumps every window EnumWindows sees to a log file, with filter reason
    public static string DumpAllWindows()
    {
        var desktop = NativeMethods.GetDesktopWindow();
        var sb = new StringBuilder();
        sb.AppendLine($"WindowBouncer diagnostic — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', 80));

        int total = 0, passed = 0;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            total++;
            var title = GetWindowTitle(hWnd);
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            var proc = GetProcessName(pid);
            NativeMethods.GetWindowRect(hWnd, out var rect);
            var monitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            bool visible = NativeMethods.IsWindowVisible(hWnd);
            bool cloaked = IsCloaked(hWnd);

            string reason = GetFilterReason(hWnd, desktop, visible, cloaked, exStyle, title);
            if (reason == "PASS") passed++;

            sb.AppendLine($"[{reason,-20}] hwnd=0x{hWnd:X8}  monitor=0x{monitor:X8}  " +
                          $"pos=({rect.Left},{rect.Top})  proc={proc,-20}  title={title}");
            return true;
        }, 0);

        sb.AppendLine(new string('-', 80));
        sb.AppendLine($"Total enumerated: {total}   Passed filter: {passed}");

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "WindowBouncer-diagnostic.txt");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string GetFilterReason(nint hWnd, nint desktop, bool visible, bool cloaked, long exStyle, string title)
    {
        if (!visible) return "invisible";
        if (cloaked) return "cloaked";
        if (string.IsNullOrWhiteSpace(title)) return "no-title";
        if ((exStyle & NativeMethods.WS_EX_APPWINDOW) != 0) return "PASS(appwindow)";
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return "toolwindow";
        if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0) return "noactivate";
        return "PASS";
    }

    private static bool IsTaskbarWindow(nint hWnd, nint desktop)
    {
        if (!NativeMethods.IsWindowVisible(hWnd))
            return false;

        if (IsCloaked(hWnd))
            return false;

        long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();

        if ((exStyle & NativeMethods.WS_EX_APPWINDOW) != 0)
            return true;

        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return false;

        if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
            return false;

        return true;
    }

    private static bool IsCloaked(nint hWnd)
    {
        try
        {
            NativeMethods.DwmGetWindowAttribute(
                hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int));
            return cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetWindowTitle(nint hWnd)
    {
        int len = NativeMethods.GetWindowTextLength(hWnd);
        if (len <= 0)
            return string.Empty;

        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetProcessName(uint pid)
    {
        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
