using System;
using System.Drawing;
using System.IO;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using WindowBouncer.Services;
using WindowBouncer.Views;
using WindowBouncer.Win32;

namespace WindowBouncer.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly TrayIconWithContextMenu _trayIcon;
    private readonly WindowService _windowService;
    private MainWindow? _mainWindow;
    private DispatcherQueue? _dispatcher;
    private Icon? _icon;

    public event EventHandler? ShowSettingsRequested;

    public TrayIconService(WindowService windowService)
    {
        _windowService = windowService;

        _icon = LoadAppIcon();

        _trayIcon = new TrayIconWithContextMenu
        {
            Icon       = _icon?.Handle ?? CreateFallbackIcon().Handle,
            ToolTip    = "WindowBouncer",
            ContextMenu = BuildContextMenu()
        };
        _trayIcon.MessageWindow.MouseEventReceived += OnMouseEvent;
        _trayIcon.Create();

        // Re-add the icon when Explorer restarts. TaskbarCreated is broadcast only to top-level
        // windows, so it must be heard on H.NotifyIcon's own window, not a message-only one.
        // When elevated, UIPI drops that broadcast from the unelevated Explorer unless allowed.
        _trayIcon.MessageWindow.TaskbarCreated += OnTaskbarCreated;
        if (ElevationService.IsElevated())
            NativeMethods.ChangeWindowMessageFilterEx(_trayIcon.MessageWindow.Handle,
                NativeMethods.RegisterWindowMessage("TaskbarCreated"), NativeMethods.MSGFLT_ALLOW, 0);
    }

    public void SetMainWindow(MainWindow window)
    {
        _mainWindow = window;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    // H.NotifyIcon raises tray events on its own message-pump thread. Marshal anything that
    // touches the WinUI window back to the UI thread or AppWindow APIs throw RPC_E_WRONG_THREAD.
    private void RunOnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    private void OnMouseEvent(object? sender, H.NotifyIcon.Core.MessageWindow.MouseEventReceivedEventArgs e)
    {
        if (e.MouseEvent == MouseEvent.IconDoubleClick)
            RunOnUi(ShowMainWindow);
    }

    private void OnTaskbarCreated(object? sender, EventArgs e)
    {
        try
        {
            _trayIcon.Remove();
            _trayIcon.Create();
        }
        catch { }
    }

    public void ShowMainWindow() => _mainWindow?.ShowFromTray();
    public void HideMainWindow() => _mainWindow?.HideToTray();

    public void ShowNotification(string title, string message)
    {
        try { _trayIcon.ShowNotification(title, message, NotificationIcon.Warning); }
        catch { }
    }

    public void UpdateTooltip(int windowCount)
        => _trayIcon.UpdateToolTip($"WindowBouncer \u2014 {windowCount} windows");

    private PopupMenu BuildContextMenu()
    {
        var menu = new PopupMenu();

        var showHide = new PopupMenuItem("Show / Hide", (_, _) => RunOnUi(() =>
        {
            if (_mainWindow is null) return;
            ShowMainWindow();
        }));

        var closeAll = new PopupMenuItem("Close All Windows", (_, _) => RunOnUi(async () =>
        {
            var windows = await _windowService.GetWindowsAsync();
            await _windowService.CloseAllAsync(windows);
        }));

        var settings = new PopupMenuItem("Settings...", (_, _) => RunOnUi(() =>
        {
            ShowSettingsRequested?.Invoke(this, EventArgs.Empty);
        }));

        // Runs on H.NotifyIcon's thread, where an unhandled exception kills the process.
        var diagnostic = new PopupMenuItem("Dump Window Diagnostic...", (_, _) =>
        {
            try
            {
                var path = WindowEnumerator.DumpAllWindows();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        });

        var exit = new PopupMenuItem("Exit", (_, _) => RunOnUi(App.Shutdown));

        menu.Items.Add(showHide);
        menu.Items.Add(new PopupMenuSeparator());
        menu.Items.Add(closeAll);
        menu.Items.Add(new PopupMenuSeparator());
        menu.Items.Add(settings);
        // The dump goes to the Desktop, a path any same-user process can redirect. An elevated
        // write there could be steered onto a protected file, so offer it only unelevated.
        if (!ElevationService.IsElevated())
            menu.Items.Add(diagnostic);
        menu.Items.Add(new PopupMenuSeparator());
        menu.Items.Add(exit);

        return menu;
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "appicon.ico");
            if (File.Exists(path))
                return new Icon(path);

            var exePath = Environment.ProcessPath;
            if (exePath is not null)
                return Icon.ExtractAssociatedIcon(exePath);
        }
        catch { }
        return null;
    }

    private static Icon CreateFallbackIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(0, 120, 212));
        using var font = new Font("Arial", 9, System.Drawing.FontStyle.Bold);
        g.DrawString("W", font, Brushes.White, -1f, 1f);
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        try { _trayIcon.MessageWindow.TaskbarCreated -= OnTaskbarCreated; } catch { }
        try { _trayIcon.Dispose(); } catch { }
        _icon?.Dispose();
    }
}
