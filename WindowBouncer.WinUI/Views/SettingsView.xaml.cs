using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WindowBouncer.ViewModels;

namespace WindowBouncer.Views;

public sealed partial class SettingsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(SettingsViewModel),
            typeof(SettingsView),
            new PropertyMetadata(null));

    public SettingsViewModel ViewModel
    {
        get => (SettingsViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public SettingsView()
    {
        InitializeComponent();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag as string;
        GeneralPane.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPane.Visibility = tag == "hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        ExcludePane.Visibility = tag == "exclude" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.HotKeyCloseAll.IsCapturing = true;
        CloseAllBox.Focus(FocusState.Programmatic);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var hk = ViewModel.HotKeyCloseAll;
        if (!hk.IsCapturing) return;

        e.Handled = true;

        var key = e.Key;
        bool ctrl  = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)     & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        bool alt   = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)        & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)       & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        bool win   = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down
                  || (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (key == VirtualKey.Escape)
        {
            hk.IsCapturing = false;
            return;
        }

        hk.CaptureKey(key, ctrl, alt, shift, win);
    }
}
