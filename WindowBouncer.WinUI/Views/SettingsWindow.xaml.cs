using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
using WindowBouncer.Services;
using WindowBouncer.ViewModels;

namespace WindowBouncer.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        WindowRegistry.Add(this);

        Title = "WindowBouncer Settings";

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        try
        {
            appWindow.SetIcon(System.IO.Path.Combine(
                AppContext.BaseDirectory, "Resources", "appicon.ico"));
        }
        catch { }
        appWindow.Resize(new SizeInt32 { Width = 540, Height = 560 });

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentIsDark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeService.ApplyTitleBar(hwnd);

        View.ViewModel = ViewModel;
        ViewModel.CloseRequested += (_, _) => Close();
    }
}
