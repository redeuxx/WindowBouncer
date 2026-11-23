using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WindowBouncer.Services;
using WindowBouncer.Settings;

namespace WindowBouncer.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WindowBouncer";

    private readonly SettingsService _settingsService;

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool StartMinimized { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool RunAsAdmin { get; set; }

    // Run as admin is only offered when non-admins can't replace the exe (see ElevationService).
    public bool CanRunAsAdmin { get; } = ElevationService.IsInstallLocationProtected(Environment.ProcessPath);

    public string RunAsAdminHint => CanRunAsAdmin
        ? "Registers a scheduled task that runs the app elevated. Requires one-time UAC approval."
        : "Unavailable: WindowBouncer is installed in a folder your account can write to, so an elevated task would let any program gain admin rights. It needs a folder only administrators can modify, such as Program Files.";

    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>().ToList();

    public HotKeyViewModel HotKeyCloseAll { get; } = new();

    public ObservableCollection<string> ExcludedProcessNames { get; } = new();
    public ObservableCollection<string> ExcludedTitlePatterns { get; } = new();

    [ObservableProperty]
    public partial string NewProcessName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewTitlePattern { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SelectedProcess { get; set; }

    [ObservableProperty]
    public partial string? SelectedTitle { get; set; }

    // Shown next to Save when the new hotkey could not be registered.
    [ObservableProperty]
    public partial string HotkeyError { get; set; } = string.Empty;

    // Registers the hotkey for real; returns false if Windows or another app already owns it.
    public Func<HotKeyConfig, bool>? ApplyHotkey { get; init; }

    // Called with true while a new hotkey is being captured, false when capture ends.
    public Action<bool>? SuspendHotkey { get; init; }

    public event EventHandler? CloseRequested;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadFromSettings();
        HotKeyCloseAll.PropertyChanged += (_, e) =>
        {
            HotkeyError = string.Empty;
            if (e.PropertyName == nameof(HotKeyViewModel.IsCapturing))
                SuspendHotkey?.Invoke(HotKeyCloseAll.IsCapturing);
        };
    }

    partial void OnNewProcessNameChanged(string value) => AddProcessCommand.NotifyCanExecuteChanged();
    partial void OnNewTitlePatternChanged(string value) => AddTitleCommand.NotifyCanExecuteChanged();
    partial void OnSelectedProcessChanged(string? value) => RemoveProcessCommand.NotifyCanExecuteChanged();
    partial void OnSelectedTitleChanged(string? value) => RemoveTitleCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanAddProcess))]
    private void AddProcess()
    {
        if (string.IsNullOrWhiteSpace(NewProcessName)) return;
        ExcludedProcessNames.Add(NewProcessName.Trim());
        NewProcessName = string.Empty;
    }

    private bool CanAddProcess() => !string.IsNullOrWhiteSpace(NewProcessName);

    [RelayCommand(CanExecute = nameof(CanRemoveProcess))]
    private void RemoveProcess()
    {
        if (SelectedProcess is not null)
            ExcludedProcessNames.Remove(SelectedProcess);
    }

    private bool CanRemoveProcess() => SelectedProcess is not null;

    [RelayCommand(CanExecute = nameof(CanAddTitle))]
    private void AddTitle()
    {
        if (string.IsNullOrWhiteSpace(NewTitlePattern)) return;
        ExcludedTitlePatterns.Add(NewTitlePattern.Trim());
        NewTitlePattern = string.Empty;
    }

    private bool CanAddTitle() => !string.IsNullOrWhiteSpace(NewTitlePattern);

    [RelayCommand(CanExecute = nameof(CanRemoveTitle))]
    private void RemoveTitle()
    {
        if (SelectedTitle is not null)
            ExcludedTitlePatterns.Remove(SelectedTitle);
    }

    private bool CanRemoveTitle() => SelectedTitle is not null;

    [RelayCommand]
    private void Save()
    {
        // Try the hotkey first: if it's taken, keep the window open and save nothing, so the
        // settings never claim a hotkey that doesn't work. The old one stays registered.
        var hotkey = HotKeyCloseAll.ToConfig();
        if (ApplyHotkey is not null && !ApplyHotkey(hotkey))
        {
            HotkeyError = $"{HotKeyCloseAll.DisplayText} is already in use by Windows or another app. Pick another hotkey.";
            return;
        }

        var s = _settingsService.Current;

        s.StartMinimized = StartMinimized;
        s.CloseToTray = CloseToTray;
        s.Theme = Theme;
        s.HotKeyCloseAll = hotkey;
        s.ExcludedProcessNames = [.. ExcludedProcessNames];
        s.ExcludedTitlePatterns = [.. ExcludedTitlePatterns];
        s.StartWithWindows = StartWithWindows;
        s.RunAsAdmin = RunAsAdmin;

        ApplyStartupSettings();
        _settingsService.Save();

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void LoadFromSettings()
    {
        var s = _settingsService.Current;

        RunAsAdmin = s.RunAsAdmin && CanRunAsAdmin;
        StartWithWindows = RunAsAdmin ? s.StartWithWindows : IsStartupRegistered();
        StartMinimized = s.StartMinimized;
        CloseToTray = s.CloseToTray;
        Theme = s.Theme;

        HotKeyCloseAll.LoadFrom(s.HotKeyCloseAll);

        ExcludedProcessNames.Clear();
        foreach (var p in s.ExcludedProcessNames) ExcludedProcessNames.Add(p);

        ExcludedTitlePatterns.Clear();
        foreach (var t in s.ExcludedTitlePatterns) ExcludedTitlePatterns.Add(t);
    }

    private void ApplyStartupSettings()
    {
        if (RunAsAdmin && ElevationService.RegisterTask(Environment.ProcessPath ?? string.Empty, StartWithWindows))
        {
            // The task now handles autostart; drop the Run key only once it exists.
            RemoveRegistryStartup();
        }
        else
        {
            // Either admin mode is off or registration failed (UAC cancelled, unprotected folder).
            RunAsAdmin = false;
            _settingsService.Current.RunAsAdmin = false;
            if (ElevationService.IsTaskRegistered())
                ElevationService.UnregisterTask();
            SetRegistryStartup(StartWithWindows);
        }
    }

    private static bool IsStartupRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) is not null;
        }
        catch { return false; }
    }

    internal static void SetRegistryStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? string.Empty;
                key.SetValue(AppName, $"\"{exePath}\" /NOUI");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch { }
    }

    private static void RemoveRegistryStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }
    }
}
