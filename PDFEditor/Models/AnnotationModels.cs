using System.Collections.Generic;
using System.Windows.Media;

namespace PDFEditor.Models;

public enum TextAlign
{
    Left,
    Center,
    Right,
    Justify
}

public enum AnnotationKind
{
    Highlight,
    StickyNote,
    Ink,
    Rectangle,
    Ellipse,
    TextStamp,
    Whiteout,
    Redaction,
    Image,
    Callout
}

/// <summary>Coordinates are normalized 0..1 in PDF page space (origin top-left, y-down).</summary>
public class PdfAnnotation
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public int PageIndex { get; set; }
    public AnnotationKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public Color Color { get; set; } = Colors.Yellow;
    /// <summary>Background/fill colour for text-carrying annotations
    /// (Notes, Callouts, Text stamps). Null means no background (transparent
    /// for stamps; falls back to the built-in yellow for notes/callouts to
    /// keep the pre-feature look).</summary>
    public Color? BackgroundColor { get; set; }
    public double StrokeThickness { get; set; } = 2.0;
    public string? Text { get; set; }
    public string? ImagePath { get; set; }
    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Arial";
    /// <summary>OpenType-style font weight. 400 = Regular, 500 = Medium,
    /// 600 = SemiBold, 700 = Bold. Values &gt;= 550 flatten to PDF-level Bold
    /// (PdfSharpCore's XFontStyle only knows Regular vs Bold; the WPF preview
    /// picks up the exact weight via FontWeight.FromOpenTypeWeight).</summary>
    public int FontWeight { get; set; } = 400;

    /// <summary>Compat surface for older read-sites and marker-key round-trip.
    /// True iff <see cref="FontWeight"/> is at least SemiBold (550). Setting it
    /// snaps <see cref="FontWeight"/> to 400 or 700.</summary>
    public bool Bold
    {
        get => FontWeight >= 550;
        set => FontWeight = value ? 700 : 400;
    }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public TextAlign Align { get; set; } = TextAlign.Left;
    public List<(double X, double Y)> InkPoints { get; set; } = new();
    /// <summary>Rectangle / Ellipse only: draw as filled instead of outlined.</summary>
    public bool Filled { get; set; }
    /// <summary>Callout only: anchor point the arrow tip points at, normalised page coords.
    /// The X/Y/Width/Height fields hold the text-box position and size.</summary>
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
}
