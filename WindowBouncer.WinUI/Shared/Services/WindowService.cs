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

    public Task<bool> CloseWindowAsync(WindowInfo window)
        => CloseOneAsync(window, DateTime.UtcNow + CloseGraceTimeout);

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
        var deadline = DateTime.UtcNow + CloseGraceTimeout;

        // All windows close in parallel; continuations run on the caller's (UI) context,
        // so the counter needs no locking.
        await Task.WhenAll(list.Select(async w =>
        {
            if (await CloseOneAsync(w, deadline))
                progress?.Report(new CloseAllProgress(++closed, total));
        }));
    }

    // Sends WM_CLOSE until the window is gone, the app shows a prompt, or the deadline passes.
    // Tabbed apps (e.g. File Explorer) treat WM_CLOSE as "close active tab", so it has to be
    // repeated. A new one goes out only after the window procedure has finished handling the
    // previous one: an app busy putting up "save changes?" never gets a second WM_CLOSE queued
    // behind the first, which would stack prompts or tear the window down under the dialog.
    private static async Task<bool> CloseOneAsync(WindowInfo window, DateTime deadline)
    {
        Task? inFlight = null;
        while (DateTime.UtcNow < deadline)
        {
            if (inFlight is null || inFlight.IsCompleted)
            {
                // Verify ownership before every send so a recycled HWND is never targeted
                if (!OwnershipValid(window.Handle, window.ProcessId))
                    return false;
                inFlight = SendCloseAsync(window.Handle, deadline);
            }

            await Task.Delay(100);
            if (!NativeMethods.IsWindowVisible(window.Handle))
                return true;
            // The app is prompting the user (e.g. "save changes?"): back off and let them
            // answer. We're closing windows, not killing processes.
            if (HasBlockingDialog(window.Handle))
                return false;
        }

        return false;
    }

    // SendMessageTimeout blocks until the window procedure returns, which a modal prompt can
    // hold open for a while, so it runs on its own thread. The timeout ends at the deadline,
    // so no thread outlives the close attempt; hung windows fail immediately.
    private static Task SendCloseAsync(nint handle, DateTime deadline)
    {
        var timeoutMs = (uint)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
        return Task.Factory.StartNew(
            () => NativeMethods.SendMessageTimeout(handle, NativeMethods.WM_CLOSE, 0, 0,
                NativeMethods.SMTO_ABORTIFHUNG, timeoutMs, out _),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
