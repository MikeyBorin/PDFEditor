using System.IO;
using System.Windows.Media.Imaging;
using PDFtoImage;

namespace PDFEditor.Services;

public class PdfRenderService
{
    public int GetPageCount(byte[] pdfBytes) => Conversion.GetPageCount(pdfBytes);

    public BitmapSource RenderPage(byte[] pdfBytes, int pageIndex, int dpi = 150)
    {
        using var ms = new MemoryStream();
        var options = new RenderOptions(Dpi: dpi);
        Conversion.SavePng(ms, pdfBytes, page: pageIndex, options: options);
        ms.Position = 0;

        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    /// <summary>Renders a normalized 0..1 region of the given page to a cropped bitmap.</summary>
    public BitmapSource RenderRegion(byte[] pdfBytes, int pageIndex, double nx, double ny, double nw, double nh, int dpi = 200)
    {
        var full = RenderPage(pdfBytes, pageIndex, dpi);
        var x = (int)System.Math.Round(nx * full.PixelWidth);
        var y = (int)System.Math.Round(ny * full.PixelHeight);
        var w = (int)System.Math.Round(nw * full.PixelWidth);
        var h = (int)System.Math.Round(nh * full.PixelHeight);
        x = System.Math.Clamp(x, 0, full.PixelWidth - 1);
        y = System.Math.Clamp(y, 0, full.PixelHeight - 1);
        w = System.Math.Clamp(w, 1, full.PixelWidth - x);
        h = System.Math.Clamp(h, 1, full.PixelHeight - y);
        var cropped = new CroppedBitmap(full, new System.Windows.Int32Rect(x, y, w, h));
        cropped.Freeze();
        return cropped;
    }

    public BitmapSource RenderThumbnail(byte[] pdfBytes, int pageIndex, int width = 140)
    {
        using var ms = new MemoryStream();
        var options = new RenderOptions(Width: width);
        Conversion.SavePng(ms, pdfBytes, page: pageIndex, options: options);
        ms.Position = 0;

        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
