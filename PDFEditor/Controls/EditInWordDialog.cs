using System.Windows;
using System.Windows.Controls;

namespace PDFEditor.Controls;

/// <summary>
/// Modal that pauses the app while the user edits the temp .docx in Word,
/// then confirms import (or discard).
/// </summary>
public static class EditInWordDialog
{
    public static bool Show(string docxPath)
    {
        var window = new Window
        {
            Title = "Edit in Word",
            Width = 460,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(16) };
        var msg = new TextBlock
        {
            Text = $"Your PDF has been opened in Microsoft Word for editing:\n\n{docxPath}\n\n" +
                   "1. Edit the document in Word.\n" +
                   "2. Save the document in Word (Ctrl+S).\n" +
                   "3. Return here and click Import Changes.\n\n" +
                   "The edited Word document will be converted back to PDF and become the current document.",
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(msg, Dock.Top);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Import Changes", Width = 130, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);

        root.Children.Add(msg);
        root.Children.Add(buttons);
        window.Content = root;

        ok.Click += (_, _) => { window.DialogResult = true; };
        return window.ShowDialog() == true;
    }
}
