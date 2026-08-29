using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PDFEditor.Controls;

/// <summary>
/// MultiValueConverter that returns AccentBrush when value1 == value2 (case-insensitive string compare),
/// otherwise Transparent. Used to visually highlight the selected toolbar button/swatch.
/// </summary>
public class EqualityToBrushConverter : IMultiValueConverter
{
    public Brush ActiveBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x4C, 0x8B, 0xF5));
    public Brush InactiveBrush { get; set; } = Brushes.Transparent;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2) return InactiveBrush;
        var a = values[0]?.ToString();
        var b = values[1]?.ToString();
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? ActiveBrush : InactiveBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
