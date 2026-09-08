using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PDFEditor.Controls;

/// <summary>Picks the correct FontFamily for a glyph based on its code point.
/// Code points E000–F8FF (Unicode Private Use Area) are Segoe MDL2 Assets
/// icons; everything else (regular Unicode symbols, emoji) renders in
/// Segoe UI Symbol with fallback to Segoe UI and Arial.
/// Segoe MDL2 Assets has empty "notdef" glyphs for regular Unicode chars,
/// which prevents WPF's font fallback — so the per-glyph pick is necessary.</summary>
public class GlyphFontConverter : IValueConverter
{
    private static readonly FontFamily Mdl2 = new("Segoe MDL2 Assets");
    private static readonly FontFamily Unicode = new("Segoe UI Symbol, Segoe UI, Arial");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        if (s.Length == 0) return Unicode;
        var c = s[0];
        return (c >= 0xE000 && c <= 0xF8FF) ? Mdl2 : Unicode;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
