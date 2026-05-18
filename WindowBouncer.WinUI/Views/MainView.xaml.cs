using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        var about = new AboutWindow();
        OwnedDialog.CenterOver(about, App.MainAppWindow);
        about.Activate();
    }
}
