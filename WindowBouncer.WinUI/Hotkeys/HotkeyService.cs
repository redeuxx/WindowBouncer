using System;
using WindowBouncer.Settings;
using WindowBouncer.Win32;

namespace WindowBouncer.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int IdCloseAll = 0x4201;

    private Win32MessageWindow? _messageWindow;
    private HotKeyConfig? _registered;

    public event EventHandler? CloseAllTriggered;

    public void Initialize()
    {
        _messageWindow = new Win32MessageWindow("WindowBouncer_HotkeyHost");
        _messageWindow.MessageReceived += OnMessageReceived;
    }

    // Registers config as the Close All hotkey. Returns false when it can't be registered
    // (taken by Windows or another app, or unsafe); the previous hotkey then stays active.
    // An unset config clears the hotkey and succeeds.
    public bool Apply(HotKeyConfig config)
    {
        if (_messageWindow is null) return false;

        var previous = _registered;
        Unregister();

        if (!config.IsSet) return true;
        if (config.IsSafe && Register(config)) return true;

        if (previous is not null) Register(previous);
        return false;
    }

    private bool Register(HotKeyConfig config)
    {
        uint mods = BuildModifiers(config) | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(_messageWindow!.Handle, IdCloseAll, mods, config.VirtualKey))
            return false;
        _registered = config;
        return true;
    }

    private void Unregister()
    {
        if (_messageWindow is null || _registered is null) return;
        NativeMethods.UnregisterHotKey(_messageWindow.Handle, IdCloseAll);
        _registered = null;
    }

    private nint OnMessageReceived(nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = (int)wParam;
            if (id == IdCloseAll)
            {
                CloseAllTriggered?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
        }
        return 0;
    }

    private static uint BuildModifiers(HotKeyConfig config)
    {
        uint mods = 0;
        if (config.Alt)   mods |= NativeMethods.MOD_ALT;
        if (config.Ctrl)  mods |= NativeMethods.MOD_CONTROL;
        if (config.Shift) mods |= NativeMethods.MOD_SHIFT;
        if (config.Win)   mods |= NativeMethods.MOD_WIN;
        return mods;
    }

    public void Dispose()
    {
        Unregister();
        if (_messageWindow is not null)
        {
            _messageWindow.MessageReceived -= OnMessageReceived;
            _messageWindow.Dispose();
            _messageWindow = null;
        }
    }
}
