using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WindowBouncer.Services;

internal static class WindowRegistry
{
    private static readonly List<Window> _windows = new();

    public static IReadOnlyList<Window> OpenWindows => _windows;

    public static DispatcherQueue? PrimaryDispatcher
        => _windows.Count > 0 ? _windows[0].DispatcherQueue : null;

    public static void Add(Window window)
    {
        if (!_windows.Contains(window))
            _windows.Add(window);
        window.Closed += OnClosed;
    }

    private static void OnClosed(object sender, WindowEventArgs args)
    {
        if (sender is Window window)
        {
            window.Closed -= OnClosed;
            _windows.Remove(window);
        }
    }
}
