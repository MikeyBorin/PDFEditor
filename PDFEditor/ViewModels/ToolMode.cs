namespace PDFEditor.ViewModels;

public enum ToolMode
{
    Select,
    Highlight,
    StickyNote,
    Ink,
    Rectangle,
    Ellipse,
    TextStamp,
    Whiteout,
    Erase,
    SelectText,
    SelectImage,
    Tick,
    Cross,
    RectangleFilled,
    EllipseFilled,
    Bullet,
    Callout,
    /// <summary>Drag a rectangle to place a new interactive AcroForm
    /// checkbox field on the page. Written into doc.AcroForm at save time.</summary>
    InsertCheckbox
}
