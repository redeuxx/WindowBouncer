using WindowBouncer.Settings;
using WindowBouncer.Win32;

namespace WindowBouncer.Services;

public sealed class WindowService(SettingsService settings)
{
    // How long to wait for a window to close gracefully before giving up
    private static readonly TimeSpan CloseGraceTimeout = TimeSpan.FromSeconds(5);

    public Task<IReadOnlyList<WindowInfo>> GetWindowsAsync()
    {
        return Task.Run(() =>
        {
            var all = WindowEnumerator.GetVisibleWindows();
            var filtered = all
                .Where(w => !settings.IsExcluded(w.ProcessName, w.Title))
                .ToList();
            return (IReadOnlyList<WindowInfo>)filtered.AsReadOnly();
        });
    }

    public async Task<bool> CloseWindowAsync(WindowInfo window)
    {
        if (!OwnershipValid(window.Handle, window.ProcessId))
            return false;

        NativeMethods.PostMessage(window.Handle, NativeMethods.WM_CLOSE, 0, 0);

        var deadline = DateTime.UtcNow + CloseGraceTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            if (!NativeMethods.IsWindowVisible(window.Handle))
                return true;
            if (HasBlockingDialog(window.Handle))
                return false;
            if (OwnershipValid(window.Handle, window.ProcessId))
                NativeMethods.PostMessage(window.Handle, NativeMethods.WM_CLOSE, 0, 0);
        }

        return false;
    }

    public void RestoreWindow(WindowInfo window)
    {
        NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(window.Handle);
    }

    public async Task CloseAllAsync(
        IEnumerable<WindowInfo> windows,
        IProgress<CloseAllProgress>? progress = null)
    {
        var list = windows.ToList();
        int total = list.Count;
        int closed = 0;

        // Send WM_CLOSE to all windows first (non-blocking), then wait
        foreach (var w in list)
        {
            if (OwnershipValid(w.Handle, w.ProcessId))
                NativeMethods.PostMessage(w.Handle, NativeMethods.WM_CLOSE, 0, 0);
        }

        var deadline = DateTime.UtcNow + CloseGraceTimeout;
        // Track handle -> expected PID so re-sends can verify ownership hasn't changed
        var remaining = list.ToDictionary(w => w.Handle, w => w.ProcessId);

        while (remaining.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(150);

            foreach (var (handle, pid) in remaining.ToList())
            {
                if (!NativeMethods.IsWindowVisible(handle))
                {
                    remaining.Remove(handle);
                    closed++;
                    progress?.Report(new CloseAllProgress(closed, total));
                }
                else if (HasBlockingDialog(handle))
                {
                    // App is prompting the user (e.g. "save changes?") — back off and let
                    // them answer. We're closing windows, not killing processes.
                    remaining.Remove(handle);
                }
                else if (OwnershipValid(handle, pid))
                {
                    // Re-send WM_CLOSE on each tick — tabbed apps (e.g. File Explorer) handle
                    // WM_CLOSE as "close active tab" rather than "close window", so we need to
                    // keep sending until all tabs are gone and the window closes.
                    NativeMethods.PostMessage(handle, NativeMethods.WM_CLOSE, 0, 0);
                }
                else
                {
                    // HWND was recycled by a different process — stop tracking it
                    remaining.Remove(handle);
                }
            }
        }
    }

    private static bool OwnershipValid(nint handle, uint expectedPid)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out uint currentPid);
        return currentPid != 0 && currentPid == expectedPid;
    }

    // Detect a confirmation dialog the app put up in response to WM_CLOSE (e.g. "save changes?").
    // We leave it for the user to handle rather than auto-dismissing — the goal is to close
    // windows cooperatively, not force-quit apps.
    private static bool HasBlockingDialog(nint ownerHandle)
    {
        bool found = false;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (hWnd == ownerHandle || !NativeMethods.IsWindowVisible(hWnd))
                return true;
            if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) == ownerHandle)
            {
                found = true;
                return false;
            }
            return true;
        }, 0);

        return found;
    }

}

public readonly record struct CloseAllProgress(int Closed, int Total)
{
    public double Fraction => Total == 0 ? 1.0 : (double)Closed / Total;
}
