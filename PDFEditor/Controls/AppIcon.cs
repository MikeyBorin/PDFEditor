using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDFEditor.Controls;

/// <summary>Renders a simple PDF-Editor icon at runtime for Window.Icon / taskbar use.</summary>
public static class AppIcon
{
    public static BitmapSource Create(int size = 256)
    {
        var vis = new DrawingVisual();
        using (var ctx = vis.RenderOpen())
        {
            // Rounded blue square background
            var bg = new SolidColorBrush(Color.FromRgb(0x4C, 0x8B, 0xF5));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x6A, 0xD8)), size * 0.02);
            var radius = size * 0.16;
            ctx.DrawRoundedRectangle(bg, pen, new Rect(0, 0, size, size), radius, radius);

            // Small "document corner fold" hint (top-right)
            var fold = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(size * 0.72, size * 0.14), IsClosed = true, IsFilled = true };
            fig.Segments.Add(new LineSegment(new Point(size * 0.88, size * 0.14), true));
            fig.Segments.Add(new LineSegment(new Point(size * 0.88, size * 0.30), true));
            fold.Figures.Add(fig);
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), null, fold);

            // "PDF" text
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var pdf = new FormattedText(
                "PDF",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                size * 0.34,
                Brushes.White,
                1.0);
            var textX = (size - pdf.Width) / 2.0;
            var textY = size * 0.32;
            ctx.DrawText(pdf, new Point(textX, textY));

            // Underline
            ctx.DrawLine(new Pen(Brushes.White, size * 0.03),
                new Point(size * 0.20, size * 0.78),
                new Point(size * 0.80, size * 0.78));
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(vis);
        bmp.Freeze();
        return bmp;
    }
}
