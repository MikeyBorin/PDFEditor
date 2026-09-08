using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace PDFEditor.Services;

// Converts any signature source image (JPG photo of pen on paper, opaque PNG,
// etc.) into a transparent PNG by making white / near-white pixels alpha=0.
// Uses a soft threshold so anti-aliased ink edges keep partial opacity instead
// of turning into a hard cut-out.
public static class SignatureImageProcessor
{
    // Pixels whose darkest channel is at/above `whiteThreshold` become fully
    // transparent. Between `softStart` and `whiteThreshold` alpha fades from
    // 255 → 0 linearly. Below `softStart` the pixel is kept fully opaque.
    public static byte[] WhiteToAlphaPng(byte[] imageBytes, byte whiteThreshold = 245, byte softStart = 200)
    {
        using var img = Image.Load<Rgba32>(imageBytes);
        // Pixel-by-pixel via indexer keeps this compatible with ImageSharp
        // 1.x (no ProcessPixelRows) and 2.x. Signature-sized images are small
        // enough (~few hundred kB) that the extra overhead is negligible.
        int span = whiteThreshold - softStart;
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                var p = img[x, y];
                var min = Math.Min(p.R, Math.Min(p.G, p.B));
                if (min >= whiteThreshold)
                {
                    p.A = 0;
                }
                else if (min >= softStart && span > 0)
                {
                    var t = (min - softStart) * 255 / span;
                    var a = 255 - t;
                    p.A = (byte)Math.Clamp(a, 0, 255);
                }
                img[x, y] = p;
            }
        }
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    public static byte[] WhiteToAlphaPngFromFile(string path) => WhiteToAlphaPng(File.ReadAllBytes(path));
}
