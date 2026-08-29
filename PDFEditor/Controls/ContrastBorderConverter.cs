using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PDFEditor.Controls;

/// <summary>Given a Color, returns a Brush that contrasts (black on light, white on dark).</summary>
public class ContrastBorderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color c)
        {
            var luma = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
            return luma > 140 ? Brushes.Black : Brushes.White;
        }
        return Brushes.Gray;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Color c ? new SolidColorBrush(c) : Brushes.Transparent;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
