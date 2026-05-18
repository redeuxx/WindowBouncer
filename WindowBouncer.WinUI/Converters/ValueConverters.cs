using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace WindowBouncer.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, string language)
        => value is bool b && !b;

    public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        => value is bool b && !b;
}

public sealed class BoolToSelectionBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    public object Convert(object value, System.Type targetType, object parameter, string language)
    {
        if (value is true
            && Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var brush)
            && brush is Brush b)
        {
            return b;
        }
        return Transparent;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        => throw new System.NotSupportedException();
}

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, string language)
        => value is true ? 0.4d : 1.0d;

    public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        => throw new System.NotSupportedException();
}
