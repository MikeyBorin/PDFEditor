using System;
using System.Globalization;
using System.Windows.Data;

namespace PDFEditor.Controls;

/// <summary>Returns (viewportWidth - inset) * zoom. Bindings: [0]=viewportWidth (double), [1]=zoom (double).</summary>
public class ZoomWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 100.0;
        double vw = values[0] is double d ? d : 0;
        double zoom = values[1] is double z ? z : 1.0;
        if (zoom <= 0) zoom = 1.0;
        double inset = 60;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) inset = p;
        return Math.Max(100, (vw - inset) * zoom);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
