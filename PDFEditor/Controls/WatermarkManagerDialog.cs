using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PDFEditor.Services;

namespace PDFEditor.Controls;

/// <summary>
/// Watermark manager. Two panes stacked in one window:
///   Top:    existing watermarks (from PDF metadata) with per-row Delete.
///   Bottom: add-a-new form (text / size / colour / opacity / angle).
/// Dialog returns exactly one intent. Caller processes it and, if more
/// actions are wanted, reopens.
/// </summary>
public static class WatermarkManagerDialog
{
    public enum ActionKind { None, Add, Delete, Edit }

    /// <summary>Dialog result. For Edit, both <see cref="OriginalIdForEdit"/> (which
    /// existing watermark is being replaced) and <see cref="ToAdd"/> (the new record)
    /// are set.</summary>
    public record Result(ActionKind Kind, WatermarkRecord? ToAdd, string? IdToDelete, string? OriginalIdForEdit = null);

    public static Result Show(IReadOnlyList<WatermarkRecord> existing, Func<WatermarkRecord, bool> canCleanRemove)
    {
        var w = new Window
        {
            Title = "Watermark",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current!.Resources["Bg"],
            Foreground = (Brush)Application.Current!.Resources["Text"]
        };

        var root = new StackPanel { Margin = new Thickness(16) };

        // --- Existing watermarks -------------------------------------------
        root.Children.Add(new TextBlock { Text = "Existing watermarks", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

        Result result = new(ActionKind.None, null, null);

        // Set below once SetEditMode exists. Row Edit buttons dispatch through this.
        Action<WatermarkRecord>? onEditClicked = null;

        if (existing.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "(none — this document has no ArtiMax-tracked watermarks yet)",
                Opacity = 0.7,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }
        else
        {
            var listBorder = new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["Border"],
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 12),
                MaxHeight = 200
            };
            var listStack = new StackPanel();
            var scroll = new ScrollViewer { Content = listStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            listBorder.Child = scroll;

            foreach (var rec in existing)
            {
                var row = new Grid { Margin = new Thickness(8, 6, 8, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = new TextBlock
                {
                    Text = $"\"{rec.Text}\"   {rec.FontSize:0}pt {rec.ColorHex}  {rec.Angle:0}°  opacity {rec.Opacity:P0}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                if (canCleanRemove(rec))
                {
                    var actions = new StackPanel { Orientation = Orientation.Horizontal };
                    var edit = new Button { Content = "Edit", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
                    var del  = new Button { Content = "Delete", Padding = new Thickness(10, 2, 10, 2) };
                    edit.Click += (_, _) => onEditClicked?.Invoke(rec);
                    del.Click += (_, _) =>
                    {
                        result = new Result(ActionKind.Delete, null, rec.Id);
                        w.DialogResult = true;
                    };
                    actions.Children.Add(edit);
                    actions.Children.Add(del);
                    Grid.SetColumn(actions, 1);
                    row.Children.Add(actions);
                }
                else
                {
                    var bakedIn = new TextBlock
                    {
                        Text = "baked in",
                        Opacity = 0.7,
                        FontStyle = FontStyles.Italic,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "This watermark can't be deleted from within the app — the pre-watermark bytes are no longer in the undo stack (either you've done other edits since, or the file was saved and reopened). To change it, start again from a clean source PDF."
                    };
                    Grid.SetColumn(bakedIn, 1);
                    row.Children.Add(bakedIn);
                }
                listStack.Children.Add(row);
            }
            root.Children.Add(listBorder);
        }

        // --- Add-new form (also reused for Edit; header + button labels swap) ---
        var formHeader = new TextBlock { Text = "Add a watermark", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) };
        root.Children.Add(formHeader);

        var g = new Grid();
        for (int i = 0; i < 2; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 90 : 1, i == 0 ? GridUnitType.Pixel : GridUnitType.Star) });
        for (int i = 0; i < 5; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock lab(string t) => new() { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
        var textBox    = new TextBox { Text = "DRAFT",  Margin = new Thickness(0, 4, 0, 4) };
        var sizeBox    = new TextBox { Text = "72",     Margin = new Thickness(0, 4, 0, 4) };
        var colorBox   = new TextBox { Text = "#FF0000", Margin = new Thickness(0, 4, 0, 4) };
        var opacityBox = new TextBox { Text = "0.30",   Margin = new Thickness(0, 4, 0, 4) };
        var angleBox   = new TextBox { Text = "-30",    Margin = new Thickness(0, 4, 0, 4) };

        string? editingId = null;   // non-null when the form is currently editing an existing record

        void Put(int row, string label, UIElement value) {
            var l = lab(label); Grid.SetRow(l, row); Grid.SetColumn(l, 0); g.Children.Add(l);
            Grid.SetRow(value, row); Grid.SetColumn(value, 1); g.Children.Add(value);
        }
        Put(0, "Text",      textBox);
        Put(1, "Size (pt)", sizeBox);
        Put(2, "Colour",    colorBox);
        Put(3, "Opacity",   opacityBox);
        Put(4, "Angle (°)", angleBox);
        root.Children.Add(g);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancelEditBtn = new Button { Content = "Cancel edit", Width = 110, Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
        var addBtn  = new Button { Content = "Add", Width = 96, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel  = new Button { Content = "Close", Width = 96, IsCancel = true };
        buttons.Children.Add(cancelEditBtn);
        buttons.Children.Add(addBtn);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        // Switch the form between "Add" mode (default) and "Edit" mode (pre-filled
        // from an existing record). editingId non-null == edit mode.
        void SetAddMode()
        {
            editingId = null;
            formHeader.Text = "Add a watermark";
            addBtn.Content = "Add";
            cancelEditBtn.Visibility = Visibility.Collapsed;
            textBox.Text = "DRAFT";
            sizeBox.Text = "72";
            colorBox.Text = "#FF0000";
            opacityBox.Text = "0.30";
            angleBox.Text = "-30";
        }
        void SetEditMode(WatermarkRecord rec)
        {
            editingId = rec.Id;
            formHeader.Text = $"Edit watermark: \"{rec.Text}\"";
            addBtn.Content = "Save changes";
            cancelEditBtn.Visibility = Visibility.Visible;
            textBox.Text = rec.Text;
            sizeBox.Text = rec.FontSize.ToString("0.###");
            colorBox.Text = rec.ColorHex;
            opacityBox.Text = rec.Opacity.ToString("0.###");
            angleBox.Text = rec.Angle.ToString("0.###");
            textBox.Focus();
            textBox.SelectAll();
        }
        cancelEditBtn.Click += (_, _) => SetAddMode();

        addBtn.Click += (_, _) =>
        {
            var text = (textBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Watermark text is required.", "Watermark", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!double.TryParse(sizeBox.Text, out var size)   || size <= 0) size = 72;
            if (!double.TryParse(opacityBox.Text, out var opa) || opa <= 0 || opa > 1) opa = 0.30;
            if (!double.TryParse(angleBox.Text, out var ang))  ang = -30;
            var col = (colorBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(col)) col = "#FF0000";

            var rec = new WatermarkRecord(
                Id: Guid.NewGuid().ToString("N"),
                Text: text,
                FontName: "Arial",
                FontSize: size,
                ColorHex: col,
                Opacity: opa,
                Angle: ang,
                AppliedUtc: DateTime.UtcNow);
            result = editingId is null
                ? new Result(ActionKind.Add,  rec, null)
                : new Result(ActionKind.Edit, rec, null, editingId);
            w.DialogResult = true;
        };

        // Now that SetEditMode exists, wire the row Edit buttons through it.
        onEditClicked = SetEditMode;

        w.Content = root;
        w.ShowDialog();
        return result;
    }
}
