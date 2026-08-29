using System.Windows;
using System.Windows.Controls;

namespace PDFEditor.Controls;

public enum RegionAction { None, Copy, Replace, Save, Translate }

public static class RegionActionDialog
{
    public static RegionAction ShowTextActions(string previewText)
    {
        return Show(
            title: "Selected text",
            preview: previewText,
            options: new[]
            {
                ("Copy", RegionAction.Copy),
                ("Replace...", RegionAction.Replace),
                ("Translate...", RegionAction.Translate),
            },
            isImage: false);
    }

    public static RegionAction ShowImageActions()
    {
        return Show(
            title: "Selected region",
            preview: "Image region captured. Copy to clipboard or save as PNG?",
            options: new[] { ("Copy", RegionAction.Copy), ("Save PNG...", RegionAction.Save) },
            isImage: true);
    }

    private static RegionAction Show(string title, string preview, (string, RegionAction)[] options, bool isImage)
    {
        var w = new Window
        {
            Title = title,
            Width = 460, SizeToContent = SizeToContent.Height,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)Application.Current!.Resources["Bg"],
            Foreground = (System.Windows.Media.Brush)Application.Current!.Resources["Text"]
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        if (!isImage)
        {
            var tb = new TextBox
            {
                Text = preview,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 60,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            root.Children.Add(tb);
        }
        else
        {
            root.Children.Add(new TextBlock { Text = preview, TextWrapping = TextWrapping.Wrap });
        }

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        RegionAction result = RegionAction.None;
        foreach (var (label, act) in options)
        {
            var b = new Button { Content = label, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
            b.Click += (_, _) => { result = act; w.DialogResult = true; };
            btns.Children.Add(b);
        }
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 4, 12, 4), MinWidth = 90, IsCancel = true };
        btns.Children.Add(cancel);
        root.Children.Add(btns);

        w.Content = root;
        return w.ShowDialog() == true ? result : RegionAction.None;
    }
}
