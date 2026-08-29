using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PDFEditor.Services;
using PDFEditor.ViewModels;

namespace PDFEditor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Icon = Controls.AppIcon.Create();
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
                    if (args.PropertyName == nameof(ViewModels.MainViewModel.Zoom))
                        SyncZoomCombo();
                };
            }
            ApplyToolbarVisibility();
            SyncZoomCombo();
        };
    }

    private void SyncZoomCombo()
    {
        var combo = FindName("TB_ZoomLevel") as ComboBox;
        if (combo is null) return;
        combo.Text = $"{VM.Zoom * 100:0}%";
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
                await VM.LoadFileAsync(files[0]);
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
        if (sender is not ComboBox cb) return;
        var text = (cb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? cb.Text ?? "";
        text = text.Replace("%", "").Trim();
        if (double.TryParse(text, out var pct) && pct >= 10 && pct <= 800)
        {
            VM.Zoom = pct / 100.0;
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
        if (VM.CurrentPage is null) return;
        VM.RequestScrollIntoView(VM.CurrentPage.PageIndex, 0.5, 0.05);
    }

    private void PagesScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_suppressScrollChange) return;
        // Find the ItemsControl → StackPanel with page items and figure out which is centered.
        var sv = (ScrollViewer)sender;
        var items = FindVisualChild<ItemsControl>(sv);
        if (items is null || items.Items.Count == 0) return;
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
                    VM.CurrentPage = pvm;
                }
                return;
            }
        }
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

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;
        if (!VM.HasUnsavedChanges) return;
        // Defer close and prompt.
        e.Cancel = true;
        if (await VM.ConfirmDiscardChangesAsync())
        {
            _forceClose = true;
            Close();
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "PDF Editor\n\n" +
            "Free desktop PDF editor built with WPF, PdfSharpCore, PDFium, PdfPig and Tesseract.\n" +
            "All components use permissive open-source licences.\n\n" +
            "Drop a PDF onto the window to open. Ctrl+O / Ctrl+S / Ctrl+P for common actions.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
