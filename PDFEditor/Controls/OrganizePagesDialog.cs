using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFEditor.Services;

namespace PDFEditor.Controls;

public class OrganizePagesResult
{
    /// <summary>Original page indexes that should be deleted (before reorder).</summary>
    public List<int> DeletedIndexes { get; set; } = new();
    /// <summary>New order after deletions — indexes into the *deleted* list.</summary>
    public List<int> NewOrder { get; set; } = new();
}

public static class OrganizePagesDialog
{
    private class Item
    {
        public int OriginalIndex { get; set; }
        public BitmapSource? Thumbnail { get; set; }
        public string Label => (OriginalIndex + 1).ToString();
    }

    public static OrganizePagesResult? Show(byte[] pdfBytes, PdfRenderService render)
    {
        var w = new Window
        {
            Title = "Organise Pages",
            Width = 900, Height = 640,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current!.Resources["Bg"],
            Foreground = (Brush)Application.Current!.Resources["Text"]
        };
        var root = new DockPanel { Margin = new Thickness(12) };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var lbl = new TextBlock { Text = "Drag thumbnails to reorder. Ctrl+click for multi-select. Delete key removes selection.",
            Foreground = (Brush)Application.Current!.Resources["TextMuted"], VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(lbl);
        DockPanel.SetDock(toolbar, Dock.Top);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var delBtn = new Button { Content = "Delete Selected", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Apply", Width = 90, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 28, IsCancel = true };
        bottom.Children.Add(delBtn); bottom.Children.Add(ok); bottom.Children.Add(cancel);
        DockPanel.SetDock(bottom, Dock.Bottom);

        var list = new ListBox
        {
            SelectionMode = SelectionMode.Extended,
            Background = (Brush)Application.Current!.Resources["PanelAlt"],
            Foreground = (Brush)Application.Current!.Resources["Text"],
            BorderThickness = new Thickness(0),
            AllowDrop = true
        };
        list.ItemsPanel = BuildItemsPanel();
        list.ItemTemplate = BuildTemplate();

        var items = new ObservableCollection<Item>();
        var count = PDFtoImage.Conversion.GetPageCount(pdfBytes);
        for (int i = 0; i < count; i++)
        {
            var t = render.RenderThumbnail(pdfBytes, i, 160);
            items.Add(new Item { OriginalIndex = i, Thumbnail = t });
        }
        list.ItemsSource = items;

        // Drag-drop reorder for the ListBox
        Point dragStart = default;
        int dragSourceIndex = -1;
        list.PreviewMouseLeftButtonDown += (s, e) =>
        {
            dragStart = e.GetPosition(list);
            dragSourceIndex = -1;
            var el = e.OriginalSource as DependencyObject;
            while (el != null && el is not ListBoxItem) el = VisualTreeHelper.GetParent(el);
            if (el is ListBoxItem lbi) dragSourceIndex = list.ItemContainerGenerator.IndexFromContainer(lbi);
        };
        list.PreviewMouseMove += (s, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragSourceIndex < 0) return;
            var pos = e.GetPosition(list);
            if (System.Math.Abs(pos.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                System.Math.Abs(pos.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            DragDrop.DoDragDrop(list, dragSourceIndex.ToString(), DragDropEffects.Move);
        };
        list.PreviewDragOver += (s, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
        list.Drop += (s, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
            if (!int.TryParse((string)e.Data.GetData(DataFormats.StringFormat), out var srcIdx)) return;
            var el = e.OriginalSource as DependencyObject;
            while (el != null && el is not ListBoxItem) el = VisualTreeHelper.GetParent(el);
            int dstIdx = el is ListBoxItem lbi ? list.ItemContainerGenerator.IndexFromContainer(lbi) : items.Count - 1;
            if (srcIdx == dstIdx || srcIdx < 0 || dstIdx < 0) return;
            var moving = items[srcIdx];
            items.RemoveAt(srcIdx);
            items.Insert(dstIdx, moving);
            list.SelectedItem = moving;
            e.Handled = true;
        };

        void DeleteSelected()
        {
            var sel = list.SelectedItems.OfType<Item>().ToList();
            foreach (var it in sel) items.Remove(it);
        }
        delBtn.Click += (_, _) => DeleteSelected();
        list.KeyDown += (_, e) => { if (e.Key == Key.Delete) DeleteSelected(); };

        root.Children.Add(toolbar);
        root.Children.Add(bottom);
        root.Children.Add(list);
        w.Content = root;

        OrganizePagesResult? result = null;
        ok.Click += (_, _) =>
        {
            var kept = items.Select(i => i.OriginalIndex).ToList();
            var allIndexes = Enumerable.Range(0, count).ToList();
            var deleted = allIndexes.Except(kept).ToList();
            // NewOrder is indexes into the post-delete document.
            // After deleting pages, the remaining page indices renumber sequentially.
            // Build map: original index → new index in doc-after-delete.
            var keptOrdered = allIndexes.Where(i => !deleted.Contains(i)).ToList();
            var origToNew = new Dictionary<int, int>();
            for (int i = 0; i < keptOrdered.Count; i++) origToNew[keptOrdered[i]] = i;
            var newOrder = kept.Select(o => origToNew[o]).ToList();
            result = new OrganizePagesResult { DeletedIndexes = deleted, NewOrder = newOrder };
            w.DialogResult = true;
        };

        return w.ShowDialog() == true ? result : null;
    }

    private static ItemsPanelTemplate BuildItemsPanel()
    {
        var xaml = @"<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                       <WrapPanel Orientation='Horizontal'/>
                     </ItemsPanelTemplate>";
        return (ItemsPanelTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    private static DataTemplate BuildTemplate()
    {
        var xaml = @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                        <StackPanel Margin='8' Width='160'>
                          <Border BorderBrush='#666' BorderThickness='1' Background='White'>
                            <Image Source='{Binding Thumbnail}' Stretch='Uniform' MaxHeight='200'/>
                          </Border>
                          <TextBlock Text='{Binding Label, StringFormat=Page {0}}' HorizontalAlignment='Center' Margin='0,4,0,0' Foreground='{DynamicResource Text}'/>
                        </StackPanel>
                     </DataTemplate>";
        return (DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}
