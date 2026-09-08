using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PDFEditor.Services;
using PDFEditor.ViewModels;

namespace PDFEditor;

public partial class MainWindow : Window
{
    private System.Windows.Threading.DispatcherTimer? _windowsMenuTimer;

    public MainWindow()
    {
        InitializeComponent();
        Icon = Controls.AppIcon.Create();
        Activated += (_, _) => RefreshWindowsMenuHeader();
        // Low-frequency poll so the Windows menu header count stays fresh
        // even if the user never re-focuses this window — for example, they
        // open a new file from here (spawns a new instance) and keep working
        // in the newly focused window. Cheap: enumerating processes is sub-ms.
        _windowsMenuTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromSeconds(3)
        };
        _windowsMenuTimer.Tick += (_, _) => RefreshWindowsMenuHeader();
        _windowsMenuTimer.Start();
        Closed += (_, _) => _windowsMenuTimer?.Stop();
        Loaded += (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.Theme.Apply(vm.Theme.Current);
                vm.ScrollIntoViewRequested += OnScrollIntoView;
                vm.ToolbarSettings.Changed += ApplyToolbarVisibility;
                vm.HitSelectionRequested += idx =>
                {
                    var lb = FindName("ResultsList") as ListBox;
                    if (lb != null && idx >= 0 && idx < lb.Items.Count) lb.SelectedIndex = idx;
                };
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ViewModels.MainViewModel.Zoom)
                        || args.PropertyName == nameof(ViewModels.MainViewModel.ZoomMode))
                        SyncZoomCombo();
                    if (args.PropertyName == nameof(ViewModels.MainViewModel.CurrentTextTool)
                        || args.PropertyName == nameof(ViewModels.MainViewModel.CurrentShapeTool))
                        RefreshGroupButtons();
                };
            }
            ApplyToolbarVisibility();
            SyncZoomCombo();
            RefreshGroupButtons();
            RefreshWindowsMenuHeader();
            // Restore the palette open/closed state from the previous session.
            if (VM.ViewSettings.Settings.ToolPaletteOpen && _toolPalette is null)
                ToggleToolPalette();
        };
    }

    private bool _syncingZoomCombo;
    private void SyncZoomCombo()
    {
        var combo = FindName("TB_ZoomLevel") as ComboBox;
        if (combo is null) return;
        _syncingZoomCombo = true;
        try
        {
            combo.Text = VM.ZoomMode switch
            {
                ViewModels.ZoomMode.FitWidth   => "Fit Width",
                ViewModels.ZoomMode.FitPage    => "Fit Page",
                ViewModels.ZoomMode.ActualSize => "Actual Size",
                _ /* Custom */                  => $"{VM.Zoom * 100:0}%",
            };
        }
        finally { _syncingZoomCombo = false; }
    }

    private void OnScrollIntoView(int pageIndex, double normX, double normY)
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            var items = FindVisualChild<ItemsControl>(PagesScroller);
            if (items is null) return;
            var container = items.ItemContainerGenerator.ContainerFromIndex(pageIndex) as FrameworkElement;
            if (container is null) return;
            // Bring the target area of the page into view.
            var top = container.TransformToAncestor(items).Transform(new Point(0, 0)).Y;
            var target = top + normY * container.ActualHeight - PagesScroller.ViewportHeight / 2;
            _suppressScrollChange = true;
            PagesScroller.ScrollToVerticalOffset(System.Math.Max(0, target));
            _suppressScrollChange = false;
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private MainViewModel VM => (MainViewModel)DataContext;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        // Bring to front on launch. When ArtiMax is started by another process
        // (Explorer file-association, another app shelling us out) Windows
        // suppresses focus-stealing so we open behind the launcher. Toggling
        // Topmost briefly forces us to the top of the Z-order; Activate/Focus
        // then transfers keyboard focus without leaving us permanently topmost.
        Topmost = true;
        Topmost = false;
        Activate();
        Focus();
        // First run: show welcome + disclaimer once. Persisted per-user under AppData.
        if (!Controls.WelcomeDialog.AlreadyAcknowledged())
        {
            Controls.WelcomeDialog.ShowOnce();
        }
        if (Application.Current is App app && !string.IsNullOrEmpty(app.PendingOpenPath))
        {
            await VM.LoadFileAsync(app.PendingOpenPath);
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length > 0)
            {
                await VM.OpenFileInAppropriateWindow(files[0]);
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Tab: cycle to next open PDF Editor window; Ctrl+Shift+Tab: previous.
        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var reverse = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            CycleToNextInstance(reverse);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ToggleToolPalette();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O: VM.OpenCommand.Execute(null); e.Handled = true; break;
                case Key.S when (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
                    VM.SaveAsCommand.Execute(null); e.Handled = true; break;
                case Key.S: VM.SaveCommand.Execute(null); e.Handled = true; break;
                case Key.P: VM.PrintCommand.Execute(null); e.Handled = true; break;
                case Key.OemPlus:
                case Key.Add: VM.ZoomInCommand.Execute(null); e.Handled = true; break;
                case Key.OemMinus:
                case Key.Subtract: VM.ZoomOutCommand.Execute(null); e.Handled = true; break;
                case Key.D0:
                case Key.NumPad0: VM.ZoomResetCommand.Execute(null); e.Handled = true; break;
                case Key.F: VM.SearchCommand.Execute(null); e.Handled = true; break;
                case Key.Z: VM.UndoCommand.Execute(null); e.Handled = true; break;
            }
        }
        if (e.Key == Key.F3)
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift) VM.PrevSearchHitCommand.Execute(null);
            else VM.NextSearchHitCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if ((e.Key == Key.F2 || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None))
            && VM.SelectedAnnotation != null
            && FocusManager.GetFocusedElement(this) is not TextBoxBase)
        {
            VM.EditSelectedAnnotationCommand.Execute(null);
            e.Handled = true;
        }
        if (e.Key == Key.Delete && VM.SelectedAnnotation != null && FocusManager.GetFocusedElement(this) is not TextBoxBase)
        {
            VM.DeleteSelectedAnnotationCommand.Execute(null);
            e.Handled = true;
        }
        if (e.Key == Key.Escape && VM.SelectedAnnotation != null)
        {
            VM.SelectedAnnotation = null;
            e.Handled = true;
        }
        if (e.Key == Key.F1)
        {
            OpenHelp();
            e.Handled = true;
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e) => OpenHelp();

    private void Support_Click(object sender, RoutedEventArgs e) => OpenHelp("support");

    private void OpenHelp(string? anchor = null)
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Help", "help.html");
            if (!System.IO.File.Exists(path))
            {
                MessageBox.Show($"Help file not found:\n{path}", "Help", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var target = string.IsNullOrWhiteSpace(anchor)
                ? path
                : new System.Uri(path).AbsoluteUri + "#" + anchor;
            var psi = new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Help", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) VM.SearchCommand.Execute(null);
    }

    private void PageNumberBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) VM.GoToPageCommand.Execute(null);
    }

    private void ZoomCombo_LostFocus(object sender, RoutedEventArgs e) => ApplyZoomFromCombo(sender);
    private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyZoomFromCombo(sender);
    private void ZoomCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyZoomFromCombo(sender); e.Handled = true; }
    }
    private void ApplyZoomFromCombo(object sender)
    {
        if (_syncingZoomCombo) return;
        if (sender is not ComboBox cb) return;
        var text = ((cb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? cb.Text ?? "").Trim();

        // Match a named fit-mode first (case-insensitive, ignores whitespace).
        var collapsed = text.Replace(" ", "").ToLowerInvariant();
        switch (collapsed)
        {
            case "fitwidth":   VM.ZoomMode = ViewModels.ZoomMode.FitWidth;   return;
            case "fitpage":    VM.ZoomMode = ViewModels.ZoomMode.FitPage;    return;
            case "actualsize": VM.ZoomMode = ViewModels.ZoomMode.ActualSize; return;
        }
        // Otherwise treat as a percentage.
        var num = text.Replace("%", "").Trim();
        if (double.TryParse(num, out var pct) && pct >= 5 && pct <= 1600)
        {
            VM.SetZoomPercentCommand.Execute(pct / 100.0);
        }
    }

    private void ApplyToolbarVisibility()
    {
        var svc = VM.ToolbarSettings;
        foreach (var name in Services.ToolbarSettingsService.AllCommandIds)
        {
            var elem = FindName("TB_" + name) as UIElement;
            if (elem is null) continue;
            elem.Visibility = svc.IsVisible(name) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---- Tool group split-buttons (Text and Shape) --------------------------
    // Each group shows the "current" tool from the group as a main button; its
    // arrow opens a popup to pick a different one. Picking updates both the
    // active tool and the group's remembered current.

    private record ToolEntry(ToolMode Mode, string Glyph, string Label, string Tooltip);

    private static readonly ToolEntry[] TextGroupTools = new[]
    {
        new ToolEntry(ToolMode.TextStamp, "", "Text", "Text stamp"),
        new ToolEntry(ToolMode.Tick,      "✓", "Tickmark", "Insert ✓ (checkbox tick)"),
        new ToolEntry(ToolMode.Cross,     "✗", "Crossmark", "Insert ✗ (checkbox cross)"),
        new ToolEntry(ToolMode.Bullet,    "•", "Bullet", "Insert • (bullet)"),
        new ToolEntry(ToolMode.Callout,   "", "Callout", "Speech-bubble note with a leader arrow pointing at a spot"),
    };

    private static readonly ToolEntry[] ShapeGroupTools = new[]
    {
        new ToolEntry(ToolMode.Ink,             "", "Draw",   "Freehand draw"),
        new ToolEntry(ToolMode.Rectangle,       "", "Rect",   "Rectangle (outlined)"),
        new ToolEntry(ToolMode.RectangleFilled, "", "Rect ●", "Rectangle (filled)"),
        new ToolEntry(ToolMode.Ellipse,         "", "Oval",   "Oval (outlined)"),
        new ToolEntry(ToolMode.EllipseFilled,   "", "Oval ●", "Oval (filled)"),
    };

    private void RefreshGroupButtons()
    {
        RefreshGroupButton(TextGroupTools, VM.CurrentTextTool,
            FindName("TB_TextGroup_Glyph") as TextBlock,
            FindName("TB_TextGroup_Label") as TextBlock,
            FindName("TB_TextGroup_Main") as Button);
        RefreshGroupButton(ShapeGroupTools, VM.CurrentShapeTool,
            FindName("TB_ShapeGroup_Glyph") as TextBlock,
            FindName("TB_ShapeGroup_Label") as TextBlock,
            FindName("TB_ShapeGroup_Main") as Button);
    }

    private static readonly System.Windows.Media.FontFamily Mdl2Font = new("Segoe MDL2 Assets");
    private static readonly System.Windows.Media.FontFamily UnicodeSymbolFont = new("Segoe UI Symbol, Segoe UI, Arial");

    private static void RefreshGroupButton(ToolEntry[] group, ToolMode current, TextBlock? glyphTb, TextBlock? labelTb, Button? mainBtn)
    {
        var e = System.Array.Find(group, x => x.Mode == current) ?? group[0];
        if (glyphTb != null)
        {
            glyphTb.Text = e.Glyph;
            // MDL2 has "notdef" boxes for regular Unicode chars, which prevents fallback.
            // Pick the right font per-glyph based on the code point range.
            var isMdl2 = e.Glyph.Length > 0 && e.Glyph[0] >= 0xE000 && e.Glyph[0] <= 0xF8FF;
            glyphTb.FontFamily = isMdl2 ? Mdl2Font : UnicodeSymbolFont;
        }
        if (labelTb != null) labelTb.Text = e.Label;
        if (mainBtn != null) mainBtn.ToolTip = e.Tooltip;
    }

    private void TextGroup_ShowMenu(object sender, RoutedEventArgs e)  => ShowGroupMenu(sender as UIElement, TextGroupTools);
    private void ShapeGroup_ShowMenu(object sender, RoutedEventArgs e) => ShowGroupMenu(sender as UIElement, ShapeGroupTools);

    // Standard palette for the toolbar Colour drop-down. 6 columns × 2 rows.
    private void ColourButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn) Controls.ColourSwatchPopup.Show(btn, VM);
    }

    private void ShowGroupMenu(UIElement? anchor, ToolEntry[] group)
    {
        if (anchor is null) return;
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false
        };
        // Attach to the anchor's ContextMenu property so WPF resolves the popup's
        // parent HWND via the anchor's visual tree. A bare `new ContextMenu()`
        // opened via IsOpen=true has no logical/visual parent, so its popup HWND
        // can't derive screen coordinates and ends up at the desktop origin
        // (0,0) — which is the "Text/Draw tool menu goes to top-left" bug.
        if (anchor is FrameworkElement fe) fe.ContextMenu = menu;
        foreach (var entry in group)
        {
            // MenuItem.Header takes a UIElement — build a two-column row so the MDL2
            // glyph renders in the correct font next to a plain-font label.
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            // MDL2 icons (E000-F8FF PUA) render only in "Segoe MDL2 Assets"; Segoe MDL2
            // has "notdef" boxes for regular Unicode chars (✓ ✗ •) which suppresses WPF's
            // fallback. So pick the font per-glyph based on the code point range.
            var isMdl2 = entry.Glyph.Length > 0 && entry.Glyph[0] >= 0xE000 && entry.Glyph[0] <= 0xF8FF;
            row.Children.Add(new TextBlock
            {
                Text = entry.Glyph,
                FontFamily = isMdl2
                    ? new System.Windows.Media.FontFamily("Segoe MDL2 Assets")
                    : new System.Windows.Media.FontFamily("Segoe UI Symbol, Segoe UI, Arial"),
                FontSize = 16,
                Width = 26,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = entry.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            });
            var item = new System.Windows.Controls.MenuItem
            {
                Header = row,
                Tag = entry.Mode,
                ToolTip = entry.Tooltip
            };
            item.Click += (_, _) => { VM.CurrentTool = entry.Mode; };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void SearchResults_DoubleClick(object sender, MouseButtonEventArgs e) => JumpToSelectedHit(sender);
    private void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e) => JumpToSelectedHit(sender);
    private void JumpToSelectedHit(object sender)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not SearchHit hit) return;
        VM.GoToHitCommand.Execute(hit);
        if (hit.NormW > 0 && hit.NormH > 0)
        {
            VM.TransientHighlight = (hit.PageIndex, hit.NormX, hit.NormY, hit.NormW, hit.NormH);
            VM.RequestScrollIntoView(hit.PageIndex, hit.NormX + hit.NormW / 2, hit.NormY + hit.NormH / 2);
        }
        else
        {
            VM.TransientHighlight = null;
            VM.RequestScrollIntoView(hit.PageIndex, 0.5, 0.1);
        }
    }

    private bool _suppressScrollChange;

    private void ThumbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection change triggered by a click on a thumbnail (not by scroll-driven
        // selection change): jump the main viewer to the picked page. Proportional
        // scroll sync then takes care of aligning the sidebar itself.
        if (_mainScrollDrivingSelection) return;
        if (VM.CurrentPage is null) return;
        VM.RequestScrollIntoView(VM.CurrentPage.PageIndex, 0.5, 0.05);
    }

    // Proportional scroll sync between the main viewer and the thumbnail sidebar —
    // both scrollbars behave like the same scrollbar in two skins. Guard flags
    // stop the two ScrollChanged handlers from ping-ponging into a feedback loop.
    private System.Windows.Controls.ScrollViewer? _thumbScrollViewer;
    private bool _syncingMainFromThumb;
    private bool _syncingThumbFromMain;
    private bool _mainScrollDrivingSelection;

    private void EnsureThumbScrollViewer()
    {
        if (_thumbScrollViewer != null) return;
        if (FindName("ThumbList") is System.Windows.Controls.ListBox lb)
        {
            _thumbScrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(lb);
            if (_thumbScrollViewer != null)
            {
                _thumbScrollViewer.ScrollChanged += ThumbScrollViewer_ScrollChanged;
            }
        }
    }

    private void PagesScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        EnsureThumbScrollViewer();
        if (_suppressScrollChange) return;
        var sv = (ScrollViewer)sender;

        // Update CurrentPage to whichever page is centered in the viewport.
        var items = FindVisualChild<ItemsControl>(sv);
        if (items != null && items.Items.Count > 0)
        {
            double viewportMid = sv.VerticalOffset + sv.ViewportHeight / 2.0;
            for (int i = 0; i < items.Items.Count; i++)
            {
                var container = items.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container is null) continue;
                var transform = container.TransformToAncestor(items);
                var topInItems = transform.Transform(new Point(0, 0)).Y;
                var bottomInItems = topInItems + container.ActualHeight;
                if (viewportMid >= topInItems && viewportMid <= bottomInItems)
                {
                    if (items.Items[i] is ViewModels.PageViewModel pvm && !ReferenceEquals(VM.CurrentPage, pvm))
                    {
                        _mainScrollDrivingSelection = true;
                        try { VM.CurrentPage = pvm; }
                        finally { _mainScrollDrivingSelection = false; }
                    }
                    break;
                }
            }
        }

        // Proportional sync: thumb sidebar mirrors the main viewer's scroll ratio.
        // Skip if this scroll was itself caused by thumb → main sync (prevents loop).
        if (!_syncingMainFromThumb && _thumbScrollViewer != null)
        {
            var mainRange  = System.Math.Max(1, sv.ExtentHeight - sv.ViewportHeight);
            var thumbRange = System.Math.Max(1, _thumbScrollViewer.ExtentHeight - _thumbScrollViewer.ViewportHeight);
            var ratio = sv.VerticalOffset / mainRange;
            _syncingThumbFromMain = true;
            try { _thumbScrollViewer.ScrollToVerticalOffset(ratio * thumbRange); }
            finally { _syncingThumbFromMain = false; }
        }
    }

    private void ThumbScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncingThumbFromMain) return;
        var thumb = (ScrollViewer)sender;
        var thumbRange = System.Math.Max(1, thumb.ExtentHeight - thumb.ViewportHeight);
        var mainRange  = System.Math.Max(1, PagesScroller.ExtentHeight - PagesScroller.ViewportHeight);
        var ratio = thumb.VerticalOffset / thumbRange;
        _syncingMainFromThumb = true;
        try { PagesScroller.ScrollToVerticalOffset(ratio * mainRange); }
        finally { _syncingMainFromThumb = false; }
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private void Bookmarks_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.TreeView tv && tv.SelectedItem is Services.BookmarkNode n)
        {
            VM.GoToBookmarkCommand.Execute(n);
        }
    }

    // --- Drag-reorder thumbnails ---
    private System.Windows.Point _dragStart;
    private int _dragSourceIndex = -1;

    private void ThumbList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragSourceIndex = -1;
        if (sender is not ListBox lb) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as System.Windows.DependencyObject);
        if (item != null)
            _dragSourceIndex = lb.ItemContainerGenerator.IndexFromContainer(item);
    }

    private void ThumbList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceIndex < 0) return;
        var pos = e.GetPosition(null);
        var dx = System.Math.Abs(pos.X - _dragStart.X);
        var dy = System.Math.Abs(pos.Y - _dragStart.Y);
        if (dx < SystemParameters.MinimumHorizontalDragDistance && dy < SystemParameters.MinimumVerticalDragDistance) return;

        if (sender is ListBox lb)
        {
            DragDrop.DoDragDrop(lb, _dragSourceIndex.ToString(), DragDropEffects.Move);
        }
    }

    private void ThumbList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private bool _dropHandled;
    private async void ThumbList_Drop(object sender, DragEventArgs e)
    {
        if (_dropHandled) return; // guard: fires for both PreviewDrop and Drop
        if (sender is not ListBox lb) return;
        if (!e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            VM.StatusText = "Drop: no data present.";
            return;
        }
        if (!int.TryParse((string)e.Data.GetData(DataFormats.StringFormat), out var srcIdx))
        {
            VM.StatusText = "Drop: bad payload.";
            return;
        }

        var target = FindAncestor<ListBoxItem>(e.OriginalSource as System.Windows.DependencyObject);
        int dstIdx = target != null ? lb.ItemContainerGenerator.IndexFromContainer(target) : lb.Items.Count - 1;
        if (dstIdx < 0)
        {
            VM.StatusText = "Drop: no target.";
            return;
        }
        if (srcIdx == dstIdx)
        {
            VM.StatusText = $"Drop: same slot ({srcIdx}).";
            return;
        }

        _dropHandled = true;
        e.Handled = true;
        VM.StatusText = $"Reordering: {srcIdx + 1} → {dstIdx + 1}...";

        var order = new System.Collections.Generic.List<int>();
        int n = lb.Items.Count;
        for (int i = 0; i < n; i++) order.Add(i);
        var moving = order[srcIdx];
        order.RemoveAt(srcIdx);
        order.Insert(dstIdx, moving);
        await VM.ReorderPagesAsync(order);
        _dropHandled = false;
    }

    private static T? FindAncestor<T>(System.Windows.DependencyObject? d) where T : System.Windows.DependencyObject
    {
        while (d != null && d is not T) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return d as T;
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (!await VM.ConfirmDiscardChangesAsync()) return;
        _forceClose = true;
        Close();
    }

    private bool _forceClose;
    private bool _isClosing;

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Set as soon as the OS starts closing us. Read by the palette's
        // Closed handler so it can distinguish "user closed the palette" from
        // "palette closed because owner window closed" — only the former
        // should persist ToolPaletteOpen = false.
        _isClosing = true;
        if (_forceClose) return;
        if (!VM.HasUnsavedChanges) return;
        // Defer close and prompt.
        e.Cancel = true;
        if (await VM.ConfirmDiscardChangesAsync())
        {
            _forceClose = true;
            // Defer Close() to a fresh dispatcher tick. ConfirmDiscardChangesAsync uses
            // a synchronous MessageBox.Show for the Yes/No/Cancel prompt; for the "No"
            // branch there's no `await` inside it, so the task completes synchronously
            // and the awaiter here resumes on the *same* stack frame — i.e. still inside
            // the Window_Closing invocation. Calling Window.Close() while the window is
            // mid-Closing throws InvalidOperationException. Posting via BeginInvoke lets
            // the current Closing event unwind first, then Close runs cleanly.
            _ = Dispatcher.BeginInvoke(new System.Action(() => Close()));
        }
        else
        {
            // User cancelled the close — the window keeps living, so the
            // palette shouldn't treat itself as being torn down with us.
            _isClosing = false;
        }
    }

    // --- Windows menu: switch between multiple running instances -----------
    // Each Open-file launch spawns a new PDFEditor.exe. This menu enumerates
    // the others by walking the process list on demand and reading their
    // MainWindowTitle (which already carries the open filename).

    private void RefreshWindowsMenuHeader()
    {
        try
        {
            var others = Services.InstanceSwitcherService.GetOtherInstances();
            WindowsMenu.Header = others.Count == 0 ? "_Windows" : $"_Windows ({others.Count + 1})";
            // Also feed the count into the VM so the title bar picks up the
            // "[N windows]" suffix — the visible cue at the top of the window.
            if (DataContext is ViewModels.MainViewModel vm) vm.OtherWindowCount = others.Count;
        }
        catch { WindowsMenu.Header = "_Windows"; }
    }

    private void WindowsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        WindowsMenu.Items.Clear();

        // Current window first, checked, disabled — anchor point.
        var self = new MenuItem
        {
            Header = "✓ " + (VM?.Title ?? Title ?? "(this window)"),
            IsEnabled = false
        };
        WindowsMenu.Items.Add(self);

        var others = Services.InstanceSwitcherService.GetOtherInstances();
        if (others.Count == 0)
        {
            WindowsMenu.Items.Add(new Separator());
            WindowsMenu.Items.Add(new MenuItem { Header = "(No other windows open)", IsEnabled = false });
            return;
        }

        WindowsMenu.Items.Add(new Separator());
        foreach (var inst in others)
        {
            // Row is filename + trailing X button. Clicking the row switches
            // to that window; clicking the X sends WM_CLOSE to it (which the
            // target respects with the usual save-changes prompt).
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = inst.FileLabel,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var closeBtn = new Button
            {
                Content = "✕",
                Width = 20, Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = 11,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Close this window (prompts if unsaved)",
                Focusable = false
            };
            Grid.SetColumn(closeBtn, 1);
            row.Children.Add(closeBtn);

            var mi = new MenuItem
            {
                Header = row,
                ToolTip = $"PID {inst.ProcessId} · {inst.FullTitle}"
            };
            var hwnd = inst.Hwnd;
            mi.Click += (_, _) => Services.InstanceSwitcherService.Activate(hwnd);

            // Handler for the X. StaysOpenOnClick keeps the menu open so you
            // can close several in a row; we mark the event handled so the
            // enclosing MenuItem.Click (switch-to) doesn't also fire.
            closeBtn.Click += (_, ev) =>
            {
                Services.InstanceSwitcherService.RequestClose(hwnd);
                ev.Handled = true;
                // Rebuild the submenu so the closed row disappears immediately.
                Dispatcher.BeginInvoke(new System.Action(() => WindowsMenu_SubmenuOpened(sender, e)));
            };

            WindowsMenu.Items.Add(mi);
        }

        WindowsMenu.Items.Add(new Separator());
        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => WindowsMenu_SubmenuOpened(sender, e);
        WindowsMenu.Items.Add(refresh);

        var closeAll = new MenuItem
        {
            Header = $"Close All Windows ({others.Count + 1})",
            ToolTip = "Send Close to every open PDF Editor window (each still prompts for unsaved changes)"
        };
        closeAll.Click += (_, _) => CloseAllWindows();
        WindowsMenu.Items.Add(closeAll);

        RefreshWindowsMenuHeader();
    }

    private void CloseAllWindows()
    {
        var others = Services.InstanceSwitcherService.GetOtherInstances();
        foreach (var inst in others)
            Services.InstanceSwitcherService.RequestClose(inst.Hwnd);
        // Close ourselves last, on a fresh dispatcher tick so the menu can
        // unwind first. Window_Closing still runs the unsaved-changes prompt.
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void CycleToNextInstance(bool reverse)
    {
        var others = Services.InstanceSwitcherService.GetOtherInstances();
        if (others.Count == 0) return;
        var target = reverse ? others[^1] : others[0];
        Services.InstanceSwitcherService.Activate(target.Hwnd);
    }

    // --- Floating Tool Palette ---------------------------------------------
    private Controls.ToolPaletteWindow? _toolPalette;

    private void ToolPaletteMenuItem_Click(object sender, RoutedEventArgs e) => ToggleToolPalette();

    private void CustomisePalette_Click(object sender, RoutedEventArgs e)
        => Controls.PaletteCustomiseDialog.Show(VM);

    private void ToggleToolPalette()
    {
        if (_toolPalette is { IsLoaded: true })
        {
            _toolPalette.Close();
            _toolPalette = null;
            ToolPaletteMenuItem.IsChecked = false;
            SetPaletteOpenSetting(false);
            return;
        }
        _toolPalette = new Controls.ToolPaletteWindow(VM, this);
        _toolPalette.Closed += (_, _) =>
        {
            _toolPalette = null;
            ToolPaletteMenuItem.IsChecked = false;
            // Only persist the "closed" state when the palette was closed by
            // the user — NOT when it closes as a side-effect of the main
            // window shutting down. Owned windows auto-close with their owner,
            // and saving `ToolPaletteOpen = false` there would make the
            // palette forget its state across app restarts.
            if (!_isClosing) SetPaletteOpenSetting(false);
        };
        _toolPalette.Show();
        // Anchor at the top-left of the page-viewer column, just past the
        // thumbnails + splitter. PointToScreen returns device pixels;
        // TransformFromDevice converts to DIPs so Window.Left/Top is correct
        // on high-DPI displays.
        try
        {
            var origin = PagesScroller.PointToScreen(new System.Windows.Point(0, 0));
            var src = System.Windows.PresentationSource.FromVisual(this);
            if (src?.CompositionTarget is not null)
            {
                var dip = src.CompositionTarget.TransformFromDevice.Transform(origin);
                _toolPalette.Left = dip.X;
                _toolPalette.Top = dip.Y;
            }
        }
        catch { /* fall back to constructor default */ }
        ToolPaletteMenuItem.IsChecked = true;
        SetPaletteOpenSetting(true);
    }

    private void SetPaletteOpenSetting(bool open)
    {
        try
        {
            VM.ViewSettings.Settings.ToolPaletteOpen = open;
            VM.ViewSettings.Save();
        }
        catch { }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "ArtiMax PDF Editor  v1.0.34\n\n" +
            "Desktop PDF editor by ArtiMax. Free for personal / non-commercial use\n" +
            "under the PolyForm Noncommercial License 1.0.0. Commercial use requires\n" +
            "a separate written licence — email support@artimax.com.au.\n\n" +
            "Built with WPF, PdfSharpCore, PDFium, PdfPig and Tesseract (all\n" +
            "permissive open-source components).\n\n" +
            "Drop a PDF onto the window to open. Ctrl+O / Ctrl+S / Ctrl+P for common actions.\n" +
            "Press F1 at any time to open the help page.\n\n" +
            "Source code:\n" +
            "  https://github.com/MikeyBorin/PDFEditor\n\n" +
            "Enjoying the app? Support development:\n" +
            "  https://github.com/sponsors/MikeyBorin\n" +
            "  https://ko-fi.com/mikeyborin\n\n" +
            "─────────────────────────────────────────────\n" +
            "DISCLAIMER\n\n" +
            "This software is provided \"AS IS\", without warranty of any kind, express " +
            "or implied. There is no guarantee that it is fit for any particular purpose. " +
            "ArtiMax accepts no liability for data loss, corrupted files, or any other " +
            "damages arising from use of this software.\n\n" +
            "USE AT YOUR OWN RISK. Keep backups of important documents before editing.\n\n" +
            "See the LICENSE file for full terms (PolyForm Noncommercial 1.0.0).",
            "About ArtiMax PDF Editor", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
