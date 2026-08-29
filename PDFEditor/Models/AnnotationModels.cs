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
    Image
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
    public double StrokeThickness { get; set; } = 2.0;
    public string? Text { get; set; }
    public string? ImagePath { get; set; }
    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Arial";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public TextAlign Align { get; set; } = TextAlign.Left;
    public List<(double X, double Y)> InkPoints { get; set; } = new();
}
