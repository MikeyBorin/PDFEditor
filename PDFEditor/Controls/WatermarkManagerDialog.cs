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
    public enum ActionKind { None, Add, Delete }

    public record Result(ActionKind Kind, WatermarkRecord? ToAdd, string? IdToDelete);

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
                    var del = new Button { Content = "Delete", Padding = new Thickness(10, 2, 10, 2) };
                    del.Click += (_, _) =>
                    {
                        result = new Result(ActionKind.Delete, null, rec.Id);
                        w.DialogResult = true;
                    };
                    Grid.SetColumn(del, 1);
                    row.Children.Add(del);
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

        // --- Add-new form --------------------------------------------------
        root.Children.Add(new TextBlock { Text = "Add a watermark", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) });

        var g = new Grid();
        for (int i = 0; i < 2; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 90 : 1, i == 0 ? GridUnitType.Pixel : GridUnitType.Star) });
        for (int i = 0; i < 5; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock lab(string t) => new() { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
        var textBox    = new TextBox { Text = "DRAFT",  Margin = new Thickness(0, 4, 0, 4) };
        var sizeBox    = new TextBox { Text = "72",     Margin = new Thickness(0, 4, 0, 4) };
        var colorBox   = new TextBox { Text = "#FF0000", Margin = new Thickness(0, 4, 0, 4) };
        var opacityBox = new TextBox { Text = "0.30",   Margin = new Thickness(0, 4, 0, 4) };
        var angleBox   = new TextBox { Text = "-30",    Margin = new Thickness(0, 4, 0, 4) };

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
        var addBtn  = new Button { Content = "Add", Width = 96, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel  = new Button { Content = "Close", Width = 96, IsCancel = true };
        buttons.Children.Add(addBtn);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

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
            result = new Result(ActionKind.Add, rec, null);
            w.DialogResult = true;
        };

        w.Content = root;
        w.ShowDialog();
        return result;
    }
}
