using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PDFEditor.Controls;

/// <summary>Bool → Visibility, inverted: true → Collapsed, false → Visible.
/// Used to hide labels when the Tools list is in icons-only mode.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
