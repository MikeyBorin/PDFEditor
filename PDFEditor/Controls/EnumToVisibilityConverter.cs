using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PDFEditor.Controls;

/// <summary>One-way IValueConverter returning Visible when value.ToString()
/// equals the parameter (case-insensitive), Collapsed otherwise. Used to
/// swap the left-panel content between Thumbnails / Tools modes.</summary>
public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
