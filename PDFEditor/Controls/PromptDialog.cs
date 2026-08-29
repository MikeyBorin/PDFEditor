using System.Windows;
using System.Windows.Controls;

namespace PDFEditor.Controls;

public static class PromptDialog
{
    public static string? Ask(string title, string prompt, string initial = "")
    {
        var window = new Window
        {
            Title = title,
            Width = 380,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(label, Dock.Top);
        var box = new TextBox { Text = initial };
        DockPanel.SetDock(box, Dock.Top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(label);
        root.Children.Add(buttons);
        root.Children.Add(box);
        window.Content = root;

        string? result = null;
        ok.Click += (_, _) => { result = box.Text; window.DialogResult = true; };
        box.Loaded += (_, _) => box.Focus();
        return window.ShowDialog() == true ? result : null;
    }
}
