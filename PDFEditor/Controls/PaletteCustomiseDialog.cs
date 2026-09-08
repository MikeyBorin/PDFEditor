using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PDFEditor.ViewModels;

namespace PDFEditor.Controls;

/// <summary>Simple checklist dialog for Tool Palette customisation. Every
/// ToolCatalog entry appears as a checkbox; ticked = visible, unticked =
/// hidden. OK writes the hidden-set to ViewSettings via MainViewModel.
/// Toolbar visibility is unaffected (curated separately via
/// Customise Toolbar…).</summary>
public static class PaletteCustomiseDialog
{
    public static bool Show(MainViewModel vm)
    {
        var w = new Window
        {
            Title = "Customise Palette",
            Width = 380,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.CanResize,
            MinWidth = 320, MinHeight = 400
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var head = new TextBlock
        {
            Text = "Tick the tools you want to see in the floating palette. " +
                   "The toolbar is unaffected (customise it separately from View → Customise Toolbar).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(head, Dock.Top);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var showAll = new Button { Content = "Show All", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "OK", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(showAll); btns.Children.Add(ok); btns.Children.Add(cancel);
        DockPanel.SetDock(btns, Dock.Bottom);

        var hidden = new HashSet<string>(vm.ViewSettings.Settings.HiddenPaletteItems, System.StringComparer.Ordinal);
        var checkboxes = new Dictionary<string, CheckBox>(System.StringComparer.Ordinal);

        var stack = new StackPanel();
        foreach (var e in ToolCatalog.All)
        {
            var cb = new CheckBox
            {
                Content = string.IsNullOrEmpty(e.Glyph) ? e.Label : $"{e.Glyph}  {e.Label}",
                IsChecked = !hidden.Contains(e.Key),
                Margin = new Thickness(0, 4, 0, 4),
                ToolTip = e.DisplayTooltip
            };
            checkboxes[e.Key] = cb;
            stack.Children.Add(cb);
        }
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        root.Children.Add(head);
        root.Children.Add(btns);
        root.Children.Add(scroll);
        w.Content = root;

        showAll.Click += (_, _) =>
        {
            foreach (var cb in checkboxes.Values) cb.IsChecked = true;
        };
        ok.Click += (_, _) =>
        {
            var newHidden = checkboxes.Where(kv => kv.Value.IsChecked != true).Select(kv => kv.Key);
            vm.SetPaletteHidden(newHidden);
            w.DialogResult = true;
        };
        return w.ShowDialog() == true;
    }
}
