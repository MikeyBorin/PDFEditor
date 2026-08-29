using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PDFEditor.Services;

namespace PDFEditor.Controls;

public static class TranslateDialog
{
    public record Result(string SourceCode, string TargetCode, bool AllPages);

    /// <summary>
    /// Language picker + scope (current / all pages) for the MyMemory translate feature.
    /// Pass the last-used codes so the pickers open where the user left off.
    /// </summary>
    public static Result? Show(string defaultSource, string defaultTarget, bool canDoAllPages)
    {
        var w = new Window
        {
            Title = "Translate",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(12) };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var srcLabel = new TextBlock { Text = "Source:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
        var tgtLabel = new TextBlock { Text = "Target:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
        var srcBox = BuildLangCombo(defaultSource);
        var tgtBox = BuildLangCombo(defaultTarget);

        Grid.SetRow(srcLabel, 0); Grid.SetColumn(srcLabel, 0);
        Grid.SetRow(srcBox,   0); Grid.SetColumn(srcBox,   1);
        Grid.SetRow(tgtLabel, 1); Grid.SetColumn(tgtLabel, 0);
        Grid.SetRow(tgtBox,   1); Grid.SetColumn(tgtBox,   1);
        grid.Children.Add(srcLabel); grid.Children.Add(srcBox);
        grid.Children.Add(tgtLabel); grid.Children.Add(tgtBox);

        var scopeBox = new CheckBox
        {
            Content = "Translate ALL pages (uses more of your daily quota)",
            IsChecked = false,
            IsEnabled = canDoAllPages,
            Margin = new Thickness(0, 10, 0, 4)
        };
        Grid.SetRow(scopeBox, 2); Grid.SetColumn(scopeBox, 0); Grid.SetColumnSpan(scopeBox, 2);
        grid.Children.Add(scopeBox);

        DockPanel.SetDock(grid, Dock.Top);

        var note = new TextBlock
        {
            Text = "Uses api.mymemory.translated.net (free public API, ~10 KB of text/day per IP; no signup).",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(note, Dock.Top);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Translate", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);

        root.Children.Add(grid);
        root.Children.Add(note);
        root.Children.Add(buttons);
        w.Content = root;

        Result? result = null;
        ok.Click += (_, _) =>
        {
            var s = (srcBox.SelectedItem as ComboBoxItem)?.Tag as string ?? defaultSource;
            var t = (tgtBox.SelectedItem as ComboBoxItem)?.Tag as string ?? defaultTarget;
            if (string.Equals(s, t, System.StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Source and target languages are the same.", "Translate", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            result = new Result(s, t, scopeBox.IsChecked == true);
            w.DialogResult = true;
        };
        return w.ShowDialog() == true ? result : null;
    }

    private static ComboBox BuildLangCombo(string selectedCode)
    {
        var cb = new ComboBox { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var (code, name) in TranslateService.Languages)
        {
            var item = new ComboBoxItem { Content = $"{name}  ({code})", Tag = code };
            cb.Items.Add(item);
            if (string.Equals(code, selectedCode, System.StringComparison.OrdinalIgnoreCase))
                cb.SelectedItem = item;
        }
        if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
        return cb;
    }
}
