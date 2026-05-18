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
    private readonly Win32MessageWindow _taskbarWatcher;
    private readonly uint _taskbarCreatedMsg;
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

        // Re-register the tray icon when Explorer (re)creates the taskbar
        _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _taskbarWatcher = new Win32MessageWindow("WindowBouncer_TaskbarWatcher");
        _taskbarWatcher.MessageReceived += OnTaskbarMessage;
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

    private nint OnTaskbarMessage(nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMsg)
        {
            try
            {
                _trayIcon.Remove();
                _trayIcon.Create();
            }
            catch { }
        }
        return 0;
    }

    public void ShowMainWindow() => _mainWindow?.ShowFromTray();
    public void HideMainWindow() => _mainWindow?.HideToTray();

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

        var diagnostic = new PopupMenuItem("Dump Window Diagnostic...", (_, _) =>
        {
            var path = WindowEnumerator.DumpAllWindows();
            try
            {
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

            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
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
        try { _trayIcon.Dispose(); } catch { }
        try { _taskbarWatcher.Dispose(); } catch { }
        _icon?.Dispose();
    }
}
