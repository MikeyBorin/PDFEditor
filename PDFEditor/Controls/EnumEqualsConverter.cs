using System;
using System.Globalization;
using System.Windows.Data;

namespace PDFEditor.Controls;

/// <summary>One-way IValueConverter that returns true when
/// value.ToString() == parameter (case-insensitive). Used to bind
/// MenuItem.IsChecked to a specific enum value.</summary>
public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
