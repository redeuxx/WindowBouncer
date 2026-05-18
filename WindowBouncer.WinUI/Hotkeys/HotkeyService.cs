using System;
using WindowBouncer.Settings;
using WindowBouncer.Win32;

namespace WindowBouncer.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int IdCloseAll = 0x4201;

    private Win32MessageWindow? _messageWindow;
    private bool _closeAllRegistered;

    public event EventHandler? CloseAllTriggered;

    public void Initialize()
    {
        _messageWindow = new Win32MessageWindow("WindowBouncer_HotkeyHost");
        _messageWindow.MessageReceived += OnMessageReceived;
    }

    public void ApplySettings(AppSettings settings)
    {
        UnregisterAll();

        if (_messageWindow is null) return;

        if (settings.HotKeyCloseAll.IsSet)
        {
            uint mods = BuildModifiers(settings.HotKeyCloseAll) | NativeMethods.MOD_NOREPEAT;
            _closeAllRegistered = NativeMethods.RegisterHotKey(
                _messageWindow.Handle, IdCloseAll, mods, settings.HotKeyCloseAll.VirtualKey);
        }
    }

    private void UnregisterAll()
    {
        if (_messageWindow is null) return;

        if (_closeAllRegistered)
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, IdCloseAll);
            _closeAllRegistered = false;
        }
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
        UnregisterAll();
        if (_messageWindow is not null)
        {
            _messageWindow.MessageReceived -= OnMessageReceived;
            _messageWindow.Dispose();
            _messageWindow = null;
        }
    }
}
