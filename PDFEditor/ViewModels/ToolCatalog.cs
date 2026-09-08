namespace PDFEditor.ViewModels;

// Shared catalog of every annotation tool + a few "action" entries (Image,
// Signature) — consumed by both the floating Tool Palette window and the
// "Tools" mode of the left sidebar. Keeps the glyph/label/tooltip in one
// place so the two views stay in sync.
//
// One of Mode / CommandName is non-null. Mode entries route through
// SetToolCommand(name). CommandName entries execute a specific VM command
// by name (looked up in the click handler).
public record ToolCatalogEntry(
    ToolMode? Mode,
    string? CommandName,
    string Glyph,
    string Label,
    string Tooltip)
{
    // Precomputed single string for the palette/sidebar ToolTip binding.
    // A single Binding is more reliable than a MultiBinding inside a
    // FrameworkElementFactory item template.
    public string DisplayTooltip => string.IsNullOrEmpty(Tooltip) ? Label : $"{Label} — {Tooltip}";
}

public static class ToolCatalog
{
    // MDL2 glyphs (0xE000–0xF8FF) MUST use \uXXXX escapes — bare private-use
    // characters don't round-trip through editors/terminals reliably and end
    // up empty. Any MDL2 glyph also needs FontFamily="Segoe MDL2 Assets" set
    // on its TextBlock; default fonts render MDL2 code points as blanks.
    // Non-MDL2 symbols (✓ ✗ • ▭ ▬ ○ ● 💬 ✎) render in Segoe UI Symbol.
    public static readonly ToolCatalogEntry[] All = new[]
    {
        // --- Tool modes (set the active drawing/select tool) ---
        new ToolCatalogEntry(ToolMode.Select,          null, "", "Select",       "Select / move existing annotations"),
        new ToolCatalogEntry(ToolMode.Highlight,       null, "", "Highlight",    "Highlight text"),
        new ToolCatalogEntry(ToolMode.StickyNote,      null, "", "Note",         "Sticky note"),
        new ToolCatalogEntry(ToolMode.TextStamp,       null, "", "Text",         "Text stamp"),
        new ToolCatalogEntry(ToolMode.Tick,            null, "✓", "Tick",         "Insert ✓ (checkbox tick)"),
        new ToolCatalogEntry(ToolMode.Cross,           null, "✗", "Cross",        "Insert ✗ (checkbox cross)"),
        new ToolCatalogEntry(ToolMode.Bullet,          null, "•", "Bullet",       "Insert • (bullet)"),
        new ToolCatalogEntry(ToolMode.Callout,         null, "", "Callout",      "Speech-bubble note with a leader arrow"),
        new ToolCatalogEntry(ToolMode.Ink,             null, "", "Draw",         "Freehand draw"),
        new ToolCatalogEntry(ToolMode.Rectangle,       null, "▭", "Rectangle",    "Rectangle (outlined)"),
        new ToolCatalogEntry(ToolMode.RectangleFilled, null, "▬", "Rect Fill",    "Rectangle (filled)"),
        new ToolCatalogEntry(ToolMode.Ellipse,         null, "○", "Oval",         "Oval (outlined)"),
        new ToolCatalogEntry(ToolMode.EllipseFilled,   null, "●", "Oval Fill",    "Oval (filled)"),
        new ToolCatalogEntry(ToolMode.Whiteout,        null, "", "Whiteout",     "Whiteout — cover with a white box"),
        new ToolCatalogEntry(ToolMode.Erase,           null, "", "Erase",        "Erase (last under cursor)"),
        new ToolCatalogEntry(ToolMode.SelectText,      null, "", "Select Text",  "Select text region — drag to copy or replace"),
        new ToolCatalogEntry(ToolMode.SelectImage,     null, "", "Select Image", "Select region as image — drag to copy or save PNG"),
        new ToolCatalogEntry(ToolMode.InsertCheckbox,  null, "", "Checkbox",     "Drag a rectangle to place an interactive PDF form checkbox (viewer can click to toggle)"),

        // --- Actions (open dialog / place content) ---
        new ToolCatalogEntry(null, "InsertImageOnCurrent", "", "Image",     "Insert an image on the current page"),
        new ToolCatalogEntry(null, "OpenSignatureLibrary", "", "Signature", "Place a signature"),
    };
}
