using System;
using System.Globalization;
using System.Windows.Data;
using PDFEditor.ViewModels;

namespace PDFEditor.Controls;

/// <summary>Returns the display width of a page in DIPs given the current
/// ZoomMode, custom zoom multiplier, viewport size, and page point dimensions.
/// Bindings, in order:
///   [0] viewportWidth  (double, DIPs)
///   [1] viewportHeight (double, DIPs)
///   [2] zoomMode       (ZoomMode)
///   [3] customZoom     (double, 1.0 = 100%)
///   [4] pageWidthPt    (double)
///   [5] pageHeightPt   (double)
/// Parameter is the inset (padding) around the page in DIPs (default 24).
/// The corresponding <see cref="ZoomHeightConverter"/> handles height.</summary>
public class ZoomWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var (w, h) = Compute(values, parameter);
        return double.IsNaN(w) || w < 1 ? 100.0 : w;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static (double W, double H) Compute(object[] values, object parameter)
    {
        if (values == null || values.Length < 6) return (double.NaN, double.NaN);
        double vw   = values[0] is double d0 ? d0 : 0;
        double vh   = values[1] is double d1 ? d1 : 0;
        var mode    = values[2] is ZoomMode z ? z : ZoomMode.FitWidth;
        double cz   = values[3] is double d3 && d3 > 0 ? d3 : 1.0;
        double pwPt = values[4] is double d4 && d4 > 0 ? d4 : 612;
        double phPt = values[5] is double d5 && d5 > 0 ? d5 : 792;

        double inset = 24;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            inset = p;

        // Available viewport area for the page (subtract padding once per side).
        double availW = Math.Max(50, vw - inset * 2);
        double availH = Math.Max(50, vh - inset * 2);

        // Actual-size scale: 1 point == 96/72 DIPs.
        const double DipsPerPoint = 96.0 / 72.0;
        double actualW = pwPt * DipsPerPoint;
        double actualH = phPt * DipsPerPoint;

        return mode switch
        {
            ZoomMode.ActualSize => (actualW, actualH),
            ZoomMode.Custom     => (actualW * cz, actualH * cz),
            ZoomMode.FitWidth   => (availW, availW * (phPt / pwPt)),
            ZoomMode.FitPage    => FitInside(availW, availH, pwPt, phPt),
            _                   => (availW, availW * (phPt / pwPt)),
        };
    }

    private static (double W, double H) FitInside(double availW, double availH, double pwPt, double phPt)
    {
        double scale = Math.Min(availW / pwPt, availH / phPt);
        return (pwPt * scale, phPt * scale);
    }
}

/// <summary>Height counterpart to <see cref="ZoomWidthConverter"/>. Same
/// bindings, returns the height component.</summary>
public class ZoomHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var (_, h) = ZoomWidthConverter.Compute(values, parameter);
        return double.IsNaN(h) || h < 1 ? 100.0 : h;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
