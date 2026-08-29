using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDFEditor.Controls;

public static class SignatureCaptureDialog
{
    /// <summary>Prompts the user to draw a signature. Returns path to a transparent-background PNG, or null.</summary>
    public static string? Show()
    {
        var w = new Window
        {
            Title = "Draw Signature",
            Width = 640, Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var hint = new TextBlock
        {
            Text = "Draw your signature with the mouse or pen. Click Save when done.",
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(hint, Dock.Top);

        var ink = new InkCanvas
        {
            Background = Brushes.White,
            EditingMode = InkCanvasEditingMode.Ink,
            MinHeight = 180
        };
        ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.Black,
            Width = 3, Height = 3,
            FitToCurve = true
        };

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var clear = new Button { Content = "Clear", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Save", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(clear); btns.Children.Add(ok); btns.Children.Add(cancel);
        DockPanel.SetDock(btns, Dock.Bottom);

        root.Children.Add(hint);
        root.Children.Add(btns);
        root.Children.Add(ink);
        w.Content = root;

        clear.Click += (_, _) => ink.Strokes.Clear();

        string? result = null;
        ok.Click += (_, _) =>
        {
            if (ink.Strokes.Count == 0) return;

            var bounds = ink.Strokes.GetBounds();
            bounds.Inflate(6, 6);
            var wPx = (int)Math.Max(64, bounds.Width);
            var hPx = (int)Math.Max(32, bounds.Height);

            // Render strokes to a transparent-background bitmap.
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                foreach (var stroke in ink.Strokes) stroke.Draw(ctx);
                ctx.Pop();
            }
            var rtb = new RenderTargetBitmap(wPx, hPx, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var path = Path.Combine(Path.GetTempPath(), $"pdfeditor-sig-{Guid.NewGuid():N}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            encoder.Save(fs);
            result = path;
            w.DialogResult = true;
        };

        return w.ShowDialog() == true ? result : null;
    }
}
