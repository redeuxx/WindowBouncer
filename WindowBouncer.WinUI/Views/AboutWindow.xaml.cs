using System;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
using WindowBouncer.Services;
using WindowBouncer.Win32;

namespace WindowBouncer.Views;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        WindowRegistry.Add(this);

        Title = "About WindowBouncer";

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(System.IO.Path.Combine(
            AppContext.BaseDirectory, "Resources", "appicon.ico"));
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        appWindow.Resize(new SizeInt32 { Width = (int)(380 * scale), Height = (int)(290 * scale) });

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentIsDark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeService.ApplyTitleBar(hwnd);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";
        BuildDateText.Text = $"Built {BuildInfo.BuildDate}";
    }

    private void OK_Click(object sender, RoutedEventArgs e) => Close();
}
