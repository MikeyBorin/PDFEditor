# PDF Editor — Project Guide for Claude Code

This file is loaded automatically by Claude Code when working in this repo. It captures the "why" a fresh session can't derive from reading the source alone.

## What this is

ArtiMax PDF Editor — a Windows desktop PDF editor by Michael Borin (ArtiMax). Personal / non-commercial licence (PolyForm Noncommercial 1.0.0), source at [github.com/MikeyBorin/PDFEditor](https://github.com/MikeyBorin/PDFEditor). WPF + .NET 9 (`net9.0-windows`). Built to fill Michael's own PDF workflow — favour **simplicity and usability** over feature count.

## Build & publish flow

- **Debug build** (fast, dev-only): `dotnet build PDFEditor/PDFEditor.csproj -c Debug` → `PDFEditor/bin/Debug/net9.0-windows/ArtiMaxPDFEditor.exe`. Not what users run.
- **Release publish** (real distribution): `.\publish.ps1 [-Version 1.0.X]` from repo root:
  1. `dotnet publish` self-contained loose-file build to `dist/ArtiMaxPDFEditor-<ver>-win-x64/`
  2. Bundles `LICENSE`, `README.md`, and (if present) `tessdata/` for OCR
  3. Zips → `dist/ArtiMaxPDFEditor-<ver>-win-x64.zip`
  4. Runs Inno Setup on `installer/ArtiMaxPDFEditor.iss` → `dist/ArtiMaxPDFEditor-Setup-<ver>.exe`
  5. Deletes the staging folder (ZIP + Setup carry the payload)
- **Installer** is per-user (`PrivilegesRequired=lowest`) — installs to `%LOCALAPPDATA%\Programs\ArtiMax\PDF Editor\`. No admin needed. Overwrites in place so the taskbar shortcut keeps working. Silent install: `Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
- **The word "packup"** = run `publish.ps1`, not `git archive`. Distinct from a git snapshot.

## Version bumps — three places must move together

Any version increment must touch **all three** in the same commit:
1. `PDFEditor/PDFEditor.csproj` — `<Version>`, `<FileVersion>`, `<InformationalVersion>`.
2. `PDFEditor/MainWindow.xaml.cs` — the `"ArtiMax PDF Editor  vX.Y.Z\n\n"` literal inside `About_Click`.
3. `PDFEditor/Help/help.html` — the `<div class="sub">Help & reference · vX.Y.Z</div>` header.

Skipping the About string drift-corrupts what users see and makes it hard to tell which build is running. There is a memory `feedback_about_version.md` covering this.

## Distribution model

Two channels:
- **ArtiMax website** — primary. Michael manually uploads `dist/ArtiMaxPDFEditor-Setup-<ver>.exe` after each packup. Users download from there.
- **GitHub Releases** — secondary but wired to the in-app updater (`UpdateCheckService.cs`). Tag `vX.Y.Z`, push tag, `gh release create vX.Y.Z dist/*.exe dist/*.zip --title ... --notes ...`. **Never move a tag once cut** — moving detaches its GitHub Release into Draft. If a release ships broken, cut a fresh patch (`vX.Y.Z+1`). Memory `feedback_release_tags.md`.

## Architecture in one screen

- `Models/AnnotationModels.cs` — `PdfAnnotation` + `AnnotationKind` (Highlight, StickyNote, Ink, Rectangle, Ellipse, TextStamp, Whiteout, Redaction, Image, Callout, CheckboxField). All coords normalised 0..1 in page space, **top-left origin, y-down**. PDF-native coords (bottom-left origin) are only used at flatten time.
- `ViewModels/MainViewModel.cs` — the god-VM (CommunityToolkit.Mvvm `[ObservableProperty]` / `[RelayCommand]`). Holds `Pages`, `SearchResults`, tool state, and every top-level command.
- `ViewModels/PageViewModel.cs` — per-page: rendered bitmap, page-point size, per-page `Annotations` overlay list. Not persisted to PDF until Save.
- `ViewModels/ToolCatalog.cs` + `ToolMode.cs` — the shared catalog the toolbar and Tool Palette both read from. New tools go here.
- `Services/PdfDocumentService.cs` — owns `Bytes`, undo stack (20 frames of full-byte snapshots), and Save. Everything is snapshot-based; there's no in-place PDF mutation across the app.
- `Services/AnnotationService.cs` — **the flatten path**. Iterates overlays, draws vector/text into the PDF via `XGraphics`. For `StickyNote` and `Callout` it writes native `/Text` and `/FreeText` annotations plus `/ArtiMax*` marker keys so `ExtractAndStripStickyNotes` can rematerialize them as editable overlays after reload. For `CheckboxField`, it creates a real `PdfCheckBoxField` widget in `doc.AcroForm.Fields` with `/AP /N /Off` and `/Yes` appearance streams. `TextStamp` and shape kinds are burned into page content (irreversible on save).
- `Services/FormService.cs` — AcroForm operations: `GetFields`, `SetFieldValues` (WPF-checkbox-aware, handles legacy `/FT`-less widgets), `CheckAllBoxes`/`UncheckAllBoxes` (raw dict, discovers each field's true "on" state via `/AP /N`), `SetAllCheckboxSizes`, `TryToggleCheckboxAt`.
- `Services/PdfRenderService.cs` — PDFium (via PDFtoImage) renders page bitmaps with `WithAnnotations: true, WithFormFill: true` so form widgets are visible.
- `Controls/AnnotationLayer.cs` — the overlay canvas. Handles mouse (draft draw, resize handles, move, select), builds WPF visuals for each `PdfAnnotation`, delegates edit dialogs. `MouseRightButtonUp` → paste-text/image context menu.
- `Controls/ToolPaletteWindow.cs` — floating tool palette. Owner-relative (follows main window across monitors), never truly Topmost above other apps. Reads `MainVM.VisiblePaletteEntries` (filtered by `ViewSettings.HiddenPaletteItems`) so **Customise Palette** works. Right-click opens `PaletteCustomiseDialog`.
- `Services/InstanceSwitcherService.cs` — cross-process discovery via `Process.GetProcessesByName` + `MainWindowTitle`. Backs the multi-window `Windows` menu and Ctrl+Tab cycling.
- `Services/ViewSettingsService.cs` — `%APPDATA%\ArtiMaxPDFEditor\view.json`. Persists ZoomMode, colour, OCR language, LeftPanelMode, ToolPaletteOpen, ToolsIconsOnly, HiddenPaletteItems.

## Round-trip contract for editable annotations

Sticky notes and callouts are the two overlay kinds that survive Save → Reopen as editable overlays (not baked page content). The mechanism:
- Flatten writes them as native `/Text` and `/FreeText` PDF annotations so other viewers see them.
- Custom `/ArtiMax*` keys carry the state we can't fit into PDF-standard fields: font family, size, weight, italic, underline, alignment, background colour, and every text effect flag (Strikethrough, DoubleStrikethrough, Superscript, Subscript, SmallCaps, AllCaps). Callouts also stash `/ArtiMaxOriginalText` when caps effects are applied, so toggling caps off on reopen restores the original case.
- Load path `AnnotationService.ExtractAndStripStickyNotes` strips those `/Text` / `/FreeText+ArtiMaxCallout` annotations back out of the byte stream (so they don't render into page bitmaps), reads the marker keys, and returns a list of `PdfAnnotation` overlays to attach to the newly-built `PageViewModel`s. Called after every Load, Save, and byte-level operation via `ApplyBytesPreservingOverlaysAsync`.
- `TextStamp` and shape kinds are **not** round-trippable — they're flattened into page content and become pixels. Adding round-trip for those means introducing another `/ArtiMaxTextStamp` annotation kind, which hasn't been done.

## Undo model

Two stacks, unified through one `Ctrl+Z`:
- `_annotationUndos` (Stack<Action>) — overlay-level. Every `AddAnnotationWithUndo` pushes a reversing lambda. LIFO.
- `_doc._undo` (byte snapshots) — 20-frame history of full PDF byte states. Rotate / Delete Page / Insert / Check All / Match Sizes / Whiteout-widget / Fill Form / Password / etc. all push here via `_doc.ReplaceBytes(bytes, label)`.

`Undo` prefers `_annotationUndos` first. `ApplyBytesPreservingOverlaysAsync` **clears** `_annotationUndos` after pushing the byte-level frame, so Ctrl+Z after a byte-level op reaches the byte frame instead of popping older overlay actions that closed over now-defunct `PageViewModel` refs. History dialog reads directly from `_doc._undo` — it's the same list.

## Working rules learned along the way

- **Always publish after a build** on Michael's dev machine — his taskbar shortcut targets the installed exe under `%LOCALAPPDATA%`, not `bin/`. Un-published builds are invisible to him. `feedback_always_publish_after_build.md`.
- **Verify UI icons after any change** — MDL2 private-use-area chars (E000–F8FF) round-trip lossily through some file writes. Use `` C# escapes, not raw pasted chars. Every icon in `ToolCatalog.cs` needs the right `FontFamily` — MDL2 chars need `Segoe MDL2 Assets`; regular Unicode (✓ ✗ • ▭ ○ etc.) needs `Segoe UI Symbol`. `GlyphFontConverter` picks per-glyph. `feedback_verify_ui_icons.md`.
- **Placeholder features** — greyed-out disabled menu items with `ToolTipService.ShowOnDisabled="True"` communicate "we could build this — email support / open an issue". Currently used for `Tools → Insert Text Field…`. Not a hidden feature, an explicit shortlist.
- **Screenshots** — Michael's screenshot drop folder is `C:\Users\micha\OneDrive\Pictures\Screenshots 1\`, not the ArtiMax `Temp\` from the global CLAUDE.md.

## What's not in the repo

Local per-user state that would otherwise leak private data:
- Signatures library (`%APPDATA%\ArtiMaxPDFEditor\signatures\`)
- View settings (`view.json`)
- Toolbar profile (`toolbar.json`)
- Theme file (`theme.json`)
- `dist/` build artifacts, `dist.zip`, `bin/`, `obj/`, `crash.log` — all gitignored
- Any Claude session memory files (live under `~/.claude/`)
- Test PDFs and personal test files

The public repo carries **only** source + `LICENSE` + `README.md` + `Help/help.html` + this file. No secrets, no tokens, no absolute paths. Copyright / support email / GitHub username appear in the standard attribution places (LICENSE, csproj, About dialog, Help) — that's OSS licence metadata, not a leak.

## Contact

Support / commercial licensing: `support@artimax.com.au`. Issues: [github.com/MikeyBorin/PDFEditor/issues](https://github.com/MikeyBorin/PDFEditor/issues).
