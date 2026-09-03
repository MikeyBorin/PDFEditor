using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PDFEditor.Models;
using PDFEditor.Services;

namespace PDFEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PdfDocumentService _doc = new();
    private readonly PdfRenderService _render = new();
    private readonly PageOperationsService _pageOps = new();
    private readonly AnnotationService _annotate = new();
    private readonly ExtractService _extract = new();
    private readonly OcrService _ocr = new();
    private readonly FormService _forms = new();
    private readonly SecurityService _security = new();
    private readonly ConvertToPdfService _convert = new();
    private readonly ContentOverlayService _overlay = new();
    private readonly WatermarkService _watermark = new();
    private readonly ExportService _export;
    private readonly BookmarkService _bookmarks = new();
    private readonly SignatureLibraryService _signatures = new();
    private readonly FileAssociationService _fileAssoc = new();
    private readonly TessdataDownloadService _tessDownload = new();
    private readonly TranslateService _translate = new();

    // Persisted only for the current session (dialog reopens where you left off).
    [ObservableProperty] private string translateSourceLang = "en";
    [ObservableProperty] private string translateTargetLang = "fr";
    public ToolbarSettingsService ToolbarSettings { get; } = new();
    public ThemeService Theme { get; } = new();
    public ViewSettingsService ViewSettings { get; } = new();

    // Tesseract language codes + display names. Keep the list small and
    // Western-focused for now — user can hand-drop other .traineddata files
    // into the tessdata folder if they need something exotic.
    public ObservableCollection<OcrLanguageItem> OcrLanguages { get; } = new()
    {
        new("eng",     "English"),
        new("fra",     "French"),
        new("deu",     "German"),
        new("spa",     "Spanish"),
        new("ita",     "Italian"),
        new("por",     "Portuguese"),
        new("nld",     "Dutch"),
        new("pol",     "Polish"),
        new("rus",     "Russian"),
        new("chi_sim", "Chinese (Simplified)"),
        new("chi_tra", "Chinese (Traditional)"),
        new("jpn",     "Japanese"),
        new("kor",     "Korean"),
        new("ara",     "Arabic"),
    };

    /// <summary>Refresh the IsInstalled / IsCurrent flags on every language row
    /// so the "OCR Languages" submenu shows the correct ticks/labels.</summary>
    private void RefreshOcrLanguages()
    {
        var current = ViewSettings.Settings.OcrLanguage;
        foreach (var lang in OcrLanguages)
        {
            lang.IsInstalled = _tessDownload.IsInstalled(lang.Code);
            lang.IsCurrent   = string.Equals(lang.Code, current, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Called from the OCR Languages submenu. If the language is
    /// installed, just mark it current. If not, prompt to download; on
    /// success, mark it current.</summary>
    [RelayCommand]
    private async Task SelectOcrLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (!_tessDownload.IsInstalled(code))
        {
            var display = OcrLanguages.FirstOrDefault(l => l.Code == code)?.DisplayName ?? code;
            var choice = MessageBox.Show(
                $"{display} ({code}.traineddata) isn't installed yet.\n\n" +
                "Download it now?  (~22 MB from github.com/tesseract-ocr/tessdata)",
                "OCR language",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes) return;
            await DownloadOcrData(code);
            if (!_tessDownload.IsInstalled(code)) return;   // download failed / cancelled
        }
        ViewSettings.Settings.OcrLanguage = code;
        ViewSettings.Save();
        RefreshOcrLanguages();
        StatusText = $"OCR language set to {OcrLanguages.FirstOrDefault(l => l.Code == code)?.DisplayName ?? code}.";
    }

    [RelayCommand]
    private void SetTheme(string themeName)
    {
        if (Enum.TryParse<AppTheme>(themeName, out var t))
        {
            Theme.Apply(t);
            StatusText = $"Theme: {t}.";
        }
    }
    public ObservableCollection<string> ProfileNames { get; } = new();

    [ObservableProperty] private string activeProfileName = "Default";
    [ObservableProperty] private string pageNumberInput = "";
    public int TotalPages => Pages.Count;

    partial void OnCurrentPageChanged(PageViewModel? value)
    {
        if (value != null) PageNumberInput = (value.PageIndex + 1).ToString();
        OnPropertyChanged(nameof(TotalPages));
        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPrev))]
    private void PrevPage()
    {
        if (CurrentPage is null) return;
        var idx = CurrentPage.PageIndex - 1;
        if (idx >= 0 && idx < Pages.Count) CurrentPage = Pages[idx];
    }
    private bool CanPrev() => CurrentPage != null && CurrentPage.PageIndex > 0;

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void NextPage()
    {
        if (CurrentPage is null) return;
        var idx = CurrentPage.PageIndex + 1;
        if (idx >= 0 && idx < Pages.Count) CurrentPage = Pages[idx];
    }
    private bool CanNext() => CurrentPage != null && CurrentPage.PageIndex < Pages.Count - 1;

    [RelayCommand(CanExecute = nameof(HasDocumentAndPages))]
    private void GoToPage()
    {
        // Guard against callers that bypass CanExecute (e.g. PageNumberBox_KeyDown
        // calling Execute(null) directly). With Pages empty, Math.Clamp(x, 0, -1)
        // throws ArgumentException because max < min.
        if (Pages.Count == 0) return;
        if (int.TryParse(PageNumberInput?.Trim(), out var n))
        {
            var idx = Math.Clamp(n - 1, 0, Pages.Count - 1);
            if (idx >= 0 && idx < Pages.Count)
            {
                CurrentPage = Pages[idx];
                RequestScrollIntoView(idx, 0.5, 0.05);
            }
        }
    }
    private bool HasDocumentAndPages() => Pages.Count > 0;

    partial void OnActiveProfileNameChanged(string value) => ToolbarSettings.SetActive(value);

    [RelayCommand]
    private void RegisterFileAssociation()
    {
        try
        {
            _fileAssoc.Register();
            var msg = "PDF Editor is now registered as a PDF handler for your user account.\n\n" +
                      "To make it the default:\n" +
                      "  1. Right-click any .pdf → Open with → Choose another app\n" +
                      "     Pick PDF Editor and tick \"Always use this app\", OR\n" +
                      "  2. Settings → Apps → Default apps → search '.pdf' → pick PDF Editor.\n\n" +
                      "(Windows blocks apps from taking the default automatically — this is an " +
                      "anti-hijacking measure, not something we can override.)\n\n" +
                      "Registered exe: " + (Environment.ProcessPath ?? "(unknown)");
            MessageBox.Show(msg, "ArtiMax PDF Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText = "Registered as PDF handler.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Register failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void UnregisterFileAssociation()
    {
        try
        {
            _fileAssoc.Unregister();
            MessageBox.Show(
                "PDF Editor removed from the Windows file-association list.\n\n" +
                "If it was your default handler, Windows will fall back to the previous app " +
                "(or ask you to pick one) next time you open a .pdf.",
                "ArtiMax PDF Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText = "Unregistered as PDF handler.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Unregister failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CustomiseToolbar()
    {
        Controls.CustomiseToolbarDialog.Show(ToolbarSettings);
        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        ProfileNames.Clear();
        foreach (var p in ToolbarSettings.Settings.Profiles) ProfileNames.Add(p.Name);
        if (ActiveProfileName != ToolbarSettings.Settings.ActiveProfileName)
            ActiveProfileName = ToolbarSettings.Settings.ActiveProfileName;
    }

    public ObservableCollection<BookmarkNode> Bookmarks { get; } = new();
    private readonly WordExportService _word;
    private readonly RecentFilesService _recents = new();
    public ObservableCollection<string> RecentFiles { get; } = new();

    public ObservableCollection<PageViewModel> Pages { get; } = new();
    public ObservableCollection<SearchHit> SearchResults { get; } = new();

    [ObservableProperty] private PageViewModel? currentPage;
    [ObservableProperty] private string statusText = "Ready. Open a PDF to begin.";
    [ObservableProperty] private string title = "ArtiMax PDF Editor";
    [ObservableProperty] private bool isBusy;
    // Custom-mode multiplier: 1.0 == 100% Actual Size. Only affects the page
    // display when ZoomMode == Custom (percentage picked from the toolbar).
    [ObservableProperty] private double zoom = 1.0;
    // Which fit-strategy is currently active. Persisted per-user.
    [ObservableProperty] private ZoomMode zoomMode = ZoomMode.FitWidth;
    [ObservableProperty] private int renderDpi = 150;

    partial void OnZoomModeChanged(ZoomMode value)
    {
        ViewSettings.Settings.ZoomMode = value;
        ViewSettings.Save();
    }

    partial void OnZoomChanged(double value)
    {
        if (ZoomMode == ZoomMode.Custom)
        {
            ViewSettings.Settings.CustomZoom = value;
            ViewSettings.Save();
        }
    }
    [ObservableProperty] private ToolMode currentTool = ToolMode.Select;
    // Last-used tool per group, driving the split-button main-click behavior.
    [ObservableProperty] private ToolMode currentTextTool  = ToolMode.TextStamp;
    [ObservableProperty] private ToolMode currentShapeTool = ToolMode.Rectangle;

    partial void OnCurrentToolChanged(ToolMode value)
    {
        // Remember which tool was last active in each group so the split button
        // reflects the user's most recent choice.
        if (value is ToolMode.TextStamp or ToolMode.Tick or ToolMode.Cross or ToolMode.Bullet or ToolMode.Callout)
        {
            CurrentTextTool = value;
        }
        else if (value is ToolMode.Ink or ToolMode.Rectangle or ToolMode.RectangleFilled
                       or ToolMode.Ellipse or ToolMode.EllipseFilled)
        {
            CurrentShapeTool = value;
        }
    }
    [ObservableProperty] private Color currentColor = Colors.Black;

    partial void OnCurrentColorChanged(Color value)
    {
        ViewSettings.Settings.CurrentColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        ViewSettings.Save();
    }
    [ObservableProperty] private double currentThickness = 3.0;
    // Last-used text-stamp font state, persisted across text-edit actions so the dialog
    // reopens with your previous choice instead of resetting to Arial 14.
    [ObservableProperty] private string currentFontFamily = "Arial";
    [ObservableProperty] private double currentFontSize = 14;
    [ObservableProperty] private int currentFontWeight = 400;
    [ObservableProperty] private bool currentItalic;
    [ObservableProperty] private bool currentUnderline;
    [ObservableProperty] private TextAlign currentAlign = TextAlign.Left;

    /// <summary>Snapshot the font settings the user just committed so the next text-edit action reuses them.</summary>
    public void RememberFontChoice(Controls.TextStampDialog.Result r)
    {
        if (r is null) return;
        if (!string.IsNullOrWhiteSpace(r.FontFamily)) CurrentFontFamily = r.FontFamily;
        if (r.FontSize > 0 && !double.IsNaN(r.FontSize) && !double.IsInfinity(r.FontSize)) CurrentFontSize = r.FontSize;
        if (r.FontWeight >= 100 && r.FontWeight <= 900) CurrentFontWeight = r.FontWeight;
        CurrentItalic = r.Italic;
        CurrentUnderline = r.Underline;
        CurrentAlign = r.Align;
    }
    [ObservableProperty] private string searchQuery = "";
    [ObservableProperty] private bool searchWholeWord;

    partial void OnSearchWholeWordChanged(bool value)
    {
        // Re-run the current search so the toggle takes effect immediately when there
        // are results on screen. If the box is empty or no search has run yet, do nothing.
        if (!string.IsNullOrWhiteSpace(SearchQuery) && SearchResults.Count > 0)
        {
            Search();
        }
    }

    [ObservableProperty] private string extractedText = "";
    [ObservableProperty] private bool hasDocument;
    [ObservableProperty] private Models.PdfAnnotation? selectedAnnotation;

    /// <summary>Last mouse position over a page in normalized 0..1 coords. Null if no page hovered yet.</summary>
    public (int PageIndex, double NormX, double NormY)? LastHover { get; set; }

    /// <summary>Raised when we want the viewport to scroll a page/annotation region into view.</summary>
    public event System.Action<int, double, double>? ScrollIntoViewRequested;
    public void RequestScrollIntoView(int pageIndex, double normX, double normY)
        => ScrollIntoViewRequested?.Invoke(pageIndex, normX, normY);

    /// <summary>A transient search-hit highlight — cleared on next page click. Null means none.</summary>
    private (int PageIndex, double X, double Y, double W, double H)? _transientHighlight;
    public (int PageIndex, double X, double Y, double W, double H)? TransientHighlight
    {
        get => _transientHighlight;
        set { _transientHighlight = value; TransientHighlightChanged?.Invoke(); }
    }
    public event System.Action? TransientHighlightChanged;

    /// <summary>True when there are pending changes (saved-in-mem via ReplaceBytes,
    /// or overlay annotations that differ from the last save).</summary>
    public bool HasUnsavedChanges => _doc.IsDirty || ComputeOverlaySignature() != _overlaySignatureAtLastSave;

    // Signature snapshotted after each Load/Save. HasUnsavedChanges compares the current
    // overlay state against it — so a note that was re-materialised from the just-saved
    // file doesn't count as an unsaved change, but any subsequent move / edit / add /
    // delete DOES because the signature diverges.
    private string _overlaySignatureAtLastSave = "";

    private string ComputeOverlaySignature()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in Pages)
        {
            foreach (var a in p.Annotations)
            {
                sb.Append(p.PageIndex).Append('|')
                  .Append((int)a.Kind).Append('|')
                  .Append(a.X.ToString("R")).Append(',').Append(a.Y.ToString("R")).Append(',')
                  .Append(a.Width.ToString("R")).Append(',').Append(a.Height.ToString("R")).Append('|')
                  .Append(a.AnchorX.ToString("R")).Append(',').Append(a.AnchorY.ToString("R")).Append('|')
                  .Append(a.Text ?? "").Append('|')
                  .Append(a.Color.ToString()).Append('|')
                  .Append(a.FontSize.ToString("R")).Append(':').Append(a.FontFamily ?? "").Append(':')
                  .Append(a.Bold).Append(a.Italic).Append(a.Underline).Append((int)a.Align).Append('|')
                  .Append(a.Filled).Append('|')
                  .Append(a.InkPoints.Count).Append(';');
            }
        }
        return sb.ToString();
    }

    private void SnapshotOverlaySignature() => _overlaySignatureAtLastSave = ComputeOverlaySignature();

    /// <summary>Prompt Save / Discard / Cancel if there are unsaved changes.
    /// Returns true if the caller may proceed; false if the user cancelled.</summary>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!HasUnsavedChanges) return true;
        var choice = MessageBox.Show(
            $"Save changes to {(string.IsNullOrEmpty(_doc.FilePath) ? "the current document" : Path.GetFileName(_doc.FilePath))} before continuing?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes)
        {
            await Save();
            return !HasUnsavedChanges;
        }
        return true; // No → discard
    }

    public MainViewModel()
    {
        _word = new WordExportService(_extract);
        _export = new ExportService(_extract);
        Controls.SigPathConverter.Library = _signatures;
        RefreshProfiles();
        ToolbarSettings.Changed += RefreshProfiles;
        _doc.DocumentChanged += OnDocumentChanged;
        Pages.CollectionChanged += (_, _) => {
            OnPropertyChanged(nameof(TotalPages));
            PrevPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
            GoToPageCommand.NotifyCanExecuteChanged();
        };
        _recents.Changed += RefreshRecents;
        RefreshRecents();
        // Restore persisted view preference. Set backing fields directly so the
        // OnZoomModeChanged / OnZoomChanged / OnCurrentColorChanged partials
        // don't fire a redundant Save.
        zoomMode = ViewSettings.Settings.ZoomMode;
        zoom = ViewSettings.Settings.CustomZoom;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(ViewSettings.Settings.CurrentColorHex);
            currentColor = c;
        }
        catch { }
        RefreshOcrLanguages();
    }

    private void RefreshRecents()
    {
        var d = Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.Invoke(RefreshRecents); return; }
        RecentFiles.Clear();
        foreach (var f in _recents.Files) RecentFiles.Add(f);
    }

    private void OnDocumentChanged()
    {
        var d = Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.Invoke(OnDocumentChanged); return; }

        HasDocument = _doc.Bytes != null;
        Title = _doc.FilePath is null
            ? "ArtiMax PDF Editor"
            : $"ArtiMax PDF Editor - {Path.GetFileName(_doc.FilePath)}{(_doc.IsDirty ? " *" : "")}";
        StatusText = HasDocument
            ? $"{_doc.PageCount} page{(_doc.PageCount == 1 ? "" : "s")}."
            : "Ready.";
        UndoCommand.NotifyCanExecuteChanged();
    }

    // --- Commands ---

    [RelayCommand]
    private async Task ConvertToPdf()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose file to convert to PDF",
            Filter = "All supported (Word, Excel, PowerPoint, images, text, email)|" +
                     "*.doc;*.docx;*.rtf;*.odt;*.xls;*.xlsx;*.xlsm;*.ods;*.ppt;*.pptx;*.odp;" +
                     "*.msg;*.oft;*.html;*.htm;*.mht;*.mhtml;" +
                     "*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;" +
                     "*.txt;*.log;*.csv;*.xml;*.json;*.md|" +
                     "All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        var save = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(dlg.FileName) + ".pdf",
            InitialDirectory = Path.GetDirectoryName(dlg.FileName)
        };
        if (save.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            StatusText = $"Converting {Path.GetFileName(dlg.FileName)}...";
            var mode = await _convert.ConvertAsync(dlg.FileName, save.FileName);
            StatusText = $"Converted via {mode}. Opening result...";
            await LoadFileAsync(save.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Convert failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Convert failed.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ConvertMultipleToPdf()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Choose files to convert to PDF",
            Filter = "All supported|*.doc;*.docx;*.rtf;*.odt;*.xls;*.xlsx;*.xlsm;*.ods;*.ppt;*.pptx;*.odp;" +
                     "*.msg;*.oft;*.html;*.htm;*.mht;*.mhtml;" +
                     "*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;" +
                     "*.txt;*.log;*.csv;*.xml;*.json;*.md|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;

        var save = new SaveFileDialog
        {
            Title = "Choose output folder (pick any filename inside it)",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = "output.pdf"
        };
        if (save.ShowDialog() != true) return;
        var outDir = Path.GetDirectoryName(save.FileName)!;

        try
        {
            IsBusy = true;
            var results = await _convert.ConvertBatchAsync(dlg.FileNames, outDir);
            int ok = 0, failed = 0;
            var errors = new System.Text.StringBuilder();
            foreach (var (src, _, mode, err) in results)
            {
                if (err == null) ok++;
                else { failed++; errors.AppendLine($"{Path.GetFileName(src)}: {err}"); }
            }
            var msg = $"Converted {ok} of {results.Count} to {outDir}.";
            if (failed > 0) msg += $"\n\nFailures:\n{errors}";
            MessageBox.Show(msg, "Batch convert", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            StatusText = $"Batch convert: {ok} OK, {failed} failed.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Batch convert failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Open()
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        await LoadFileAsync(path: dlg.FileName, checkDirty: false);
    }

    public Task LoadFileAsync(string path) => LoadFileAsync(path, checkDirty: true);

    public async Task LoadFileAsync(string path, bool checkDirty)
    {
        if (checkDirty && !await ConfirmDiscardChangesAsync()) return;
        _annotationUndos.Clear();
        try
        {
            IsBusy = true;
            StatusText = $"Loading {Path.GetFileName(path)}...";
            await Task.Run(() => _doc.Load(path));
            // Strip native /Text sticky notes from the bytes BEFORE rendering so
            // no small default icons end up in the page bitmaps; attach them to
            // the fresh pages after rebuild.
            var extractedNotes = ExtractStickyNotesForRematerialization();
            await RebuildPagesAsync();
            ApplyRematerializedStickyNotes(extractedNotes);
            SnapshotOverlaySignature();
            StatusText = $"Loaded {Path.GetFileName(path)} ({_doc.PageCount} pages).";
            _recents.Add(path);
        }
        catch (Exception ex)
        {
            _recents.Remove(path);
            MessageBox.Show(ex.Message, "Failed to open PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Open failed.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task OpenRecent(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path))
        {
            _recents.Remove(path);
            MessageBox.Show($"File no longer exists:\n{path}", "ArtiMax PDF Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await LoadFileAsync(path);
    }

    [RelayCommand]
    private void ClearRecent() => _recents.Clear();

    private void RefreshBookmarks()
    {
        Bookmarks.Clear();
        if (_doc.Bytes is null) return;
        try
        {
            foreach (var b in _bookmarks.Read(_doc.Bytes)) Bookmarks.Add(b);
        }
        catch { }
    }

    [RelayCommand]
    private void GoToBookmark(BookmarkNode node)
    {
        if (node is null || node.PageIndex < 0) return;
        var p = Pages.ElementAtOrDefault(node.PageIndex);
        if (p != null) CurrentPage = p;
    }

    public async Task ReorderPagesAsync(IList<int> newOrder)
    {
        if (_doc.Bytes is null) return;
        try
        {
            IsBusy = true;
            var bytes = await Task.Run(() => _pageOps.Reorder(_doc.Bytes!, newOrder));
            await ApplyBytesPreservingOverlaysAsync(bytes, "Reorder pages");
            StatusText = "Pages reordered.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Reorder failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    /// <summary>Apply new PDF bytes with the given history label, then rebuild the page views,
    /// while PRESERVING the unsaved overlay annotations (text stamps, ticks, notes, drawings)
    /// that live on the PageViewModel collections. Without this, any byte-level operation
    /// (rotate, watermark, headers, etc.) silently discards everything the user has annotated
    /// but not yet saved. Restore is index-based: perfect for page-count-preserving ops; a
    /// best-effort for ops that change page count/order (delete/insert/reorder — overlays on
    /// removed pages are dropped, overlays on shifted pages may land on the wrong page).</summary>
    private async Task ApplyBytesPreservingOverlaysAsync(byte[] newBytes, string label)
    {
        var overlaysSnapshot = Pages.Select(p => p.Annotations.ToList()).ToList();
        _doc.ReplaceBytes(newBytes, label);
        await RebuildPagesAsync();
        for (int i = 0; i < overlaysSnapshot.Count && i < Pages.Count; i++)
            foreach (var a in overlaysSnapshot[i])
                Pages[i].Annotations.Add(a);
    }

    /// <summary>Two-phase round-trip. Phase 1 (before rebuild): extract every native
    /// /Text sticky note from _doc.Bytes and strip them, so the render is clean.
    /// Returns the list of extracted notes for phase 2 to attach after RebuildPagesAsync
    /// has re-created the PageViewModels.</summary>
    private System.Collections.Generic.List<Models.PdfAnnotation> ExtractStickyNotesForRematerialization()
    {
        var empty = new System.Collections.Generic.List<Models.PdfAnnotation>();
        if (_doc.Bytes is null) return empty;
        try
        {
            var (cleaned, notes) = _annotate.ExtractAndStripStickyNotes(_doc.Bytes);
            if (notes.Count == 0) return empty;
            _doc.ReplaceBytes(cleaned, label: "(extract sticky notes)", markDirty: false, pushUndo: false);
            return notes;
        }
        catch { return empty; }
    }

    /// <summary>Phase 2: attach previously-extracted sticky notes to their pages.
    /// Call after RebuildPagesAsync so the target Page objects exist.</summary>
    private void ApplyRematerializedStickyNotes(System.Collections.Generic.List<Models.PdfAnnotation> notes)
    {
        foreach (var n in notes)
        {
            if (n.PageIndex >= 0 && n.PageIndex < Pages.Count)
                Pages[n.PageIndex].Annotations.Add(n);
        }
    }

    private async Task RebuildPagesAsync()
    {
        Pages.Clear();
        if (_doc.Bytes is null) return;
        var bytes = _doc.Bytes;
        var count = _doc.PageCount;

        for (int i = 0; i < count; i++) Pages.Add(new PageViewModel(i));

        await Task.Run(() =>
        {
            for (int i = 0; i < count; i++)
            {
                var img = _render.RenderPage(bytes, i, RenderDpi);
                var thumb = _render.RenderThumbnail(bytes, i);
                int idx = i;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var pvm = Pages[idx];
                    pvm.PageImage = img;
                    pvm.Thumbnail = thumb;
                    pvm.PixelWidth = img.PixelWidth;
                    pvm.PixelHeight = img.PixelHeight;
                    // Derive point dimensions from the bitmap — this already
                    // reflects PDFium's rotation handling, so we don't need to
                    // separately read PdfSharpCore's Rotate key.
                    pvm.WidthPt  = img.PixelWidth  * 72.0 / RenderDpi;
                    pvm.HeightPt = img.PixelHeight * 72.0 / RenderDpi;
                    if (idx == 0) CurrentPage = pvm;
                });
            }
        });
        RefreshBookmarks();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (_doc.FilePath is null) { await SaveAs(); return; }
        await FlattenAndPersist(_doc.FilePath);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(_doc.FilePath ?? "document") + "-edited.pdf"
        };
        if (dlg.ShowDialog() != true) return;
        await FlattenAndPersist(dlg.FileName);
        // Add the newly-saved file to Recent Files so it's one click away next launch.
        if (!_doc.IsDirty && _doc.FilePath == dlg.FileName) _recents.Add(dlg.FileName);
    }

    private async Task FlattenAndPersist(string path)
    {
        try
        {
            IsBusy = true;
            StatusText = "Saving...";
            var allAnnos = Pages.SelectMany(p => p.Annotations).ToList();
            var bytes = await Task.Run(() =>
            {
                var flattened = allAnnos.Count > 0
                    ? _annotate.Flatten(_doc.Bytes!, allAnnos)
                    : _doc.Bytes!;
                return flattened;
            });
            // Save is a terminal act: the file is written to disk. Don't push it to
            // the undo stack — Ctrl+Z can't unsave, and Save showing up as a "later
            // edit" pollutes flows that walk the stack (e.g., watermark delete).
            _doc.ReplaceBytes(bytes, label: "Save (flatten annotations)", markDirty: false, pushUndo: false);
            // Retry loop: file-in-use errors (typically the target is open in another
            // app) offer Retry / Cancel. Cancel keeps the flattened bytes in memory as
            // unsaved so the user can free the file and try again with plain Ctrl+S.
            while (true)
            {
                try
                {
                    _doc.Save(path);
                    break;
                }
                catch (Exception ioEx) when (ioEx is System.IO.IOException || ioEx is UnauthorizedAccessException)
                {
                    var choice = MessageBox.Show(
                        $"Could not save to:\n{path}\n\n{ioEx.Message}\n\n" +
                        "The file may be open in another application (Acrobat, a browser, Explorer preview, etc.). " +
                        "Close it, then click Yes to try again. No to cancel and keep the changes in memory.",
                        "Save failed — retry?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.Yes)
                    {
                        _doc.IsDirty = true;
                        StatusText = "Save cancelled — changes are still in memory.";
                        return;
                    }
                }
            }
            foreach (var p in Pages) p.Annotations.Clear();
            // Post-save round-trip: strip the just-written /Text notes from the bytes
            // BEFORE rendering (so no icons in the bitmaps), then rebuild, then attach
            // them back as editable overlays.
            var extractedNotes = ExtractStickyNotesForRematerialization();
            await RebuildPagesAsync();
            ApplyRematerializedStickyNotes(extractedNotes);
            SnapshotOverlaySignature();
            StatusText = $"Saved to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Save failed.";
        }
        finally { IsBusy = false; }
    }

    private bool CanSave() => HasDocument;

    [RelayCommand]
    private async Task Close()
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        _doc.Close();
        Pages.Clear();
        CurrentPage = null;
        ExtractedText = "";
        SearchResults.Clear();
        PageNumberInput = "0";
        _overlaySignatureAtLastSave = "";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task RotateLeft() => await RotateCurrent(-90);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task RotateRight() => await RotateCurrent(90);

    private async Task RotateCurrent(int deg)
    {
        if (CurrentPage is null || _doc.Bytes is null) return;
        var idx = CurrentPage.PageIndex;
        var bytes = await Task.Run(() => _pageOps.Rotate(_doc.Bytes!, idx, deg));
        await ApplyBytesPreservingOverlaysAsync(bytes, $"Rotate page {idx + 1} ({deg:+#;-#;0}°)");
        if (idx < Pages.Count) CurrentPage = Pages[idx];
        // Snap back to the top of the rotated page so it stays in view after the page size changes.
        RequestScrollIntoView(idx, 0.5, 0.05);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task DeleteCurrentPage()
    {
        if (CurrentPage is null || _doc.Bytes is null) return;
        if (_doc.PageCount <= 1)
        {
            MessageBox.Show("A PDF must have at least one page.", "ArtiMax PDF Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var idx = CurrentPage.PageIndex;
        var bytes = await Task.Run(() => _pageOps.DeletePages(_doc.Bytes!, new[] { idx }));
        await ApplyBytesPreservingOverlaysAsync(bytes, $"Delete page {idx + 1}");
        CurrentPage = Pages.ElementAtOrDefault(Math.Min(idx, Pages.Count - 1));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task InsertPagesFromFile()
    {
        if (_doc.Bytes is null || CurrentPage is null) return;
        var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf" };
        if (dlg.ShowDialog() != true) return;
        var at = CurrentPage.PageIndex + 1;
        var bytes = await Task.Run(() => _pageOps.InsertPagesFromFile(_doc.Bytes!, dlg.FileName, at));
        await ApplyBytesPreservingOverlaysAsync(bytes, $"Insert pages from {System.IO.Path.GetFileName(dlg.FileName)}");
        CurrentPage = Pages.ElementAtOrDefault(at);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ExtractRange()
    {
        if (_doc.Bytes is null) return;
        var dlg = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "extracted.pdf" };
        if (dlg.ShowDialog() != true) return;
        // For simplicity: extract from current page to end.
        var start = CurrentPage?.PageIndex ?? 0;
        var end = _doc.PageCount - 1;
        var bytes = await Task.Run(() => _pageOps.Extract(_doc.Bytes!, start, end));
        File.WriteAllBytes(dlg.FileName, bytes);
        StatusText = $"Extracted pages {start + 1}-{end + 1} to {Path.GetFileName(dlg.FileName)}.";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SplitEachPage()
    {
        if (_doc.Bytes is null) return;
        var dlg = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "page.pdf" };
        if (dlg.ShowDialog() != true) return;
        var dir = Path.GetDirectoryName(dlg.FileName)!;
        var stem = Path.GetFileNameWithoutExtension(dlg.FileName);
        var parts = await Task.Run(() => _pageOps.Split(_doc.Bytes!, 1));
        for (int i = 0; i < parts.Count; i++)
            File.WriteAllBytes(Path.Combine(dir, $"{stem}-{i + 1:000}.pdf"), parts[i]);
        StatusText = $"Split into {parts.Count} files in {dir}.";
    }

    [RelayCommand]
    private async Task MergeFiles()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Choose PDF files to combine (order = selection order in dialog)"
        };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length < 2) return;
        var save = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "combined.pdf" };
        if (save.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            StatusText = $"Combining {dlg.FileNames.Length} files...";
            var bytes = await Task.Run(() => _pageOps.Merge(dlg.FileNames.Select(File.ReadAllBytes)));
            await File.WriteAllBytesAsync(save.FileName, bytes);
            StatusText = $"Combined {dlg.FileNames.Length} files to {Path.GetFileName(save.FileName)}. Opening...";
            await LoadFileAsync(save.FileName);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Combine failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task OrganizePages()
    {
        if (_doc.Bytes is null) return;
        var res = Controls.OrganizePagesDialog.Show(_doc.Bytes, _render);
        if (res is null) return;
        try
        {
            IsBusy = true;
            StatusText = "Applying page changes...";
            byte[] bytes = _doc.Bytes;
            // Delete pages first (from source indexes)
            if (res.DeletedIndexes.Count > 0)
                bytes = await Task.Run(() => _pageOps.DeletePages(bytes, res.DeletedIndexes));
            // Then reorder the remaining
            if (res.NewOrder != null && res.NewOrder.Count > 0)
                bytes = await Task.Run(() => _pageOps.Reorder(bytes, res.NewOrder));
            await ApplyBytesPreservingOverlaysAsync(bytes, "Organise pages");
            StatusText = "Pages reorganised.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Organise pages failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEditInWord))]
    private async Task EditInWord()
    {
        if (_doc.Bytes is null) return;
        if (!_word.IsWordAvailable)
        {
            MessageBox.Show("Microsoft Word is required for the round-trip editor.", "ArtiMax PDF Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(_doc.FilePath ?? "document");
        var tempDocx = Path.Combine(Path.GetTempPath(), $"{stem}-edit-{Guid.NewGuid():N}.docx");

        try
        {
            IsBusy = true;
            StatusText = "Exporting to Word for editing...";
            await _word.ExportAsync(_doc.Bytes, _doc.FilePath, tempDocx);

            WordExportService.OpenDocxInWord(tempDocx);
            IsBusy = false;

            var import = Controls.EditInWordDialog.Show(tempDocx);
            if (!import)
            {
                StatusText = "Edit in Word cancelled.";
                return;
            }

            IsBusy = true;
            StatusText = "Converting edited document back to PDF...";
            var newPdf = await _word.DocxToPdfBytesAsync(tempDocx);
            await ApplyBytesPreservingOverlaysAsync(newPdf, "Edit in Word (import)");
            StatusText = "Edits imported. Use File → Save to persist.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Edit in Word failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Edit in Word failed.";
        }
        finally
        {
            IsBusy = false;
            if (File.Exists(tempDocx)) try { File.Delete(tempDocx); } catch { }
        }
    }

    private bool CanEditInWord() => HasDocument && _word.IsWordAvailable;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ExportAsWord()
    {
        if (_doc.Bytes is null) return;
        var suggested = Path.GetFileNameWithoutExtension(_doc.FilePath ?? "document") + ".docx";
        var dlg = new SaveFileDialog
        {
            Filter = "Word Document (*.docx)|*.docx",
            FileName = suggested
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            StatusText = _word.IsWordAvailable
                ? "Converting via Microsoft Word (high fidelity)..."
                : "Converting via text extract (basic — install Word for high fidelity)...";
            var mode = await _word.ExportAsync(_doc.Bytes, _doc.FilePath, dlg.FileName);
            StatusText = mode == "Word"
                ? $"Exported to {Path.GetFileName(dlg.FileName)} via Word."
                : $"Exported text-only to {Path.GetFileName(dlg.FileName)}. Install Word for layout-preserving export.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Export failed.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Translate a specific block of text (already extracted, e.g. from the
    /// Select Text region tool). Same dialog + service as the menu-driven Translate.</summary>
    private async Task TranslateSelectionAsync(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return;
        var pick = Controls.TranslateDialog.Show(TranslateSourceLang, TranslateTargetLang, canDoAllPages: false);
        if (pick is null) return;
        TranslateSourceLang = pick.SourceCode;
        TranslateTargetLang = pick.TargetCode;

        try
        {
            IsBusy = true;
            StatusText = $"Translating selection {pick.SourceCode} → {pick.TargetCode}...";
            var progress = new Progress<(int done, int total)>(p =>
            {
                StatusText = $"Translating selection... chunk {p.done}/{p.total}";
            });
            var translated = await _translate.TranslateAsync(sourceText, pick.SourceCode, pick.TargetCode, progress, System.Threading.CancellationToken.None);
            ExtractedText =
                $"--- Translated selection {pick.SourceCode} → {pick.TargetCode} ---{Environment.NewLine}{Environment.NewLine}" +
                $"[Original]{Environment.NewLine}{sourceText}{Environment.NewLine}{Environment.NewLine}" +
                $"[Translated]{Environment.NewLine}{translated}";
            StatusText = $"Translated selection ({sourceText.Length:N0} → {translated.Length:N0} chars).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Translation failed: " + ex.Message + Environment.NewLine + Environment.NewLine +
                "MyMemory has a free-tier daily limit (~10 KB of text per IP).",
                "Translate failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Translation failed.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Translate()
    {
        if (_doc.Bytes is null) return;
        var pick = Controls.TranslateDialog.Show(TranslateSourceLang, TranslateTargetLang, canDoAllPages: Pages.Count > 0);
        if (pick is null) return;
        TranslateSourceLang = pick.SourceCode;
        TranslateTargetLang = pick.TargetCode;

        try
        {
            IsBusy = true;
            string sourceText;
            if (pick.AllPages)
            {
                sourceText = await Task.Run(() => _extract.ExtractAllText(_doc.Bytes!));
            }
            else
            {
                var idx = CurrentPage?.PageIndex ?? 0;
                sourceText = await Task.Run(() => _extract.ExtractPageText(_doc.Bytes!, idx));
            }
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                MessageBox.Show("No extractable text found on the selected page(s). If the PDF is a scan, run OCR first (Tools → Make Searchable PDF).",
                    "Translate", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StatusText = $"Translating {pick.SourceCode} → {pick.TargetCode}...";
            var progress = new Progress<(int done, int total)>(p =>
            {
                StatusText = $"Translating {pick.SourceCode} → {pick.TargetCode}... chunk {p.done}/{p.total}";
            });
            var translated = await _translate.TranslateAsync(sourceText, pick.SourceCode, pick.TargetCode, progress, System.Threading.CancellationToken.None);
            var scope = pick.AllPages ? "all pages" : $"page {(CurrentPage?.PageIndex ?? 0) + 1}";
            ExtractedText = $"--- Translated {pick.SourceCode} → {pick.TargetCode} ({scope}) ---{Environment.NewLine}{Environment.NewLine}{translated}";
            StatusText = $"Translated {sourceText.Length:N0} chars → {translated.Length:N0} chars.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Translation failed: " + ex.Message + Environment.NewLine + Environment.NewLine +
                "MyMemory has a free-tier daily limit (~10 KB of text per IP). If you hit the limit, wait 24 h or try again from a different network.",
                "Translate failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Translation failed.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ExtractText()
    {
        if (_doc.Bytes is null) return;
        var text = await Task.Run(() => _extract.ExtractAllText(_doc.Bytes!));
        ExtractedText = text;
        StatusText = $"Extracted {text.Length:N0} chars.";
    }

    /// <summary>Prompt the user; on Yes, download eng.traineddata. Returns true if OCR
    /// is available afterwards (either already-installed, or freshly-downloaded).</summary>
    private async Task<bool> PromptDownloadOcrDataAsync()
    {
        var lang = ViewSettings.Settings.OcrLanguage;
        if (_ocr.IsLanguageInstalled(lang)) return true;

        var display = OcrLanguages.FirstOrDefault(l => l.Code == lang)?.DisplayName ?? lang;
        var choice = MessageBox.Show(
            $"OCR training data for {display} ({lang}.traineddata) is not installed.\n\n" +
            "Would you like to download it now?\n\n" +
            "Source: github.com/tesseract-ocr/tessdata\n" +
            "Size: ~22 MB\n" +
            "Installs to: " + _tessDownload.InstallDir,
            "OCR training data required",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return false;

        await DownloadOcrData(lang);
        return _ocr.IsLanguageInstalled(lang);
    }

    [RelayCommand]
    private Task DownloadOcrDataDefault() => DownloadOcrData("eng");

    private async Task DownloadOcrData(string languageCode)
    {
        if (_tessDownload.IsInstalled(languageCode))
        {
            MessageBox.Show(
                $"{languageCode}.traineddata is already installed at:\n{_tessDownload.DestinationPath(languageCode)}",
                "OCR training data", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = $"Downloading {languageCode}.traineddata...";
            var progress = new Progress<(long downloaded, long? total)>(p =>
            {
                var mb = p.downloaded / (1024.0 * 1024.0);
                if (p.total.HasValue)
                {
                    var totalMb = p.total.Value / (1024.0 * 1024.0);
                    var pct = (int)(100.0 * p.downloaded / p.total.Value);
                    StatusText = $"Downloading {languageCode}.traineddata... {mb:0.0}/{totalMb:0.0} MB ({pct}%)";
                }
                else
                {
                    StatusText = $"Downloading {languageCode}.traineddata... {mb:0.0} MB";
                }
            });
            await _tessDownload.DownloadAsync(languageCode, progress, System.Threading.CancellationToken.None);
            StatusText = $"OCR training data installed. OCR is now ready.";
            RunOcrOnCurrentCommand.NotifyCanExecuteChanged();
            OcrAllPagesCommand.NotifyCanExecuteChanged();
            MakeSearchablePdfCommand.NotifyCanExecuteChanged();
            RefreshOcrLanguages();
            MessageBox.Show(
                $"Installed {languageCode}.traineddata at:\n{_tessDownload.DestinationPath(languageCode)}\n\nOCR is now available under Tools → OCR.",
                "Download complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Download failed: " + ex.Message + "\n\n" +
                "You can install manually — download eng.traineddata from\n" +
                "https://github.com/tesseract-ocr/tessdata and place it in:\n" +
                _tessDownload.InstallDir,
                "Download failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "OCR training data download failed.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task RunOcrOnCurrent()
    {
        if (_doc.Bytes is null || CurrentPage is null) return;
        if (!_ocr.IsAvailable && !await PromptDownloadOcrDataAsync()) return;
        try
        {
            IsBusy = true;
            var idx = CurrentPage.PageIndex;
            var lang = ViewSettings.Settings.OcrLanguage;
            var text = await Task.Run(() => _ocr.OcrPage(_doc.Bytes!, idx, language: lang));
            ExtractedText = $"--- OCR Page {idx + 1} ({lang}) ---\n{text}";
            StatusText = $"OCR done on page {idx + 1} ({lang}).";
        }
        catch (Exception ex) { ShowOcrFailure(ex, "OCR failed"); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Search()
    {
        SearchResults.Clear();
        CurrentHitIndex = -1;
        if (_doc.Bytes is null || string.IsNullOrWhiteSpace(SearchQuery)) return;
        var hits = _extract.Search(_doc.Bytes, SearchQuery, caseSensitive: false, wholeWord: SearchWholeWord);
        foreach (var h in hits) SearchResults.Add(h);
        StatusText = SearchWholeWord
            ? $"{hits.Count} whole-word match(es) for \"{SearchQuery}\"."
            : $"{hits.Count} match(es) for \"{SearchQuery}\".";
        NextSearchHitCommand.NotifyCanExecuteChanged();
        PrevSearchHitCommand.NotifyCanExecuteChanged();
        // Jump to first hit automatically.
        if (SearchResults.Count > 0) SelectAndShowHit(0);
    }

    [ObservableProperty] private int currentHitIndex = -1;

    partial void OnCurrentHitIndexChanged(int value)
    {
        NextSearchHitCommand.NotifyCanExecuteChanged();
        PrevSearchHitCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void GoToHit(SearchHit hit)
    {
        var page = Pages.ElementAtOrDefault(hit.PageIndex);
        if (page != null) CurrentPage = page;
        CurrentHitIndex = SearchResults.IndexOf(hit);
    }

    [RelayCommand(CanExecute = nameof(CanNextHit))]
    private void NextSearchHit()
    {
        if (SearchResults.Count == 0) return;
        var next = (CurrentHitIndex + 1) % SearchResults.Count;
        SelectAndShowHit(next);
    }
    private bool CanNextHit() => SearchResults.Count > 0;

    [RelayCommand(CanExecute = nameof(CanPrevHit))]
    private void PrevSearchHit()
    {
        if (SearchResults.Count == 0) return;
        var prev = CurrentHitIndex <= 0 ? SearchResults.Count - 1 : CurrentHitIndex - 1;
        SelectAndShowHit(prev);
    }
    private bool CanPrevHit() => SearchResults.Count > 0;

    /// <summary>Raised so the view can select the corresponding item in the results list.</summary>
    public event System.Action<int>? HitSelectionRequested;

    private void SelectAndShowHit(int index)
    {
        if (index < 0 || index >= SearchResults.Count) return;
        CurrentHitIndex = index;
        var hit = SearchResults[index];
        var page = Pages.ElementAtOrDefault(hit.PageIndex);
        if (page != null) CurrentPage = page;
        if (hit.NormW > 0 && hit.NormH > 0)
        {
            TransientHighlight = (hit.PageIndex, hit.NormX, hit.NormY, hit.NormW, hit.NormH);
            RequestScrollIntoView(hit.PageIndex, hit.NormX + hit.NormW / 2, hit.NormY + hit.NormH / 2);
        }
        else
        {
            TransientHighlight = null;
            RequestScrollIntoView(hit.PageIndex, 0.5, 0.1);
        }
        StatusText = $"Hit {index + 1} of {SearchResults.Count}.";
        HitSelectionRequested?.Invoke(index);
    }

    [RelayCommand]
    private void ZoomIn()
    {
        // Bumping into Custom from a fit-mode: jump to 125% relative to actual
        // size. Predictable and matches "one press bigger than 100%".
        if (ZoomMode != ZoomMode.Custom) { ZoomMode = ZoomMode.Custom; Zoom = 1.25; return; }
        Zoom = Math.Min(Zoom * 1.25, 6.0);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomMode != ZoomMode.Custom) { ZoomMode = ZoomMode.Custom; Zoom = 0.8; return; }
        Zoom = Math.Max(Zoom / 1.25, 0.25);
    }

    [RelayCommand]
    private void ZoomReset() { ZoomMode = ZoomMode.ActualSize; Zoom = 1.0; }

    /// <summary>Set the fit-mode (or a specific % via ZoomMode.Custom).
    /// When switching to Custom, pass the desired percentage as a double
    /// (e.g. 1.5 for 150%) via <see cref="SetZoomPercent"/> instead.</summary>
    [RelayCommand]
    private void SetZoomMode(ZoomMode mode) => ZoomMode = mode;

    /// <summary>Switch to Custom mode at the given percentage (1.0 = 100%).</summary>
    [RelayCommand]
    private void SetZoomPercent(double percent)
    {
        if (percent <= 0) return;
        ZoomMode = ZoomMode.Custom;
        Zoom = Math.Clamp(percent, 0.05, 16.0);
    }

    public void HandleRegionSelection(int pageIndex, double nx, double ny, double nw, double nh, ToolMode tool)
    {
        if (_doc.Bytes is null) return;

        if (tool == ToolMode.SelectText)
        {
            PDFEditor.Services.ExtractService.RegionText region;
            try { region = _extract.ExtractTextInRegion(_doc.Bytes, pageIndex, nx, ny, nw, nh); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Text extraction failed"); return; }
            if (string.IsNullOrWhiteSpace(region.Text))
            {
                MessageBox.Show("No text found in the selected region.", "Select text",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            StatusText = "Detected: " + region.DebugInfo;
            var choice = Controls.RegionActionDialog.ShowTextActions(region.Text);
            switch (choice)
            {
                case Controls.RegionAction.Copy:
                    try { Controls.ClipboardHelper.SetText(region.Text); StatusText = "Text copied to clipboard."; }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "Clipboard failed"); }
                    break;
                case Controls.RegionAction.Replace:
                    ReplaceRegionWithText(pageIndex, nx, ny, nw, nh, region);
                    break;
                case Controls.RegionAction.Translate:
                    _ = TranslateSelectionAsync(region.Text);
                    break;
            }
            // One-shot: revert to Select after Copy or Cancel. Replace already does this
            // inside ReplaceRegionWithText — the assignment here is idempotent.
            CurrentTool = ToolMode.Select;
            return;
        }

        if (tool == ToolMode.SelectImage)
        {
            System.Windows.Media.Imaging.BitmapSource bmp;
            try { bmp = _render.RenderRegion(_doc.Bytes, pageIndex, nx, ny, nw, nh, dpi: 200); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Region capture failed"); return; }
            var choice = Controls.RegionActionDialog.ShowImageActions();
            switch (choice)
            {
                case Controls.RegionAction.Copy:
                    try { Controls.ClipboardHelper.SetImage(bmp); StatusText = "Region copied to clipboard as image."; }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "Clipboard failed"); }
                    break;
                case Controls.RegionAction.Save:
                    var sd = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "region.png" };
                    if (sd.ShowDialog() == true)
                    {
                        try
                        {
                            using var fs = File.Create(sd.FileName);
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                            enc.Save(fs);
                            StatusText = $"Saved region to {Path.GetFileName(sd.FileName)}.";
                        }
                        catch (Exception ex) { MessageBox.Show(ex.Message, "Save failed"); }
                    }
                    break;
            }
            // One-shot: revert to Select after Copy, Save, or Cancel.
            CurrentTool = ToolMode.Select;
        }
    }

    private void ReplaceRegionWithText(int pageIndex, double nx, double ny, double nw, double nh, PDFEditor.Services.ExtractService.RegionText region)
    {
        var page = Pages.ElementAtOrDefault(pageIndex);
        if (page is null) return;

        // Replacement flow: pre-fill the dialog purely from the source glyphs. The
        // toolbar's CurrentFontFamily / CurrentFontSize / CurrentBold / CurrentItalic /
        // CurrentUnderline / CurrentAlign / CurrentColor exist for CREATING new text and
        // must NOT leak into a replacement (that would silently override the source's
        // formatting). Use plain neutral defaults for fields we can't detect (colour is
        // black; underline/align default to their natural values).
        var detectedFont = string.IsNullOrWhiteSpace(region.FontFamily) ? "Arial" : region.FontFamily;
        var detectedSize = region.FontPointSize > 1 && region.FontPointSize < 400 ? region.FontPointSize : 12.0;
        var r = Controls.TextStampDialog.Show(
            defaultText: region.Text,
            defaultFont: detectedFont,
            defaultSize: detectedSize,
            defaultFontWeight: region.FontWeight,
            defaultItalic: region.Italic,
            defaultUnderline: false,
            defaultColorHex: "#000000",
            defaultAlign: PDFEditor.Models.TextAlign.Left,
            defaultBackgroundHex: null);
        if (r is null) return;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(r.ColorHex);
            var safeFontSize = r.FontSize > 0 && !double.IsNaN(r.FontSize) && !double.IsInfinity(r.FontSize) ? r.FontSize : 12;
            // Whiteout the source region (a little padding so we cover descenders).
            var padY = nh * 0.15;
            var whiteout = new Models.PdfAnnotation
            {
                PageIndex = pageIndex, Kind = Models.AnnotationKind.Whiteout,
                X = nx, Y = System.Math.Max(0, ny - padY / 2),
                Width = nw, Height = nh + padY,
                Color = Colors.White
            };
            page.Annotations.Add(whiteout);
            // Text stamp with the (possibly edited) source text.
            Color? bg = null;
            if (!string.IsNullOrWhiteSpace(r.BackgroundHex))
            { try { bg = (Color)ColorConverter.ConvertFromString(r.BackgroundHex); } catch { } }
            var stamp = new Models.PdfAnnotation
            {
                PageIndex = pageIndex, Kind = Models.AnnotationKind.TextStamp,
                X = nx, Y = ny,
                Width = System.Math.Max(nw, 0.1),
                Height = System.Math.Max(nh, 0.02),
                Color = c, Text = r.Text,
                FontFamily = r.FontFamily, FontSize = safeFontSize,
                FontWeight = r.FontWeight, Italic = r.Italic, Underline = r.Underline, Align = r.Align,
                BackgroundColor = bg
            };
            page.Annotations.Add(stamp);
            SelectedAnnotation = stamp;
            CurrentTool = ToolMode.Select;
            // Intentionally do NOT RememberFontChoice(r) — the picked font came from
            // the source text being replaced, not a user preference. Overwriting the
            // toolbar defaults from a replace flow would silently change what the
            // Text / Sticky Note / Callout tools do next time.
            // Register undo: remove both annotations we just added.
            PushAnnotationUndo(() =>
            {
                page.Annotations.Remove(stamp);
                page.Annotations.Remove(whiteout);
                if (ReferenceEquals(SelectedAnnotation, stamp) || ReferenceEquals(SelectedAnnotation, whiteout))
                    SelectedAnnotation = null;
                StatusText = "Undo: text replace reverted.";
            });
            StatusText = $"Region replaced ({safeFontSize:0}pt). Drag with Select to fine-tune, then Save.";
        }
        catch { }
    }

    [RelayCommand]
    private void SetTool(string tool)
    {
        if (Enum.TryParse<ToolMode>(tool, out var t))
        {
            CurrentTool = t;
            StatusText = t == ToolMode.Select
                ? "Select tool — click a page to select; use other tools to annotate."
                : $"{t} tool — click and drag on the page.";
        }
    }

    /// <summary>Undo stack for overlay-annotation changes (add/remove/edit). Runs newest first.</summary>
    private readonly System.Collections.Generic.Stack<System.Action> _annotationUndos = new();

    public void PushAnnotationUndo(System.Action undo)
    {
        _annotationUndos.Push(undo);
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task Undo()
    {
        // Annotation-level undos are LIFO and don't touch the PDF bytes.
        if (_annotationUndos.Count > 0)
        {
            try { _annotationUndos.Pop()(); }
            catch (Exception ex) { StatusText = "Undo failed: " + ex.Message; }
            UndoCommand.NotifyCanExecuteChanged();
            StatusText = "Undo (annotation).";
            return;
        }
        // Snapshot overlays so Undo doesn't silently discard unsaved annotations.
        var overlaysSnapshot = Pages.Select(p => p.Annotations.ToList()).ToList();
        if (!_doc.Undo()) return;
        await RebuildPagesAsync();
        for (int i = 0; i < overlaysSnapshot.Count && i < Pages.Count; i++)
            foreach (var a in overlaysSnapshot[i])
                Pages[i].Annotations.Add(a);
        StatusText = "Undo.";
        UndoCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo() => HasDocument && (_doc.CanUndo || _annotationUndos.Count > 0);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ShowHistory()
    {
        var idx = Controls.HistoryDialog.Show(_doc.History);
        if (idx < 0) return;
        try
        {
            IsBusy = true;
            var overlaysSnapshot = Pages.Select(p => p.Annotations.ToList()).ToList();
            if (_doc.RevertToBefore(idx))
            {
                await RebuildPagesAsync();
                for (int i = 0; i < overlaysSnapshot.Count && i < Pages.Count; i++)
                    foreach (var a in overlaysSnapshot[i])
                        Pages[i].Annotations.Add(a);
                StatusText = $"Reverted {idx + 1} edit(s).";
                UndoCommand.NotifyCanExecuteChanged();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "History revert failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void PickColour()
    {
        using var dlg = new System.Windows.Forms.ColorDialog { AllowFullOpen = true, FullOpen = true };
        dlg.Color = System.Drawing.Color.FromArgb(CurrentColor.A, CurrentColor.R, CurrentColor.G, CurrentColor.B);
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var c = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
        CurrentColor = c;
        if (SelectedAnnotation != null && CurrentPage != null)
        {
            SelectedAnnotation.Color = c;
            CurrentPage.RaiseAnnotationChanged();
            StatusText = "Colour changed on selected annotation.";
        }
        else
        {
            StatusText = $"Colour set to #{c.R:X2}{c.G:X2}{c.B:X2}.";
        }
    }

    [RelayCommand]
    private void SetColor(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            CurrentColor = c;
            if (SelectedAnnotation != null && CurrentPage != null)
            {
                SelectedAnnotation.Color = c;
                CurrentPage.RaiseAnnotationChanged();
                StatusText = $"Colour changed to {hex} on selected annotation.";
            }
            else
            {
                StatusText = $"Colour set to {hex} for new annotations.";
            }
        }
        catch { }
    }

    [RelayCommand]
    private void EditSelectedAnnotation()
    {
        var a = SelectedAnnotation;
        if (a is null) return;
        if (a.Kind == Models.AnnotationKind.TextStamp)
        {
            var hex = "#" + a.Color.R.ToString("X2") + a.Color.G.ToString("X2") + a.Color.B.ToString("X2");
            var bgDefault = a.BackgroundColor is Color bgc ? $"#{bgc.R:X2}{bgc.G:X2}{bgc.B:X2}" : null;
            var r = Controls.TextStampDialog.Show(
                defaultText: a.Text ?? "",
                defaultFont: string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily,
                defaultSize: a.FontSize > 0 ? a.FontSize : 14,
                defaultFontWeight: a.FontWeight > 0 ? a.FontWeight : 400, defaultItalic: a.Italic, defaultUnderline: a.Underline,
                defaultColorHex: hex,
                defaultAlign: a.Align,
                defaultBackgroundHex: bgDefault);
            if (r != null)
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(r.ColorHex);
                    a.Text = r.Text; a.FontFamily = r.FontFamily; a.FontSize = r.FontSize;
                    a.FontWeight = r.FontWeight; a.Italic = r.Italic; a.Underline = r.Underline;
                    a.Align = r.Align; a.Color = c;
                    Color? bg = null;
                    if (!string.IsNullOrWhiteSpace(r.BackgroundHex))
                    { try { bg = (Color)ColorConverter.ConvertFromString(r.BackgroundHex); } catch { } }
                    a.BackgroundColor = bg;
                    RememberFontChoice(r);
                    CurrentPage?.RaiseAnnotationChanged();
                    StatusText = "Text updated.";
                }
                catch { }
            }
        }
        else if (a.Kind == Models.AnnotationKind.StickyNote)
        {
            var text = Controls.PromptDialog.Ask("Edit Note", "Note text:", a.Text ?? "");
            if (text != null) { a.Text = text; CurrentPage?.RaiseAnnotationChanged(); StatusText = "Note updated."; }
        }
    }

    [RelayCommand]
    private void DeleteSelectedAnnotation()
    {
        if (SelectedAnnotation is null || CurrentPage is null) return;
        CurrentPage.Annotations.Remove(SelectedAnnotation);
        SelectedAnnotation = null;
        StatusText = "Annotation deleted.";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ProtectWithPassword()
    {
        if (_doc.Bytes is null) return;
        var s = Controls.PasswordDialog.Show();
        if (s is null) return;
        try
        {
            IsBusy = true;
            var bytes = await Task.Run(() => _security.Protect(_doc.Bytes!, s.UserPassword, s.OwnerPassword,
                s.PermitPrint, s.PermitCopy, s.PermitAnnotations, s.PermitModify));
            await ApplyBytesPreservingOverlaysAsync(bytes, "Password protect");
            StatusText = "Security applied. Save to persist.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Protect failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task RemoveProtection()
    {
        if (_doc.Bytes is null) return;
        try
        {
            IsBusy = true;
            var bytes = await Task.Run(() => _security.RemoveProtection(_doc.Bytes!));
            await ApplyBytesPreservingOverlaysAsync(bytes, "Remove protection");
            StatusText = "Security removed. Save to persist.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not remove protection. The document may require the owner password — try saving a copy through File → Save As first, or open with the owner password.\n\n" + ex.Message,
                "Remove protection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task CropAllPages()
    {
        if (_doc.Bytes is null) return;
        var r = Controls.CropDialog.Show();
        if (r is null) return;
        try
        {
            IsBusy = true;
            var bytes = await Task.Run(() => _pageOps.CropAllPages(_doc.Bytes!, r.LeftPt, r.RightPt, r.TopPt, r.BottomPt));
            await ApplyBytesPreservingOverlaysAsync(bytes, "Crop all pages");
            StatusText = "Pages cropped.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Crop failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task RedactSearchResults()
    {
        if (_doc.Bytes is null || SearchResults.Count == 0)
        {
            MessageBox.Show("Run a Find first, then this command paints black boxes over each hit.", "ArtiMax PDF Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirm = MessageBox.Show(
            "This paints black bars over every current search hit and flattens them into the PDF.\n\n" +
            "NOTE: the underlying text bytes remain in the content stream (visually redacted, not byte-redacted). " +
            "For true byte-level redaction you'd need a commercial library.\n\nProceed?",
            "Redact search results", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        var annos = SearchResults
            .Where(h => h.NormW > 0 && h.NormH > 0)
            .Select(h => new PDFEditor.Models.PdfAnnotation
            {
                PageIndex = h.PageIndex,
                Kind = PDFEditor.Models.AnnotationKind.Redaction,
                X = h.NormX, Y = h.NormY, Width = h.NormW, Height = h.NormH,
                Color = System.Windows.Media.Colors.Black
            }).ToList();

        if (annos.Count == 0)
        {
            MessageBox.Show("No search hits with location data — cannot redact.", "ArtiMax PDF Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsBusy = true;
            var bytes = await Task.Run(() => _annotate.Flatten(_doc.Bytes!, annos));
            await ApplyBytesPreservingOverlaysAsync(bytes, $"Redact {annos.Count} search hit(s)");
            SearchResults.Clear();
            StatusText = $"Redacted {annos.Count} hit(s).";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Redaction failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void CompareWithFile()
    {
        if (_doc.Bytes is null) return;
        var dlg = new OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var otherBytes = File.ReadAllBytes(dlg.FileName);
            var labelA = Path.GetFileName(_doc.FilePath ?? "current");
            var labelB = Path.GetFileName(dlg.FileName);
            Controls.CompareWindow.Show(_doc.Bytes, labelA, otherBytes, labelB);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Compare failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task FillForm()
    {
        if (_doc.Bytes is null) return;
        try
        {
            var fields = _forms.GetFields(_doc.Bytes);
            var values = Controls.FillFormDialog.Show(fields);
            if (values is null || values.Count == 0) return;
            IsBusy = true;
            var bytes = await Task.Run(() => _forms.SetFieldValues(_doc.Bytes!, values));
            await ApplyBytesPreservingOverlaysAsync(bytes, $"Fill {values.Count} form field(s)");
            StatusText = $"Filled {values.Count} field(s). Save to persist.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fill form failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void OpenSignatureLibrary()
    {
        if (_doc.Bytes is null || CurrentPage is null) return;
        var picked = Controls.SignatureLibraryDialog.ShowAndPick(_signatures);
        if (picked is null) return;
        var fullPath = _signatures.GetFullPath(picked);
        PlaceImageAsAnnotation(fullPath, $"Signature '{picked.Name}' placed. Drag with Select tool; Save to flatten.");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void LoadSignatureImage()
    {
        if (_doc.Bytes is null || CurrentPage is null) return;
        var pick = new OpenFileDialog { Filter = "Signature image (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*" };
        if (pick.ShowDialog() != true) return;
        PlaceImageAsAnnotation(pick.FileName, "Signature image placed. Drag with Select tool; Save to flatten.");
    }

    private void PlaceImageAsAnnotation(string imagePath, string statusMessage)
    {
        // Prefer the page the user is actually looking at / hovering; fall back to CurrentPage.
        var hoverPageIdx = LastHover?.PageIndex ?? CurrentPage?.PageIndex ?? -1;
        var page = hoverPageIdx >= 0 && hoverPageIdx < Pages.Count ? Pages[hoverPageIdx] : CurrentPage;
        if (page is null) return;

        double aspect = 3.0;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(imagePath);
            bmp.EndInit();
            if (bmp.PixelHeight > 0) aspect = (double)bmp.PixelWidth / bmp.PixelHeight;
        }
        catch { }

        double normW = 0.30;
        double normH = normW * ((double)page.PixelWidth / page.PixelHeight) / aspect;
        if (normH <= 0.01 || normH > 0.5) normH = 0.10;

        // Centre on hover point if we have one, else viewport-visible default (upper-middle).
        double centerX = LastHover?.NormX ?? 0.5;
        double centerY = LastHover?.NormY ?? 0.3;
        double normX = System.Math.Clamp(centerX - normW / 2, 0, 1 - normW);
        double normY = System.Math.Clamp(centerY - normH / 2, 0, 1 - normH);

        page.Annotations.Add(new Models.PdfAnnotation
        {
            PageIndex = page.PageIndex,
            Kind = Models.AnnotationKind.Image,
            X = normX, Y = normY, Width = normW, Height = normH,
            ImagePath = imagePath
        });
        SelectedAnnotation = page.Annotations[^1];
        CurrentPage = page;
        CurrentTool = ToolMode.Select;
        StatusText = statusMessage;
        RequestScrollIntoView(page.PageIndex, normX + normW / 2, normY + normH / 2);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task DrawSignature()
    {
        if (_doc.Bytes is null || CurrentPage is null) return;
        var sigPath = Controls.SignatureCaptureDialog.Show();
        if (string.IsNullOrEmpty(sigPath)) return;
        var place = Controls.InsertImageDialogHelpers.PlaceOnly(sigPath, defaultY: 0.85, defaultW: 0.25, defaultH: 0.08);
        if (place is null) { try { File.Delete(sigPath); } catch { } return; }
        try
        {
            IsBusy = true;
            var idx = CurrentPage.PageIndex;
            var bytes = await Task.Run(() => _overlay.InsertImage(_doc.Bytes!, idx, place.ImagePath, place.XNorm, place.YNorm, place.WidthNorm, place.HeightNorm));
            await ApplyBytesPreservingOverlaysAsync(bytes, $"Insert image on page {idx + 1}");
            if (idx < Pages.Count) CurrentPage = Pages[idx];
            StatusText = "Signature placed. Save to persist.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Signature failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            IsBusy = false;
            try { File.Delete(sigPath); } catch { }
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task OcrAllPages()
    {
        if (_doc.Bytes is null) return;
        if (!_ocr.IsAvailable && !await PromptDownloadOcrDataAsync()) return;
        var dlg = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = Path.GetFileNameWithoutExtension(_doc.FilePath ?? "document") + "-ocr.txt" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            var lang = ViewSettings.Settings.OcrLanguage;
            var text = await Task.Run(() => _ocr.OcrAllPages(_doc.Bytes!, language: lang));
            await File.WriteAllTextAsync(dlg.FileName, text);
            ExtractedText = text;
            StatusText = $"OCR complete for {_doc.PageCount} pages ({lang}) → {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex) { ShowOcrFailure(ex, "OCR failed"); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task MakeSearchablePdf()
    {
        if (_doc.Bytes is null) return;
        if (!_ocr.IsAvailable && !await PromptDownloadOcrDataAsync()) return;

        // Guard: refuse to OCR a PDF that already has embedded text. Running
        // Tesseract on a vector PDF rasterises every page — you lose vector
        // fidelity and replace the accurate embedded text with less-accurate
        // OCR output.
        //
        // Heuristic: count how many pages carry "real" text (≥100 non-whitespace
        // chars). If MOST pages have real text, warn — the doc is likely a
        // vector PDF. If most pages are text-poor, treat as a scan and proceed
        // silently. A scanned contract with a couple of embedded signature or
        // form-field lines shouldn't trip the warning.
        try
        {
            var bytes = _doc.Bytes!;
            var pageCount = _doc.PageCount;
            const int PerPageThreshold = 100;
            int textRich = await Task.Run(() =>
            {
                int rich = 0;
                for (int i = 0; i < pageCount; i++)
                {
                    var t = _extract.ExtractPageText(bytes, i) ?? "";
                    int nonWs = 0;
                    foreach (var c in t) if (!char.IsWhiteSpace(c)) nonWs++;
                    if (nonWs >= PerPageThreshold) rich++;
                }
                return rich;
            });
            // Majority of pages are text-bearing → warn.
            if (pageCount > 0 && textRich * 2 >= pageCount)
            {
                var proceed = MessageBox.Show(
                    $"This PDF already has embedded text on {textRich} of {pageCount} pages. " +
                    "OCR isn't needed and would replace the existing text/vector content " +
                    "with a rasterised image and less-accurate OCR output.\n\n" +
                    "Continue anyway?",
                    "Make Searchable PDF",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);
                if (proceed != MessageBoxResult.OK) return;
            }
        }
        catch { /* if extract fails, treat as no-text and OCR ahead */ }

        try
        {
            IsBusy = true;
            var lang = ViewSettings.Settings.OcrLanguage;
            var bytes = await Task.Run(() => _ocr.BuildSearchablePdf(_doc.Bytes!, language: lang));
            // Route through the standard in-place pipeline: undo-able, marks
            // dirty, next Ctrl+S overwrites the on-disk file.
            await ApplyBytesPreservingOverlaysAsync(bytes, $"OCR — Make Searchable ({lang})");
            StatusText = "Made searchable. Save (Ctrl+S) to overwrite the file.";
        }
        catch (Exception ex) { ShowOcrFailure(ex, "Searchable PDF failed"); }
        finally { IsBusy = false; }
    }

    /// <summary>Format an OCR failure so the actual root cause (not the outer
    /// TargetInvocationException wrapper) is shown, plus a hint for the common
    /// culprits when Tesseract's native DLLs fail to load — missing VC++
    /// runtime, missing x64 folder, antivirus blocking DLLs.</summary>
    private static void ShowOcrFailure(Exception ex, string caption)
    {
        var root = ex.GetBaseException();
        var body = $"{root.GetType().Name}: {root.Message}";

        // If the root looks like a native-DLL / type-load failure, most likely
        // Tesseract couldn't find leptonica*.dll / tesseract*.dll or a runtime
        // dep. Add pointers so the user knows what to check.
        var s = (root.Message ?? "") + " " + root.GetType().Name;
        bool nativeishFailure =
            root is System.DllNotFoundException ||
            root is System.BadImageFormatException ||
            root is System.TypeInitializationException ||
            s.Contains("leptonica", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("tesseract", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("target of an invocation", StringComparison.OrdinalIgnoreCase);
        if (nativeishFailure)
        {
            body += "\n\nOCR failed to load its native components. Common causes:\n" +
                    "  • Missing Microsoft Visual C++ 2015-2022 Redistributable (x64).\n" +
                    "    Download: https://aka.ms/vs/17/release/vc_redist.x64.exe\n" +
                    "  • The 'x64' folder next to ArtiMaxPDFEditor.exe is missing or\n" +
                    "    was quarantined by antivirus. Reinstall or restore the DLLs.\n" +
                    "  • The " + Path.Combine("tessdata", "*.traineddata") + " file is corrupt.\n" +
                    "    Redownload via Tools → OCR Languages.";
        }
        MessageBox.Show(body, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ExportAsImages(string format)
    {
        if (_doc.Bytes is null) return;
        var isPng = format?.Equals("png", StringComparison.OrdinalIgnoreCase) == true;
        var dlg = new SaveFileDialog
        {
            Filter = isPng ? "PNG image (*.png)|*.png" : "JPEG image (*.jpg)|*.jpg",
            FileName = (Path.GetFileNameWithoutExtension(_doc.FilePath ?? "document") + "-page")
        };
        if (dlg.ShowDialog() != true) return;
        var dir = Path.GetDirectoryName(dlg.FileName)!;
        var stem = Path.GetFileNameWithoutExtension(dlg.FileName);
        try
        {
            IsBusy = true;
            var files = await Task.Run(() => isPng
                ? _export.ExportPagesAsPng(_doc.Bytes!, dir, stem)
                : _export.ExportPagesAsJpeg(_doc.Bytes!, dir, stem));
            StatusText = $"Exported {files.Count} {(isPng ? "PNG" : "JPEG")} files to {dir}.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ExportAsHtml()
    {
        if (_doc.Bytes is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "HTML (*.html)|*.html",
            FileName = Path.GetFileNameWithoutExtension(_doc.FilePath ?? "document") + ".html"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            var stem = Path.GetFileNameWithoutExtension(_doc.FilePath ?? "document");
            await Task.Run(() => _export.ExportAsHtml(_doc.Bytes!, dlg.FileName, stem));
            StatusText = $"Exported HTML to {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task ExportAsExcel()
    {
        if (_doc.Bytes is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = Path.GetFileNameWithoutExtension(_doc.FilePath ?? "document") + ".xlsx"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            await Task.Run(() => _export.ExportAsXlsx(_doc.Bytes!, dlg.FileName));
            StatusText = $"Exported to {Path.GetFileName(dlg.FileName)} (text-only, one sheet per page).";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Watermark()
    {
        if (_doc.Bytes is null) return;
        var existing = _watermark.List(_doc.Bytes);
        // A watermark is "cleanly removable" whenever its add entry is still
        // somewhere in the undo stack — we can rewind to before it (losing any
        // edits made afterwards, which the confirm dialog names explicitly).
        // After save+reopen the undo stack is empty → "baked in".
        int IndexOfWatermarkAdd(WatermarkRecord rec)
        {
            var target = $"Watermark added: \"{rec.Text}\"";
            for (int i = 0; i < _doc.History.Count; i++)
                if (_doc.History[i].Label == target) return i;
            return -1;
        }
        bool CanClean(WatermarkRecord rec) => IndexOfWatermarkAdd(rec) >= 0;
        var r = Controls.WatermarkManagerDialog.Show(existing, CanClean);
        if (r.Kind == Controls.WatermarkManagerDialog.ActionKind.None) return;
        try
        {
            IsBusy = true;
            byte[] bytes; string label;
            if (r.Kind == Controls.WatermarkManagerDialog.ActionKind.Add && r.ToAdd is not null)
            {
                var rec = r.ToAdd;
                bytes = await Task.Run(() => _watermark.Apply(_doc.Bytes!, rec));
                label = $"Watermark added: \"{rec.Text}\"";
            }
            else if (r.Kind == Controls.WatermarkManagerDialog.ActionKind.Edit
                     && r.ToAdd is not null && r.OriginalIdForEdit is not null)
            {
                // Edit = rewind past the original watermark's Add, then re-apply
                // with the new record on top. Reuses the Delete rewind machinery.
                var original = existing.FirstOrDefault(x => x.Id == r.OriginalIdForEdit);
                if (original is null) { IsBusy = false; return; }
                var wmIdx = IndexOfWatermarkAdd(original);
                if (wmIdx < 0)
                {
                    IsBusy = false;
                    MessageBox.Show(
                        "This watermark can no longer be edited from within the app.\n\n" +
                        "The undo history no longer contains its add entry. Start again from " +
                        "a clean source PDF if you need to change it.",
                        "Watermark", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var laterCount = wmIdx;
                string prompt;
                if (laterCount == 0)
                {
                    prompt = $"Replace watermark \"{original.Text}\" with the new settings?\n\n" +
                             "The pre-watermark bytes are restored from the undo stack (background " +
                             "preserved exactly), then the new watermark is applied on top. Ctrl+Z " +
                             "will reverse the change.";
                }
                else
                {
                    var laterLabels = string.Join("\n  • ",
                        _doc.History.Take(laterCount + 1).Take(laterCount).Select(h => h.Label));
                    prompt = $"Replace watermark \"{original.Text}\" with the new settings?\n\n" +
                             $"WARNING: {laterCount} edit(s) were made after this watermark and will " +
                             "also be undone before the replacement is applied:\n\n  • " + laterLabels +
                             "\n\nYou'll need to re-apply them manually if you still want them. " +
                             "Overlay annotations (text stamps, ticks, notes, drawings) are preserved.";
                }
                var confirm = MessageBox.Show(prompt, "Edit watermark",
                    MessageBoxButton.OKCancel,
                    laterCount == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.OK) { IsBusy = false; return; }

                var overlaysSnapshot = Pages.Select(p => p.Annotations.ToList()).ToList();
                _doc.RevertToBefore(wmIdx);
                await RebuildPagesAsync();
                for (int i = 0; i < overlaysSnapshot.Count && i < Pages.Count; i++)
                    foreach (var a in overlaysSnapshot[i])
                        Pages[i].Annotations.Add(a);

                var newRec = r.ToAdd;
                var newBytes = await Task.Run(() => _watermark.Apply(_doc.Bytes!, newRec));
                var editLabel = $"Watermark added: \"{newRec.Text}\"";
                await ApplyBytesPreservingOverlaysAsync(newBytes, editLabel);
                StatusText = $"Watermark edited: \"{original.Text}\" → \"{newRec.Text}\"" +
                             (laterCount > 0 ? $" ({laterCount} later edit(s) undone)." : ".");
                UndoCommand.NotifyCanExecuteChanged();
                return;
            }
            else if (r.Kind == Controls.WatermarkManagerDialog.ActionKind.Delete && r.IdToDelete is not null)
            {
                var toDelete = existing.FirstOrDefault(x => x.Id == r.IdToDelete);
                var text = toDelete?.Text ?? r.IdToDelete;

                if (toDelete is null)
                {
                    IsBusy = false;
                    return;
                }

                var wmIndex = IndexOfWatermarkAdd(toDelete);
                if (wmIndex < 0)
                {
                    IsBusy = false;
                    MessageBox.Show(
                        "This watermark can no longer be deleted from within the app.\n\n" +
                        "The undo history no longer contains its add entry (the document was saved " +
                        "and reopened, or the entry aged out of the 20-step undo cap). Start again " +
                        "from a clean source PDF if you need to change it.",
                        "Watermark", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // wmIndex tells us how many operations happened AFTER the watermark
                // was added (0 = watermark is the most recent op, N = N later edits).
                var laterCount = wmIndex;
                string prompt;
                if (laterCount == 0)
                {
                    prompt = $"Remove watermark \"{text}\"?\n\nThe pre-watermark bytes are restored " +
                             "from the undo stack, so the background is preserved exactly. You can " +
                             "Ctrl+Z afterward to bring the watermark back.";
                }
                else
                {
                    var laterLabels = string.Join("\n  • ",
                        _doc.History.Take(laterCount + 1).Take(laterCount).Select(h => h.Label));
                    prompt = $"Remove watermark \"{text}\"?\n\n" +
                             $"WARNING: {laterCount} edit(s) were made after this watermark and will " +
                             "also be undone:\n\n  • " + laterLabels + "\n\n" +
                             "You'll need to re-apply them manually if you still want them. Overlay " +
                             "annotations (text stamps, ticks, notes, drawings) are preserved.";
                }
                var confirm = MessageBox.Show(prompt, "Remove watermark",
                    MessageBoxButton.OKCancel,
                    laterCount == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.OK) { IsBusy = false; return; }

                // Snapshot overlays so they survive the rewind + RebuildPagesAsync.
                var overlaysSnapshot = Pages.Select(p => p.Annotations.ToList()).ToList();
                _doc.RevertToBefore(wmIndex);
                await RebuildPagesAsync();
                for (int i = 0; i < overlaysSnapshot.Count && i < Pages.Count; i++)
                    foreach (var a in overlaysSnapshot[i])
                        Pages[i].Annotations.Add(a);

                StatusText = $"Watermark removed: \"{text}\"" +
                             (laterCount > 0 ? $" (and {laterCount} later edit(s) undone)." : ".");
                UndoCommand.NotifyCanExecuteChanged();
                return;
            }
            else return;

            await ApplyBytesPreservingOverlaysAsync(bytes, label);
            StatusText = label + ".";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Watermark failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task AddHeadersFooters()
    {
        if (_doc.Bytes is null) return;
        var o = Controls.HeadersFootersDialog.Show();
        if (o is null) return;
        var fn = _doc.FilePath is null ? "" : Path.GetFileName(_doc.FilePath);
        try
        {
            IsBusy = true;
            var bytes = await Task.Run(() => _overlay.AddHeadersFooters(_doc.Bytes!, o, fn));
            await ApplyBytesPreservingOverlaysAsync(bytes, "Headers/footers");
            StatusText = "Headers/footers added.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Headers/footers failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task AddBates()
    {
        if (_doc.Bytes is null) return;
        var r = Controls.BatesDialog.Show();
        if (r is null) return;
        try
        {
            IsBusy = true;
            var bytes = await Task.Run(() => _overlay.AddBates(_doc.Bytes!, r.Prefix, r.StartNumber, r.Digits, "#000000", 10, r.BottomRight));
            await ApplyBytesPreservingOverlaysAsync(bytes, $"Bates numbering ({r.Prefix}{r.StartNumber:D}…)");
            StatusText = "Bates numbering added.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Bates failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void InsertImageOnCurrent()
    {
        if (_doc.Bytes is null || CurrentPage is null) return;
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        PlaceImageAsAnnotation(dlg.FileName, "Image placed. Drag with Select tool; Save to flatten.");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SanitizeMetadata()
    {
        if (_doc.Bytes is null) return;
        try
        {
            IsBusy = true;
            var bytes = await Task.Run(() => _security.SanitizeMetadata(_doc.Bytes!));
            await ApplyBytesPreservingOverlaysAsync(bytes, "Sanitize metadata");
            StatusText = "Metadata sanitized. Save to persist.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Sanitize failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void ClearAnnotations()
    {
        var curCount = CurrentPage?.Annotations.Count ?? 0;
        var total    = Pages.Sum(p => p.Annotations.Count);
        if (total == 0)
        {
            StatusText = "No annotations to clear.";
            return;
        }
        var scope = Controls.ClearAnnotationsDialog.Show(curCount, total);
        if (scope == Controls.ClearAnnotationsDialog.Scope.Cancel) return;

        if (scope == Controls.ClearAnnotationsDialog.Scope.Current && CurrentPage != null)
        {
            var page = CurrentPage;
            var snapshot = page.Annotations.ToList();
            page.Annotations.Clear();
            PushAnnotationUndo(() =>
            {
                foreach (var a in snapshot) page.Annotations.Add(a);
                StatusText = $"Undo: restored {snapshot.Count} annotation(s) on page {page.PageIndex + 1}.";
            });
            StatusText = $"Cleared {snapshot.Count} annotation(s) on page {page.PageIndex + 1}.";
        }
        else if (scope == Controls.ClearAnnotationsDialog.Scope.All)
        {
            var snapshots = Pages.Select(p => (page: p, list: p.Annotations.ToList())).ToList();
            foreach (var (page, _) in snapshots) page.Annotations.Clear();
            PushAnnotationUndo(() =>
            {
                int restored = 0;
                foreach (var (page, list) in snapshots)
                {
                    foreach (var a in list) page.Annotations.Add(a);
                    restored += list.Count;
                }
                StatusText = $"Undo: restored {restored} annotation(s) across all pages.";
            });
            StatusText = $"Cleared {total} annotation(s) across all pages.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Print()
    {
        if (_doc.Bytes is null) return;

        // Decide about note-type markup up front so we can exclude it from the flatten
        // step when the user wants a note-free print. "Note-type" here means sticky
        // notes (Layer 1 overlays or saved /Text) and callouts (Layer 1 overlays
        // only — callouts flatten into the content stream on Save and are no longer
        // separable after that).
        var hasNoteOverlays = Pages.Any(p => p.Annotations.Any(a =>
            a.Kind == Models.AnnotationKind.StickyNote ||
            a.Kind == Models.AnnotationKind.Callout));
        var hasSavedStickies = _annotate.HasStickyNotes(_doc.Bytes);
        var includeMarkup = true;
        if (hasNoteOverlays || hasSavedStickies)
        {
            var choice = MessageBox.Show(
                "This document contains notes / callouts.\n\nInclude them in the print?",
                "Print options", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;
            includeMarkup = choice == MessageBoxResult.Yes;
        }

        // Flatten unsaved overlays into a working byte stream so anything you've
        // annotated this session appears in the print. When hiding markup we
        // deliberately skip sticky notes and callouts so they don't get baked in.
        var overlaysToFlatten = includeMarkup
            ? Pages.SelectMany(p => p.Annotations).ToList()
            : Pages.SelectMany(p => p.Annotations).Where(a =>
                a.Kind != Models.AnnotationKind.StickyNote &&
                a.Kind != Models.AnnotationKind.Callout).ToList();
        byte[] workingBytes;
        try
        {
            workingBytes = overlaysToFlatten.Count > 0
                ? await Task.Run(() => _annotate.Flatten(_doc.Bytes!, overlaysToFlatten))
                : _doc.Bytes;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not prepare document for printing: " + ex.Message,
                "Print failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dlg = new System.Windows.Controls.PrintDialog
        {
            UserPageRangeEnabled = true,
            MinPage = 1,
            MaxPage = (uint)_doc.PageCount,
            PageRangeSelection = System.Windows.Controls.PageRangeSelection.AllPages
        };
        if (dlg.ShowDialog() != true) return;

        int first = 1, last = _doc.PageCount;
        if (dlg.PageRangeSelection == System.Windows.Controls.PageRangeSelection.UserPages)
        {
            first = System.Math.Max(1, dlg.PageRange.PageFrom);
            last = System.Math.Min(_doc.PageCount, dlg.PageRange.PageTo);
        }

        // workingBytes already has the session overlays flattened in (except for
        // notes/callouts if hiding markup). Also strip native /Text sticky-note
        // annotations from previously-saved documents when hiding markup.
        var printBytes = includeMarkup ? workingBytes : _annotate.StripAnnotations(workingBytes);

        try
        {
            IsBusy = true;
            StatusText = includeMarkup
                ? $"Rendering pages {first}-{last} for printing (with notes)..."
                : $"Rendering pages {first}-{last} for printing (notes hidden)...";

            // FixedDocument is a DispatcherObject and must be created + accessed on the
            // UI thread only. Build it here; page rasterisation is CPU-bound and runs in
            // parallel across cores. RenderPage returns a frozen BitmapSource so it's
            // safe to hand across threads — we collect results into an indexed array to
            // preserve page order, then add them to fixedDoc sequentially on the UI thread.
            var fixedDoc = new System.Windows.Documents.FixedDocument();
            fixedDoc.DocumentPaginator.PageSize = new System.Windows.Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);

            var printableW = dlg.PrintableAreaWidth;
            var printableH = dlg.PrintableAreaHeight;
            var pageCount = last - first + 1;
            var bitmaps = new System.Windows.Media.Imaging.BitmapSource[pageCount];
            int completed = 0;
            // Leave one core free for the UI thread; use at least one worker even on
            // single-core hardware.
            var parallelism = System.Math.Max(1, Environment.ProcessorCount - 1);

            await Task.Run(() =>
            {
                System.Threading.Tasks.Parallel.For(
                    0, pageCount,
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = parallelism },
                    k =>
                    {
                        int pageIdx = first - 1 + k;
                        bitmaps[k] = _render.RenderPage(printBytes, pageIdx, dpi: 200);
                        var done = System.Threading.Interlocked.Increment(ref completed);
                        // Cheap status update every page — Dispatcher.BeginInvoke is fire-and-forget.
                        Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                        {
                            StatusText = $"Rendering pages... {done}/{pageCount}";
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    });
            });

            for (int k = 0; k < pageCount; k++)
            {
                var page = new System.Windows.Documents.FixedPage
                {
                    Width = printableW,
                    Height = printableH
                };
                var img = new System.Windows.Controls.Image
                {
                    Source = bitmaps[k],
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Width = printableW,
                    Height = printableH
                };
                System.Windows.Documents.FixedPage.SetLeft(img, 0);
                System.Windows.Documents.FixedPage.SetTop(img, 0);
                page.Children.Add(img);
                var pc = new System.Windows.Documents.PageContent();
                ((System.Windows.Markup.IAddChild)pc).AddChild(page);
                fixedDoc.Pages.Add(pc);
            }

            dlg.PrintDocument(fixedDoc.DocumentPaginator, Path.GetFileName(_doc.FilePath ?? "document"));
            StatusText = $"Sent pages {first}-{last} to {dlg.PrintQueue.Name}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Print failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Print failed.";
        }
        finally { IsBusy = false; }
    }

    partial void OnHasDocumentChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        RotateLeftCommand.NotifyCanExecuteChanged();
        RotateRightCommand.NotifyCanExecuteChanged();
        DeleteCurrentPageCommand.NotifyCanExecuteChanged();
        InsertPagesFromFileCommand.NotifyCanExecuteChanged();
        ExtractRangeCommand.NotifyCanExecuteChanged();
        SplitEachPageCommand.NotifyCanExecuteChanged();
        ExtractTextCommand.NotifyCanExecuteChanged();
        ExportAsWordCommand.NotifyCanExecuteChanged();
        EditInWordCommand.NotifyCanExecuteChanged();
        ProtectWithPasswordCommand.NotifyCanExecuteChanged();
        RemoveProtectionCommand.NotifyCanExecuteChanged();
        SanitizeMetadataCommand.NotifyCanExecuteChanged();
        WatermarkCommand.NotifyCanExecuteChanged();
        AddHeadersFootersCommand.NotifyCanExecuteChanged();
        AddBatesCommand.NotifyCanExecuteChanged();
        InsertImageOnCurrentCommand.NotifyCanExecuteChanged();
        ExportAsImagesCommand.NotifyCanExecuteChanged();
        ExportAsHtmlCommand.NotifyCanExecuteChanged();
        ExportAsExcelCommand.NotifyCanExecuteChanged();
        OrganizePagesCommand.NotifyCanExecuteChanged();
        OcrAllPagesCommand.NotifyCanExecuteChanged();
        MakeSearchablePdfCommand.NotifyCanExecuteChanged();
        FillFormCommand.NotifyCanExecuteChanged();
        DrawSignatureCommand.NotifyCanExecuteChanged();
        LoadSignatureImageCommand.NotifyCanExecuteChanged();
        OpenSignatureLibraryCommand.NotifyCanExecuteChanged();
        CompareWithFileCommand.NotifyCanExecuteChanged();
        CropAllPagesCommand.NotifyCanExecuteChanged();
        RedactSearchResultsCommand.NotifyCanExecuteChanged();
        RunOcrOnCurrentCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        ClearAnnotationsCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged();
        ShowHistoryCommand.NotifyCanExecuteChanged();
    }
}
