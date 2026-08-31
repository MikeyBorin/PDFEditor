using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PDFEditor.Controls;

/// <summary>
/// Scope picker for Tools → Clear Annotations. Three buttons: Current Page,
/// All Pages, Cancel — each button shows its live count so users know what
/// they're about to wipe. Cancel is the safe default (Escape / X).
/// </summary>
public static class ClearAnnotationsDialog
{
    public enum Scope { Cancel, Current, All }

    public static Scope Show(int currentPageCount, int totalCount)
    {
        var w = new Window
        {
            Title = "Clear annotations",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current!.Resources["Bg"],
            Foreground = (Brush)Application.Current!.Resources["Text"]
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Clear which annotations?",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Removes unsaved overlay annotations only — highlights, sticky notes, text stamps, " +
                   "tickmarks, drawings, shapes, whiteouts, placed images. Ctrl+Z will restore.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 14)
        });

        Scope result = Scope.Cancel;
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cur = new Button
        {
            Content = $"Current page ({currentPageCount})",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 130,
            IsEnabled = currentPageCount > 0,
            IsDefault = currentPageCount > 0
        };
        var all = new Button
        {
            Content = $"All pages ({totalCount})",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 130,
            IsEnabled = totalCount > 0
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 4, 12, 4),
            MinWidth = 90,
            IsCancel = true
        };
        cur.Click += (_, _) => { result = Scope.Current; w.DialogResult = true; };
        all.Click += (_, _) => { result = Scope.All;     w.DialogResult = true; };
        btns.Children.Add(cur);
        btns.Children.Add(all);
        btns.Children.Add(cancel);
        root.Children.Add(btns);
        w.Content = root;
        w.ShowDialog();
        return result;
    }
}
