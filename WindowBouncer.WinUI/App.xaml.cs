using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using WindowBouncer.Hotkeys;
using WindowBouncer.Services;
using WindowBouncer.Settings;
using WindowBouncer.Tray;
using WindowBouncer.ViewModels;
using WindowBouncer.Views;

namespace WindowBouncer;

public partial class App : Application
{
    private static Mutex? _instanceMutex;

    public static SettingsService Settings { get; } = new();
    public static WindowService WindowService { get; private set; } = null!;
    public static TrayIconService TrayIcon { get; private set; } = null!;
    public static HotkeyService Hotkeys { get; private set; } = null!;
    public static ThemeService Theme { get; } = new();

    public static MainWindow? MainAppWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cliArgs = Environment.GetCommandLineArgs();
        var flags = ParseArgs(cliArgs);

        _instanceMutex = new Mutex(initiallyOwned: true, "WindowBouncer_SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            _instanceMutex.Dispose();
            Exit();
            return;
        }

        Settings.Load();

        if (Settings.Current.RunAsAdmin && !ElevationService.IsElevated())
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { }
            if (ElevationService.IsTaskRegistered())
                ElevationService.LaunchViaTask();
            else
            {
                Settings.Current.RunAsAdmin = false;
                Settings.Save();
            }
            Exit();
            return;
        }

        Theme.Apply(Settings.Current.Theme);
        WindowService = new WindowService(Settings);

        if (flags.HasFlag(CliFlag.RegisterStartup))
            ApplyInstallerStartup();

        if (flags.HasFlag(CliFlag.Quit))
        {
            Exit();
            return;
        }

        if (flags.HasFlag(CliFlag.Close))
        {
            RunSilentAndExit();
            return;
        }

        var vm = new MainViewModel(WindowService);
        var window = new MainWindow(vm);
        MainAppWindow = window;
        window.SetCloseToTray(Settings.Current.CloseToTray);

        TrayIcon = new TrayIconService(WindowService);
        TrayIcon.SetMainWindow(window);
        TrayIcon.ShowSettingsRequested += (_, _) => ShowSettings();

        Hotkeys = new HotkeyService();
        Hotkeys.Initialize();
        Hotkeys.ApplySettings(Settings.Current);
        Hotkeys.CloseAllTriggered += async (_, _) =>
        {
            var windows = await WindowService.GetWindowsAsync();
            await WindowService.CloseAllAsync(windows);
        };

        bool startHidden = flags.HasFlag(CliFlag.NoUi) || Settings.Current.StartMinimized;

        if (Settings.Current.RestoreWindowOnNextLaunch)
        {
            startHidden = false;
            Settings.Current.RestoreWindowOnNextLaunch = false;
            Settings.Save();
        }

        // WinUI 3 keeps the message pump alive as long as a Window is active, so we have
        // to Activate even when starting hidden. Park the window off-screen first so the
        // unavoidable activation frame doesn't flash on the user's display.
        if (startHidden)
        {
            window.PrepareHiddenStartup();
            window.Activate();
            window.HideToTray();
        }
        else
        {
            window.Activate();
            _ = window.InitializeAsync();
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"Unhandled: {e.Exception}");
    }

    // CLI ACTIONS

    private static void ApplyInstallerStartup()
    {
        if (Settings.Current.StartWithWindows) return;
        Settings.Current.StartWithWindows = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            key?.SetValue("WindowBouncer", $"\"{exePath}\" /NOUI");
        }
        catch { }
        Settings.Save();
    }

    private static async void RunSilentAndExit()
    {
        var windows = await WindowService.GetWindowsAsync();
        await WindowService.CloseAllAsync(windows);
        ((App)Current).Exit();
    }

    [Flags]
    private enum CliFlag { None = 0, NoUi = 1, Close = 2, RegisterStartup = 4, Quit = 8 }

    private static CliFlag ParseArgs(string[] args)
    {
        var flags = CliFlag.None;
        foreach (var arg in args)
        {
            flags |= arg.ToUpperInvariant() switch
            {
                "/NOUI"             or "--NOUI"             => CliFlag.NoUi,
                "/CLOSE"            or "--CLOSE"            => CliFlag.Close,
                "/REGISTERSTARTUP"  or "--REGISTERSTARTUP"  => CliFlag.RegisterStartup,
                "/QUIT"             or "--QUIT"             => CliFlag.Quit,
                _                                            => CliFlag.None
            };
        }
        return flags;
    }

    internal static void ShowSettings()
    {
        var vm = new SettingsViewModel(Settings);
        var win = new SettingsWindow(vm);
        vm.CloseRequested += (_, _) =>
        {
            Hotkeys.ApplySettings(Settings.Current);
            Theme.Apply(Settings.Current.Theme);
            if (MainAppWindow is MainWindow mw)
                mw.SetCloseToTray(Settings.Current.CloseToTray);

            if (Settings.Current.RunAsAdmin && !ElevationService.IsElevated() && ElevationService.IsTaskRegistered())
            {
                Settings.Current.RestoreWindowOnNextLaunch = true;
                Settings.Save();
                try { _instanceMutex?.ReleaseMutex(); } catch { }
                ElevationService.LaunchViaTask();
                Shutdown();
            }
        };
        OwnedDialog.CenterOver(win, MainAppWindow);
        win.Activate();
    }

    internal static void Shutdown()
    {
        try
        {
            Hotkeys?.Dispose();
            TrayIcon?.Dispose();
            Settings.Save();
        }
        finally
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { }
            _instanceMutex?.Dispose();
        }
        ((App)Current).Exit();
    }
}
