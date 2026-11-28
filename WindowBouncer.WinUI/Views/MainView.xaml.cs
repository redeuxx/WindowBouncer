using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WindowBouncer.Services;
using WindowBouncer.ViewModels;

namespace WindowBouncer.Views;

public sealed partial class MainView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MainViewModel),
            typeof(MainView),
            new PropertyMetadata(null, OnViewModelChanged));

    public MainViewModel ViewModel
    {
        get => (MainViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MainView()
    {
        InitializeComponent();
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MainView view) return;

        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.Windows.CollectionChanged -= view.OnWindowsChanged;
            oldVm.PropertyChanged           -= view.OnViewModelPropertyChanged;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            newVm.Windows.CollectionChanged += view.OnWindowsChanged;
            newVm.PropertyChanged           += view.OnViewModelPropertyChanged;
            view.UpdateSelectAllHeader();
        }
    }

    private void OnWindowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => UpdateSelectAllHeader();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedCount)
            || e.PropertyName == nameof(MainViewModel.TotalCount))
        {
            UpdateSelectAllHeader();
        }
    }

    private void UpdateSelectAllHeader()
    {
        if (ViewModel is null) return;

        if (ViewModel.Windows.Count == 0)
        {
            SelectAllHeader.IsChecked = false;
            return;
        }

        if (ViewModel.SelectedCount == 0)
            SelectAllHeader.IsChecked = false;
        else if (ViewModel.SelectedCount == ViewModel.Windows.Count)
            SelectAllHeader.IsChecked = true;
        else
            SelectAllHeader.IsChecked = null;
    }

    private void SelectAllHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && ViewModel is not null)
        {
            if (cb.IsChecked == true)
                ViewModel.SelectAllCommand.Execute(null);
            else
                ViewModel.SelectNoneCommand.Execute(null);
        }
    }

    // Right-click on a row, or the context menu key / Shift+F10 on the focused row.
    private void WindowList_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        // Mouse: a text or image inside the row template. Keyboard: the ListViewItem container.
        var source = e.OriginalSource as FrameworkElement;
        var row = source is ListViewItem container ? WindowList.ItemFromContainer(container) : source?.DataContext;
        if (row is not WindowItemViewModel item || string.IsNullOrEmpty(item.ProcessName))
            return;

        var exclude = new MenuFlyoutItem { Text = $"Exclude \"{item.ProcessName}\"" };
        ToolTipService.SetToolTip(exclude, "Hide all windows of this app from the list and from Close All. Undo in Settings > Exclude List.");
        exclude.Click += (_, _) =>
        {
            App.ExcludeProcess(item.ProcessName);
            // While a close is running, its own refresh at the end drops the excluded rows.
            if (ViewModel.RefreshCommand.CanExecute(null))
                ViewModel.RefreshCommand.Execute(null);
        };

        var flyout = new MenuFlyout();
        flyout.Items.Add(exclude);
        if (e.TryGetPosition(sender, out var point))
            flyout.ShowAt(sender, new FlyoutShowOptions { Position = point });
        else
            flyout.ShowAt((FrameworkElement)e.OriginalSource);
        e.Handled = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.ShowSettings();

    private void HideToTray_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainAppWindow is MainWindow mw)
            mw.HideToTray();
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Exit WindowBouncer?",
            Content = "Are you sure you want to completely exit WindowBouncer? Your global keyboard shortcut for closing windows will no longer work.",
            PrimaryButtonText = "Exit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            App.Shutdown();
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        if (AboutWindow.OpenInstance is not null)
        {
            AboutWindow.OpenInstance.Activate();
            return;
        }
        var about = new AboutWindow();
        OwnedDialog.CenterOver(about, App.MainAppWindow);
        about.Activate();
    }
}
