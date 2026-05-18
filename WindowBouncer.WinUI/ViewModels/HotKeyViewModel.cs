using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowBouncer.Hotkeys;
using WindowBouncer.Settings;
using Windows.System;

namespace WindowBouncer.ViewModels;

public sealed partial class HotKeyViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool Alt { get; set; }

    [ObservableProperty]
    public partial bool Ctrl { get; set; }

    [ObservableProperty]
    public partial bool Shift { get; set; }

    [ObservableProperty]
    public partial bool Win { get; set; }

    [ObservableProperty]
    public partial uint VirtualKey { get; set; }

    [ObservableProperty]
    public partial bool IsCapturing { get; set; }

    public bool IsSet => VirtualKey != 0;
    public string CaptureButtonLabel => IsCapturing ? "Press a key..." : "Set";

    public string DisplayText
    {
        get
        {
            if (VirtualKey == 0) return "(none)";
            var parts = new System.Collections.Generic.List<string>();
            if (Ctrl)  parts.Add("Ctrl");
            if (Alt)   parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win)   parts.Add("Win");
            parts.Add(VirtualKeyNames.FromVirtualKey(VirtualKey));
            return string.Join(" + ", parts);
        }
    }

    partial void OnVirtualKeyChanged(uint value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(IsSet));
        ClearCommand.NotifyCanExecuteChanged();
    }

    partial void OnAltChanged(bool value)   => OnPropertyChanged(nameof(DisplayText));
    partial void OnCtrlChanged(bool value)  => OnPropertyChanged(nameof(DisplayText));
    partial void OnShiftChanged(bool value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnWinChanged(bool value)   => OnPropertyChanged(nameof(DisplayText));
    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(CaptureButtonLabel));

    public void CaptureKey(VirtualKey key, bool ctrl, bool alt, bool shift, bool win)
    {
        if (VirtualKeyNames.IsModifier(key))
            return;

        Ctrl  = ctrl;
        Alt   = alt;
        Shift = shift;
        Win   = win;
        VirtualKey = (uint)key;
        IsCapturing = false;
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Alt = Ctrl = Shift = Win = false;
        VirtualKey = 0;
        IsCapturing = false;
    }

    private bool CanClear() => IsSet;

    public void LoadFrom(HotKeyConfig config)
    {
        Alt = config.Alt; Ctrl = config.Ctrl; Shift = config.Shift; Win = config.Win;
        VirtualKey = config.VirtualKey;
    }

    public HotKeyConfig ToConfig() => new()
    {
        Alt = Alt, Ctrl = Ctrl, Shift = Shift, Win = Win, VirtualKey = VirtualKey
    };
}
