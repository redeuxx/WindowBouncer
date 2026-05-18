using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowBouncer.Services;
using WindowBouncer.Settings;

namespace WindowBouncer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly WindowService _windowService;

    public ObservableCollection<WindowItemViewModel> Windows { get; } = new();
    public ObservableCollection<WindowItemViewModel> FilteredWindows { get; } = new();

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public int TotalCount => FilteredWindows.Count;
    public int SelectedCount => Windows.Count(w => w.IsSelected);

    public MainViewModel(WindowService windowService)
    {
        _windowService = windowService;
        Windows.CollectionChanged += OnWindowsCollectionChanged;
    }

    partial void OnFilterTextChanged(string value) => RebuildFilter();

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CloseSelectedCommand.NotifyCanExecuteChanged();
        CloseAllCommand.NotifyCanExecuteChanged();
    }

    private void OnWindowsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (WindowItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnItemPropertyChanged;

        if (e.NewItems is not null)
            foreach (WindowItemViewModel item in e.NewItems)
                item.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            CloseSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private void RebuildFilter()
    {
        FilteredWindows.Clear();
        IEnumerable<WindowItemViewModel> source = Windows;

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var f = FilterText;
            source = source.Where(w =>
                w.Title.Contains(f, StringComparison.OrdinalIgnoreCase)
                || w.ProcessName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in source)
            FilteredWindows.Add(item);

        OnPropertyChanged(nameof(TotalCount));
        CloseAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var windows = await _windowService.GetWindowsAsync();
            Windows.Clear();
            foreach (var w in windows)
                Windows.Add(new WindowItemViewModel(w, OnCloseItem));

            RebuildFilter();
            OnPropertyChanged(nameof(SelectedCount));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCloseSelected))]
    private async Task CloseSelectedAsync()
    {
        IsBusy = true;
        try
        {
            var targets = Windows.Where(w => w.IsSelected).ToList();
            foreach (var item in targets)
                item.IsClosing = true;

            await _windowService.CloseAllAsync(targets.Select(w => w.Info));
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCloseSelected() => !IsBusy && SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanCloseAll))]
    private async Task CloseAllAsync()
    {
        IsBusy = true;
        try
        {
            await _windowService.CloseAllAsync(Windows.Select(w => w.Info));
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCloseAll() => !IsBusy && Windows.Count > 0;

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var w in FilteredWindows) w.IsSelected = true;
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var w in Windows) w.IsSelected = false;
        OnPropertyChanged(nameof(SelectedCount));
    }

    private async void OnCloseItem(WindowItemViewModel item)
    {
        item.IsClosing = true;
        await _windowService.CloseWindowAsync(item.Info);
        Windows.Remove(item);
        RebuildFilter();
        OnPropertyChanged(nameof(SelectedCount));
    }
}
