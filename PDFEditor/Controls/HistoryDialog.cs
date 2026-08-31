using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PDFEditor.Controls;

/// <summary>
/// Read-only view of the document's undo stack, plus a "Revert to before this"
/// action per entry (with a big warning that everything after will also be lost —
/// the undo mechanism is byte-snapshot based so surgical single-op removal isn't
/// possible).
/// </summary>
public static class HistoryDialog
{
    public record HistoryRow(int Index, string Label, DateTime When)
    {
        public string WhenText => When.ToString("HH:mm:ss");
        public string Position => Index == 0 ? "← most recent" : "";
    }

    /// <summary>Show the dialog. Returns the 0-based index the user chose to revert
    /// to (i.e., undo everything down to and INCLUDING this entry) or -1 if cancelled.</summary>
    public static int Show(System.Collections.Generic.IReadOnlyList<Services.PdfDocumentService.HistoryEntry> entries)
    {
        var w = new Window
        {
            Title = "Edit history",
            Width = 620,
            Height = 460,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current!.Resources["Bg"],
            Foreground = (Brush)Application.Current!.Resources["Text"]
        };

        var root = new DockPanel { Margin = new Thickness(14) };

        var header = new TextBlock
        {
            Text = entries.Count == 0
                ? "No edits recorded in this session yet."
                : $"{entries.Count} edit(s) recorded this session. Most-recent first.",
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var note = new TextBlock
        {
            Text = "This list shows document-level edits (watermark, page ops, headers, etc.) that " +
                   "replaced the PDF's bytes. Unsaved overlay annotations — text stamps, ticks, " +
                   "notes, ink — don't appear here because they haven't been flattened into the PDF " +
                   "yet; they're pending until File → Save. \"Revert to before this\" also discards " +
                   "every later edit in this list. History is not saved with the file — closing the " +
                   "document clears it.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var close = new Button { Content = "Close", Width = 90, IsCancel = true };
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var rows = new List<HistoryRow>();
        for (int i = 0; i < entries.Count; i++)
            rows.Add(new HistoryRow(i, entries[i].Label, entries[i].When));

        int chosenIndex = -1;

        var list = new ListView
        {
            ItemsSource = rows,
            SelectionMode = SelectionMode.Single,
            BorderBrush = (Brush)Application.Current!.Resources["Border"]
        };
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "Time", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("WhenText") });
        gv.Columns.Add(new GridViewColumn { Header = "Operation", Width = 320, DisplayMemberBinding = new System.Windows.Data.Binding("Label") });
        gv.Columns.Add(new GridViewColumn { Header = "", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("Position") });
        list.View = gv;
        root.Children.Add(list);

        var revert = new Button
        {
            Content = "Revert to before selected...",
            Padding = new Thickness(14, 4, 14, 4),
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false
        };
        buttons.Children.Insert(0, revert);

        list.SelectionChanged += (_, _) => revert.IsEnabled = list.SelectedItem is HistoryRow;

        revert.Click += (_, _) =>
        {
            if (list.SelectedItem is not HistoryRow row) return;
            var laterCount = row.Index; // number of ops that came AFTER the selected one
            var msg = laterCount == 0
                ? $"Revert to before \"{row.Label}\"?\n\nThis single edit will be undone."
                : $"Revert to before \"{row.Label}\"?\n\n" +
                  $"WARNING: this will ALSO discard the {laterCount} edit(s) made after it. " +
                  "This is unavoidable — the undo history stores full byte snapshots, so a single " +
                  "edit cannot be surgically removed from the middle of the stack.\n\n" +
                  "You can still recover by pressing Ctrl+Z once for each discarded edit if you don't " +
                  "close the document or apply a new edit first.";
            var confirm = MessageBox.Show(msg, "Revert",
                MessageBoxButton.OKCancel,
                laterCount == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
            chosenIndex = row.Index;
            w.DialogResult = true;
        };

        w.Content = root;
        w.ShowDialog();
        return chosenIndex;
    }
}
