using System.Windows;
using System.Windows.Controls;

namespace PDFEditor.Controls;

public enum RegionAction { None, Copy, Replace, Save, Translate }

public record RegionActionResult(RegionAction Choice, bool AlwaysCopy);

public static class RegionActionDialog
{
    public static RegionActionResult ShowTextActions(string previewText, bool initialAlwaysCopy)
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
            isImage: false,
            showAlwaysCopy: true,
            initialAlwaysCopy: initialAlwaysCopy);
    }

    public static RegionAction ShowImageActions()
    {
        return Show(
            title: "Selected region",
            preview: "Image region captured. Copy to clipboard or save as PNG?",
            options: new[] { ("Copy", RegionAction.Copy), ("Save PNG...", RegionAction.Save) },
            isImage: true,
            showAlwaysCopy: false,
            initialAlwaysCopy: false).Choice;
    }

    private static RegionActionResult Show(string title, string preview, (string, RegionAction)[] options, bool isImage,
                                           bool showAlwaysCopy, bool initialAlwaysCopy)
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

        CheckBox? alwaysCopyBox = null;
        if (showAlwaysCopy)
        {
            alwaysCopyBox = new CheckBox
            {
                Content = "Auto-copy to clipboard when text is selected",
                ToolTip = "When ticked, the text is copied to the clipboard as soon as it's selected — the Copy button below becomes optional. Toggle also from Tools → Auto-Copy Selected Text.",
                IsChecked = initialAlwaysCopy,
                Margin = new Thickness(0, 12, 0, 0)
            };
            root.Children.Add(alwaysCopyBox);
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
        var ok = w.ShowDialog() == true;
        var alwaysCopy = alwaysCopyBox?.IsChecked == true;
        return new RegionActionResult(ok ? result : RegionAction.None, alwaysCopy);
    }
}
