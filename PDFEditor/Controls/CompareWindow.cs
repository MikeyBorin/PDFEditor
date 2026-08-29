using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFEditor.Services;

namespace PDFEditor.Controls;

public static class CompareWindow
{
    public static void Show(byte[] pdfA, string labelA, byte[] pdfB, string labelB)
    {
        var w = new Window
        {
            Title = $"Compare — {labelA} vs {labelB}",
            Width = 1200, Height = 800,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x22)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };

        var svc = new CompareService();
        var render = new PdfRenderService();
        var results = svc.ComparePages(pdfA, pdfB);

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        main.ColumnDefinitions.Add(new ColumnDefinition());
        main.ColumnDefinitions.Add(new ColumnDefinition());
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header
        var head = new TextBlock
        {
            Text = $"{results.Count(r => !r.SameText)} of {results.Count} page(s) differ.",
            Padding = new Thickness(10, 8, 10, 8),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumnSpan(head, 3);
        main.Children.Add(head);

        // Left summary list
        var summary = new ListBox { Background = new SolidColorBrush(Color.FromRgb(0x23, 0x24, 0x28)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        foreach (var r in results)
        {
            var status = r.SameText ? "same" : $"diff (+{r.LinesAdded}/-{r.LinesRemoved})";
            summary.Items.Add(new ListBoxItem
            {
                Content = $"Page {r.PageIndex + 1}: {status}",
                Foreground = r.SameText ? Brushes.LightGray : Brushes.OrangeRed,
                Tag = r.PageIndex
            });
        }
        Grid.SetRow(summary, 1); Grid.SetColumn(summary, 0);
        main.Children.Add(summary);

        // Two page viewers
        var imgA = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
        var imgB = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
        Grid.SetRow(imgA, 1); Grid.SetColumn(imgA, 1);
        Grid.SetRow(imgB, 1); Grid.SetColumn(imgB, 2);
        main.Children.Add(imgA); main.Children.Add(imgB);

        summary.SelectionChanged += (_, _) =>
        {
            if (summary.SelectedItem is ListBoxItem li && li.Tag is int idx)
            {
                imgA.Source = idx < RenderPageCount(pdfA) ? render.RenderPage(pdfA, idx, 120) : null;
                imgB.Source = idx < RenderPageCount(pdfB) ? render.RenderPage(pdfB, idx, 120) : null;
            }
        };
        if (results.Count > 0) summary.SelectedIndex = 0;

        w.Content = main;
        w.ShowDialog();
    }

    private static int RenderPageCount(byte[] pdf) => PDFtoImage.Conversion.GetPageCount(pdf);
}
