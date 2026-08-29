using System;
using System.Globalization;
using System.Windows.Data;

namespace PDFEditor.Controls;

/// <summary>-1 → 0, otherwise n+1. Used to display 0-based indices as 1-based counts.</summary>
public class OneBasedIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int n) return n < 0 ? 0 : n + 1;
        return 0;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
