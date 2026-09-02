# ArtiMax PDF Editor

A desktop PDF editor for Windows by **ArtiMax** — **free for personal and
non-commercial use** under the PolyForm Noncommercial License 1.0.0. Aims
to cover the everyday features people actually reach for in Adobe Acrobat,
without the subscription. Commercial use requires a separate licence — see
the [LICENSE](LICENSE) file and get in touch.

Built on **.NET 9 / WPF** using permissive open-source libraries: PdfSharpCore
(MIT), PDFium via PDFtoImage (Apache 2.0), PdfPig (Apache 2.0), Tesseract
(Apache 2.0), CommunityToolkit.Mvvm (MIT), DocumentFormat.OpenXml (MIT). Those
components remain under their original licences for downstream users.

## Getting started

```
cd PDFEditor
dotnet build
bin\Debug\net9.0-windows\PDFEditor.exe [optional-pdf-path]
```

Drag a PDF onto the window, or use **File → Open** / **File → Open Recent**.

## Building a release

```
.\publish.ps1                    # → dist\PDFEditor-<git-describe>-win-x64.zip
.\publish.ps1 -Version 1.0.0     # → dist\PDFEditor-1.0.0-win-x64.zip
```

Produces a portable, self-contained ZIP (~80 MB) with `PDFEditor.exe`,
`LICENSE`, `README.md`, and the `Help\` folder. No .NET install needed on the
target machine. Pass `-TessdataPath <folder>` to bundle Tesseract training
data for OCR support.

## What it does

### Viewing
- Continuous vertical page scrolling, fit-to-width, editable zoom combo (25–400%)
- Thumbnail sidebar with page number, Prev/Next navigation, jump-to-page input
- Selecting a thumbnail scrolls the main viewer; scrolling updates the thumbnail
- Bookmarks / outline tree in the right panel
- Search with results panel, transient yellow highlight on the match,
  Prev/Next hit buttons (F3 / Shift+F3), single-click to jump

### Annotate
- Select, Highlight, Sticky Note, Text, Draw (ink), Rectangle, Ellipse,
  Whiteout, Redaction, Erase
- Colour swatch button opens the full Windows colour picker; contrast border
- Text stamps support any installed font, size dropdown (Word-style presets),
  bold / italic / underline, and text alignment (Left/Center/Right/Justify)
- Multi-line text with Enter for newline; auto-growing editor dialog with
  live preview
- Wrap width is bounded by the annotation's resizable box (E, S, SE handles)
- Move any annotation with the Select tool (drag); Delete to remove;
  F2/Enter/double-click to edit; colour swatch recolours the selection

### Save behaviour
- Overlay annotations live on top of the pages until you Save
- On Save they flatten into the PDF — except **sticky notes**, which are
  written as native PDF Text Annotations (visible as icons in any viewer)
- Print offers "Include notes/annotations" so you can print without markup

### Signatures
- Signature Library (Acrobat-style): Draw with the mouse, or Load an image
- Named, persisted across sessions in `%AppData%\PDFEditor\signatures\`
- Placed as movable image annotations; drag/resize before Save

### Select Text / Select Image (region-based)
- Drag over any text → **Copy** to clipboard, or **Replace** which whiteouts
  the region and drops a text stamp pre-filled with the source text,
  font family, size, bold/italic detected via PdfPig glyph metadata
- Drag over any region → **Copy** to clipboard as a raster, or **Save PNG**

### Page ops
- Rotate, Delete, Insert, Extract range, Split, **Combine** (multi-select →
  auto-open result), **Organise Pages** (grid of thumbnails with drag-drop
  reorder and multi-select delete), Crop All Pages

### Convert
- **Convert to PDF** — dispatches by extension:
  Word / Excel / PowerPoint / Outlook MSG via COM (when installed),
  images and text via PdfSharpCore
- **Convert Multiple to PDF** — batch mode with per-file success/failure
- **Edit in Word** — round-trip: export → open in Word → click Import →
  Word saves as PDF → replaces current bytes
- **Export as Word / Excel / HTML / PNG images / JPEG images**

### OCR
- OCR Current Page → text in extract panel
- OCR All Pages → text file
- Make Searchable PDF — rasterises each page and overlays invisible OCR
  text so the result is searchable/copyable
- Requires `tessdata\eng.traineddata` next to the executable

### Security
- Password protect (user + owner + permissions: print / copy / annotations /
  modify)
- Remove protection
- Sanitize metadata (strips Title/Author/Subject/Keywords/Creator/Producer +
  XMP)

### Content overlays
- Watermark (text, size, colour, opacity, angle)
- Headers & Footers (6 slots with `{page}`, `{total}`, `{date}`, `{filename}`
  placeholders)
- Bates numbering (prefix + start + digits + position)
- Insert image on current page

### Fill & Sign
- Reads AcroForm fields, edit and apply

### Compare
- Side-by-side viewer + per-page text diff summary

### UI
- **Dark / Light theme** — full theme system with dynamic brush swap;
  choice persists to `%AppData%\PDFEditor\theme.json`
- **Customise Toolbar** — every button is toggleable per named profile
  (multiple profiles supported); saved to `%AppData%\PDFEditor\toolbar.json`
- **Recent files** menu (persistent, 10 slots)
- **Undo** (Ctrl+Z) — covers both PDF-byte changes (watermarks, Bates, page
  ops, security) and overlay annotation changes (Replace, add, edit)
- **Unsaved-changes prompt** on Open, Close, drag-drop and window close
- **File lock** while a PDF is open — other apps can read but not modify
- **Status bar** is selectable/copyable

## Layout

```
PDFEditor/
  Models/             Annotation data
  Services/           PDF I/O, render, page ops, extract, OCR, security,
                      overlays, export, convert, forms, bookmarks, theme,
                      signatures, toolbar profiles, recents, compare
  ViewModels/         MainViewModel, PageViewModel, ToolMode
  Controls/           Annotation overlay, dialogs, converters, helpers
  App.xaml            Theme brushes + control templates
  MainWindow.xaml     Menu, toolbar, thumbnails, viewer, right panel,
                      status bar
```

## Honest scope

**Not** aiming to replicate Acrobat's click-into-existing-text reflow editing
— that requires either iText 7 (AGPL, which would make this app AGPL too)
or commercial licensing. The Select Text → Replace workflow covers the
common "edit that word/sentence" case without the licence trap.

Some Acrobat features intentionally out of scope on the free stack:
XFA forms, PDF JavaScript, PDF/A compliance validation, Preflight, digital
signatures with certificate chains.

## Licence

**PolyForm Noncommercial License 1.0.0.** Free to use, copy, modify,
and share for any noncommercial purpose (personal, hobby, educational,
research, charitable, government, non-profit). Commercial use requires
a separate written licence — contact the copyright holder.

Source code is public on GitHub:
<https://github.com/MikeyBorin/PDFEditor>. Anyone can view, clone, or
download it — the non-commercial licence still applies to what you do
with it. Tagged releases give you a fixed source snapshot matching each
shipped binary.

Third-party libraries retain their own licences (all permissive open
source) and downstream users receive those components under their
original terms. See the [LICENSE](LICENSE) file for full terms.

## Support the project

ArtiMax PDF Editor is offered free for noncommercial use. If it saves
you time or money, please consider a donation. Donations are optional
and do not entitle the donor to any commercial rights under the
licence.

- **GitHub Sponsors:** <https://github.com/sponsors/MikeyBorin>
- **Ko-fi:** <https://ko-fi.com/mikeyborin>

*(both links go live once the accounts are set up on the respective platforms —
GitHub Sponsors needs identity verification + Stripe; Ko-fi needs a free
account with a PayPal or Stripe connection.)*

## Disclaimer

**USE AT YOUR OWN RISK.** This software is provided "as is", without warranty
of any kind, express or implied. There is no guarantee it is fit for any
particular purpose. The author accepts no liability for data loss, corrupted
files, missed information, incorrect edits, or any other damages arising
from use of this software. Always keep backups of important documents before
editing.
