using System;
using System.Collections.Generic;
using Windows.System;

namespace WindowBouncer.Hotkeys;

internal static class VirtualKeyNames
{
    private static readonly Dictionary<uint, string> _overrides = new()
    {
        [(uint)VirtualKey.Space]    = "Space",
        [(uint)VirtualKey.Enter]    = "Enter",
        [(uint)VirtualKey.Tab]      = "Tab",
        [(uint)VirtualKey.Back]     = "Backspace",
        [(uint)VirtualKey.Escape]   = "Esc",
        [(uint)VirtualKey.Up]       = "Up",
        [(uint)VirtualKey.Down]     = "Down",
        [(uint)VirtualKey.Left]     = "Left",
        [(uint)VirtualKey.Right]    = "Right",
        [(uint)VirtualKey.PageUp]   = "PageUp",
        [(uint)VirtualKey.PageDown] = "PageDown",
        [(uint)VirtualKey.Home]     = "Home",
        [(uint)VirtualKey.End]      = "End",
        [(uint)VirtualKey.Insert]   = "Insert",
        [(uint)VirtualKey.Delete]   = "Delete",
    };

    public static string FromVirtualKey(uint vk)
    {
        if (_overrides.TryGetValue(vk, out var name))
            return name;

        var key = (VirtualKey)vk;
        if (key >= VirtualKey.F1 && key <= VirtualKey.F24)
            return key.ToString();

        if (vk >= 0x30 && vk <= 0x39) // 0-9
            return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) // A-Z
            return ((char)vk).ToString();

        return key.ToString();
    }

    public static bool IsModifier(VirtualKey key) => key switch
    {
        VirtualKey.Control      => true,
        VirtualKey.LeftControl  => true,
        VirtualKey.RightControl => true,
        VirtualKey.Shift        => true,
        VirtualKey.LeftShift    => true,
        VirtualKey.RightShift   => true,
        VirtualKey.Menu         => true,
        VirtualKey.LeftMenu     => true,
        VirtualKey.RightMenu    => true,
        VirtualKey.LeftWindows  => true,
        VirtualKey.RightWindows => true,
        _                        => false
    };
}
