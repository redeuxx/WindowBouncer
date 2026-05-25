using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
using WindowBouncer.Services;
using WindowBouncer.ViewModels;

namespace WindowBouncer.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private bool _closeToTray;
    private bool _exiting;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        WindowRegistry.Add(this);

        Title = "WindowBouncer";

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        try
        {
            appWindow.SetIcon(System.IO.Path.Combine(
                AppContext.BaseDirectory, "Resources", "appicon.ico"));
        }
        catch { }

        appWindow.Resize(new SizeInt32 { Width = 620, Height = 520 });

        var work = DisplayArea.Primary.WorkArea;
        appWindow.Move(new PointInt32(
            work.X + (work.Width  - appWindow.Size.Width)  / 2,
            work.Y + (work.Height - appWindow.Size.Height) / 2));

        if (appWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsMaximizable = false;

        appWindow.Closing += OnAppWindowClosing;

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentIsDark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeService.ApplyTitleBar(hwnd);

        View.ViewModel = ViewModel;
    }

    public Task InitializeAsync() => ViewModel.RefreshAsync();

    public void SetCloseToTray(bool value) => _closeToTray = value;

    // OFF-SCREEN STARTUP
    // WinUI 3 won't start the dispatcher until a Window is activated, but Activate also
    // paints one frame. For tray-only launches we park the window off any monitor so the
    // unavoidable frame is invisible; ShowFromTray re-centers on first real show.
    private const int OffscreenSentinel = -32000;

    public void PrepareHiddenStartup()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(windowId).Move(new PointInt32(OffscreenSentinel, OffscreenSentinel));
    }

    public void RequestExit()
    {
        _exiting = true;
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeToTray && !_exiting)
        {
            args.Cancel = true;
            HideToTray();
        }
    }

    public void HideToTray()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(windowId).Hide();
    }

    public void ShowFromTray()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Position.X <= OffscreenSentinel || appWindow.Position.Y <= OffscreenSentinel)
        {
            var work = DisplayArea.Primary.WorkArea;
            appWindow.Move(new PointInt32(
                work.X + (work.Width  - appWindow.Size.Width)  / 2,
                work.Y + (work.Height - appWindow.Size.Height) / 2));
        }

        appWindow.Show();
        Activate();
        _ = ViewModel.RefreshAsync();
    }
}
