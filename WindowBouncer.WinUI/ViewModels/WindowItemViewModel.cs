using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WindowBouncer.Win32;
using Windows.Storage.Streams;

namespace WindowBouncer.ViewModels;

public sealed partial class WindowItemViewModel : ObservableObject
{
    private readonly Action<WindowItemViewModel> _onClose;

    public WindowInfo Info { get; }

    public nint Handle      => Info.Handle;
    public string Title       => Info.Title;
    public string ProcessName => Info.ProcessName;
    public uint ProcessId    => Info.ProcessId;
    public bool IsMinimized  => Info.IsMinimized;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsClosing { get; set; }

    private BitmapImage? _processIcon;
    private bool _iconLoaded;

    public BitmapImage? ProcessIcon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _processIcon = LoadProcessIcon(Info.ProcessId);
            }
            return _processIcon;
        }
    }

    public WindowItemViewModel(WindowInfo info, Action<WindowItemViewModel> onClose)
    {
        Info = info;
        _onClose = onClose;
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => _onClose(this);

    private bool CanClose() => !IsClosing;

    partial void OnIsClosingChanged(bool value) => CloseCommand.NotifyCanExecuteChanged();

    private static BitmapImage? LoadProcessIcon(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            var exePath = proc.MainModule?.FileName;
            if (exePath is null) return null;

            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;

            using var bmp = icon.ToBitmap();
            using var ms  = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bitmapImage = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(ms.ToArray());
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }
            stream.Seek(0);
            bitmapImage.SetSource(stream);
            return bitmapImage;
        }
        catch
        {
            return null;
        }
    }
}
