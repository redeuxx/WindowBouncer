using System;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using WindowBouncer.Settings;
using WindowBouncer.Win32;
using Windows.UI.ViewManagement;

namespace WindowBouncer.Services;

public sealed class ThemeService
{
    private UISettings? _uiSettings;

    public static bool CurrentIsDark { get; private set; }

    public void Apply(AppTheme theme)
    {
        CurrentIsDark = theme == AppTheme.Dark || (theme == AppTheme.System && IsSystemDark());

        var element = CurrentIsDark ? ElementTheme.Dark : ElementTheme.Light;

        // Apply RequestedTheme to every open Window's root element
        foreach (var window in WindowRegistry.OpenWindows)
        {
            if (window.Content is FrameworkElement root)
                root.RequestedTheme = element;

            ApplyTitleBar(WinRT.Interop.WindowNative.GetWindowHandle(window));
        }

        // Listen to system theme changes when in System mode
        if (theme == AppTheme.System)
        {
            if (_uiSettings is null)
            {
                _uiSettings = new UISettings();
                _uiSettings.ColorValuesChanged += OnSystemColorChanged;
            }
        }
        else
        {
            if (_uiSettings is not null)
            {
                _uiSettings.ColorValuesChanged -= OnSystemColorChanged;
                _uiSettings = null;
            }
        }
    }

    private void OnSystemColorChanged(UISettings sender, object args)
    {
        var dispatcher = WindowRegistry.PrimaryDispatcher;
        dispatcher?.TryEnqueue(() =>
        {
            if (App.Settings.Current.Theme == AppTheme.System)
                Apply(AppTheme.System);
        });
    }

    public static void ApplyTitleBar(nint hwnd)
    {
        if (hwnd == 0) return;
        int value = CurrentIsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }
}
