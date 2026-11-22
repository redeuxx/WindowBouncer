using System.Text.Json.Serialization;

namespace WindowBouncer.Settings;

public sealed class AppSettings
{
    public List<string> ExcludedProcessNames { get; set; } = [];

    public List<string> ExcludedTitlePatterns { get; set; } = [];

    public HotKeyConfig HotKeyCloseAll { get; set; } = new();

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool StartMinimized { get; set; } = false;

    public bool CloseToTray { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;

    public bool RunAsAdmin { get; set; } = false;

    public bool RestoreWindowOnNextLaunch { get; set; } = false;
}

public sealed class HotKeyConfig
{
    public bool Alt { get; set; }
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }

    // Virtual key code (0 = not set)
    public uint VirtualKey { get; set; }

    [JsonIgnore]
    public bool IsSet => VirtualKey != 0;

    [JsonIgnore]
    public bool IsSafe => IsSet && IsSafeCombo(Ctrl, Alt, Win, VirtualKey);

    // Without Ctrl, Alt or Win the hotkey would swallow that key (or Shift+key) in every app
    // and close all windows each time it is typed. F13-F24 are fine alone: nobody types them.
    public static bool IsSafeCombo(bool ctrl, bool alt, bool win, uint virtualKey)
        => ctrl || alt || win || virtualKey is >= 0x7C and <= 0x87;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    System,
    Light,
    Dark
}
