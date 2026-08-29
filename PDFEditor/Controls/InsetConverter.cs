using System;
using System.Globalization;
using System.Windows.Data;

namespace PDFEditor.Controls;

/// <summary>Subtracts a fixed inset (default 60px) from a double value; clamps to 100 min.</summary>
public class InsetConverter : IValueConverter
{
    public double Inset { get; set; } = 60;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && !double.IsNaN(d))
        {
            var inset = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : Inset;
            return Math.Max(100, d - inset);
        }
        return 100.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
