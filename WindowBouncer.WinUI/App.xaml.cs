using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.UI.Xaml;
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
    private static SettingsWindow? _settingsWindow;

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

        Settings.Load();
        WindowService = new WindowService(Settings);

        // /CLOSE is a one-shot action that needs no singleton, so it works while the tray
        // instance is running.
        if (flags.HasFlag(CliFlag.Close))
        {
            RunSilentAndExit();
            return;
        }

#if DEBUG
        const string mutexName = "WindowBouncer_SingleInstance_Debug";
#else
        const string mutexName = "WindowBouncer_SingleInstance";
#endif
        bool isNewInstance;
        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, mutexName, out isNewInstance);
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated instance created the mutex and its DACL keeps us out: it is running.
            Exit();
            return;
        }
        // An instance started by the task waits for the instance that launched it to exit.
        if (!isNewInstance && !(flags.HasFlag(CliFlag.FromTask) && WaitForInstanceMutex()))
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Exit();
            return;
        }

        // Installer actions run before the elevation redirect, which would otherwise start a
        // full elevated instance instead of quitting.
        if (flags.HasFlag(CliFlag.RegisterStartup))
            ApplyInstallerStartup();

        if (flags.HasFlag(CliFlag.Quit))
        {
            Exit();
            return;
        }

#if !DEBUG
        if (Settings.Current.RunAsAdmin && RedirectToElevatedInstance(flags))
        {
            Exit();
            return;
        }
#endif

        Theme.Apply(Settings.Current.Theme);

        var vm = new MainViewModel(WindowService);
        var window = new MainWindow(vm);
        MainAppWindow = window;
        window.SetCloseToTray(Settings.Current.CloseToTray);

        TrayIcon = new TrayIconService(WindowService);
        TrayIcon.SetMainWindow(window);
        TrayIcon.ShowSettingsRequested += (_, _) => ShowSettings();

        Hotkeys = new HotkeyService();
        Hotkeys.Initialize();
        if (!Hotkeys.Apply(Settings.Current.HotKeyCloseAll))
            TrayIcon.ShowNotification("Close All hotkey not active",
                "The configured hotkey is in use by Windows or another app. Pick another one in Settings.");
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

    // ELEVATION

    private static bool WaitForInstanceMutex()
    {
        try { return _instanceMutex!.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { return true; }
    }

    private static void ReleaseInstanceMutex()
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
    }

    // Returns true when an elevated instance was started and this one should exit.
    private static bool RedirectToElevatedInstance(CliFlag flags)
    {
        if (ElevationService.IsElevated())
        {
            // A task registered by an older version for a user-writable install folder.
            // Remove it now, while that needs no UAC prompt.
            if (!ElevationService.IsInstallLocationProtected(Environment.ProcessPath))
            {
                ElevationService.UnregisterTask();
                DisableRunAsAdmin();
            }
            return false;
        }

        // Started by the task but still not elevated (e.g. a standard user, where HighestAvailable
        // gives a limited token). Redirecting again would loop forever.
        if (flags.HasFlag(CliFlag.FromTask))
        {
            ElevationService.UnregisterTask();
            DisableRunAsAdmin();
            return false;
        }

        if (!ElevationService.IsTaskRegistered())
        {
            DisableRunAsAdmin();
            return false;
        }

        // The task passes /NOUI when autostart is on; a manual launch should still show the window.
        if (!flags.HasFlag(CliFlag.NoUi))
        {
            Settings.Current.RestoreWindowOnNextLaunch = true;
            Settings.Save();
        }

        if (ElevationService.LaunchViaTask())
        {
            ReleaseInstanceMutex();
            return true;
        }

        // Task disabled or broken: run unelevated this time rather than not at all.
        Settings.Current.RestoreWindowOnNextLaunch = false;
        Settings.Save();
        return false;
    }

    private static void DisableRunAsAdmin()
    {
        Settings.Current.RunAsAdmin = false;
        // The task was providing autostart; hand it back to the Run key.
        if (Settings.Current.StartWithWindows)
            SettingsViewModel.SetRegistryStartup(true);
        Settings.Save();
    }

    // CLI ACTIONS

    private static void ApplyInstallerStartup()
    {
        if (Settings.Current.StartWithWindows) return;
        Settings.Current.StartWithWindows = true;
        SettingsViewModel.SetRegistryStartup(true);
        Settings.Save();
    }

    private static async void RunSilentAndExit()
    {
        var windows = await WindowService.GetWindowsAsync();
        await WindowService.CloseAllAsync(windows);
        ((App)Current).Exit();
    }

    [Flags]
    private enum CliFlag { None = 0, NoUi = 1, Close = 2, RegisterStartup = 4, Quit = 8, FromTask = 16 }

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
                ElevationService.FromTaskArg                => CliFlag.FromTask,
                _                                            => CliFlag.None
            };
        }
        return flags;
    }

    internal static void ShowSettings()
    {
        // One Settings window at a time, so two copies can't overwrite each other's changes.
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(Settings) { ApplyHotkey = Hotkeys.Apply };
        var win = new SettingsWindow(vm);
        _settingsWindow = win;
        win.Closed += (_, _) => _settingsWindow = null;
        vm.CloseRequested += (_, _) =>
        {
            Theme.Apply(Settings.Current.Theme);
            if (MainAppWindow is MainWindow mw)
                mw.SetCloseToTray(Settings.Current.CloseToTray);

            if (Settings.Current.RunAsAdmin && !ElevationService.IsElevated() && ElevationService.IsTaskRegistered())
            {
                Settings.Current.RestoreWindowOnNextLaunch = true;
                Settings.Save();
                // The new instance waits for our mutex, which Shutdown releases.
                if (ElevationService.LaunchViaTask())
                {
                    Shutdown();
                    return;
                }
                Settings.Current.RestoreWindowOnNextLaunch = false;
                Settings.Save();
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
            ReleaseInstanceMutex();
        }
        ((App)Current).Exit();
    }
}
