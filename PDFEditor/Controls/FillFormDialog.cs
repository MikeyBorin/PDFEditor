using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PDFEditor.Services;

namespace PDFEditor.Controls;

public static class FillFormDialog
{
    public static Dictionary<string, string>? Show(IReadOnlyList<FormField> fields)
    {
        var w = new Window
        {
            Title = "Fill Form",
            Width = 520, Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var head = new TextBlock
        {
            Text = fields.Count == 0
                ? "This PDF has no AcroForm fields."
                : $"Edit values for {fields.Count} field(s). Blank values are left unchanged.",
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(head, Dock.Top);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Apply", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), IsEnabled = fields.Count > 0 };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        DockPanel.SetDock(btns, Dock.Bottom);

        var editors = new Dictionary<string, TextBox>();
        var stack = new StackPanel();
        foreach (var f in fields)
        {
            stack.Children.Add(new TextBlock { Text = $"{f.Name}   ({f.TypeName})", Margin = new Thickness(0, 8, 0, 2), FontWeight = FontWeights.SemiBold });
            var tb = new TextBox { Text = f.Value ?? "" };
            editors[f.Name] = tb;
            stack.Children.Add(tb);
        }
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        root.Children.Add(head);
        root.Children.Add(btns);
        root.Children.Add(scroll);
        w.Content = root;

        Dictionary<string, string>? result = null;
        ok.Click += (_, _) =>
        {
            result = editors.Where(kv => !string.IsNullOrEmpty(kv.Value.Text))
                            .ToDictionary(kv => kv.Key, kv => kv.Value.Text);
            w.DialogResult = true;
        };
        return w.ShowDialog() == true ? result : null;
    }
}
