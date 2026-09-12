namespace PDFEditor.ViewModels;

public enum ToolMode
{
    Select,
    /// <summary>Hand tool: grab the page and drag to scroll the view.
    /// Navigation only -- creates no annotation and pushes no undo.</summary>
    Pan,
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
